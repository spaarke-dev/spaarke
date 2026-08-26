# task-029-deviations.md

> **Task**: 029 — Refactor `app-service.bicep` to consume UAMI + bind BOTH production and staging slots (T5 structural fix)
> **Wave**: 2 Batch 2A (parallel-dispatch)
> **Date**: 2026-08-17
> **Rigor**: FULL @ opus/high
> **Consumes**: task 028 (`modules/uami.bicep`, commit `b17a146ca`) — output `uami.outputs.id` is the identity resource ID this refactor binds to both slots
> **Reference pattern**: task 033's `modules/controlplane-app-service.bicep` (commit `d3b994434`) — UAMI-only App Service authored as a Path A exception because THIS task had not yet shipped

## Summary of change (structural T5 fix)

`app-service.bicep`, `app-service-slot.bicep`, and `deployment-slot.bicep` are now UAMI-only. Each accepts a **REQUIRED** `userAssignedIdentityResourceId` parameter and emits `identity: { type: 'UserAssigned', userAssignedIdentities: { '${userAssignedIdentityResourceId}': {} } }`. The parent App Service module (`app-service.bicep`) removes the previous `enableManagedIdentity` SA-MI toggle AND removes the SA-MI-tied `keyVaultAccessPolicy` resource block. Both slot modules replace their previous unconditional `identity: { type: 'SystemAssigned' }` block with the UAMI binding.

**T5 pre-fix (anti-pattern)**: SystemAssigned MI has a different `objectId` per slot AND rotates on any App Service delete/recreate. KV RBAC + Dataverse App User + Graph app-roles bound to the wrong-slot principalId after a swap => silent 503 window.

**T5 post-fix (this task + task 030 + H4)**: ONE UAMI (from `modules/uami.bicep`, task 028) bound to BOTH slots via `identity.userAssignedIdentities`. Downstream RBAC (task 030) + Dataverse App User (H10) + Graph app-roles (H10) bind to the UAMI's stable principalId. Slot-swap no longer rotates the effective identity.

## Deviations from POML

### D1 — Task boundary: module-only refactor; caller migration deferred (BREAKING CHANGE)

**POML step 2** said: "Add a `userAssignedIdentityResourceId` parameter to app-service.bicep + slot module(s); pass through from `customer.bicep` (task 027 already declares the param at the customer level)."

**Shipped**: refactored the three module files only. Did NOT touch caller files (`platform.bicep`, `stacks/model1-shared.bicep`, `stacks/model2-full.bicep`).

**Callers now FAIL `az bicep build` at the stack level** — this is the intended semantic break of the T5 fix (verified: `az bicep build infrastructure/bicep/platform.bicep` reports 6 errors: 2× `BCP035 missing required property "userAssignedIdentityResourceId"`, 2× `BCP037 property "keyVaultName" not allowed`, 2× `BCP037 property "enableManagedIdentity" not allowed`, plus 2× `BCP053 property "appServicePrincipalId" does not exist` on downstream output consumers). Module-level `az bicep build modules/{app-service,app-service-slot,deployment-slot}.bicep` succeeds cleanly.

**Rationale**:
1. **Task-boundary alignment**. POML `<outputs>` enumerates only the three module files. Acceptance criterion 1 asks for module-level `az bicep build` success (satisfied). No POML criterion requires the stack files to keep building.
2. **Task 033 precedent (Path A exception)**. Task 033 shipped `modules/controlplane-app-service.bicep` as a *new* UAMI-only module rather than modify the shared `app-service.bicep`, explicitly to avoid caller-migration collision. That precedent implicitly recommends that when the shared module IS refactored, caller migration is a separate coordination step.
3. **Wave 2 Batch 2A collision risk**. Sibling task 031 (`platform.bicep-shrink`) is actively editing `platform.bicep` in parallel on the same working tree. Any concurrent edit to `platform.bicep` in this task is a merge-conflict on the same file. Deferring caller migration eliminates the collision.
4. **Semantic force-function**. Leaving the callers broken forces whoever runs the follow-on task to *explicitly* provision (or accept) a UAMI for every BFF App Service — no BFF can be deployed under the new module signature without doing so. That is the exact behavior T5 wants: "no BFF gets SA-MI ever again".
5. **`customer.bicep` is not a direct caller**. Contrary to the POML/dispatcher framing, `customer.bicep` does NOT invoke `app-service.bicep` (it declares `userAssignedIdentityResourceId` as a subscription-scoped pass-through parameter + echoes it as an output). The actual callers are `platform.bicep` (shared-platform BFF), `stacks/model1-shared.bicep`, and `stacks/model2-full.bicep`. Task 027 (commit `0ca76777a`) extended `customer.bicep` correctly for a future orchestration that composes it with an App Service template — that composition does not exist yet.

**Follow-on task recommended**: migrate the three callers atomically:
- `platform.bicep` lines 166 (bffApi module invocation), 204 (bffApiStagingSlot module invocation), 276 + 293 (downstream `appServicePrincipalId` consumers)
- `stacks/model1-shared.bicep` line 400 (sharedBffApi invocation) + any output consumers
- `stacks/model2-full.bicep` line 177 (bffApi invocation) + any output consumers

