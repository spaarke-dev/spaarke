// infrastructure/bicep/customer.bicep
// Per-customer Bicep template for Spaarke production environment
// Deploys isolated data resources into a dedicated customer resource group.
// The ONLY customer-stamp template (owner D19, task 249): deployed by L2 handler H2a (ArmDeploymentRunner)
// into the customer's own subscription, for new stamps and upgrades alike.
//
// Resources deployed:
//   - Storage Account (temp files, document processing)
//   - Key Vault (customer-specific secrets)
//   - Service Bus namespace (job queues)
//   - Azure Managed Redis (per-customer cache, Microsoft Entra only — see REDIS CACHE below)
//
// Note (Redis): every customer stamp has its own Redis (D-12, 2026-09-28: Redis access control is per
// instance, so a shared cache cannot separate customers). Since task 242 (owner D12/D13, 2026-09-30) it is
// Azure Managed Redis Balanced_B0 with high availability, access keys disabled, reached by the stamp UAMI
// through an access-policy assignment. No Redis key, connection string or Key Vault secret exists for a stamp.

targetScope = 'subscription'

// ============================================================================
// PARAMETERS
// ============================================================================

// CUSTOMER IDENTIFIER — the canonical standard. See
// docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "The customerId standard" for the derivation.
//
// 🔴 THE LIMIT IS 8, NOT 10, AND IT IS DERIVED — NOT A PREFERENCE. THIS file composes its Key Vault name as
// take(format('sprk-{0}-{1}-kv', customerId, environmentName), 24). Key Vault names are 3-24 chars and MAY
// NOT END IN A HYPHEN. With environmentName = 'staging' (the longest allowed value):
//     len  8 -> 'sprk-xxxxxxxx-staging-kv'  (24) complete
//     len  9 -> 'sprk-xxxxxxxxx-staging-k'  (24) truncated, loses the 'v'
//     len 10 -> 'sprk-xxxxxxxxxx-staging-'  (24) INVALID — trailing hyphen, Azure REJECTS it
// So the previous @maxLength(10) admitted a value that FAILS TO DEPLOY. take() hid it: the name was
// silently shortened rather than the deployment refusing, so the error surfaced from Azure, not from here.
//
// 🔴 THE CHARACTER RULE CANNOT BE ENFORCED HERE. Bicep/ARM has NO regex constraint on parameters — there
// is no @pattern decorator, and this repo has no bicepconfig.json enabling the experimental assertions
// feature. Only the LENGTH is enforceable at this layer, and it is, above.
//
// That matters because the character rule is not cosmetic: the storage-account name strips hyphens
// (replace(...,'-','')), so 'acme-x' and 'acmex' resolve to the SAME storage account with nothing
// validating it. The rule is therefore enforced where the value is ASSIGNED — at provisioning intake —
// and this parameter documents it so the two cannot drift apart silently.
//
// Leading letter is a READABILITY convention, not an Azure rule — the 'sprk' prefix already satisfies the
// platform's start-character requirements.
@description('Customer identifier. 3-8 chars, lowercase letters and digits, starting with a letter. Drives all resource naming; the 8-char limit comes from the Key Vault name composed below (see AZURE-RESOURCE-NAMING-CONVENTION.md).')
@minLength(3)
@maxLength(8)
param customerId string

@description('Environment name')
@allowed(['dev', 'staging', 'prod'])
param environmentName string = 'prod'

@description('Primary Azure region for all customer resources')
param location string = 'westus2'

@description('Azure region for Azure OpenAI deployment. Defaults to westus3 per canonical Spaarke strategy: westus2 platform services + westus3 OpenAI (see operator memory reference_azure_fresh_sub_regional_gotchas). Split-region is intentional: westus3 has richer OpenAI catalog + higher frontier-tier TPM; westus2 has richer platform-service SKUs. Cross-region OpenAI adds ~15-25ms per call (negligible vs AI inference time) and ~5-15 dollars per month egress for trial customers (rounding error for production). Override to co-locate ONLY when data-residency or single-region compliance requires it.')
param openAiLocation string = 'westus3'

@description('Azure region for the Azure AI Content Safety account (task 246). It must offer BOTH Prompt Shields and Groundedness Detection; per the Microsoft region table (2026-09-18) the US regions with both are westus, eastus, eastus2 and canadaeast. westus2 (the stamp default location) has no Groundedness Detection, so this does NOT follow `location`: default westus, the nearest such region to westus2 (keeps the Prompt Shield call inside its deadline). Split-region like openAiLocation.')
param contentSafetyLocation string = 'westus'

// --- Storage Account options ---

@description('SKU for the customer Storage Account')
@allowed(['Standard_LRS', 'Standard_GRS', 'Standard_ZRS'])
param storageSku string = 'Standard_LRS'

@description('Blob containers to create in the customer storage account')
param storageContainers array = ['temp-files', 'document-processing', 'ai-chunks']

// --- Key Vault options ---

@description('Key Vault SKU')
@allowed(['standard', 'premium'])
param keyVaultSku string = 'standard'

// --- Service Bus options ---

@description('Service Bus SKU')
@allowed(['Basic', 'Standard', 'Premium'])
param serviceBusSku string = 'Standard'

@description('Service Bus queue names to create')
param serviceBusQueues array = ['sdap-jobs', 'document-indexing', 'ai-indexing', 'sdap-communication']

@description('Principal ID of the platform BFF App Service Managed Identity (granted Sender on membership topic + Receiver on recon subscription per R3 D3 / FR-2P2.3). Leave empty to skip RBAC assignment — operator must grant manually.')
param bffPrincipalId string = ''

@description('Principal ID of the fleet-scoped L2 control-plane UAMI (sprk-controlplane-{env}-uami, provisioned by infrastructure/bicep/platform-controlplane.bicep). REQUIRED for the per-customer BFF Website Contributor grant (customer-provisioning-orchestration-r1 task 203b, punch list row A21 / task 201 Deferred #1): the L2 Worker`s H4b handler fetches Kudu docker logs from this customer`s BFF App Service and the H9 handler zip-deploys BFF artifacts to the same site -- both operations require Website Contributor. H2a (ArmDeploymentRunner) sends it for Model 1 stamps only (task 249): a Model 2 stamp is in the customer\'s tenant, where a role assignment cannot name a Spaarke-tenant principal, and L2 reaches it through its Lighthouse delegation. Empty skips the grant.')
param controlPlaneUamiPrincipalId string = ''

