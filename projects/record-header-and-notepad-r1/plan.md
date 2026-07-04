# Project Plan: Record Header + Notepad — R1

> **Last Updated**: 2026-07-04
> **Status**: ✅ Complete (all 5 phases + Phase 6 DEF absorption shipped + user acceptance 2026-07-04)
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md) · **Plan extension**: [plan-extension.md](plan-extension.md)
> **Actual timeline**: 2 days end-to-end (2026-07-02 → 2026-07-04), well ahead of the 3-week projection — most of the time was live-QA iteration + Phase 6 DEF absorption.

---

## 1. Executive Summary

**Purpose**: Ship reusable record-header primitives in `@spaarke/ui-components` (shared toolbar, card shell, field grid, field renderers, and a shared `useRecordHeaderToolbarActions` hook) plus a standalone entity-agnostic Notepad code page and a Matter-specific PCF composed from those primitives. Establishes the template for per-entity thin PCFs (each ~80 LOC) that v2+ projects will ship for Project, Invoice, Event, etc.

**Scope**:
- 4 new shared components + 4 field renderers + 3 shared hooks in `@spaarke/ui-components`
- `MatterHeaderPcf` (thin PCF, ~80 LOC composition)
- Notepad Vite React 18 SPA (standalone code page, entity-agnostic launch contract)
- Unit + integration tests
- Authoring guide + pattern pointer

**Timeline**: ~3 weeks (12-18 dev-days) | **Estimated Effort**: 90-140 hours across 4 phases + wrap-up

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006** — Prefer PCF over webresources. `MatterHeaderPcf` is a PCF; future entity headers follow the same pattern.
- **ADR-011** — Dataset PCF over subgrids. Not directly applicable (not a dataset control), but the "typed components > runtime schemas" principle informs the per-entity thin PCF decision.
- **ADR-012** — Shared component library. All primitives + hooks live in `@spaarke/ui-components`.
- **ADR-021** — Fluent UI v9 semantic tokens exclusively. Zero hex/rgb literals in components.
- **ADR-022** — PCF platform libraries: React 16/17 compatibility for anything consumed by PCFs. Notepad SPA can use React 18.
- **ADR-024** — Polymorphic resolver pattern. `sprk_memo` deviates (text-field regarding); Path A exception documented.
- **ADR-028** — Spaarke Auth v2. Not applicable — this project has no BFF surface.
- **ADR-032** — BFF Null-Object kill-switch. Not applicable — no BFF surface.
- **ADR-038** — Testing strategy. Unit tests for renderers + hooks; integration test for toolbar wiring.

**From Spec**:
- All Dataverse I/O via `Xrm.WebApi` — no `@spaarke/auth`, no BFF endpoints
- Fluent v9 semantic tokens only — zero hex/rgb
- Notepad launch contract (`?regardingEntity=<logical>&regardingId=<guid>`) is external API surface
- Refresh icon in sparkle popover rendered but not wired in R1 (follow-on BFF work)
- Matter form binding is a follow-on maker task, not R1

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Per-entity thin PCF (not one PCF with `variant`) | Self-documenting manifest; typed JSX layout; per-entity release-risk decoupling | ~30 LOC boilerplate per new entity; scales cleanly to 10+ entities |
| Shared `useRecordHeaderToolbarActions` hook | Three toolbar actions are 100% invariant across entities | Per-entity PCFs never re-implement toolbar wiring |
| Sparkle → popover (not modal) with refresh icon | Faster inline preview + entry point for future BFF-backed regen | FR-08 revised from design draft; refresh unwired in R1 (FR-08a) |
| Notepad `sprk_regardingrecordid` = GUID only (text field) | Owner clarification O1 — pre-existing schema | Non-standard vs ADR-024; documented as Path A exception |
| Notepad standalone Vite SPA, entity-agnostic | Reusability discipline — any surface can launch it | NFR-09 pins URL contract as external API |
| `useSprkMemoRepository` initially inside Notepad | Wait for second consumer before promoting to shared lib | MemoSection adoption is follow-on cleanup (out of R1) |

### Discovered Resources

