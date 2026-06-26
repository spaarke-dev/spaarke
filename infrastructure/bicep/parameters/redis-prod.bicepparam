// infrastructure/bicep/parameters/redis-prod.bicepparam
// Dedicated BFF Redis cache — PROD environment parameters (PLACEHOLDER)
//
// ============================================================================
// 🚨 DO NOT DEPLOY FROM THIS PROJECT — produced for sister/future use;
//    actual prod provisioning is separate go/no-go per spec Out of Scope.
//    NFR-05 binding: this project's `Deploy-RedisCache.ps1` rejects -Environment
//    prod without -Force.
// ============================================================================
//
// Project: spaarke-redis-cache-remediation-r1 (task 024, FR-20 SKU table)
// Spec Out of Scope: prod provisioning is a separate go/no-go decision; this
//   file is a canonical template for sister/future projects to reference.
// FR-20 SKU table: Standard C2 is the starting recommendation for prod;
//   Premium tier (VNet injection, RDB persistence, geo-replication) deferred
//   to S1 once finance + security sign-off lands.
// NFR-03: canonical name `spaarke-bff-redis-{env}` (top-level env-suffix).
// NFR-05: MUST NOT touch prod during this project — deploy gate enforced in
//   `Deploy-RedisCache.ps1` (rejects prod/demo without explicit -Force).
//
// Deploy gate (sister/future project ONLY, never this one):
//   ./scripts/Deploy-RedisCache.ps1 -Environment prod -Force
//   (Force flag requires explicit finance + security sign-off per tags below.)
//
// Constraints:
//   - FR-09: targets the audited redis.bicep module (SKU shape = string+int)
//   - FR-20: Standard C2 starting point; Premium upgrade path documented in
//            ADR-009 amendment SKU table
//   - NFR-05: prod environment OUT OF SCOPE for spaarke-redis-cache-remediation-r1

using '../modules/redis.bicep'

// ============================================================================
// IDENTITY
// ============================================================================

// Canonical resource name (NFR-03)
param redisName = 'spaarke-bff-redis-prod'

// ============================================================================
// SKU (Standard C2 — prod starting recommendation per FR-20 SKU table)
// ============================================================================
// Standard C2 (2.5 GB cache, replicated, ~99.9% SLA) is the MVP prod tier.
// Upgrade to Premium (P1+) for VNet injection, RDB persistence, geo-rep —
// deferred to S1 per FR-20.
param sku = 'Standard'
param capacity = 2

// ============================================================================
// SECURITY
// ============================================================================

param minimumTlsVersion = '1.2'
param enableNonSslPort = false

// ============================================================================
// TAGS
// ============================================================================
// `deploy-gate` tag documents the human sign-off required before any prod
// provisioning attempt. Enforced procedurally (NFR-05), not by Bicep.
param tags = {
  environment: 'prod'
  'deploy-gate': 'finance+security'
}
