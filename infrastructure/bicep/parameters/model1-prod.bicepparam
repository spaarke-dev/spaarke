// infrastructure/bicep/parameters/model1-prod.bicepparam
//
// Production-environment parameter file for stacks/model1-shared.bicep — the
// Model 1 (SharedTenancy / SMB tier) shared-platform stack per design.md §3A A1.
//
// Deploys to subscription: Spaarke Model 1 Production
//                          (cd95fcec-6b89-49ea-8339-c2b579b12587)
//                          tenant a221a95e-6abc-4434-aecc-e48338a1b2f2
//
// Owner (2026-08-21 T20:30Z): stand-up chosen over adopt-in-place of the r2
// prod scaffolding in the existing dev sub (rg-spaarke-platform-prod / spaarke-
// bff-prod family) because r2's partial scaffolding was missing 6 of 12 runtime
// dependencies (Cosmos, Redis, SB, Storage, AI Search, UAMI) and adopting would
// have accrued ~7 permanent naming exceptions vs. the r3 canonical
// (sprk-{env}-kv, sprksharedprod-*, sprk-{env}-shared-bff-uami). Clean deploy in
// a dedicated sub eliminates that debt and gives Model 1 Prod pristine billing
// isolation as a product-tier deliverable.
//
// Usage:
//   az account set --subscription cd95fcec-6b89-49ea-8339-c2b579b12587
//   az deployment sub create `
//     --location eastus `
//     --template-file infrastructure/bicep/stacks/model1-shared.bicep `
//     --parameters infrastructure/bicep/parameters/model1-prod.bicepparam
//
// The stack declares its OWN resource groups at subscription scope
// (rg-spaarke-shared-prod for shared floors, rg-spaarke-trial01-prod-model1 for
// the seed per-tenant stamp). Do NOT pre-create RGs.

using '../stacks/model1-shared.bicep'

// ============================================================================
// SHARED-PLATFORM GROUP (required)
// ============================================================================

// Environment name — drives ALL shared naming defaults per §7.9 canonical:
//   Shared RG            = rg-spaarke-shared-prod
//   Shared KV            = sprk-prod-kv
//   Shared base          = sprksharedprod (drives *-plan, *-openai, *-search,
//                          *-redis, *-sb, *sa, *-docintel, *-api, *-insights,
//                          *-logs)
//   Shared BFF UAMI      = sprk-prod-shared-bff-uami
param environment = 'prod'

// eastus chosen over westus2 because (a) it is the Bicep default at the stack
// level, (b) it has the broadest coverage for the Azure OpenAI model/version
// pins already baked into stacks/model1-shared.bicep (gpt-4o 2024-08-06,
// gpt-4o-mini 2024-07-18, text-embedding-3-large v1) and their required TPM
// capacities (150/200/350), (c) eastus AI Search Standard has predictable
// replica availability for the environment=='prod' ? 2 : 1 replica choice
// baked into the stack. Dev's westus2 choice was historical, not architectural.
param location = 'eastus'

// ============================================================================
// PER-TENANT SEED GROUP (required — stacks/model1-shared.bicep composes shared
// floors + a first per-tenant stamp in one deploy)
// ============================================================================

// Seed per-tenant customer ID. Design §4.1a establishes the "trial-{yyyymmdd}"
// naming convention for E2E acceptance stamps; we use a stable 'trial01' as
// the FIRST per-tenant slot so subsequent real Model 1 customers get their own
// bicepparam files (e.g. model1-prod-acme01.bicepparam) targeting the same
// stack with different perTenantCustomerId values. Shared modules idempotent
// per stack header comment (line 69-71).
//
// Constraints: lowercase alphanumeric, 3-10 chars. 'trial01' = 7 chars OK.
//
// Materializes: rg-spaarke-trial01-prod-model1 with per-tenant UAMI/KV/Storage/
// Cosmos/AppInsights/LogAnalytics.
param perTenantCustomerId = 'trial01'

// Spaarke Entra tenant ID — REQUIRED per §4D I1 (FR-28) which forbids
// defaulting this at the stack level. For Model 1 (SharedTenancy per D3/§9.1
// v3.5), ALL per-tenant stamps use the Spaarke tenant since customers sign in
// as B2B guests into Spaarke's tenant. Model 2 (dedicated) would use the
// customer's own tenant ID here; Model 1 does not.
param perTenantTenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'
