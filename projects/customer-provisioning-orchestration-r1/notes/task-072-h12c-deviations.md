# Task 072 (H12c Runtime References Handler) — Deviations

> Recorded per CLAUDE.md §6.5 ADR Conflict Resolution Protocol + task-execute Step 9 (deviation documentation).

## D-072-1: Model 1 metering-attribution field — Path C (pivot to comply)

**POML literal instruction** (goal + step 4): "Model 1 rows carry a metering-attribution tenantId field."

**What the live schema actually has**: verified the `sprk_aimodeldeployment` Dataverse table via `mcp__dataverse__describe('tables/sprk_aimodeldeployment')`. Fields: `sprk_aimodeldeploymentid`, `sprk_capability` (choice), `sprk_contextwindow`, `sprk_description`, `sprk_endpoint`, `sprk_isactive`, `sprk_isdefault`, `sprk_modelid`, `sprk_name`, `sprk_provider` (choice). **No `tenantId` column exists.**

**Resolution — Path C (pivot to comply)**: per CLAUDE.md §11 ("prefer extending existing... schema changes are their own task"), H12c does NOT add a new Dataverse column to a live table from inside a handler-implementation task. Instead:
- Model 1 (`Model1Shared`) rows carry a human-readable metering-attribution note in `sprk_description`: `"Shared platform Azure OpenAI deployment (Model1Shared tenancy). Per-tenant metering attribution: tenantId={tenantId}, customerId={customerId} (task 077 owns the queryable metering-attribution mechanism)."`
- The **queryable** metering-attribution mechanism (the actual thing that needs to correlate token spend to a tenant) is left to task 077 (per-tenant token metering layer), which is expected to key off the run's `tenantId` parameter / BFF request context rather than a redundant column on this shared reference table — `sprk_aimodeldeployment` describes the MODEL, not the CALLER, and every Model1Shared customer's rows are otherwise byte-identical (same endpoint, same 3 pinned models) so a per-row tenantId column would not even distinguish rows meaningfully within one customer's own environment.

**Why this is not an ADR conflict**: confirmed via `adr-check` (Step 9.5) that no ADR mandates a specific metering-attribution storage mechanism. This is a design/schema-scope decision under CLAUDE.md §11, not an ADR MUST/MUST NOT rule — so no Path A (exception) or Path B (amendment) is needed, Path C is the correct and sufficient resolution.

**Alternative considered and rejected**: adding a `sprk_tenantid` column to `sprk_aimodeldeployment` directly from this task. Rejected because (a) it's a live-table schema change requiring its own `dataverse-create-schema` task + solution-version bump, out of scope for a handler-implementation task; (b) per the reasoning above, the column wouldn't actually distinguish anything within one customer's own Dataverse environment (each customer has its own environment + its own `sprk_aimodeldeployment` rows — there is no cross-tenant row to disambiguate inside a single environment).

## D-072-2: H12c enqueues H14 (temporary wave-Cp bridge, not explicitly in POML steps)

The POML's literal step 5 says only "Register in L2 Program.cs DI" — it does not mention a downstream enqueue. Following the established WAVE-C4/Cp TEMPORARY BRIDGE pattern used by every predecessor handler in this DAG (H0→H0.5, H12a/H12b→H12c), H12c enqueues H14 (task 073) on its own success, since H12c is H14's single upstream trigger per design.md §4.1's DAG (`H12c → H14 → H13`). This is a Path C "pivot" — following established codebase convention already accepted in Step 9.5 gates for tasks 070/071 — not a new architectural decision requiring escalation. Enqueue failure does not fail the handler (parity with H12b's enqueue-failure handling); the reconciler's crash-recovery scan re-emits H14.

## D-072-3: Idempotency key format — POML literal wins over design.md's original format

design.md §4.1's handler catalog table lists H12c's idempotency key as `runtimerefs-{customerId}-{modelVer}`. The task 072 POML `<prompt>` explicitly states `h12c-{customerId}-{tenancyModel}-{endpointHash}`. Implemented the POML's literal format — consistent with how sibling tasks 070/071 also used their POML-literal `h12a-`/`h12b-` prefixes rather than design.md's original `aiseed-`/`configseed-` prefixes. No escalation needed; this is the established precedent for this DAG segment.
