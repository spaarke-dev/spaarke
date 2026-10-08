// infrastructure/bicep/notification-playbook-alerts.bicep
// Azure Monitor alert: a notification playbook failed for EVERY user (ISS-018, #1452, owner decision D-78).
//
// Why: all 7 notification playbooks in spaarkedev1 failed for every user for 89+ days and nobody was told — the
// scheduler logged Warnings and reported success. PlaybookSchedulerJob now logs an Error
// ("Notification playbook {PlaybookId} ({Name}) failed for every user — …", EventId 46101
// NotificationPlaybookTotalFailure), marks the run Failed and does not advance sprk_lastrundate. This rule turns that
// Error into a page through the EXISTING alerting channel: an App Insights scheduled-query rule routed to the
// environment's existing on-call action group — the same mechanism and module shape as infrastructure/bicep/alerts.bicep
// (Redis). No new channel.
//
// The query keys on the logger category and the stable phrase "failed for every user" (PlaybookSchedulerJob keeps it
// stable; see TotalFailureEventId). While the failure persists the scheduler retries every hourly tick, so the rule
// fires each tick until a run succeeds; autoMitigate resolves it then.
//
// Deploy (an Azure change — owner approval per environment):
//   az deployment group create -g spe-infrastructure-westus2 \
//     --template-file infrastructure/bicep/notification-playbook-alerts.bicep \
//     --parameters appInsightsName=spe-insights-dev-67e2xz environment=dev \
//                  actionGroupResourceId=/subscriptions/<sub>/resourceGroups/rg-spaarke-dev/providers/microsoft.insights/actionGroups/ag-spaarke-oncall-dev

@description('Application Insights resource name the BFF writes traces to (dev: spe-insights-dev-67e2xz).')
param appInsightsName string

@description('Action group resource ID for on-call routing (dev: ag-spaarke-oncall-dev).')
param actionGroupResourceId string

@description('Location for the scheduled-query rule. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Environment tag; flows into the rule name and tags.')
@allowed(['dev', 'demo', 'staging', 'prod'])
param environment string = 'dev'

@description('Severity (0-4). Default 1 = Error: users receive no notifications of that type at all.')
@allowed([0, 1, 2, 3, 4])
param alertSeverity int = 1

@description('Tags propagated to the alert resource.')
param tags object = {
  environment: environment
  feature: 'notification-playbooks'
  issue: 'ISS-018'
}

var appInsightsResourceId = resourceId('Microsoft.Insights/components', appInsightsName)

var totalFailureKql = '''
traces
| where severityLevel >= 3
| where tostring(customDimensions.CategoryName) == "Sprk.Bff.Api.Services.Ai.PlaybookSchedulerJob"
| where message has "failed for every user"
| summarize failures = count() by playbookId = tostring(customDimensions.PlaybookId), playbook = tostring(customDimensions.Name)
'''

resource totalFailureAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: 'notification-playbook-total-failure-${environment}'
  location: location
  tags: tags
  properties: {
    description: 'A notification playbook failed for every user (ISS-018 / D-78): users get no notifications of that type. The scheduler retries each hourly tick and does not advance sprk_lastrundate. Read the per-user "failed for user" traces with the same childCorrelationId for the failing node.'
    severity: alertSeverity
    enabled: true
    evaluationFrequency: 'PT15M'
    windowSize: 'PT1H'
    scopes: [
      appInsightsResourceId
    ]
    criteria: {
      allOf: [
        {
          query: totalFailureKql
          timeAggregation: 'Count'
          dimensions: [
            {
              name: 'playbook'
              operator: 'Include'
              values: [
                '*'
              ]
            }
          ]
          operator: 'GreaterThan'
          threshold: 0
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: [
        actionGroupResourceId
      ]
    }
    autoMitigate: true
  }
}

output totalFailureAlertId string = totalFailureAlert.id
