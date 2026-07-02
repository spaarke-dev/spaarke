# Spaarke DataGrid Framework R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-02
> **Source**: `projects/spaarke-dataset-grid-framework-r2/design.md`
> **Predecessor**: `projects/spaarke-datagrid-framework-r1/` (framework R1, shipped 2026-06)

---

<hot-path-declaration>
BFF: N — no changes to `src/server/api/Sprk.Bff.Api/` or its consumed shared libs.
SpaarkeAi: Y — FR-08 rebuild reaches SpaarkeAi via shared-lib update; FR-10 (Issue 12 Option B) restructures how `@spaarke/legal-workspace` is consumed by SpaarkeAi's Vite build.
ci-workflows: N — no changes to `.github/workflows/**`.
skill-directives: Y — FR-09 (Issue 12 Option A) adds a dual-deploy warning to the `code-page-deploy` skill.
root-CLAUDE.md: N — no changes to root `CLAUDE.md`.
</hot-path-declaration>

Registry: [`projects/INDEX.md`](../INDEX.md) will need an entry for this project (added by `project-pipeline`).

---

## Executive Summary

The Spaarke DataGrid framework (R1) shipped `<DataGrid configId=... />` as the canonical shared-lib grid driven by `sprk_gridconfiguration` records. Production use in `ai-spaarke-ai-workspace-UI-r2` surfaced 11 gaps — the load-bearing one is a broken height chain that causes multi-section workspace grids to grow unbounded. R2 delivers additive framework capabilities (no `_version` bump), unwinds a tactical CSS `maxHeight` hack from the 6 entity-list section registrations, and — per owner clarification 2026-07-02 — extracts LegalWorkspace's section registry into a proper shared package so SpaarkeAi's build no longer aliases into a sibling workspace's source tree.

