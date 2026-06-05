# Project Plan: Spaarke DataGrid Framework (R1)

> **Last Updated**: 2026-06-01
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md)
> **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Build one configuration-driven `<DataGrid configId={...} />` component in `@spaarke/ui-components` that every Spaarke surface uses to render Dataverse entity grids — zero per-entity TypeScript, MDA visual parity, Fluent v9 native, host-extensible.

**Scope**:
- Framework core in `@spaarke/ui-components` + 5 filter chip primitives + 6-action command bar
- `IDataverseClient` abstraction with Xrm + BFF implementations
- 5 BFF passthrough endpoints + cached projection services
- 2 drill-through Custom Pages + 3 new `sprk_gridconfiguration` records (Matter Health, Budget Performance, EventsPage)
- EventsPage migration onto framework + record-link bug fix
- SemanticSearch migration to v1.0 schema
- Retire 6 grid components + 1 PCF

**Timeline**: 6 phases (A–F) | **Estimated Effort**: ~36 tasks across foundation, BFF, drill-throughs, migrations, and retirement

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-006**: PCF only for form binding; Code Pages default for new UI
- **ADR-008**: Endpoint authorization filter pattern — required on all 5 BFF endpoints (FR-BFF-07)
- **ADR-012**: `@spaarke/ui-components` is the canonical shared component library; `IDataverseClient` abstraction lives here
- **ADR-019**: ProblemDetails on BFF endpoint errors (FR-BFF-01..05)
- **ADR-021**: Fluent UI v9 + dark mode + token-only styling (no raw hex)
- **ADR-022**: Framework code is React-16-safe (NFR-05); Custom Page hosts use React 18
- **ADR-026**: Full-page Custom Page standard — Vite + `vite-plugin-singlefile` + React 19 (Custom Pages only; framework stays React-16-safe)
- **ADR-028**: `BffDataverseClient` uses `@spaarke/auth.authenticatedFetch` ONLY (FR-BFF-06)
- **ADR-029**: BFF publish hygiene — framework-dependent linux-x64, sourcemap exclusion, transitive CVE override, size baseline ratchet

**From Spec**:
- Build on Fluent v9 native `<DataGrid>` primitive — no hand-rolled `<table>` (FR-DG-11)
- Match MDA Power Apps grid visual exactly (NFR-01) — codified in `DataGrid/tokens.ts`
- `applyStylesToPortals={true}` on every popover-bearing FluentProvider (NFR-03)
- `IDataverseClient` for all Dataverse access — no direct `Xrm.WebApi` in framework code (FR-DG-02)
- `@spaarke/auth.authenticatedFetch` for BFF calls in `BffDataverseClient` (FR-BFF-06, ADR-028)
- No `@fluentui/react` v8 anywhere (ADR-021)
- No React-18-only APIs in framework code (NFR-05)
- No `teamsHighContrastTheme` — Windows HC is automatic in Fluent v9 (FR-DG-10)
- No `window.confirm` — use Fluent v9 `<Dialog>` (FR-DG-14)
- Placement Justification per `.claude/constraints/bff-extensions.md` required for Phase B PRs (FR-BFF-08)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| Single `<DataGrid configId={...} />` with `IDataverseClient` injection | Cross-context portability (MDA + workspace widgets + Code Pages + future SPAs) | New abstraction layer; one component replaces 4+ bespoke implementations |
| `sprk_configjson` v1.0 as single config carrier | Avoid expanding `sprk_gridconfiguration` schema for every new field | Entity schema stays minimal; configjson absorbs growth |
| Three-tier config resolution (explicit → record → metadata defaults) | Zero-config rendering when no record exists | Test scenarios + Storybook stories work without authoring records |
| Lazy infinite-scroll paging (default 100, no "Load More") | Better UX for matter-context drill-throughs (typically <500 records) | IntersectionObserver-driven; FetchXML page cookie chain |
| Auto-derive filter chips from layoutXml + entity metadata | New entity = 0 new TypeScript | `AttributeType ∈ {Picklist, Status, State, Lookup, DateTime, Boolean}` → chip auto-promotion |
| Lift EventsPage CRUD as framework defaults | Avoid duplication; EventsPage was the reference implementation | Generalized `openNewEventForm`, `deleteSelectedEvents`, `executeBulkStatusUpdate` |
| Phase B BFF endpoints land in R1 (not R2) | Framework must work outside MDA from day one | 5 endpoints + cached projection services + integration tests in scope |
| Client-side CSV in R1, server-side Excel deferred | 99% of users need quick visible-columns export | Filename = `{entityName}-{savedQueryName}-{yyyymmdd}.csv`, UTF-8 BOM |
| `rowOpen.type: "webResource"` default via `Xrm.Navigation.navigateTo` | Closes record-link-not-opening bug (side pane opens behind dialog) | Side effect: EventsPage bug closed in Phase D |

