# Project Plan: Spaarke DataGrid Framework — R2

> **Last Updated**: 2026-07-02
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)

---

## 1. Executive Summary

**Purpose**: Address 11 gaps discovered during R1 production use — one load-bearing (broken height chain), plus per-instance flexibility, maker experience, framework hygiene, and one operational trap (SpaarkeAi build-time alias into LegalWorkspace source). All schema changes additive; `sprk_configjson._version` remains `'1.0'`.

**Scope**:
- Framework additions: `contentSizing`, `widthPreference`, `availableViews`, `SectionInstance`, `rowHeight`
- Consumer unwind: remove tactical CSS `maxHeight` from 6 section registrations; adopt `contentSizing: 'clamped'`
- Wizard UI: expose `rowHeight`, `SectionInstance` "Advanced" panel, placement warnings
- Shared package extraction: new `@spaarke/legal-workspace` (or equivalent) library for section registry
- Documentation: three config templates + dual-deploy skill/guide warning
- `pageSize` default change: 100 → 25 (owner clarification)

**Timeline**: ~3.5 days focused work + 3 deploy cycles | **Estimated Effort**: 22-28 hours

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply — verified paths):
- **[ADR-012](../../.claude/adr/ADR-012-shared-components.md)** (shared component library) — Framework changes stay in `@spaarke/ui-components`. FR-10's new package follows the same SSOT rule.
- **[ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md)** (Fluent Design System) — No Fluent v8 imports. Native scrollbar retained per Issue 10 decision.
- **[ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md)** (PCF Platform Libraries) — Shared-lib code stays React-16-safe for PCF consumer compatibility.
- **[ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md)** (Spaarke Auth v2) — No auth surface touched.
- **[ADR-038](../../docs/adr/ADR-038-testing-strategy.md)** (Testing Strategy) — New tests classified per 6 KEEP categories; no `Mock<HttpMessageHandler>`; no DI-registration tests.

**From spec**:
- Additive schema evolution only — `sprk_configjson._version` remains `'1.0'`
- Backward-compat: bare-string entries in `LayoutJsonRow.sections`, omitted-field defaults reproduce current behavior for every existing record
- No BFF endpoint / service / DI change (BFF hot-path = N — CLAUDE.md §10 not triggered)
- Regression surface: 6 entity-list widgets (Communications, Documents, Invoices, Matters, Projects, Work Assignments) in Dashboard II + their single-section full-page system layouts

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| `pageSize` default: 100 → 25 | Owner clarification 2026-07-02 — workspace embedding is dominant use case; drill-through consumers explicitly override | FR-07 becomes a code change in `DataGrid.tsx` + doc update; existing config records with `pageSize: 25` no longer need explicit setting |
| Adopt Issue 12 Option B (extract shared package) in R2 | Owner clarification 2026-07-02 — eliminate SpaarkeAi ← LegalWorkspace source-alias trap permanently rather than documenting it | FR-10 added to scope (+~1 day); introduces PR 3; SpaarkeAi build hygiene test in NFR-05 |
| All 6 entity-list widgets default to `widthPreference: 'full'` | Owner clarification 2026-07-02 — dense grids read best full-width; operator opts-out per placement | FR-04 acceptance; existing multi-column dashboards see warnings on next edit but do not retroactively break |
| Ship in 3 PRs (framework, wizard, extraction) | Design-time split reflects independent review surfaces + rollback safety | PR 1 lands first (framework), PR 2 on top (wizard), PR 3 on top (extraction) |

### Discovered Resources

**Applicable Skills** (12 identified via Step 2 discovery):
- `code-page-deploy` — LegalWorkspace + SpaarkeAi + WorkspaceLayoutWizard redeploys
- `fluent-v9-component` — DataGrid, WorkspaceShell, WorkspaceLayoutWizard UI (ADR-021)
- `adr-check` — verify FR-01…FR-10 against ADR-012/021/022/028/038 at each Step 9.5 gate
- `code-review` — comprehensive review at Step 9.5 for TEST-MODIFYING tasks (unconditional per CLAUDE.md §8)
- `test-diet` — invoked at wrap-up (090-*) per ADR-038 KEEP-category classifier
- `context-handoff` — proactive checkpointing every 3 steps (CLAUDE.md §5)
- `task-execute` — every task starts here (CLAUDE.md §4 mandatory)
- `merge-to-master` — after each PR merges
- `worktree-sync` — before each PR, after each merge
- `spaarke-conventions` — commit message style, package/file naming
- `docs-guide` — updates to `BUILD-A-NEW-WORKSPACE-WIDGET.md` (FR-09)
- `docs-architecture` — updates to `SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` § 6.5

