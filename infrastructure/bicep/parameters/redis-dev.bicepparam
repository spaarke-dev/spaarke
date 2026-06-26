// infrastructure/bicep/parameters/redis-dev.bicepparam
// Dedicated BFF Redis cache — dev environment parameters
//
// Project: spaarke-redis-cache-remediation-r1 (task 022, FR-10)
// Spec Q1: dev SKU = Basic C0 (~$15/mo) — cost-optimized for dev workloads.
// NFR-03: canonical name `spaarke-bff-redis-{env}` (top-level env-suffix).
//
// Usage:
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file infrastructure/bicep/modules/redis.bicep \
//     --parameters infrastructure/bicep/parameters/redis-dev.bicepparam
//
// OR via deploy script (task 030+):
//   ./scripts/Deploy-RedisCache.ps1 -Environment dev
//
// Constraints:
//   - FR-09: targets the audited redis.bicep module (SKU shape = string+int per audit decision)
//   - NFR-05: dev environment only — prod/demo are separate param files
//   - Module defaults: redisVersion='' (Azure default), subnetId='' (public), staticIP=''

using '../modules/redis.bicep'

// ============================================================================
// IDENTITY
// ============================================================================

// Canonical resource name (NFR-03)
param redisName = 'spaarke-bff-redis-dev'

// ============================================================================
// SKU (Basic C0 — dev cost optimization, Spec Q1)
// ============================================================================

param sku = 'Basic'
param capacity = 0

// ============================================================================
// SECURITY
// ============================================================================

param minimumTlsVersion = '1.2'
param enableNonSslPort = false

// ============================================================================
// TAGS
// ============================================================================

param tags = {
  environment: 'dev'
}
