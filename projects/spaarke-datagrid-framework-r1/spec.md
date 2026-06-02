# Spaarke DataGrid Framework — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-01
> **Source**: `projects/spaarke-datagrid-framework-r1/design.md` (≈700 lines)
> **Author**: AI-assisted spec extraction from interactively-authored design (Ralph Schroeder)

---

## Executive Summary

Build a single configuration-driven dataset grid framework in `@spaarke/ui-components` that any Spaarke surface — MDA Custom Page, SpaarkeAi workspace widget, MCP App widget, future external SPA — can use to display, filter, and act on any Dataverse entity with **zero per-entity TypeScript code**. The framework's contract is one Dataverse record (`sprk_gridconfiguration`) + one injectable Dataverse client (`IDataverseClient` with `Xrm` and `BFF` implementations).

R1 delivers: the framework + BFF passthrough endpoints + EventsPage migration onto it + the Matter Health and Budget Performance drill-throughs that triggered this design. R1 also closes the EventsPage record-link-not-opening bug (dialog-mode side pane failure) as a side effect of the new `rowOpen.type: "webResource"` default handler.

## Scope

### In Scope

- New `<DataGrid configId={...} />` component in `@spaarke/ui-components` built on Fluent v9 native `DataGrid` primitive
- `IDataverseClient` interface + two implementations: `XrmDataverseClient` (Xrm.WebApi wrapper) and `BffDataverseClient` (calls BFF passthrough endpoints via `@spaarke/auth.authenticatedFetch`)
- Five filter chip primitives: `LookupMultiFilterChip` (async Combobox), `OptionSetMultiFilterChip`, `DateRangeFilterChip`, `TextFilterChip`, `BoolFilterChip` — all auto-derivable from layoutXml column + entity metadata `AttributeType`
- Command bar primitive with six built-in actions: `create-form`, `delete-selected`, `refresh`, `export-excel` (CSV), `edit-columns`, `edit-filters` — plus `custom` action with host-registered handler
- Server-side paging with lazy infinite-scroll loading (default page size 100, configurable via `sprk_configjson.behavior.pageSize`)
- `sprk_configjson` schema v1.0 (full schema in design.md §6.3)
- Three-tier config resolution: explicit prop overrides → `sprk_gridconfiguration` record → entity metadata + layoutXml fallbacks
- Host extension model: three emit-events (`onRecordOpen`, `onRecordAction`, `onCommandInvoke`) + handler registry (`registerCommandHandler`, `registerWizard`, `registerDialog`)
- Four BFF passthrough endpoints in `Sprk.Bff.Api/Api/Dataverse/`: savedquery / savedqueries-for-entity / metadata / fetch + record retrieve
- Design tokens for MDA Power Apps grid visual parity codified in `@spaarke/ui-components/src/components/DataGrid/tokens.ts`
- Two new `sprk_gridconfiguration` records (one for `sprk_kpiassessment`, one for `sprk_invoice`) + two new Custom Pages (`sprk_kpiassessmentspage.html`, `sprk_invoicespage.html`)
- VisualHost chart-def updates: Matter Health + Budget Performance `sprk_drillthroughtarget` → new Code Pages
- EventsPage migrated onto framework (~150-line host shell; Calendar pane + side-pane wiring retained as host concerns)
- SemanticSearch code page migrated to v1.0 `sprk_configjson` schema (drops `_type: semantic-search-view`)
- Retire: `@spaarke/events-components/GridSection`, `AssignedToFilter`, `RecordTypeFilter`, `StatusFilter`; `@spaarke/ui-components/DatasetGrid/GridView`, `CardView`, `ListView`, `VirtualizedGridView`, `VirtualizedListView`; the `UniversalDatasetGrid` PCF (with SpeAdminApp DashboardPage migrated to new DataGrid)
- Storybook coverage for every primitive (light + dark + axe a11y); MDA pixel-parity screenshot diff acceptance gate

### Out of Scope

- `SemanticSearchControl` PCF — NOT touched. Its hand-rolled `ListView` + `ResultCard` + `CommandBar` + `FilterPanel` keep shipping as-is. Zero impact required.
- `VisualHost` PCF code — NOT touched (only its chart-def `sprk_drillthroughtarget` value changes for two cards)
- OOB sub-grids in Dataverse entity main forms — NOT replaced; Spaarke grids appear only in Custom Pages + workspace widgets
- Per-user grid customization (column show/hide, reorder, save view) — R2 candidate
- Per-user filter persistence across sessions — R2 candidate
- Charting / visualization — `VisualHost` PCF remains the canonical chart framework
- Write operations through `BffDataverseClient` — R1 `BffDataverseClient` is READ-ONLY. All writes go through `XrmDataverseClient` (Xrm.WebApi) in R1
- Server-side "Export to Excel" via Dataverse workflow — R2 if needed. R1 ships client-side CSV only.
- Cross-entity correlation / joins beyond what FetchXML's `<link-entity>` natively supports

### Affected Areas

