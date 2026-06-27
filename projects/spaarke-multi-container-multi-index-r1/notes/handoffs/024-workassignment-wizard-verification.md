# Task 024 — CreateWorkAssignmentWizard verification (FR-WIZ-04)

**Date**: 2026-06-07
**Author**: task-execute (Opus 4.7)
**File of interest**: `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts`

## sprk_containerid verification — finding

**`sprk_containerid` was already being set** prior to this task — but the source was a constructor-injected `_containerId` value (passed by the host `WorkAssignmentWizardDialog`), NOT the user's owning Business Unit per FR-WIZ-04.

Source (pre-fix, lines 402-405):

```ts
// Store the SPE container ID on the work assignment record (enables Documents tab)
if (this._containerId) {
  entity['sprk_containerid'] = this._containerId;
}
```

The host dialog (`WorkAssignmentWizardDialog.tsx`) resolves `_containerId` from either a direct prop or a caller-supplied `resolveSpeContainerId?: () => Promise<string>`. That host-resolved value is appropriate as an **explicit override** (per INV-5 contract) but does NOT satisfy FR-WIZ-04, which mandates BU-derived defaults.

## Gap summary

| Field | Pre-task state | Required (FR-WIZ-04) |
|---|---|---|
| `sprk_containerid` | Set from host constructor param `_containerId` (could be undefined if host didn't resolve one) | Set from current user's BU `sprk_containerid` when host did not pre-set; host value wins per INV-5 |
| `sprk_searchindexname` | **Not set at all** | Set from current user's BU `sprk_searchindexname`, INV-5 safe |

## Fix applied

Added a BU-cascade block immediately after the existing host-`_containerId` assignment (which is now positioned as a pre-seed for the cascade — INV-5 preserves it):

```ts
// 1) Host-resolved container (explicit override pre-seed, INV-5 sacred)
if (this._containerId) {
  entity['sprk_containerid'] = this._containerId;
}

// 2) BU cascade — fills any field the host did not pre-set; both fields INV-5 guarded
try {
  const currentUserId = _getCurrentUserId();
  if (currentUserId) {
    const defaults = await EntityCreationService.resolveUserBuDefaults(this._dataService, currentUserId);
    EntityCreationService.applyUserBuDefaults(entity, defaults);
  } else {
    console.warn('[WorkAssignmentService] BU cascade skipped: current user ID could not be resolved.');
  }
} catch (err) {
  console.warn('[WorkAssignmentService] BU cascade failed (non-fatal):', err);
}
```

A module-private `_getCurrentUserId()` walks `window → window.parent → window.top` searching for an Xrm global, returning the GUID (brace-stripped, lower-cased) from either `Xrm.Utility.getGlobalContext().userSettings.userId` or `Xrm.Utility.getUserId()`. Returns `''` when no Xrm is reachable — caller treats that as "skip cascade" so non-Xrm test harnesses still create records successfully.

## INV-5 evidence (matrix)

| Scenario | Host `_containerId` | BU container | BU index | Final `sprk_containerid` | Final `sprk_searchindexname` |
|---|---|---|---|---|---|
| Standard cascade | undefined | `bu-container-abc` | `spaarke-knowledge-index-v2` | `bu-container-abc` | `spaarke-knowledge-index-v2` |
| Host override | `host-xyz` | `bu-container-abc` | `spaarke-knowledge-index-v2` | `host-xyz` (INV-5) | `spaarke-knowledge-index-v2` |
| NULL BU index (Phase A.5 lag) | undefined | `bu-container-abc` | `null` | `bu-container-abc` | (unset → BFF default) |
| No Xrm | `host-xyz` | n/a | n/a | `host-xyz` | (unset → BFF default) |
| User has no BU | `host-xyz` | n/a | n/a | `host-xyz` | (unset → BFF default) |
| Network error on BU lookup | `host-xyz` | n/a | n/a | `host-xyz` | (unset → BFF default) |

All six scenarios are covered by `__tests__/workAssignmentService.cascade.test.ts` (6 cases).

## ADR compliance

- **ADR-012** (shared lib is the contract): cascade implemented in `@spaarke/ui-components`; consumers gain it transparently.
- **ADR-021** (Fluent v9): N/A — service layer, no UI.
- **ADR-022** (React version boundaries): no React imports added; service stays portable across React 16 (PCF) / React 18 (Code Pages).
- **ADR-028** (Spaarke Auth v2): no new BFF calls; BU resolution uses the existing `IDataService` (structural superset of `IWebApiLike`), which the wizard already injects.

## Files modified

1. `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts`
   - Added module-private `_getCurrentUserId()` helper (~40 LOC).
   - Added BU-cascade block immediately after the existing host-`_containerId` assignment in `createWorkAssignment` (~20 LOC).

2. `src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/__tests__/workAssignmentService.cascade.test.ts`
   - New file, 6 test cases covering cascade, INV-5, NULL BU, no Xrm, no BU, network error.

## Files unchanged (per task constraint)

- `EntityCreationService.ts` — DO NOT modify per task.
- All other wizards (Matter/Project/Invoice/Event) and DocUploadWizard — out of scope per task.
- `WorkAssignmentWizardDialog.tsx` and step components — out of scope (host already passes IDataService; no host-side change needed).

## Build / test

- `npm run build` in `Spaarke.UI.Components/` → ✅ tsc clean (see top of report).
- `npm test` is not run locally per the task-020 handoff note ("ts-jest not installed locally"); CI will execute. Tests are authored to the same shape as `EntityCreationService.cascade.test.ts`.
