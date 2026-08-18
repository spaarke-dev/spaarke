// infrastructure/bicep/platform.bicep
// ============================================================================
// ENVIRONMENT-SCOPE SHARED PLATFORM — shrunk per D12 (customer-provisioning-orchestration-r1 task 031)
// ============================================================================
//
// PURPOSE (post-D12 shrink)
//   Composes ONLY genuinely env-scope shared infrastructure — resources that
//   are neither per-customer nor L2-orchestrator-specific:
//     1. Resource Group   `rg-spaarke-platform-{env}`
//     2. Monitoring       App Insights + Log Analytics (env-scope operational visibility)
//     3. Platform KV      `sprk-{env}-kv` (env-scope shared vault per §7.9 canonical name)
//
// DELIBERATELY OUT OF SCOPE — moved / owned elsewhere per D12 + Wave C2 topology
//   * Per-customer AI resources (OpenAI, AI Search, Doc Intelligence, per-customer Cosmos)
//         → `stacks/model2-full.bicep`  (Model 2 dedicated per-customer stack)
//         → `stacks/model1-shared.bicep` (Model 1 fixed-floor shared + per-tenant Cosmos)
//         → `customer.bicep`             (per-customer Cosmos + optional SignalR, task 027)
//         Rationale: D12 mandates "per-customer AI resources move to `customer.bicep`";
//         spec.md §7.2 disposition table classifies OpenAI/AI Search as 🟡 fixed-floor
//         (shared only in Model 1) and Doc Intel/Cosmos as 🔴 dedicated in both tiers.
//         Platform-scope AI would violate the D3 per-customer dedication principle.
//   * L2 orchestrator (App Service + Cosmos + control-plane KV + monitoring)
//         → `platform-controlplane.bicep` (task 033, shipped at `d3b994434`)
//         Rationale: the L2 orchestrator is a NEW fleet-scoped service with its own
//         UAMI, its own KV (`sprk-controlplane-{env}-kv`), and its own Cosmos
//         (`cosmos-spaarke-platform-{env}` — for provisioning-run state, distinct
//         from the retired per-customer AI Cosmos this stack used to declare).
//   * SPAARKE-INTERNAL BFF App Service
//         → `stacks/model1-shared.bicep` (multi-tenant BFF for Model 1 shared tier)
//         → `stacks/model2-full.bicep`   (dedicated BFF per Model 2 customer)
//         Rationale: after D12, per-customer AI wiring is a runtime-per-request
//         concern; the BFF's AI dependencies (KV refs for OpenAI/AI Search/Doc Intel
//         endpoints + keys) can no longer be baked into a platform-scope App
//         Service. Both first-class stacks already provision their own BFF with
//         the correct AI wiring for their tier. Additionally, `modules/app-service.bicep`
//         was refactored in this project (Wave C2 sibling task 029) to require
//         `userAssignedIdentityResourceId` and to drop `enableManagedIdentity` +
//         `keyVaultName` — a structural signal that BFF App Services must now be
//         composed via UAMI-first stacks, not via a legacy SystemAssigned platform
//         monolith.
//   * Env-scope Redis (per Q-E FR-12)
//         → `scripts/Deploy-RedisCache.ps1` (per-env, shared across customers)
//   * Per-customer Service Bus + Storage + Key Vault + membership topic + ACS + SignalR
//         → `customer.bicep`
//
// TOPOLOGY DECISION (per CLAUDE.md §6.5 — documented in `notes/task-031-topology-decision.md`)
//   Path C — Comply. The pre-shrink `platform.bicep` (316 lines) composed:
//     - App Service Plan + shared BFF App Service + staging slot + config override
//     - Azure OpenAI + AI Search + Doc Intelligence + Cosmos DB
//     - Monitoring + Platform KV
//   Removing the per-customer AI resources (per D12) also removes the BFF App
//   Service's dependency chain (OPENAI_ENDPOINT / AI_SEARCH_ENDPOINT / DOC_INTELLIGENCE_ENDPOINT
//   Key Vault references pointed at those modules' outputs). Rather than leave a
//   BFF App Service with dangling KV refs and no legitimate home for its AI
//   wiring, the whole BFF chain (Plan + App + Config + Slot) is also removed —
//   its correct home is `stacks/model1-shared.bicep` (Model 1) or
//   `stacks/model2-full.bicep` (Model 2). This is consistent with the Wave C2
//   sibling task 029 refactor of `modules/app-service.bicep` (which structurally
//   requires UAMI-first composition — a pattern the stacks already follow).
//
// COORDINATION WITH `platform-controlplane.bicep` (task 033)
//   Zero file overlap. Both stacks target subscription scope + `rg-spaarke-platform-{env}`
//   as their RG. Both invoke `modules/monitoring.bicep` and `modules/key-vault.bicep`
//   with DIFFERENT resource names — the vaults + workspaces coexist:
//     * platform.bicep (THIS file):
//         - Monitoring:  `sprk-platform-{env}-insights` + `sprk-platform-{env}-logs`
//         - KV:          `sprk-{env}-kv` (env-scope shared vault; §7.9 canonical)
//     * platform-controlplane.bicep (task 033):
//         - Monitoring:  `sprk-controlplane-{env}-insights` + `sprk-controlplane-{env}-logs`
//         - KV:          `sprk-controlplane-{env}-kv` (L2-scope orchestrator vault)
//   Rationale for separate monitoring instances: L2 telemetry is fleet-orchestration
//   audit trail (NFR-11); mixing with env-scope operational telemetry would
//   complicate querying + retention tuning. Same RG keeps cross-workspace queries
//   trivial when needed. (See `notes/task-033-deviations.md` N5.)
//
// FOLLOW-ON WORK (out of scope for task 031; flagged for downstream)
//   * `scripts/Deploy-Platform.ps1` header comments enumerate the pre-shrink
//     resource list — those will read as stale once this shrink lands. Follow-on
//     alignment during Wave D deploy tooling.
//   * `infrastructure/bicep/parameters/platform-prod.bicepparam` is being aligned
//     alongside this shrink (removes obsolete OpenAI/AI Search/App Service Plan
//     parameters).
//
// USAGE
//   az deployment sub create \
//     --location westus2 \
//     --template-file infrastructure/bicep/platform.bicep \
//     --parameters infrastructure/bicep/parameters/platform-prod.bicepparam