| Path | Change | Phase |
|---|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/` (NEW dir) | Build `<DataGrid />`, primitives, tokens | A |
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/tokens.ts` (NEW) | Codified MDA parity design tokens | A |
| `src/client/shared/Spaarke.UI.Components/src/services/` | Add `XrmDataverseClient.ts`, `BffDataverseClient.ts`; generalize `FetchXmlService`, `ViewService`, `ConfigurationService`, `ColumnRendererService` | A, B |
| `src/client/shared/Spaarke.UI.Components/src/index.ts` | Barrel export new DataGrid + chip primitives + client interfaces | A |
| `src/client/shared/Spaarke.UI.Components/storybook/` | Storybook stories for every primitive | A |
| `src/client/shared/Spaarke.Events.Components/src/components/GridSection/` (RETIRE after migration) | Replaced by new DataGrid | D |
| `src/client/shared/Spaarke.Events.Components/src/components/{AssignedTo,RecordType,Status}Filter/` (RETIRE) | Replaced by auto-derived chips | D |
| `src/client/shared/Spaarke.Events.Components/src/components/{ColumnHeaderMenu,ColumnFilterHeader}/` | Lift to `@spaarke/ui-components` with `applyStylesToPortals` fix | A |
| `src/client/shared/Spaarke.Events.Components/src/services/FetchXmlService.ts` | Lift to `@spaarke/ui-components` after de-Event-typing (read `<row id>` from layoutXml instead of hardcoded `sprk_eventid`) | A |
| `src/solutions/EventsPage/src/App.tsx` (REWRITE → ~150 lines) | Becomes thin host: registers Event handlers, mounts Calendar pane, passes `configId` to DataGrid | D |
| `src/solutions/sprk_kpiassessmentspage/` (NEW) | ~50-line Custom Page for Matter Health drill-through | C |
| `src/solutions/sprk_invoicespage/` (NEW) | ~50-line Custom Page for Budget Performance drill-through | C |
| `src/solutions/SpeAdminApp/src/components/dashboard/DashboardPage.tsx` | Migrate from `UniversalDatasetGrid` PCF binding → new DataGrid | F |
| `src/client/pcf/UniversalDatasetGrid/` (RETIRE) | PCF shell retired after F2 | F |
| `src/server/api/Sprk.Bff.Api/Api/Dataverse/` (NEW dir) | 4 passthrough endpoints + endpoint filters | B |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/` (NEW dir) | Cached projection services for savedquery + metadata | B |
| `src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx` | Migrate to consume new DataGrid (or retire) | E |
| Dataverse: `sprk_gridconfiguration` records (NEW × 3+) | `sprk_kpiassessment`, `sprk_invoice`, `sprk_event` config records; SemanticSearch record migrated to v1.0 schema | C, D, E |
| Dataverse: VisualHost chart-defs (UPDATE × 2) | Matter Health + Budget Performance `sprk_drillthroughtarget` | C |
| Dataverse: savedquery records (NEW × 2) | KPI Assessment layout + Invoice layout for the drill-through views | C |

## Requirements

### Functional Requirements

#### Phase A — Foundation (Framework + Primitives)

**FR-DG-01**: `<DataGrid configId={...} parentContext={...} onRecordOpen={...} onRecordAction={...} onCommandInvoke={...} dataverseClient={...} overrides={...} />` component in `@spaarke/ui-components/DataGrid` per [design.md §6.1](design.md). — Acceptance: Storybook story renders with valid `configId`; defaults to `XrmDataverseClient` when no client passed; emits the three event signals with full context object including `selectedIds`, `entityName`, `parentContext`.

**FR-DG-02**: `IDataverseClient` interface with `retrieveSavedQuery`, `retrieveSavedQueriesForEntity`, `retrieveEntityMetadata`, `retrieveMultipleRecords`, `retrieveRecord` (per [design.md §6.2](design.md)). `XrmDataverseClient` implementation wraps `Xrm.WebApi` + `Xrm.Utility.getEntityMetadata`, walks `window` / `window.parent` to find Xrm. — Acceptance: contract type-checks at consumer boundary; XrmDataverseClient unit tests pass against mocked Xrm; works in MDA Custom Page iframe context.

**FR-DG-03**: `sprk_configjson` v1.0 schema implemented per [design.md §6.3](design.md). TypeScript discriminated union types in `@spaarke/ui-components/src/types/GridConfigJson.ts`. — Acceptance: invalid JSON rejected with a non-throwing fall-back (framework defaults apply); schema includes `_version: '1.0'` and all sub-sections (source / display / filterChips / commandBar / rowOpen / secondaryActions / columns / behavior).

**FR-DG-04**: Three-tier config resolution per [design.md §6.4](design.md): explicit prop overrides → `sprk_gridconfiguration.sprk_configjson` → entity metadata + layoutXml defaults → framework defaults. — Acceptance: a `<DataGrid />` with `configId` pointing to a non-existent record still renders correctly using metadata + layoutXml defaults; a configjson referencing a removed field gracefully falls through to default rendering for that field.

**FR-DG-05**: Host extension model. Three emit events: `onRecordOpen(recordId, record, ctx)`, `onRecordAction(actionId, recordId, record, ctx)`, `onCommandInvoke(commandId, selectedIds, ctx)`. Handler registry: `registerCommandHandler(id, handler)`, `registerWizard(name, component)`, `registerDialog(name, component)`. Framework-default handlers for `rowOpen.type ∈ {sidePane, wizard, navigateToForm, dialog, webResource, custom}` and `secondaryActions[].kind ∈ {ai-assistant, playbook, wizard, navigate, custom}` per [design.md §7.2](design.md). — Acceptance: a host that registers no handlers still gets sensible default UX; a host that overrides one type takes precedence over the framework default; conflicts between custom handlers logged via `console.warn` (last-write-wins).

**FR-DG-06**: Auto-derived filter chips from layoutXml columns + entity metadata. Default mode = chips auto-promoted for any visible column whose `attributeType ∈ {Picklist, Status, State, Lookup, DateTime, Boolean}`. Configjson `filterChips.mode ∈ {auto, allowlist, denylist, explicit}` controls the set. Per [design.md §8.1](design.md). — Acceptance: a savedquery with no configjson renders a working filter strip; explicit configjson overrides labels and chip kinds; chip layout matches MDA Power Apps view-toolbar density.

**FR-DG-07**: Five filter chip primitives in `@spaarke/ui-components/components/DataGrid/chips/`:
- `<LookupMultiFilterChip />` — Fluent `Combobox` with debounced type-to-search via `IDataverseClient.retrieveMultipleRecords` (async option load)
- `<OptionSetMultiFilterChip />` — Fluent `Menu` + `MenuItemCheckbox`, colors from entity metadata `optionSet.color`
- `<DateRangeFilterChip />` — Fluent `Popover` + `Calendar` (start + end), LOCAL → UTC bounds conversion (port the `localDateToUtcBounds` logic from [GridSection.tsx:358](src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx#L358))
- `<TextFilterChip />` — Fluent `Popover` + `Input` with `contains` operator
- `<BoolFilterChip />` — Fluent `Dropdown` Yes / No / Any

— Acceptance: Each chip has Storybook stories for empty / single-select / multi-select / cleared states in light + dark; each chip's popover surface uses `applyStylesToPortals={true}` on its inner FluentProvider per [design.md §11.5.4](design.md).

**FR-DG-08**: Command bar primitive with six built-in actions:
- `create-form` — `Xrm.Navigation.openForm({ entityName, createFromEntity })` (lifted from EventsPage `openNewEventForm` per [design.md §11.5.3](design.md))
- `delete-selected` — `Xrm.WebApi.deleteRecord` per-ID via `Promise.all` with Fluent v9 `<Dialog>` confirmation (replacing EventsPage's `window.confirm`)
- `refresh` — re-fetch current page set
- `export-excel` — client-side CSV of visible columns of currently-loaded records, respecting active filter chip state (per Q-D answer)
- `edit-columns` — hidden in R1 (per scope: no user customization)
- `edit-filters` — toggles filter chip visibility row

Plus `custom` action invokes `registerCommandHandler(customHandlerId)` registered handler.

— Acceptance: Command bar matches MDA OOB visual per [design.md §11.5.2](design.md) parity tokens; CSV export validated against Excel + LibreOffice; bulk delete shows progress for >10 records.

**FR-DG-09**: Lift `ColumnHeaderMenu` and `ColumnFilterHeader` from `@spaarke/events-components` to `@spaarke/ui-components/DataGrid/columnHeader/` with `applyStylesToPortals={true}` on every `Popover` and `Menu` surface. — Acceptance: dark mode renders correctly in popovers (closes the existing Fluent v9 portal-gotcha gap); axe a11y passes; keyboard accessibility (Enter / Space / Esc / Arrow) verified.

**FR-DG-10**: Design tokens codified at `@spaarke/ui-components/src/components/DataGrid/tokens.ts` per [design.md §11.5.2](design.md) parity table. All primitives + the DataGrid itself import from this single source. NO inline hex anywhere in framework code. — Acceptance: grep for `#[0-9a-fA-F]` in `DataGrid/` returns zero; light + dark + Windows High Contrast all render correctly with NO `teamsHighContrastTheme` branching.

