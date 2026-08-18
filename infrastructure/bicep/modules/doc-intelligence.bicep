// infrastructure/bicep/modules/doc-intelligence.bicep
// Azure Document Intelligence module for Spaarke document processing

@description('Name of the Document Intelligence resource')
param docIntelligenceName string

@description('Location for the resource')
param location string = resourceGroup().location

@description('SKU for Document Intelligence')
@allowed(['F0', 'S0'])
param sku string = 'S0'

@description('Principal ID of the per-customer User-Assigned Managed Identity (from `modules/uami.bicep`, task 028) granted Cognitive Services User (built-in role `a97b65f3-24c7-4388-baec-2e87135dc908`) on this Document Intelligence account. Task 030 canonical target per ADR-028: BFF calls the Doc Intelligence data-plane via `DefaultAzureCredential` pinned to the UAMI. Empty default skips the assignment. Emitted assignment sets principalType=ServicePrincipal.')
param userAssignedIdentityPrincipalId string = ''

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

// ============================================================================
// RBAC: Cognitive Services User for UAMI (task 030 — Phase C canonical grant)
// Built-in role ID: a97b65f3-24c7-4388-baec-2e87135dc908
// Grants the per-customer UAMI the right to call Document Intelligence data-
// plane actions via `DefaultAzureCredential` — the canonical ADR-028 outbound
// path. No prior RBAC on this module — no interim SA-MI grant to preserve.
// ============================================================================

var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource uamiCognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(userAssignedIdentityPrincipalId)) {
  scope: docIntelligence
  name: guid(docIntelligence.id, userAssignedIdentityPrincipalId, cognitiveServicesUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Per-customer UAMI (task 028) invokes Document Intelligence data-plane via DefaultAzureCredential (task 030 / ADR-028)'
  }
}

output docIntelligenceId string = docIntelligence.id
output docIntelligenceName string = docIntelligence.name
output docIntelligenceEndpoint string = docIntelligence.properties.endpoint
#disable-next-line outputs-should-not-contain-secrets
output docIntelligenceKey string = docIntelligence.listKeys().key1
