// infrastructure/bicep/modules/app-service-config.bicep
// Applies site configuration overrides to an existing App Service
// Used to set health check path and other platform-specific settings

@description('Name of the existing App Service')
param appServiceName string

@description('Health check path')
param healthCheckPath string = '/healthz'

resource appService 'Microsoft.Web/sites@2023-01-01' existing = {
  name: appServiceName
}

resource siteConfig 'Microsoft.Web/sites/config@2023-01-01' = {
  parent: appService
  name: 'web'
  properties: {
    healthCheckPath: healthCheckPath
  }
}
