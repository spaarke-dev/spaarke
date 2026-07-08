# Deferred Issues — spaarke-dataset-grid-framework-r2

> **Created**: 2026-07-02 by main session during Phase 2 close
> **Purpose**: Track deferred work + issues discovered during R2 execution per CLAUDE.md rule
> **Twin**: GitHub Issues (to be filed via `/defer` skill or manually)

---

## Format

- **DEF-{NNN}** — Deferred work (scope item pushed out of R2)
- **ISS-{NNN}** — Issue discovered (bug or gap in adjacent code)

---

## DEF-001 — Wizard test runner setup

**Status**: Deferred to follow-on project
**Discovered by**: Task 011, confirmed by task 013 + 015
**Concrete failure without work**: 3 test files scaffolded in `src/solutions/WorkspaceLayoutWizard/src/__tests__/` (rowHeight.test.tsx, sectionInstanceAdvanced.test.tsx, widthPreferencePlacement.test.tsx) cannot run — wizard package has no `jest.config`, no test script in `package.json`, no `@types/jest` / `@testing-library/react` devDependencies. Tests will accumulate unrunnable until a test-runner setup PR lands.

**Cost of not doing**: FR-02, FR-03, FR-04 wizard behavior has scaffolded tests but no runtime verification. Regression drift possible on subsequent wizard changes.

**Effort**: ~1 hour — add jest.config, install `@testing-library/react` + `@types/jest`, add test script.

**Related**: pre-existing `src/steps/__tests__/TemplateStep.test.tsx` is in the same state — this is a pre-R2 gap, R2 just made it more visible.

---

## DEF-002 — configId picker: real Dataverse query

**Status**: ✅ **RESOLVED** 2026-07-02 (post-wrap-up follow-on). Commit: _(main session fills in hash)_. Implemented Option A per original recommendation — new BFF endpoint `GET /api/dataverse/gridconfigurations/{entityLogicalName}` returns active `sprk_gridconfiguration` records filtered by entity. Wizard's `AdvancedSectionControl` in `src/solutions/WorkspaceLayoutWizard/src/steps/ArrangeStep.tsx` now hydrates the configId Dropdown from BFF via existing `authenticatedFetch`. `SectionMetadata.entityName` field added to `sectionMetadataCatalog.ts` interface + populated on 6 entity-list entries (documents, matters, projects, invoices, work-assignments, communications). Loading + error + graceful-degradation states rendered. New files: `Api/Dataverse/GridConfigurationEndpoints.cs`, `Services/Dataverse/GridConfigurationService.cs`, `Services/Dataverse/Models/GridConfigurationSummaryDto.cs`, `Services/Dataverse/Extensions/GridConfigurationServiceExtensions.cs`, `tests/integration/Sprk.Bff.Api.IntegrationTests/Api/Dataverse/GridConfigurationEndpointsTests.cs` (4 tests). This flips BFF hot-path from N → Y — see `spec.md` + `design.md` Placement Justification.
**Discovered by**: Task 013
**Concrete failure without work**: Wizard "Advanced" panel `configId` dropdown shows ONLY "None (use default)" — cannot select real `sprk_gridconfiguration` records because:
1. Wizard is standalone Vite app with no `Xrm.WebApi` in scope (only `authenticatedFetch` for BFF)
2. `SectionMetadata` doesn't include the section's entity name (needed to filter records)

**Cost of not doing**: FR-03 `configIdOverride` is authorable via JSON edit only, not via wizard UI. Makers cannot pick a config visually.

**Options** (both explored by task 013 agent):
- **Option A (recommended)**: Add BFF endpoint `GET /api/grid-configurations?entity={name}` returning records filtered by entity. Wizard calls via existing `authenticatedFetch`.
- **Option B**: Extend `SectionMetadata` to include entity name + reach `Xrm.WebApi` through the host dialog (bigger change).

**Follow-up location**: `src/solutions/WorkspaceLayoutWizard/src/steps/ArrangeStep.tsx` — search for `PLACEHOLDER STATUS —` comment block.

**Effort**: ~3 hours (Option A: endpoint + wiring) or ~1 day (Option B: metadata extension + host bridging).

---

## DEF-003 — availableViews: TagPicker instead of comma-separated input

**Status**: ✅ **RESOLVED** 2026-07-02 (post-wrap-up follow-on). Commit: _(main session fills in hash)_. Implemented via **reuse of existing** `GET /api/dataverse/savedqueries/{entityLogicalName}` (originally shipped by `spaarke-datagrid-framework-r1` FR-BFF-02) — no new BFF endpoint needed. Wizard's `AdvancedSectionControl` now renders a Fluent v9 `Combobox` in `multiselect` mode instead of a comma-separated `Input`. Selected values track `savedqueryid`s; display shows friendly names (with "(default)" suffix on the entity's default view). Same graceful-degradation semantics as DEF-002. **Chose `Combobox multiselect` over `TagPicker`** — Combobox is already imported by the wizard, has narrower API surface, and produces the same UX + accessibility properties as TagPicker for this bounded (small-N) allowlist use case. Files changed: `src/solutions/WorkspaceLayoutWizard/src/steps/ArrangeStep.tsx` only.
**Discovered by**: Task 013
**Concrete failure without work**: FR-03 `overrides.availableViews` requires makers to type savedquery GUIDs comma-separated. No autocomplete, no visual confirmation the GUIDs match real savedqueries.

