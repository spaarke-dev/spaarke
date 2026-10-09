// infrastructure/bicep/modules/openai.bicep
// Azure OpenAI module for Spaarke AI services
// Task 046: Network hardening + capacity planning
//
// Deployment strategy:
//   1. Deploy with disablePublicNetworkAccess=false (default) alongside private endpoints
//   2. Validate private endpoint connectivity from App Service VNet integration
//   3. Set disablePublicNetworkAccess=true to lock down
//
// Model upgrade strategy (task 247 supersedes task 046's add-a-new-name rotation):
//   - Chat deployments: KEEP the deployment names (the BFF hard-codes them) and change
//     the model/version behind them, in this file and PinnedModelCatalog.cs together.
//   - text-embedding-3-large: Version locked. Changing embedding model requires
//     full re-indexing of AI Search. Plan ~2h downtime window for re-index.
//   - text-embedding-3-small: DEPRECATED and removed from Bicep. Migration to
//     text-embedding-3-large (3072 dims) is complete. Do not re-add.
//   - PTU evaluation: At >100K TPM sustained usage, evaluate Provisioned
//     Throughput Units for cost savings. Current beta scale (~200 analyses/day)
//     does not justify PTU commitment.

@description('Name of the Azure OpenAI resource')
param openAiName string

@description('Location for the resource')
param location string = resourceGroup().location

@description('SKU for Azure OpenAI')
param sku string = 'S0'

@description('Disable public network access (enable after private endpoint validation)')
param disablePublicNetworkAccess bool = false

@description('Allowed IP ranges when public access is enabled (empty = allow all when public)')
param allowedIpRanges array = []

@description('Model deployments to create. Capacity is in thousands of tokens per minute (TPM).')
// Task 247 (owner 2026-10-06) — mirrored by PinnedModelCatalog.cs (L2), which H0 checks quota and model
// availability against; ArmTemplateInspectorTests fails if the two differ. Change both together.
//   - Deployment NAMES are what the BFF calls (hard-coded across its AI code) — keep them.
//   - SKU DataZoneStandard: owner decision — prompts are processed inside the US data zone. Fresh
//     subscriptions auto-grant it (westus3, read 2026-10-06: gpt-4o 300, gpt4.1-mini 2000,
//     text-embedding-3-large 1000), while Standard gives gpt-4o 0.
//   - `gpt-4o-mini` runs gpt-4.1-mini 2025-04-14: gpt-4o-mini 2024-07-18 is Deprecating in westus3, and
//     Azure blocks NEW deployments of a Deprecating version (2026-08-22 stand-up, F1/O1). Same API shape.
//   - gpt-4o 2024-11-20 and gpt-4.1-mini 2025-04-14 are Legacy and retire 2027-04-14; H0's freshness
//     probe starts failing 90 days before — refresh the pins (here + PinnedModelCatalog) before then.
//   - spaarke-gpt4o-mini (AIPU2-004 classification) removed: no BFF code reads ClassificationModelName.
// ALWAYS check `az cognitiveservices model list --location <openAiLocation>` before changing a pin.
param deployments array = [
  {
    name: 'gpt-4o'
    model: 'gpt-4o'
    version: '2024-11-20'
    sku: 'DataZoneStandard'
    capacity: 150
  }
  {
    name: 'gpt-4o-mini'
    model: 'gpt-4.1-mini'
    version: '2025-04-14'
    sku: 'DataZoneStandard'
    capacity: 200 // Minimum 200 TPM for beta scale (~200 analyses/day)
  }
  {
    name: 'text-embedding-3-large'
    model: 'text-embedding-3-large'
    version: '1'
    sku: 'DataZoneStandard'
    capacity: 350
  }
]

@description('Principal ID of the per-customer User-Assigned Managed Identity (from `modules/uami.bicep`, task 028) granted Cognitive Services User (built-in role `a97b65f3-24c7-4388-baec-2e87135dc908`) on this OpenAI account. Task 030 canonical target per ADR-028: the BFF acquires Azure OpenAI tokens via `DefaultAzureCredential` pinned to the UAMI (`AZURE_CLIENT_ID` app-setting). Local auth is disabled on this account (task 244), so this grant is the only route the stamp BFF has into it; the ADR-028 E-2 API-key fallback applies to the shared dev account, not to stamps. Empty default skips the assignment (caller-side wiring). Emitted assignment sets principalType=ServicePrincipal.')
param userAssignedIdentityPrincipalId string = ''

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// OPENAI ACCOUNT
// ============================================================================

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: sku
  }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: disablePublicNetworkAccess ? 'Disabled' : 'Enabled'
    networkAcls: {
      defaultAction: disablePublicNetworkAccess ? 'Deny' : (empty(allowedIpRanges) ? 'Allow' : 'Deny')
      ipRules: [for ip in allowedIpRanges: {
        value: ip
      }]
    }
    // Keyless (owner D13, task 244): Microsoft Entra only. The stamp BFF calls this account with its
    // UAMI (Cognitive Services User below); no customer vault holds an OpenAI key, and the BFF uses a
    // key only when one is configured. ADR-028 E-2 (MI 401 on the shared dev `AIServices`-kind account)
    // is measured for this `OpenAI`-kind account by H13 (task 230).
    disableLocalAuth: true
  }
}

// ============================================================================
// MODEL DEPLOYMENTS
// ============================================================================

// Deployment SKU is per-deployment optional (default 'Standard' preserves prior
// caller behavior); every stamp deployment sets DataZoneStandard (task 247). gpt-5.x
// family REQUIRES 'GlobalStandard' (gpt-5-pro literally supports no other SKU). The pinned set is this module's `deployments` default (customer.bicep
// passes none); the gpt-5.x tier example lived in the retired model1-shared.bicep (task 225a). Discovered 2026-08-22 during Model 1 Prod
// stand-up (customer-provisioning-orchestration-r1) — preflight rejects with
// "InvalidResourceProperties: The specified SKU 'Standard' of account deployment
// is not supported by the model 'gpt-5.x'" when this defaults for gpt-5 models.
@batchSize(1)
resource modelDeployments 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = [for deployment in deployments: {
  parent: openAi
  name: deployment.name
  sku: {
    name: deployment.?sku ?? 'Standard'
    capacity: deployment.capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: deployment.model
      version: deployment.version
    }
    raiPolicyName: 'Microsoft.Default'
  }
}]

// ============================================================================
// RBAC: Cognitive Services User for UAMI (task 030 — Phase C canonical grant)
// Built-in role ID: a97b65f3-24c7-4388-baec-2e87135dc908
// Grants the per-customer UAMI the right to call OpenAI data-plane actions
// (chat completions, embeddings) via `DefaultAzureCredential` — the canonical
// ADR-028 outbound-auth path and, with local auth disabled (task 244), the only one.
// The API-key fallback of ADR-028 E-2 applies to the shared dev account only.
// No prior RBAC on this module — no interim SA-MI grant to preserve.
// ============================================================================

var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'

resource uamiCognitiveServicesUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(userAssignedIdentityPrincipalId)) {
  scope: openAi
  name: guid(openAi.id, userAssignedIdentityPrincipalId, cognitiveServicesUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Per-customer UAMI (task 028) invokes Azure OpenAI data-plane via DefaultAzureCredential (task 030 / ADR-028)'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output openAiId string = openAi.id
output openAiName string = openAi.name
output openAiEndpoint string = openAi.properties.endpoint
output publicNetworkAccess string = openAi.properties.publicNetworkAccess
// No key output (task 244): local auth is disabled.
