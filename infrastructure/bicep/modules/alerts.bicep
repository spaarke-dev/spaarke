// infrastructure/bicep/modules/alerts.bicep
// Azure Monitor alert rules for AI feature monitoring

@description('Name prefix for alert rules')
param alertNamePrefix string

@description('Location for the scheduled-query rules (alerts 7 and 8). Metric alerts and the action group are global.')
param location string = resourceGroup().location

@description('Application Insights resource ID')
param appInsightsId string

@description('Action group ID for alert notifications')
param actionGroupId string

@description('Tags for the resources')
param tags object = {}

@description('Severity for critical alerts (0-4)')
@allowed([0, 1, 2, 3, 4])
param criticalSeverity int = 1

@description('Severity for warning alerts (0-4)')
@allowed([0, 1, 2, 3, 4])
param warningSeverity int = 2

// =====================================================
// Alert 1: AI Request Failure Rate > 10%
// =====================================================
resource aiFailureRateAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-ai-failure-rate'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when AI request failure rate exceeds 10%'
    severity: criticalSeverity
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'FailureRateCriteria'
          criterionType: 'DynamicThresholdCriterion'
          metricName: 'customMetrics/ai.summarize.failures'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          alertSensitivity: 'Medium'
          failingPeriods: {
            numberOfEvaluationPeriods: 4
            minFailingPeriodsToAlert: 3
          }
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
  }
}

// =====================================================
// Alert 2: Circuit Breaker Open
// =====================================================
resource circuitBreakerOpenAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-circuit-breaker-open'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when any circuit breaker is open (service degraded)'
    severity: criticalSeverity
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'CircuitOpenCriteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'customMetrics/circuit_breaker.open_count'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 0
          timeAggregation: 'Maximum'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
  }
}

// =====================================================
// Alert 3: High RAG Latency (P95 > 3000ms)
// =====================================================
resource ragLatencyAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-rag-high-latency'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when RAG search latency is abnormally high'
    severity: warningSeverity
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'HighLatencyCriteria'
          criterionType: 'StaticThresholdCriterion'
          metricName: 'customMetrics/ai.rag.duration'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          threshold: 3000 // 3 seconds
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
  }
}

// =====================================================
// Alert 4: Tool Execution High Failure Rate
// =====================================================
resource toolFailureAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-tool-failures'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when tool execution failures spike'
    severity: warningSeverity
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'ToolFailureCriteria'
          criterionType: 'DynamicThresholdCriterion'
          metricName: 'customMetrics/ai.tool.requests'
          metricNamespace: 'microsoft.insights/components'
          operator: 'LessThan'
          alertSensitivity: 'High'
          failingPeriods: {
            numberOfEvaluationPeriods: 4
            minFailingPeriodsToAlert: 3
          }
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
  }
}

// =====================================================
// Alert 5: Cache Miss Rate Spike
// =====================================================
resource cacheMissAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-cache-miss-spike'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when cache miss rate spikes (potential performance impact)'
    severity: warningSeverity
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'CacheMissCriteria'
          criterionType: 'DynamicThresholdCriterion'
          metricName: 'customMetrics/cache.misses'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          alertSensitivity: 'Medium'
          failingPeriods: {
            numberOfEvaluationPeriods: 4
            minFailingPeriodsToAlert: 3
          }
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
  }
}

// =====================================================
// Alert 6: High Token Usage (Cost Alert)
// =====================================================
resource tokenUsageAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-high-token-usage'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when token usage is abnormally high (cost impact)'
    severity: warningSeverity
    enabled: true
    scopes: [
      appInsightsId
    ]
    evaluationFrequency: 'PT1H'
    windowSize: 'PT6H'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.MultipleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'TokenUsageCriteria'
          criterionType: 'DynamicThresholdCriterion'
          metricName: 'customMetrics/ai.summarize.tokens'
          metricNamespace: 'microsoft.insights/components'
          operator: 'GreaterThan'
          alertSensitivity: 'Low'
          failingPeriods: {
            numberOfEvaluationPeriods: 4
            minFailingPeriodsToAlert: 2
          }
          timeAggregation: 'Total'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroupId
      }
    ]
    autoMitigate: true
  }
}

