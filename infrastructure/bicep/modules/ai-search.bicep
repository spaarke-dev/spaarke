// infrastructure/bicep/modules/ai-search.bicep
// Azure AI Search module for Spaarke vector search

@description('Name of the Azure AI Search resource')
param searchServiceName string

@description('Location for the resource')
param location string = resourceGroup().location

@description('SKU for Azure AI Search')
@allowed(['free', 'basic', 'standard', 'standard2', 'standard3', 'storage_optimized_l1', 'storage_optimized_l2'])
param sku string = 'standard'

@description('Number of replicas (for HA)')
@minValue(1)
@maxValue(12)
param replicaCount int = 1

@description('Number of partitions (for storage/throughput)')
@minValue(1)
@maxValue(12)
param partitionCount int = 1

@description('Enable semantic search')
@allowed(['disabled', 'free', 'standard'])
param semanticSearch string = 'standard'

@description('Tags for the resource')
param tags object = {}

resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  name: searchServiceName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    replicaCount: replicaCount
    partitionCount: partitionCount
    hostingMode: 'default'
    semanticSearch: semanticSearch
    publicNetworkAccess: 'enabled'
    encryptionWithCmk: {
      enforcement: 'Unspecified'
    }
  }
}

output searchServiceId string = searchService.id
output searchServiceName string = searchService.name
output searchServiceEndpoint string = 'https://${searchServiceName}.search.windows.net'
#disable-next-line outputs-should-not-contain-secrets
output searchServiceAdminKey string = searchService.listAdminKeys().primaryKey
output searchServiceQueryKey string = searchService.listQueryKeys()[0].key