// --- Optional SignalR (per ADR-032 Null-Object Kill-Switch pattern; ADR-034 realtime spine) ---

@description('Deploy the per-customer Azure SignalR Service resource for the notifications spine (ADR-034). Default false — no resource + downstream BFF resolves the Null-Object variant per ADR-032. Set true to provision the resource; requires the BFF Notifications:SignalRSpine:Enabled flag to be true in the same environment for end-to-end enablement.')
param signalrEnabled bool = false

@description('SignalR SKU (ignored when signalrEnabled=false). Default Free_F1 for scaffold + dev; production customers requiring realtime bump to Standard_S1 (~$48/mo/unit per notes/pricing-research-2026-08-12.md).')
@allowed(['Free_F1', 'Standard_S1', 'Premium_P1'])
param signalrSku string = 'Free_F1'

// --- ACS messaging options (messaging-communication-app-r1, task 012, FR-18) ---

@description('Deploy the per-boundary ACS resource + Event Grid system topic/subscription (messaging). Default false — existing customer provisioning is unchanged until messaging is enabled for the boundary.')
param deployAcsMessaging bool = false

@description('ACS data location for this boundary. IMMUTABLE at create time (design §8.7 / D-01) — residency is achieved by a separate ACS resource per boundary. Choose deliberately at onboarding.')
param acsDataLocation string = 'UnitedStates'

@description('BFF inbound webhook URL the Event Grid chat-event subscription delivers to (task 030 ingress). Required when deployAcsMessaging is true.')
param acsWebhookEndpointUrl string = ''

// --- App Service options (Phase C — customer-provisioning-orchestration-r1, task 127) ---

@description('SKU for the BFF App Service Plan. Default S1 (Standard) per design.md §7.2 Resource Catalog row 7.')
@allowed(['B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P1v3', 'P2v3', 'P3v3'])
param appServiceSku string = 'S1'

// --- Azure Managed Redis options (task 242, owner D12) ---

@description('Azure Managed Redis SKU for the per-customer cache. Owner D12: Balanced_B0. Size up only on a measured memory metric — Managed Redis has no scale-down.')
@allowed(['Balanced_B0', 'Balanced_B1', 'Balanced_B3', 'Balanced_B5', 'Balanced_B10'])
param redisSkuName string = 'Balanced_B0'

@description('High availability for the per-customer cache. Owner D12: Enabled. Fixed at create time — changing it on an existing stamp is rejected by Azure.')
@allowed(['Enabled', 'Disabled'])
param redisHighAvailability string = 'Enabled'

// --- Tags ---

// task 249 (D19, 2026-10-02): `createdDate: utcNow(...)` was dropped from the default. A `utcNow()`
// parameter default is evaluated at EVERY deployment (not compile time), so on an UPGRADE run the tag
// value changes on every deploy even though nothing about the customer or environment changed — the
// upgrade what-if then reports a spurious tag Modify on every tagged resource (ArmWhatIfDriftDetector /
// T225a CR-1 pattern). There is no Bicep-only way to compute a value that is genuinely stable "since
// first deploy" without an external input this template does not have, and ARM already records creation
// time (`systemData.createdAt` on most resource types; `createdTime` via `$expand=createdTime` on resource
// listings) — this tag added nothing the platform doesn't expose, while actively causing drift. Dropped, not
// replaced. One-time effect: the first upgrade of a stamp deployed before task 249 sees a tag-removal Modify.
@description('Tags applied to ALL resources for cost tracking and management')
param tags object = {
  customer: customerId
  environment: environmentName
  application: 'spaarke'
  managedBy: 'bicep'
}

// ============================================================================
// VARIABLES
// ============================================================================

// Resource group name follows naming standard: rg-spaarke-{customerId}-{env}
var resourceGroupName = 'rg-spaarke-${customerId}-${environmentName}'

// Base name for resource naming: sprk{customer}{env}
var baseName = 'sprk${customerId}${environmentName}'

// Storage account: sprk{customer}{env}sa (lowercase, no hyphens, max 24 chars)
var storageAccountName = take(toLower(replace('${baseName}sa', '-', '')), 24)

// Key Vault: sprk-{customer}-{env}-kv (max 24 chars).
// Canonical per AZURE-RESOURCE-NAMING-CONVENTION.md § "KV-Secret & Resource Naming
// Standard" (R3). Dev exception: `spaarke-spekvcert` is a DO-NOT-RENAME live
// dev-artifact per projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md
// (owner directive #3 · FR-35 · §7.9 R3). Task 018 parameterizes vault-name to
// allow the dev exception to be honored via caller override at deployment time.
@description('Customer Key Vault name. Canonical per-customer form incorporates customerId per AZURE-RESOURCE-NAMING-CONVENTION.md § "Multi-Customer Prod Environment" (customer + env combo isolates per-customer vaults; capped at 24 chars per Key Vault limit). Parameterized by task 018 (was hardcoded `var keyVaultName`) so H4 handler + Phase H seeder address vaults deterministically. Override supported for codified exceptions per task 020 (see naming-exception-registry.md).')
param keyVaultName string = take('sprk-${customerId}-${environmentName}-kv', 24)

// Service Bus: spaarke-{customer}-{env}-sbus (Note: '-sb' suffix is reserved by Azure)
var serviceBusName = 'spaarke-${customerId}-${environmentName}-sbus'

// ACS resource: sprk-{customer}-{env}-acs (per boundary; data location immutable — D-01)
var acsResourceName = 'sprk-${customerId}-${environmentName}-acs'

// Cosmos DB account: spaarke-{customer}-{env}-cosmos (per-customer; max 44 chars per naming convention)
// Serverless SQL API; hosts the `spaarke-ai` database (sessions/prompts/audit/memory/feedback) per
// docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md. BFF is per-customer's dedicated data plane.
var cosmosAccountName = take('spaarke-${customerId}-${environmentName}-cosmos', 44)