**Knowledge Articles**:
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` — framework entry point, config flow (§6.5 gets new fields)
- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` — SpaarkeAi surface context for FR-10 extraction
- `docs/architecture/SPAARKEAI-COMPONENT-MODEL.md` — component consumption patterns
- `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` — configuration guide (Step 5 gets new fields; Step 2 references templates)
- `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` — height-chain § 7.2 replaced by `contentSizing` description
- `.claude/patterns/ui/embedded-widget-sizing.md` — **CRITICAL** for FR-01 (existing height-chain pattern doc)
- `.claude/patterns/ui/fluent-v9-component-authoring.md` — for wizard UI additions (FR-02/03/04)

**Reusable Code**:
- **6 entity-list registrations** — `src/solutions/LegalWorkspace/src/sections/{communications,documents,invoices,matters,projects,workAssignments}.registration.ts` — modification target for FR-08
- **`src/solutions/LegalWorkspace/src/sectionRegistry.ts`** — runtime dev-guard target for FR-04 (may relocate under FR-10)
- **Shared package template**: `src/client/shared/Spaarke.DailyBriefing.Components/` — closest structural analogue for FR-10's new package (comparable scope: domain-specific section registry + widgets)
- **11 existing shared packages** in `src/client/shared/` establish the pattern: `package.json`, `src/{components,hooks,services,types,utils}/index.ts`, `tsconfig.json`, `.storybook/`

**Scripts**:
- `scripts/Build-AllClientComponents.ps1` — compile all TypeScript client libs (touched by FR-10 to include new shared package)
- `scripts/Build-ViteSolutionsDirect.ps1` — build Vite-bundled solutions (SpaarkeAi, LegalWorkspace, WorkspaceLayoutWizard)
- `scripts/Deploy-AllDataGridConsumers.ps1` — deploy web resources for all grid-consuming surfaces
- `scripts/Deploy-AllWebResources.ps1` — generic web resource deployment
- `scripts/Build-SpaarkeMaster.ps1` — full-stack master build orchestrator
- **`scripts/config-templates/`** — directory DOES NOT EXIST YET (FR-06 creates it)

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Framework Schema + Consumer Unwind (PR 1)     — ~10 hrs
├─ FR-01: contentSizing on SectionMetadata
├─ FR-05: availableViews allowlist
├─ FR-07: pageSize default → 25
├─ FR-06: 3 config templates
└─ FR-08: unwind maxHeight from 6 sections (depends on FR-01)

Phase 2: Wizard UI + Per-Instance Overrides (PR 2)     — ~10 hrs
├─ FR-02: rowHeight in LayoutJsonRow + wizard
├─ FR-03: SectionInstance schema + wizard "Advanced" panel
└─ FR-04: widthPreference + wizard placement checks + runtime guard

Phase 3: Shared Package Extraction + Documentation (PR 3) — ~8 hrs
├─ FR-10: extract into new shared package
└─ FR-09: dual-deploy skill/guide warning

