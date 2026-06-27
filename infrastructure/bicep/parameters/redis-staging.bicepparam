// infrastructure/bicep/parameters/redis-staging.bicepparam
// Dedicated BFF Redis cache — staging environment parameters
//
// Project: spaarke-redis-cache-remediation-r1 (task 023, FR-20)
// FR-20 (ADR-009 amendment SKU table): staging SKU = Standard C0 — HA required
// for staging fidelity to prod (single-node Basic insufficient).
// NFR-03: canonical name `spaarke-bff-redis-{env}` (top-level env-suffix).
//
// Usage:
//   az deployment group create \
//     --resource-group <rg> \
//     --template-file infrastructure/bicep/modules/redis.bicep \
//     --parameters infrastructure/bicep/parameters/redis-staging.bicepparam
//
// OR via deploy script (task 030+):
//   ./scripts/Deploy-RedisCache.ps1 -Environment staging
//
// Constraints:
//   - FR-09: targets the audited redis.bicep module (SKU shape = string+int per audit decision)
//   - FR-20: staging gets Standard (replication) per ADR-009 SKU table addition
//   - NFR-05: staging environment only — Deploy-RedisCache.ps1 rejects prod/demo without -Force
//   - Module defaults: redisVersion='' (Azure default), subnetId='' (public), staticIP=''

using '../modules/redis.bicep'

// ============================================================================
// IDENTITY
// ============================================================================

// Canonical resource name (NFR-03)
param redisName = 'spaarke-bff-redis-staging'

// ============================================================================
// SKU (Standard C0 — HA for staging fidelity, FR-20)
// ============================================================================

param sku = 'Standard'
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
  environment: 'staging'
}
