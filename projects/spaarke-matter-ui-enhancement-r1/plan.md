# Project Plan: Spaarke Matter UI Enhancement R1

> **Last Updated**: 2026-05-27
> **Status**: Ready for Tasks
> **Spec**: [spec.md](./spec.md) (Rev 6 — recon-validated)
> **Source design**: [design.md](./design.md)

---

## 1. Executive Summary

**Purpose**: Deliver a modern Fluent v9 Matter Overview tab — Documents PCF behavior modernization + Matter Performance side pane (5 Visual Host instances + 5 chart defs) + 2-column form layout — built on existing infrastructure with zero parallel component libraries.

**Scope**:
- 5 surgical changes to existing SemanticSearchControl PCF + telemetry wiring + doc updates
- 5 backward-compatible Custom Options additions to Visual Host renderers + internal `CardChrome` wrapper
- 5 new `sprk_chartdefinition` records
- 2 new shared components in `@spaarke/ui-components`
- 1 new BFF endpoint + projection extension to `/api/ai/search`
- Matter main-form Overview tab XML reconfiguration

**Timeline**: 2-3 weeks dev (driven by parallel-group execution model) | **Estimated Effort**: 60-90 hours across ~34 tasks

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006** (PCF over web resources): existing PCF surfaces are the binding context for this work
- **ADR-007** (`SpeFileStore` facade): FR-BFF-02 bulk-download MUST access SPE via `SpeFileStore.DownloadFileAsync` — no Graph SDK types leaked above the facade
- **ADR-008** (Endpoint filters for auth): FR-BFF-02 uses an endpoint filter (modeled on `SemanticSearchAuthorizationFilter`) — NOT global middleware
- **ADR-010** (DI minimalism): FR-BFF-02 implementation MUST NOT push DI registrations above the ≤15 cap
- **ADR-012** (Shared component library): FR-SC-01 and FR-SC-02 are added to `@spaarke/ui-components`; abstracted services via `IDataService` where applicable
- **ADR-019** (ProblemDetails): FR-BFF-02 error responses MUST emit RFC 7807 ProblemDetails with stable error codes (**Spec drift note**: spec.md line 332 labels ADR-019 as "SPE access" — that is incorrect; ADR-019 is ProblemDetails. SPE access is governed by ADR-007. Both apply to FR-BFF-02.)
- **ADR-021** (Fluent UI v9 Design System): ALL UI MUST use Fluent v9 semantic tokens; dark mode required; zero hardcoded hex/rgb (NFR-04)
- **ADR-022** (PCF Platform Libraries): PCFs use React 16/17 platform-provided; FR-DOC-* and FR-VH-* changes MUST NOT pull in React 18+ dependencies
- **ADR-028** (Spaarke Auth Architecture v2): both PCFs already use `@spaarke/auth`; FR-BFF-02 MUST follow function-based contract + managed-identity pattern
- **ADR-029** (BFF Publish Hygiene): no new NuGet packages; no transitive CVE introductions; publish-size impact verified (baseline ~60 MB per `.claude/constraints/azure-deployment.md`)

**From Spec** (binding):
- **No new visual types** in Visual Host (spec §6.4.0; ❌ MUST NOT create `MatterHealthComposite`, `ScorecardDonut`, etc.)
- **No parallel chart components** in `@spaarke/ui-components` (no `HealthDonut`, `KpiCard`, etc.)
- **No `MatterPerformancePane` container PCF** — form section handles vertical stacking
- **No schema additions to `sprk_matter`** — all rollups already exist (Phase A.5 recon)
- **No new BFF endpoints for the Performance pane** — Visual Host reads Dataverse WebAPI / FetchXML directly
- **Backward compatibility (binding, NFR-05)**: every existing in-production chart def MUST render unchanged after FR-VH-* extensions land
- **`/fluent-v9-component` skill invocation MANDATORY** for every UI task (design.md §6.0) — code-review blocks PRs that skipped it
- **`AssociatedOnly` auto-search behavior** at `SemanticSearchControl.tsx:359-375` preserved verbatim during filter relocation