Phase 4: Wrap-up                                       — ~2 hrs
├─ /test-diet reconciliation
├─ Architecture doc + configuration guide updates
├─ Lessons-learned + notes
└─ Mark project Complete
```

### Critical Path

**Blocking Dependencies:**
- FR-08 (unwind) BLOCKED BY FR-01 (framework `contentSizing` must land first)
- Phase 2 wizard work can start in parallel with Phase 1 after schema types are drafted (interface-first)
- Phase 3 (FR-10 extraction) BLOCKED BY Phase 1 + Phase 2 (extraction should include the new fields from both PRs to avoid a chained refactor)
- Wrap-up BLOCKED BY all three PRs merged

**High-Risk Items:**
- **FR-10 shared package extraction** — touches SpaarkeAi build chain; risk of breaking either LegalWorkspace or SpaarkeAi. Mitigation: build both in isolation before merge; regression on Dashboard II identical rendering.
- **FR-07 `pageSize` default change** — global behavior change. Mitigation: unit test on fallback; regression on all 6 widgets; drill-through consumers that want 100 must be identified during Phase 1 and explicitly overridden (if any exist).
- **`spaarkeai-compose-r1` merge-order collision** — Compose adds a section-registry entry that FR-10 must accommodate. Mitigation: coordinate merge order with that worktree's owner before Phase 3 kicks off.

---

## 4. Phase Breakdown

### Phase 1: Framework Schema + Consumer Unwind (PR 1) — ~10 hrs

**Objectives:**
1. Land the `contentSizing` field + wiring — the load-bearing FR
2. Add `availableViews` allowlist + `pageSize` default change + 3 config templates as low-risk bundled additions
3. Unwind the tactical `maxHeight` hack from the 6 section registrations after FR-01 is in place

**Deliverables:**
- [ ] `SectionMetadata.contentSizing?: 'grow' | 'clamped'` added to types + `buildDynamicWorkspaceConfig.ts` wired
- [ ] Unit test on `buildDynamicWorkspaceConfig` covers both branches
- [ ] `SourceSavedQuery.availableViews?: string[]` added; `configResolution.ts` filter added; unit test covers present/absent
- [ ] `DataGrid.tsx` `pageSize` fallback: `?? 100` → `?? 25`; doc comment on `DataGridConfiguration.ts:329` updated
- [ ] 3 JSON files under `scripts/config-templates/`: `entity-list-basic.json`, `entity-list-drill-through.json`, `entity-list-full.json`
- [ ] `DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` § Step 2 references templates
- [ ] 6 entity-list registration files: `maxHeight` + `display: 'flex'` removed from `style`
- [ ] `sectionMetadataCatalog.ts`: `contentSizing: 'clamped'` on 6 entries
- [ ] LegalWorkspace + SpaarkeAi + WorkspaceLayoutWizard rebuilt + deployed to spaarkedev1
- [ ] Regression: Dashboard II Communications shows visible scrollbar + page-2 fetch on scroll

**Critical Tasks:**
- FR-01 code (`contentSizing`) — MUST BE FIRST — blocks FR-08 unwind
- FR-08 unwind — MUST BE LAST in Phase 1 — depends on FR-01 shipping

**Inputs**: spec.md, discovered resources, existing `buildDynamicWorkspaceConfig.ts` at ~lines 167-171, `DataGridConfiguration.ts:329`, `DataGrid.tsx:847`
**Outputs**: PR 1 (single merge commit), spaarkedev1 deployment verified

### Phase 2: Wizard UI + Per-Instance Overrides (PR 2) — ~10 hrs

**Objectives:**
1. Extend `LayoutJsonRow` schema with `rowHeight` + widened `sections` typing (`SectionInstance` union)
2. Build wizard UI for `rowHeight` + `SectionInstance` "Advanced" panel + `widthPreference` placement checks
3. Add runtime dev-guard for `widthPreference` violations

**Deliverables:**
- [ ] `LayoutJsonRow.rowHeight?: string` added to types; parser accepts it
- [ ] `LayoutJsonRow.sections: Array<string | SectionInstance>` widened; bare-string entries continue to work
- [ ] `SectionInstance` type: `id`, `configIdOverride?`, `label?`, `overrides?.{pageSize, availableViews}`
- [ ] `SectionMetadata.widthPreference?: 'full' | 'half' | 'any'` added; default `'any'`
- [ ] All 6 entity-list metadata entries: `widthPreference: 'full'` (owner clarification)
- [ ] Wizard UI: row `rowHeight` input with presets (`auto`, `40vh`, `60vh`, `80vh`, `100vh`)
- [ ] Wizard UI: per-section "Advanced" panel — `configId` picker (queries `sprk_gridconfiguration` records for section entity), label override, `pageSize` override, `availableViews` allowlist
- [ ] Wizard UI: `widthPreference: 'full'` in multi-slot row → modal "convert to single-column?" (Yes / Cancel)
- [ ] Wizard UI: `widthPreference: 'half'` in single-slot row → warning icon + tooltip
- [ ] Runtime dev-guard in `sectionRegistry.ts`: `console.warn` when `'full'` widget in multi-column row
- [ ] Unit tests: schema parsers (types), runtime guard behavior
- [ ] Wizard integration tests: placement modal, "Advanced" panel writes correct JSON
- [ ] Regression: existing 8+ system layouts render correctly with bare-string entries

**Inputs**: Phase 1 merged, `WorkspaceLayoutWizard/src/App.tsx`, `sectionRegistry.ts`
**Outputs**: PR 2, wizard-authored layout with `rowHeight: '80vh'` renders correctly

### Phase 3: Shared Package Extraction + Documentation (PR 3) — ~8 hrs

**Objectives:**
1. Extract LegalWorkspace section registry into a new shared package under `src/client/shared/`
2. Migrate SpaarkeAi's `vite.config.ts` from source-alias to normal package dependency
3. Add dual-deploy warning to `code-page-deploy` skill + widget-authoring guide

**Deliverables:**
- [ ] New shared package created (name TBD — `@spaarke/legal-workspace` proposed) at `src/client/shared/Spaarke.LegalWorkspace.*/`
- [ ] Package builds independently and publishes types (mirror `Spaarke.DailyBriefing.Components` structure)
- [ ] Section registry + metadata + registration exports migrated OR re-exported from new package
- [ ] `src/solutions/SpaarkeAi/vite.config.ts`: removed `@spaarke/legal-workspace/src` + `@spaarke/legal-workspace` source aliases
- [ ] SpaarkeAi's `package.json`: adds file dependency on new shared package
- [ ] LegalWorkspace's build continues to work via the same new shared package
- [ ] WorkspaceLayoutWizard (if it consumed the source alias) also switches to shared-package dependency
- [ ] `Build-AllClientComponents.ps1` includes new shared package in build order
- [ ] `.claude/skills/code-page-deploy/SKILL.md` gains dual-deploy warning section (see FR-09 acceptance in spec.md)
- [ ] `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` gains companion note
- [ ] Both LegalWorkspace + SpaarkeAi deploy + Dashboard II renders identically pre/post

**Inputs**: Phases 1 + 2 merged, `Spaarke.DailyBriefing.Components/` as structural template, existing `sectionRegistry.ts` + `workspaceConfig.tsx`
**Outputs**: PR 3, both consumers on shared-package dependency, dual-deploy trap resolved for this pair

### Phase 4: Wrap-up — ~2 hrs

**Objectives:**
1. Reconcile new tests per ADR-038 KEEP-category classifier (`/test-diet`)
2. Finalize architecture + configuration guide updates
3. Capture lessons-learned + mark project Complete

**Deliverables:**
- [ ] `/test-diet` report at `projects/spaarke-dataset-grid-framework-r2/notes/test-diet-report.md`
- [ ] Any SCAFFOLDING-class tests deleted (report emits `git rm` commands; reviewer applies)
- [ ] `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` § 6.5 extended with new fields
- [ ] `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` § Step 5 subsections cover new fields
- [ ] `notes/lessons-learned.md` written
- [ ] README status → "Complete"
- [ ] `projects/INDEX.md` — this project's row removed OR moved to archived section per current maintenance contract

**Inputs**: Phases 1-3 all merged
**Outputs**: Project marked Complete; artifacts finalized

---

## 5. Dependencies

### External Dependencies

None. No new NuGet / npm packages required. No new Azure resources. No new Dataverse entities.

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| DataGrid framework (R1) | `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/` | Production |
| `Spaarke.DailyBriefing.Components` (structural template for FR-10) | `src/client/shared/Spaarke.DailyBriefing.Components/` | Production |
| `sprk_gridconfiguration` schema | Dataverse solution | Production — additive fields only |
| ADR-012, ADR-021, ADR-022, ADR-028, ADR-038 | `.claude/adr/`, `docs/adr/` | Current |
| Deploy scripts | `scripts/` | Existing (FR-10 amends `Build-AllClientComponents.ps1`) |

---

## 6. Testing Strategy

Per ADR-038, tests are **integration-heavy**, MAINTAIN-class only (no SCAFFOLDING). All new tests classified via `/test-diet` at wrap-up.

**Unit Tests** (framework-contract):
- `buildDynamicWorkspaceConfig` — both `contentSizing: 'grow'` and `'clamped'` branches (FR-01)
- `configResolution.ts` — `availableViews` allowlist present/absent (FR-05)
- `DataGrid.tsx` — `pageSize` fallback returns 25 when omitted (FR-07)
- `LayoutJsonRow` schema parsers — accept `string | SectionInstance` union + `rowHeight?` (FR-02, FR-03)
- `SectionMetadata` — `widthPreference` field type (FR-04)
- `sectionRegistry.ts` — runtime dev-guard logs on `'full'` widget in multi-column row (FR-04)

**Integration Tests** (wizard UI):
- Placing a `'full'` widget in `1fr 1fr` row → modal prompts row conversion (FR-04)
- Placing a section with `configIdOverride` → alt config record loaded (FR-03)
- Row `rowHeight: '80vh'` → row rendered at 80vh (FR-02)

**Regression Tests** (manual + DevTools):
- Dashboard II Communications: visible scrollbar + page-2 fetch on scroll (FR-01, FR-07, FR-08)
- Documents single-section full-page layout: uses ~80vh, not 480px (FR-02, FR-08)
- 6 entity-list widgets in Dashboard II render correctly (FR-08)
- 4 System Workspace Layouts (Communications, Projects, Invoices, Work Assignments) render correctly (FR-08)
- 8+ existing published `sprk_workspacelayout` records continue to render with bare-string sections (FR-03 back-compat)
- Post-FR-10: Dashboard II identical pre/post LegalWorkspace + SpaarkeAi rebuild

**No Mock<HttpMessageHandler>**, **no DI-registration tests**, **no ctor null-check tests** per ADR-038.

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1 (PR 1):**
- [ ] FR-01 unit test passes both branches; Dashboard II Communications scrolls
- [ ] FR-05 unit test passes both branches
- [ ] FR-07 unit test on new `?? 25` fallback; regression on 6 widgets confirms scroll behavior
- [ ] FR-06 three JSONs parse against current type; guide references them
- [ ] FR-08 grep confirms zero `maxHeight` in 6 registration files; `contentSizing: 'clamped'` on 6 metadata entries

**Phase 2 (PR 2):**
- [ ] FR-02 wizard-authored `rowHeight: '80vh'` renders correctly
- [ ] FR-03 wizard "Advanced" panel writes valid `SectionInstance` JSON; alt configId + label override work at runtime
- [ ] FR-04 wizard modal on invalid placement + runtime dev-guard `console.warn`; all 6 widgets ship with `widthPreference: 'full'`

**Phase 3 (PR 3):**
- [ ] FR-10 SpaarkeAi's `vite.config.ts` has no source aliases; both consumers build off shared package
- [ ] FR-10 Dashboard II identical pre/post rebuild
- [ ] FR-09 `code-page-deploy` skill + widget-authoring guide contain dual-deploy warning

**Phase 4 (Wrap-up):**
- [ ] `/test-diet` report generated; no MAINTAIN-class tests deleted; SCAFFOLDING-class tests classified for deletion
- [ ] Architecture doc + configuration guide + widget-authoring guide updated
- [ ] `code-review` + `adr-check` clean at final wrap-up task
- [ ] `notes/lessons-learned.md` captured

### Business Acceptance

- [ ] Operator can author a "Documents" full-page layout that fills the tab (FR-02)
- [ ] Operator can author a "Communications" section with `label: "Email"` + `availableViews` restricted to email-only savedqueries (FR-03, FR-05)
- [ ] Framework maintainer can edit a section registration once and see the change reach both LegalWorkspace AND SpaarkeAi via a normal shared-lib rebuild (FR-10)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | FR-10 shared-package extraction breaks SpaarkeAi build | Medium | High | Build SpaarkeAi in isolation immediately after extraction; regression on Dashboard II identical rendering; roll back PR 3 if divergence found (PR 1 + PR 2 already merged and stable) |
| R2 | `pageSize` default change (FR-07) surprises drill-through consumers | Low | Medium | Identify all `sprk_gridconfiguration` records with omitted `pageSize` during Phase 1; explicitly set them to 100 where drill-through/full-page consumers rely on the old default |
| R3 | `spaarkeai-compose-r1` merge lands during Phase 3 with conflicting section-registry changes | Medium | Medium | Coordinate merge order — either land Compose first (R2 rebases on top) OR land R2 PR 3 first (Compose rebases). Check `spaarkeai-compose-r1` PR status before Phase 3 kicks off. |
| R4 | Wizard UI changes (Phase 2) break existing published layouts | Low | High | Bare-string entries + omitted fields = current behavior; unit test on schema parser; manual regression on 8+ existing layouts |
| R5 | FR-04 `widthPreference: 'full'` on all 6 widgets triggers wizard warnings on every existing multi-column dashboard | Medium | Low | Warnings surface at edit-time only, not at render-time; existing published layouts continue to work; document behavior in guide update |
| R6 | ADR-022 React-16-safety violated by new wizard hooks | Low | Medium | Wizard code lives in `src/solutions/WorkspaceLayoutWizard/` (Vite React 18 app), NOT in shared lib. Framework additions are types only. Verify at `adr-check` Step 9.5. |

---

## 9. Next Steps

1. **Review this plan.md** — sanity-check phase decomposition + owner clarifications applied correctly
2. **Run** `/task-create projects/spaarke-dataset-grid-framework-r2` to decompose Phase 1-4 into POML task files (estimated 17-25 tasks + 3 deployment tasks + 1 wrap-up task)
3. **Begin** Phase 1 execution via `/task-execute` on the first pending task

---

**Status**: Ready for Tasks
**Next Action**: Run `/task-create projects/spaarke-dataset-grid-framework-r2`

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
