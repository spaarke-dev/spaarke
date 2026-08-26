// infrastructure/bicep/customer.bicep
// Per-customer Bicep template for Spaarke production environment
// Deploys isolated data resources into a dedicated customer resource group.
// Run once per customer onboarding via Provision-Customer.ps1.
//
// Resources deployed:
//   - Storage Account (temp files, document processing)
//   - Key Vault (customer-specific secrets)
//   - Service Bus namespace (job queues)
//
// Note (UPDATED 2026-08-19, task 128b -- E2 reconciliation): per-customer Redis
// WAS deprecated per Q-E Architecture 1 / FR-12 (spaarke-redis-cache-remediation-r1
// + r2, which removed this template's Redis module call in r2 task 020). Owner
// reconciliation (2026-08-19): this template is confirmed (task 129 background) to
// be the SOLE template deployed for the Model2Dedicated branch, where env=customer
// 1:1 -- so "per-environment" and "per-customer" are the same unit for THIS
// template. modules/redis.bicep is wired unconditionally below (see REDIS CACHE
// section) as the per-environment Redis for that customer's dedicated environment.
// Model 1 (shared/trial) Redis is UNAFFECTED -- it remains per-env-shared via
// scripts/Deploy-RedisCache.ps1 and has no code path through this file. See
// spec.md v3.6 FR-04 / § MUST Rules and design.md v3.6 §7.2 for the Model 1 vs
// Model 2 distinction this reconciliation introduced.

targetScope = 'subscription'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Customer identifier (lowercase, alphanumeric only). Drives all resource naming.')
@minLength(3)
@maxLength(10)
param customerId string

@description('Environment name')
@allowed(['dev', 'staging', 'prod'])
param environmentName string = 'prod'

@description('Primary Azure region for all customer resources')
param location string = 'westus2'

@description('Name of the platform Key Vault (from platform.bicep deployment) for cross-references. Canonical: sprk-{env}-kv per docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "KV-Secret & Resource Naming Standard" R3 + spec.md §7.9 / FR-35 (task 018 drops legacy `-platform-` qualifier from default; matches platform.bicep keyVaultName default). Override supported for codified exceptions per task 020.')
param platformKeyVaultName string = 'sprk-${environmentName}-kv'

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

@description('Principal ID of the fleet-scoped L2 control-plane UAMI (sprk-controlplane-{env}-uami, provisioned by infrastructure/bicep/platform-controlplane.bicep). REQUIRED for the per-customer BFF Website Contributor grant (customer-provisioning-orchestration-r1 task 203b, punch list row A21 / task 201 Deferred #1): the L2 Worker`s H4b handler fetches Kudu docker logs from this customer`s BFF App Service and the H9 handler zip-deploys BFF artifacts to the same site -- both operations require Website Contributor. Empty default skips the grant (what-if isolation only); real per-customer deploys MUST supply the L2 UAMI principalId.')
param controlPlaneUamiPrincipalId string = ''

// --- Optional SignalR (per ADR-032 Null-Object Kill-Switch pattern; ADR-034 realtime spine) ---

@description('Deploy the per-customer Azure SignalR Service resource for the notifications spine (ADR-034). Default false — no resource + downstream BFF resolves the Null-Object variant per ADR-032. Set true to provision the resource; requires the BFF Notifications:SignalRSpine:Enabled flag to be true in the same environment for end-to-end enablement.')
param signalrEnabled bool = false

@description('SignalR SKU (ignored when signalrEnabled=false). Default Free_F1 for scaffold + dev; production customers requiring realtime bump to Standard_S1 (~$48/mo/unit per notes/pricing-research-2026-08-12.md).')
@allowed(['Free_F1', 'Standard_S1', 'Premium_P1'])
param signalrSku string = 'Free_F1'

// --- Secret-free identity gate (auth-v4 §9.1 / customer-provisioning-orchestration-r1 punch row A38b, 2026-08-25) ---

@description('When true, OMIT `AiSearch--AdminKey` and `ServiceBus-ConnectionString` from the per-customer KV kvSecretValues map (auth-v4 §9.1 sentinel-free contract; A38b re-scope 2026-08-25; the downstream `kv-secrets.generated.bicep` skip-if-absent guard fires when the key is absent). Default false preserves current behavior for pre-migration envs.')
param requireSecretFreeIdentity bool = false

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

// --- Redis Cache options (Phase C — customer-provisioning-orchestration-r1, task 128b;
// E2 reconciliation — per-customer Redis for Model2Dedicated, see header note) ---

@description('SKU for the per-customer Redis Cache. Default Basic (dev-cost-optimized, ~$15/mo) per redis-dev.bicepparam precedent — single overridable default, not environment-conditional Bicep logic; override via CLI --parameters for staging/prod, matching appServiceSku default S1 being overridden the same way.')
@allowed(['Basic', 'Standard', 'Premium'])
param redisSku string = 'Basic'