// =====================================================
// Alert 7: Ontology Signal writer failures (silent-failure guard)
// =====================================================
// spaarke-ontology-platform-r1 task 035, owner directive 2026-10-04 (deploy approved D-138): the writer fails closed by design, so a broken
// credential, a refused privilege or a sentence-template refusal would otherwise look exactly like "no conditions found".
// UNFILTERED on purpose: any ontology.writer.failures{reason} increment pages; by-design skips are counted on
// ontology.writer.skips, never here (OntologyWriterTelemetry).
// A scheduled-query rule, not a metric alert: the counter only exists in customMetrics after its first increment, and a
// metric alert cannot be created against a series that has never been emitted.
var ontologyWriterFailuresKql = '''
customMetrics
| where name == "ontology.writer.failures"
| summarize failures = sum(valueSum)
| where failures > 0
'''

resource ontologyWriterFailuresAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${alertNamePrefix}-ontology-writer-failures'
  location: location
  tags: tags
  properties: {
    description: 'Ontology Signal/Decision Record writer recorded at least one failure in 15 minutes (ontology.writer.failures > 0, unfiltered). The writer fails closed, so this is the only signal of a broken credential or refused write. Break down by customDimensions.reason.'
    severity: criticalSeverity
    enabled: true
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    scopes: [
      appInsightsId
    ]
    criteria: {
      allOf: [
        {
          query: ontologyWriterFailuresKql
          timeAggregation: 'Count'
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
        actionGroupId
      ]
    }
    autoMitigate: true
  }
}

// =====================================================
// Alert 8: Ontology evaluator has not succeeded recently (dead-job guard)
// =====================================================
// The signal-reevaluation job runs nightly (SignalReevaluationJob.DefaultCronSchedule "30 2 * * *" = 02:30 UTC), so a
// healthy day has exactly one ontology.evaluator.runs{outcome=succeeded}. Window = 24 h: task 035 asked for 26 h (nightly
// cadence plus margin) but a scheduled-query rule only accepts 5-720 min, 1440 min or 2880 min windows, and 48 h would let one
// whole missed night go unreported. Hourly evaluation; a run more than a few minutes late can fire it for an hour, and
// autoMitigate clears it. Absence cannot be expressed as a metric alert (no data never fires one), hence a scheduled-query rule:
// summarize without a by-clause returns one row (0) on an empty set, which is what makes the "no runs" case fire.
// Expected on first deploy: fires until the first nightly run succeeds after the BFF carrying the evaluator is live.
var ontologyEvaluatorStalledKql = '''
customMetrics
| where name == "ontology.evaluator.runs"
| where tostring(customDimensions.outcome) == "succeeded"
| summarize succeededRuns = countif(valueSum > 0)
| where succeededRuns == 0
'''

resource ontologyEvaluatorStalledAlert 'Microsoft.Insights/scheduledQueryRules@2023-03-15-preview' = {
  name: '${alertNamePrefix}-ontology-evaluator-stalled'
  location: location
  tags: tags
  properties: {
    description: 'No successful ontology.evaluator.runs (outcome=succeeded) in 24 hours. The nightly signal-reevaluation job is dead, failing every attempt, or the BFF is not exporting metrics. Check GET /api/admin/jobs/signal-reevaluation/status.'
    severity: criticalSeverity
    enabled: true
    evaluationFrequency: 'PT1H'
    windowSize: 'P1D'
    scopes: [
      appInsightsId
    ]
    criteria: {
      allOf: [
        {
          query: ontologyEvaluatorStalledKql
          timeAggregation: 'Count'
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
        actionGroupId
      ]
    }
    autoMitigate: true
  }
}

// =====================================================
// Action Group (if not provided externally)
// =====================================================
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = if (empty(actionGroupId)) {
  name: '${alertNamePrefix}-action-group'
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'SDAP-Alerts'
    enabled: true
    emailReceivers: [
      {
        name: 'Admin Email'
        emailAddress: 'admin-dev@spaarke.com'
        useCommonAlertSchema: true
      }
    ]
  }
}

// Outputs
output aiFailureRateAlertId string = aiFailureRateAlert.id
output circuitBreakerOpenAlertId string = circuitBreakerOpenAlert.id
output ragLatencyAlertId string = ragLatencyAlert.id
output toolFailureAlertId string = toolFailureAlert.id
output cacheMissAlertId string = cacheMissAlert.id
output tokenUsageAlertId string = tokenUsageAlert.id
output ontologyWriterFailuresAlertId string = ontologyWriterFailuresAlert.id
output ontologyEvaluatorStalledAlertId string = ontologyEvaluatorStalledAlert.id
output defaultActionGroupId string = empty(actionGroupId) ? actionGroup.id : actionGroupId
