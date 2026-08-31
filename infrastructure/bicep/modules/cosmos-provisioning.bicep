// infrastructure/bicep/modules/cosmos-provisioning.bicep
//
// Azure Cosmos DB for NoSQL — FLEET-SCOPED L2 control-plane orchestration state store.
//
// PURPOSE:  Store `ProvisioningRun` documents (one document per pipeline execution against a
//           customer environment). Consumed exclusively by the L2 Control-Plane service
//           (Sprk.Provisioning.ControlPlane) — this account is NOT deployed per-customer and
//           NOT read/written by the BFF at runtime.
//
// DESIGN REF:
//   - projects/customer-provisioning-orchestration-r1/design.md §5.3 D13 (separate stores per concern)
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2 (ProvisioningRun schema)
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-27 + FR-30 (§4D I3 partition-key
//     predicate invariant — enforced at the SDK boundary by task 037 wiring; this bicep guarantees
//     the container's partition key is `/customerId` so the predicate is meaningful).
//
// DIFFERENTIATED FROM cosmos-db.bicep:
//   - `cosmos-db.bicep` is invoked PER-CUSTOMER (accountName parameter) for the `spaarke-ai`
//     runtime database (containers: sessions, prompts, audit, memory, memory-items, feedback —
//     all partitioned by `/tenantId` or `/subjectId` per ADR-014 + ADR-042). BFF-only.
//   - THIS module is FLEET-SCOPED — one account, deployed to `rg-spaarke-platform-{env}` next
//     to the L2 control-plane App Service. One `spaarke-provisioning` database with one `runs`
//     container partitioned by `/customerId`.
//
// SECURITY:
//   - RBAC-only data plane access (App Service MI acts as data-contributor).
//   - Secrets in `parameters.secrets` on ProvisioningRun documents are stored ONLY as Key Vault
//     URI references (`@Microsoft.KeyVault(VaultName=...;SecretName=...)`); cleartext secrets in
//     Cosmos rows are a CATASTROPHIC leak vector — invariant enforced at the C# type level via
//     `KeyVaultSecretRef` (no `Value` string) and verified by task 025 ArchTest.
//   - `disableLocalAuth: true` — no master keys ever used by application code.

targetScope = 'resourceGroup'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Cosmos DB account name (fleet-scoped, one per environment; e.g. cosmos-spaarke-platform-prod)')
param accountName string

@description('Location for the Cosmos DB account (typically the L2 control-plane region)')
param location string = resourceGroup().location

@description('Name of the L2 control-plane database (locked by design — do not override in non-emergency scenarios)')
param databaseName string = 'spaarke-provisioning'

@description('Name of the runs container (locked by design)')
param containerName string = 'runs'

@description('Container TTL in seconds; 31536000 = 365 days per design.md §6.2 (audit-retention target)')
param defaultTtlSeconds int = 31536000

@description('Principal ID of the L2 control-plane App Service managed identity (granted Cosmos DB Built-in Data Contributor at account scope)')
param controlPlanePrincipalId string = ''

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// COSMOS DB ACCOUNT (Serverless — matches existing spaarke-ai pattern)
// ============================================================================

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: accountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    // Session consistency matches the reconciler's read-your-writes semantics without
    // paying the cost of Strong; state transitions in this container are single-partition.
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
    publicNetworkAccess: 'Enabled'
    // RBAC-only (no master keys); ADR-028 MUST — DefaultAzureCredential everywhere.
    disableLocalAuth: true
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
// DATABASE: spaarke-provisioning (fleet-scoped L2 orchestration state)
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
// CONTAINER: runs
//
// One document per ProvisioningRun. TTL = 365 days (matches audit retention).
// Partition key = /customerId per design.md §6.2 — every read/write MUST pass a
// PartitionKey (verified by FR-30 ArchTest in task 025).
//
// Indexing policy:
//   - Consistent indexing on all paths (default) — supports ad-hoc reconciler queries.
//   - Composite indexes for the two dominant query shapes:
//       (a) status + createdOn — reconciler scans in-flight + failed runs newest-first
//       (b) customerId + createdOn — fleet-view queries a customer's run history
// ============================================================================

resource runsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: containerName
  properties: {
    resource: {
      id: containerName
      partitionKey: {
        paths: ['/customerId']
        kind: 'Hash'
        version: 2
      }
      // 365 days (31,536,000 seconds) — matches audit retention target per design §6.2.
      defaultTtl: defaultTtlSeconds
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
        includedPaths: [
          { path: '/*' }
        ]
        excludedPaths: [
          { path: '/"_etag"/?' }
        ]
        compositeIndexes: [
          // Reconciler: "give me all Running/Failed/Quarantined runs newest-first"
          [
            { path: '/status', order: 'ascending' }
            { path: '/createdOn', order: 'descending' }
          ]
          // Fleet view: "show me runs for customer X newest-first"
          [
            { path: '/customerId', order: 'ascending' }
            { path: '/createdOn', order: 'descending' }
          ]
        ]
      }
    }
  }
}

// ============================================================================
// RBAC: Cosmos DB Built-in Data Contributor for L2 control-plane MI
// Built-in role ID: 00000000-0000-0000-0000-000000000002
// ============================================================================

var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource cosmosRbac 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = if (!empty(controlPlanePrincipalId)) {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, controlPlanePrincipalId, cosmosDataContributorRoleId)
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: controlPlanePrincipalId
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
output containerName string = runsContainer.name