### Discovered Resources

**Applicable ADRs**:
- [`ADR-006`](../../.claude/adr/ADR-006-pcf-over-webresources.md) — PCF only for form binding; Code Pages default for new UI
- [`ADR-008`](../../.claude/adr/ADR-008-endpoint-filters.md) — Endpoint authorization filter pattern (FR-BFF-07)
- [`ADR-012`](../../.claude/adr/ADR-012-shared-components.md) — `@spaarke/ui-components` canonical shared library
- [`ADR-019`](../../.claude/adr/ADR-019-problemdetails.md) — ProblemDetails on BFF errors
- [`ADR-021`](../../.claude/adr/ADR-021-fluent-design-system.md) — Fluent v9, dark mode, tokens-only
- [`ADR-022`](../../.claude/adr/ADR-022-pcf-platform-libraries.md) — React version boundaries
- [`ADR-026`](../../.claude/adr/ADR-026-full-page-custom-page-standard.md) — Custom Page standard
- [`ADR-028`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — `authenticatedFetch` only
- [`ADR-029`](../../.claude/adr/ADR-029-bff-publish-hygiene.md) — BFF publish hygiene

**Applicable Skills** (auto-loaded by tag-to-knowledge mapping):
- `.claude/skills/fluent-v9-component/` — MANDATORY Step 0.5 for every UI primitive task
- `.claude/skills/pcf-deploy/` — for `UniversalDatasetGrid` PCF retirement (Phase F)
- `.claude/skills/dataverse-create-schema/` — for `sprk_gridconfiguration` records (Phase C/D/E)
- `.claude/skills/dataverse-deploy/` — for solutions + Custom Pages + configuration records
- `.claude/skills/bff-deploy/` — for Phase B endpoint deploy
- `.claude/skills/code-review/` — quality gate at task close (FULL rigor tasks)
- `.claude/skills/adr-check/` — validate against ADR-008, ADR-012, ADR-021, ADR-022, ADR-028
- `.claude/skills/task-execute/` — per-task execution wrapper (mandatory)

**Applicable Patterns**:
- `.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md` — MDA theming context
- `.claude/patterns/pcf/fluent-v9-modern-theming.md` — Dark mode theming
- `.claude/patterns/ui/fluent-v9-component-authoring.md` — Component authoring
- `.claude/patterns/ui/fluent-v9-react-version-boundaries.md` — React 16/18/19 boundaries
- `.claude/patterns/dataverse/web-api-client.md` — `IDataverseClient` contract reference
- `.claude/patterns/webresource/full-page-custom-page.md` — Custom Page build + deploy

**Knowledge Articles** (operational guides):
- [`docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md`](../../docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md) — for `sprk_gridconfiguration` record authoring
- [`docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md`](../../docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md) — for `BffDataverseClient` auth
- [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) — for UDG retirement + solution version bump
- [`docs/guides/CONFIGURATION-MATRIX.md`](../../docs/guides/CONFIGURATION-MATRIX.md) — `sprk_configjson` v1.0 schema reference

**Binding Constraints**:
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — **Required for all Phase B tasks** (Placement Justification + decision criteria)

**Reusable Code** (canonical implementations to lift / model after):
- [`src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx`](../../src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx) — closest existing Fluent v9 native `DataGrid` usage; model framework core on this structure
- [`src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx`](../../src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx) lines 195-273 — MDA-parity styling already implemented; lift as `tokens.ts` entries
- [`src/client/shared/Spaarke.Events.Components/src/components/{ColumnHeaderMenu,ColumnFilterHeader}/`](../../src/client/shared/Spaarke.Events.Components/src/components/) — lift to `@spaarke/ui-components` with `applyStylesToPortals` fix
- [`src/client/shared/Spaarke.Events.Components/src/services/FetchXmlService.ts`](../../src/client/shared/Spaarke.Events.Components/src/services/FetchXmlService.ts) — generalize (`<row id>` instead of `sprk_eventid`) and lift
- [`src/solutions/EventsPage/src/App.tsx`](../../src/solutions/EventsPage/src/App.tsx) lines 375-715 — the 4 CRUD operations to lift as framework defaults (`openNewEventForm`, `deleteSelectedEvents`, `executeBulkStatusUpdate`, `openEventDetailPane`)
- `src/client/pcf/SemanticSearchControl/components/{CommandBar,BulkActionBar,FilterDropdown,FilterPanel,DateRangeFilter}.tsx` — patterns only (NOT copied directly) for chip + command bar primitive design
- `Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs` `SemanticSearchAuthorizationFilter` — model FR-BFF-07 endpoint filter shape

---

## 3. Implementation Approach

### Phase Structure

```
Phase A (Foundation) — Framework + Primitives
└─ DataGrid core, 5 filter chips, command bar, ColumnHeaderMenu, tokens.ts, IDataverseClient, Xrm impl, Storybook

Phase B (BFF Passthrough)
└─ 5 endpoints in Api/Dataverse/, cached projection services, BffDataverseClient impl, integration tests

Phase C (Matter UI Drill-throughs)
└─ 3 sprk_gridconfiguration records, 2 savedqueries, 2 Custom Pages, VisualHost chart-def updates, UAT

Phase D (EventsPage Migration)
└─ sprk_event config record, App.tsx rewrite (~150 lines), 4-component retirement, record-link bug closure, UAT

Phase E (SemanticSearch Migration)
└─ sprk_configjson v1.0 schema migration, SearchResultsGrid refactor, UAT

Phase F (Legacy Retirement)
└─ Remove DatasetGrid/{Card,List,VirtualizedGrid,VirtualizedList}, UniversalDatasetGrid PCF, SpeAdminApp migration
```

### Critical Path

**Blocking Dependencies**:
- Phase A blocks Phase B (BffDataverseClient uses `IDataverseClient` interface from Phase A)
- Phase A blocks Phase C (drill-through Custom Pages consume `<DataGrid />`)
- Phase A blocks Phase D (EventsPage migration consumes framework)
- Phase B blocks Phase C consumers that opt for BFF client (Custom Pages can default to Xrm client if BFF not ready)
- Phase D blocks Phase F (UDG retirement waits for SpeAdminApp migration, which depends on framework being stable per Phase D learnings)
- Phase E may run parallel with C and D (only depends on Phase A)

**High-Risk Items**:
- FR-DG-12 (lazy infinite-scroll with paging cookie chain) — Mitigation: build a 3-page test scenario in Storybook first
- FR-MIG-04 (EventsPage record-link bug) — Mitigation: UAT specifically the dialog→row→record-open path in all 4 modes
- NFR-01 (MDA pixel parity light + dark + 4 zoom levels) — Mitigation: codify in tokens.ts early (Phase A); visual diff gate per primitive

---

## 4. Phase Breakdown

### Phase A — Foundation (Framework + Primitives)

**Objectives**:
1. Build `<DataGrid configId={...} />` core with three-tier config resolution
2. Implement `IDataverseClient` + `XrmDataverseClient`
3. Build 5 filter chip primitives auto-derived from layoutXml + metadata
4. Build 6-action command bar with framework defaults
5. Lift `ColumnHeaderMenu` + `ColumnFilterHeader` with portal fix
6. Codify MDA visual parity in `tokens.ts`
7. Storybook coverage for every primitive (light + dark + axe)

**Deliverables**:
- [ ] `@spaarke/ui-components/src/components/DataGrid/{DataGrid.tsx, tokens.ts, types.ts}`
- [ ] `@spaarke/ui-components/src/components/DataGrid/chips/{LookupMultiFilterChip, OptionSetMultiFilterChip, DateRangeFilterChip, TextFilterChip, BoolFilterChip}.tsx`
- [ ] `@spaarke/ui-components/src/components/DataGrid/commandBar/CommandBar.tsx`
- [ ] `@spaarke/ui-components/src/components/DataGrid/columnHeader/{ColumnHeaderMenu, ColumnFilterHeader}.tsx`
- [ ] `@spaarke/ui-components/src/services/{IDataverseClient.ts, XrmDataverseClient.ts}`
- [ ] `@spaarke/ui-components/src/types/GridConfigJson.ts`
- [ ] `@spaarke/ui-components/src/hooks/useDataGridContext.ts`
- [ ] Storybook stories for every primitive (light + dark + axe-clean)

**Critical Tasks**:
- DataGrid core scaffold — BLOCKS everything in this phase
- `tokens.ts` codification — BLOCKS visual parity gates
- `IDataverseClient` interface — BLOCKS BffDataverseClient (Phase B)

**Inputs**: spec.md FR-DG-01..17, design.md §6/7/11, `GridSection.tsx` styles, `SearchResultsGrid.tsx` structure

**Outputs**: framework + primitives ready for consumer integration; Storybook fully covered

### Phase B — BFF Passthrough

**Objectives**:
1. Author 5 BFF passthrough endpoints in `Sprk.Bff.Api/Api/Dataverse/`
2. Build cached projection services (savedquery 1h, metadata 6h)
3. Implement `BffDataverseClient` using `authenticatedFetch`
4. Apply ADR-008 authorization filters on all endpoints
5. Document Placement Justification per `bff-extensions.md`

**Deliverables**:
- [ ] `Sprk.Bff.Api/Api/Dataverse/DataverseEndpoints.cs` — 5 endpoints
- [ ] `Sprk.Bff.Api/Services/Dataverse/{SavedQueryService, MetadataService, FetchService, RecordService}.cs`
- [ ] `Sprk.Bff.Api/Services/Dataverse/DataverseAuthorizationFilter.cs` (ADR-008 pattern)
- [ ] `@spaarke/ui-components/src/services/BffDataverseClient.ts`
- [ ] `tests/integration/Sprk.Bff.Api.IntegrationTests/Api/Dataverse/` — integration tests for 5 endpoints (with restricted-user 403 test per FR-BFF-07)
- [ ] PR description with Placement Justification section

**Critical Tasks**:
- Placement Justification design — BLOCKS endpoint authoring (must be agreed first)
- ADR-008 filter shape — BLOCKS all 5 endpoints

**Inputs**: spec.md FR-BFF-01..08, `.claude/constraints/bff-extensions.md`, `SemanticSearchAuthorizationFilter` model

**Outputs**: 5 endpoints deployable + `BffDataverseClient` ready for Custom Page consumers

### Phase C — Matter UI Drill-throughs

**Objectives**:
1. Author `sprk_gridconfiguration` record for `sprk_kpiassessment` (Matter Health)
2. Author `sprk_gridconfiguration` record for `sprk_invoice` (Budget Performance)
3. Author 2 new savedqueries (KPI Assessment + Invoice Matter-context views)
4. Build 2 new Custom Pages (`sprk_kpiassessmentspage.html`, `sprk_invoicespage.html`)
5. Update VisualHost chart-defs for Matter Health + Budget Performance `sprk_drillthroughtarget`
6. UAT drill-through flow end-to-end

**Deliverables**:
- [ ] Dataverse: `sprk_gridconfiguration` record for `sprk_kpiassessment`
- [ ] Dataverse: `sprk_gridconfiguration` record for `sprk_invoice`
- [ ] Dataverse: savedquery `sprk_kpiassessment_matter_context_view`
- [ ] Dataverse: savedquery `sprk_invoice_matter_context_view`
- [ ] `src/solutions/sprk_kpiassessmentspage/` — Custom Page (~50 lines)
- [ ] `src/solutions/sprk_invoicespage/` — Custom Page (~50 lines)
- [ ] Dataverse: VisualHost chart-def `sprk_drillthroughtarget` updated for Matter Health (`a8b8df8b-f359-f111-a825-3833c5d9bcab`) + Budget Performance (`7bf5b79e-f359-f111-a825-3833c5d9bcab`)

**Critical Tasks**:
- Savedquery layouts (UQ-04) — must define before configuration records
- Configjson per entity — must reference correct savedQueryId
- UAT — verifies VisualHost CardChrome → dialog → grid → row open path

**Inputs**: spec.md FR-CON-01..05, design.md §C, owner clarifications on column layouts

**Outputs**: working Matter Health + Budget Performance drill-throughs in production

### Phase D — EventsPage Migration

**Objectives**:
1. Author `sprk_gridconfiguration` record for `sprk_event` per design.md appendix
2. Rewrite `EventsPage/App.tsx` as ~150-line thin host (Calendar pane + handler registration only)
3. Retire `@spaarke/events-components/{GridSection, AssignedToFilter, RecordTypeFilter, StatusFilter}`
4. Close record-link-not-opening bug via `rowOpen.type: "webResource"` default
5. Migrate SpaarkeAi Calendar widget to consume new `<DataGrid />`
6. UAT all 4 modes (system / dialog / embedded / standalone)

**Deliverables**:
- [ ] Dataverse: `sprk_gridconfiguration` record for `sprk_event`
- [ ] `src/solutions/EventsPage/src/App.tsx` (rewritten, ≤200 lines, no `IEventRecord`)
- [ ] Retired directories: `@spaarke/events-components/components/{GridSection, AssignedToFilter, RecordTypeFilter, StatusFilter}/`
- [ ] SpaarkeAi Calendar widget updated to consume `<DataGrid configId={...} />`
- [ ] UAT report covering all 4 modes
- [ ] Bug closure: EventsPage record-link in dialog mode verified

**Critical Tasks**:
- `BulkUpdateEventStatus` handler registration — preserves existing Mark Complete / Cancel / Reassign / Archive UX
- Calendar pane mutual exclusivity — must remain after migration
- Record-link bug UAT — specifically the VisualHost → dialog → row → record-open path

**Inputs**: spec.md FR-MIG-01..05, design.md §11.5.3 / §11.5.4, EventsPage CRUD reference

**Outputs**: EventsPage running on framework with no UX regression + record-link bug closed

### Phase E — SemanticSearch Migration

**Objectives**:
1. Migrate SemanticSearch `sprk_gridconfiguration` record (`d99a4352-4913-f111-8343-7ced8d1dc988`) `sprk_configjson` to v1.0 schema
2. Refactor `SearchResultsGrid.tsx` to consume new `<DataGrid />` (or retire if framework covers all features)
3. UAT all existing SS scenarios with zero visual regression

**Deliverables**:
- [ ] Dataverse: SemanticSearch `sprk_gridconfiguration` record updated to v1.0 schema with `source.type = "inline"` (preserves custom fetchXml/layoutXml)
- [ ] `src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx` migrated or retired
- [ ] `searchResultAdapter.ts` / `useSearchViewDefinitions.ts` lifted into framework projection layer (if applicable)
- [ ] UAT report covering all existing SS UAT scenarios + visual diff

**Critical Tasks**:
- v1.0 schema migration must preserve existing SS fetchXml/layoutXml exactly
- Visual diff gate — zero regression vs. pre-migration screenshot

**Inputs**: spec.md FR-MIG-06..07, design.md §E, existing SS UAT scenarios

**Outputs**: SS code page running on framework

### Phase F — Legacy Retirement

**Objectives**:
1. Remove `@spaarke/ui-components/DatasetGrid/{GridView, CardView, ListView, VirtualizedGridView, VirtualizedListView}.tsx`
2. Migrate `SpeAdminApp/src/components/dashboard/DashboardPage.tsx` from `UniversalDatasetGrid` PCF to new `<DataGrid />`
3. Retire `src/client/pcf/UniversalDatasetGrid/` PCF
4. Solution version bump for UDG removal

**Deliverables**:
- [ ] Removed: `@spaarke/ui-components/components/DatasetGrid/{GridView, CardView, ListView, VirtualizedGridView, VirtualizedListView}.tsx`
- [ ] Updated: `@spaarke/ui-components/src/index.ts` barrel exports
- [ ] `src/solutions/SpeAdminApp/src/components/dashboard/DashboardPage.tsx` migrated
- [ ] Removed: `src/client/pcf/UniversalDatasetGrid/`
- [ ] Solution manifest version bump

**Critical Tasks**:
- Audit consumers of removed exports — must migrate all consumers before deletion
- SpeAdminApp DashboardPage visual diff vs. UDG render

**Inputs**: spec.md FR-MIG-08..09, current consumer list of removed exports

**Outputs**: clean repo with no legacy grid implementations; one framework, one client abstraction

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Fluent UI v9.46+ | Stable | Low | Pinned; flag major version PRs for review |
| Dataverse savedquery (`sprk_kpiassessment` Matter context) | Pending | Med | Phase C author task (UQ-04) — Ralph to define columns |
| Dataverse savedquery (`sprk_invoice` Matter context) | Pending | Med | Phase C author task (UQ-04) — Ralph to define columns |
| Power Apps Maker portal access | Ready | Low | Standard deploy |
| SpaarkeAi Calendar widget owner sign-off (FR-MIG-05) | Pending | Med | Pre-Phase-D coordination needed (UQ-06) |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| `@spaarke/auth` v2 — `authenticatedFetch` | `src/client/shared/@spaarke/auth/` | Production |
| `@spaarke/ui-components` Storybook | `src/client/shared/Spaarke.UI.Components/storybook/` | Production |
| `Sprk.Bff.Api` `IDistributedCache` (Redis) | `src/server/api/Sprk.Bff.Api/` | Production |
| VisualHost v1.4.16 (drill-through dialog mode) | `src/client/pcf/VisualHost/` | Production |
| ADR-008, ADR-012, ADR-021, ADR-022, ADR-026, ADR-028, ADR-029 | `docs/adr/` | Current |

---

## 6. Testing Strategy

**Unit Tests** (≥80% coverage on framework code):
- `<DataGrid />` config resolution paths (FR-DG-04 three-tier)
- `IDataverseClient` contract conformance (Xrm + BFF impls)
- Each filter chip primitive (auto-derivation + state changes)
- Command bar default handlers (`create-form`, `delete-selected`, `refresh`, `export-excel`)
- `useDataGridContext` hook (FR-DG-15)
- CSV export RFC 4180 quoting + UTF-8 BOM (FR-DG-17)

**Integration Tests**:
- 5 BFF passthrough endpoints (200 / 401 / 403 / 404 / ProblemDetails shapes)
- Cache hit/miss behavior on savedquery + metadata endpoints (FR-BFF-01..03)
- `BffDataverseClient` against dev BFF endpoints

**E2E Tests** (Storybook + manual UAT):
- Storybook coverage for every primitive — light + dark + axe-clean (NFR-04, NFR-07)
- MDA pixel parity screenshot diff vs. native MDA Events sub-grid (NFR-01)
- EventsPage migration UAT — 4 modes (system / dialog / embedded / standalone)
- EventsPage record-link bug UAT — VisualHost → dialog → row → record-open path (FR-MIG-04)
- Matter Health + Budget Performance drill-throughs end-to-end (Phase C UAT)
- SemanticSearch zero-regression UAT (Phase E)
- Adding a new entity grid with zero new TypeScript (CI scenario, graduation criterion #5)

---

## 7. Acceptance Criteria

### Technical Acceptance

**Phase A**:
- [ ] `<DataGrid configId={...} />` renders against `sprk_event` config record in Storybook with no errors
- [ ] All 5 filter chips render in light + dark + auto-derive correctly per `AttributeType`
- [ ] Command bar's 6 actions work via default handlers
- [ ] `tokens.ts` is single source of truth (zero hex in `DataGrid/`)
- [ ] axe-core: zero serious/critical violations per Storybook story

**Phase B**:
- [ ] 5 endpoints respond with documented shapes
- [ ] ADR-008 filters reject unauthorized with 403 ProblemDetails
- [ ] Cache hit rate >95% after warmup
- [ ] Placement Justification in PR description per `bff-extensions.md`

**Phase C**:
- [ ] VisualHost CardChrome expand on Matter Health card → dialog → KPI grid renders with current-Matter context filter
- [ ] Same for Budget Performance → Invoice grid
- [ ] Both Custom Pages open in dialog + standalone modes

**Phase D**:
- [ ] EventsPage all 4 modes pass UAT
- [ ] Record-link bug closed (VisualHost → dialog → row → record opens)
- [ ] EventsPage `App.tsx` ≤200 lines, no `IEventRecord`
- [ ] Calendar pane mutual exclusivity preserved

**Phase E**:
- [ ] SS code page renders identically pre/post migration (visual diff)
- [ ] All existing SS UAT scenarios pass

**Phase F**:
- [ ] Removed directories absent; build passes
- [ ] SpeAdminApp DashboardPage renders identically to UDG-based version

### Business Acceptance

- [ ] Adding a new entity grid in a CI test scenario requires zero new TypeScript (graduation criterion #5)
- [ ] First-page p50 latency <2s on default tenant (NFR-10)
- [ ] Lazy-load page p50 latency <1s (NFR-10)
- [ ] Bundle size delta <120KB gzip on `@spaarke/ui-components` (NFR-10)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | Fluent v9 native `DataGrid` lacks a feature we'd assumed (e.g. cell-level focus restoration) | Med | Med | Storybook story gate per feature (NFR-07); wrapper-level fallback if needed |
| R2 | MDA visual parity drifts from `tokens.ts` after MDA OOB grid updates | Med | Low | Centralized tokens; quarterly visual diff check |
| R3 | Bundle size budget (<120KB gzip) exceeded | Med | Med | webpack-bundle-analyzer CI gate per NFR-10 |
| R4 | EventsPage migration introduces regression in dialog mode | High | Low | UAT all 4 modes (FR-MIG-04) |
| R5 | Cache invalidation lag (1h savedquery TTL) causes dev friction | Med | Low | TTL intentional; add cache-bust endpoint in R2 if friction high |
| R6 | SemanticSearch migration breaks an existing UAT scenario | High | Low | Phase E reserved for SS-specific tests; visual diff gate |
| R7 | Lazy-load page cookie expiry (60min Dataverse default) breaks scroll continuity | Low | Low | Framework refetches from page 1 after idle (AS-03) |
| R8 | Calendar pane mutual exclusivity broken by event detail pane migration | Med | Low | Lift wiring as host concern in Phase D, not framework concern |
| R9 | `@fluentui/react-components` major version bump invalidates DataGrid API | Low | Low | Pin v9.46+; flag major version PRs |
| R10 | Phase B endpoint scope creep (more than 5 endpoints) | Med | Low | Placement Justification + `bff-extensions.md` gate enforces discipline |

---

## 9. Next Steps

1. **Review this plan.md** alongside spec.md and design.md
2. **Run `/task-create projects/spaarke-datagrid-framework-r1`** to decompose into POML task files (auto-invoked by `/project-pipeline` Step 3)
3. **Begin Phase A** — DataGrid core scaffold (task 001)

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files via task-create skill (Step 3 of project-pipeline)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks via task-execute.*
