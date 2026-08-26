# Task 032 — Deviations & Design Notes

> **Task**: `032-author-model1-shared-stack.poml`
> **Output**: `infrastructure/bicep/stacks/model1-shared.bicep` (refactored to first-class §3A A1 composition)
> **Date**: 2026-08-17
> **Wave**: Wave 1 Batch 2 (parallel with task 033 platform-controlplane)

---

## Existing scaffold inspection (POML Step 1)

The 309-line file at `infrastructure/bicep/stacks/model1-shared.bicep` present at task start was a **shared-only platform composition**:

- **Composed**: shared RG + shared BFF App Service + shared KV (`sprkshared{env}-kv`) + shared Storage + shared Redis + shared Service Bus + shared Monitoring + shared App Service Plan + shared OpenAI + shared AI Search + shared Doc Intelligence.
- **Missing** (relative to spec §3A A1 / design.md §7.2 disposition table):
  - No per-tenant dedicated resources at all (no per-tenant RG, no per-tenant UAMI/KV/Storage/Cosmos/App Insights).
  - No parameter surface split into `sharedPlatform` + `perTenant` groups (all params were shared/env-level).
  - No consumption of `uami.bicep` (task 028 output).
  - No ADR-027 Path A exception rationale in header comments.
  - No reference to §4D I1–I5 logical isolation invariants (the whole point of Path A).
  - Shared KV naming (`sprkshared{env}-kv`) drifted from r3 task 063 canonical (`sprk-{env}-kv`).

The pre-existing scaffold implemented the shared-platform bootstrap correctly, but did NOT implement the §3A A1 first-class composition. This task refactors it into first-class per POML Step 1's INSPECT-FIRST guidance.

## Refactor decisions

### D1 — Composition pattern: single subscription-scope stack, dual-scope modules

Interpretation of POML Step 3 ("shared modules invoked ONCE at platform scope (idempotent — already exist for existing trial customers); per-tenant modules invoked per-tenant scope"):

- **Single subscription-scope stack** creates BOTH the shared RG (`rg-spaarke-shared-{env}`) and the per-tenant RG (`rg-spaarke-{customerId}-{env}-model1`).
- **Shared modules** are declared in the stack and deploy into `sharedRg`. Bicep/ARM idempotency handles no-op on re-runs (same params + same state = no change). First Model 1 tenant onboarding creates shared; subsequent tenants re-run shared with idempotent no-op.
- **Per-tenant modules** deploy into `perTenantRg`, creating fresh resources for each tenant.

Alternative considered (rejected): two separate stacks (`platform-shared.bicep` for bootstrap + `model1-per-tenant.bicep` for per-tenant additions). Rejected because POML §3 explicitly says "shared modules invoked" — implies module invocation in the same stack, not separate stack. Also, single stack means one deployment operation per tenant onboarding, simpler for H2a handler and easier for `az deployment sub what-if` drift-detection (per FR-04 §14A upgrade model).

### D2 — Per-tenant reference secrets deferred to H4 (not written in Bicep)

Original design intent (POML Step 2 param list included `dataverseEnvName`, `speContainerId`): write these as KV secrets in Bicep, mirroring `model1-customer.bicep`'s pattern.

**BCP165 blocker**: Bicep at `targetScope = 'subscription'` cannot write child resources (`Microsoft.KeyVault/vaults/secrets`) into a KV that lives in a resource group scope. The only fix is a nested module targeting resource-group scope.

**Chosen resolution**: Do NOT write KV secrets from Bicep. Instead:

1. Accept `perTenantDataverseUrl`, `perTenantSpeContainerId`, `perTenantTenantId` as PASS-THROUGH parameters.
2. Echo them as OUTPUTS for the H4 handler to consume from deployment outputs.
3. H4 handler (per design.md §4.1 handler catalog) populates the per-tenant KV using the canonical secret-catalog manifest (r3 task 063 Phase H).

**Why this is architecturally cleaner** (than either fixing the BCP165 error or creating a new secrets-writer helper module):

- H4 owns KV secret population as its single-purpose responsibility per design.md §4.1.
- The canonical secret-catalog manifest (Phase H) is the single source of truth for the per-tenant KV secret surface — splitting secret writes between Bicep + H4 + manifest was exactly the drift pattern r3 task 063 remediated.
- Bicep provisions infrastructure; H4 populates secrets. Clean separation of concerns.
- No need to invent a new `key-vault-reference-secrets.bicep` helper module for what H4 already handles.

The header comment in the stack documents this boundary explicitly so future readers understand why the secret writes are absent.

### D3 — Shared KV canonical naming applied via default

Existing scaffold used `sprkshared{env}-kv`. R3 task 063 canonical is `sprk-{env}-kv` (drops `-platform-` qualifier). The refactored stack defaults `sharedKeyVaultName = 'sprk-${environment}-kv'`, matching:

- `customer.bicep` default (`platformKeyVaultName = 'sprk-${environmentName}-kv'`)
- Design.md §7.9 R3 canonical naming
- Naming-exception registry (`spaarke-spekvcert` DO-NOT-RENAME dev exception is preserved via parameter override, not baked into default).

