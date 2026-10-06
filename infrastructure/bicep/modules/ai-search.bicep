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

@description('Principal ID of the per-customer User-Assigned Managed Identity (from `modules/uami.bicep`, task 028) granted Cognitive Services User (built-in role `a97b65f3-24c7-4388-baec-2e87135dc908`) on this AI Search service per task 030 constraint (c). Empty default skips the assignment. Emitted assignment sets principalType=ServicePrincipal. See notes/task-030-deviations.md N1: `Cognitive Services User` is scoped to Microsoft.CognitiveServices resource provider and is functionally dormant on this Microsoft.Search resource; the AI Search grants that ARE effective are made elsewhere: Search Index Data Contributor + Search Service Contributor for the BFF UAMI (modules/bff-runtime-rbac.bicep) and Search Service Contributor + Search Index Data Reader for the L2 UAMI (modules/customer-l2-bff-rbac.bicep, task 244). The literal POML constraint is honored here so the audit trail matches spec FR-37.')
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
    // Keyless (owner D13, task 244): Microsoft Entra only. API keys are rejected, so every caller
    // authenticates with its identity: the stamp BFF UAMI (Search Index Data Contributor + Search
    // Service Contributor, modules/bff-runtime-rbac.bicep) and the L2 control-plane UAMI for H2b
    // index creation (same two roles, modules/customer-l2-bff-rbac.bicep). Azure requires
    // `authOptions` to be absent when local auth is disabled.
    disableLocalAuth: true
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
// `Search Service Contributor` — granted to the BFF UAMI by bff-runtime-rbac.bicep
// and to the L2 UAMI by customer-l2-bff-rbac.bicep (task 244).
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
// No key outputs (task 244): local auth is disabled, so an admin or query key would authenticate nothing.
