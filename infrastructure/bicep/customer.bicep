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

@description('Name of the platform Key Vault (from platform.bicep deployment) for cross-references')
param platformKeyVaultName string = 'sprk-platform-prod-kv'

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

// Key Vault: sprk-{customer}-{env}-kv (max 24 chars)
var keyVaultName = take('sprk-${customerId}-${environmentName}-kv', 24)

// Service Bus: spaarke-{customer}-{env}-sbus (Note: '-sb' suffix is reserved by Azure)
var serviceBusName = 'spaarke-${customerId}-${environmentName}-sbus'

// ACS resource: sprk-{customer}-{env}-acs (per boundary; data location immutable — D-01)
var acsResourceName = 'sprk-${customerId}-${environmentName}-acs'

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

// --- Platform cross-reference ---
output platformKeyVaultName string = platformKeyVaultName
