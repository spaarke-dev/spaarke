// infrastructure/bicep/parameters/redis-staging.bicepparam
// Staging per-environment Redis — Azure Managed Redis, Microsoft Entra only (task 242, owner D12/D13).
// No staging environment exists today (2026-10-04); this file is the template for when one is stood up.
// High availability on for staging fidelity to customer stamps. The cache has no access keys: list every
// identity that must read or write it (the staging BFF's user-assigned identity) before deploying.
//
// Usage:
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file infrastructure/bicep/modules/redis.bicep \
//     --parameters infrastructure/bicep/parameters/redis-staging.bicepparam

using '../modules/redis.bicep'

param redisName = 'spaarke-bff-redis-staging'

param skuName = 'Balanced_B0'
param highAvailability = 'Enabled'
param minimumTlsVersion = '1.2'
param publicNetworkAccess = 'Enabled'

// PLACEHOLDER — replace with the staging BFF user-assigned identity's OBJECT id before deploying. Not a GUID on purpose:
// Azure rejects it, so this template cannot deploy a cache that no identity can reach (the module requires >= 1).
param accessPolicyPrincipalIds = [
  'REPLACE-WITH-staging-BFF-UAMI-object-id'
]

param tags = {
  environment: 'staging'
}
