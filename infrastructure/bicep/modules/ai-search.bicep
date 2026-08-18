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

@description('Principal ID of the per-customer User-Assigned Managed Identity (from `modules/uami.bicep`, task 028) granted Cognitive Services User (built-in role `a97b65f3-24c7-4388-baec-2e87135dc908`) on this AI Search service per task 030 constraint (c). Empty default skips the assignment. Emitted assignment sets principalType=ServicePrincipal. See notes/task-030-deviations.md N1: `Cognitive Services User` is scoped to Microsoft.CognitiveServices resource provider and is functionally dormant on this Microsoft.Search resource; the AI Search data-plane MI grant that IS actionable (`Search Index Data Contributor` / `Search Service Contributor`) is deferred to a follow-on task. The literal POML constraint is honored here so the audit trail matches spec FR-37; runtime effectiveness will be validated at Phase F acceptance and the follow-on filed if the gap is observed.')
param userAssignedIdentityPrincipalId string = ''

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

// ============================================================================
// RBAC: Cognitive Services User for UAMI (task 030 — POML constraint (c))
// Built-in role ID: a97b65f3-24c7-4388-baec-2e87135dc908
// Honors the literal POML constraint from task 030 which enumerates AI Search
// as a Cognitive Services User target alongside OpenAI + Doc Intelligence.
// See notes/task-030-deviations.md N1 for the effectiveness caveat (this role
// is scoped to Microsoft.CognitiveServices; Microsoft.Search RBAC for the AI
// Search data plane uses `Search Index Data Contributor` /
// `Search Service Contributor`, filed as a follow-on to validate at Phase F).
// No prior RBAC on this module — no interim SA-MI grant to preserve.
// ============================================================================

var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource uamiCognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(userAssignedIdentityPrincipalId)) {
  scope: searchService
  name: guid(searchService.id, userAssignedIdentityPrincipalId, cognitiveServicesUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Per-customer UAMI (task 028) — Cognitive Services User grant honors task 030 POML constraint (c); see notes/task-030-deviations.md N1 for Microsoft.Search RBAC effectiveness caveat'
  }
}

output searchServiceId string = searchService.id
output searchServiceName string = searchService.name
output searchServiceEndpoint string = 'https://${searchServiceName}.search.windows.net'
#disable-next-line outputs-should-not-contain-secrets
output searchServiceAdminKey string = searchService.listAdminKeys().primaryKey
#disable-next-line outputs-should-not-contain-secrets
output searchServiceQueryKey string = searchService.listQueryKeys().value[0].key
