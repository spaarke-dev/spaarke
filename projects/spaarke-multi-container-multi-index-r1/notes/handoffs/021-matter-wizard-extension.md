# Task 021 — CreateMatterWizard FR-WIZ-01 handoff

**Date**: 2026-06-07
**Package**: `@spaarke/ui-components` (v2.1.0)
**Status**: ✅ Completed (FULL rigor)

## What changed

`src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts`:

1. Added imports:
   - `IWebApiLike` (type) — used to adapt the injected `IDataService` for `EntityCreationService.resolveUserBuDefaults`.
   - `EntityCreationService` (already imported; now also used for the new static cascade helpers).

2. Added two file-local helpers:
   - `_tryGetCurrentUserId()` — best-effort `Xrm.Utility.getUserId()` lookup across `window`, `window.parent`, `window.top` (cross-origin safe). Mirrors the established `SummarizeFilesWizard.getBusinessUnitContainerId` pattern. Returns `null` on failure so the cascade can degrade gracefully.
   - `_toWebApiLike(dataService)` — narrow adapter from `IDataService` to `IWebApiLike` for the cascade resolver.

3. Inside `MatterService.createMatter`, immediately after the existing `entity['sprk_containerid'] = this._containerId;` assignment (the canonical line-216 reference), added a `try/catch` block that:
   - Resolves the current user GUID via `_tryGetCurrentUserId()`.
   - Calls `EntityCreationService.resolveUserBuDefaults(webApi, userId)` to chain `systemuser → businessunit → sprk_searchindexname`.
   - Applies the value via `EntityCreationService.applyDefaultSearchIndexName(entity, buDefaults.searchIndexName)` — INV-5 safe (no-op when payload already has a non-empty value).
   - Logs the outcome (cascaded / preserved / unset / xrm-unavailable / lookup-failed).
   - On any failure, **never aborts matter creation** — the BFF tenant-default chain handles the fallback server-side.

## Helper used: `applyDefaultSearchIndexName`

Granular helper, not `applyUserBuDefaults`. Reason: `sprk_containerid` is sourced from the host-provided `this._containerId` (passed via the `MatterService` constructor as `context.speContainerId`), which is the existing pre-FR-WIZ-01 behavior. Re-running the container cascade from the BU would be redundant and could conflict with host-provided overrides. Only `sprk_searchindexname` needs to come from BU.

## INV-5 evidence

- **Helper level**: `EntityCreationService.applyDefaultSearchIndexName` calls `_hasExplicitValue` which short-circuits when the payload already has a non-empty string at `sprk_searchindexname`. Covered by 5 dedicated tests in `services/__tests__/EntityCreationService.cascade.test.ts` (all green).
- **Service level**: `matterService.ts` exclusively invokes `applyDefaultSearchIndexName` — no direct `entity['sprk_searchindexname'] = ...` assignment. The INV-5 guard cannot be bypassed.
- **Logging**: when INV-5 short-circuits, `[MatterService] sprk_searchindexname already explicitly set on payload — preserving (INV-5).` is logged for traceability.

## Build

```
$ npm run build
> @spaarke/ui-components@2.1.0 build
> tsc
(exit 0 — clean)
```

## Tests

New: `src/components/CreateMatterWizard/__tests__/matterService.cascade.test.ts` — 6 tests, all green:

| # | Test | Asserts |
|---|---|---|
| 1 | `adds sprk_searchindexname to the createRecord payload from the user BU` | FR-WIZ-01 — cascade flows end-to-end through `createMatter` |
| 2 | `preserves an explicit sprk_searchindexname value on the payload (INV-5 / FR-WIZ-08)` | INV-5 helper invoked (BU lookup not bypassed); deeper INV-5 coverage in `EntityCreationService.cascade.test.ts` |
| 3 | `leaves sprk_searchindexname unset when the BU value is NULL` | Phase A.5 ordering scenario (Spaarke Dev 1 / Test 1) — graceful unset, BFF tenant-default applies |
| 4 | `leaves sprk_searchindexname unset when Xrm.Utility.getUserId() is unavailable` | Graceful degradation; matter still created |
| 5 | `does NOT abort matter creation when the BU lookup itself fails` | `try/catch` around the cascade is exercised |
| 6 | `preserves existing sprk_containerid cascade behavior when no host container is provided` | Regression — container cascade untouched |

Regression: existing 19 `EntityCreationService.cascade.test.ts` tests still green.

## Files modified / created

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts` | + 2 imports, + 2 file-local helpers (~55 LOC), + cascade block inside `createMatter` (~50 LOC including comments + logging) |
| `src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/__tests__/matterService.cascade.test.ts` | New file, ~220 LOC, 6 tests |
| `projects/spaarke-multi-container-multi-index-r1/notes/handoffs/021-matter-wizard-extension.md` | This handoff file |

## MCP verification expectation (for task 029 / UAT task 071)

After the next code-page deploy:

1. Run `CreateMatterWizard` from the **Spaarke Demo BU** (a user whose owning BU has `sprk_searchindexname = "spaarke-knowledge-index-v2"`).
2. Complete the wizard (any matter name, e.g. `FR-WIZ-01 verification`).
3. MCP query against the new sprk_matter:
   ```
   SELECT sprk_searchindexname FROM sprk_matter WHERE sprk_matterid = '{new-id}'
   ```
   Expected: `spaarke-knowledge-index-v2`.

If the BU has no `sprk_searchindexname` set (Spaarke Dev 1 / Test 1 per Phase A.5 ordering), the matter is created with `sprk_searchindexname = NULL` and the BFF tenant-default chain applies server-side — no exception is thrown.

## ADR compliance

- **ADR-012**: All wizard logic lives in `@spaarke/ui-components`. Code-page consumers (`LegalWorkspace`, `Spaarke.UI.Components/CreateMatterWizard.tsx`) instantiate `MatterService` and call `createMatter` — no duplication.
- **ADR-021**: N/A — service layer, no UI surface.
- **ADR-022**: No React imports added; pure async helpers. Compatible with React 16 (PCF host) and React 18 (Code Pages).
- **ADR-028**: No BFF calls added by this task. Cascade uses `IDataService` → `IWebApiLike` (Xrm.WebApi-backed in production), which is the canonical Dataverse client per design.md and matches the existing `SummarizeFilesWizard` precedent.

## Deviations

None.

## Blockers for downstream tasks

None. Task 028 (aggregate UAT / INV-5 cross-wizard test suite) can consume `matterService.cascade.test.ts` as the FR-WIZ-01 reference fixture for the matter-side of the suite.