Ships as **two independently-mergeable PRs**. Estimated total effort: **~3.5 days** (design's 2.5 days + 1 day for the FR-10 shared-package extraction adopted post-design).

---

## Scope

### In Scope

- **FR-01** Framework height-chain fix (`contentSizing: 'grow' | 'clamped'` on `SectionMetadata`)
- **FR-02** Per-row `rowHeight` override in `LayoutJsonRow` + wizard UI
- **FR-03** `SectionInstance` schema for per-instance `configId` / `label` / behavior overrides + wizard "Advanced" panel (folds design Issue 6 rename)
- **FR-04** `widthPreference: 'full' | 'half' | 'any'` on `SectionMetadata` + wizard placement warnings + runtime dev-guard
- **FR-05** `availableViews` allowlist on `SourceSavedQuery` for `<ViewSelector>` filtering
- **FR-06** Three DataGrid config templates in `scripts/config-templates/` + guide update
- **FR-07** `pageSize` default alignment — code changes from 100 to 25 (owner clarification 2026-07-02) + doc updated to match
- **FR-08** Unwind tactical `maxHeight` hack from 6 entity-list section registration files + set `contentSizing: 'clamped'` in metadata catalog
- **FR-09** Add dual-deploy warning to `code-page-deploy` skill and `BUILD-A-NEW-WORKSPACE-WIDGET.md` (Issue 12 Option A)
- **FR-10** Extract LegalWorkspace section registry into `@spaarke/legal-workspace` shared package under `src/client/shared/`; migrate SpaarkeAi's Vite alias to a normal package dependency (Issue 12 Option B — adopted 2026-07-02)

### Out of Scope

- Editable-cells feature (deferred; separate future project)
- Server-side aggregations (use BFF endpoints or savedquery rollups)
- Legacy `IGridConfigJson` consumer retirement (owned by `spaarke-datagrid-framework-r1` backlog)
- Any `sprk_configjson` `_version` bump — additive fields only, remains `'1.0'`
- Any BFF endpoint / service / DI change (no BFF surface touched — hot-path BFF=N)
- Hiding the DataGrid scrollbar (Issue 10 — decision to defer, no code change)
- Retirement of `RecordNavigationModalShell` or any Layout 2 surface
- Any change to `ai-spaarke-ai-workspace-UI-r2`'s shipped Layout 1 unification

### Affected Areas

- **Framework (shared lib)** — `src/client/shared/Spaarke.UI.Components/src/`
  - `types/DataGridConfiguration.ts` — new schema fields (`availableViews`, `SectionInstance`, per-row `rowHeight`)
  - `components/WorkspaceShell/buildDynamicWorkspaceConfig.ts` — `contentSizing` sizing logic (~lines 167-171)
  - `components/WorkspaceShell/sectionMetadataCatalog.ts` — `contentSizing: 'clamped'` on 6 entity-list entries + `widthPreference` defaults
  - Runtime dev-guard for `widthPreference` — target file identified during plan (currently `src/solutions/LegalWorkspace/src/sectionRegistry.ts`; may relocate to shared-package under FR-10)
  - `components/DataGrid/configResolution.ts` — `availableViews` allowlist filter
  - `components/DataGrid/DataGrid.tsx` — `pageSize` default fallback (~line 847)
- **LegalWorkspace consumer** — `src/solutions/LegalWorkspace/src/sections/`
  - 6 entity-list registration files: `communications.registration.ts`, `documents.registration.ts`, `invoices.registration.ts`, `matters.registration.ts`, `projects.registration.ts`, `workAssignments.registration.ts`
  - `src/solutions/LegalWorkspace/src/workspaceConfig.tsx` — registry export contract (impacted by FR-10 extraction)
- **SpaarkeAi consumer** — `src/solutions/SpaarkeAi/`
  - `vite.config.ts` — remove `@spaarke/legal-workspace` source alias (replaced by shared-package dependency in FR-10)
- **New shared package** (FR-10) — `src/client/shared/Spaarke.LegalWorkspace.Registry/` (name TBD in plan; must match existing naming conventions)
- **Wizard code page** — `src/solutions/WorkspaceLayoutWizard/src/App.tsx`
  - New UI for `rowHeight`, `SectionInstance` "Advanced" panel, `widthPreference` placement checks
- **Configuration guide** — `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` — new fields into Step 5 subsections; templates referenced in Step 2
- **Widget authoring guide** — `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — § 7.2 replaced by `contentSizing`; § 12 dual-deploy warning
- **Architecture doc** — `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` — § 6.5 extended with new fields
- **Deploy skill** — `.claude/skills/code-page-deploy/SKILL.md` — dual-deploy warning for consumer relationships
- **Config templates** (new) — `scripts/config-templates/entity-list-basic.json`, `entity-list-drill-through.json`, `entity-list-full.json`

---

## Requirements

### Functional Requirements

1. **FR-01 (Issue 1, P0, load-bearing)** — Framework `SectionMetadata` gains a `contentSizing?: 'grow' | 'clamped'` field, default `'grow'` for back-compat. `buildDynamicWorkspaceConfig.ts` applies `defaultHeight` as `max-height` + `overflow: hidden` + `display: flex` when `'clamped'`, as `min-height` when `'grow'` (existing behavior). No breaking change for existing sections.
   - **Acceptance**: Unit test on `buildDynamicWorkspaceConfig` verifying both branches. Manual regression: Dashboard II with `contentSizing: 'clamped'` sections shows visible Fluent scrollbar + lazy-load-on-scroll behavior in Communications (currently the P0 symptom).

2. **FR-02 (Issue 2, P0)** — `LayoutJsonRow` (Dataverse `sprk_workspacelayout.sprk_sectionsjson`) accepts optional `rowHeight?: string`. When set, row is clamped to that height and sections respect the ceiling regardless of `contentSizing`. `WorkspaceLayoutWizard` exposes a `rowHeight` input with presets (`auto`, `40vh`, `60vh`, `80vh`, `100vh`).
   - **Acceptance**: Type test on `LayoutJsonRow`. A wizard-authored row with `rowHeight: '80vh'` renders at 80% viewport height. Documents system layout (single-section full-page) with `rowHeight: '80vh'` uses most of the tab height instead of the old 480px waste.

3. **FR-03 (Issues 3 + 6, P1)** — `LayoutJsonRow.sections` widens from `string[]` to `Array<string | SectionInstance>` where `SectionInstance = { id: string; configIdOverride?: string; label?: string; overrides?: { pageSize?: number; availableViews?: string[] } }`. Bare-string entries continue to work (treated as `{ id: X }`). `WorkspaceLayoutWizard` exposes an "Advanced" panel per placed section with fields for configId picker (dropdown of `sprk_gridconfiguration` records for the section's entity), label override, `pageSize` override, `availableViews` allowlist.
   - **Acceptance**: Type test on widened `LayoutJsonRow.sections`. Placing a Communications section with `configIdOverride` renders a different config record than the section's baked-in default. Setting `label: "Email"` renders "Email" in the section header + picker chip while metadata `label` stays "Communications". Bare-string back-compat regression on the 8+ existing system layouts.

4. **FR-04 (Issue 4, P1)** — `SectionMetadata` gains `widthPreference?: 'full' | 'half' | 'any'`, default `'any'`. Wizard blocks / prompts on invalid placements: dropping a `'full'` widget into a multi-slot row shows a modal offering to convert the row to single-column; dropping a `'half'` widget into a single-slot row shows a subtle warning icon + tooltip. Runtime dev-guard in `sectionRegistry.ts` logs `console.warn` when a `'full'` widget renders in a multi-column row.
   - **Owner clarification (2026-07-02)**: All 6 entity-list widgets ship with `widthPreference: 'full'` (Communications, Documents, Invoices, Matters, Projects, Work Assignments). Any half-width use requires the wizard operator to explicitly proceed past the warning.
   - **Acceptance**: Unit test on the runtime guard. Wizard integration test on the placement modal. Regression: existing multi-column dashboards that place any entity-list widget in a `1fr 1fr` row prompt the wizard warning on next edit (but don't retroactively break existing published layouts).

5. **FR-05 (Issue 5, P1)** — `SourceSavedQuery` gains optional `availableViews?: string[]`. When set, `configResolution.ts` filters the `retrieveSavedQueriesForEntity` result before returning it to `<ViewSelector>`. When absent, all siblings show (back-compat). Interacts with FR-03's per-instance `availableViews` — the per-instance value takes precedence over the global config-level allowlist when both are set.
   - **Acceptance**: Unit test on the allowlist filter (both present + absent branches). Runtime regression: Communications config with `availableViews: ['<active-communications-guid>']` shows only that view in the picker.

6. **FR-06 (Issue 7, P2)** — Three files exist under `scripts/config-templates/`:
   - `entity-list-basic.json` — minimum viable (source + display + rowOpen + behavior.pageSize)
   - `entity-list-drill-through.json` — parent-context filter + column overrides + secondaryActions
   - `entity-list-full.json` — every field with sensible defaults + `$comment` annotations per field
   Each is a valid `sprk_configjson` payload. `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` § Step 2 references them as the recommended starting point.
   - **Acceptance**: All three JSON files parse against the current `DataGridConfiguration` TypeScript type. Guide's § Step 2 links point to real files.

7. **FR-07 (Issues 8 + 11, P2, owner-clarified 2026-07-02)** — Framework `pageSize` default changes from `100` to `25` in `DataGrid.tsx` (the `?? 100` fallback becomes `?? 25`). Doc comment on `DataGridConfiguration.ts:329` updated to `Default 25` and cross-referenced in the configuration guide. The 6 entity-list widget config records that today explicitly set `pageSize: 25` may keep the explicit setting (no functional change) OR remove it (rely on new default) — either is acceptable during the migration.
   - **Owner rationale (2026-07-02)**: Workspace embedding is the dominant use case; drill-through / full-page consumers that want 100 can override explicitly.
   - **Acceptance**: Unit test on the fallback constant. Runtime regression: an entity-list widget with `behavior.pageSize` omitted from its config record loads 25 rows on page 1 and triggers page 2 on scroll (currently loads 100 rows and never scrolls).

8. **FR-08 (Issue 9, P3)** — After FR-01 ships, remove the tactical `maxHeight: '480px'` + `display: 'flex'` additions from the `style` blocks of the 6 entity-list section registration files. Add `contentSizing: 'clamped'` to those 6 entries in `sectionMetadataCatalog.ts`. Redeploy LegalWorkspace + (via FR-10) SpaarkeAi.
   - **Acceptance**: The 6 registration files have zero `maxHeight` occurrences (grep verifiable). The 6 metadata entries have `contentSizing: 'clamped'`. Manual regression on Dashboard II Communications matches FR-01 acceptance. Documents system layout no longer shows the tactical 480px waste; combined with FR-02 `rowHeight: '80vh'`, uses most of the tab height.

9. **FR-09 (Issue 12 Option A, P2)** — `.claude/skills/code-page-deploy/SKILL.md` gains a dual-deploy warning section: "When deploying a code page whose source is aliased by another code page's Vite config, both consumers must be rebuilt + redeployed. Known cases: LegalWorkspace ← SpaarkeAi (as of 2026-07). Verify by grepping the target code page's `vite.config.ts` for `resolve.alias` entries pointing at another `src/solutions/*/src`." Companion note in `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`.
   - **Acceptance**: Skill file contains the warning section. Guide contains the note. When a future project touches LegalWorkspace section registrations, the skill trigger surfaces the warning to the executing agent.

10. **FR-10 (Issue 12 Option B, P1, adopted 2026-07-02 per owner clarification)** — Extract the LegalWorkspace section registry (`workspaceConfig.tsx` + section metadata + registration exports) into a new shared package under `src/client/shared/`. SpaarkeAi's `vite.config.ts` removes the `@spaarke/legal-workspace/src` and `@spaarke/legal-workspace` source aliases and consumes the shared package via a normal file: dependency. After this ships, editing a section registration in the shared package updates SpaarkeAi via a normal shared-lib rebuild — no LegalWorkspace-specific rebuild needed for SpaarkeAi to pick up the change.
   - **Interaction with FR-08 + FR-09**: When FR-10 ships, FR-09's warning becomes obsolete for the SpaarkeAi ← LegalWorkspace pair specifically. Keep the warning in place (still applies to any future aliased-source setups); update the "Known cases" list to note the LegalWorkspace pair was resolved.
   - **Acceptance**: New shared package builds independently and publishes types. SpaarkeAi's `vite.config.ts` no longer contains `@spaarke/legal-workspace` source aliases. Editing a section registration file (still located in `src/solutions/LegalWorkspace/src/sections/`) OR its new shared-package equivalent is picked up by BOTH LegalWorkspace's build AND SpaarkeAi's build via the normal shared-lib mechanism. Regression: both code pages render Dashboard II identically after redeploy.

### Non-Functional Requirements

- **NFR-01 (schema stability)** — All schema additions are additive only. `sprk_configjson._version` stays `'1.0'`. Bare-string / omitted-field defaults preserve behavior for every existing `sprk_gridconfiguration` and `sprk_workspacelayout` record. No manual data migration required.
- **NFR-02 (PCF compatibility)** — Framework code stays React-16-safe (no React 18/19-specific hooks, no `use()` API, no `useSyncExternalStore` in shared lib code that PCF consumers reach). `@spaarke/ui-components` remains a valid PCF dependency.
- **NFR-03 (Fluent v9 only)** — No Fluent UI v8 imports introduced. Native scrollbar retained (per Issue 10 decision).
- **NFR-04 (testing)** — All new tests classified per [ADR-038](../../docs/adr/ADR-038-testing-strategy.md) 6 KEEP categories. Framework-contract tests on `buildDynamicWorkspaceConfig` (FR-01), `configResolution` allowlist (FR-05), `DataGrid` pageSize fallback (FR-07) are MAINTAIN-class. Wizard UI regression tests (FR-02, FR-03, FR-04) are MAINTAIN-class (behavior tests, not DI-registration tests). `Mock<HttpMessageHandler>` prohibited; use real `IDataverseClient` adapter fakes.
- **NFR-05 (build hygiene)** — SpaarkeAi build after FR-10 completes in ≤ current baseline time (no regressions from added shared-package resolution). No new HIGH-severity CVEs (`npm audit --production` clean for the new shared package).
- **NFR-06 (PR reviewability)** — Ships as ≥2 phased PRs. PR 1: FR-01, FR-05, FR-07, FR-08 (framework schema + unwind hack + pageSize + allowlist — no wizard, no extraction). PR 2: FR-02, FR-03, FR-04 (wizard + per-instance overrides). PR 3 (new, per FR-10): shared-package extraction + FR-09 documentation. Each PR independently mergeable and reversible.
- **NFR-07 (regression surface)** — Explicit regression tests on Communications, Documents, Invoices, Matters, Projects, Work Assignments in Dashboard II AND their respective single-section full-page system layouts. Both surfaces must render correctly before wrap-up.

---

## Technical Constraints

### Applicable ADRs

- **ADR-012 (shared component library)** — Framework changes stay in `@spaarke/ui-components`; consumers (LegalWorkspace, SpaarkeAi, WorkspaceLayoutWizard) redeploy independently. FR-10 adds a new shared package (`@spaarke/legal-workspace` or equivalent) under the same governance.
- **ADR-021 (Fluent UI v9)** — No v8 imports; Fluent v9 native scrollbar retained per Issue 10.
- **ADR-022 (React 19 / PCF compat)** — Shared-lib framework code stays React-16-safe for PCF consumer compatibility.
- **ADR-028 (Spaarke Auth v2)** — No auth surface touched; existing `Xrm.WebApi` via `XrmDataverseClient` continues.
- **ADR-038 (testing strategy)** — New tests are MAINTAIN-class per the 6 KEEP categories (framework-contract tests). No `Mock<HttpMessageHandler>`. No DI-registration tests. No coverage-as-gate.
- **CLAUDE.md §10 BFF Hygiene** — Not triggered (BFF hot-path = N). No BFF endpoints / services / DI added. No publish-size verification needed.
- **CLAUDE.md §11 Component Justification** — FR-10 introduces a new shared package. Three-question template answered in the plan-time WBS (project-pipeline Step 2 / task-create Step 3.5.6).

### MUST Rules

- ✅ MUST preserve `sprk_configjson._version = '1.0'`; ALL new fields are optional; ALL omitted-field defaults reproduce current behavior.
- ✅ MUST keep bare-string entries valid in `LayoutJsonRow.sections` (backward-compat).
- ✅ MUST run all changes through both LegalWorkspace AND SpaarkeAi build+deploy validation before marking any FR complete (until FR-10 ships and consolidates the dual-deploy requirement).
- ✅ MUST classify all new tests per ADR-038 6 KEEP categories in the wrap-up test diet (`/test-diet` gate).
- ✅ MUST keep `@spaarke/ui-components` React-16-safe (no React 18/19-only APIs in code PCF consumers reach).
- ❌ MUST NOT bump `sprk_configjson._version`.
- ❌ MUST NOT add BFF endpoints, services, or DI registrations.
- ❌ MUST NOT introduce Fluent UI v8 imports.
- ❌ MUST NOT delete existing `sprk_gridconfiguration` records or reshape their JSON payloads (additive schema evolution only).
- ❌ MUST NOT retire the `code-page-deploy` dual-deploy warning even after FR-10 ships (still applies to any future aliased-source pair).

### Existing Patterns to Follow

- **DataGrid framework** — [`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md) (§ 6.5 for schema shape).
- **Height-chain contract** — [`docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) § 7.2 (load-bearing prior art for FR-01; will be updated to describe `contentSizing` as the framework-level replacement).
- **Configuration guide** — [`docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](../../docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md) (already refreshed 2026-07-01; will be extended with new-field subsections).
- **Section registration pattern** — see any of the 6 files under [`src/solutions/LegalWorkspace/src/sections/`](../../src/solutions/LegalWorkspace/src/sections/) — same file shape post-FR-10 (registrations either continue to live under LegalWorkspace/src/sections/ OR move into the new shared package; plan decides).
- **Shared package pattern** — see existing packages under [`src/client/shared/`](../../src/client/shared/) (e.g., `Spaarke.UI.Components`, `Spaarke.Auth`, `Spaarke.AI.Widgets`) for build/publish conventions FR-10 must follow.

---

## ADR Tensions (per CLAUDE.md §6.5)

Evaluated at design time. Two candidate tensions surfaced; both resolve as **Path C (comply — no exception needed)** on inspection:

| ADR | Rule considered | Analysis | Resolution |
|---|---|---|---|
| ADR-022 (React 19 / PCF compat) | Shared lib must stay React-16-safe for PCF consumers | New wizard UI (FR-02/03/04) is authored inside `src/solutions/WorkspaceLayoutWizard/` (a Vite React 18+ code page), NOT in the shared lib. Framework schema changes are pure TypeScript types. No new hooks in shared-lib code. | Path C — compliant. |
| CLAUDE.md §11 Component Justification | New components must justify existence | FR-10 introduces one new shared package. Three-question answer: (1) overlaps with nothing today — SpaarkeAi aliases INTO LegalWorkspace's source tree; (2) can't extend because the alias IS the extension mechanism and it's the failure mode; (3) concrete cost-of-doing-nothing = 30+ min of debug time on every future section-registration change + operator confusion. | Path C — proceed with the new package. Justification cited in FR-10 acceptance. |

> No project-scoped ADR exceptions (Path A) or ADR amendments (Path B) required for R2. This section may be updated if a tension emerges during task execution.

---

## Success Criteria

1. [ ] **FR-01** — `buildDynamicWorkspaceConfig` unit test passes both `'grow'` and `'clamped'` branches. Dashboard II Communications shows visible scrollbar + page-2 fetch on scroll. Verify by: automated test + manual DevTools height-chain inspection.
2. [ ] **FR-02** — `LayoutJsonRow.rowHeight` accepted by type + parser. Documents system layout with `rowHeight: '80vh'` fills the tab. Wizard UI produces valid JSON. Verify by: type test + wizard integration test + manual E2E.
3. [ ] **FR-03** — `SectionInstance` shape accepted; bare-string entries still valid. Communications with `configIdOverride` uses the alt config; label rename shows in header. Verify by: unit test on schema parser + wizard integration test + regression on 8+ existing system layouts.
4. [ ] **FR-04** — All 6 entity-list widgets ship with `widthPreference: 'full'`. Wizard blocks / warns on invalid placements. Runtime dev-guard logs on unavoidable violations. Verify by: unit test on guard + wizard integration test.
5. [ ] **FR-05** — Config-level `availableViews` filter works; per-instance override (from FR-03) takes precedence. Verify by: unit test on `configResolution` allowlist filter + manual `<ViewSelector>` check.
6. [ ] **FR-06** — Three template JSONs exist under `scripts/config-templates/` and parse against current `DataGridConfiguration` type. Guide references them. Verify by: automated JSON parse test in CI + grep on guide.
7. [ ] **FR-07** — `DataGrid.tsx` fallback is `?? 25`; doc comment matches. Entity-list widget without explicit `pageSize` loads 25 + scrolls. Verify by: unit test on fallback + manual DevTools + regression on all 6 widgets.
8. [ ] **FR-08** — Grep on the 6 registration files finds zero `maxHeight` occurrences. `sectionMetadataCatalog.ts` has `contentSizing: 'clamped'` on 6 entries. Verify by: grep + Dashboard II regression + Documents-full-page regression.
9. [ ] **FR-09** — `code-page-deploy` skill file contains dual-deploy warning section. `BUILD-A-NEW-WORKSPACE-WIDGET.md` § 12 (or similar) has companion note. Verify by: grep.
10. [ ] **FR-10** — New shared package builds independently and publishes types. `src/solutions/SpaarkeAi/vite.config.ts` contains no `@spaarke/legal-workspace` source aliases. LegalWorkspace + SpaarkeAi builds both consume the shared package. Dashboard II identical on both post-redeploy. Verify by: build succeeds + grep on vite.config + regression + shared-package build hygiene (types published, no CVEs).
11. [ ] **Wrap-up** — All new tests classified via `/test-diet` per ADR-038. `code-review` + `adr-check` clean on wrap-up task. Both PRs pass CI. LegalWorkspace + SpaarkeAi + WorkspaceLayoutWizard deployed to dev + smoke-tested.

---

## Dependencies

### Prerequisites

- **R1 framework** — `projects/spaarke-datagrid-framework-r1/` shipped; `<DataGrid configId=... />` + `sprk_gridconfiguration` schema exist.
- **`ai-spaarke-ai-workspace-UI-r2`** — 6 entity-list widgets shipped; are the primary regression surface for FR-01 + FR-08.
- **`ai-spaarke-ai-workspace-UI-r2` follow-up PR #531** — 4 new System Workspace Layouts (Communications, Projects, Invoices, Work Assignments); regression surface for FR-02 + FR-08.
- **Deploy scripts** — `Deploy-CorporateWorkspace.ps1`, `Deploy-AllDataGridConsumers.ps1`, `Deploy-SystemWorkspaceLayouts.ps1` exist and work.

### External Dependencies

- None. No new NuGet / npm packages required. No new Azure resources. No new Dataverse entities.

---

## Owner Clarifications

*Answers captured during design-to-spec interview 2026-07-02:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| pageSize default (Issue 11) | 100 / 50 / 25 / context-aware? | **25 (matches majority workspace-widget use case)** | Merges into FR-07 as a code change (from 100 → 25) alongside the Issue 8 doc alignment. Drill-through / full-page consumers now explicitly override to 100 if they want the old default. |
| Issue 12 scope | Option A only / A+B / A+C? | **Option A + Option B (both in R2)** | FR-10 added to scope. R2 expands from ~2.5 days to ~3.5 days. Ships as a third PR after PR 2. |
| widthPreference defaults (Issue 4) | Design's split / all 'any' / all 'full'? | **All 6 default to 'full' (dense grids look best at full width)** | FR-04 acceptance: all 6 entity-list widgets ship with `widthPreference: 'full'`. Wizard warns on any half-width placement of these widgets. Existing multi-column dashboards keep working (retroactive migration is opt-in during next edit). |

---

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **FR-10 package name** — Assuming `@spaarke/legal-workspace` in `src/client/shared/Spaarke.LegalWorkspace.Registry/` (or equivalent PascalCase folder). Plan / project-pipeline decides final naming per Spaarke conventions.
- **FR-10 registration file location** — Assuming section registration source (`*.registration.ts`) files remain under `src/solutions/LegalWorkspace/src/sections/` and are imported/re-exported by the new shared package's index; OR they move into the shared package outright. Plan decides after inspecting the actual coupling.
- **FR-04 retroactive migration** — Assuming existing published `sprk_workspacelayout` records that place `'full'`-preferred widgets in multi-column rows continue to work (runtime dev-guard warns but does not block render). Wizard authoring surfaces the warning only on next edit of that layout.
- **FR-06 template file names** — Using the design's proposed names verbatim. Rename during plan if a shorter convention is preferred.
- **Test placement** — Assuming framework-contract tests go under `src/client/shared/Spaarke.UI.Components/src/**/__tests__/` alongside the code being tested; wizard tests go under `src/solutions/WorkspaceLayoutWizard/src/**/__tests__/`. Plan confirms per existing conventions.
- **Deploy sequencing** — Assuming PR 3 (FR-10) rebases on top of PR 2 (wizard) which rebases on PR 1 (framework). Any PR can ship independently to master if others are delayed; FR-08 unwind only runs after FR-01 lands.
- **Wizard scope in R2** — Assuming FR-02, FR-03, FR-04 wizard work stays in R2 (per PR 2 deployment strategy in the design). If any prove larger than the design's estimates, plan may split further.

---

## Unresolved Questions

*Still need answers before or during implementation:*

- [ ] **FR-10 naming convention** — Blocks: shared-package folder + npm-name creation. Confirm during `project-pipeline` Step 2 resource discovery by inspecting existing `src/client/shared/**` naming.
- [ ] **FR-10 test placement** — Blocks: whether the new shared package gets its own test folder or reuses `Spaarke.UI.Components` conventions. Decide during PR 3 authoring.
- [ ] **FR-04 wizard modal wording** — Blocks: nothing critical, but the "convert row to single-column" modal needs final copy. Wizard UX pass can decide.
- [ ] **FR-08 `contentSizing` fallback for non-clamped intent** — Blocks: nothing today; but if any of the 6 entity-list widgets is ever used in a card-context (`contentSizing: 'grow'`), the operator needs to override at the layout level. Documented in the guide update; not a code change.

---

*AI-optimized specification. Original design: `projects/spaarke-dataset-grid-framework-r2/design.md` (preserved verbatim; do not overwrite).*