**FR-DG-11**: Fluent v9 native `DataGrid` feature exploitation per [design.md §11.5.1](design.md). Selection via `<DataGrid selectionMode>`, sort via `createTableColumn({ compare })`, resize via `resizableColumns + columnSizingOptions`, keyboard nav via `focusMode`, density via `<DataGrid size>`, sticky header via Fluent's built-in layout. NO hand-rolled equivalents. — Acceptance: Storybook story per feature demonstrating it works in the framework wrapper; `GridSection.tsx`-style hand-rolled checkbox columns / sort arrows / sticky headers do NOT appear in framework code (code review gate).

**FR-DG-12**: Server-side paging with lazy infinite-scroll loading. Default page size **100**, configurable via `sprk_configjson.behavior.pageSize`. `IntersectionObserver` on a sentinel row near the bottom triggers fetch of next page via FetchXML `<fetch page='N' paging-cookie='...'/>`. Loading spinner row appears at bottom while next page loads. Selection state (`Set<string>` of selected IDs) preserved across lazy-load pages. NO "Load More" button. NO classic pagination controls. — Acceptance: scroll-to-bottom on a 1000-record entity loads page 2, 3, … without user click; selection persists; replays existing chip state on each fetch; recovers from network failure with retry CTA.

**FR-DG-13**: Async lookup-multi filter chip via Fluent `Combobox`. Debounced (300ms) type-to-search calls `IDataverseClient.retrieveMultipleRecords(lookupTargetEntity, fetchXmlContains)`. — Acceptance: typing "Acme" surfaces matching options within 500ms (cached for 60s in component state); empty search shows top 50 recent; works for `systemuser`, `sprk_matter`, `sprk_vendor`, and any other lookup target.

