// infrastructure/bicep/parameters/redis-prod.bicepparam
// Prod per-environment Redis — Azure Managed Redis, Microsoft Entra only (task 242, owner D12/D13). PLACEHOLDER.
//
// No Spaarke prod BFF exists today (2026-10-04 — `spaarke-bff-prod` is not deployed); customer production runs
// on per-customer stamps (customer.bicep). Deploying this needs a separate owner go/no-go.
// High availability on; B0 as the starting size (size up only on a measured memory metric — no scale-down).
// The cache has no access keys: list the prod BFF's user-assigned identity before deploying.

using '../modules/redis.bicep'

param redisName = 'spaarke-bff-redis-prod'

param skuName = 'Balanced_B0'
param highAvailability = 'Enabled'
param minimumTlsVersion = '1.2'
param publicNetworkAccess = 'Enabled'

// PLACEHOLDER — replace with the prod BFF user-assigned identity's OBJECT id before deploying. Not a GUID on purpose:
// Azure rejects it, so this template cannot deploy a cache that no identity can reach (the module requires >= 1).
param accessPolicyPrincipalIds = [
  'REPLACE-WITH-prod-BFF-UAMI-object-id'
]

param tags = {
  environment: 'prod'
  'deploy-gate': 'owner'
}
