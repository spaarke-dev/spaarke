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
// SHARED container: pinned-context rows + workspace-tab durable rows (+ legacy
// matter-memory docs, retired by task AIR2-050 — left in place per the 2026-07-09
// fresh-container ruling; generalized record/user memory lives in `memory-items`).
// TTL = 90 days. Partition key: /tenantId — per ADR-015 governed data stores.
// Do NOT retire or re-key this container: pins + workspace tabs depend on it.
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
// CONTAINER: memory-items  (task AIR2-050, FR-B-01 — ADR-042 candidate)
// Generalized Record (entityType, entityId) + User (userId) structured AI memory,
// one document PER FACT aligned to the MemoryItem v1 contract.
// Partition key: /subjectId — partitioned by SUBJECT (entityId for record memory,
// userId for user memory), deliberately NOT /tenantId: deployments are
// customer-dedicated, so /tenantId would be a single hot logical partition against
// the 20 GB cap. Cosmos partition keys cannot change in place — this is why a NEW
// container exists instead of re-keying `memory` (which is also SHARED with
// pinned-context + workspace-tab docs and stays as-is; its legacy matter docs are
// left in place per the 2026-07-09 fresh-container ruling).
// defaultTtl: -1 — enables PER-ITEM ttl (task 052 maps retentionClass → ttl) with
// no container-wide expiry.
// ============================================================================

resource memoryItemsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'memory-items'
  properties: {
    resource: {
      id: 'memory-items'
      partitionKey: {
        paths: ['/subjectId']
        kind: 'Hash'
        version: 2
      }
      defaultTtl: -1 // No container-wide expiry; enables per-item ttl (retentionClass → ttl at task 052)
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
