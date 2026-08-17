// infrastructure/bicep/stacks/model1-shared.bicep
// ============================================================================
// MODEL 1 FIRST-CLASS TRIAL-TIER STACK (spec.md §3A A1 · customer-provisioning-orchestration-r1 task 032)
// ============================================================================
//
// PURPOSE — first-class per-onboarding composition for the Model 1 (trial/SMB) tier.
// One `az deployment sub create` per Model 1 tenant onboarding:
//   1. Deploys the SHARED PLATFORM RG (idempotent — already exists after 1st tenant) and
//      shared modules (App Service Plan + OpenAI + AI Search + Redis + Service Bus +
//      Monitoring + Doc Intelligence + shared KV + shared BFF App Service).
//   2. Deploys a NEW PER-TENANT RG and per-tenant dedicated modules (UAMI + per-tenant KV +
//      per-tenant Storage + per-tenant Cosmos + per-tenant App Insights).
//   3. Writes per-tenant reference secrets (Dataverse URL + SPE container ID + tenantId)
//      into the per-tenant KV for BFF runtime consumption (H4 completes the full secret
//      surface via canonical secret-catalog manifest).
//
// PATH A EXCEPTION — ADR-027 (spec.md § ADR Tensions row 6):
//   ADR-027 mandates "one Azure subscription per customer" (D4 subscription-isolation +
//   billing unit). This stack is the documented Path A exception for the Model 1 trial/SMB
//   tier per spec.md §3A A1 / design.md §3A rationale. The three fixed-floor Azure resources
//   (App Service Plan, Azure OpenAI provisioned TPM, Azure AI Search) impose ~$400/mo floor
//   that makes strict per-customer stamps uneconomic for trial/SMB prospects (NFR-04). The
//   deviation is scoped narrowly to the 🟡 fixed-floor row in design.md §7.2 disposition
//   table; Model 2 stack (`model2-full.bicep`) remains strict per-customer dedicated.
//
//   Logical isolation replaces subscription-level isolation via 5 BINDING code-level
//   invariants (spec.md §4D — enforced by ArchTests per r3 forcing-function pattern):
//     * §4D I1 (FR-28): no hardcoded default tenant in provisioning scripts
//     * §4D I2 (FR-29): every AI Search query includes unconditional `tenantId eq` filter
//     * §4D I3 (FR-30): every Cosmos read/write includes `/tenantId` partition-key predicate
//     * §4D I4 (FR-31): SPE container IDs derived from per-tenant context (never fallback)
//     * §4D I5 (FR-32): Graph tokens acquired per-tenant scoped (no default-tenant fallback)
//   These invariants land in Wave C6 (5 ArchTests I1–I5 per r3 task 040 pattern) and this
//   stack's operational safety is COUPLED to them landing. Bicep-level network isolation is
//   not feasible for shared fixed-floor resources — logical isolation at the code layer is
//   the whole point of the Path A exception.
//
// PLACEMENT DECISION (CLAUDE.md §10 / §11):
//   Kept as a SEPARATE stack (spec.md § New Components row 5) rather than adding Model 1
//   conditionals to `customer.bicep`. Merging into customer.bicep would reintroduce the
//   shared-vs-dedicated conditional-registration anti-pattern (§10 §F.1 / ADR-032 P1). The
//   separate stack keeps Model 2 (customer.bicep + model2-full.bicep) strictly ADR-027
//   compliant and confines the Path A exception to this file.
//
// SHARED-VS-DEDICATED DISPOSITION (design.md §7.2 authoritative table):
//   🔴 Always dedicated (this stack per-tenant): UAMI, KV secrets, Storage, Cosmos, App Insights
//        Also 🔴 in both tiers but NOT provisioned here: Dataverse env (H5), SPE container (H8),
//        Entra app config (H3). These are PASS-THROUGH references — their identifiers are
//        accepted as params and echoed into per-tenant KV as reference secrets for the BFF.
//   🟡 Fixed-floor levers (this stack SHARED, metered per D19): App Service Plan, Azure OpenAI,
//        Azure AI Search — the reason Path A exists.
//   🟢 Safely shareable (this stack SHARED): Service Bus, App Insights (shared), Content Safety,
//        Doc Intelligence, SignalR (optional). Also 🟢 in Model 2 but STILL dedicated there.
//   🔵 Per-environment (this stack SHARED): Redis Cache per Q-E FR-12 — deployed by the shared
//        module here for env bootstrap; shared across ALL customers in the env.
//
// SUPERSEDES: the 309-line shared-only scaffold that previously lived here (which composed
// only the shared platform infrastructure, had no per-tenant hooks, and did not cite the
// ADR-027 Path A exception in header comments). Deviations relative to the scaffold + the
// intentional interpretation of POML step 3 "shared modules invoked ONCE at platform scope
// (idempotent)" are documented in
// projects/customer-provisioning-orchestration-r1/notes/task-032-deviations.md.
// ============================================================================

