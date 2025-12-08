// infrastructure/bicep/modules/app-service.bicep
// Azure App Service module for Sprk.Bff.Api

@description('Name of the App Service')
param appServiceName string

@description('App Service Plan resource ID')
param appServicePlanId string

@description('Location for the App Service')
param location string = resourceGroup().location

@description('Runtime stack')
param runtimeStack string = 'DOTNETCORE|8.0'

@description('App settings as key-value object')
param appSettings object = {}

@description('Key Vault name for secret references')
param keyVaultName string = ''

@description('Enable managed identity')
param enableManagedIdentity bool = true

@description('Tags for the resource')
param tags object = {}

resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: appServiceName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: enableManagedIdentity ? {
    type: 'SystemAssigned'
  } : null
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: runtimeStack
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [for setting in items(appSettings): {
        name: setting.key
        value: setting.value
      }]
    }
  }
}

// Grant Key Vault access to App Service managed identity
resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = if (keyVaultName != '' && enableManagedIdentity) {
  name: '${keyVaultName}/add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: appService.identity.principalId
        permissions: {
          secrets: ['get', 'list']
          certificates: ['get', 'list']
        }
      }
    ]
  }
}

output appServiceId string = appService.id
output appServiceName string = appService.name
output appServicePrincipalId string = enableManagedIdentity ? appService.identity.principalId : ''
output appServiceDefaultHostName string = appService.properties.defaultHostName
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