// SignalR resource: sprk-{customer}-{env}-signalr (per design.md §7.1 naming convention).
// Only referenced when signalrEnabled=true (ADR-032 Null-Object kill-switch caller-side gate).
var signalrName = 'sprk-${customerId}-${environmentName}-signalr'

// Azure OpenAI: sprk-{customer}-{env}-openai (per design.md §7.1 naming convention).
var openAiName = 'sprk-${customerId}-${environmentName}-openai'

// AI Search: sprk-{customer}-{env}-search (per design.md §7.1 naming convention).
var searchServiceName = 'sprk-${customerId}-${environmentName}-search'

// App Insights: sprk-{customer}-{env}-insights (per design.md §7.1 naming convention).
var appInsightsName = 'sprk-${customerId}-${environmentName}-insights'

// Log Analytics workspace: sprk-{customer}-{env}-logs (per design.md §7.1 naming convention).
var logAnalyticsName = 'sprk-${customerId}-${environmentName}-logs'

// Document Intelligence: sprk-{customer}-{env}-docintel (per design.md §7.1 naming convention).
var docIntelligenceName = 'sprk-${customerId}-${environmentName}-docintel'

// Azure AI Content Safety: sprk-{customer}-{env}-contentsafety (task 246; also the custom subdomain).
var contentSafetyName = 'sprk-${customerId}-${environmentName}-contentsafety'

// Azure Managed Redis: sprk-{customer}-{env}-redis (task 128b naming; Managed Redis since task 242).
var redisCacheName = 'sprk-${customerId}-${environmentName}-redis'

// Dead-letter blob container for the ACS Event Grid subscription (task 012 / §8.3).
var acsDeadLetterContainerName = 'acs-eventgrid-deadletter'

// When messaging is enabled for the boundary, ensure the dead-letter container exists in the
// customer Storage account (reuse — §11 default-to-reuse; no separate storage account).
var effectiveStorageContainers = deployAcsMessaging ? union(storageContainers, [acsDeadLetterContainerName]) : storageContainers

// ============================================================================
// RESOURCE GROUP
// ============================================================================

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ============================================================================
// USER-ASSIGNED MANAGED IDENTITY (Phase C — customer-provisioning-orchestration-r1,
// task 127; module authored by task 028)
// ONE stable per-customer identity bound to BOTH the production App Service and
// its staging slot (below), so a slot-swap does not rotate downstream KV/Storage/
// Cosmos/Graph/Dataverse App-User grants (T5 structural fix). Declared before Key
// Vault + App Service per design.md §7.6 Deployment Order steps 2/4/9/14 — both
// consume `uami.outputs.principalId` / `uami.outputs.id` for RBAC + identity binding.
// ============================================================================

module uami 'modules/uami.bicep' = {
  scope: rg
  name: 'uami-${baseName}'
  params: {
    name: 'mi-spaarke-${customerId}-${environmentName}'
    location: location
    tags: tags
  }
}

// ============================================================================
// KEY VAULT (Deploy first - other resources store secrets here)
// ============================================================================

module keyVault 'modules/key-vault.bicep' = {
  scope: rg
  name: 'keyVault-${baseName}'
  params: {
    keyVaultName: keyVaultName
    location: location
    sku: keyVaultSku
    // T5 structural fix (task 127): grant Key Vault Secrets User to the stable
    // per-customer UAMI (uami.bicep, task 028) via key-vault.bicep's existing
    // `userAssignedIdentityPrincipalId` param (task 030 wiring point).
    userAssignedIdentityPrincipalId: uami.outputs.principalId
    // task 249 (D19, 2026-10-02): wire audit-log diagnostics to the stamp's OWN Log Analytics
    // workspace (`monitoring` module below) via key-vault.bicep's existing `logAnalyticsWorkspaceId`
    // param. It takes the workspace's ARM RESOURCE ID (`logAnalyticsId`) — NOT monitoring's
    // `logAnalyticsWorkspaceId` output, which is the workspace's customerId GUID and makes the
    // diagnostic setting fail at deploy time. The module already declares the diagnosticSettings resource (conditional on this being
    // non-empty); this caller simply never supplied it. Referencing `monitoring.outputs.*` here creates
    // an implicit dependency (keyVault after monitoring) regardless of declaration order — no cycle,
    // since monitoring does not reference keyVault.
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsId
    tags: tags
  }
}

// ============================================================================
// MONITORING (App Insights + Log Analytics) — Phase C — customer-provisioning-
// orchestration-r1, task 128b. Single module (modules/monitoring.bicep) emits
// BOTH the Log Analytics workspace AND the App Insights instance wired to it via
// `WorkspaceResourceId`. Per design.md §7.2 row 12 / §7.6 Deployment Order step 3
// -- placed immediately after Key Vault (closest available position to the
// documented early placement without reordering already-shipped modules). No
// UAMI RBAC param -- App Insights auth is connection-string/instrumentation-key
// based, not MI-based, so there is nothing to grant. No `retentionInDays`
// override passed -- module default (90) already matches design.md.
// ============================================================================

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring-${baseName}'
  params: {
    appInsightsName: appInsightsName
    logAnalyticsName: logAnalyticsName
    location: location
    tags: tags
  }
}

// ============================================================================
// STORAGE ACCOUNT (Temp files, document processing)
// ============================================================================

module storage 'modules/storage-account.bicep' = {
  scope: rg
  name: 'storage-${baseName}'
  params: {
    storageAccountName: storageAccountName
    location: location
    sku: storageSku
    containers: effectiveStorageContainers
    enableTestDocumentLifecycle: false
    // G-8 Batch 1 defect #13: grant the per-customer UAMI Storage Blob Data
    // Contributor (ba92f5b4-2d11-453d-a403-e96b0029c9fe) on this account. The
    // invocation previously passed neither principal param, so the module's
    // RBAC blocks never fired and blob access relied solely on the KV
    // account-key fallback. Same T5-stable UAMI pattern as openAi/aiSearch/
    // docIntelligence below.
    userAssignedIdentityPrincipalId: uami.outputs.principalId
    // Keyless (owner D13, task 244): no shared-key access. Any caller uses the blob endpoint with an
    // identity — the stamp UAMI holds Storage Blob Data Contributor (above); Event Grid dead-letters
    // with its system topic's identity (acs-communication.bicep).
    disableSharedKeyAccess: true
    tags: tags
  }
}