### Key Technical Decisions

| Decision | Rationale | Impact |
|---|---|---|
| 5 Visual Host instances + 5 chart defs (NOT a new Performance pane PCF) | Existing Visual Host infrastructure is generic + sufficient; parallel chart library forbidden | Saves 1 net-new PCF; reuses production-validated renderer pipeline |
| Tags filter sources `sprk_documenttype` (relocated existing filter) | No new schema; reuses `DataverseMetadataService.fetchOptionSet` | Zero schema churn; 1 fewer projection field in BFF |
| `POST /api/documents/bulk-download` server-side zip stream | Streams via `SpeFileStore` → `ZipArchive` → HTTP response; bypasses 500-MB browser memory ceiling | New BFF endpoint with strict ADR-008 + ADR-007 + ADR-019 compliance |
| App Insights via direct browser SDK (NOT BFF-proxied) | Lower latency telemetry; instrumentation key via existing manifest-property env-var pattern | Adds `@microsoft/applicationinsights-web` to PCF deps; no BFF surface area |
| `CardChrome` internal-only (NOT exported from `@spaarke/ui-components`) | Wraps Visual Host cards with title + expand icon; concept too Visual-Host-specific to generalize today | Lives in `src/client/pcf/VisualHost/control/components/CardChrome.tsx` |
| Bulk Document Type apply = optimistic UI + 5s Undo toast | Confirmation dialog would block the most common bulk operation | Snappy UX; rollback on toast Undo or error retry |

### Discovered Resources