@description('SKU capacity (family size) for the per-customer Redis Cache. Default 0 (Basic C0, cheapest tier) per redis-dev.bicepparam precedent.')
param redisCapacity int = 0

// --- Tags ---

@description('Tags applied to ALL resources for cost tracking and management')
param tags object = {
  customer: customerId
  environment: environmentName
  application: 'spaarke'
  managedBy: 'bicep'
  createdDate: utcNow('yyyy-MM-dd')
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

// Redis Cache: sprk-{customer}-{env}-redis (task 128b / E2 reconciliation -- not yet a
// canonical design.md §7.1 row; matches the existing SignalR/OpenAI/AI Search naming
// shape used elsewhere in this file. See design.md v3.6 §7.1 amendment.)
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
// Wave C2 (task 032) will refactor into the multi-stack composition (model1-shared /
// model2-full); this scaffold ensures the module is wired so C2 lands cleanly.
// Redis IS now provisioned per-customer (task 128b, E2 reconciliation) -- see the
// REDIS CACHE section below + the updated header note. Redis is not co-located
// with Cosmos DB in this file; it is grouped with the other supporting-infra
// resources (Document Intelligence + Monitoring) after AI Search per §7.6.
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
// override is passed — the module's own default array (gpt-4o:150,
// gpt-4o-mini:200, spaarke-gpt4o-mini:30, text-embedding-3-large:350) is the
// exact spec.md FR-01 / NFR-12 TPM budget and design.md §7.4's 4-row table,
// byte-for-byte verified consistent. `openAiEndpoint` output name is
// LOAD-BEARING — ArmDeploymentRunner.MapOutputs (task 123) reads it exactly.
// ============================================================================

module openAi 'modules/openai.bicep' = {
  scope: rg
  name: 'openAi-${baseName}'
  params: {
    openAiName: openAiName
    location: location
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
// reads it exactly. Raw `docIntelligenceKey` is intentionally NOT echoed here.
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
// REDIS CACHE (Phase C — customer-provisioning-orchestration-r1, task 128b;
// module authored by spaarke-redis-cache-remediation-r1 task 020, FR-09
// hardened). Per the owner's E2 reconciliation (2026-08-19; see the updated
// header note above): this template is confirmed to be the SOLE template
// deployed for the Model2Dedicated branch, where "per-environment" and
// "per-customer" are the same unit -- so modules/redis.bicep is wired
// UNCONDITIONALLY (no feature-gate param), matching Cosmos DB's unconditional-
// invocation precedent in this file. Model 1 (shared/trial) is NOT affected --
// it has no code path through this file and continues to use the per-env-
// shared Redis via scripts/Deploy-RedisCache.ps1. `redisSku`/`redisCapacity`
// default to 'Basic'/0 (dev-appropriate cost posture per redis-dev.bicepparam
// precedent, same pattern as `appServiceSku`'s single overridable default --
// staging/prod override at deploy time via CLI `--parameters`, not env-
// conditional Bicep logic). No UAMI RBAC param -- Redis auth is access-key
// based, not MI-based. No `subnetId`/`staticIP` override -- this file has no
// VNet module; public network access matches Cosmos DB / OpenAI / AI Search's
// own public-endpoint posture here. Raw `redisPrimaryKey`/`redisConnectionString`
// are intentionally NOT echoed as top-level outputs (secret-output-hygiene
// precedent from task 128) -- future task-129-style kv-secrets wiring can
// reference `redis.outputs.*` symbolically in-file.
// ============================================================================

module redisCache 'modules/redis.bicep' = {
  scope: rg
  name: 'redisCache-${baseName}'
  params: {
    redisName: redisCacheName
    location: location
    sku: redisSku
    capacity: redisCapacity
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
//   - When true, provisions the resource + grants the BFF Managed Identity the
//     built-in "SignalR App Server" role (only when bffPrincipalId is non-empty).
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
    bffPrincipalId: bffPrincipalId
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
    // probe 404'd post-zip-deploy. Mirrors the model1-shared.bicep sharedBffApi
    // pattern, adapted per-customer:
    //   - KV references target the CUSTOMER vault using the CANONICAL secret
    //     names written by the kvSecrets module below (kv-secrets.generated.bicep
    //     / manifest.yaml) — NOT the legacy lowercase names model1-shared still
    //     carries for Redis/ServiceBus/Storage.
    //   - Only secrets in this file's resolvable kvSecretValues set get KV refs.
    //     OPENAI_API_KEY (AzureOpenAI-ApiKey, value_source=from-run-parameter) is
    //     deliberately OMITTED: an unresolvable KV ref surfaces the literal
    //     @Microsoft.KeyVault(...) string as the setting value and would be sent
    //     as an API key. Absent the setting, the BFF falls back to
    //     DefaultAzureCredential (MI) per ADR-028 — and the UAMI already holds
    //     Cognitive Services User on the OpenAI resource (openAi module above).
    //   - KV references resolve only after H4 PATCHes keyVaultReferenceIdentity
    //     to the UAMI on both slots (ArmAppServiceIdentityPatcher, task 125) and
    //     the kvSecrets module has written real values. No ARM dependsOn needed:
    //     KV refs are runtime-resolved strings (and kvSecrets depends on THIS
    //     module for Communication-WebhookUrl — a dependsOn here would cycle).
    appSettings: {
      // UAMI pin — DefaultAzureCredential resolves this client ID (ADR-028 / T5).
      AZURE_CLIENT_ID: uami.outputs.clientId
      ManagedIdentity__ClientId: uami.outputs.clientId

      // Redis (per-customer, task 128b)
      Redis__Enabled: 'true'
      Redis__ConnectionString: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Redis-ConnectionString)'
      Redis__InstanceName: 'spaarke:' // Prefix for key isolation

      // Service Bus (per-customer)
      ConnectionStrings__ServiceBus: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=ServiceBus-ConnectionString)'

      // Storage (per-customer)
      ConnectionStrings__Storage: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=Storage-ConnectionString)'

      // AI Services — endpoints direct from sibling-module outputs; admin key via
      // canonical KV ref. OpenAI auth is MI-only here (see header note above).
      OPENAI_ENDPOINT: openAi.outputs.openAiEndpoint
      AI_SEARCH_ENDPOINT: aiSearch.outputs.searchServiceEndpoint
      AI_SEARCH_API_KEY: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=AiSearch--AdminKey)'

      // Document Intelligence (per-customer, task 128b)
      DOC_INTELLIGENCE_ENDPOINT: docIntelligence.outputs.docIntelligenceEndpoint
      DOC_INTELLIGENCE_KEY: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=DocumentIntelligence-ApiKey)'

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
// from the L2 Worker. Split into modules/customer-l2-bff-rbac.bicep because
// this stack (targetScope='subscription') cannot inline RG-scoped role
// assignments (BCP139) -- same pattern as modules/model1-shared-l2-rbac.bicep
// for the Model 1 tier.
// ============================================================================

module customerL2BffRbac 'modules/customer-l2-bff-rbac.bicep' = {
  scope: rg
  name: 'l2-bff-rbac-${baseName}'
  params: {
    controlPlaneUamiPrincipalId: controlPlaneUamiPrincipalId
    // Implicit dependency on bffApi via bffApi.outputs.appServiceName -- no
    // explicit dependsOn needed (BCP linter rule no-unnecessary-dependson).
    bffAppServiceName: bffApi.outputs.appServiceName
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
// Resolvable (10) -- direct sibling-module output references:
//   AiSearch--AdminKey, AiSearch-Endpoint, AppInsights-ConnectionString,
//   AzureOpenAI-Endpoint, Communication-WebhookUrl, DocumentIntelligence-ApiKey,
//   DocumentIntelligence-Endpoint, Redis-ConnectionString,
//   ServiceBus-ConnectionString, Storage-ConnectionString
//
// Secret-free-gated (2 of the above 10) -- `AiSearch--AdminKey` +
// `ServiceBus-ConnectionString` are OMITTED (never sentinel-valued) from the
// map when `requireSecretFreeIdentity=true` (customer-provisioning-orchestration-r1
// punch row A38b, 2026-08-25; auth-v4 §9.1 sentinel-free contract). Omitting
// the key -- not writing a placeholder -- is what makes the existing
// `if (contains(secretValues, ...))` skip-if-absent guard in
// kv-secrets.generated.bicep effective on secret-free stamps. Default false
// keeps today's behavior bit-identical for pre-migration environments.
//
// Deliberately OMITTED (5) -- never fabricated; each has a documented reason +
// recommended resolution path (honest-signal discipline, root CLAUDE.md §6.5):
//   SPE-ContainerTypeId, SPE-DefaultContainerId, SPE-CommunicationArchiveContainerId
//     -> H8/H9 RUNTIME outputs (SPE container-type creation + 24h replication);
//        no ARM-deploy-time value exists. Resolved at runtime via H4's
//        FromRunParameters path after H8/H9 execute (expected, not a failure).
//        Recommended owner: H8/H9 handler authors (Wave G-3, tasks 131/132).
//   BFF-API-ClientId, BFF-API-Audience
//     -> H3 (task 130) creates the per-customer BFF app-registration at RUNTIME
//        and writes ClientId/Audience to RunParameters.Secrets. manifest.yaml
//        reclassified these from FromBicepOutput to FromRunParameters (task 129
//        step 6, owner E3 2026-08-19) -- no Bicep resource produces these
//        values; this Bicep composition correctly has nothing to contribute
//        here. Recommended owner: H3 handler author (Wave G-3, task 130).
// ============================================================================

var kvSecretValuesBase = {
  'AiSearch-Endpoint': aiSearch.outputs.searchServiceEndpoint
  'AppInsights-ConnectionString': monitoring.outputs.connectionString
  'AzureOpenAI-Endpoint': openAi.outputs.openAiEndpoint
  'Communication-WebhookUrl': '${bffApi.outputs.appServiceUrl}/api/communications/incoming-webhook'
  'DocumentIntelligence-ApiKey': docIntelligence.outputs.docIntelligenceKey
  'DocumentIntelligence-Endpoint': docIntelligence.outputs.docIntelligenceEndpoint
  'Redis-ConnectionString': redisCache.outputs.redisConnectionString
  'Storage-ConnectionString': storage.outputs.connectionString
}

// requireSecretFreeIdentity=true -> {} (both keys OMITTED, never sentinel-valued);
// requireSecretFreeIdentity=false (default) -> both keys present, bit-identical
// to pre-A38b behavior. See requireSecretFreeIdentity param @description above.
var kvSecretValuesGated = requireSecretFreeIdentity ? {} : {
  'AiSearch--AdminKey': aiSearch.outputs.searchServiceAdminKey
  'ServiceBus-ConnectionString': serviceBus.outputs.serviceBusConnectionString
}

var kvSecretValues = union(kvSecretValuesBase, kvSecretValuesGated)

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
#disable-next-line outputs-should-not-contain-secrets
output storageConnectionString string = storage.outputs.connectionString

// --- Service Bus ---
output serviceBusName string = serviceBus.outputs.serviceBusName
output serviceBusEndpoint string = serviceBus.outputs.serviceBusEndpoint
#disable-next-line outputs-should-not-contain-secrets
output serviceBusConnectionString string = serviceBus.outputs.serviceBusConnectionString

// --- Cosmos DB (task 014 Wave C2 prep — per-customer AI platform state) ---
output cosmosAccountName string = cosmosDb.outputs.accountName
output cosmosAccountId string = cosmosDb.outputs.accountId
output cosmosAccountEndpoint string = cosmosDb.outputs.accountEndpoint
output cosmosDatabaseName string = cosmosDb.outputs.databaseName

// --- Azure OpenAI (task 128 / Phase C). Output name is LOAD-BEARING:
// ArmDeploymentRunner.MapOutputs (task 123) reads this exact name to populate
// BicepDeployOutputs.OpenAiEndpoint. Raw `openAiKey` is intentionally NOT
// echoed here — flows through task 129's kv-secrets wiring instead. ---
output openAiEndpoint string = openAi.outputs.openAiEndpoint

// --- AI Search (task 128 / Phase C). Output name is LOAD-BEARING:
// ArmDeploymentRunner.MapOutputs (task 123) reads this exact name to populate
// BicepDeployOutputs.AiSearchEndpoint. Raw `searchServiceAdminKey` is
// intentionally NOT echoed here — flows through task 129's kv-secrets wiring
// instead. ---
output aiSearchEndpoint string = aiSearch.outputs.searchServiceEndpoint

// --- Document Intelligence (task 128b / Phase C). Output name is LOAD-BEARING:
// ArmDeploymentRunner.MapOutputs (task 123) reads this exact name to populate
// BicepDeployOutputs.DocIntelligenceEndpoint. Raw `docIntelligenceKey` is
// intentionally NOT echoed here — flows through a future kv-secrets wiring
// task instead (task 129 territory). ---
output docIntelligenceEndpoint string = docIntelligence.outputs.docIntelligenceEndpoint
output docIntelligenceName string = docIntelligence.outputs.docIntelligenceName

// --- Monitoring: App Insights + Log Analytics (task 128b / Phase C). Raw
// `connectionString`/`instrumentationKey` are intentionally NOT echoed here —
// flows through a future kv-secrets wiring task instead (task 129 territory). ---
output appInsightsName string = monitoring.outputs.appInsightsName
output appInsightsId string = monitoring.outputs.appInsightsId
output logAnalyticsName string = monitoring.outputs.logAnalyticsName
output logAnalyticsWorkspaceId string = monitoring.outputs.logAnalyticsWorkspaceId

// --- Redis Cache (task 128b / Phase C — E2 reconciliation). Raw
// `redisPrimaryKey`/`redisConnectionString` are intentionally NOT echoed here —
// flows through a future kv-secrets wiring task instead (task 129 territory). ---
output redisName string = redisCache.outputs.redisName
output redisHostName string = redisCache.outputs.redisHostName
output redisPort int = redisCache.outputs.redisPort

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

// --- Platform cross-reference ---
output platformKeyVaultName string = platformKeyVaultName