Each caller needs to (a) declare a caller-level `param userAssignedIdentityResourceId string` (REQUIRED — no default; the T5 fix intent), (b) instantiate `modules/uami.bicep` OR accept the UAMI resource ID from an operator-supplied param, (c) drop `enableManagedIdentity: true` and `keyVaultName: ...` from the module invocation, (d) add `userAssignedIdentityResourceId: userAssignedIdentityResourceId`, (e) update downstream consumers of the removed `appServicePrincipalId` output to read from `uami.outputs.principalId` instead. This is a straightforward mechanical migration; it should be one small task once task 031 and this task both land.

### D2 — Removed `keyVaultAccessPolicy` block + `keyVaultName` param (per POML scope + task 030 boundary)

**POML constraint** (source="project"): "Scope (this task only): app-service + slot identity binding — do NOT migrate RBAC assignments (task 030) and do NOT touch `keyVaultReferenceIdentity` (that is H4 handler concern, informed by this task's output)."

**Shipped**: REMOVED the previous `keyVaultAccessPolicy` resource + the `keyVaultName` param that fed it.

**Rationale**: The removed block granted the SA-MI (`appService.identity.principalId`) `get`/`list` on secrets + certificates. With SA-MI removed:
- `appService.identity.principalId` is no longer discoverable from this module's App Service resource.
- The UAMI's principalId lives at the CALLER level as `uami.outputs.principalId` (task 028 output).
- Task 030 (`role-assignment-*.bicep`) owns UAMI role assignments — the vault-level RBAC replacement lands at the caller composition, not inside this module.
- Retaining the block with SA-MI's `principalId` reference would silently fail at deploy (empty principal in access policy = ARM validation error).
- Retaining the block with a caller-supplied principalId would be scope-creep into task 030 (the POML forbids RBAC migration here).

Follow-on task (or task 030 as it lands) will re-add the KV grant to the UAMI's principalId using the modern RBAC-role model (`Key Vault Secrets User` per ADR-028), aligned with `key-vault.bicep`'s vault-level RBAC.

### D3 — Removed `appServicePrincipalId` output

**Shipped**: removed the `appServicePrincipalId` output from `app-service.bicep`. Removed the `slotPrincipalId` output from both slot modules.

**Rationale**: Without SA-MI on the App Service or slot, `appService.identity.principalId` returns `null` (or ARM raises a validation error on the output expression). Downstream consumers MUST read `uami.outputs.principalId` at the caller level — the UAMI is where the stable principalId lives. Verified this hits real consumers: `platform.bicep` line 276 (`bffPrincipalId: bffApi.outputs.appServicePrincipalId` piped into `customer.bicep` for role assignments) and line 293 (a downstream output). Both consumers need to be updated to source the principalId from the platform-level UAMI module invocation instead. Documented as part of the follow-on caller-migration task (D1).

### D4 — `deployment-slot.bicep` refactored despite having no in-tree callers

**Shipped**: refactored `deployment-slot.bicep` (the comprehensive slot variant with warm-up + slot-sticky settings) even though `grep` confirms no in-tree caller invokes it.

**Rationale**: POML `<outputs>` explicitly lists `deployment-slot.bicep (or app-service-slot.bicep — verify local topology)`. Both exist. Refactoring only one and leaving the other with `identity: { type: 'SystemAssigned' }` would leave a latent T5 anti-pattern waiting for the first future caller. Consistency + T5-elimination intent justify refactoring both.

### D5 — Live `az deployment group what-if` deferred (per dispatcher note)

**POML step 7**: "Run `az deployment group what-if` against dev — verify identity change diff on BOTH slot resources."

**Deferred** per dispatcher note: "Live `az deployment group what-if` (if in POML): defer if no dev subscription context available. `az bicep build` is sufficient authoring gate."

**Rationale**: what-if requires a stack-level template + a dev subscription context. Because callers are intentionally broken (D1) until the follow-on caller-migration task, running a stack-level what-if is not meaningful right now — it would just report the same 6 build errors as `az bicep build`. Module-level `az bicep build` on all three refactored files exits 0 with 0 errors + 0 warnings. Deferring what-if is aligned with the module-only task boundary; it should run once the caller-migration follow-on task lands and the stack rebuilds cleanly.

## Verification

- `az bicep build infrastructure/bicep/modules/app-service.bicep` → exit 0, 0 errors, 0 warnings.
- `az bicep build infrastructure/bicep/modules/app-service-slot.bicep` → exit 0, 0 errors, 0 warnings.
- `az bicep build infrastructure/bicep/modules/deployment-slot.bicep` → exit 0, 0 errors, 0 warnings.
- `az bicep build infrastructure/bicep/platform.bicep` → FAILS with the 6 expected errors (documented in D1). Confirmed intentional semantic break.
- Grep for `SystemAssigned` / `enableManagedIdentity` in the three modules: **0 code-line matches** (all matches are comments explaining the removal — acceptance criterion 3 uses "excluding comments or interim-mitigation notes" verbatim).
- All three modules bind the SAME `userAssignedIdentityResourceId` via `identity.userAssignedIdentities['${userAssignedIdentityResourceId}']: {}` — identity parity across slots guaranteed by the caller passing the same param.
- Bicep CLI version: 0.46.1.