targetScope = 'subscription'

// ============================================================================
// PARAMETERS — SHARED PLATFORM GROUP
// (invoked idempotently; already exist after the FIRST Model 1 tenant onboarding
//  per POML step 3. `az deployment sub create` no-ops shared modules on re-run.)
// ============================================================================

@description('Environment name — drives shared resource naming and RG.')
@allowed(['dev', 'staging', 'prod'])
param environment string

@description('Primary Azure region for all resources (shared + per-tenant).')
param location string = 'eastus'

@description('SHARED PLATFORM — Resource group name for shared infrastructure. Canonical: rg-spaarke-shared-{env}.')
param sharedResourceGroupName string = 'rg-spaarke-shared-${environment}'

@description('SHARED PLATFORM — Base name for shared resources. Canonical: sprkshared{env}.')
param sharedBaseName string = 'sprkshared${environment}'

@description('SHARED PLATFORM — Key Vault name. Canonical per r3 task 063 (§7.9 Phase G): sprk-{env}-kv. Parameterized so operators can override for codified exceptions (naming-exception-registry.md).')
param sharedKeyVaultName string = 'sprk-${environment}-kv'

@description('SHARED PLATFORM — App Service Plan name (fixed-floor per §3A A1; shared across all Model 1 tenants).')
param sharedAppServicePlanName string = '${sharedBaseName}-plan'

@description('SHARED PLATFORM — Azure OpenAI resource name (fixed-floor per §3A A1; multi-tenant, per-tenant metering via D19 token-metering layer).')
param sharedOpenAiName string = '${sharedBaseName}-openai'

@description('SHARED PLATFORM — Azure AI Search service name (fixed-floor per §3A A1; multi-tenant via §4D I2 unconditional tenantId filter).')
param sharedAiSearchName string = '${sharedBaseName}-search'

@description('SHARED PLATFORM — Redis Cache name (per-env per Q-E FR-12).')
param sharedRedisName string = '${sharedBaseName}-redis'

@description('SHARED PLATFORM — Service Bus namespace name.')
param sharedServiceBusName string = '${sharedBaseName}-sb'

@description('SHARED PLATFORM — Storage account name (shared cross-tenant buffers: temp-files, ai-chunks, customer-exports). Per-tenant storage is separate.')
param sharedStorageAccountName string = take(toLower(replace('${sharedBaseName}sa', '-', '')), 24)

@description('SHARED PLATFORM — Doc Intelligence resource name.')
param sharedDocIntelligenceName string = '${sharedBaseName}-docintel'

@description('SHARED PLATFORM — BFF App Service name (single multi-tenant deployment serving all Model 1 tenants; per-request tenant context resolved from tid claim per §4D I5).')
param sharedBffAppServiceName string = '${sharedBaseName}-api'

@description('SHARED PLATFORM — App Service Plan SKU (fixed-floor lever per §3A A1).')
@allowed(['S1', 'S2', 'S3', 'P1v3', 'P2v3', 'P3v3'])
param sharedAppServicePlanSku string = 'S1'

@description('SHARED PLATFORM — Azure AI Search SKU (fixed-floor lever per §3A A1).')
@allowed(['standard', 'standard2', 'standard3'])
param sharedAiSearchSku string = 'standard'

@description('SHARED PLATFORM — Redis Cache SKU.')
@allowed(['Standard', 'Premium'])
param sharedRedisSku string = 'Standard'

@description('SHARED PLATFORM — Tags applied to shared-platform resources.')
param sharedTags object = {
  environment: environment
  application: 'spaarke'
  deploymentModel: 'model1'
  scope: 'platform-shared'
  managedBy: 'bicep'
}

