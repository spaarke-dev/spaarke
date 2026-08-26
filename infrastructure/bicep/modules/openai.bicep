// infrastructure/bicep/modules/openai.bicep
// Azure OpenAI module for Spaarke AI services
// Task 046: Network hardening + capacity planning
//
// Deployment strategy:
//   1. Deploy with disablePublicNetworkAccess=false (default) alongside private endpoints
//   2. Validate private endpoint connectivity from App Service VNet integration
//   3. Set disablePublicNetworkAccess=true to lock down
//
// Model upgrade strategy (documented per task 046 step 5):
//   - gpt-4o: Pin to specific version. Upgrade by adding new deployment name
//     (e.g. gpt-4o-2025) then switching app config, then removing old deployment.
//   - gpt-4o-mini: Same pin-and-rotate strategy.
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
// SESSION 12 (2026-08-26) version-pin refresh — verified via
//   az cognitiveservices model list --location westus3 --subscription <sub>
// Reality in westus3 as of 2026-08-26:
//   gpt-4o                  2024-05-13  Deprecating
//   gpt-4o                  2024-08-06  Deprecating (previous pin)
//   gpt-4o                  2024-11-20  Legacy       (bumped to — newest available)
//   gpt-4o-mini             2024-07-18  Deprecating (ONLY version in westus3 — cannot bump)
//   text-embedding-3-large  1           GenerallyAvailable (kept)
// No GA gpt-4o / gpt-4o-mini exists in westus3 today; 2024-11-20 (Legacy) is the
// least-bad gpt-4o pin. gpt-4o-mini stays at 2024-07-18 because westus3 offers
// no newer version. Follow-on: migrate to gpt-4.1 / gpt-5.x family when frontier
// TPM quota is granted (per MEMORY reference_azure_fresh_sub_openai_tier_gates).
param deployments array = [
  {
    name: 'gpt-4o'
    model: 'gpt-4o'
    version: '2024-11-20' // SESSION 12: bumped from 2024-08-06 (Deprecating) to 2024-11-20 (Legacy, newest in westus3)
    capacity: 150
  }
  {
    name: 'gpt-4o-mini'
    model: 'gpt-4o-mini'
    version: '2024-07-18' // SESSION 12: retained — ONLY gpt-4o-mini version available in westus3 (Deprecating). See follow-on above.
    capacity: 200 // Minimum 200 TPM for beta scale (~200 analyses/day)
  }
  {
    // spaarke-gpt4o-mini: dedicated classification deployment (AIPU2-004)
    // Separate from gpt-4o-mini to isolate Layer 2 classification workloads
    // (capability routing, safety pre-checks, feedback triage) from
    // general-purpose mini usage. Prevents workload mixing per Microsoft
    // recommendation for stable TPM accounting.
    // Use cases: ~600-token classification prompts, session summarization
    //            (~4000 token inputs), intent routing, feedback triage.
    // 30K TPM is sufficient for classification workload at dev scale.
    name: 'spaarke-gpt4o-mini'
    model: 'gpt-4o-mini'
    version: '2024-07-18' // SESSION 12: retained — ONLY gpt-4o-mini version available in westus3 (Deprecating).
    capacity: 30
  }
  {
    name: 'text-embedding-3-large'
    model: 'text-embedding-3-large'
    version: '1' // SESSION 12: verified GenerallyAvailable in westus3 — kept.
    capacity: 350
  }
  // NOTE: text-embedding-3-small has been removed (deprecated).
  // Migration to text-embedding-3-large (3072 dims) is complete.
  // See docs/guides/AI-EMBEDDING-STRATEGY.md for rationale.
]

@description('Principal ID of the per-customer User-Assigned Managed Identity (from `modules/uami.bicep`, task 028) granted Cognitive Services User (built-in role `a97b65f3-24c7-4388-baec-2e87135dc908`) on this OpenAI account. Task 030 canonical target per ADR-028: the BFF acquires Azure OpenAI tokens via `DefaultAzureCredential` pinned to the UAMI (`AZURE_CLIENT_ID` app-setting). Note the ADR-028 E-2 documented MI exception for OpenAI data plane — when API-key auth is active this grant is dormant but MUST still be provisioned so the MI code path can be restored via config change alone. Empty default skips the assignment (caller-side wiring). Emitted assignment sets principalType=ServicePrincipal.')
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
    // Disable local API key auth when using managed identity + private endpoint
    // Uncomment after validating managed identity auth end-to-end:
    // disableLocalAuth: true
  }
}

// ============================================================================
// MODEL DEPLOYMENTS
// ============================================================================

// Deployment SKU is per-deployment optional (default 'Standard' preserves prior
// caller behavior). gpt-5.x family REQUIRES 'GlobalStandard' (gpt-5-pro literally
// supports no other SKU); gpt-4o family supports 'Standard'. Add `sku: 'GlobalStandard'`
// to a deployment object to override — see stacks/model1-shared.bicep for the
// canonical gpt-5.x tier stack example. Discovered 2026-08-22 during Model 1 Prod
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
// ADR-028 outbound-auth path. See E-2 exception note on the param description
// for the API-key fallback currently in force on `spaarke-openai-dev`.
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
#disable-next-line outputs-should-not-contain-secrets
output openAiKey string = openAi.listKeys().key1
