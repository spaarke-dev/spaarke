// infrastructure/bicep/modules/content-safety.bicep
// Azure AI Content Safety module — the per-customer safety perimeter (task 246, plan G26)
//
// Capabilities used by the BFF (named HttpClient "ContentSafety"):
//   - Prompt Shields: jailbreak detection + indirect (document) attack detection (PromptShieldService)
//   - Groundedness Detection: RAG response validation against source content (GroundednessCheckService)
//
// Keyless (owner D13): Microsoft Entra only. The custom subdomain below is required for Entra auth; the
// stamp BFF calls with its UAMI (Cognitive Services User below — Cognitive Services OpenAI User does NOT
// cover Content Safety dataActions). There is no key output and no customer vault stores a key.
//
// Regional note:
//   Prompt Shields are offered in every Content Safety region; Groundedness Detection only in some. Per
//   learn.microsoft.com/azure/ai-services/content-safety/region-availability (updated 2026-09-18) the US regions with BOTH are westus, eastus, eastus2 and canadaeast —
//   NOT westus2 (the stamp default location). In a region without groundedness every check fails open
//   silently. customer.bicep passes `contentSafetyLocation` (default: westus).
//
// Caller: customer.bicep only. Shared dev serves Content Safety from its multi-service AIServices account
// (spaarke-openai-dev) and does not use this module.

@description('Name of the Azure AI Content Safety resource (also its custom subdomain)')
param contentSafetyName string

@description('Location for the resource. It must offer Prompt Shields AND Groundedness Detection (US: westus, eastus, eastus2, canadaeast; not westus2).')
param location string = resourceGroup().location

@description('SKU for Content Safety. S0 is the paid tier (billed per call); F0 is free with a low call limit.')
@allowed(['F0', 'S0'])
param sku string = 'S0'

@description('Disable public network access. Set true after confirming private endpoint connectivity.')
param disablePublicNetworkAccess bool = false

@description('Principal ID of the per-customer User-Assigned Managed Identity (from `modules/uami.bicep`) granted Cognitive Services User (built-in role `a97b65f3-24c7-4388-baec-2e87135dc908`) on this account. The stamp BFF calls Prompt Shields and Groundedness Detection with it. Empty default skips the assignment. Emitted assignment sets principalType=ServicePrincipal.')
param userAssignedIdentityPrincipalId string = ''

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// CONTENT SAFETY ACCOUNT
// ============================================================================

resource contentSafety 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: contentSafetyName
  location: location
  tags: tags
  kind: 'ContentSafety'
  sku: {
    name: sku
  }
  properties: {
    customSubDomainName: contentSafetyName
    publicNetworkAccess: disablePublicNetworkAccess ? 'Disabled' : 'Enabled'
    networkAcls: {
      defaultAction: disablePublicNetworkAccess ? 'Deny' : 'Allow'
    }
    // Keyless (owner D13, task 246): Microsoft Entra only — the BFF's ContentSafetyAuthHandler sends a
    // bearer token (AiSafety__ContentSafety__ManagedIdentity__Enabled=true on every stamp).
    disableLocalAuth: true
  }
}

// ============================================================================
// RBAC: Cognitive Services User for the stamp UAMI
// Built-in role ID: a97b65f3-24c7-4388-baec-2e87135dc908 (dataActions Microsoft.CognitiveServices/*,
// which covers accounts/ContentSafety/text:shieldprompt/action and text:detectgroundedness/action).
// ============================================================================

var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource uamiCognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(userAssignedIdentityPrincipalId)) {
  scope: contentSafety
  name: guid(contentSafety.id, userAssignedIdentityPrincipalId, cognitiveServicesUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Per-customer UAMI calls Content Safety (Prompt Shields, Groundedness Detection) via DefaultAzureCredential (task 246 / ADR-028)'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output contentSafetyId string = contentSafety.id
output contentSafetyName string = contentSafety.name
output contentSafetyEndpoint string = contentSafety.properties.endpoint
// Task 246 (owner D13): no key output — local auth is disabled and the stamp BFF uses its UAMI.