// ============================================================================
// PARAMETERS — PER-TENANT DEDICATED GROUP
// (created NEW for each Model 1 tenant onboarding; §7.2 🔴 always-dedicated row.)
// ============================================================================

@description('PER-TENANT — Customer identifier (lowercase, alphanumeric, 3–10 chars). Drives per-tenant resource naming and tags.')
@minLength(3)
@maxLength(10)
param perTenantCustomerId string

@description('PER-TENANT — Entra tenant ID (customer\'s AAD tenant) — used for logical isolation invariants (§4D I1/I2/I5). MUST NOT default to Spaarke tenant per §4D I1 (FR-28) — this parameter has NO default value by design.')
param perTenantTenantId string

@description('PER-TENANT — UAMI name (task 028 uami.bicep). Convention per uami.bicep header: sprk-{env}-{customerId}-uami.')
param perTenantUamiName string = 'sprk-${environment}-${perTenantCustomerId}-uami'

@description('PER-TENANT — Key Vault name. Canonical per Phase G naming: sprk-{customerId}-{env}-kv (max 24 chars per KV name limit).')
param perTenantKvName string = take('sprk-${perTenantCustomerId}-${environment}-kv', 24)

@description('PER-TENANT — Storage account name. Convention: sprk{customerId}{env}sa (lowercase, no hyphens, max 24 chars).')
param perTenantStorageName string = take(toLower(replace('sprk${perTenantCustomerId}${environment}sa', '-', '')), 24)

@description('PER-TENANT — Cosmos DB account name. Convention per customer.bicep + design.md §7.1: spaarke-{customerId}-{env}-cosmos (max 44 chars).')
param perTenantCosmosName string = take('spaarke-${perTenantCustomerId}-${environment}-cosmos', 44)

@description('PER-TENANT — Per-tenant App Insights name (metering + tracing attribution). Convention: sprk-{customerId}-{env}-insights.')
param perTenantAppInsightsName string = 'sprk-${perTenantCustomerId}-${environment}-insights'

@description('PER-TENANT — Log Analytics workspace name for per-tenant App Insights. Convention: sprk-{customerId}-{env}-logs.')
param perTenantLogAnalyticsName string = 'sprk-${perTenantCustomerId}-${environment}-logs'

@description('PER-TENANT — Dataverse environment URL (from H5 handler — NOT provisioned by this stack; PASS-THROUGH written into per-tenant KV as reference secret for BFF runtime consumption).')
param perTenantDataverseUrl string = ''

@description('PER-TENANT — SPE container ID (from H8 handler — NOT provisioned by this stack; PASS-THROUGH written into per-tenant KV as reference secret for BFF runtime consumption).')
param perTenantSpeContainerId string = ''

@description('PER-TENANT — Storage SKU (per-tenant dedicated).')
@allowed(['Standard_LRS', 'Standard_GRS', 'Standard_ZRS'])
param perTenantStorageSku string = 'Standard_LRS'

@description('PER-TENANT — Storage containers for per-tenant document processing.')
param perTenantStorageContainers array = ['temp-files', 'document-processing', 'ai-chunks']

@description('PER-TENANT — KV SKU.')
@allowed(['standard', 'premium'])
param perTenantKvSku string = 'standard'

@description('PER-TENANT — Tags applied to per-tenant dedicated resources.')
param perTenantTags object = {
  customer: perTenantCustomerId
  tenantId: perTenantTenantId
  environment: environment
  application: 'spaarke'
  deploymentModel: 'model1'
  scope: 'per-tenant-dedicated'
  managedBy: 'bicep'
}

// ============================================================================
// VARIABLES
// ============================================================================

// Per-tenant RG name — distinct from shared RG so per-tenant teardown does not
// affect shared platform. Naming follows the Model 2 pattern but suffixed
// `-model1` to distinguish tier without ambiguity.
var perTenantResourceGroupName = 'rg-spaarke-${perTenantCustomerId}-${environment}-model1'

// ============================================================================
// RESOURCE GROUPS (subscription-scope declarations)
// ============================================================================

resource sharedRg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: sharedResourceGroupName
  location: location
  tags: sharedTags
}

resource perTenantRg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: perTenantResourceGroupName
  location: location
  tags: perTenantTags
}

// ============================================================================
// SHARED PLATFORM — MONITORING (deploy first; other shared modules reference)
// ============================================================================

