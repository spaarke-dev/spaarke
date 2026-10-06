# Pre-Dispatch Readiness Audit — trial1 batch (2026-08-28)

**Verdict**: 🛑 **CANNOT DISPATCH SAFELY YET**. Five gap classes discovered in a 4-minute deep-audit that would each cause a hard-stop or silent-fail during the run. All are *operator config / environmental state* gaps — orthogonal to the code correctness that the recent Fable-level review verified.

**Why Fable didn't catch these**: the Fable review verified each artifact in isolation — SKILL flow correctness, POML correctness, ADR conformance, arch tests, credential lifecycle. It did NOT run an end-to-end **integration test** of "would this actually dispatch a fresh customer today?" That is the only test that surfaces the following three classes of miss, all present here.

---

## Gap A — Operator config data completeness (KNOWN class)

`scripts/provisioning-prereqs/spaarke-constants.yaml` has 6 null values across dev/demo/prod:

| Field | State | Consequence |
|---|---|---|
| `per_env_constants.dev.containerTypeId` | `null` | SKILL Step 0.5b HARD STOPs before any prereq iteration (line ~337 sanity check) |
| `per_env_constants.dev.bffMultiTenantAppId` | `null` | Same sanity check + used by Step 5a admin-consent URL |
| `per_env_constants.demo.*` (both) | `null` | Not relevant for this dispatch |
| `per_env_constants.prod.*` (both) | `null` | Not relevant for this dispatch |

**Discoverable by Fable?** In principle yes (visible in the file), but the Fable review didn't have a checklist item that said "run the SKILL end-to-end and observe what actually blocks."

---

## Gap B — Naming mismatch between `prereqs.yaml` recipes and SKILL substitution chain (NEW class)

The SKILL Step 0.5b substitution loop covers 17 placeholders. `prereqs.yaml` recipes use **8 placeholder names the SKILL does NOT substitute** — every one would trigger PLX-14 `[skill-config] unresolved placeholder` HARD STOP:

