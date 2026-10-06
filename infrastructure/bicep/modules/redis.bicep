// infrastructure/bicep/modules/redis.bicep
// Azure Managed Redis (Microsoft.Cache/redisEnterprise) — Microsoft Entra only.
//
// Owner D12 (2026-09-30, customer-provisioning-orchestration-r1 task 242): every Spaarke Redis is Azure Managed Redis.
// Azure Cache for Redis Basic/Standard/Premium retires 2028-09-30 and new-customer creation has been blocked since
// 2026-04-01. Owner D13 (keyless stamps): access keys are disabled on the database, so there is no key, no connection
// string and no Key Vault secret. Clients authenticate with a managed identity that holds an access-policy assignment
// below; the BFF and the L2 Worker connect with `Microsoft.Azure.StackExchangeRedis`
// (`ConfigureForAzureWithUserAssignedManagedIdentityAsync`, RESP3) using the `redisEndpoint` output as `Redis__Endpoint`.
//
// Callers: customer.bicep (per-customer stamp: Balanced_B0, HA on — the D12 defaults) and the per-environment
// parameter files parameters/redis-{env}.bicepparam (dev: B0 non-HA, task 242b).
//
// Managed Redis rules that shape this module (researcher redis-per-customer-stamp-amr-decision-2026-09-30.md):
//   - high availability can only be chosen at create time (it cannot be turned off later) and there is no scale-down;
//   - the clustering policy is immutable once set — OSSCluster (clients must be cluster-aware; StackExchange.Redis is);
//   - the database must be named 'default'; port 10000; TLS only.

@description('Name of the Azure Managed Redis cluster (globally unique DNS label).')
param redisName string

@description('Location for the cluster.')
param location string = resourceGroup().location

@description('Azure Managed Redis SKU. Owner D12: Balanced_B0 for customer stamps; size up only on a measured memory metric (no scale-down).')
@allowed([
  'Balanced_B0'
  'Balanced_B1'
  'Balanced_B3'
  'Balanced_B5'
  'Balanced_B10'
])
param skuName string = 'Balanced_B0'

@description('High availability (two nodes, zone-redundant in AZ regions). Owner D12: Enabled for customer stamps; dev uses Disabled. Can only be set at create time.')
@allowed([
  'Enabled'
  'Disabled'
])
param highAvailability string = 'Enabled'

@description('Minimum TLS version.')
param minimumTlsVersion string = '1.2'

@description('Public network access. Set explicitly (the 2025-07-01 API requires it). Customer stamps have no VNet, matching Cosmos DB / OpenAI / AI Search in customer.bicep.')
@allowed([
  'Enabled'
  'Disabled'
])
param publicNetworkAccess string = 'Enabled'

@description('Object (principal) IDs of the managed identities granted the built-in "default" data access policy. The only way in: access keys are disabled, so an empty list would deploy a cache nobody can use.')
@minLength(1)
param accessPolicyPrincipalIds array

@description('Tags for the resource.')
param tags object = {}

var databasePort = 10000

resource cluster 'Microsoft.Cache/redisEnterprise@2025-07-01' = {
  name: redisName
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  properties: {
    highAvailability: highAvailability
    minimumTlsVersion: minimumTlsVersion
    publicNetworkAccess: publicNetworkAccess
  }
}

resource database 'Microsoft.Cache/redisEnterprise/databases@2025-07-01' = {
  parent: cluster
  name: 'default'
  properties: {
    clientProtocol: 'Encrypted'
    port: databasePort
    clusteringPolicy: 'OSSCluster'
    evictionPolicy: 'AllKeysLRU'
    // Explicit: D13 keyless. With keys disabled there is nothing to list or rotate.
    accessKeysAuthentication: 'Disabled'
  }
}

// One assignment per identity. Names are the object id without dashes (stable across deploys, so an upgrade what-if
// shows no change). batchSize(1): the cluster accepts one update operation at a time.
@batchSize(1)
resource accessPolicyAssignments 'Microsoft.Cache/redisEnterprise/databases/accessPolicyAssignments@2025-07-01' = [
  for principalId in accessPolicyPrincipalIds: {
    parent: database
    name: replace(principalId, '-', '')
    properties: {
      accessPolicyName: 'default'
      user: {
        objectId: principalId
      }
    }
  }
]

output redisId string = cluster.id
output redisName string = cluster.name
output redisHostName string = cluster.properties.hostName
output redisPort int = databasePort
@description('host:port — the value of the BFF / Worker `Redis__Endpoint` setting. Not a secret.')
output redisEndpoint string = '${cluster.properties.hostName}:${databasePort}'
