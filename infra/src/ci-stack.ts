import { CfnOutput, Duration, Stack, type StackProps } from 'aws-cdk-lib'
import { OidcProviderNative, PolicyStatement, Role, WebIdentityPrincipal, type IOidcProvider } from 'aws-cdk-lib/aws-iam'
import type { Construct } from 'constructs'

// Fixed names, so the site stack can grant the deploy role by name without depending on this stack,
// and GitHub's repository variables can hold the ARNs.
export const DIFF_ROLE_NAME = 'altrobe-github-diff'
export const DEPLOY_ROLE_NAME = 'altrobe-github-deploy'

const GITHUB_ISSUER = 'token.actions.githubusercontent.com'
// The bootstrap stack's default qualifier (cdk bootstrap, no --qualifier).
const QUALIFIER = 'hnb659fds'

export interface CiStackProps extends StackProps {
  /** GitHub repository, owner/name. */
  repo: string
  /**
   * An existing GitHub OIDC provider's ARN. An account has at most one per issuer URL, so if another
   * stack already made one, pass it (context `githubOidcProviderArn`) instead of creating a second.
   */
  oidcProviderArn?: string
}

// GitHub Actions' way into AWS, with no stored keys: an OIDC provider and two roles.
// - diff: any workflow in the repository, pull requests included; may only read (CDK lookup role).
// - deploy: only jobs in the repository's `production` environment, which needs the maintainer's
//   approval; may assume the CDK bootstrap roles. The site stack grants it the bundle bucket and
//   invalidations by name.
export class CiStack extends Stack {
  constructor(scope: Construct, id: string, props: CiStackProps) {
    super(scope, id, props)
    const provider: IOidcProvider = props.oidcProviderArn
      ? OidcProviderNative.fromOidcProviderArn(this, 'GitHubOidc', props.oidcProviderArn)
      : new OidcProviderNative(this, 'GitHubOidc', { url: `https://${GITHUB_ISSUER}`, clientIds: ['sts.amazonaws.com'] })

    const trust = (sub: { StringEquals?: Record<string, string>; StringLike?: Record<string, string> }) =>
      new WebIdentityPrincipal(provider.oidcProviderArn, {
        StringEquals: { [`${GITHUB_ISSUER}:aud`]: 'sts.amazonaws.com', ...sub.StringEquals },
        ...(sub.StringLike ? { StringLike: sub.StringLike } : {}),
      })
    // The account and region come from the stack's environment (CDK_DEFAULT_ACCOUNT at deploy time).
    const bootstrapRole = (kind: string) => `arn:${this.partition}:iam::${this.account}:role/cdk-${QUALIFIER}-${kind}-${this.account}-${this.region}`

    const diff = new Role(this, 'DiffRole', {
      roleName: DIFF_ROLE_NAME,
      assumedBy: trust({ StringLike: { [`${GITHUB_ISSUER}:sub`]: `repo:${props.repo}:*` } }),
      maxSessionDuration: Duration.hours(1),
      description: 'GitHub Actions: cdk diff (read only), for pull requests and before each deploy.',
    })
    diff.addToPolicy(new PolicyStatement({ actions: ['sts:AssumeRole'], resources: [bootstrapRole('lookup-role')] }))

    const deploy = new Role(this, 'DeployRole', {
      roleName: DEPLOY_ROLE_NAME,
      assumedBy: trust({ StringEquals: { [`${GITHUB_ISSUER}:sub`]: `repo:${props.repo}:environment:production` } }),
      maxSessionDuration: Duration.hours(2),
      description: 'GitHub Actions: cdk deploy and bundle publishing, from the production environment only.',
    })
    deploy.addToPolicy(new PolicyStatement({
      actions: ['sts:AssumeRole'],
      resources: ['deploy-role', 'file-publishing-role', 'image-publishing-role', 'lookup-role'].map(bootstrapRole),
    }))

    new CfnOutput(this, 'DiffRoleArn', { value: diff.roleArn, description: 'GitHub repository variable AWS_DIFF_ROLE_ARN' })
    new CfnOutput(this, 'DeployRoleArn', { value: deploy.roleArn, description: 'GitHub repository variable AWS_DEPLOY_ROLE_ARN' })
  }
}
