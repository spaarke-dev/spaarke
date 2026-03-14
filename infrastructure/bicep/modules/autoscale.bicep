// infrastructure/bicep/modules/autoscale.bicep
// Autoscale settings for App Service Plan
// Scales on CPU, memory, and HTTP queue length metrics

@description('Name of the autoscale setting')
param autoscaleSettingName string

@description('Resource ID of the App Service Plan to scale')
param appServicePlanId string

@description('Location for the autoscale setting')
param location string = resourceGroup().location

@description('Minimum instance count')
@minValue(1)
@maxValue(20)
param minInstanceCount int = 2

@description('Maximum instance count')
@minValue(1)
@maxValue(20)
param maxInstanceCount int = 10

@description('Default instance count (used when metrics unavailable)')
@minValue(1)
@maxValue(20)
param defaultInstanceCount int = 2

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// AUTOSCALE SETTING
// ============================================================================

resource autoscaleSetting 'Microsoft.Insights/autoscalesettings@2022-10-01' = {
  name: autoscaleSettingName
  location: location
  tags: tags
  properties: {
    enabled: true
    targetResourceUri: appServicePlanId
    profiles: [
      {
        name: 'DefaultAutoscaleProfile'
        capacity: {
          minimum: string(minInstanceCount)
          maximum: string(maxInstanceCount)
          default: string(defaultInstanceCount)
        }
        rules: [
          // --------------------------------------------------------------------
          // CPU: Scale OUT when > 70% for 5 minutes
          // --------------------------------------------------------------------
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appServicePlanId
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 70
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          // --------------------------------------------------------------------
          // CPU: Scale IN when < 30% for 10 minutes
          // --------------------------------------------------------------------
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appServicePlanId
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT10M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: 30
            }
            scaleAction: {
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT10M'
            }
          }
          // --------------------------------------------------------------------
          // Memory: Scale OUT when > 80% for 5 minutes
          // --------------------------------------------------------------------
          {
            metricTrigger: {
              metricName: 'MemoryPercentage'
              metricResourceUri: appServicePlanId
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT5M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 80
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT5M'
            }
          }
          // --------------------------------------------------------------------
          // Memory: Scale IN when < 40% for 10 minutes
          // --------------------------------------------------------------------
          {
            metricTrigger: {
              metricName: 'MemoryPercentage'
              metricResourceUri: appServicePlanId
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT10M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: 40
            }
            scaleAction: {
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT10M'
            }
          }
          // --------------------------------------------------------------------
          // HTTP Queue: Scale OUT when > 100 for 2 minutes
          // --------------------------------------------------------------------
          {
            metricTrigger: {
              metricName: 'HttpQueueLength'
              metricResourceUri: appServicePlanId
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT2M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: 100
            }
            scaleAction: {
              direction: 'Increase'
              type: 'ChangeCount'
              value: '2'
              cooldown: 'PT5M'
            }
          }
          // --------------------------------------------------------------------
          // HTTP Queue: Scale IN when < 20 for 10 minutes
          // --------------------------------------------------------------------
          {
            metricTrigger: {
              metricName: 'HttpQueueLength'
              metricResourceUri: appServicePlanId
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT10M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: 20
            }
            scaleAction: {
              direction: 'Decrease'
              type: 'ChangeCount'
              value: '1'
              cooldown: 'PT10M'
            }
          }
        ]
      }
    ]
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output autoscaleSettingId string = autoscaleSetting.id
output autoscaleSettingName string = autoscaleSetting.name
