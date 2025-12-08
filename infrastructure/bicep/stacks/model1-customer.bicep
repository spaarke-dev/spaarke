// infrastructure/bicep/stacks/model1-customer.bicep
// Per-customer resources for Model 1 (Spaarke-hosted multi-tenant)
// Creates customer-specific resources that connect to shared infrastructure

targetScope = 'resourceGroup'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Customer identifier (lowercase, alphanumeric only)')
@minLength(3)
@maxLength(20)
param customerId string

@description('Customer display name')
param customerName string

@description('Shared Key Vault name (from model1-shared deployment)')
param sharedKeyVaultName string

@description('Shared AI Search endpoint (from model1-shared deployment)')
param sharedAiSearchEndpoint string

@description('Customer Dataverse environment URL')
param dataverseUrl string

@description('SPE Container ID (created via API/script)')
param speContainerId string = ''

@description('Tags applied to customer resources')
param tags object = {
  customer: customerId
  customerName: customerName
  application: 'spaarke'
  deploymentModel: 'model1'
}

// ============================================================================
// VARIABLES
// ============================================================================

var customerPrefix = 'sprk-${customerId}'
var aiSearchIndexName = 'spaarke-docs-${customerId}'

// ============================================================================
// CUSTOMER KEY VAULT (Optional - for customer-specific secrets)
// ============================================================================

// Note: Most customers use shared Key Vault with customer-prefixed secrets
// Only create dedicated Key Vault for enterprise customers with compliance requirements

// resource customerKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
//   name: '${customerPrefix}-kv'
//   location: resourceGroup().location
//   tags: tags
//   properties: {
//     // ... customer-specific Key Vault if needed
//   }
// }

// ============================================================================
// CUSTOMER SECRETS (in shared Key Vault)
// ============================================================================

// Reference to shared Key Vault
resource sharedKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: sharedKeyVaultName
}

// Customer-specific Dataverse credentials
resource customerDataverseUrl 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: sharedKeyVault
  name: 'customer-${customerId}-dataverse-url'
  properties: {
    value: dataverseUrl
  }
}

// Customer SPE Container ID
resource customerSpeContainer 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (speContainerId != '') {
  parent: sharedKeyVault
  name: 'customer-${customerId}-spe-container-id'
  properties: {
    value: speContainerId
  }
}

// ============================================================================
// AI SEARCH INDEX (Customer-specific, in shared search service)
// ============================================================================

// Note: AI Search indexes are created via SDK/API, not Bicep
// This is documented as a post-deployment step

// Index schema (for reference - apply via API):
// {
//   "name": "spaarke-docs-{customerId}",
//   "fields": [
//     { "name": "id", "type": "Edm.String", "key": true },
//     { "name": "document_id", "type": "Edm.String", "filterable": true },
//     { "name": "customer_id", "type": "Edm.String", "filterable": true },
//     { "name": "matter_id", "type": "Edm.String", "filterable": true },
//     { "name": "chunk_id", "type": "Edm.Int32" },
//     { "name": "content", "type": "Edm.String", "searchable": true },
//     { "name": "title", "type": "Edm.String", "searchable": true },
//     { "name": "content_vector", "type": "Collection(Edm.Single)",
//       "dimensions": 3072, "vectorSearchProfile": "default" }
//   ],
//   "vectorSearch": { ... },
//   "semantic": { ... }
// }

// ============================================================================
// OUTPUTS
// ============================================================================

output customerId string = customerId
output customerName string = customerName
output dataverseUrl string = dataverseUrl
output speContainerId string = speContainerId
output aiSearchIndexName string = aiSearchIndexName

// Secrets stored
output secretsCreated array = [
  'customer-${customerId}-dataverse-url'
  speContainerId != '' ? 'customer-${customerId}-spe-container-id' : ''
]
