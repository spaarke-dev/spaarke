// infrastructure/bicep/modules/app-service-slot.bicep
// Creates a deployment slot for an existing App Service
// Used for zero-downtime deployments via staging → production swap

@description('Name of the parent App Service')
param appServiceName string

@description('Name of the deployment slot')
param slotName string = 'staging'

@description('Location for the slot')
param location string = resourceGroup().location

@description('App Service Plan resource ID')
param appServicePlanId string

@description('Health check path for the slot')
param healthCheckPath string = '/healthz'

@description('Tags for the resource')
param tags object = {}

resource appService 'Microsoft.Web/sites@2023-01-01' existing = {
  name: appServiceName
}

resource slot 'Microsoft.Web/sites/slots@2023-01-01' = {
  parent: appService
  name: slotName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: healthCheckPath
    }
  }
}

output slotName string = slot.name
output slotDefaultHostName string = slot.properties.defaultHostName
output slotUrl string = 'https://${slot.properties.defaultHostName}'
output slotPrincipalId string = slot.identity.principalId