// ============================================================================
// SERVICE BUS (Job queues for async processing)
// ============================================================================

module serviceBus 'modules/service-bus.bicep' = {
  scope: rg
  name: 'serviceBus-${baseName}'
  params: {
    serviceBusName: serviceBusName
    location: location
    sku: serviceBusSku
    queueNames: serviceBusQueues
    tags: tags
  }
}

// ============================================================================
// COSMOS DB (Per-customer AI platform state — Wave C2 prep, task 014)
// Per spec §5.3 + FR-04 + R11 + § MUST rules: Cosmos MUST be per-customer (BFF prereq —
// BFF will not start without it, R11). Unconditional invocation (no feature gate).
// (Wave C2's multi-stack plan is moot: this is the template H2a deploys for both models (D-12;
// Model 1 since task 228); task 225a retired stacks/model1-shared.bicep.)
// Redis is per-customer too (see the REDIS CACHE section below + the header note). It is
// grouped with the other supporting-infra resources after AI Search per §7.6.
// Database + containers + RBAC (Data Contributor for BFF MI) are owned by the module.
// ============================================================================

module cosmosDb 'modules/cosmos-db.bicep' = {
  scope: rg
  name: 'cosmos-${baseName}'
  params: {
    accountName: cosmosAccountName
    location: location
    databaseName: 'spaarke-ai'
    appServicePrincipalId: bffPrincipalId
    // G-8 Batch 1 defect #12: the UAMI is the BFF's actual runtime identity
    // (ADR-028 DefaultAzureCredential over UAMI). `bffPrincipalId` defaults ''
    // and H2a never passes it — without this grant the module's sqlRoleAssignment
    // never fires and the BFF 403s on every Cosmos data-plane call at runtime
    // (deploy stays green). Module grants Cosmos DB Built-in Data Contributor
    // (data-plane role 00000000-0000-0000-0000-000000000002) via sqlRoleAssignments.
    userAssignedIdentityPrincipalId: uami.outputs.principalId
    tags: tags
  }
}

// ============================================================================
// AZURE OPENAI (Phase C — customer-provisioning-orchestration-r1, task 128;
// module authored by task 046). Per design.md §7.2 row 9 / §7.6 Deployment
// Order step 10 — UAMI (task 127's `uami` module) granted Cognitive Services
// User RBAC (built-in role a97b65f3-24c7-4388-baec-2e87135dc908) via the
// module's existing `userAssignedIdentityPrincipalId` param. NO `deployments`
// override is passed — the module's own default array is the stamp set
// (task 247: DataZoneStandard gpt-4o 150, gpt-4o-mini → gpt-4.1-mini 200,
// text-embedding-3-large 350), mirrored by L2's PinnedModelCatalog and checked
// against this template by ArmTemplateInspectorTests. `openAiEndpoint` output name is
// LOAD-BEARING — ArmDeploymentRunner.MapOutputs (task 123) reads it exactly.
// ============================================================================

module openAi 'modules/openai.bicep' = {
  scope: rg
  name: 'openAi-${baseName}'
  params: {
    openAiName: openAiName
    location: openAiLocation
    sku: 'S0'
    userAssignedIdentityPrincipalId: uami.outputs.principalId
    tags: tags
  }
}

// ============================================================================
// AI SEARCH (Phase C — customer-provisioning-orchestration-r1, task 128;
// module authored by task 046). Per design.md §7.2 row 10 / §7.6 Deployment
// Order step 11 — UAMI granted Cognitive Services User RBAC per task 030
// POML constraint (c); see ai-search.bicep's own header note for the
// documented N1 caveat (role is functionally dormant on Microsoft.Search —
// not fixed here, honors the literal spec/design instruction as the module
// already does). Module defaults used for sku/replicaCount/partitionCount/
// semanticSearch — task 124's H2b completion notes confirm the real
// SearchIndexClientProvisioner authenticates via the UAMI-pinned
// TokenCredential (zero admin-key handling) and needs no infra shape beyond
// the service endpoint; index creation is H2b's job (SearchIndexClient via
// Deploy-AllIndexes.ps1's catalog), not this Bicep phase. `aiSearchEndpoint`
// output name is LOAD-BEARING — ArmDeploymentRunner.MapOutputs (task 123)
// reads it exactly.
// ============================================================================

module aiSearch 'modules/ai-search.bicep' = {
  scope: rg
  name: 'aiSearch-${baseName}'
  params: {
    searchServiceName: searchServiceName
    location: location
    userAssignedIdentityPrincipalId: uami.outputs.principalId
    tags: tags
  }
}

// ============================================================================
// DOCUMENT INTELLIGENCE (Phase C — customer-provisioning-orchestration-r1,
// task 128b; module authored by task 030). Per design.md §7.2 row 11 / §7.6
// Deployment Order step 12 -- placed immediately after AI Search (still before
// Membership Topic), grouping with the other AI resources per design.md §7.6's
// 10-11-12 (OpenAI -> AI Search -> DocIntel) adjacency. UAMI granted Cognitive
// Services User RBAC (built-in role a97b65f3-24c7-4388-baec-2e87135dc908) via
// the module's existing `userAssignedIdentityPrincipalId` param, same pattern
// task 128 wired for openai.bicep/ai-search.bicep. `docIntelligenceEndpoint`
// output name is LOAD-BEARING -- ArmDeploymentRunner.MapOutputs (task 123)
// reads it exactly. The module has no key output (T243: the BFF uses the stamp UAMI).
// ============================================================================

module docIntelligence 'modules/doc-intelligence.bicep' = {
  scope: rg
  name: 'docIntelligence-${baseName}'
  params: {
    docIntelligenceName: docIntelligenceName
    location: location
    sku: 'S0'
    userAssignedIdentityPrincipalId: uami.outputs.principalId
    tags: tags
  }
}

// ============================================================================
// AZURE AI CONTENT SAFETY (task 246, plan G26)
// The stamp's own safety perimeter for the BFF's Prompt Shield + groundedness checks. Before this,
// a stamp had no Content Safety resource and the BFF fell back to a hard-coded dev endpoint.
// Keyless like its siblings (owner D13): custom subdomain + local auth disabled; the UAMI holds
// Cognitive Services User via the module. `contentSafetyEndpoint` output name is LOAD-BEARING --
// ArmDeploymentRunner.MapOutputs reads it into InterStepState for H4b.
// ============================================================================

