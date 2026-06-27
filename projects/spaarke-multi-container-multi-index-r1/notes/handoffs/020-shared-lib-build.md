# Task 020 — Shared-lib build handoff

**Date**: 2026-06-07
**Package**: `@spaarke/ui-components` (v2.1.0)
**Build command**: `npm run build` in `src/client/shared/Spaarke.UI.Components/`
**Result**: ✅ Exit 0 — clean tsc compile.

## What downstream tasks 021–027 / 029 consume

New API surface added to `EntityCreationService` (static helpers — no construction needed).

### Type

```ts
export interface IUserBuCascadeDefaults {
  containerId?: string;       // businessunit.sprk_containerid (undefined if unset)
  searchIndexName?: string;   // businessunit.sprk_searchindexname (undefined if unset)
  businessUnitId?: string;    // BU GUID (undefined only if user has no BU)
}
```

### Static helpers (INV-5 safe — never overwrite explicit values)

```ts
// 1. Resolve current user → BU → defaults
static async resolveUserBuDefaults(
  webApi: IWebApiLike,
  userId: string
): Promise<IUserBuCascadeDefaults>;

// 2. Apply both defaults to a create payload (per-field INV-5 guard)
static applyUserBuDefaults(
  entity: Record<string, unknown>,
  defaults: IUserBuCascadeDefaults | null | undefined
): { containerIdSet: boolean; searchIndexNameSet: boolean };

// 3. Granular helpers (if a wizard wants to apply one field at a time)
static applyDefaultContainerId(
  entity: Record<string, unknown>,
  containerId: string | null | undefined
): boolean; // true if set, false if INV-5-skipped or input empty

static applyDefaultSearchIndexName(
  entity: Record<string, unknown>,
  searchIndexName: string | null | undefined
): boolean;
```

## Canonical usage pattern (for tasks 021–025)

```ts
// In each per-wizard service, immediately after building the entity payload:
const userId = (window.parent as any).Xrm?.Utility?.getUserId()?.replace(/^\{|\}$/g, '');
const defaults = await EntityCreationService.resolveUserBuDefaults(this._dataService, userId);
EntityCreationService.applyUserBuDefaults(entity, defaults);
// Now entity has sprk_containerid + sprk_searchindexname populated from BU
// — but ONLY if they were not already set explicitly on the payload (INV-5).
```

For task 027 (DocumentUploadWizard `buildRecordPayload`) only the searchindexname is needed (no `sprk_containerid` per the document record convention — `sprk_graphdriveid` is the only container-ish field on `sprk_document`):

```ts
EntityCreationService.applyDefaultSearchIndexName(payload, resolvedIndexName);
// resolvedIndexName comes from AssociateToStep's resolveSearchIndexNameForRecord
// (parent → BU fallback chain — task 026).
```

## INV-5 contract

A pre-existing non-empty value on the payload is **sacred** — the helper:
- Skips when the field already has a non-empty string value
- Treats `null`, `undefined`, and whitespace-only strings as "unset" (cascade can fill)
- Treats booleans / numbers (incl. `false` / `0`) as explicit values (cascade skips)

## NULL BU value scenario (Phase A.5 ordering)

If a BU does not yet have `sprk_searchindexname` set (Spaarke Dev 1 / Test 1 per Phase A.5 ordering), `resolveUserBuDefaults` returns `searchIndexName: undefined` and `applyUserBuDefaults` leaves the field unset on the payload. The BFF tenant-default chain handles the fallback server-side. No exception is thrown.

## Build artifacts

```
dist/services/EntityCreationService.d.ts
dist/services/EntityCreationService.d.ts.map
dist/services/EntityCreationService.js
dist/services/EntityCreationService.js.map
```

The new types/methods are visible in `dist/services/EntityCreationService.d.ts` and re-exported from the package barrel (`src/services/index.ts` adds `IUserBuCascadeDefaults` to the type-only re-exports list).

## Tests

`src/services/__tests__/EntityCreationService.cascade.test.ts` — 19 test cases covering:

- `applyDefaultContainerId`: cascade, INV-5, empty input, whitespace/null pre-existing
- `applyDefaultSearchIndexName`: cascade, INV-5, NULL BU value scenario
- `applyUserBuDefaults`: both, per-field INV-5 independence, NULL BU fallback, null defaults
- `resolveUserBuDefaults`: chain, brace-stripping, no-BU case, NULL field normalization, empty-string normalization
- End-to-end: resolve + apply with operator-override scenario

**Note**: tests were authored but NOT executed in this worktree — `ts-jest` is not installed locally. Run `npm install` in `Spaarke.UI.Components/` then `npm test -- src/services/__tests__/EntityCreationService.cascade.test.ts` to validate. CI will run these automatically.

## ADR compliance

- **ADR-012** (shared lib is the contract): helpers added to `@spaarke/ui-components`; no consumer-side duplication required.
- **ADR-021** (Fluent v9): N/A — service layer, no UI.
- **ADR-022** (React version boundaries): no React imports; pure helpers compatible with React 16 (PCF) and React 18 (Code Pages).
- **ADR-028** (Spaarke Auth v2): no BFF calls in this task; resolution uses `IWebApiLike` (Xrm.WebApi or PCF context.webAPI), which is the canonical Dataverse client per design.md and the existing `SummarizeFilesWizard.getBusinessUnitContainerId` precedent.

## Files modified

- `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` (added `IUserBuCascadeDefaults` + 5 static methods, ~170 LOC additive)
- `src/client/shared/Spaarke.UI.Components/src/services/index.ts` (added `IUserBuCascadeDefaults` to the type re-export list)
- `src/client/shared/Spaarke.UI.Components/src/services/__tests__/EntityCreationService.cascade.test.ts` (new, ~280 LOC)
