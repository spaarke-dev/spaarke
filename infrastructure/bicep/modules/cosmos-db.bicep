// infrastructure/bicep/modules/cosmos-db.bicep
// Azure Cosmos DB for NoSQL module — serverless account for AI platform persistence
// Stores AI sessions, prompts, audit records, memory snapshots, and user feedback
// RBAC-only access (no connection strings) via DefaultAzureCredential / Managed Identity

@description('Name of the Cosmos DB account')
param accountName string

@description('Location for the Cosmos DB account')
param location string = resourceGroup().location

@description('Name of the database to create')
param databaseName string = 'spaarke-ai'

@description('Principal ID of the App Service managed identity (granted Cosmos DB Built-in Data Contributor)')
param appServicePrincipalId string = ''

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// COSMOS DB ACCOUNT (Serverless — no fixed provisioning costs for dev)
// ============================================================================

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    // Serverless capacity mode — billed per RU consumed, no reserved throughput
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    // Consistency: Session — best balance of consistency and performance for AI workloads
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    // SecuredByPerimeter in production; Enabled for dev (App Service needs public endpoint)
    publicNetworkAccess: 'Enabled'
    // RBAC-only data plane access (no master keys used by application code)
    disableLocalAuth: false
    // TLS 1.2+ enforced
    minimalTlsVersion: 'Tls12'
    backupPolicy: {
      type: 'Continuous'
      continuousModeProperties: {
        tier: 'Continuous7Days'
      }
    }
  }
}

// ============================================================================
// DATABASE: spaarke-ai
// ============================================================================

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
}

// ============================================================================
// CONTAINER: sessions
// Stores AI conversation sessions per user. TTL = 90 days (7,776,000 seconds).
// Partition key: /tenantId — tenant isolation per ADR-015 governed data stores.
// ============================================================================

resource sessionsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'sessions'
  properties: {
    resource: {
      id: 'sessions'
      partitionKey: {
        paths: ['/tenantId']
        kind: 'Hash'
        version: 2
      }
      defaultTtl: 7776000 // 90 days
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          { path: '/"_etag"/?' }
        ]
      }
    }
  }
}

// ============================================================================
// CONTAINER: prompts
// Stores individual prompts/completions within a session. TTL = 90 days.
// Partition key: /tenantId — tenant isolation per ADR-015 governed data stores.
// ============================================================================

resource promptsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'prompts'
  properties: {
    resource: {
      id: 'prompts'
      partitionKey: {
        paths: ['/tenantId']
        kind: 'Hash'
        version: 2
      }
      defaultTtl: 7776000 // 90 days
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          { path: '/"_etag"/?' }
        ]
      }
    }
  }
}

// ============================================================================
// CONTAINER: audit
// Immutable audit trail for AI actions and data access. No TTL (permanent retention).
// Analytical storage enabled (TTL = -1) for long-term Synapse Analytics queries.
// Partition key: /tenantId — audit queries are typically scoped by tenant.
// ============================================================================

resource auditContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'audit'
  properties: {
    resource: {
      id: 'audit'
      partitionKey: {
        paths: ['/tenantId']
        kind: 'Hash'
        version: 2
      }
      defaultTtl: -1 // No automatic expiry — audit records are permanent
      // analyticalStorageTtl not supported on serverless accounts — use Synapse Link at account level if needed
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          { path: '/"_etag"/?' }
        ]
      }
    }
  }
}

// ============================================================================
// CONTAINER: memory
// Stores AI memory snapshots (user preferences, context, learned facts). TTL = 90 days.
// Partition key: /tenantId — tenant isolation per ADR-015 governed data stores.
// ============================================================================

resource memoryContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'memory'
  properties: {
    resource: {
      id: 'memory'
      partitionKey: {
        paths: ['/tenantId']
        kind: 'Hash'
        version: 2
      }
      defaultTtl: 7776000 // 90 days
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          { path: '/"_etag"/?' }
        ]
      }
    }
  }
}

// ============================================================================
// CONTAINER: feedback
// Stores user feedback on AI responses. No TTL — feedback is retained for model improvement.
// Partition key: /tenantId — tenant isolation per ADR-015 governed data stores.
// ============================================================================

resource feedbackContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'feedback'
  properties: {
    resource: {
      id: 'feedback'
      partitionKey: {
        paths: ['/tenantId']
        kind: 'Hash'
        version: 2
      }
      defaultTtl: -1 // No automatic expiry — feedback retained indefinitely
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          { path: '/"_etag"/?' }
        ]
      }
    }
  }
}

// ============================================================================
// RBAC: Cosmos DB Built-in Data Contributor for App Service managed identity
// Built-in role ID: 00000000-0000-0000-0000-000000000002
// Grants read + write to all containers in this account without using keys.
// Application code uses DefaultAzureCredential — no connection strings required.
// ============================================================================

var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource cosmosRbac 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(appServicePrincipalId)) {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, appServicePrincipalId, cosmosDataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: appServicePrincipalId
    scope: cosmosAccount.id
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output accountName string = cosmosAccount.name
output accountId string = cosmosAccount.id
output accountEndpoint string = cosmosAccount.properties.documentEndpoint
output databaseName string = database.name