**FR-DG-14**: CRUD default handlers lifted from EventsPage per [design.md §11.5.3](design.md):
- `create-form` ← `openNewEventForm` ([App.tsx:461](src/solutions/EventsPage/src/App.tsx#L461)), generalized for any entity, parent-context pre-fill preserved
- `delete-selected` ← `deleteSelectedEvents` ([App.tsx:687](src/solutions/EventsPage/src/App.tsx#L687)), generalized, `window.confirm` → Fluent v9 `<Dialog>`
- bulk field update ← `executeBulkStatusUpdate` ([App.tsx:731](src/solutions/EventsPage/src/App.tsx#L731)), generalized as registered framework handler `BulkUpdateField` with `{fieldName, value}` payload; Promise.all + global notification preserved
- `rowOpen.type: "webResource"` default handler uses `Xrm.Navigation.navigateTo({pageType:"webresource"...})` which works in dialog mode (closes EventsPage record-link-not-opening bug)

— Acceptance: EventsPage CRUD commands work identically after migration to framework defaults; record-link bug closed when validated from VisualHost drill-through → EventsPage dialog → row click → record opens correctly.

**FR-DG-15**: `useDataGridContext()` hook returning `{ selectedIds, refresh, currentView, parentContext, dataverseClient, entityMetadata }`. Available to host extensions (registered command handlers, secondary action handlers, custom wizards). — Acceptance: a custom command handler can call `ctx.refresh()` after a bulk operation without prop drilling; React Strict Mode passes.

**FR-DG-16**: Density toggle wired to Fluent v9 native `<DataGrid size>`. `sprk_configjson.display.densityDefault ∈ {"extra-small", "small", "medium"}`. Optional toolbar toggle button. — Acceptance: density change reflows row heights instantly; persists in session storage per-grid-instance (per Q1 answer — session only).

**FR-DG-17**: Client-side CSV export. Visible columns of currently-loaded records, respecting active filter chip state. RFC 4180 quoting. Filename = `{entityName}-{savedQueryName}-{yyyymmdd}.csv`. — Acceptance: opens cleanly in Excel + LibreOffice + Google Sheets; UTF-8 BOM included for Excel compatibility; large datasets (5000 rows) export in <2s.

#### Phase B — BFF Passthrough (R1 per Q-C answer)

**FR-BFF-01**: `GET /api/dataverse/savedquery/{savedQueryId}` returns `{ entityName, fetchXml, layoutXml, name }`. `IDistributedCache` with **1 hour** TTL keyed by savedQueryId. Per [design.md §9](design.md). — Acceptance: cache hit-rate >95% after warmup; cache miss reads from Dataverse via app-only `ServiceClient`; ProblemDetails on errors per ADR-019.

**FR-BFF-02**: `GET /api/dataverse/savedqueries/{entityLogicalName}` returns `[{ id, name, isDefault, queryType }]`. 1-hour cache. — Acceptance: returns only `statecode = 0 AND queryType = 0` (saved view, not user query); excludes archived/inactive.

**FR-BFF-03**: `GET /api/dataverse/metadata/{entityLogicalName}` returns projected entity metadata: `primaryIdAttribute`, `primaryNameAttribute`, `attributes[].{logicalName, attributeType, format, isPrimaryName, isPrimaryId, optionSet?}`. `IDistributedCache` with **6 hour** TTL. — Acceptance: metadata refresh on solution import detected within 6h window; payload <50KB per entity (no unused metadata).

**FR-BFF-04**: `POST /api/dataverse/fetch` body `{ entityName, fetchXml, pagingCookie? }` returns `{ entities, moreRecords, pagingCookie }`. NO caching. — Acceptance: roundtrip <500ms p50 on default tenant; handles FetchXML `<link-entity>` correctly; rejects malformed FetchXML with ProblemDetails.

**FR-BFF-05**: `GET /api/dataverse/record/{entityLogicalName}/{id}?$select=field1,field2` returns single record. NO caching. — Acceptance: 404 ProblemDetails on missing record; 403 ProblemDetails on read-denied via authorization filter.

**FR-BFF-06**: `BffDataverseClient` implementation in `@spaarke/ui-components/services/BffDataverseClient.ts` using `authenticatedFetch` from `@spaarke/auth` per ADR-028. — Acceptance: same 5-method `IDataverseClient` contract as `XrmDataverseClient`; consumer can swap implementations without code change; 401 retry via authenticatedFetch's built-in behavior.

**FR-BFF-07**: ADR-008 endpoint authorization filters on all 5 BFF endpoints. Filter checks caller has `Read` privilege on the target entity via cached metadata. — Acceptance: 403 ProblemDetails when privilege missing; integration test against dev BFF with restricted user; no privilege bypass possible via FetchXML cross-entity link-entity.

**FR-BFF-08**: Placement Justification documented per `.claude/constraints/bff-extensions.md`. — Acceptance: PR description includes Placement Justification section answering the decision criteria; bff-extensions.md checklist all green.

#### Phase C — Matter UI Drill-through Consumers

**FR-CON-01**: `sprk_gridconfiguration` record for `sprk_kpiassessment` (Matter Health drill-through) authored via MCP. `sprk_configjson` includes: `source.type = "savedquery"`, savedQueryId pointing to the new KPI view; `filterChips.mode = "auto"`; `rowOpen.type = "navigateToForm"`; secondary action for "Ask sprkchat about this KPI". — Acceptance: record created; configjson validates against schema; default view auto-loads.

**FR-CON-02**: `sprk_gridconfiguration` record for `sprk_invoice` (Budget Performance drill-through). `sprk_configjson` includes: `source.type = "savedquery-set"` (auto-discover all `sprk_invoice` savedqueries); commands for Mark Paid + Export; secondary actions for sprkchat + Review playbook. Example in [design.md Appendix](design.md). — Acceptance: same as FR-CON-01.

**FR-CON-03**: New Custom Page `sprk_kpiassessmentspage.html` (~50 lines). React 18 SPA: mounts `<FluentProvider applyStylesToPortals>` wrapping `<DataGrid configId={...} parentContext={fromUrlParams}/>`. — Acceptance: deployed as Dataverse web resource; opens correctly in dialog mode + standalone mode; parses URL params `entityName / filterField / filterValue` for context filter.

**FR-CON-04**: New Custom Page `sprk_invoicespage.html` (~50 lines). Same shell pattern as FR-CON-03 with the `sprk_invoice` configId. — Acceptance: same as FR-CON-03.

**FR-CON-05**: VisualHost chart-def updates (via MCP):
- Matter Health (`a8b8df8b-f359-f111-a825-3833c5d9bcab`): `sprk_drillthroughtarget` → `sprk_kpiassessmentspage.html`
- Budget Performance (`7bf5b79e-f359-f111-a825-3833c5d9bcab`): `sprk_drillthroughtarget` → `sprk_invoicespage.html`

— Acceptance: VisualHost CardChrome expand button opens the new pages in dialog with correct context filter (current Matter id).

#### Phase D — EventsPage Migration

**FR-MIG-01**: `sprk_gridconfiguration` record for `sprk_event` with full `sprk_configjson` per [design.md Appendix](design.md): savedquery-set source, allowlist filter chips (`_ownerid_value, sprk_eventtype_ref, sprk_eventstatus, sprk_duedate`), full command bar, `rowOpen.type = "webResource"` (closes record-link bug), sprkchat secondary action. — Acceptance: record created; existing Event UX preserved post-migration (UAT all 4 modes: system / dialog / embedded / standalone).

**FR-MIG-02**: `EventsPage/App.tsx` rewritten as ~150-line thin host. Registers Event-specific handlers via `registerCommandHandler` (e.g., `BulkUpdateEventStatus` for Mark Complete / Cancel / Reassign / Archive that the existing `executeBulkStatusUpdate` supports), mounts Calendar pane in `Xrm.App.sidePanes`, passes `configId` to `<DataGrid />`. The 3 event-specific filter components retire; Calendar pane stays. — Acceptance: line count ≤ 200; no `IEventRecord` interface remains; Calendar pane mutual-exclusivity with Event detail pane preserved.

**FR-MIG-03**: Retire `@spaarke/events-components/components/{GridSection, AssignedToFilter, RecordTypeFilter, StatusFilter}/`. Keep `{CalendarSection, ViewSelectorDropdown}/`, `EventsPageContext`, `useViewSelection`. — Acceptance: directories deleted; no `import` references remain in repo; build passes.

**FR-MIG-04**: Fluent v9 portal fix + record-link bug closed. Validated by clicking a row from EventsPage opened as a VisualHost drill-through dialog — the record now opens via `Xrm.Navigation.navigateTo({pageType:"webresource"})` instead of failing silently behind the dialog. — Acceptance: UAT confirms record opens from both dialog mode (was broken) and standalone mode (was working).

**FR-MIG-05**: SpaarkeAi Calendar workspace widget migrated to consume new `<DataGrid />` instead of `GridSection`. Same `sprk_configjson` record as EventsPage (`FR-MIG-01`). — Acceptance: widget renders identically before + after migration; SpaarkeAi workspace integration test passes.

#### Phase E — SemanticSearch Migration

**FR-MIG-06**: Update SemanticSearch `sprk_gridconfiguration` record (`d99a4352-4913-f111-8343-7ced8d1dc988`) — migrate `sprk_configjson` from `{_type: "semantic-search-view", domain, columns, defaultSort}` to v1.0 schema with `source.type = "inline"` for the semantic-search view's custom fetchXml/layoutXml. — Acceptance: SemanticSearch code page renders correctly post-migration; no UX regression.

**FR-MIG-07**: `SearchResultsGrid.tsx` migrates to consume new `<DataGrid />` (or retire if all features covered by framework). `searchResultAdapter.ts` and `useSearchViewDefinitions.ts` may be lifted into the framework as the `IDataverseClient` projection layer. — Acceptance: SS code page passes existing UAT scenarios; visual diff vs. pre-migration: zero regression.

#### Phase F — Legacy Retire

**FR-MIG-08**: Remove `@spaarke/ui-components/components/DatasetGrid/{GridView, CardView, ListView, VirtualizedGridView, VirtualizedListView}.tsx`. Keep `ViewSelector.tsx` (generalized). — Acceptance: directories cleaned; barrel exports updated; consumers of removed exports listed and migrated.

**FR-MIG-09**: Retire `src/client/pcf/UniversalDatasetGrid/` PCF. Migrate `SpeAdminApp/src/components/dashboard/DashboardPage.tsx` to use new `<DataGrid />` directly. — Acceptance: SpeAdminApp DashboardPage renders identically; UDG PCF removed from `pcf/UniversalDatasetGrid/` and the solution; manifest version bump if deployed.

### Non-Functional Requirements

**NFR-01 (MDA Pixel Parity)**: Side-by-side screenshot diff vs. native MDA Events sub-grid passes for: light theme, dark theme, 75%/100%/125%/150% zoom levels. No pixel-level regression vs. current `EventsPage` grid. Per [design.md §11.5.2](design.md).

**NFR-02 (Token-only styling)**: Grep for `#[0-9a-fA-F]` regex in `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/` returns zero matches. Grep for `var(--` returns zero matches. All colors / spacing / radius via `tokens.*`. Per ADR-021.

**NFR-03 (Fluent v9 portal compliance)**: Every popover-bearing primitive (`ColumnHeaderMenu`, all 5 filter chips, `Dialog` confirmations, `Menu` overflow) verified to render correctly in dark theme. Either via `applyStylesToPortals={true}` on host FluentProvider OR inner FluentProvider wrap. Per [design.md §11.5.4](design.md).

**NFR-04 (Accessibility)**: axe-core scan passes (zero serious/critical violations) on every Storybook story. Keyboard navigation tested: Tab through chips → table → command bar; Enter/Space activate; Esc closes popovers; Arrow keys navigate table cells.

**NFR-05 (React-16 safety)**: Framework code uses NO React-18-only APIs (no `useId`, no `useSyncExternalStore`, no `createRoot`). Uses `JSXElement` from `@fluentui/react-components` for cross-version compat. Verified by build against `react: "16.14.0"` target. Per ADR-022 — enables future PCF host adoption in R2.

**NFR-06 (Zero impact on out-of-scope surfaces)**: `SemanticSearchControl` PCF version unchanged after R1 ships. `VisualHost` PCF code unchanged after R1 ships (chart-def data changes only). OOB sub-grids on entity forms unaffected.

**NFR-07 (Storybook coverage)**: Storybook story per Fluent v9 native feature demonstrating it works in the wrapper: selection (single, multi, select-all), sort, column resize, keyboard nav, density (3 sizes), sticky header. Plus stories for empty state, error state, loading state, lazy-load in progress.

**NFR-08 (BFF cache TTLs)**: Savedquery cache TTL = 1 hour. Metadata cache TTL = 6 hours. Both via `IDistributedCache` (Redis in prod, in-memory in dev). Per [design.md §9](design.md).

**NFR-09 (Lazy-load selection preservation)**: Selection state (`Set<string>`) preserved across lazy-load page boundaries. "Select all" semantics: selects all CURRENTLY-LOADED records (does not auto-fetch all pages). Verified by selecting 5 records on page 1, scrolling to page 3, confirming all 5 still checked.

**NFR-10 (Perf budgets)**: First page (100 records) p50 latency: <2 seconds on default tenant. Subsequent lazy-load pages: <1 second. Initial bundle size impact on `@spaarke/ui-components`: <120KB gzip added.

**NFR-11 (Custom Page React 18 + Fluent v9)**: New Custom Pages (`sprk_kpiassessmentspage.html`, `sprk_invoicespage.html`) MUST set `applyStylesToPortals={true}` on root `FluentProvider`. Per [design.md §11.5.4](design.md).

## Technical Constraints

### Applicable ADRs

- **ADR-008** — Endpoint authorization filter pattern (FR-BFF-07)
- **ADR-012** — Shared component library: this work is the canonical example. Framework lives in `@spaarke/ui-components`.
- **ADR-019** — ProblemDetails on BFF endpoint errors (FR-BFF-01..05)
- **ADR-021** — Fluent UI v9 + theming. NO `@fluentui/react` v8. NO raw hex. Dark mode required.
- **ADR-022** — React versions. Framework code is React-16-safe (NFR-05); Custom Page hosts use React 18.
- **ADR-028** — Spaarke Auth: `BffDataverseClient` uses `@spaarke/auth.authenticatedFetch` ONLY (FR-BFF-06).

### Binding Skills and Constraints

- **`.claude/skills/fluent-v9-component/SKILL.md`** — MANDATORY Step 0.5 invocation for every task that authors or modifies UI primitives, the DataGrid core, or any consumer/host. Applies to all of Phase A, parts of Phase C/D/E. Codified as the acceptance gate per [design.md §11.5.4](design.md).
- **`.claude/constraints/bff-extensions.md`** — Placement Justification required before any task that adds endpoints / services / DI / packages to `Sprk.Bff.Api`. Applies to all Phase B tasks (FR-BFF-01..08).
- **`src/client/pcf/CLAUDE.md`** §6.0 — Fluent v9 mandate.

### MUST Rules

- ✅ MUST build on Fluent v9 native `DataGrid` primitive — no hand-rolled `<table>` with custom sort/select/resize logic (NFR-07, FR-DG-11)
- ✅ MUST match MDA Power Apps grid UI exactly — codified in `tokens.ts` per [design.md §11.5.2](design.md) (NFR-01, FR-DG-10)
- ✅ MUST invoke `/fluent-v9-component` skill as Step 0.5 on every UI task (FR-DG-13, FR-MIG-04, NFR-03)
- ✅ MUST set `applyStylesToPortals={true}` on every FluentProvider hosting a popover-bearing primitive (NFR-03)
- ✅ MUST use `tokens.*` for all colors / spacing / radius — NO raw hex (NFR-02)
- ✅ MUST use `IDataverseClient` for all Dataverse access — NO direct `Xrm.WebApi.*` calls in framework code (FR-DG-02)
- ✅ MUST use `@spaarke/auth.authenticatedFetch` for BFF calls in `BffDataverseClient` — NO direct `fetch` with manual headers (FR-BFF-06, ADR-028)
- ✅ MUST use `Promise.all` for bulk operations with progress feedback (FR-DG-14, lift from `executeBulkStatusUpdate`)
- ❌ MUST NOT touch `SemanticSearchControl` PCF code (NFR-06)
- ❌ MUST NOT touch `VisualHost` PCF code — only chart-def `sprk_drillthroughtarget` data (NFR-06)
- ❌ MUST NOT use React-18-only APIs in framework code (`useId`, `useSyncExternalStore`, `createRoot`) per ADR-022 (NFR-05)
- ❌ MUST NOT use `@fluentui/react` v8 — Fluent v9 only (ADR-021)
- ❌ MUST NOT introduce per-user grid state persistence (R1 non-goal)
- ❌ MUST NOT use `teamsHighContrastTheme` — Windows HC is automatic in Fluent v9 (FR-DG-10)
- ❌ MUST NOT write `window.confirm` for confirmations — use Fluent v9 `<Dialog>` (FR-DG-14)

### Existing Patterns to Follow

- **`SearchResultsGrid.tsx`** ([src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx](src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx)) — canonical Fluent v9 native `DataGrid` usage + `dataType`-keyed cell renderer pattern. Closest existing implementation to target. Model the new DataGrid core on this structure.
- **`GridSection.tsx`** styles ([src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx](src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx)) lines 195-273 — MDA-parity styling already implemented. Lift these as `tokens.ts` entries.
- **`SemanticSearchControl/components/{CommandBar, BulkActionBar, FilterDropdown, DateRangeFilter, FilterPanel}.tsx`** — polished filter/command UX patterns. Inform framework primitive design (NOT copied directly — patterns only).
- **`@spaarke/auth.authenticatedFetch`** pattern in SSC `SemanticSearchControl.tsx` and `SpeDocumentViewer/BffClient.ts` — canonical BFF call pattern (ADR-028) for `BffDataverseClient` implementation.
- **EventsPage CRUD** in [src/solutions/EventsPage/src/App.tsx](src/solutions/EventsPage/src/App.tsx) lines 375-715 — the four CRUD operations (`openEventDetailPane`, `openNewEventForm`, `deleteSelectedEvents`, `executeBulkStatusUpdate`) lifted as framework defaults per FR-DG-14 + [design.md §11.5.3](design.md).
- **`FetchXmlService.parseLayoutXml`** in [src/client/shared/Spaarke.Events.Components/src/services/FetchXmlService.ts](src/client/shared/Spaarke.Events.Components/src/services/FetchXmlService.ts) — generalize and lift to `@spaarke/ui-components`. Replace hardcoded `sprk_eventid` ([line 205](src/client/shared/Spaarke.Events.Components/src/services/FetchXmlService.ts#L205)) with `<row id>` attribute extraction.
- **VisualHost `injectContextFilter`** v1.4.14 fix — lift to framework's FetchXML preprocessing pipeline (handles `<link-entity>` placement correctly).
- **Endpoint filter pattern** in `Sprk.Bff.Api/Api/Ai/SemanticSearchEndpoints.cs` (`SemanticSearchAuthorizationFilter`) — model FR-BFF-07 on this shape per ADR-008.

## Success Criteria

### Functional Gates

1. [ ] Matter Health + Budget Performance drill-through to working data grids — Verify: click VisualHost CardChrome expand on each card, dialog opens with filtered grid showing related records.
2. [ ] EventsPage migrated to framework with no UX regression — Verify: UAT all 4 modes (system / dialog / embedded / standalone); Calendar pane mutual exclusivity preserved.
3. [ ] EventsPage record-link-not-opening bug closed — Verify: open EventsPage as VisualHost drill-through dialog, click any row, record opens correctly via `Xrm.Navigation.navigateTo({pageType:"webresource"})`.
4. [ ] Single `<DataGrid configId={...} />` works in MDA Custom Page + SpaarkeAi workspace widget + standalone Storybook — Verify: smoke test each surface.
5. [ ] Adding a new entity grid requires zero new TypeScript — Verify: author one `sprk_gridconfiguration` record + one savedquery + one ~50-line Custom Page in CI test scenario; grid renders.
6. [ ] BFF passthrough endpoints all return correct shapes — Verify: integration tests against dev BFF for each of the 5 endpoints.
7. [ ] SemanticSearch code page migrated — Verify: UAT all existing SS scenarios; zero visual regression vs. pre-migration screenshot.

### Fluent v9 Native Exploitation Gates (FR-DG-11)

8. [ ] Selection uses `<DataGrid selectionMode>` — Verify: no custom checkbox column code in framework.
9. [ ] Sort uses `createTableColumn({ compare })` — Verify: no custom sort arrow rendering / no manual sort state in framework.
10. [ ] Column resize uses `resizableColumns` + `columnSizingOptions` — Verify: no custom drag handles.
11. [ ] Keyboard nav uses `focusMode` — Verify: no manual `onKeyDown` for arrow / tab navigation.
12. [ ] Density toggle wired to `<DataGrid size>` — Verify: 3 sizes render correctly; density change does not break selection state.

### MDA Visual Parity Gates (FR-DG-10, NFR-01)

13. [ ] Design tokens codified in `DataGrid/tokens.ts` — Verify: single source of truth, all primitives import from it.
14. [ ] Light-theme side-by-side screenshot diff vs. MDA Events sub-grid: PASS — Verify: visual review.
15. [ ] Dark-theme side-by-side screenshot diff: PASS — Verify: visual review.
16. [ ] Zoom levels 75% / 100% / 125% / 150% all render correctly — Verify: visual review.
17. [ ] Zero pixel regression vs. current `EventsPage` grid — Verify: visual diff before/after EventsPage migration.

### `/fluent-v9-component` Skill Compliance Gates (FR-DG-13, NFR-02, NFR-03, NFR-04)

18. [ ] `/fluent-v9-component` skill checklist green for every primitive — Verify: code review per primitive.
19. [ ] axe-core scan passes (zero serious/critical) on every Storybook story — Verify: CI axe job.
20. [ ] `applyStylesToPortals={true}` verified on every popover-bearing primitive — Verify: grep + dark mode story per primitive.
21. [ ] Grep for `#[0-9a-fA-F]` in `DataGrid/` returns zero — Verify: CI grep gate.
22. [ ] React-16-safe (no `useId`, no `useSyncExternalStore`, no `createRoot`) in framework code — Verify: build against React 16.14 target.

### BFF Gates (FR-BFF-01..08)

23. [ ] 4 BFF endpoints respond with correct shapes + status codes — Verify: integration tests.
24. [ ] ADR-008 endpoint filters reject unauthorized callers with 403 ProblemDetails — Verify: restricted-user integration test.
25. [ ] Cache hit rate >95% after warmup for savedquery + metadata endpoints — Verify: telemetry probe in dev.
26. [ ] Placement Justification documented in PR description per `bff-extensions.md` — Verify: PR description review.

### Perf Gates (NFR-10)

27. [ ] First-page p50 latency <2s on default tenant — Verify: Application Insights query post-deploy.
28. [ ] Lazy-load page p50 latency <1s — Verify: same as above.
29. [ ] Bundle size delta <120KB gzip on `@spaarke/ui-components` — Verify: webpack-bundle-analyzer before/after.

## Dependencies

### Prerequisites

- VisualHost v1.4.16 (current branch tip) — drill-through dialog mode works correctly; entitylist target detection in v1.4.13 stays intact.
- `@spaarke/auth` v2 (ADR-028) — `authenticatedFetch` for `BffDataverseClient`.
- `@spaarke/ui-components` Storybook setup (current; verified present).
- Fluent UI v9.46+ in `@spaarke/ui-components` — for `DataGrid` `subtleSelection`, `focusMode`, `resizableColumns` props.
- `IDistributedCache` Redis configured in `Sprk.Bff.Api` (existing) for FR-BFF-01/02/03 caching.

### External Dependencies

- Dataverse savedquery records authored for `sprk_kpiassessment` view (Phase C dependency)
- Dataverse savedquery records authored for `sprk_invoice` view (Phase C dependency)
- Power Apps Maker portal access for Custom Page registration (`sprk_kpiassessmentspage.html`, `sprk_invoicespage.html`)
- SpaarkeAi Calendar workspace widget owner sign-off on FR-MIG-05 migration

## Owner Clarifications

*Answers captured during design.md authoring (2026-06-01) and `/design-to-spec` interview:*

| # | Topic | Question | Answer | Impact |
|---|---|---|---|---|
| OC-01 | Drill-through grid scope | Use shared components to build grids for Matter Health + Budget Performance? | Yes — build a generic framework, both drill-throughs become thin shells consuming it | Triggered this entire project |
| OC-02 | Fluent v9 skill compliance gap | Review GridSection / EventsPage against `/fluent-v9-component`? | Yes — found `applyStylesToPortals` gap in EventsPage FluentProvider | NFR-03, FR-DG-09, FR-MIG-04 |
| OC-03 | Record link bug | EventsPage record link doesn't open from dialog mode | Confirmed bug; root cause: side pane opens behind dialog | FR-DG-14, FR-MIG-04 (closed via `rowOpen.type: "webResource"` default) |
| OC-04 | Existing reusable grid? | Did the Events page build a reusable grid? | Partial — data layer reusable, render layer Event-typed | §5 inventory; FR-MIG-03 (retire 4 files) |
| OC-05 | Cross-context wrapper | How to support non-MDA hosts? | `IDataverseClient` abstraction with Xrm + BFF impls | FR-DG-02, FR-BFF-06, FR-BFF-01..05 |
| OC-06 | View as injection mechanism | Can savedquery itself inject field requirements? | Yes — layoutXml `<row id>` and `<grid jump>` + entity metadata `AttributeType` give nearly everything | FR-DG-03, FR-DG-06, FR-DG-11 |
| OC-07 | Side pane / wizard / AI launcher pattern | Anticipate side pane, wizard, sprkchat, playbook launchers? | Yes — host extension model with three emit events + handler registry | FR-DG-05 |
| OC-08 | What are filter chips? | Define + identify entity equivalents | Quick-filter dropdowns above grid; auto-derive from visible columns with AttributeType ∈ {OptionSet, Status, State, Lookup, DateTime, Boolean} | FR-DG-06, FR-DG-07 |
| OC-09 | Toolbar standardization | Config-driven per entity, security optional | Yes — configjson controls; optional `privilege` field gates via PrivilegeService | FR-DG-08 |
| OC-10 | sprk_gridconfiguration role | Lean into it as the canonical config carrier | Yes — `sprk_configjson` holds everything; entity schema kept simple | FR-DG-03 |
| OC-11 | sprk_gridconfiguration actual fields | Only sprk_name, sprk_entitylogicalname, sprk_configjson, sprk_isdefault, sprk_sortorder (+ system) — schema doc was aspirational | No new entity fields needed | FR-DG-03 |
| OC-12 | UDG history | Originally PCF, components may be reusable | Services harvested (`CommandRegistry, PrivilegeService, EntityConfigurationService, ColumnRendererService, ConfigurationService, ViewService, FetchXmlService`); GridView/CardView/ListView retired | FR-MIG-08, FR-MIG-09 |
| OC-13 | Code Page target | Build as Code Page (not PCF) | Yes — Fluent v9 native `DataGrid`, React 18, no PCF constraint | FR-DG-11, NFR-05 (still React-16-safe for R2 PCF host) |
| OC-14 | Filter chip fields | Chips = fields in entity views | Auto-derived from layoutXml columns | FR-DG-06 |
| OC-15 | Dynamic view discovery | New savedquery auto-appears | Yes — savedquery-set source mode discovers via Dataverse query | FR-DG-03, FR-MIG-01 |
| OC-16 | Filter chip placement | Generic in `@spaarke/ui-components`; entity-specific extras (Calendar pane) with host | Confirmed — Calendar pane is a host-level side pane, not a chip | FR-DG-07, §11 non-impacts |
| OC-17 | Backward compat for SemanticSearch | Skip backward compat; migrate the one SS record | Migration in Phase E (FR-MIG-06) | Cuts schema versioning code |
| OC-18 | SemanticSearchControl PCF | Don't break; reuse patterns | OUT OF SCOPE for migration; patterns from CommandBar/BulkActionBar/FilterDropdown/FilterPanel/DateRangeFilter inform framework primitives | §11 non-impacts; FR-DG-07, FR-DG-08 |
| OC-19 | Persist chip selections per-user? (Q1) | No, session only | Out of R1 scope | NFR-09 |
| OC-20 | useDataGridContext() hook? (Q2) | Yes | Available to host extensions | FR-DG-15 |
| OC-21 | Bulk action — server vs client? (Q3) | Both — defaults client-side `Promise.all`, custom handlers can opt for single BFF call | Lift `executeBulkStatusUpdate` pattern | FR-DG-14 |
| OC-22 | Existing Event CRUD reuse? (Q4) | Yes — lift directly | 4 EventsPage CRUD ops become framework defaults | FR-DG-14 |
| OC-23 | Column-level user customization? (Q5) | No for R1 | R2 candidate | Out of scope |
| OC-24 | Density toggle? (Q6) | OK if easy — wire to Fluent v9 `<DataGrid size>` | Built-in, effectively free | FR-DG-16 |
| OC-25 | Multi-select filter chip semantics? (Q7) | OR within chip, AND across chips | Standard query-builder semantics | FR-DG-07 |
| OC-26 | Export to Excel — server or client CSV? (Q8) | Client-side CSV in R1; server-side Excel R2 if needed | Visible columns of loaded records, respecting active chips | FR-DG-17 |
| OC-27 | Fluent v9 native DataGrid features | Use built-ins; don't hand-roll | All capabilities sourced from `@fluentui/react-components` DataGrid | G9, FR-DG-11, §11.5.1 |
| OC-28 | MDA visual parity | Exact match — light + dark, multiple zoom levels | Design tokens codified | G10, NFR-01, §11.5.2 |
| OC-29 | `/fluent-v9-component` skill | Binding for every UI task | Step 0.5 invocation mandatory | G11, FR-DG-13, NFR-03, §11.5.4 |
| OC-30 | Virtualization strategy (interview Q-A) | Server-side paging with LAZY infinite-scroll loading (NOT pagination, NOT "Load more"), default page size 100, configurable via configjson | IntersectionObserver-driven; FetchXML page cookie chain | FR-DG-12 |
| OC-31 | Lookup chip option source (interview Q-B) | Async Combobox type-to-search via IDataverseClient | Debounced 300ms; works for any lookup target | FR-DG-13 |
| OC-32 | Phase B BFF in R1 or R2? (interview Q-C) | R1 — full portable framework | 4 BFF endpoints + caching + BffDataverseClient all in R1 | Phase B all FRs |
| OC-33 | CSV export scope (interview Q-D) | Visible columns of loaded records, respecting active chips | RFC 4180, UTF-8 BOM | FR-DG-17 |
| OC-34 | registerCommandHandler conflict policy (UQ-01) | Last-write-wins + `console.warn` | Hosts can intentionally override framework defaults; dev gets a warning to catch unintended overrides | FR-DG-05 |
| OC-35 | sprk_gridconfiguration lookup precedence (UQ-02) | Three-tier: (entity + savedQueryId) → (entity + sprk_isdefault) → metadata + layoutXml defaults | Per-view and per-entity records both supported; zero-config rendering when no record exists | FR-DG-03, FR-DG-04 |
| OC-36 | Storybook location (UQ-03) | `@spaarke/ui-components/storybook/` (existing setup) | No new tooling; stories live next to component code | NFR-07 |

## Assumptions

*Proceeding with these assumptions where owner did not explicitly answer:*

- **AS-01 — Storybook location**: Stories live in `@spaarke/ui-components/storybook/` (existing setup). Same package, same build pipeline. No new tooling.
- **AS-02 — Selection upper bound**: Selection state tracked as `Set<string>` with no hardcoded cap. UX warning if user selects >500 records ("Bulk operations on >500 records may take time").
- **AS-03 — Lazy-load page cookie expiry**: FetchXML paging cookies remain valid for 60 minutes per Dataverse default. Framework re-fetches from page 1 if user returns to a grid after 60+ minutes idle.
- **AS-04 — Bundle splitting**: New DataGrid + primitives ship as part of `@spaarke/ui-components` main bundle (no code-splitting in R1). NFR-10 budget assumes this.
- **AS-05 — Density toggle UX**: When `display.densityDefault` not specified, framework defaults to `"small"` (matches MDA OOB Events sub-grid density).
- **AS-06 — Configjson schema validation**: Runtime validation via TypeScript discriminated union types + minimal runtime guards (no Zod). Invalid configjson logs warning + falls back to defaults; does NOT throw.
- **AS-07 — VisualHost chart-def update**: Done via MCP `update_record` against the two `sprk_chartdefinition` records — no code change in VisualHost PCF.

## Unresolved Questions

*UQ-01/02/03 from `/design-to-spec` interview Q-D — RESOLVED 2026-06-01, captured as OC-34/35/36 above.*

*Additional discovery items deferred to project-pipeline Step 2 comprehensive resource discovery + Phase C/D task execution:*

- [ ] **UQ-04**: Concrete savedquery layouts for `sprk_kpiassessment` and `sprk_invoice` Matter-context views — column list + sort order. Owner to define during Phase C task execution.
- [ ] **UQ-05**: KPI Assessment + Invoice command bar customization in configjson — e.g., does `sprk_invoice` need a "Mark Paid" handler (mentioned in design.md appendix as example)? Confirm in Phase C.
- [ ] **UQ-06**: SpaarkeAi Calendar widget owner sign-off on FR-MIG-05 — needs coordination with that widget's maintainer. Confirm before Phase D scheduling.

---

*AI-optimized specification. Original design: `projects/spaarke-datagrid-framework-r1/design.md`*
*Generated by `/design-to-spec` skill on 2026-06-01.*