**Cost of not doing**: High footgun — typos silently produce empty pickers at render time. Poor maker experience.

**Follow-up**: Once entity name is available (per DEF-002), query savedqueries for the entity via `Xrm.WebApi` or BFF and populate a Fluent v9 `TagPicker`.

**Effort**: ~2 hours (follows DEF-002 pattern).

---

## DEF-004 — Runtime dev-guard: wire warnOnWidthPreferenceViolations into render pipeline

**Status**: Deferred — trivial follow-on
**Discovered by**: Task 015
**Concrete failure without work**: `warnOnWidthPreferenceViolations` is DEFINED and EXPORTED from `src/solutions/LegalWorkspace/src/sectionRegistry.ts` (lines ~231-315), but not called from anywhere. It fires zero warnings at runtime today.

**Cost of not doing**: FR-04 runtime dev-guard is dormant. Wizard-side warnings still fire (bulk of user protection), but JSON-hand-edited layouts that place 'full' widgets in multi-column rows go undetected in dev.

**Follow-up location**: `src/solutions/LegalWorkspace/src/hooks/useWorkspaceLayouts.ts` — add one call to `warnOnWidthPreferenceViolations(parsedLayout)` after `parseLayoutJson` returns.

**Effort**: ~15 minutes.

**Note**: Could be bundled with Phase 3 shared-package extraction (task 021) if the hook file also relocates.

---

## DEF-005 — Consumer factories: consume `context.sectionInstance` overrides

**Status**: Deferred to follow-on project OR bundled with FR-10 shared-package refactor
**Discovered by**: Task 012
**Concrete failure without work**: FR-03 SectionInstance framework surface is complete — the overrides reach the section factory via `context.sectionInstance`. But the 6 entity-list section factories in `src/solutions/LegalWorkspace/src/sections/*.registration.ts` don't yet read the context field. So `configIdOverride`, `overrides.pageSize`, `overrides.availableViews` are populated in state but never reach the DataGrid.

**Cost of not doing**: FR-03 acceptance criteria fail end-to-end. Wizard produces valid `SectionInstance` JSON but overrides have no runtime effect until factories consume them.

**Follow-up**: For each of the 6 registrations, wire:
- `configId` prop: pass `context.sectionInstance?.configIdOverride ?? BAKED_IN_ID`
- Optionally: forward `context.sectionInstance?.overrides` to a new `<DataGrid overrides={...} />` prop OR consume via `resolveEffectivePageSize` + `resolveEffectiveAvailableViews` helpers (already added to `configResolution.ts` by task 012).
- Section header label: read `context.sectionInstance?.label` (may require WorkspaceShell to pass through instead of section factory).

**Effort**: ~2-3 hours (all 6 files follow same pattern; parallel subagents work well).

---

## ISS-001 — Pre-existing baseline type errors in App.tsx

**Status**: Pre-existing, not caused by R2
**Discovered by**: Task 011 (first noticed)
**Details**: `src/solutions/WorkspaceLayoutWizard/src/App.tsx` has 3 type errors that predate R2:
- Line 107 → shifted to line 116 after 011: FluentIcon nested-node_modules duplication
- Line 309 → shifted to 426: `scope` field on LayoutJson (undocumented)
- Line 660 → shifted to 793: missing `title` prop

**Cost of not fixing**: Baseline noise makes future type-check regressions harder to spot. Not blocking, but recommended for hygiene.

**Effort**: ~1 hour (investigate each; may need dependency updates or type fixes).

---

## ISS-002 — Pre-existing test drift in sectionMetadataCatalog.test.ts

**Status**: Pre-existing, not caused by R2
**Discovered by**: Task 001, confirmed by tasks 005, 014
**Details**: `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/__tests__/sectionMetadataCatalog.test.ts` line 26-35 asserts exactly 7 canonical sections; actual catalog has 13 entries. Test has been failing on master for a while, unrelated to R2.

**Cost of not fixing**: Ongoing red X in CI test summary. Masks new regressions on the file.

**Effort**: ~15 minutes.

---

## ISS-003 — LegalWorkspace + SpaarkeAi + WorkspaceLayoutWizard vite build depends on shared-lib peer packages built

**Status**: Pre-existing project-wide observation
**Discovered by**: Task 005 (LegalWorkspace vite build), confirmed by task 011 (WorkspaceLayoutWizard build)
**Details**: Running `npm run build` in any of the 3 Vite code pages standalone fails because sibling packages (`@spaarke/auth`, `@spaarke/ui-components`, `@spaarke/legal-workspace`, `@spaarke/ai-widgets`, `@spaarke/document-operations`, `@spaarke/sdap-client`, etc.) don't have `dist/` folders locally. Local dev requires `scripts/Build-AllClientComponents.ps1` to be run first.

**Cost of not documenting/fixing**: New contributors hit "@spaarke/auth cannot be resolved" errors and don't know to run the orchestrator first.

**Follow-up**: Add a preflight check to the standalone `npm run build` scripts OR document in a WORKSPACE-BUILD.md guide.

**Effort**: ~1 hour (script check) or ~30 min (doc).

---

*Update this file whenever a deferred item is resolved. Cite the resolving PR/commit.*