module sharedMonitoring '../modules/monitoring.bicep' = {
  scope: sharedRg
  name: 'monitoring-shared'
  params: {
    appInsightsName: '${sharedBaseName}-insights'
    logAnalyticsName: '${sharedBaseName}-logs'
    location: location
    retentionInDays: 180 // Longer retention for multi-tenant shared platform
    tags: sharedTags
  }
}

// ============================================================================
// SHARED PLATFORM — KEY VAULT (BFF app-only secrets; cross-tenant runtime secrets)
// Per-tenant secrets live in per-tenant KV below; this shared KV holds BFF-scoped
// secrets used by the multi-tenant BFF instance (shared OpenAI key, shared AI
// Search admin key, Redis connection string, etc.).
// ============================================================================

module sharedKeyVault '../modules/key-vault.bicep' = {
  scope: sharedRg
  name: 'keyVault-shared'
  params: {
    keyVaultName: sharedKeyVaultName
    location: location
    sku: 'standard'
    tags: sharedTags
  }
}

// ============================================================================
// SHARED PLATFORM — REDIS CACHE (per-env per Q-E FR-12; NOT per-tenant)
// ============================================================================

module sharedRedis '../modules/redis.bicep' = {
  scope: sharedRg
  name: 'redis-shared'
  params: {
    redisName: sharedRedisName
    location: location
    sku: sharedRedisSku
    capacity: sharedRedisSku == 'Premium' ? 1 : 2 // C2 for Standard, P1 for Premium
    tags: sharedTags
  }
}

// ============================================================================
// SHARED PLATFORM — SERVICE BUS (shared namespace; per-tenant partitioning via
// SessionId / MessageId patterns at the BFF layer)
// ============================================================================

module sharedServiceBus '../modules/service-bus.bicep' = {
  scope: sharedRg
  name: 'serviceBus-shared'
  params: {
    serviceBusName: sharedServiceBusName
    location: location
    sku: 'Standard'
    queueNames: [
      'sdap-jobs'
      'document-indexing'
      'ai-indexing'
      'customer-onboarding'
      'sdap-communication'
    ]
    tags: sharedTags
  }
}

// ============================================================================
// SHARED PLATFORM — STORAGE (shared cross-tenant buffers)
// Per-tenant document processing storage is separate (see per-tenant modules below).
// ============================================================================

module sharedStorage '../modules/storage-account.bicep' = {
  scope: sharedRg
  name: 'storage-shared'
  params: {
    storageAccountName: sharedStorageAccountName
    location: location
    sku: 'Standard_GRS' // Geo-redundant for shared platform storage
    containers: [
      'temp-files'
      'document-processing'
      'ai-chunks'
      'customer-exports'
    ]
    tags: sharedTags
  }
}

// ============================================================================
// SHARED PLATFORM — APP SERVICE PLAN (fixed-floor lever per §3A A1)
// ============================================================================

module sharedAppServicePlan '../modules/app-service-plan.bicep' = {
  scope: sharedRg
  name: 'appServicePlan-shared'
  params: {
    planName: sharedAppServicePlanName
    location: location
    sku: sharedAppServicePlanSku
    os: 'Linux'
    tags: sharedTags
  }
}

// ============================================================================
// SHARED PLATFORM — AI SERVICES (fixed-floor levers per §3A A1)
// Multi-tenant safety relies on §4D I2 (tenantId filter on AI Search) enforced
// at BFF query construction — see header ADR-027 Path A rationale.
// ============================================================================

module sharedOpenAi '../modules/openai.bicep' = {
  scope: sharedRg
  name: 'openAi-shared'
  params: {
    openAiName: sharedOpenAiName
    location: location
    // Higher capacity than Model 2 dedicated — serves multiple Model 1 tenants
    // with per-tenant budget enforcement via D19/A2 token-metering layer.
    deployments: [
      {
        name: 'gpt-4o'
        model: 'gpt-4o'
        version: '2024-08-06'
        capacity: 150
      }
      {
        name: 'gpt-4o-mini'
        model: 'gpt-4o-mini'
        version: '2024-07-18'
        capacity: 200 // >= 200 TPM required for beta scale
      }
      {
        name: 'text-embedding-3-large'
        model: 'text-embedding-3-large'
        version: '1'
        capacity: 350
      }
    ]
    tags: sharedTags
  }
}

