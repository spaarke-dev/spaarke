# Task 011 — Consolidate nav-prop discovery (COMPLETED, Path A)

> **Verdict**: COMPLETED (Path A — 6 of 7 consolidated).
> **Date**: 2026-07-09 · **Rigor**: FULL · model-tier opus · effort high.
> **Escalation history**: The `<escalation><trigger>` fired first (see `task-011-BLOCKED.md`). The orchestrator reviewed the escalation, **accepted it**, and directed **Path A**: consolidate the 6 array-form services now; defer matterService's map-form convergence to a separate task (016). This note records the executed Path A.

## What was done

1. **Added the shared function** to `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`, co-located with `findNavProp`:
   - `export async function discoverNavProps(entityLogicalName: string, fetchImpl: typeof fetch = globalThis.fetch): Promise<INavPropEntry[]>`
   - Module-level cache `const _navPropCache: Record<string, INavPropEntry[]> = {}` (page-lifetime, same semantics as the removed private copies).
   - `export function _resetNavPropCacheForTests(entityLogicalName?: string): void` (test-only; clears one key or all).
   - Mirrors the array-form private implementation exactly: same URL, same `$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`, same `[]`-on-failure never-throw behavior. `fetchImpl` default (`globalThis.fetch`, evaluated at call time) preserves todoService's test seam.

2. **Exported** `discoverNavProps` + `_resetNavPropCacheForTests` from `src/client/shared/Spaarke.UI.Components/src/services/index.ts` (barrel), so task 012's field-mapping engine can consume it.

3. **Swapped the 6 array-form services** to import the shared function and dropped their private `_discoverNavProps` + `_navPropCache`:
   - `CreateEventWizard/eventService.ts` — kept local `NavPropEntry` + `_findNavProp` (structurally identical shape; accepts `INavPropEntry[]`).
   - `CreateProjectWizard/projectService.ts` — same; added the PolymorphicResolverService import (previously had none).
   - `CreateInvoiceWizard/invoiceService.ts` — removed now-unused `INavPropEntry` type import; `_resetInvoiceServiceNavPropCacheForTests()` now delegates to `_resetNavPropCacheForTests()`.
   - `CreateReportCardWizard/reportCardService.ts` — same reset delegation via `_resetReportCardServiceNavPropCacheForTests()`.
   - `CreateTodoWizard/todoService.ts` — reset delegation via `_resetTodoServiceNavPropCacheForTests()`; kept `IPolymorphicWebApi` type import (still used); seam preserved.
   - `CreateWorkAssignmentWizard/workAssignmentService.ts` — kept `INavPropEntry` type import (used by `_resolveDocNavProp` at line 143); 3 call sites swapped. No reset export (unchanged).

4. **matterService.ts — NOT touched.** Its map-form `_discoverNavProps` (`Record<string,string>`) and all create-payload call sites (`navPropMap[lk.col]` @317, `_resolveNavProp` @354/@443) are byte-for-byte unchanged. Deferred to task **016** (`016-matterservice-navprop-convergence.poml`).

## Acceptance criteria

| Criterion | Result |
|---|---|
| Exactly one array-form `discoverNavProps` implementation; the array-form services import it | ✅ (6 of 7; matter deferred per accepted Path A — orchestrator-approved scope) |
| All existing service tests pass unchanged | ✅ 15 suites / 165 tests green |
| No service's create-payload output changed | ✅ matter untouched; the 6 services swapped only the discovery *source*, payload logic unchanged (invoice/reportCard/todo/wa resolver tests assert real payloads — all pass) |

## Verification artifacts

- **Deps**: `npm install --legacy-peer-deps --no-audit --no-fund` (added 792 packages). Also built the `@spaarke/sdap-client` workspace package (its `dist/` was missing — a pre-existing prerequisite unrelated to this change; blocks `tsc` on `EntityCreationService.ts`).
- **Build**: `npm run build` (tsc) → **exit 0, no errors**.
- **Tests**: `npx jest "CreateEventWizard|CreateInvoiceWizard|CreateProjectWizard|CreateReportCardWizard|CreateTodoWizard|CreateWorkAssignmentWizard|PolymorphicResolver|TodoRegardingUpdateBuilder"` → **Test Suites: 15 passed, 15 total; Tests: 165 passed, 165 total; 0 failed.** (Nav-prop-critical suites covered: eventService.cascade, invoiceService.resolver, workAssignmentService.cascade, todoService, reportCardService.resolver, PolymorphicResolver, TodoRegardingUpdateBuilder.)
- **Diff size**: +172 / −321 = **−149 LOC** (6 copies → 1 shared fn).

## Step 9.5 quality gates

- **code-review**: PASS — 0 critical, 0 warning. Suggestions only (all high-confidence-benign): shared cache now spans 6 services (more efficient; Jest per-file isolation confirmed safe), consolidated log text + dropped `console.info` diagnostic (no test asserts them — grep-verified), `entityLogicalName` URL interpolation unchanged from originals and only ever fed hard-coded literals.
- **adr-check**: PASS — ADR-012 compliant (context-agnostic `fetch('/api/data/v9.0/...')`, no `ComponentFramework`/`Xrm.WebApi`/PCF APIs introduced; grep-confirmed — the only `Xrm.` hits are pre-existing `buildRecordUrl` + docstrings, outside this diff). 0 violations, 0 warnings. §6.5 not triggered.

## Test-hook behavior note

The 3 reset helpers now clear the **whole** shared cache (superset of their previous targeted key-deletes). Safe because each `*.test.ts` targets a single service and Jest isolates module state per file — no cross-service cache contamination is reachable. Confirmed empirically (165/165 pass).

## Follow-up

- **Task 016** (`016-matterservice-navprop-convergence.poml`) owns matterService's convergence onto the shared function — a create-payload-touching change requiring a payload-equivalence regression test + owner sign-off per CLAUDE.md §6.5. Until then, two nav-prop discovery shapes coexist by design: the shared array form (`INavPropEntry[]`, 6 services + engine) and matter's map form (`Record<string,string>`).