module contentSafety 'modules/content-safety.bicep' = {
  scope: rg
  name: 'contentSafety-${baseName}'
  params: {
    contentSafetyName: contentSafetyName
    location: contentSafetyLocation
    sku: 'S0'
    userAssignedIdentityPrincipalId: uami.outputs.principalId
    tags: tags
  }
}

// ============================================================================
// REDIS CACHE — Azure Managed Redis, Microsoft Entra only (task 242, owner D12/D13)
// Balanced_B0 with high availability by default; one database ('default', port 10000,
// OSSCluster, AllKeysLRU) with access keys DISABLED. The stamp UAMI is the only identity
// with a data-access policy: the BFF runs as the UAMI (ADR-028) and connects with
// Microsoft.Azure.StackExchangeRedis over RESP3 using `Redis__Endpoint` (host:10000, a
// plain app setting — not a secret). Public endpoint, matching Cosmos DB / OpenAI /
// AI Search in this file (no VNet module here).
// ============================================================================

module redisCache 'modules/redis.bicep' = {
  scope: rg
  name: 'redisCache-${baseName}'
  params: {
    redisName: redisCacheName
    location: location
    skuName: redisSkuName
    highAvailability: redisHighAvailability
    accessPolicyPrincipalIds: [
      uami.outputs.principalId
    ]
    tags: tags
  }
}

// ============================================================================
// MEMBERSHIP TOPIC (R3 Phase 2 — D3 / FR-2P2.3)
// Topic + subscription for membership-change events, with BFF MI Sender+Receiver RBAC.
// ============================================================================

module membershipTopic 'modules/membership-topic.bicep' = {
  scope: rg
  name: 'membershipTopic-${baseName}'
  params: {
    serviceBusNamespaceName: serviceBusName
    bffPrincipalId: bffPrincipalId
  }
  dependsOn: [
    serviceBus
  ]
}

// ============================================================================
// ACS MESSAGING (messaging-communication-app-r1 — task 012 / FR-18)
// Per-boundary ACS resource + Event Grid system topic + chat-event subscription
// -> BFF webhook + dead-letter Storage. EXTENSION of the ADR-027 per-customer
// orchestrator (mirrors membership-topic module), gated per boundary.
// Data location is IMMUTABLE at create (D-01) — the residency mechanism.
// ============================================================================

module acsCommunication 'modules/acs-communication.bicep' = if (deployAcsMessaging) {
  scope: rg
  name: 'acs-${baseName}'
  params: {
    acsResourceName: acsResourceName
    acsDataLocation: acsDataLocation
    webhookEndpointUrl: acsWebhookEndpointUrl
    deadLetterStorageAccountResourceId: storage.outputs.storageAccountId
    deadLetterContainerName: acsDeadLetterContainerName
    tags: tags
  }
  dependsOn: [
    storage
  ]
}

// ============================================================================
// SIGNALR (OPTIONAL — per ADR-032 Null-Object Kill-Switch pattern)
// Per-customer Azure SignalR Service for the ADR-034 notifications spine.
//   - Feature-gated on `signalrEnabled` (default false). When false, NO SignalR
//     resource is deployed AND the BFF DI container resolves the Null-Object
//     variant (per ADR-032 P3 Fail-fast Null-Object).
//   - When true, provisions the resource + grants the stamp's BFF identity (the per-customer
//     UAMI, `uami.outputs.principalId` — task 249) the built-in "SignalR App Server" role.
//   - `signalrEnabled=true` in Bicep is the *caller-side* half of the switch; the
//     BFF-side half is the `Notifications:SignalRSpine:Enabled` config flag. Both
//     must be true for end-to-end realtime; either false = feature disabled with
//     no dangling resource + no client-side crash.
// ============================================================================

module signalr 'modules/signalr.bicep' = if (signalrEnabled) {
  scope: rg
  name: 'signalr-${baseName}'
  params: {
    signalrName: signalrName
    location: location
    signalrSku: signalrSku
    // task 249 (D19, 2026-10-02): the stamp's BFF runs AS the UAMI (ADR-028 — no separate "BFF
    // principal" exists; `bffPrincipalId` above defaults '' and H2a never passes it, so the role
    // assignment this module makes conditional on a non-empty principal never fired). Pass the stamp
    // UAMI's principalId — the same identity bffRuntimeRbac/cosmosDb/openAi/aiSearch already grant.
    // Key auth stays disabled (signalr.bicep's own `disableLocalAuth: true` default — unchanged).
    bffPrincipalId: uami.outputs.principalId
    tags: tags
  }
}

// ============================================================================
// APP SERVICE PLAN + APP SERVICE (BFF) + STAGING SLOT
// (Phase C — customer-provisioning-orchestration-r1, task 127; modules authored
// by tasks 029/028). UAMI-only identity (ADR-028 — no co-emitted SA-MI per the
// anti-pattern app-service.bicep's header eliminates) bound to BOTH the
// production App Service and its staging slot via the SAME
// `uami.outputs.id`, so a slot-swap does not rotate the effective identity
// (T5 structural fix). `keyVaultReferenceIdentity` PATCH on both slots is H4's
// post-deploy job (ArmAppServiceIdentityPatcher, task 125, already shipped) —
// this Bicep section only binds the UAMI to `identity.userAssignedIdentities`.
// Per design.md §7.6 Deployment Order steps 9 (plan) / 14 (App Service).
// ============================================================================

module appServicePlan 'modules/app-service-plan.bicep' = {
  scope: rg
  name: 'appServicePlan-${baseName}'
  params: {
    planName: 'sprk-${customerId}-${environmentName}-plan'
    location: location
    sku: appServiceSku
    os: 'Linux'
    tags: tags
  }
}