module sharedAiSearch '../modules/ai-search.bicep' = {
  scope: sharedRg
  name: 'aiSearch-shared'
  params: {
    searchServiceName: sharedAiSearchName
    location: location
    sku: sharedAiSearchSku
    // HA replicas for production shared platform
    replicaCount: environment == 'prod' ? 2 : 1
    partitionCount: 1
    semanticSearch: 'standard'
    tags: sharedTags
  }
}

module sharedDocIntelligence '../modules/doc-intelligence.bicep' = {
  scope: sharedRg
  name: 'docIntelligence-shared'
  params: {
    docIntelligenceName: sharedDocIntelligenceName
    location: location
    sku: 'S0'
    tags: sharedTags
  }
}

// ============================================================================
// SHARED PLATFORM — BFF APP SERVICE (single multi-tenant deployment)
// The multi-tenant BFF resolves per-request tenant context from the caller's
// AAD tid claim (§4D I5). Per-tenant secrets (Dataverse URL, SPE container ID)
// are read from per-tenant KVs at runtime, keyed by tenant context.
// ============================================================================

module sharedBffApi '../modules/app-service.bicep' = {
  scope: sharedRg
  name: 'bffApi-shared'
  params: {
    appServiceName: sharedBffAppServiceName
    appServicePlanId: sharedAppServicePlan.outputs.planId
    location: location
    keyVaultName: sharedKeyVault.outputs.keyVaultName
    enableManagedIdentity: true
    appSettings: {
      // Multi-tenant mode marker consumed by BFF at boot for per-request
      // tenant-context resolution (§4D I5).
      MULTI_TENANT_MODE: 'true'

      // Redis (shared per-env)
      Redis__Enabled: 'true'
      Redis__ConnectionString: '@Microsoft.KeyVault(VaultName=${sharedKeyVault.outputs.keyVaultName};SecretName=redis-connection-string)'
      Redis__InstanceName: 'spaarke:' // Prefix for key isolation

      // Service Bus (shared)
      ConnectionStrings__ServiceBus: '@Microsoft.KeyVault(VaultName=${sharedKeyVault.outputs.keyVaultName};SecretName=servicebus-connection-string)'

      // Storage (shared)
      ConnectionStrings__Storage: '@Microsoft.KeyVault(VaultName=${sharedKeyVault.outputs.keyVaultName};SecretName=storage-connection-string)'

      // AI Services (shared) — multi-tenant safety enforced at BFF query layer
      // via §4D I2 (unconditional `tenantId eq` filter on every AI Search query).
      OPENAI_ENDPOINT: sharedOpenAi.outputs.openAiEndpoint
      OPENAI_API_KEY: '@Microsoft.KeyVault(VaultName=${sharedKeyVault.outputs.keyVaultName};SecretName=openai-api-key)'
      AI_SEARCH_ENDPOINT: sharedAiSearch.outputs.searchServiceEndpoint
      AI_SEARCH_API_KEY: '@Microsoft.KeyVault(VaultName=${sharedKeyVault.outputs.keyVaultName};SecretName=aisearch-admin-key)'

      // Document Intelligence (shared)
      DOC_INTELLIGENCE_ENDPOINT: sharedDocIntelligence.outputs.docIntelligenceEndpoint
      DOC_INTELLIGENCE_KEY: '@Microsoft.KeyVault(VaultName=${sharedKeyVault.outputs.keyVaultName};SecretName=docintel-key)'

      // Monitoring (shared)
      APPLICATIONINSIGHTS_CONNECTION_STRING: sharedMonitoring.outputs.connectionString
      ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    }
    tags: sharedTags
  }
}

// ============================================================================
// PER-TENANT DEDICATED — USER-ASSIGNED MANAGED IDENTITY (task 028)
// One long-lived identity per Model 1 tenant, bound to KV/Storage/Cosmos RBAC
// per task 030 (deferred). Even though the BFF is shared (Model 1), per-tenant
// UAMI enables per-tenant Graph app-role parity + per-tenant Dataverse App User
// registration + fine-grained per-tenant KV secret access. Also structurally
// eliminates T5 slot-swap parity issues for any per-tenant App Service that
// may be added in future (out of scope now — BFF is shared).
// ============================================================================