| Placeholder in prereqs.yaml | SKILL has instead | Occurrences |
|---|---|---|
| `{acrName}` | `{acrId}` (ARM resource-id, wrong shape for `azurecr.io` hostname) | PRQ-S-15 |
| `{platformKvName}` | `{kvResourceId}` (ARM resource-id, wrong shape for `vault.azure.net` hostname) | PRQ-S-15 |
| `{platformRg}` | (nothing) | PRQ-E-06 (3×) |
| `{sbName}` | `{sbNamespace}` | PRQ-E-06 |
| `{serviceBusFqn}` | (nothing) | PRQ-S-15 |
| `{serviceBusNamespaceName}` | `{sbNamespace}` | PRQ-E-06 (3×) |
| `{cosmosAccountName}` | (nothing) | PRQ-S-15 |
| `{dvUrl}` | `{adminDvUrl}` (only) | PRQ-C-04 (customer-scoped, doesn't fire at Step 0.5 but will at server-side H0) |

**Consequence**: after fixing Gap A, the SKILL would immediately HARD STOP at the FIRST `once_per_subscription` or `once_per_env` prereq that uses any of these (PRQ-E-06 for env scope, PRQ-S-15 for subscription scope). Every subsequent dispatch attempt hits the same wall until either (a) the SKILL substitution chain is extended, or (b) `prereqs.yaml` recipes are refactored to use the current substituted names.

**Why Fable didn't catch this**: this is the intersection between two artifacts that isolated review misses. No single reviewer looked at "SKILL substitutes X" and cross-checked "does every `{placeholder}` in prereqs.yaml appear in X."

---

## Gap C — Environmental drift from SKILL/`prereqs.yaml` naming assumptions (NEW class)

The SKILL Step 0.5b `name_templates` in `spaarke-constants.yaml` assume:

| Template | Assumed name | Actual state |
|---|---|---|
| `platformResourceGroup` | `rg-spaarke-platform-{env}` | ✅ matches (L2 lives there) |
| `platformKvName` | `sprk-{env}-kv` → `sprk-dev-kv` | ❌ **KV `sprk-dev-kv` DOES NOT EXIST** in this subscription. Actual dev KV in `rg-spaarke-platform-dev` is `sprk-controlplane-dev-kv` |
| (BFF App Service Step 0.5b query) | Looks in `rg-spaarke-platform-dev` for `sprksharedprod-api*` OR `spaarke-bff-$env` | ❌ **BFF `spaarke-bff-dev` lives in `rg-spaarke-dev` NOT `rg-spaarke-platform-dev`** — Step 0.5b line ~279 would set `$bffAppServiceId = ""` (silent) |
| `artifactsStorageName` | `sprk{env}artifacts` → `sprkdevartifacts` | UNVERIFIED — likely doesn't exist in `rg-spaarke-platform-dev` either |
| `acrName` | `sprk{env}acr` → `sprkdevacr` | UNVERIFIED — likely doesn't exist |

**Consequence**: `$kvResourceId = null`, `$bffAppServiceId = null`. Every prereq using `{kvResourceId}` or `{bffAppServiceId}` would silently pass or silently fail depending on the recipe. PRQ-S-15 (single-recipe scope authorization check across 4 platform resources) hits this hardest — its for-loop recipe would either falsely PASS or emit truncated error output per empty resource-id.

**Why Fable didn't catch this**: naming conventions in the constants file were assumed correct rather than verified against `az resource list`.

---

## Gap D — Missing Model 1 shared multi-tenant BFF app-reg (NEW class, HIGH severity)

Per **ADR-028** line 229: *"Model 1 — shared Spaarke environment (20+ customers; ONE shared multi-tenant BFF App Service + ONE shared BFF UAMI)"*

**Actual state of Spaarke tenant** (queried today):
- Zero app-regs with `signInAudience != 'AzureADMyOrg'` — all 14 spaarke-* app-regs are single-tenant
- `spaarke-bff-dev` App Service uses `1e40baad-e065-4aea-a8d4-4b7ab273458c` = `Spaarke BFF`, verified single-tenant (`AzureAd__TenantId = a221a95e-...` pinned to Spaarke tenant only)

**Consequence for trial1**: `intake.tenantId = a221a95e-...` (Spaarke tenant) + `tenancyModel = Model1Shared`. Because this trial1 run's tenantId IS the Spaarke tenant, single-tenant BFF may actually work end-to-end for this specific dispatch. But:
- The r1 SKILL Step 5a admin-consent URL construction ASSUMES multi-tenant (would produce a URL that's non-functional if actually needed)
- Any future Model 1 customer whose `tenantId != a221a95e-...` (i.e., every real trial) will hit total failure at H0.5
- The constant name `bffMultiTenantAppId` in the file strongly implies "must be multi-tenant" — treating a single-tenant appId as if it were multi-tenant hides the real gap

**Why Fable didn't catch this**: architectural intent (ADR-028) vs. deployed reality was not cross-checked.

---

## Gap E — L2 App Service has no `SPE:ContainerTypeId` setting (NEW class, medium)

L2 dev App Service has 13 app settings — none reference SPE or ContainerType. But H8 (SPE container creation) and H13 (acceptance verification) need this value at runtime.

**Question the audit can't answer**: where does H8 get the containerTypeId from at execution time? Options:
- The intake `nonSecretParameters` payload (would be logged in audit trail)
- A KV secret (which KV? `sprk-controlplane-dev-kv` search for `SPE` or `Container` naming returned nothing)
- Hardcoded in the L2 image (would be dev-env-specific → bad practice)

Without this being explicit somewhere, H8 will fail at dispatch time with an unactionable "container-type not found" error.

**Why Fable didn't catch this**: code review checked `H8SPEContainerHandler.cs` for correct behavior *given* a containerTypeId, not "how does it get one in the first place?"

---

## What actually needs to happen before dispatch (recommendations)

### Option 1 — Pre-dispatch remediation task (RECOMMENDED)

File a new POML `212-dispatch-readiness-remediation.poml` scoped to:

1. **Populate `spaarke-constants.yaml per_env_constants.dev.*` with real values**:
   - `containerTypeId`: operator looks up out-of-band (SharePoint admin center → Containers, or app-only Graph query from a shell with `FileStorageContainerType.Selected` app permission)
   - `bffMultiTenantAppId`: DECISION REQUIRED — either use the existing single-tenant `1e40baad-...` with a code comment explaining the dev-vs-ADR-028 tension, OR file a separate `spaarke-dev-multitenant-bff-registration-r1` project to actually create the multi-tenant app-reg per ADR-028
2. **Fix the 8 placeholder mismatches** in either `prereqs.yaml` or the SKILL Step 0.5b substitution chain (pick canonical names — this is a "which name wins" alignment, not a "which is right" question)
3. **Correct `name_templates` in constants.yaml**:
   - `platformKvName: sprk-controlplane-{env}-kv` (matches actual) — OR add explicit KV-name overrides per env
   - Add `bffAppServiceRg: rg-spaarke-{env}` OR change SKILL Step 0.5b BFF query to search across RGs
4. **Document where H8 gets containerTypeId at runtime**, and if from KV, add Step 0.5c sanity check that the secret exists in `sprk-controlplane-dev-kv`

**Estimated effort**: 4-6h.

### Option 2 — Force-dispatch anyway (NOT RECOMMENDED)

Set `SkipStep0_5=true` in the intake JSON, bypass every prereq check, dispatch fails somewhere deeper (likely H4b KV writes or H8 container-type). Burns L2 cycles + Cosmos state + operator time. You've explicitly said you don't want more restart cycles.

### Option 3 — Amend Fable review protocol (parallel follow-on)

Add "dry-dispatch integration test" as a mandatory Fable step for any skill that dispatches to an external system. This is the root-cause fix — no more silent Gap-B/C/D/E classes reaching operator dispatch. Doesn't block remediation but should land as its own r1-follow project.

---

## Root cause of the class of miss

The Fable process treats each artifact as a review target in isolation:
- SKILL.md — reviewed for procedural correctness
- prereqs.yaml — reviewed for recipe correctness
- constants.yaml — reviewed for schema correctness
- ADR-028 — reviewed for architectural correctness

But the **intersection** — "does SKILL.md's substitution chain actually cover every placeholder in prereqs.yaml? does the constants.yaml `platformKvName` template actually match a live Azure KV? does ADR-028's multi-tenant BFF assumption actually have a deployed app-reg?" — was **not on any reviewer's checklist**.

The fix is not "review harder." The fix is a mandatory dry-dispatch smoke that exercises the intersection at Fable time. This proposal is Option 3 above.

---

## Immediate ask

Two decisions:
1. **Proceed with Option 1** (file remediation POML, work through it, then dispatch) — my recommendation
2. **Or Option 2** (force-dispatch, debug reactively) — I do not recommend this
3. **Also amend the Fable process per Option 3** (as a parallel follow-on) — my recommendation regardless

Waiting on your decision before proceeding.
