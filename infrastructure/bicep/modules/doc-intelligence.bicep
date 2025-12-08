// infrastructure/bicep/modules/doc-intelligence.bicep
// Azure Document Intelligence module for Spaarke document processing

@description('Name of the Document Intelligence resource')
param docIntelligenceName string

@description('Location for the resource')
param location string = resourceGroup().location

@description('SKU for Document Intelligence')
@allowed(['F0', 'S0'])
param sku string = 'S0'

@description('Tags for the resource')
param tags object = {}

resource docIntelligence 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: docIntelligenceName
  location: location
  tags: tags
  kind: 'FormRecognizer'
  sku: {
    name: sku
  }
  properties: {
    customSubDomainName: docIntelligenceName
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

output docIntelligenceId string = docIntelligence.id
output docIntelligenceName string = docIntelligence.name
output docIntelligenceEndpoint string = docIntelligence.properties.endpoint
#disable-next-line outputs-should-not-contain-secrets
output docIntelligenceKey string = docIntelligence.listKeys().key1