**Applicable ADRs** (loaded during resource discovery):
- `.claude/adr/ADR-006-prefer-pcf-over-webresources.md` — PCF-first mandate
- `.claude/adr/ADR-011-dataset-pcf-over-subgrids.md` — dataset control primacy
- `.claude/adr/ADR-012-shared-component-library.md` — `@spaarke/ui-components` binding
- `.claude/adr/ADR-021-fluent-ui-design-system.md` — Fluent v9 semantic tokens only
- `.claude/adr/ADR-022-pcf-platform-libraries.md` — React 16/17 compat boundary
- `.claude/adr/ADR-024-polymorphic-resolver-pattern.md` — 11-entity regarding (Path A exception noted)
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` — auth v2 (N/A here)
- `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` — Null-Object DI (N/A here)
- `.claude/adr/ADR-038-testing-strategy.md` — integration-heavy pyramid

**Applicable Skills**:
- `fluent-v9-component` — CRITICAL for all component authoring
- `pcf-deploy` — CRITICAL for `MatterHeaderPcf` build + solution ZIP
- `code-page-deploy` — CRITICAL for Notepad Vite SPA deploy
- `ui-test` — integration test execution
- `code-review`, `adr-check` — quality gates at Step 9.5 of `task-execute`
- `task-execute`, `context-handoff`, `adr-aware`, `spaarke-conventions`, `script-aware` — standard task-execution stack

**Applicable Patterns**:
- `.claude/patterns/pcf/control-initialization.md` — PCF lifecycle (init/updateView/render/destroy)
- `.claude/patterns/pcf/fluent-v9-modern-theming.md` — FluentProvider theme source
- `.claude/patterns/pcf/dataverse-queries.md` — `Xrm.WebApi` patterns
- `.claude/patterns/ui/fluent-v9-component-authoring.md` — component authoring standards
- `.claude/patterns/ui/fluent-v9-theming.md` — theming decisions
- `.claude/patterns/ui/fluent-v9-react-version-boundaries.md` — React 16 safety in shared lib
- `.claude/patterns/ui/record-modal-selection.md` — Layout 1 / Layout 2 modal patterns
- `.claude/patterns/webresource/full-page-custom-page.md` — Vite SPA template
- `.claude/patterns/webresource/code-page-wizard-wrapper.md` — modal launch mechanics

**Applicable Guides**:
- `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` — `@spaarke/ui-components` build/consumption
- `docs/guides/PCF-DEPLOYMENT-GUIDE.md` — PCF build & deploy procedure
- `docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md` — schema reference for `sprk_memo`

**Applicable Standards**:
- `docs/standards/MODAL-DECISION-CRITERIA.md` — 85%×85% (Layout 1) for entity records; 70%×80% for specialized editors (Notepad)
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` — `Xrm.WebApi` for all client-side Dataverse I/O
- `docs/standards/CODING-STANDARDS.md` — naming, structure
- `docs/standards/INTEGRATION-CONTRACTS.md` — Notepad URL contract stability
- `docs/standards/ANTI-PATTERNS.md` — no v8 imports, no hex literals, no cross-React exports
- `docs/standards/TEST-ARCHITECTURE.md` — test pyramid + KEEP categories

**Reusable Code**:
- `src/client/pcf/VisualHost/control/components/CardChrome.tsx` — reference for HeaderToolbar behavior (internal to VisualHost; do NOT reuse, use as pattern reference only)
- `src/solutions/EventDetailSidePane/src/components/MemoSection.tsx` — existing `sprk_memo` CRUD (adapt into `useSprkMemoRepository`)
- `src/solutions/SmartTodo/` — Vite code-page template + `useLaunchContext` hook (adapt for Notepad)
- `src/client/pcf/CLAUDE.md` — PCF module rules, React 16 compat, version-footer convention
- `src/client/pcf/DocumentRelationshipViewer/`, `src/client/pcf/SemanticSearchControl/` — React-16-compatible PCF control lifecycle references
- `src/client/shared/Spaarke.UI.Components/src/index.ts` — shared library entry point (edit to add exports)

