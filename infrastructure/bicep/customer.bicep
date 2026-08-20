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
// Note: per-customer Redis is DEPRECATED per Q-E Architecture 1 / FR-12
// (spaarke-redis-cache-remediation-r1 + r2). Redis is provisioned per-environment
// via scripts/Deploy-RedisCache.ps1 (spaarke-bff-redis-{env}) and consumed by the
// BFF via Key Vault reference. See projects/spaarke-redis-cache-remediation-r2/
// for the IaC gap closure (this template's Redis module call was removed in r2 task 020).

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
// Redis is intentionally NOT provisioned per-customer (Q-E FR-12) — see header note.
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