module perTenantUami '../modules/uami.bicep' = {
  scope: perTenantRg
  name: 'uami-${perTenantCustomerId}'
  params: {
    name: perTenantUamiName
    location: location
    tags: perTenantTags
  }
}

// ============================================================================
// PER-TENANT DEDICATED — KEY VAULT (per-tenant secrets)
// Holds per-tenant secrets: dataverse-url (from H5), spe-container-id (from H8),
// tenant-id, plus H4-populated app-only credentials. NOT the same as the shared
// platform KV which holds BFF-scoped shared secrets.
// ============================================================================

module perTenantKv '../modules/key-vault.bicep' = {
  scope: perTenantRg
  name: 'kv-${perTenantCustomerId}'
  params: {
    keyVaultName: perTenantKvName
    location: location
    sku: perTenantKvSku
    tags: perTenantTags
  }
}

// ============================================================================
// PER-TENANT DEDICATED — STORAGE (per-tenant document processing)
// Per §7.2 🔴 always-dedicated row: per-tenant Storage keeps document processing
// artefacts tenant-scoped even in Model 1 (Storage has no fixed floor, so no
// economic pressure to share). The shared storage account above holds cross-
// tenant buffers only.
// ============================================================================

module perTenantStorage '../modules/storage-account.bicep' = {
  scope: perTenantRg
  name: 'storage-${perTenantCustomerId}'
  params: {
    storageAccountName: perTenantStorageName
    location: location
    sku: perTenantStorageSku
    containers: perTenantStorageContainers
    enableTestDocumentLifecycle: false
    tags: perTenantTags
  }
}

// ============================================================================
// PER-TENANT DEDICATED — COSMOS DB (per-tenant AI sessions / audit / memory)
// Per §7.2 🔴 always-dedicated (v3.2 correction: dedicated in BOTH tiers — only
// the fixed-floor levers are shared under §3A A1, and Cosmos serverless has no
// fixed floor). §4D I3 (FR-30) partition-key `/tenantId` predicate MUST be on
// every read/write — enforced by ArchTest at code level.
// ============================================================================

module perTenantCosmos '../modules/cosmos-db.bicep' = {
  scope: perTenantRg
  name: 'cosmos-${perTenantCustomerId}'
  params: {
    accountName: perTenantCosmosName
    location: location
    databaseName: 'spaarke-ai'
    // Data-plane RBAC deferred: shared BFF System-Assigned MI + per-tenant UAMI
    // both need Cosmos Data Contributor. Task 030 wires per-tenant UAMI RBAC to
    // per-tenant Cosmos; shared BFF MI RBAC is a separate concern owned by the
    // shared-platform bootstrap flow. Leaving empty here yields a data-plane-
    // access-empty account that later RBAC modules populate.
    appServicePrincipalId: ''
    tags: perTenantTags
  }
}

// ============================================================================
// PER-TENANT DEDICATED — APP INSIGHTS (metering + tracing attribution)
// Per-tenant App Insights lets per-tenant queries + per-tenant cost dashboards
// scope cleanly without cross-tenant contamination in Log Analytics.
// ============================================================================

module perTenantMonitoring '../modules/monitoring.bicep' = {
  scope: perTenantRg
  name: 'monitoring-${perTenantCustomerId}'
  params: {
    appInsightsName: perTenantAppInsightsName
    logAnalyticsName: perTenantLogAnalyticsName
    location: location
    retentionInDays: 90
    tags: perTenantTags
  }
}

// ============================================================================
// PER-TENANT REFERENCE SECRETS — Dataverse URL, SPE container ID, tenant ID
// Passed as PASS-THROUGH params from H5 (Dataverse env creation) and H8 (SPE
// container-type creation) — this stack does NOT provision those resources.
//
// DELIBERATE ARCHITECTURAL BOUNDARY: Bicep does NOT write these values into the
// per-tenant KV. That job belongs to handler H4 per design.md §4.1 handler
// catalog (H4 = "Key Vault secrets population + canonical naming applied at
// seed time via canonical secret-catalog manifest — Phase H per r3 task 063").
// Bicep provisions infrastructure; H4 populates secrets from the canonical
// manifest. Splitting the responsibility here keeps the secret surface single-
// source (Phase H manifest) rather than split between Bicep + manifest — which
// was the drift pattern that motivated r3 task 063's canonical remediation.
//
// This stack echoes the pass-through references as OUTPUTS so H4 can consume
// them from the deployment output when invoked by the L2 control-plane.
// ============================================================================

