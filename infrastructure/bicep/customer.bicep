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

// --- User-Assigned Managed Identity (Phase C — customer-provisioning-orchestration-r1) ---

@description('Resource ID of the customer User-Assigned Managed Identity (from modules/uami.bicep — authored in task 028). Phase C parameter accepted here as a PASS-THROUGH ONLY: this task (027) declares the parameter for downstream consumers (task 029 refactors app-service.bicep to bind slots + `keyVaultReferenceIdentity` PATCH to this UAMI per T1/T5). Leave empty in the current customer.bicep composition — no resource in THIS module binds to it yet. Default empty preserves the pre-Phase-C System-Assigned MI behavior on the deployed BFF app-service.')
param userAssignedIdentityResourceId string = ''

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
// KEY VAULT (Deploy first - other resources store secrets here)
// ============================================================================

module keyVault 'modules/key-vault.bicep' = {
  scope: rg
  name: 'keyVault-${baseName}'
  params: {
    keyVaultName: keyVaultName
    location: location
    sku: keyVaultSku
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

// --- UAMI pass-through (task 027 / Phase C) — echoes the input for downstream module composers ---
// Task 029 will bind this to app-service.bicep (`identity.userAssignedIdentities` + `keyVaultReferenceIdentity`
// PATCH); this task only accepts + exposes the parameter so callers can wire it uniformly across the
// customer stamp without touching every downstream module signature at once.
output userAssignedIdentityResourceId string = userAssignedIdentityResourceId

// --- Platform cross-reference ---
output platformKeyVaultName string = platformKeyVaultName
