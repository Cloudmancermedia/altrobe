import { mkdtempSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { App } from 'aws-cdk-lib'
import { Match, Template } from 'aws-cdk-lib/assertions'
import { describe, expect, test } from 'vitest'
import { CiStack, DEPLOY_ROLE_NAME, DIFF_ROLE_NAME } from '../src/ci-stack.js'
import { DnsStack } from '../src/dns-stack.js'
import { BudgetStack } from '../src/budget-stack.js'
import { SiteStack } from '../src/site-stack.js'

const env = { account: '123456789012', region: 'us-east-1' }
const repo = 'Cloudmancermedia/altrobe'

function stacks(opts: { oidcProviderArn?: string; webDistPath?: string } = {}) {
  const app = new App()
  const dns = new DnsStack(app, 'AltrobeDns', { env, domainName: 'altrobe.com' })
  const ci = new CiStack(app, 'AltrobeCi', { env, repo, oidcProviderArn: opts.oidcProviderArn })
  const site = new SiteStack(app, 'AltrobeSite', { env, zone: dns.zone, domainName: 'altrobe.com', deployRoleName: DEPLOY_ROLE_NAME, webDistPath: opts.webDistPath })
  return { dns: Template.fromStack(dns), ci: Template.fromStack(ci), site: Template.fromStack(site), siteStack: site }
}

function webDist() {
  const dir = mkdtempSync(join(tmpdir(), 'altrobe-web-'))
  writeFileSync(join(dir, 'index.html'), '<!doctype html><title>Altrobe</title>')
  return dir
}

describe('AltrobeDns', () => {
  test('creates the altrobe.com zone and outputs its nameservers for the registrar', () => {
    const { dns } = stacks()
    dns.hasResourceProperties('AWS::Route53::HostedZone', { Name: 'altrobe.com.' })
    expect(Object.keys(dns.findOutputs('*', { Value: Match.objectLike({ 'Fn::Join': Match.anyValue() }) }))).not.toHaveLength(0)
  })
})

describe('AltrobeCi', () => {
  test('creates the GitHub OIDC provider unless an existing one is passed in', () => {
    stacks().ci.hasResourceProperties('AWS::IAM::OIDCProvider', {
      Url: 'https://token.actions.githubusercontent.com', ClientIdList: ['sts.amazonaws.com'],
    })
    stacks({ oidcProviderArn: 'arn:aws:iam::123456789012:oidc-provider/token.actions.githubusercontent.com' })
      .ci.resourceCountIs('AWS::IAM::OIDCProvider', 0)
  })

  test('the deploy role trusts only this repository in the production environment', () => {
    stacks().ci.hasResourceProperties('AWS::IAM::Role', {
      RoleName: DEPLOY_ROLE_NAME,
      AssumeRolePolicyDocument: Match.objectLike({
        Statement: [Match.objectLike({
          Action: 'sts:AssumeRoleWithWebIdentity',
          Condition: {
            StringEquals: {
              'token.actions.githubusercontent.com:aud': 'sts.amazonaws.com',
              'token.actions.githubusercontent.com:sub': `repo:${repo}:environment:production`,
            },
          },
        })],
      }),
    })
  })

  test('the diff role trusts this repository only and may assume only the CDK lookup role', () => {
    const { ci } = stacks()
    ci.hasResourceProperties('AWS::IAM::Role', {
      RoleName: DIFF_ROLE_NAME,
      AssumeRolePolicyDocument: Match.objectLike({
        Statement: [Match.objectLike({
          Condition: {
            StringEquals: { 'token.actions.githubusercontent.com:aud': 'sts.amazonaws.com' },
            StringLike: { 'token.actions.githubusercontent.com:sub': `repo:${repo}:*` },
          },
        })],
      }),
    })
    const policies = ci.findResources('AWS::IAM::Policy')
    const diff = Object.values(policies).find((p) => JSON.stringify(p).includes(DIFF_ROLE_NAME) || JSON.stringify(p.Properties.Roles).includes('DiffRole'))
    const text = JSON.stringify(diff)
    expect(text).toContain('cdk-hnb659fds-lookup-role')
    expect(text).not.toContain('cdk-hnb659fds-deploy-role')
  })

  test('trust keys only on the repository name in sub, plus aud: no numeric repository or owner IDs', () => {
    // The repository may be deleted and recreated, which changes its numeric IDs but not its name.
    const roles = Object.values(stacks().ci.findResources('AWS::IAM::Role'))
    expect(roles).toHaveLength(2)
    for (const r of roles) {
      const text = JSON.stringify(r.Properties.AssumeRolePolicyDocument)
      expect(text).not.toMatch(/repository_id|repository_owner_id|repository_owner|actor_id/)
      const keys = r.Properties.AssumeRolePolicyDocument.Statement.flatMap((s: { Condition: Record<string, Record<string, string>> }) =>
        Object.values(s.Condition).flatMap((c) => Object.keys(c)))
      expect(keys.sort()).toEqual(['token.actions.githubusercontent.com:aud', 'token.actions.githubusercontent.com:sub'])
    }
  })

  test('the deploy role may assume the CDK bootstrap roles that deploy and publish assets', () => {
    const text = JSON.stringify(stacks().ci.findResources('AWS::IAM::Policy'))
    for (const r of ['deploy-role', 'file-publishing-role', 'image-publishing-role', 'lookup-role']) expect(text).toContain(`cdk-hnb659fds-${r}-123456789012-us-east-1`)
  })
})

describe('AltrobeSite', () => {
  test('both buckets block every kind of public access and are encrypted', () => {
    const { site } = stacks()
    const buckets = site.findResources('AWS::S3::Bucket')
    expect(Object.keys(buckets)).toHaveLength(2)
    for (const b of Object.values(buckets)) {
      expect(b.Properties.PublicAccessBlockConfiguration).toEqual({
        BlockPublicAcls: true, BlockPublicPolicy: true, IgnorePublicAcls: true, RestrictPublicBuckets: true,
      })
      expect(b.Properties.BucketEncryption).toBeDefined()
    }
  })

  test('CloudFront reads the buckets through origin access control, and only CloudFront may read them', () => {
    const { site } = stacks()
    site.resourceCountIs('AWS::CloudFront::OriginAccessControl', 2)
    for (const p of Object.values(site.findResources('AWS::S3::BucketPolicy'))) {
      const allows = p.Properties.PolicyDocument.Statement.filter((s: { Effect: string }) => s.Effect === 'Allow')
      for (const s of allows) expect(s.Principal).toEqual({ Service: 'cloudfront.amazonaws.com' })
    }
  })

  test('both distributions are HTTPS only, compressed, and serve their own hostnames on one certificate', () => {
    const { site } = stacks()
    site.hasResourceProperties('AWS::CertificateManager::Certificate', {
      DomainName: 'altrobe.com', SubjectAlternativeNames: Match.arrayWith(['www.altrobe.com', 'assets.altrobe.com']), ValidationMethod: 'DNS',
    })
    const dists = Object.values(site.findResources('AWS::CloudFront::Distribution'))
    expect(dists.map((d) => d.Properties.DistributionConfig.Aliases).sort()).toEqual([['altrobe.com', 'www.altrobe.com'], ['assets.altrobe.com']])
    for (const d of dists) {
      const behaviors = [d.Properties.DistributionConfig.DefaultCacheBehavior, ...(d.Properties.DistributionConfig.CacheBehaviors ?? [])]
      for (const b of behaviors) {
        expect(b.ViewerProtocolPolicy).toBe('redirect-to-https')
        expect(b.Compress).toBe(true)
      }
    }
  })

  test('the app sends www to the apex and falls back to index.html for app routes', () => {
    const { site } = stacks()
    site.hasResourceProperties('AWS::CloudFront::Function', {
      FunctionCode: Match.stringLikeRegexp('www\\.altrobe\\.com'),
    })
    site.hasResourceProperties('AWS::CloudFront::Distribution', {
      DistributionConfig: Match.objectLike({
        Aliases: ['altrobe.com', 'www.altrobe.com'],
        CustomErrorResponses: Match.arrayWith([Match.objectLike({ ErrorCode: 404, ResponseCode: 200, ResponsePagePath: '/index.html' })]),
      }),
    })
  })

  test('assets allow only https://altrobe.com cross-origin, cache builds for a year and current.json for a minute', () => {
    const { site } = stacks()
    site.hasResourceProperties('AWS::CloudFront::ResponseHeadersPolicy', {
      ResponseHeadersPolicyConfig: Match.objectLike({
        CorsConfig: Match.objectLike({ AccessControlAllowOrigins: { Items: ['https://altrobe.com'] } }),
      }),
    })
    site.hasResourceProperties('AWS::CloudFront::CachePolicy', { CachePolicyConfig: Match.objectLike({ DefaultTTL: 31536000 }) })
    site.hasResourceProperties('AWS::CloudFront::CachePolicy', { CachePolicyConfig: Match.objectLike({ DefaultTTL: 60, MaxTTL: 60 }) })
    site.hasResourceProperties('AWS::CloudFront::Distribution', {
      DistributionConfig: Match.objectLike({
        Aliases: ['assets.altrobe.com'],
        CacheBehaviors: [Match.objectLike({ PathPattern: '*/current.json' })],
      }),
    })
  })

  test('apex, www and assets point at CloudFront over IPv4 and IPv6', () => {
    const { site } = stacks()
    for (const type of ['A', 'AAAA'])
      for (const name of ['altrobe.com.', 'www.altrobe.com.', 'assets.altrobe.com.'])
        site.hasResourceProperties('AWS::Route53::RecordSet', { Name: name, Type: type, AliasTarget: Match.anyValue() })
  })

  test('the deploy role may write the bundle bucket and invalidate both distributions', () => {
    const text = JSON.stringify(stacks().site.findResources('AWS::IAM::Policy'))
    expect(text).toContain(DEPLOY_ROLE_NAME)
    expect(text).toContain('s3:PutObject')
    expect(text).toContain('cloudfront:CreateInvalidation')
  })

  test('the web build deploys only when it exists; bundle data never goes through CDK', () => {
    expect(Object.keys(stacks().site.findResources('Custom::CDKBucketDeployment'))).toHaveLength(0)
    const { site } = stacks({ webDistPath: webDist() })
    expect(Object.keys(site.findResources('Custom::CDKBucketDeployment'))).toHaveLength(1)
  })
})

describe('AltrobeBudget', () => {
  const budget = () => Template.fromStack(new BudgetStack(new App(), 'AltrobeBudget', {
    env, email: 'owner@example.com', monthlyLimitUsd: 50, tag: { key: 'project', value: 'altrobe' }, runId: 'run-1',
  }))

  test('alerts by email on Altrobe-tagged spend: 80% actual and 100% forecast of $50 a month', () => {
    budget().hasResourceProperties('AWS::Budgets::Budget', {
      Budget: {
        BudgetType: 'COST', TimeUnit: 'MONTHLY', BudgetLimit: { Amount: 50, Unit: 'USD' },
        FilterExpression: { Tags: { Key: 'project', Values: ['altrobe'], MatchOptions: ['EQUALS'] } },
      },
      NotificationsWithSubscribers: [
        { Notification: { NotificationType: 'ACTUAL', ComparisonOperator: 'GREATER_THAN', Threshold: 80, ThresholdType: 'PERCENTAGE' },
          Subscribers: [{ SubscriptionType: 'EMAIL', Address: 'owner@example.com' }] },
        { Notification: { NotificationType: 'FORECASTED', ComparisonOperator: 'GREATER_THAN', Threshold: 100, ThresholdType: 'PERCENTAGE' },
          Subscribers: [{ SubscriptionType: 'EMAIL', Address: 'owner@example.com' }] },
      ],
    })
  })

  test('activates the tag for cost allocation on every deploy, with only that permission', () => {
    const t = budget()
    const [cr] = Object.values(t.findResources('Custom::AWS'))
    const create = JSON.stringify(cr.Properties.Create)
    expect(create).toContain('updateCostAllocationTagsStatus')
    expect(create).toContain('"TagKey\\":\\"project\\",\\"Status\\":\\"Active\\"')
    expect(JSON.stringify(cr.Properties.Update)).toContain('run-1')
    t.hasResourceProperties('AWS::IAM::Policy', {
      PolicyDocument: { Statement: [{ Action: 'ce:UpdateCostAllocationTagsStatus', Effect: 'Allow', Resource: '*' }] },
    })
  })
})