**Applicable Scripts**:
- `scripts/Deploy-PCFWebResources.ps1` — `MatterHeaderPcf` build + solution ZIP deploy
- `scripts/Deploy-CustomPage.ps1` — Notepad webresource deploy

---

## 3. Implementation Approach

### Phase Structure

```
Phase 1: Shared Library — HeaderToolbar, RecordHeaderShell, FieldGrid, field renderers, hooks
└─ 15-18 tasks · Week 1

Phase 2: MatterHeaderPcf — thin PCF composition + build/deploy
└─ 6-8 tasks · Week 2 (early)

Phase 3: Notepad Code Page — Vite SPA + hooks + launch-contract test
└─ 12-14 tasks · Week 2-3 (parallelizable with Phase 2)

Phase 4: Documentation — authoring guide + pattern pointer
└─ 3-4 tasks · Week 3 (parallelizable with Phase 2/3 late work)

Phase 5: Wrap-up — lessons learned, INDEX cleanup, close-out
└─ 1 task · End of Week 3
```

### Critical Path

**Blocking Dependencies:**
- Phase 2 (`MatterHeaderPcf`) BLOCKED BY Phase 1 (shared components + `useRecordHeaderToolbarActions`)
- Phase 3 (Notepad) has PARTIAL dependency on Phase 1 (`useRecordHeaderToolbarActions` needs Notepad's URL contract stable — Phase 1 task defining `toolbarLaunchDefaults.ts` must land first)
- Phase 4 (docs) BLOCKED BY Phase 2 completion (guide references working `MatterHeaderPcf`)
- Wrap-up BLOCKED BY all phases

**Parallel Execution Opportunities:**
- Field renderers (`TextField`, `LookupField`, `OptionSetField`, `TextareaField`) → 4-way parallel
- Shared hooks (`useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`) → 2-way parallel (last one depends on first two)
- Phase 2 + Phase 3 mid-work can proceed in parallel after `toolbarLaunchDefaults.ts` lands
- Notepad component build (`MemoList`, `MemoEditor`, `CreatedByPopover`) → 3-way parallel

**High-Risk Items:**
- `sprk_memo` schema verification — Phase 1 discovery task must complete before Phase 3 CRUD implementation
- `sprk_smarttodo_page` webresource name — Phase 2 verification before wiring checkmark

---

## 4. Phase Breakdown

### Phase 1: Shared Library (Week 1)

**Objectives:**
1. Ship `HeaderToolbar`, `RecordHeaderShell`, `FieldGrid`, four field renderers as reusable Fluent v9 primitives in `@spaarke/ui-components`
2. Ship three shared hooks: `useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`
3. Establish `toolbarLaunchDefaults.ts` with modal-size constants (85%×85% for Layout 1, 70%×80% for Notepad)
4. Verify `sprk_memo` and `sprk_recordsummary` schemas via `Xrm.WebApi` metadata + `MemoSection.tsx` inspection

**Deliverables:**
- [ ] `HeaderToolbar` component (title, iconSlots, badges, tooltips) — FR-01
- [ ] `RecordHeaderShell` (card chrome, toolbar slot, body slot, loading skeleton) — FR-02
- [ ] `FieldGrid` (CSS grid with configurable columns, span-aware children) — FR-03
- [ ] Four field renderers: `TextField`, `LookupField`, `OptionSetField`, `TextareaField` — FR-04
- [ ] `useRecordFieldValues` hook — FR-05
- [ ] `useRelatedCount` hook (with `sprk_regardingrecordid` filter for memo count) — FR-06
- [ ] `useRecordHeaderToolbarActions` hook (sparkle popover behavior + checkmark modal + annotation modal) — FR-07, FR-08, FR-08a, FR-09, FR-10, FR-11
- [ ] `toolbarLaunchDefaults.ts` (modal-size + navigation constants)
- [ ] All new symbols exported from `src/client/shared/Spaarke.UI.Components/src/index.ts`
- [ ] Unit tests for each renderer + each hook (mock `Xrm.WebApi`)
- [ ] `sprk_memo` schema verification task documented in `notes/`

**Critical Tasks:**
- Schema-verification task — MUST BE FIRST — BLOCKS FR-06/FR-14/FR-15 implementation
- `HeaderToolbar` — BLOCKS `RecordHeaderShell`
- `useRecordFieldValues` + `useRelatedCount` — BLOCK `useRecordHeaderToolbarActions`

**Inputs**: spec.md, design.md, `src/client/shared/Spaarke.UI.Components/src/index.ts`, `MemoSection.tsx` (reference), `CardChrome.tsx` (behavior reference), Fluent v9 patterns

**Outputs**: New files under `src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/`, `src/components/RecordHeader/`, `src/hooks/`; updated `src/index.ts`; unit tests under `__tests__/`

### Phase 2: MatterHeaderPcf (Week 2 early)

**Objectives:**
1. Build `MatterHeaderPcf` as a thin composition of Phase 1 primitives (~80 LOC total)
2. Verify `sprk_smarttodo_page` webresource name against target deployment
3. Produce solution ZIP importable to Dataverse (bundle ≤ 250 KB minified)

**Deliverables:**
- [ ] `src/client/pcf/MatterHeader/ControlManifest.Input.xml` with `recordId` input override property
- [ ] `src/client/pcf/MatterHeader/control/index.ts` (PCF class, ADR-006/022 lifecycle)
- [ ] `src/client/pcf/MatterHeader/control/MatterHeaderView.tsx` (composition — see spec FR-12)
- [ ] `src/client/pcf/MatterHeader/control/version.ts`
- [ ] Solution folder + `pack.ps1`
- [ ] Solution ZIP built via `npm run build:prod`; bundle size measured against NFR-04
- [ ] Manual QA: Sparkle popover, checkmark modal, annotation modal all wire correctly against a real Matter record

**Critical Tasks:**
- `sprk_smarttodo_page` webresource verification — BLOCKS checkmark wiring
- PCF manifest — MUST match ADR-006 conventions
- `MatterHeaderView.tsx` — thin composition; MUST NOT re-implement toolbar wiring

**Inputs**: Phase 1 outputs (shared lib exports), `src/client/pcf/CLAUDE.md`, existing PCF exemplars

**Outputs**: `src/client/pcf/MatterHeader/**`, solution ZIP artifact

### Phase 3: Notepad Code Page (Week 2-3, parallelizable with Phase 2)

**Objectives:**
1. Build Notepad as a standalone Vite React 18 SPA at `src/solutions/Notepad/`
2. Establish entity-agnostic launch contract (URL params `regardingEntity` + `regardingId`)
3. Ship `useSprkMemoRepository` hook (initially inside Notepad; future promotion to shared lib)
4. Verify launch contract by test-only launcher against synthetic non-Matter record (FR-19)

**Deliverables:**
- [ ] Vite project scaffold (`package.json`, `vite.config.ts`, `tsconfig.json`, `index.html`, `src/main.tsx`, `src/App.tsx`) — adapt from `src/solutions/SmartTodo/`
- [ ] `NotepadShell.tsx` (top bar, list dropdown, editor area, info popover)
- [ ] `MemoList.tsx` (dropdown of prior memos, derived-title preview)
- [ ] `MemoEditor.tsx` (textarea with Ctrl+Enter save, blur save, 1s idle debounce)
- [ ] `CreatedByPopover.tsx` (Fluent v9 popover with createdby + createdon)
- [ ] `useSprkMemoRepository.ts` hook (CRUD against `sprk_memo` via `Xrm.WebApi`)
- [ ] `useLaunchContext.ts` (URL param parsing, adapted from SmartTodo)
- [ ] `utils/deriveTitle.ts` (first non-empty line, truncated)
- [ ] `types/memo.ts` (TypeScript types)
- [ ] URL-param error handling (MessageBar + Close) — FR-13
- [ ] `sprk_notepad_page` webresource registered in Dataverse
- [ ] Entity-agnostic launch test wiring documented (FR-19)

**Critical Tasks:**
- Schema verification (from Phase 1) — BLOCKS `useSprkMemoRepository` implementation
- URL contract — MUST match NFR-09 (external API surface)
- Debounce logic — measure against NFR (write frequency vs throttling)

**Inputs**: Phase 1 `useSprkMemoRepository` schema decisions, `MemoSection.tsx` (CRUD reference), `SmartTodo/` (Vite template), Fluent v9 patterns

**Outputs**: `src/solutions/Notepad/**`, `sprk_notepad_page` webresource

### Phase 4: Documentation (Week 3, parallelizable with late Phase 2/3)

**Objectives:**
1. Publish `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` enabling a developer to ship a new per-entity PCF (~80 LOC) without re-reading spec.md
2. Create `.claude/patterns/ui/record-header-composition.md` pointer file (≤ 25 lines)

**Deliverables:**
- [ ] `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` (walkthrough: manifest, class, view, deploy)
- [ ] `.claude/patterns/ui/record-header-composition.md` (pointer, follows pattern-file convention)
- [ ] Entity-agnostic Notepad launch scenario documented in guide
- [ ] Reference exemplar walkthrough (`MatterHeaderPcf` → `ProjectHeaderPcf` template)

**Critical Tasks:**
- Guide references working `MatterHeaderPcf` — BLOCKED BY Phase 2 completion

**Inputs**: All prior phases

**Outputs**: `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`, `.claude/patterns/ui/record-header-composition.md`

### Phase 5: Wrap-up (End of Week 3)

**Objectives:**
1. Capture lessons learned
2. Update `projects/INDEX.md` (remove from active or mark as complete)
3. Run `/test-diet` per project-close gate (ADR-038)
4. Set README status to Complete

**Deliverables:**
- [ ] `notes/lessons-learned.md` written
- [ ] `README.md` status → Complete
- [ ] `projects/INDEX.md` row updated
- [ ] `/test-diet` report at `notes/test-diet-report.md`
- [ ] Follow-on backlog captured in `notes/defer-issues.md` (VisualHost CardChrome migration, MemoSection adoption of `useSprkMemoRepository`, refresh-icon BFF wiring, per-entity PCFs for v2)

**Critical Tasks:**
- `/test-diet` before marking complete — BINDING per CLAUDE.md §7

---

## 5. Dependencies

### External Dependencies

None. Explicitly zero (NFR-08).

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `@spaarke/ui-components` shared library | `src/client/shared/Spaarke.UI.Components/` | Production |
| `Xrm.WebApi` (host runtime) | Model-driven app host | Production |
| `sprk_memo` entity | Dataverse | Production |
| `sprk_recordsummary` entity | Dataverse | Production |
| `sprk_todo` entity | Dataverse | Production |
| `sprk_smarttodo_page` webresource | Dataverse | Production |
| Fluent UI v9 | npm dependency of shared lib | Production |

---

## 6. Testing Strategy

**Unit Tests** (component + hook coverage):
- Each field renderer (`TextField`, `LookupField`, `OptionSetField`, `TextareaField`) — rendering + interaction
- `HeaderToolbar` — slot rendering, badge suppression, tooltip a11y
- `RecordHeaderShell` — card chrome, loading skeleton, body slot
- `FieldGrid` — column counts, span layout
- `useRecordFieldValues` — mock `Xrm.WebApi`, verify $select build
- `useRelatedCount` — mock `Xrm.WebApi`, verify $count query + `sprk_regardingrecordid` filter
- `useRecordHeaderToolbarActions` — verify three slots emitted, disabled flag respected, sparkle popover state
- `deriveTitle` utility — first non-empty line, truncation edge cases

**Integration Tests**:
- `MatterHeaderPcf` full composition renders + toolbar actions wire correctly (mock `Xrm`)
- Notepad create → edit → save → switch → info-popover round trip
- Notepad missing URL params → MessageBar + Close renders

**E2E / UI Tests** (via `ui-test` skill):
- `MatterHeaderPcf` renders on a real Matter form; sparkle popover, checkmark modal (85%×85%), annotation modal (70%×80%) verified in dev environment
- Notepad Ctrl+Enter save + blur save + 1s idle debounce verified
- Notepad launched with synthetic non-Matter `regardingEntity` + `regardingId` renders identically (FR-19)
- Dark-mode compliance for `HeaderToolbar`, `RecordHeaderShell`, field renderers (ADR-021)

**Excluded from coverage**:
- Notepad rich-text (out of scope)
- Field editing (out of scope)
- Second entity PCF (out of scope)

Coverage target: standard KEEP-category coverage per ADR-038; no numeric percentage gate.

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase 1:**
- [ ] All shared components render Fluent v9 with zero hex/rgb literals
- [ ] Unit tests pass for every renderer + hook
- [ ] `sprk_memo` schema verified (Phase 1 discovery task output cited in notes)
- [ ] React 16/17 compatibility verified (no `use()`, `useSyncExternalStore` w/o polyfill, etc.)

**Phase 2:**
- [ ] `MatterHeaderPcf` builds via `npm run build:prod` (0 errors, 0 warnings)
- [ ] Bundle size ≤ 250 KB minified (NFR-04)
- [ ] PCF LOC ≤ 100 excluding shared primitives (NFR-02)
- [ ] Solution ZIP importable to Dataverse dev environment
- [ ] Manual QA verifies sparkle popover, checkmark modal (85%×85%), annotation modal (70%×80%)

**Phase 3:**
- [ ] Notepad Vite build succeeds; deployed as `sprk_notepad_page` webresource
- [ ] URL contract stable (`?regardingEntity=<logical>&regardingId=<guid>`) per NFR-09
- [ ] FR-13 through FR-18 behaviors verified in QA
- [ ] FR-19 entity-agnostic launch verified with synthetic non-Matter record

**Phase 4:**
- [ ] Authoring guide published; a second developer can ship `ProjectHeaderPcf` in ~80 LOC without re-reading spec
- [ ] Pattern pointer file present at `.claude/patterns/ui/record-header-composition.md` (≤ 25 lines)

**Phase 5:**
- [ ] `/test-diet` report published at `notes/test-diet-report.md`
- [ ] `notes/lessons-learned.md` published
- [ ] Follow-on items filed in `notes/defer-issues.md` + linked GitHub Issues

### Business Acceptance

- [ ] `MatterHeaderPcf` ready to be added to Matter form by maker (follow-on task)
- [ ] Notepad launchable from any Spaarke surface via URL contract
- [ ] Follow-on projects (per-entity headers, VisualHost migration, MemoSection consolidation) have a documented path forward

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | `sprk_memo` schema differs from assumption (field type / size / regarding shape) | Low | Med | Phase 1 schema-verification task fires before Phase 3 CRUD |
| R2 | Per-entity PCFs drift out of sync with shared primitives across versions | Low | Low | Shared lib is workspace-local dep — all PCFs build against same version |
| R3 | `sprk_smarttodo_page` webresource name differs from assumption | Med | Low | Phase 2 verification task; adapter added if needed |
| R4 | Notepad write frequency (1s debounce + blur + Ctrl+Enter) causes API throttling | Low | Low | Measure in QA; fall back to Ctrl+Enter + blur only if needed |
| R5 | Bundle-size creep for shared lib | Med | Low | Baseline measurement in test; NFR-04 250 KB ceiling per PCF |
| R6 | Sparkle popover UX (width, scroll, empty-state copy) needs iteration | Med | Low | UX review in Phase 2 QA; adjust before merge |
| R7 | ADR-024 exception on `sprk_memo` regarding leads to referential integrity gaps (cascade delete) | Low | Low | Documented as Path A exception; application-level cleanup accepted for R1 |
| R8 | Refresh-icon deferral confuses reviewers (tooltip copy, visual state) | Low | Low | Explicit "unwired in R1" tooltip; code review verifies no accidental wire-up |

---

## 9. Next Steps

1. **Review this plan.md** for accuracy and phase boundary alignment
2. **Run** `/task-create projects/record-header-and-notepad-r1` to generate task files (or continue with the current `/project-pipeline` invocation which will call it)
3. **Begin** Phase 1 with the schema-verification task (task 001)

---

**Status**: Ready for task decomposition
**Next Action**: Continue `/project-pipeline` → task-create → tasks/TASK-INDEX.md → begin Phase 1

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
