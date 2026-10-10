import { CfnOutput, Fn, Stack, type StackProps } from 'aws-cdk-lib'
import { PublicHostedZone } from 'aws-cdk-lib/aws-route53'
import type { Construct } from 'constructs'

export interface DnsStackProps extends StackProps {
  domainName: string
}

// The domain's public zone. The domain's nameservers are set at its registrar by hand to the four
// this stack outputs: the one step outside CDK (docs/deployment.md).
export class DnsStack extends Stack {
  readonly zone: PublicHostedZone

  constructor(scope: Construct, id: string, props: DnsStackProps) {
    super(scope, id, props)
    this.zone = new PublicHostedZone(this, 'Zone', { zoneName: props.domainName })
    new CfnOutput(this, 'NameServers', {
      value: Fn.join(' ', this.zone.hostedZoneNameServers!),
      description: `Set these as ${props.domainName}'s nameservers at the registrar.`,
    })
  }
}
