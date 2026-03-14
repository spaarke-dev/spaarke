// infrastructure/bicep/modules/deployment-slot.bicep
// Staging deployment slot for zero-downtime deployments
// Constraint: Auto-swap disabled — require manual approval or CI/CD gate
//
// Deployment flow:
//   1. Deploy code to staging slot
//   2. Azure hits warm-up endpoints (applicationInitialization)
//   3. Health check passes on /healthz
//   4. CI/CD gate or manual approval triggers swap
//   5. Swap promotes staging → production atomically

@description('Name of the parent App Service')
param appServiceName string

@description('Location for the deployment slot')
param location string = resourceGroup().location

@description('Slot name')
param slotName string = 'staging'

@description('Runtime stack')
param runtimeStack string = 'DOTNETCORE|8.0'

@description('Health check path for the staging slot')
param healthCheckPath string = '/healthz'

@description('App settings for the staging slot (merged with swap warm-up settings)')
param appSettings object = {}

@description('Slot-sticky setting names (not swapped to production)')
param slotSettingNames array = [
  'ASPNETCORE_ENVIRONMENT'
  'APPLICATIONINSIGHTS_CONNECTION_STRING'
  'ApplicationInsightsAgent_EXTENSION_VERSION'
  'WEBSITE_SWAP_WARMUP_PING_PATH'
  'WEBSITE_SWAP_WARMUP_PING_STATUSES'
]

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// PARENT APP SERVICE (must already exist)
// ============================================================================

resource appService 'Microsoft.Web/sites@2023-01-01' existing = {
  name: appServiceName
}

// ============================================================================
// STAGING DEPLOYMENT SLOT
// ============================================================================

// Merge caller-provided app settings with swap warm-up settings
var swapWarmUpSettings = {
  WEBSITE_SWAP_WARMUP_PING_PATH: healthCheckPath
  WEBSITE_SWAP_WARMUP_PING_STATUSES: '200'
}
var mergedSettings = union(appSettings, swapWarmUpSettings)

resource stagingSlot 'Microsoft.Web/sites/slots@2023-01-01' = {
  parent: appService
  name: slotName
  location: location
  tags: union(tags, {
    slot: slotName
    purpose: 'zero-downtime-deployment'
  })
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appService.properties.serverFarmId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: runtimeStack
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: healthCheckPath

      // Auto-swap disabled — require manual approval or CI/CD gate
      autoSwapSlotName: ''

      appSettings: [for setting in items(mergedSettings): {
        name: setting.key
        value: setting.value
      }]
    }
  }
}

// ============================================================================
// WARM-UP CONFIGURATION
// ============================================================================

// Warm-up for Linux App Service is configured via app settings:
//   WEBSITE_SWAP_WARMUP_PING_PATH  — endpoint Azure hits before completing swap
//   WEBSITE_SWAP_WARMUP_PING_STATUSES — expected HTTP status (200)
// Combined with healthCheckPath on the slot, this ensures the .NET runtime,
// DI container, and caches are fully warm before traffic is routed.
//
// Additional slot web config for diagnostics during staging validation:
resource stagingSlotWebConfig 'Microsoft.Web/sites/slots/config@2023-01-01' = {
  parent: stagingSlot
  name: 'web'
  properties: {
    autoHealEnabled: false
    detailedErrorLoggingEnabled: false
    httpLoggingEnabled: true
    requestTracingEnabled: true
  }
}

// ============================================================================
// SLOT-STICKY SETTINGS
// ============================================================================

// Configure slot-sticky settings on the parent App Service
// These settings stay with their respective slot and are NOT swapped
// e.g., ASPNETCORE_ENVIRONMENT=Staging stays on staging even after swap
resource slotConfigNames 'Microsoft.Web/sites/config@2023-01-01' = {
  parent: appService
  name: 'slotConfigNames'
  properties: {
    appSettingNames: slotSettingNames
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output slotId string = stagingSlot.id
output slotName string = stagingSlot.name
output slotDefaultHostName string = stagingSlot.properties.defaultHostName
output slotUrl string = 'https://${stagingSlot.properties.defaultHostName}'
output slotPrincipalId string = stagingSlot.identity.principalId
