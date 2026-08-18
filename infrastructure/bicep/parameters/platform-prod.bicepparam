// infrastructure/bicep/parameters/platform-prod.bicepparam
// Production environment parameters for env-scope shared platform infrastructure.
//
// SHRUNK per D12 (customer-provisioning-orchestration-r1 task 031, 2026-08-17)
//   Pre-shrink this file also configured App Service Plan SKU, Azure OpenAI
//   model deployments, and AI Search SKU / replica count — all removed alongside
//   the corresponding module invocations in `platform.bicep`. The remaining
//   surface is genuinely env-scope shared (RG + Monitoring + Platform KV).
//
// Deploys to `rg-spaarke-platform-prod`:
//   - Monitoring (App Insights + Log Analytics, 180-day retention)
//   - Platform Key Vault (`sprk-prod-kv`, Standard SKU)
//
// See `platform.bicep` header + `notes/task-031-topology-decision.md` for the
// full rationale on why the pre-shrink BFF App Service + per-customer AI stack
// moved out. TL;DR: BFF now lives in `stacks/model1-shared.bicep` (Model 1) or
// `stacks/model2-full.bicep` (Model 2); L2 orchestrator lives in
// `platform-controlplane.bicep`; per-customer resources live in `customer.bicep`.
//
// Usage:
//   az deployment sub create \
//     --location westus2 \
//     --template-file infrastructure/bicep/platform.bicep \
//     --parameters infrastructure/bicep/parameters/platform-prod.bicepparam
//
// Constraints:
//   - FR-08: No secrets in this file — Platform KV populated by H4 canonical seeder
//   - FR-11: All resource names follow sprk-/spaarke- canonical naming standard
//   - D12:   No per-customer AI resources; no BFF; no L2 App Service in platform.bicep

using '../platform.bicep'

// ============================================================================
// ENVIRONMENT
// ============================================================================

param environmentName = 'prod'
param location = 'westus2'

// ============================================================================
// MONITORING
// ============================================================================

// 180-day log retention for production compliance and troubleshooting
param logRetentionDays = 180

// ============================================================================
// TAGS
// ============================================================================

param tags = {
  environment: 'prod'
  application: 'spaarke'
  layer: 'platform'
  managedBy: 'bicep'
  costCenter: 'platform'
}
