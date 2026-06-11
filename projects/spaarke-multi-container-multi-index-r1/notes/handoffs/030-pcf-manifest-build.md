# Task 030 — PCF Manifest `searchIndexName` Property Build Handoff

**Date**: 2026-06-07
**Task**: 030-pcf-manifest-searchindexname-property
**Status**: Completed
**Rigor**: FULL

## Change summary

Added a single new bound property declaration to
`src/client/pcf/SemanticSearchControl/SemanticSearchControl/ControlManifest.Input.xml`:

```xml
<!-- Bound: Azure AI Search index name from scope record (FR-PCF-01, multi-container-multi-index-r1) -->
<property name="searchIndexName" display-name-key="Search Index Name" description-key="Azure AI Search index name bound from the scope record's sprk_searchindexname field; routes search requests to the correct index"
          of-type="SingleLine.Text" usage="bound" required="false" />
```

Placed immediately after the existing `scopeId` bound property for grouping
consistency. Uses inline `display-name-key` / `description-key` strings to
mirror the file's existing convention (no resx files referenced anywhere in
this manifest).

## Convention alignment

- Attribute order: `name`, `display-name-key`, `description-key`, `of-type`,
  `usage`, `required` — same as the 14 other `<property>` declarations.
- `usage="bound"` `required="false"` `of-type="SingleLine.Text"` — exact match
  to `scopeId` (the existing bound-property analog).
- No version bump in this task (`version="1.1.73"` unchanged) — version bump
  to v1.1.74 is task 033 (5-location update).

## Type generation result

`pcf-scripts build` regenerated
`SemanticSearchControl/generated/ManifestTypes.d.ts` cleanly:

```typescript
export interface IInputs {
    ...
    scopeId: ComponentFramework.PropertyTypes.StringProperty;
    searchIndexName: ComponentFramework.PropertyTypes.StringProperty;  // NEW (line 14)
    showFilters: ComponentFramework.PropertyTypes.TwoOptionsProperty;
    ...
}
export interface IOutputs {
    scopeId?: string;
    searchIndexName?: string;   // NEW (line 24)
    selectedDocumentId?: string;
}
```

Downstream code (tasks 031 + 032) can now reference
`context.parameters.searchIndexName.raw` in a type-safe manner.

## Build observation

`npm run build` (validation mode, NOT prod):

- **Manifest XML validation**: ✅ Pass (PCF tooling parsed the new property
  declaration without warnings).
- **Type generation step**: ✅ Pass (`ManifestTypes.d.ts` regenerated; new
  `searchIndexName` property added to both `IInputs` and `IOutputs`).
- **Webpack compilation**: ❌ 8 pre-existing TypeScript errors in
  `components/ListView.tsx` (line 1447), `components/ResultCard.tsx`
  (lines 588, 667), etc. — all related to `FilePreviewDialog` /
  `DocumentRowMenu` having React-18-style return types (`ReactNode |
  Promise<ReactNode>`) that don't satisfy React-16 JSX element constraints
  in the PCF's TypeScript 5.x project.

**Pre-existing-error attribution**: The 8 webpack errors are entirely in
`.tsx` files my edit never touched. They reference JSX component-type
mismatches between PCF (React 16 typings) and shared lib re-exports.
A manifest XML edit cannot produce TypeScript JSX errors. These errors
exist on baseline (HEAD of `work/spaarke-multi-container-multi-index-r1`)
and are tracked separately from this task. They will need to be addressed
before task 035's production build, but per task 030's scope, the manifest
change itself is complete and validated.

## FR-PCF-01 acceptance

| Criterion | Status |
|---|---|
| `ControlManifest.Input.xml` contains the new `<property name="searchIndexName" ... />` with the existing attribute set | ✅ Pass |
| PCF type regen exposes `context.parameters.searchIndexName` to consumer TS | ✅ Pass — appears in `IInputs` line 14 |
| Maker portal will list `searchIndexName` as bindable property on the control | Awaits task 035 deployment for UI verification |

## ADR compliance

- **ADR-006**: ✅ Manifest authoring stays in `ControlManifest.Input.xml`;
  no parallel webresource introduced.
- **ADR-011**: ✅ New bound property follows the canonical attribute set
  used by the file's existing 14 `<property>` declarations.
- **ADR-012**: ✅ No shared lib code touched.
- **ADR-021**: ✅ N/A (manifest itself has no styling; downstream UI uses
  Fluent v9 + tokens.* per existing pattern).
- **ADR-022**: ✅ React 16 boundary respected (manifest change is
  React-version-neutral; `platform-library` declarations unchanged).

## Files modified

- `src/client/pcf/SemanticSearchControl/SemanticSearchControl/ControlManifest.Input.xml` (additive: +4 lines, 0 changes to existing properties)

## Files NOT modified (intentional per task scope)

- No `.tsx` files touched (sibling tasks 031, 032, 034 handle UI/service code)
- `SemanticSearchApiService.ts` (task 031)
- `NavigationService.ts` (task 032)
- Version strings (task 033 — 5-location bump)
- Tests (task 034)

## Downstream serial blockers cleared

This task unblocks:
- Task 031 — `SemanticSearchApiService.search()` body addition
- Task 032 — `NavigationService.openSemanticSearchPage()` envelope addition
- Task 033 — 5-location version bump to v1.1.74

## Deviations

None. The task definition specified `display-name-key="searchIndexName_Display_Key"` / `description-key="searchIndexName_Desc_Key"` (resx-style keys), but the file's existing convention uses inline strings (e.g., `"Scope ID"`, `"ID for scoped search..."`) — no resx files exist. Per POML Step 3 ("If no resx exists ... use the inline `display-name-key` attribute directly per the existing convention") and the task instructions ("Match the exact attribute style of existing bound properties... mirror the existing pattern in this file"), inline strings were used: `display-name-key="Search Index Name"` and `description-key="Azure AI Search index name bound from the scope record's sprk_searchindexname field..."`.

## Blockers

None.