module bffApi 'modules/app-service.bicep' = {
  scope: rg
  name: 'bffApi-${baseName}'
  params: {
    appServiceName: 'sprk-${customerId}-${environmentName}-api'
    appServicePlanId: appServicePlan.outputs.planId
    location: location
    userAssignedIdentityResourceId: uami.outputs.id
    // G-8 Batch 1 defect #14: this invocation previously passed ZERO appSettings
    // — the Model 2 BFF booted with no config and no AZURE_CLIENT_ID UAMI pin,
    // so DefaultAzureCredential could not resolve the UAMI and the H9 health
    // probe 404'd post-zip-deploy. Mirrored the (since retired, task 225a)
    // model1-shared.bicep sharedBffApi pattern, adapted per-customer:
    //   - KV references target the CUSTOMER vault using the CANONICAL secret
    //     names written by the kvSecrets module below (kv-secrets.generated.bicep
    //     / manifest.yaml) — NOT the legacy lowercase names the retired shared
    //     stack carried for Redis/ServiceBus/Storage.
    //   - Only secrets in this file's resolvable kvSecretValues set get KV refs. An
    //     unresolvable KV ref surfaces the literal @Microsoft.KeyVault(...) string as the
    //     setting value, which the BFF would then send as a key.
    //   - T226 (owner 2026-09-30): OpenAI, AI Search and Service Bus are reached with
    //     the stamp UAMI (ADR-028) - no key setting is emitted for them. The UAMI holds
    //     Cognitive Services User on OpenAI and Data Sender/Receiver + Index
    //     Data/Service Contributor on Service Bus + AI Search (bffRuntimeRbac below).
    //     H4b adds the MI selectors (ServiceBus__FullyQualifiedNamespace,
    //     AiSearch__ManagedIdentity__Enabled). Storage's connection string was unused
    //     by any BFF code and is not emitted. Since T244 (G16 closed) every one of these
    //     services has local/key auth DISABLED (Storage: shared key off), so the UAMI is
    //     the only way in — a key setting could not work even if one were emitted.
    //   - Document Intelligence is reached with the stamp UAMI too (T243, owner D13): the
    //     BFF uses managed identity when no DocumentIntelligence__DocIntelKey is set, and no
    //     stamp is given one. The module sets a custom subdomain (Entra needs it) and grants
    //     the UAMI Cognitive Services User. H4b emits DocumentIntelligence__Enabled=true
    //     (the BFF's AI master switch) and the endpoint.
    //   - KV references resolve only after H4 PATCHes keyVaultReferenceIdentity
    //     to the UAMI on both slots (ArmAppServiceIdentityPatcher, task 125) and
    //     the kvSecrets module has written real values. No ARM dependsOn needed:
    //     KV refs are runtime-resolved strings (and kvSecrets depends on THIS
    //     module for Communication-WebhookUrl — a dependsOn here would cycle).
    appSettings: {
      // Customer runtime identity (D-14 / unified-access-control-r2 task 123). customerId names this
      // whole resource group and everything in it; before this setting, no line of BFF code could read
      // it. D-12 established that tenantId is IDENTICAL for every Model 1 customer, so tenant-keyed
      // controls separate Entra tenants rather than customers — this is the runtime handle on the
      // boundary the infrastructure already draws.
      //
      // The BFF can also DERIVE this from WEBSITE_RESOURCE_GROUP (which App Service sets automatically
      // and which is literally rg-spaarke-{customerId}-{env}), so an older stamp without this setting
      // still works. Emitting it explicitly is nonetheless correct: the derived path logs a WARNING by
      // design, because a stamp running on the fallback forever is a stamp whose settings were never
      // finished. Absent BOTH, the BFF refuses to start rather than defaulting to a shared value.
      Customer__Id: customerId

      // UAMI pin — DefaultAzureCredential resolves this client ID (ADR-028 / T5).
      AZURE_CLIENT_ID: uami.outputs.clientId
      ManagedIdentity__ClientId: uami.outputs.clientId

      // Redis (per-customer Azure Managed Redis, Entra only — task 242). The BFF authenticates
      // with the UAMI (ManagedIdentity__ClientId above); there is no Redis key or connection
      // string. H4b writes the same Redis__Endpoint value (per_env_settings, from H2a's output).
      Redis__Enabled: 'true'
      Redis__Endpoint: redisCache.outputs.redisEndpoint
      Redis__InstanceName: 'spaarke:' // Prefix for key isolation

      // AI Services — endpoints direct from sibling-module outputs; auth is the stamp
      // UAMI for OpenAI and AI Search (see header note above).
      OPENAI_ENDPOINT: openAi.outputs.openAiEndpoint
      AI_SEARCH_ENDPOINT: aiSearch.outputs.searchServiceEndpoint

      // Document Intelligence (per-customer, task 128b)
      DOC_INTELLIGENCE_ENDPOINT: docIntelligence.outputs.docIntelligenceEndpoint

      // Content Safety (per-customer, task 246). The BFF refuses to start outside Development/Testing
      // without this setting; it authenticates with the UAMI (H4b also sets
      // AiSafety__ContentSafety__ManagedIdentity__Enabled=true and re-applies this endpoint from H2a's output).
      AiSafety__ContentSafety__Endpoint: contentSafety.outputs.contentSafetyEndpoint

      // Monitoring (per-customer App Insights, task 128b)
      APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.connectionString
      ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    }
    tags: tags
  }
}

module bffApiSlot 'modules/app-service-slot.bicep' = {
  scope: rg
  name: 'bffApiSlot-${baseName}'
  params: {
    appServiceName: bffApi.outputs.appServiceName
    slotName: 'staging'
    location: location
    appServicePlanId: appServicePlan.outputs.planId
    userAssignedIdentityResourceId: uami.outputs.id
    tags: tags
  }
}

// ============================================================================
// L2 CONTROL-PLANE UAMI -- Website Contributor on the per-customer BFF App
// Service (customer-provisioning-orchestration-r1 task 203b, punch list row A21
// / task 201 "Deferred #1"). Enables H4b Kudu docker-log fetch + H9 zip-deploy
// from the L2 Worker. Plus Search Service Contributor + Search Index Data
// Contributor on the stamp AI Search service for H2b (task 244, G16 -- the
// service has local auth disabled). Split into modules/customer-l2-bff-rbac.bicep because
// this stack (targetScope='subscription') cannot inline RG-scoped role
// assignments (BCP139) -- same pattern as modules/bff-runtime-rbac.bicep.
// ============================================================================