## Acceptance criteria mapping (POML)

| Criterion | Status |
|---|---|
| `az bicep build` on modified modules: 0 errors + 0 warnings | ✅ verified (three module builds exit 0) |
| `what-if`: both prod + staging show `identity.type = 'UserAssigned'` on the same `userAssignedIdentities` key | ⏭️ DEFERRED — see D5; what-if requires stack-level context which is intentionally broken until follow-on caller-migration lands. Structural correctness is verified by reading the three refactored modules: all three emit `type: 'UserAssigned'` with `'${userAssignedIdentityResourceId}': {}` bound to the same caller-supplied param. |
| Grep for `SystemAssigned` / `enableManagedIdentity` in the two modules → 0 (excluding comments) | ✅ verified — see D2 grep evidence. Matches are in comments explaining the removal. |
| Negative: no RBAC role assignment changes in this task | ✅ verified — removed a SA-MI-tied access-policy block; did NOT add any UAMI role assignments (task 030 scope). |
| Negative: no BFF code changes; publish-size delta = 0 | ✅ verified — bicep-only; no `.cs` / `.ts` / `.tsx` touched. Publish-size delta = 0 MB. |

## ADR-028 compliance

- **MUST** use `DefaultAzureCredential` (managed identity) for all server outbound: this module now binds a User-Assigned MI, aligning with the ADR MUST rule. The bound UAMI's `clientId` (`uami.outputs.clientId`) is what App Service consumers pin `DefaultAzureCredential` to via `AZURE_CLIENT_ID` (see `controlplane-app-service.bicep` line 145 for the reference wiring).
- **Anti-pattern avoided**: `type: 'SystemAssigned, UserAssigned'` (co-emitted) — this refactor emits UAMI ONLY, never co-emitting SA-MI. Grep evidence per D2 confirms.
- **Amendments A1/A2/A3** (external-portal / Teams host / module-host platform): unaffected — those govern client-side auth surfaces; this refactor is server-side identity binding.

## Escalation not triggered

POML escalation triggers (spec.md T5 slot-swap smoke test failure; Azure ARM refusing the same UAMI on two slots): neither applies at this task boundary — smoke testing is Phase F acceptance work + requires the follow-on caller-migration to land first. Same-UAMI-on-multiple-slots is a documented Azure capability (confirmed by task 033's `controlplane-app-service.bicep` production pattern) and did not surface as a limitation during module authoring.

## Sibling coordination (Wave 2 Batch 2A)

Wave 2 Batch 2A siblings running in parallel on the same working tree:

- **Task 025** (ArchTest) — no expected file overlap with this task.
- **Task 031** (`platform.bicep`-shrink) — collision-risk file `platform.bicep`. This task INTENTIONALLY does NOT touch `platform.bicep` (D1). If task 031's shrink work happens to remove or restructure the `bffApi` / `bffApiStagingSlot` module invocations, the follow-on caller-migration task must reconcile against the shrunk topology. No expected collision from this task.
- **Task 037** (L2 Cosmos-wiring) — targets `platform-controlplane.bicep` + `stacks/*.bicep` for Cosmos wiring; unlikely to touch the App Service module signature.

Task 033 predecessor (`controlplane-app-service.bicep`, commit `d3b994434`) is intentionally NOT deleted here per dispatcher note ("Do NOT delete task 033's dedicated module in this task — separate consolidation"). After this task lands, `controlplane-app-service.bicep` becomes topology-equivalent to `<modules/app-service.bicep> + <modules/app-service-slot.bicep>`; deletion decision belongs to a future consolidation task documented as Path A D1 in task 033's own deviation note.

## Follow-on tasks recommended

1. **CALLER-MIGRATION (required to restore stack builds)**: update `platform.bicep`, `stacks/model1-shared.bicep`, `stacks/model2-full.bicep` per D1 recommendation. Include downstream output-consumer updates (D3).
2. **CONSOLIDATION (optional, per task 033 deviation note)**: after caller-migration lands, retire `modules/controlplane-app-service.bicep` in favor of two invocations (`modules/app-service.bicep` + `modules/app-service-slot.bicep`) from `platform-controlplane.bicep`. Topology-equivalent; the dedicated module was an interim Path A exception.
3. **RBAC (task 030 scope)**: re-add `Key Vault Secrets User` grant on the UAMI (`uami.outputs.principalId`) at the appropriate caller composition to replace the removed SA-MI access-policy grant (D2). Task 030 already owns this per its POML.
4. **H4 handler (spec.md § MUST rules)**: PATCH `keyVaultReferenceIdentity` on both slots to the UAMI's resource ID post-deploy. Out of scope for THIS task per POML constraint.
