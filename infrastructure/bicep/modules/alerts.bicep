// infrastructure/bicep/modules/alerts.bicep
// Azure Monitor alert rules for AI feature monitoring

@description('Name prefix for alert rules')
param alertNamePrefix string

@description('Location for alert action group (global only)')
#disable-next-line no-unused-params
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
// Alert 7: Export Failures
// =====================================================
resource exportFailureAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${alertNamePrefix}-export-failures'
  location: 'global'
  tags: tags
  properties: {
    description: 'Alert when export operations are failing'
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
          name: 'ExportFailureCriteria'
          criterionType: 'DynamicThresholdCriterion'
          metricName: 'customMetrics/ai.export.requests'
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
        emailAddress: 'admin@contoso.com'
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
output exportFailureAlertId string = exportFailureAlert.id
output defaultActionGroupId string = empty(actionGroupId) ? actionGroup.id : actionGroupId