module customerL2BffRbac 'modules/customer-l2-bff-rbac.bicep' = {
  scope: rg
  name: 'l2-bff-rbac-${baseName}'
  params: {
    controlPlaneUamiPrincipalId: controlPlaneUamiPrincipalId
    // Implicit dependency on bffApi via bffApi.outputs.appServiceName -- no
    // explicit dependsOn needed (BCP linter rule no-unnecessary-dependson).
    bffAppServiceName: bffApi.outputs.appServiceName
    searchServiceName: aiSearch.outputs.searchServiceName
  }
}

// ============================================================================
// BFF RUNTIME UAMI -- MI-ONLY RBAC on per-customer Service Bus + AI Search
// (auth-v4 PROVISIONING-CHANGE-REQUEST §10.1 Δ1 + Δ2 + §10.3; punch rows
//  A36 + A37)
//
// Grants the per-customer BFF runtime UAMI (uami) the four data-plane role
// assignments required by the auth-v4 §10.2 live contract on the per-customer
// stamp's Service Bus namespace + AI Search service:
//   A36 Service Bus:  Data Sender + Data Receiver
//   A37 AI Search:    Index Data Contributor + Service Contributor
//
// Same BCP139 forcing-function as customerL2BffRbac above; invoked with
// `scope: rg`. Different principal from A20 (Model 1 L2 UAMI grants) --
// this is the per-customer BFF's own runtime identity, not the L2 provisioning
// UAMI.
// ============================================================================

module bffRuntimeRbac 'modules/bff-runtime-rbac.bicep' = {
  scope: rg
  name: 'bff-runtime-rbac-${baseName}'
  params: {
    bffUamiPrincipalId: uami.outputs.principalId
    serviceBusNamespaceName: serviceBus.outputs.serviceBusName
    searchServiceName: aiSearch.outputs.searchServiceName
  }
}

// NOTE (2026-08-25, A37 dispatch reconciling A36 concurrent-add):
//   A36's agent landed after A37 and added a REDUNDANT second invocation of
//   the SAME module here (`bffRuntimeSbRbac`) with the SAME deployment name
//   (`bff-runtime-rbac-${baseName}`) as `bffRuntimeRbac` above -- would have
//   ARM-errored at deploy time on duplicate deployment name + was also missing
//   the required `searchServiceName` param. The single invocation above
//   already binds A36 SB roles + A37 Search roles together
//   (bff-runtime-rbac.bicep is comprehensive by design per the coordination
//   instruction in the A37 dispatch). A36's block deleted here; A36 role
//   coverage preserved unchanged.

// ============================================================================
// KEY VAULT SECRETS (canonical secret catalog) — Phase C — customer-provisioning-
// orchestration-r1, task 129. Invokes scripts/canonical-secret-catalog/generated/
// kv-secrets.generated.bicep (task 084 -- DO NOT EDIT BY HAND; generated from
// scripts/canonical-secret-catalog/manifest.yaml) to WRITE REAL VALUES onto the
// customer Key Vault for every canonical secret this Bicep composition can
// genuinely resolve from its own sibling-module outputs. Per task-126-deviations.md
// Deviation #3: H4's SecretClientKvWriter checks secret EXISTENCE on the vault
// (not ARM deployment outputs) for FromBicepOutput entries -- this module call is
// therefore the actual value-writer H4 depends on to no-op/succeed on these
// entries instead of failing QuarantineRequired on a fresh customer.
//
// Resolvable (5) -- direct sibling-module output references:
//   AiSearch-Endpoint, AppInsights-ConnectionString, AzureOpenAI-Endpoint,
//   Communication-WebhookUrl, DocumentIntelligence-Endpoint
//
// REMOVED FROM THE PROCESS (T226, owner 2026-09-30) -- the BFF reaches these services
// with the stamp UAMI, so no key is written to the vault for them:
//   AiSearch--AdminKey, ServiceBus-ConnectionString, AzureOpenAI-ApiKey;
//   Storage-ConnectionString (no BFF reader at all); DocumentIntelligence-ApiKey (T243);
//   Redis-ConnectionString (T242: Azure Managed Redis with access keys disabled — the BFF
//     connects with the UAMI using the plain Redis__Endpoint app setting).
//
// Deliberately OMITTED (2) -- never fabricated; each has a documented reason +
// recommended resolution path (honest-signal discipline, root CLAUDE.md §6.5):
//   (No SPE secrets remain. T227c / G18: SPE-DefaultContainerId and
//    SPE-CommunicationArchiveContainerId left the catalog — the customer's container is
//    created at runtime by H8 and reaches the BFF as plain app settings H4b writes from
//    H8's output. T227e: SPE-ContainerTypeId left too — nothing read it; the BFF reads
//    the container type as the plain H4b setting SharePointEmbedded__ContainerTypeId.)
//   BFF-API-ClientId, BFF-API-Audience
//     -> H3 creates the per-customer BFF app-registration at RUNTIME and writes
//        ClientId/Audience to this vault itself (manifest value_source
//        `written-by-h3`, task 245a; H4 skips them). No Bicep resource produces
//        these values; this composition correctly has nothing to contribute.
//   RunContextContractTests rule (g) checks this map against the manifest's
//   from-bicep-output entries in both directions.
// ============================================================================

var kvSecretValues = {
  'AiSearch-Endpoint': aiSearch.outputs.searchServiceEndpoint
  'AppInsights-ConnectionString': monitoring.outputs.connectionString
  'AzureOpenAI-Endpoint': openAi.outputs.openAiEndpoint
  'Communication-WebhookUrl': '${bffApi.outputs.appServiceUrl}/api/communications/incoming-webhook'
  'DocumentIntelligence-Endpoint': docIntelligence.outputs.docIntelligenceEndpoint
}

module kvSecrets '../../scripts/canonical-secret-catalog/generated/kv-secrets.generated.bicep' = {
  scope: rg
  name: 'kvSecrets-${baseName}'
  params: {
    keyVaultName: keyVaultName
    secretValues: kvSecretValues
  }
  dependsOn: [
    keyVault
  ]
}

// ============================================================================
// OUTPUTS
// ============================================================================

