// infrastructure/bicep/modules/app-service-plan.bicep
// Azure App Service Plan module

@description('Name of the App Service Plan')
param planName string

@description('Location for the App Service Plan')
param location string = resourceGroup().location

@description('SKU for the App Service Plan')
@allowed(['B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P1v3', 'P2v3', 'P3v3'])
param sku string = 'B1'

@description('Operating system')
@allowed(['Linux', 'Windows'])
param os string = 'Linux'

@description('Tags for the resource')
param tags object = {}

var skuTiers = {
  B1: 'Basic'
  B2: 'Basic'
  B3: 'Basic'
  S1: 'Standard'
  S2: 'Standard'
  S3: 'Standard'
  P1v3: 'PremiumV3'
  P2v3: 'PremiumV3'
  P3v3: 'PremiumV3'
}

resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: planName
  location: location
  tags: tags
  kind: os == 'Linux' ? 'linux' : 'app'
  sku: {
    name: sku
    tier: skuTiers[sku]
  }
  properties: {
    reserved: os == 'Linux'
  }
}

output planId string = appServicePlan.id
output planName string = appServicePlan.name
