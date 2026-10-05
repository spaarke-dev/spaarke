// infrastructure/bicep/parameters/redis-dev.bicepparam
// Dev per-environment Redis — Azure Managed Redis, Microsoft Entra only.
//
// Task 242 (owner D12/D13, customer-provisioning-orchestration-r1) moved modules/redis.bicep to Azure Managed Redis;
// task 242b creates this cache and cuts the dev BFF and the dev L2 Worker over to it. Dev = Balanced_B0 WITHOUT high
// availability (cost); customer stamps use B0 WITH high availability (customer.bicep). High availability is fixed at
// create time.
//
// Usage (task 242b — owner-approved live step):
//   az deployment group create \
//     --resource-group spe-infrastructure-westus2 \
//     --template-file infrastructure/bicep/modules/redis.bicep \
//     --parameters infrastructure/bicep/parameters/redis-dev.bicepparam
//
// The cache has no access keys: the identities below are the only way in. Each connects with its user-assigned managed
// identity using `Redis__Endpoint` (host:10000) — no connection string, no Key Vault secret.

using '../modules/redis.bicep'

// Canonical per-environment name (ADR-009). Azure accepted it for the new cluster while the old Azure Cache for Redis of
// the same name (Microsoft.Cache/redis, a different DNS zone) still existed — created 2026-10-04 (task 242b step 2).
param redisName = 'spaarke-bff-redis-dev'

param skuName = 'Balanced_B0'
param highAvailability = 'Disabled'
param minimumTlsVersion = '1.2'
param publicNetworkAccess = 'Enabled'

param accessPolicyPrincipalIds = [
  '9fd47efb-7962-492b-ac44-e5ccd0268ebb' // mi-bff-api-dev (dev BFF spaarke-bff-dev runs as it)
  '38f7693f-e6e2-4a3e-9acf-7f9e29dd4044' // sprk-controlplane-dev-uami (L2 Worker dispatch-idempotency cache)
]

param tags = {
  environment: 'dev'
}