// --- Resource identifiers ---
output resourceGroupName string = rg.name
output customerId string = customerId
output location string = location

// --- Key Vault ---
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output keyVaultId string = keyVault.outputs.keyVaultId

// --- Storage Account ---
output storageAccountName string = storage.outputs.storageAccountName
output storagePrimaryEndpoint string = storage.outputs.primaryEndpoint
// No storage connection-string output (task 244): shared-key access is disabled.

// --- Service Bus ---
output serviceBusName string = serviceBus.outputs.serviceBusName
output serviceBusEndpoint string = serviceBus.outputs.serviceBusEndpoint
// No Service Bus connection-string output (task 244): local (SAS) auth is disabled.

// --- Cosmos DB (task 014 Wave C2 prep — per-customer AI platform state) ---
output cosmosAccountName string = cosmosDb.outputs.accountName
output cosmosAccountId string = cosmosDb.outputs.accountId
output cosmosAccountEndpoint string = cosmosDb.outputs.accountEndpoint
output cosmosDatabaseName string = cosmosDb.outputs.databaseName

// --- Azure OpenAI (task 128 / Phase C). Output name is LOAD-BEARING:
// ArmDeploymentRunner.MapOutputs (task 123) reads this exact name to populate
// BicepDeployOutputs.OpenAiEndpoint. There is no key: local auth is disabled
// (task 244) and the stamp BFF uses its UAMI. ---
output openAiEndpoint string = openAi.outputs.openAiEndpoint

// --- AI Search (task 128 / Phase C). Output name is LOAD-BEARING:
// ArmDeploymentRunner.MapOutputs (task 123) reads this exact name to populate
// BicepDeployOutputs.AiSearchEndpoint. There is no key: local auth is disabled
// (task 244); the BFF UAMI and the L2 UAMI hold Search roles. ---
output aiSearchEndpoint string = aiSearch.outputs.searchServiceEndpoint

// --- Document Intelligence (task 128b / Phase C). Output name is LOAD-BEARING:
// ArmDeploymentRunner.MapOutputs (task 123) reads this exact name to populate
// BicepDeployOutputs.DocIntelligenceEndpoint. There is no key output: the stamp BFF
// reaches Document Intelligence with its UAMI (T243). ---
output docIntelligenceEndpoint string = docIntelligence.outputs.docIntelligenceEndpoint
output docIntelligenceName string = docIntelligence.outputs.docIntelligenceName

// --- Azure AI Content Safety (task 246). Output name is LOAD-BEARING: ArmDeploymentRunner.MapOutputs
// reads it into BicepDeployOutputs.ContentSafetyEndpoint -> InterStepState -> H4b's
// AiSafety__ContentSafety__Endpoint. No key output: local auth is disabled. ---
output contentSafetyEndpoint string = contentSafety.outputs.contentSafetyEndpoint
output contentSafetyName string = contentSafety.outputs.contentSafetyName

// --- Monitoring: App Insights + Log Analytics (task 128b / Phase C). Raw
// `connectionString`/`instrumentationKey` are intentionally NOT echoed here —
// flows through a future kv-secrets wiring task instead (task 129 territory). ---
output appInsightsName string = monitoring.outputs.appInsightsName
output appInsightsId string = monitoring.outputs.appInsightsId
output logAnalyticsName string = monitoring.outputs.logAnalyticsName
output logAnalyticsWorkspaceId string = monitoring.outputs.logAnalyticsWorkspaceId

// --- Azure Managed Redis (task 242). Output name `redisEndpoint` is LOAD-BEARING:
// ArmDeploymentRunner.MapOutputs reads it into BicepDeployOutputs.RedisEndpoint → InterStepState →
// H4b's Redis__Endpoint. Not a secret (host:port); the cache has no keys. ---
output redisName string = redisCache.outputs.redisName
output redisHostName string = redisCache.outputs.redisHostName
output redisPort int = redisCache.outputs.redisPort
output redisEndpoint string = redisCache.outputs.redisEndpoint

// --- Membership topic (R3 Phase 2) ---
output membershipTopicName string = membershipTopic.outputs.topicName
output membershipReconSubscriptionName string = membershipTopic.outputs.subscriptionName

// --- ACS messaging (task 012 / FR-18) — populated only when deployAcsMessaging=true ---
output acsMessagingDeployed bool = deployAcsMessaging
output acsResourceId string = deployAcsMessaging ? acsCommunication.outputs.acsResourceId : ''
output acsHostName string = deployAcsMessaging ? acsCommunication.outputs.acsHostName : ''
output acsDataLocation string = deployAcsMessaging ? acsCommunication.outputs.acsDataLocation : ''
output acsSystemTopicName string = deployAcsMessaging ? acsCommunication.outputs.systemTopicName : ''
output acsEventSubscriptionName string = deployAcsMessaging ? acsCommunication.outputs.eventSubscriptionName : ''

// --- SignalR (optional; task 027 / ADR-032 / ADR-034) — populated only when signalrEnabled=true ---
// Uses Bicep null-safe access + coalesce to satisfy BCP318 on conditional module outputs.
output signalrEnabled bool = signalrEnabled
output signalrResourceId string = signalr.?outputs.signalrId ?? ''
output signalrHostName string = signalr.?outputs.signalrHostName ?? ''
output signalrSkuDeployed string = signalr.?outputs.signalrSku ?? ''

// --- User-Assigned Managed Identity (task 127 / Phase C) — real values, not a pass-through.
// Output names are LOAD-BEARING: ArmDeploymentRunner.MapOutputs (task 123) reads these exact
// names to populate BicepDeployOutputs.UserAssignedIdentity{ResourceId,ObjectId,ClientId}. ---
output userAssignedIdentityResourceId string = uami.outputs.id
output userAssignedIdentityObjectId string = uami.outputs.principalId
output userAssignedIdentityClientId string = uami.outputs.clientId

// --- App Service (BFF) — task 127 / Phase C. Output names are LOAD-BEARING: ArmDeploymentRunner.MapOutputs
// (task 123) reads these exact names to populate BicepDeployOutputs.AppServiceName / AppServiceStagingSlotName. ---
output appServiceName string = bffApi.outputs.appServiceName
output appServiceStagingSlotName string = bffApiSlot.outputs.slotName