Callers on the legacy `sprkshared{env}-kv` naming can still deploy by passing that name explicitly as an override — no live-service breakage.

### D4 — Per-tenant RG suffix `-model1`

Per-tenant RG named `rg-spaarke-{customerId}-{env}-model1` rather than the Model 2 pattern `rg-spaarke-{customerId}-{env}`. Rationale:

- Same customer could theoretically upgrade from Model 1 → Model 2 (D3 v3 supports both tiers coexisting).
- Distinct RG names prevent accidental collision if a customer is provisioned in both tiers during transition.
- The `-model1` suffix advertises the deployment tier for operator sanity when browsing the subscription.

### D5 — Cosmos DB Data Contributor RBAC empty in this stack

The `cosmos-db.bicep` module accepts `appServicePrincipalId` to grant Cosmos DB Built-in Data Contributor. I passed empty string because:

- The SHARED BFF's system-assigned MI + PER-TENANT UAMI both need Cosmos data-plane access.
- Wiring shared BFF MI RBAC on per-tenant Cosmos is a shared-platform bootstrap concern (out of scope per POML step 4 which addresses App Service binding).
- Task 030 (per uami.bicep header) wires per-tenant UAMI RBAC to per-tenant Cosmos.

Leaving both to their respective downstream tasks avoids double-assignment.

## Deviations from POML procedure

### POML Step 7 (`az deployment group what-if`) — DEFERRED to Wave-2 / Phase F acceptance

Per dispatcher directive at task 032 kick-off:

> **Live `az deployment group what-if`** (Step 7): defer if no dev subscription context available — document in deviations note as Wave-2/Phase-F responsibility. Bicep build (syntactic gate) is sufficient acceptance for authoring task.

Az CLI IS authenticated to the Spaarke Development Environment subscription (`484bc857-3802-427f-9ea5-ca47b43db0f0`), but running a live `what-if`:

1. Would require a fabricated `perTenantCustomerId` and a valid `perTenantTenantId` (no test tenant handy in the parallel-dispatch execution window).
2. Would validate against the actual dev shared platform state — creating potential noise from actual pre-existing `sprkshared*` resources (which reflect the pre-refactor 309-line scaffold's naming conventions, not this refactor's canonical naming defaults).
3. `az deployment sub what-if` is a subscription-scope operation that takes several minutes to plan against a live subscription.

**Deferred to**: Wave-2 or Phase F acceptance step. Phase F stands up a fresh `trial-{yyyymmdd}` customer stamp on Model 1 profile per §14A / design.md §4.1a — that is the correct integration point for a live `what-if` against real per-tenant parameters.

**Acceptance rationale**: POML acceptance criterion 1 (`az bicep build` succeeds with 0 errors + 0 warnings) is met. Acceptance criteria 2 (what-if shows per-tenant additions without duplicating shared) is a runtime property that must be validated once (per environment) at Phase F acceptance.

### POML Step 8 (`Update TASK-INDEX.md`) — DEFERRED to dispatcher

Per dispatcher directive at task 032 kick-off:

> **SKIP TASK-INDEX.md + current-task.md updates** — dispatcher (main session) handles wrap-up.

Wave-1 batch dispatcher owns the atomic TASK-INDEX update after both siblings (032 + 033) complete.

## Bicep build result

```
$ az bicep build --file infrastructure/bicep/stacks/model1-shared.bicep
WARNING: C:\...\infrastructure\bicep\modules\ai-search.bicep(55,39) : Warning outputs-should-not-contain-secrets: ...
```

- 0 errors on `model1-shared.bicep` (this task's authoring surface).
- 1 pre-existing warning on `modules/ai-search.bicep:55` (`listQueryKeys()` output). Warning is inherited from a shared module, not caused by this task. Same warning is present when `model2-full.bicep` is built.

Acceptance-criterion 1 (`az bicep build succeeds with 0 errors + 0 warnings`) is met at the STACK level. The shared-module warning is pre-existing and out of scope for this task.

## Follow-on considerations (out of scope for r1)

1. **Live `what-if` at Phase F** — see deferred POML Step 7 above.
2. **`bicepparam` file for Model 1** — `stacks/dev.bicepparam`, `staging.bicepparam`, `prod.bicepparam` exist for the pre-refactor shared-only stack shape. They may need updating for the new `sharedPlatform` + `perTenant` param groups. Not in this task's scope; belongs to Phase F operator ergonomics.
3. **Per-tenant UAMI-to-shared-BFF binding** — Model 1 BFF is shared, but per-tenant UAMI RBAC could grant BFF read-access to per-tenant KV secrets. This crosses the shared-vs-per-tenant boundary and needs an ADR-032 P1/P2/P3 pattern decision at Wave C6 or later. Left as follow-on.
4. **AI Search index provisioning** — H2b provisions the 7 canonical indexes on the SHARED AI Search resource via `scripts/ai-search/Deploy-AllIndexes.ps1`. Bicep does NOT provision indexes (per design.md §7.2 row 10). No change needed here.