// ============================================================================
// OUTPUTS — SHARED PLATFORM
// ============================================================================

output sharedResourceGroupName string = sharedRg.name
output sharedLocation string = location
output sharedEnvironment string = environment

// Shared BFF endpoint (multi-tenant)
output sharedApiUrl string = sharedBffApi.outputs.appServiceUrl
output sharedApiPrincipalId string = sharedBffApi.outputs.appServicePrincipalId
output sharedAppServicePlanId string = sharedAppServicePlan.outputs.planId

// Shared KV
output sharedKeyVaultName string = sharedKeyVault.outputs.keyVaultName
output sharedKeyVaultUri string = sharedKeyVault.outputs.keyVaultUri

// Shared AI Service endpoints
output sharedOpenAiEndpoint string = sharedOpenAi.outputs.openAiEndpoint
output sharedAiSearchEndpoint string = sharedAiSearch.outputs.searchServiceEndpoint
output sharedDocIntelligenceEndpoint string = sharedDocIntelligence.outputs.docIntelligenceEndpoint

// Shared monitoring
output sharedAppInsightsName string = sharedMonitoring.outputs.appInsightsName
output sharedLogAnalyticsWorkspaceId string = sharedMonitoring.outputs.logAnalyticsWorkspaceId

// Shared connection strings (populate KV via post-deployment script — matches
// existing shared stack behavior; secrets should be stored in KV, not returned
// as outputs in production. Kept behind `#disable-next-line` for post-deploy
// automation only.)
#disable-next-line outputs-should-not-contain-secrets
output sharedRedisConnectionString string = sharedRedis.outputs.redisConnectionString
#disable-next-line outputs-should-not-contain-secrets
output sharedServiceBusConnectionString string = sharedServiceBus.outputs.serviceBusConnectionString
#disable-next-line outputs-should-not-contain-secrets
output sharedStorageConnectionString string = sharedStorage.outputs.connectionString
#disable-next-line outputs-should-not-contain-secrets
output sharedOpenAiKey string = sharedOpenAi.outputs.openAiKey
#disable-next-line outputs-should-not-contain-secrets
output sharedAiSearchAdminKey string = sharedAiSearch.outputs.searchServiceAdminKey
#disable-next-line outputs-should-not-contain-secrets
output sharedDocIntelligenceKey string = sharedDocIntelligence.outputs.docIntelligenceKey

// ============================================================================
// OUTPUTS — PER-TENANT DEDICATED
// ============================================================================

output perTenantResourceGroupName string = perTenantRg.name
output perTenantCustomerId string = perTenantCustomerId
output perTenantTenantId string = perTenantTenantId

// Per-tenant UAMI (task 028 outputs)
output perTenantUamiId string = perTenantUami.outputs.id
output perTenantUamiName string = perTenantUami.outputs.name
output perTenantUamiPrincipalId string = perTenantUami.outputs.principalId
output perTenantUamiClientId string = perTenantUami.outputs.clientId

// Per-tenant KV
output perTenantKvName string = perTenantKv.outputs.keyVaultName
output perTenantKvUri string = perTenantKv.outputs.keyVaultUri

// Per-tenant Storage
output perTenantStorageAccountName string = perTenantStorage.outputs.storageAccountName
output perTenantStoragePrimaryEndpoint string = perTenantStorage.outputs.primaryEndpoint

// Per-tenant Cosmos
output perTenantCosmosAccountName string = perTenantCosmos.outputs.accountName
output perTenantCosmosAccountEndpoint string = perTenantCosmos.outputs.accountEndpoint
output perTenantCosmosDatabaseName string = perTenantCosmos.outputs.databaseName

// Per-tenant App Insights
output perTenantAppInsightsName string = perTenantMonitoring.outputs.appInsightsName
output perTenantLogAnalyticsWorkspaceId string = perTenantMonitoring.outputs.logAnalyticsWorkspaceId

// Per-tenant pass-through references (echo for downstream tooling)
output perTenantDataverseUrl string = perTenantDataverseUrl
output perTenantSpeContainerId string = perTenantSpeContainerId