targetScope = 'subscription'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Environment name (dev, staging, prod)')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Primary Azure region for all shared platform resources')
param location string = 'westus2'

@description('Log Analytics retention in days')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 180

@description('Tags applied to all resources')
param tags object = {
  environment: environmentName
  application: 'spaarke'
  layer: 'platform'
  managedBy: 'bicep'
}

@description('Platform Key Vault name. Canonical: sprk-{env}-kv per docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "KV-Secret & Resource Naming Standard" R3 + spec.md §7.9 / FR-35. Parameterized (task 018) so H4 handler + Phase H seeder can address correctly-named per-env vaults deterministically and codified exceptions (e.g., spaarke-spekvcert dev carve-out per task 020) can override the default.')
param keyVaultName string = 'sprk-${environmentName}-kv'

// ============================================================================
// VARIABLES
// ============================================================================

var resourceGroupName = 'rg-spaarke-platform-${environmentName}'

// Distinct from `platform-controlplane.bicep`'s `sprk-controlplane-{env}-insights`
// / `sprk-controlplane-{env}-logs` (L2-scope) — see COORDINATION note above.
var appInsightsName = 'sprk-platform-${environmentName}-insights'
var logAnalyticsName = 'sprk-platform-${environmentName}-logs'

// ============================================================================
// RESOURCE GROUP
// ============================================================================

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ============================================================================
// 1. MONITORING — App Insights + Log Analytics for env-scope operational visibility
// ============================================================================

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'platform-monitoring'
  params: {
    appInsightsName: appInsightsName
    logAnalyticsName: logAnalyticsName
    location: location
    retentionInDays: logRetentionDays
    tags: tags
  }
}

// ============================================================================
// 2. PLATFORM KEY VAULT — env-scope shared vault (canonical `sprk-{env}-kv`)
// ============================================================================

module keyVault 'modules/key-vault.bicep' = {
  scope: rg
  name: 'platform-keyvault'
  params: {
    keyVaultName: keyVaultName
    location: location
    sku: 'standard'
    tags: tags
  }
}

// ============================================================================
// OUTPUTS — Exported for deployment scripts / downstream tooling
// ============================================================================

// Resource Group
output resourceGroupName string = rg.name
output location string = location
output environmentName string = environmentName

// Key Vault
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output keyVaultId string = keyVault.outputs.keyVaultId

// Monitoring
output appInsightsName string = monitoring.outputs.appInsightsName
output appInsightsConnectionString string = monitoring.outputs.connectionString
output logAnalyticsWorkspaceId string = monitoring.outputs.logAnalyticsWorkspaceId
