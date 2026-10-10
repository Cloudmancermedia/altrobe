import { Stack, type StackProps } from 'aws-cdk-lib'
import { CfnBudget } from 'aws-cdk-lib/aws-budgets'
import { PolicyStatement } from 'aws-cdk-lib/aws-iam'
import { AwsCustomResource, AwsCustomResourcePolicy, PhysicalResourceId } from 'aws-cdk-lib/custom-resources'
import type { Construct } from 'constructs'

export interface BudgetStackProps extends StackProps {
  /** Where the alerts go. Comes from the deploy environment, never the repository. */
  email: string
  monthlyLimitUsd: number
  /** The tag every Altrobe resource carries (app.ts); the budget counts only spend with it. */
  tag: { key: string; value: string }
  /** Changes on every deploy so the tag activation runs again (see below). */
  runId: string
}

// A monthly cost budget for Altrobe's own resources, so other spend in the account doesn't count.
export class BudgetStack extends Stack {
  constructor(scope: Construct, id: string, props: BudgetStackProps) {
    super(scope, id, props)
    const { email, tag } = props
    const alert = (notificationType: string, threshold: number): CfnBudget.NotificationWithSubscribersProperty => ({
      notification: { notificationType, comparisonOperator: 'GREATER_THAN', threshold, thresholdType: 'PERCENTAGE' },
      subscribers: [{ subscriptionType: 'EMAIL', address: email }],
    })

    new CfnBudget(this, 'Budget', {
      budget: {
        budgetName: 'altrobe-monthly',
        budgetType: 'COST',
        timeUnit: 'MONTHLY',
        budgetLimit: { amount: props.monthlyLimitUsd, unit: 'USD' },
        filterExpression: { tags: { key: tag.key, values: [tag.value], matchOptions: ['EQUALS'] } },
      },
      notificationsWithSubscribers: [alert('ACTUAL', 80), alert('FORECASTED', 100)],
    })

    // A budget sees a tag only once it is activated for cost allocation, and AWS lists a new tag key
    // for activation up to 24 hours after resources first carry it. Activating an active tag does
    // nothing and an unknown one is reported, not thrown, so this runs on every deploy and takes
    // effect on the first deploy after AWS has seen the tag.
    const call = {
      service: 'CostExplorer',
      action: 'updateCostAllocationTagsStatus',
      parameters: { CostAllocationTagsStatus: [{ TagKey: tag.key, Status: 'Active' }] },
      region: 'us-east-1',
      physicalResourceId: PhysicalResourceId.of(`cost-tag-${tag.key}-${props.runId}`),
    }
    new AwsCustomResource(this, 'ActivateCostTag', {
      onCreate: call,
      onUpdate: call,
      policy: AwsCustomResourcePolicy.fromStatements([
        new PolicyStatement({ actions: ['ce:UpdateCostAllocationTagsStatus'], resources: ['*'] }),
      ]),
      installLatestAwsSdk: false,
    })
  }
}