**Applicable ADRs** (10 — all verified to exist in `.claude/adr/`):
- [`ADR-006`](../../.claude/adr/ADR-006-pcf-over-webresources.md) — PCF surface architecture (binding context)
- [`ADR-007`](../../.claude/adr/ADR-007-spefilestore.md) — `SpeFileStore` facade (FR-BFF-02 file access)
- [`ADR-008`](../../.claude/adr/ADR-008-endpoint-filters.md) — Endpoint filters for auth (FR-BFF-02 pattern)
- [`ADR-010`](../../.claude/adr/ADR-010-di-minimalism.md) — DI minimalism (≤15 service cap)
- [`ADR-012`](../../.claude/adr/ADR-012-shared-components.md) — Shared component library
- [`ADR-019`](../../.claude/adr/ADR-019-problemdetails.md) — ProblemDetails + error handling (NOTE: spec mislabels as "SPE access" — see Spec drift note above)
- [`ADR-021`](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent UI v9 (binding for all UI tasks)
- [`ADR-022`](../../.claude/adr/ADR-022-pcf-platform-libraries.md) — PCF Platform Libraries (React 16/17 boundary)
- [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — Spaarke Auth v2
- [`ADR-029`](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — BFF publish hygiene

**Applicable Skills** (auto-discovered + spec-confirmed):
- **MANDATORY for every UI task**: [`.claude/skills/fluent-v9-component/`](../../.claude/skills/fluent-v9-component/) — design.md §6.0 binding
- **Every task**: [`.claude/skills/task-execute/`](../../.claude/skills/task-execute/) — CLAUDE.md §4 mandatory
- **Quality gates**: [`.claude/skills/code-review/`](../../.claude/skills/code-review/), [`.claude/skills/adr-check/`](../../.claude/skills/adr-check/) — FULL-rigor tasks Step 9.5
- **Resource discovery**: [`.claude/skills/adr-aware/`](../../.claude/skills/adr-aware/)
- **Deployment**: [`.claude/skills/pcf-deploy/`](../../.claude/skills/pcf-deploy/), [`.claude/skills/bff-deploy/`](../../.claude/skills/bff-deploy/)
- **Dataverse**: [`.claude/skills/dataverse:dv-metadata/`](../../.claude/skills/dataverse/) (sprk_documenttype option set, chart defs), [`.claude/skills/dataverse:dv-solution/`](../../.claude/skills/dataverse/) (form XML)

**Knowledge Articles + Patterns**:
- [`docs/architecture/VISUALHOST-ARCHITECTURE.md`](../../docs/architecture/VISUALHOST-ARCHITECTURE.md) (232 lines — FR-DOC-09 must update)
- [`docs/guides/VISUALHOST-SETUP-GUIDE.md`](../../docs/guides/VISUALHOST-SETUP-GUIDE.md) (2,112 lines — FR-DOC-09 must add chart-def authoring examples)
- [`.claude/patterns/ui/`](../../.claude/patterns/ui/) — Fluent v9 portal gotcha, theming, React version boundaries
- [`.claude/patterns/pcf/`](../../.claude/patterns/pcf/) — modern theming, canvas-vs-MDA-disabled
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — **BINDING** for FR-BFF-01/02 (Placement Justification required)
- [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) — mandatory PCF version-bump procedure; `npm run build:prod`

**Reusable Code** (canonical to follow — no parallel implementations):
- `FieldPivotService.ts` (VisualHost) — FR-VH-01 plugs into existing multi-field consumption
- `getTokenSetColors()` at [`MetricCardMatrix.tsx:138`](../../src/client/pcf/VisualHost/control/components/MetricCardMatrix.tsx#L138) — reuse for donut segment colors
- `cardConfigResolver.ts` (VisualHost utils) — chart-def + config-JSON + PCF override merge
- `handleExpandClick` + `sprk_chartdefinition` Drill Through Settings — FR-VH-05 `onExpand` wires here
- `authenticatedFetch()` from [SpeDocumentViewer BffClient.ts](../../src/client/pcf/SpeDocumentViewer/control/BffClient.ts) — auth pattern
- [`SemanticSearchAuthorizationFilter.cs`](../../src/server/api/Sprk.Bff.Api/Api/Filters/SemanticSearchAuthorizationFilter.cs) — endpoint filter shape for FR-BFF-02
- `SpeFileStore.DownloadFileAsync()` at [`SpeFileStore.cs:73`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs#L73) — FR-BFF-02 streaming
- `AiSummaryPopover` from `@spaarke/ui-components` — retained on row sparkle hover (FR-DOC-01)

**Visual contract**: [`c:\code_files\spaarke-prototype\projects\2026-05-matter-form-redesign\`](file:///c:/code_files/spaarke-prototype/projects/2026-05-matter-form-redesign/) (Variant H) + 5 local screenshots in [`./screenshots/`](./screenshots/).

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Foundation (3 tasks) — telemetry shared wiring, regression-test inventory, sprk_event schema verification
└─ Outputs: App Insights shared utility, regression-test list, verified sprk_event field names

Phase 1: Shared components (3 tasks) — TagFilter, DocumentRowMenu, barrel export
└─ Outputs: @spaarke/ui-components/src/components/{TagFilter,DocumentRowMenu}.tsx + updated barrel

Phase 2: Visual Host renderer extensions (6 tasks) — FR-VH-01..05 + regression smoke
└─ Outputs: Generic Custom Options additions in DonutChart, MetricCard, HSBar; new CardChrome wrapper

Phase 3: Chart definitions (5 tasks, parallel) — FR-DV-01..05
└─ Outputs: 5 sprk_chartdefinition records (Matter Health, Budget, Tasks, Next Date, Activity)

Phase 4: Documents PCF (8 tasks) — FR-DOC-01..07 + FR-DOC-09 docs
└─ Outputs: 3-dot menu, list/card toggle, tags filter, filter relocation, dialog restructure, bulk-action bar, telemetry, VisualHost docs

Phase 5: BFF (2 tasks) — FR-BFF-01 + FR-BFF-02
└─ Outputs: modifiedAt/modifiedBy projection; POST /api/documents/bulk-download endpoint

Phase 6: Form configuration (1 task) — FR-FORM-01
└─ Outputs: Matter main-form solution XML reconfigured (2-column 66/34, 5 Visual Host placements)

Phase 7: Deploy + cross-cutting validation (5 tasks)
└─ Outputs: PCFs deployed, BFF deployed, solution imported, axe + dark mode + NFR-05 regression smoke

Phase 8: Wrap-up (1 task) — mandatory per task-create Step 3.7
└─ Outputs: README → Complete, lessons-learned.md, repo-cleanup
```

### Critical Path

**Blocking Dependencies:**
- Phase 1 (shared components) BLOCKS Phase 4 (Documents PCF consumes TagFilter + DocumentRowMenu)
- Phase 2 (VH renderer extensions) BLOCKS Phase 3 (chart defs need renderer support) — except FR-DV-04 (DueDateCard, no renderer change)
- Phase 3 (chart defs) BLOCKS Phase 6 (form binds Visual Host instances to chart def lookups)
- Phase 5 BFF tasks BLOCK Phase 4 dependent tasks:
  - Task 050 (FR-BFF-01 modifiedAt/By) BLOCKS task 041 (list view default sort `modifiedAt DESC`)
  - Task 051 (FR-BFF-02 bulk-download) BLOCKS task 045 ("Download selected" bulk action)
- Phase 6 (form XML) BLOCKS Phase 7 (deploy via solution import)
- Phase 7 (deploys) BLOCKS Phase 8 (wrap-up validates everything live)

**High-Risk Items:**
- **NFR-05 regression**: every existing in-production chart def must render unchanged after Phase 2 — Mitigation: task 001 enumerates baseline; task 025 smoke-checks before merge
- **`sprk_event` field-name mismatch** (spec Assumption #1): could derail FR-DV-03, FR-DV-04, FR-DV-05 — Mitigation: task 003 verifies via MCP `describe_table` before Phase 3 starts
- **Fluent v9 portal gotcha** (Menu / Dialog inside MDA): biting in 3-dot menu, bulk-action dropdowns, FilePreviewDialog — Mitigation: every UI task invokes `/fluent-v9-component` (binding), loads `.claude/patterns/ui/fluent-v9-portal-gotcha.md`
- **Barrel merge in `@spaarke/ui-components/src/index.ts`**: 010 + 011 both export to barrel — Mitigation: 012 (barrel) serializes after both

---

## 4. Phase Breakdown

### Phase 0: Foundation (Wave 0)

**Objectives:**
1. Establish telemetry infrastructure used by every PCF task
2. Build the regression-test baseline for NFR-05
3. Verify `sprk_event` field assumptions before Phase 3 chart-def tasks rely on them

**Deliverables:**
- [ ] Shared App Insights wiring utility / hook used by both PCFs (FR-TEL-01 infra)
- [ ] `notes/spikes/visualhost-chart-def-inventory.md` — list of every in-production `sprk_chartdefinition` record + the visual type it uses (NFR-05 baseline)
- [ ] `notes/spikes/sprk_event-field-verification.md` — actual field names + types confirmed via MCP `describe_table`

**Critical Tasks**: 001 (regression inventory) is a hard prerequisite for task 025 (Phase 2 regression smoke).

**Inputs**: spec.md §FR-TEL-01, §NFR-05, §Assumptions

**Outputs**: 3 task POMLs, 2 spike notes

### Phase 1: Shared components

**Objectives:**
1. Add `TagFilter` (generic multi-select chip filter) to `@spaarke/ui-components`
2. Add `DocumentRowMenu` (13-action Fluent v9 Menu) to `@spaarke/ui-components`
3. Update barrel export

**Deliverables:**
- [ ] `src/client/shared/Spaarke.UI.Components/src/components/TagFilter.tsx` + types
- [ ] `src/client/shared/Spaarke.UI.Components/src/components/DocumentRowMenu.tsx` + `IDocumentRowMenuTarget` / `DocumentRowAction` types
- [ ] Updated `src/client/shared/Spaarke.UI.Components/src/index.ts` barrel

**Critical Tasks**: 010 + 011 in parallel; 012 (barrel) serial follow-up.

### Phase 2: Visual Host renderer extensions

**Objectives:**
1. Extend `DonutChart` with `fieldPivot` consumption + `matrixRight` layout + `colorThresholds` segment coloring + center via `meanOfFields` (FR-VH-01)
2. Add `badge` slot to `MetricCard` (FR-VH-02)
3. Add `descriptionColor` prop to `MetricCard` (FR-VH-03)
4. Add `headlineAboveBar` layout to `HorizontalStackedBar` (FR-VH-04)
5. Add internal `CardChrome` wrapper component (FR-VH-05)
6. Smoke-test backward compat against every chart def in regression baseline (NFR-05)

**Deliverables:**
- [ ] Generic Custom Options keys added to each renderer (`donutLayout`, `donutCenterMode`, `donutCenterLabel`, `showBreakdownRows`, `breakdownValueFormat`, `badge`, `descriptionColor`, `layoutMode`, `headlineFromField`, `subLineTemplate`)
- [ ] `src/client/pcf/VisualHost/control/components/CardChrome.tsx` (internal, NOT exported from shared lib)
- [ ] `VisualHostRoot.tsx` wraps each card in `CardChrome`
- [ ] Backward-compat regression smoke check on every chart def in baseline — ZERO visual regression

**Critical Tasks**: 021 + 022 BOTH touch `MetricCard.tsx` — serialize. 020, 023, 024 parallel-safe.

### Phase 3: Chart definitions

**Objectives:**
Create 5 `sprk_chartdefinition` records that bind to the Visual Host extensions from Phase 2.

**Deliverables:**
- [ ] FR-DV-01 Matter Health Composite (Donut, fieldPivot, matrixRight, meanOfFields)
- [ ] FR-DV-02 Matter Budget (HSBar, headlineAboveBar, colorThresholds)
- [ ] FR-DV-03 Matter Tasks (MetricCard, badge, FetchXML aggregation)
- [ ] FR-DV-04 Matter Next Date (DueDateCard — no renderer change)
- [ ] FR-DV-05 Matter Activity (MetricCard, descriptionColor: brand)

**Critical Tasks**: all 5 parallel-safe (different records). FR-DV-04 is the only one that does not depend on Phase 2 renderer changes.

### Phase 4: Documents PCF

**Objectives:**
Modernize the Semantic Search PCF per FR-DOC-01..07 + FR-DOC-09 doc updates.

**Deliverables:**
- [ ] 3-dot row menu replacing all inline icons + dialog toolbar icons (FR-DOC-01)
- [ ] List/card view toggle with sortable columns, multi-select, pinning, default sort `modifiedAt DESC` (FR-DOC-04)
- [ ] Tags filter (FR-DOC-05) consuming the new `TagFilter` shared component, sourced from `sprk_documenttype`
- [ ] Filters relocated from sidebar rail to command-bar dropdowns; `AssociatedOnly` auto-search behavior preserved verbatim (FR-DOC-06)
- [ ] Click-to-Dialog preview restructured to 960 px max-width 2-column layout (FR-DOC-03)
- [ ] Bulk-action bar (6 actions: email, download, pin, delete, doc-type, share link) (FR-DOC-02)
- [ ] App Insights telemetry replacing `console.log` (FR-DOC-07)
- [ ] VISUALHOST-ARCHITECTURE.md + VISUALHOST-SETUP-GUIDE.md updated with new Custom Options keys + chart-def authoring examples (FR-DOC-09)

**Critical Tasks**: Most touch `SemanticSearchControl.tsx` — serialize. FR-DOC-09 (047) touches docs only — parallel-safe.

### Phase 5: BFF

**Objectives:**
Add minimum-surface-area BFF changes per `.claude/constraints/bff-extensions.md`.

**Deliverables:**
- [ ] `SearchResult` shape + `/api/ai/search` projection adds `modifiedAt` + `modifiedBy` (FR-BFF-01)
- [ ] New `POST /api/documents/bulk-download` endpoint with `System.IO.Compression.ZipArchive` streaming, endpoint-filter auth, 500-id limit, `_FAILED.txt` manifest (FR-BFF-02)
- [ ] Placement Justification documented in commit message per `.claude/constraints/bff-extensions.md`

**Critical Tasks**: 050 + 051 parallel-safe (different files). NFR-06 publish-size check before merge.

### Phase 6: Form configuration

**Objectives:**
Reconfigure Matter main-form Overview tab to 2-column 66/34 layout with 5 Visual Host placements.

**Deliverables:**
- [ ] Matter main-form solution XML updated (FR-FORM-01)
- [ ] Left column (66%): Matter Information + Documents (Semantic Search PCF)
- [ ] Right column (34%): 5 stacked Visual Host instances bound to chart def lookups

**Critical Tasks**: 060 depends on all Phase 3 chart defs existing.

### Phase 7: Deploy + cross-cutting validation

**Objectives:**
Deploy each surface to dev environment + run cross-cutting NFR validation.

**Deliverables:**
- [ ] SemanticSearchControl PCF deployed (`npm run build:prod` → `pcf-deploy`)
- [ ] VisualHost PCF deployed (`npm run build:prod` → `pcf-deploy`)
- [ ] BFF deployed (`bff-deploy`)
- [ ] Dataverse solution imported (chart defs + form XML via `dataverse:dv-solution`)
- [ ] Cross-cutting validation: axe DevTools zero WCAG 2.1 AA violations (light + dark); hardcoded-color grep returns zero hits; NFR-05 regression smoke on Matter KPI Scorecard + Matter Financial Metrics Scorecard; App Insights events visible in <60s

**Critical Tasks**: 070 + 071 + 072 parallel-safe (different deploy targets). 073 + 074 serial after.

### Phase 8: Wrap-up

**Objectives:**
Mandatory wrap-up per task-create Step 3.7.

**Deliverables:**
- [ ] `/code-review` (FULL rigor) on all project code — no critical issues
- [ ] `/adr-check` on all project code — no violations
- [ ] `/repo-cleanup projects/spaarke-matter-ui-enhancement-r1` — ephemeral notes archived
- [ ] README.md status → Complete, all graduation criteria checkboxes ticked, Completed Date set
- [ ] `notes/lessons-learned.md` written
- [ ] All task files in TASK-INDEX.md show ✅

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|---|---|---|---|
| Application Insights resource (instrumentation key) | TBD | Medium | Phase 0 task confirms — fallback to provisioning if no existing instance |
| Dataverse permissions to author `sprk_chartdefinition` + modify Matter solution XML | TBD | Low | Standard dev-env permissions; verify in Phase 3 + Phase 6 prep |
| Existing in-production chart defs (Matter KPI Scorecard, Matter Financial Metrics Scorecard) | Present in prod | Low | Required as NFR-05 regression baseline; task 001 enumerates |

### Internal Dependencies

| Dependency | Location | Status |
|---|---|---|
| `@spaarke/auth` v2 | [`src/client/shared/Spaarke.Auth/`](../../src/client/shared/Spaarke.Auth/) | Production — both PCFs already use it |
| `@spaarke/ui-components` v2.0.0 (Fluent v9.73.2) | [`src/client/shared/Spaarke.UI.Components/`](../../src/client/shared/Spaarke.UI.Components/) | Production — adds 2 exports |
| Visual Host renderer suite | [`src/client/pcf/VisualHost/control/components/`](../../src/client/pcf/VisualHost/control/components/) | Production — extends generically |
| `SpeFileStore` facade (ADR-007) | [`src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs`](../../src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeFileStore.cs) | Production |
| ADR-006, 007, 008, 010, 012, 019, 021, 022, 028, 029 | [`.claude/adr/`](../../.claude/adr/) | Current |

---

## 6. Testing Strategy

**Unit Tests**:
- Shared components (FR-SC-01, FR-SC-02) — coverage targets per `.claude/patterns/testing/unit-test-structure.md`
- Visual Host renderer extensions — backward-compat unit tests (existing chart defs MUST render unchanged)
- BFF endpoints — `WebApplicationFactory` integration tests for both FR-BFF-01 and FR-BFF-02

**Integration Tests**:
- `/api/ai/search` projection contains `modifiedAt` + `modifiedBy` (FR-BFF-01)
- `POST /api/documents/bulk-download` — valid request returns zip; >500 ids returns 413; per-doc failures appear in `_FAILED.txt` manifest

**E2E / UI Tests** (defined inline in PCF task POMLs via `<ui-tests>` per task-create Step 3.65):
- Documents PCF: 3-dot menu opens + `stopPropagation` verified; list/card toggle persists; tags filter populates from `sprk_documenttype`; dialog opens at 960 px; bulk-action bar appears at ≥1 selection
- Visual Host: each of 5 cards renders against test matter; expand opens correct drill-through; dark-mode tokens swap correctly (ADR-021)
- Regression: Matter KPI Scorecard + Matter Financial Metrics Scorecard render visually identical to pre-change (NFR-05)
- Cross-cutting: axe DevTools (zero WCAG 2.1 AA violations); `prefers-reduced-motion` honored (NFR-08)

---

## 7. Acceptance Criteria

Acceptance criteria are tracked at the FR level — see [README.md §Graduation Criteria](./README.md#graduation-criteria) for the consolidated list and [spec.md §Success Criteria](./spec.md#success-criteria) for the full 36-item checklist.

**Phase-by-phase summary**:

**Phase 0**: Regression inventory + sprk_event field verification produced; App Insights wiring usable from both PCFs.
**Phase 1**: `TagFilter` + `DocumentRowMenu` exported from `@spaarke/ui-components`; pass a11y check; first consumers (Phase 4 tasks) work.
**Phase 2**: 5 generic extensions to renderers; CardChrome wraps cards; NFR-05 regression check passes.
**Phase 3**: 5 chart def records in Dataverse; each renders correctly when bound to a Visual Host instance on a test matter.
**Phase 4**: All 7 FR-DOC requirements met; AssociatedOnly auto-search preserved; VisualHost docs updated.
**Phase 5**: Both BFF additions ship with Placement Justification; publish-size within baseline.
**Phase 6**: Matter form Overview tab renders 2-column 66/34 layout binding all 5 Visual Host instances correctly.
**Phase 7**: All deploys live; cross-cutting validations pass.
**Phase 8**: Wrap-up complete; project status → Complete.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|---|---|---|---|---|
| R1 | FR-VH-01..04 breaks an existing in-production chart def | Medium | High | Task 001 enumerates baseline; task 025 smoke-checks before merge; binding NFR-05 |
| R2 | `sprk_event` field-name mismatch with spec assumptions | Medium | Medium | Task 003 verifies via MCP `describe_table` BEFORE Phase 3 chart-def tasks start |
| R3 | Fluent v9 portal gotcha bites Menu/Dialog inside MDA | Medium | Medium | `/fluent-v9-component` invocation MANDATORY for every UI task (design.md §6.0) |
| R4 | FR-BFF-02 pushes BFF publish-size past baseline | Low | Medium | BCL `ZipArchive` only (no new NuGet); `dotnet publish` size diffed pre-merge per NFR-06 |
| R5 | Matter form lives in a managed solution (not the deployment target) | Low | High | Task 060 prep: verify solution layer; if managed, document the workaround upfront |
| R6 | Barrel merge conflict in `@spaarke/ui-components/src/index.ts` | Medium | Low | Task 012 serializes barrel update after 010 + 011 both ship |
| R7 | App Insights instrumentation key not available in dev env | Low | Low | Phase 0 confirms; use existing Spaarke App Insights or provision per Phase 7 prerequisite |
| R8 | `AssociatedOnly` auto-search behavior breaks during filter relocation | Medium | Medium | Task 043 explicitly preserves SemanticSearchControl.tsx:359-375 verbatim; tested before merge |

---

## 9. Next Steps

1. **Review this plan + [README.md](./README.md)** for accuracy
2. **Tasks already generated** by `/project-pipeline` — see [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md)
3. **Begin Phase 0** with task `001-chart-def-regression-inventory.poml` via `task-execute`

---

**Status**: Ready for Tasks
**Next Action**: `task-execute projects/spaarke-matter-ui-enhancement-r1/tasks/001-chart-def-regression-inventory.poml`

---

*For Claude Code: This plan provides phase + dependency context. Per-task constraints live in each task POML's `<constraints>` and `<knowledge>` sections.*
