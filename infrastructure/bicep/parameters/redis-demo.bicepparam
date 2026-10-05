// infrastructure/bicep/parameters/redis-demo.bicepparam
// Demo per-environment Redis — Azure Managed Redis, Microsoft Entra only.
//
// Task 242b (owner D12/D13, customer-provisioning-orchestration-r1): the demo BFF `spaarke-bff-demo` (subscription
// "Spaarke Demo Environment" 2ff9ee48-6f1d-4664-865c-f11868dd1b50, rg-spaarke-demo, westus2) had no cache and
// `Redis__Enabled=false`; a T242+ build runs as Production and needs a cache. Same shape as dev: Balanced_B0 WITHOUT
// high availability (fixed at create time).
//
// Usage (owner-approved live step; pass the subscription, never `az account set`):
//   pwsh ./scripts/Deploy-RedisCache.ps1 -Environment demo -Force \
//     -SubscriptionId 2ff9ee48-6f1d-4664-865c-f11868dd1b50 -CutoverBffSettings -RemoveBffConnectionString
//
// The cache has no access keys: the identity below is the only way in. The BFF connects with its user-assigned
// managed identity using `Redis__Endpoint` (host:10000) — no connection string, no Key Vault secret.

using '../modules/redis.bicep'

param redisName = 'spaarke-bff-redis-demo'

param skuName = 'Balanced_B0'
param highAvailability = 'Disabled'
param minimumTlsVersion = '1.2'
param publicNetworkAccess = 'Enabled'

param accessPolicyPrincipalIds = [
  'eaf9591e-1b60-4579-a84d-8316eb86f9ce' // mi-bff-api-demo (demo BFF spaarke-bff-demo runs as it)
]

param tags = {
  environment: 'demo'
}
