// infrastructure/bicep/customer.bicep
// Per-customer Bicep template for Spaarke production environment
// Deploys isolated data resources into a dedicated customer resource group.
// Run once per customer onboarding via Provision-Customer.ps1.
//
// Resources deployed:
//   - Storage Account (temp files, document processing)
//   - Key Vault (customer-specific secrets)
//   - Service Bus namespace (job queues)
//   - Redis Cache (token caching, session state)

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

// --- Redis options ---

@description('Redis Cache SKU')
@allowed(['Basic', 'Standard', 'Premium'])
param redisSku string = 'Basic'

@description('Redis Cache capacity (family size: 0-6)')
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

// Key Vault: sprk-{customer}-{env}-kv (max 24 chars)
var keyVaultName = take('sprk-${customerId}-${environmentName}-kv', 24)

// Service Bus: spaarke-{customer}-{env}-sbus (Note: '-sb' suffix is reserved by Azure)
var serviceBusName = 'spaarke-${customerId}-${environmentName}-sbus'

// Redis: spaarke-{customer}-{env}-cache
var redisName = 'spaarke-${customerId}-${environmentName}-cache'

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
    containers: storageContainers
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
// REDIS CACHE (Token caching, session state per ADR-009)
// ============================================================================

module redis 'modules/redis.bicep' = {
  scope: rg
  name: 'redis-${baseName}'
  params: {
    redisName: redisName
    location: location
    sku: redisSku
    capacity: redisCapacity
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

// --- Redis Cache ---
output redisHostName string = redis.outputs.redisHostName
output redisPort int = redis.outputs.redisPort
#disable-next-line outputs-should-not-contain-secrets
output redisConnectionString string = redis.outputs.redisConnectionString

// --- Platform cross-reference ---
output platformKeyVaultName string = platformKeyVaultName
