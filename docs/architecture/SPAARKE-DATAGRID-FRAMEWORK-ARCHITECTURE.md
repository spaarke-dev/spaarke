# Spaarke DataGrid Framework — Architecture

> **Status**: R1 shipped (Phase A–C). Phase D (EventsPage migration) + Phase E (SemanticSearch migration) + Phase F (legacy retirement) in progress.
> **Established**: 2026-06 (project `spaarke-datagrid-framework-r1`)
> **Audience**: Developers consuming the framework, makers authoring configuration records.
> **Operational guide**: [`DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](../guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md)
> **Supersedes**: [`universal-dataset-grid-architecture.md`](universal-dataset-grid-architecture.md) (PCF — retires in Phase F)

---

## 1. What it is

A **single, configuration-driven Fluent v9 `<DataGrid>` React component** in `@spaarke/ui-components`, plus a `sprk_gridconfiguration` Dataverse entity that holds the per-grid configuration JSON, plus an `IDataverseClient` adapter that lets the same grid run in MDA, in a Custom Page, in a workspace widget, or in any future surface that can provide a Dataverse client.

The framework replaces — across Phase D / E / F — three legacy components:
- the `DatasetGrid` React component (pre-R1, in `Spaarke.UI.Components`)
- the EventsPage `GridSection` (hand-rolled HTML table host)
- the `UniversalDatasetGrid` PCF (web-resource grid; see [`universal-dataset-grid-architecture.md`](universal-dataset-grid-architecture.md))

One configuration → one entry point → one set of column-header, filter, command-bar, and empty-state behaviors → every surface.

---

## 2. Entry point

```tsx
import { DataGrid, XrmDataverseClient } from '@spaarke/ui-components';

<DataGrid
  configId="3019a06e-9b5e-f111-ab0c-7c1e521545d7"   // sprk_gridconfiguration record ID
  parentContext={{ matterId }}                       // optional — drives parent-context filter overlay
  dataverseClient={new XrmDataverseClient()}         // or BffDataverseClient (code-page / non-MDA)
  theme={resolvedTheme}                              // optional — for portal dark-mode propagation
  onBack={() => window.close()}                      // optional — back-arrow handler
/>
```

That is the entire host-side API. Everything else flows from the `sprk_gridconfiguration` record the `configId` resolves to.

---

## 3. Two architectural layers

### Layer A — The React component (in `@spaarke/ui-components`)

| File | Role |
|---|---|
| [`components/DataGrid/DataGrid.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx) | Composition root: loads config, resolves source FetchXML, renders header card (View selector + Command bar) + inner card (Fluent v9 `<DataGrid>` body + empty state + footer). |
| [`components/DataGrid/HeaderCellContent.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/HeaderCellContent.tsx) | OOB-style column header: label + sort indicator + active-filter glyph + chevron menu (A→Z / Z→A / Filter by / Clear filter / Column width). |
| [`components/DataGrid/ViewSelector.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/ViewSelector.tsx) | Back arrow + view-name dropdown (radio menu over available saved queries). |
| [`components/DataGrid/commandBar/CommandBar.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/commandBar/CommandBar.tsx) | Right-aligned inline command buttons + `⋯` overflow menu. |
| [`components/DataGrid/filterChips/`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/filterChips/) | Filter chip primitives, descriptor discovery from layoutXml + entity metadata, and runtime FetchXML augmentation. |
| [`components/DataGrid/fetchXmlOverlay.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts) | Injects the parent-context filter AND host-supplied filters into the base FetchXML at runtime. Exposes `overlayParentContextFilter` (declarative, configjson-driven) and `overlayHostFilters` (imperative, `<DataGrid hostFilters={…}/>` prop-driven — see [host filters](#host-filters-imperative-third-composition-layer) below). |
| [`components/DataGrid/configResolution.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts) | Resolves layoutXml + entity metadata + configjson `columns` overrides into the final column model. |
| [`components/DataGrid/useLazyLoad.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/useLazyLoad.ts) | IntersectionObserver-driven infinite scroll (Dataverse paging cookie). |
| [`components/DataGrid/tokens.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/tokens.ts) | Fluent v9 token aliases tuned for Power Apps OOB visual parity. |
| [`types/DataGridConfiguration.ts`](../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts) | v1.0 schema for `sprk_configjson`. Includes runtime guard `isValidDataGridConfiguration`. |

The shared library MUST stay **React-16-safe** per ADR-022 (no `useId`, no `useSyncExternalStore`, no `createRoot`) so it can be consumed by PCF controls in addition to React 18 / 19 SPAs. Custom Page hosts run React 18+; PCF hosts run React 16.14.

### Layer B — The Dataverse contract (`sprk_gridconfiguration`)

A custom entity. The configuration record holds:

| Attribute | Purpose |
|---|---|
| `sprk_name` | Friendly name (e.g. `KPI Assessment Matter Health`). |
| `sprk_entitylogicalname` | The child entity the grid lists (e.g. `sprk_kpiassessment`). |
| `sprk_configjson` | The JSON body — schema = [`DataGridConfiguration`](../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts), v1.0. |
| `sprk_isdefault` | Marks the default config for the entity (only one per entity). |
| `sprk_sortorder` | Tie-breaker when multiple configs exist for the same entity. |

The configjson schema is versioned via `_version: "1.0"`. The runtime guard rejects invalid shapes and falls back to framework defaults derived from entity metadata + layoutXml — **invalid configs do NOT throw**.

---

## 4. The `IDataverseClient` adapter

The framework never calls Xrm or fetch directly. All Dataverse access goes through one interface:

```typescript
// src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts
interface IDataverseClient {
  retrieveMultipleRecords(entityName, fetchXml, pagingInfo?): Promise<RetrieveResult>;
  retrieveEntityMetadata(entityName): Promise<EntityMetadata>;
  retrieveSavedQuery(savedQueryId): Promise<SavedQuery>;
  retrieveSavedQueriesForEntity(entityLogicalName): Promise<SavedQuery[]>;
  retrieveGridConfiguration(configId): Promise<DataGridConfiguration>;
}
```

Two implementations ship in R1:

| Implementation | Host | Mechanism |
|---|---|---|
| **`XrmDataverseClient`** | Model-Driven Apps, Custom Pages launched from MDA, anywhere `window.Xrm` exists | Wraps `Xrm.WebApi`, `Xrm.Utility`, and direct `EntityDefinitions` Web API for attribute display-name fetch. |
| **`BffDataverseClient`** | Workspace SPA, external-access surfaces, anywhere `window.Xrm` is unavailable | Calls Spaarke BFF passthrough endpoints (Phase B, tasks 010–013) using `@spaarke/auth.authenticatedFetch` per [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md). |

A future host that can produce a `IDataverseClient` (e.g. a Teams app shell) gets the same framework with zero changes to the component.

---

## 5. Host surface patterns

| Surface | Host shell | Notes |
|---|---|---|
| **Drill-through Custom Page** | `src/solutions/sprk_*page/src/main.tsx` (~50 lines per ADR-026) | Shell parses URL `data=` envelope from VisualHost, builds `parentContext`, mounts `<DataGrid>`. Two reference shells shipped R1: `sprk_kpiassessmentspage` + `sprk_invoicespage`. |
| **EventsPage** (Phase D, in progress) | Code Page rewrite (~150 lines) | Replaces the hand-rolled `<table>` host. Uses `behavior.parentContextFilter` with the Event → Matter lookup `_sprk_regardingmatter_value`. |
| **SemanticSearchControl** (Phase E, planned) | PCF — `SearchResultsGrid` refactor | Migrates `sprk_configjson` v0 → v1.0; framework code stays React-16-safe so the PCF can consume it. |
| **Workspace widget** (Phase F+) | `@spaarke/ai-widgets` shim | A widget owns the configId + Dataverse client; the widget shim mounts `<DataGrid>` inside the layout. |

A host shell is **never** allowed to embed business logic, fetch policy, or column overrides — every customization belongs in the configjson record. See [BUILD-A-NEW-WORKSPACE-WIDGET.md](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) for the parallel pattern.

---

## 6. Composition flow (per render)

```
configId
  │
  ▼
dataverseClient.retrieveGridConfiguration(configId)        ── Dataverse
  │
  ▼
DataGridConfiguration { _version: '1.0', source, … }
  │
  ├──▶ Resolve source FetchXML
  │     • savedquery → dataverseClient.retrieveSavedQuery(savedQueryId)
  │     • inline     → use fetchXml + layoutXml from config
  │     • savedquery-set → dataverseClient.retrieveSavedQueriesForEntity → pick default
  │
  ├──▶ Overlay parent-context filter (if behavior.parentContextFilter present)
  │     overlayParentContextFilter(fetchXml, parentContext, behavior.parentContextFilter)
  │     ─ injects <condition attribute="…" operator="eq" value="{guid}"/>
  │
  ├──▶ Overlay host filters (if <DataGrid hostFilters={…}/> prop is supplied)
  │     overlayHostFilters(fetchXml, hostFilters)
  │     ─ host (e.g. CalendarWorkspaceWidget) builds a HostFilterCondition[]
  │       from its own filter UI; each condition becomes a <condition> in the
  │       top-level <filter type='and'>. Supports eq/neq/in/between/null/etc.
  │
  ├──▶ Augment with chip filters (if user has applied any column filters)
  │     augmentFetchXmlWithChips(fetchXml, chipState, descriptors)
  │
  ▼
dataverseClient.retrieveMultipleRecords(entityName, fetchXml, paging)
  │
  ▼
GridItem[] → Fluent v9 <DataGrid> body
```

Console diagnostic: search for `[DataGrid] fetchXml composition` in DevTools — the framework logs the composed FetchXML at each render, including `hasParentFilterMatch`, `hostFilterCount`, and chip-state summary.

### Host filters (imperative, third composition layer)

`hostFilters` is the permanent imperative companion to `behavior.parentContextFilter`. It lets a host that owns its own filter UI translate that UI state into FetchXML at runtime, without writing it to a savedquery and without sharing schema with other consumers of the same configjson.

```tsx
<DataGrid
  configId="…"
  hostFilters={[
    { attribute: 'sprk_eventtype_ref', operator: 'eq', value: eventTypeId },
    { attribute: 'sprk_eventstatus', operator: 'in', value: [1, 2] },
    { attribute: 'sprk_duedate', operator: 'between', value: [from, to] },
  ]}
  onRecordsLoaded={records => deriveCalendarDots(records)}
/>
```

| Property | Type | Purpose |
|---|---|---|
| `hostFilters` | `ReadonlyArray<HostFilterCondition>` | Host-supplied FetchXML conditions appended to the top-level `<filter type='and'>`. |
| `HostFilterCondition.attribute` | `string` | FetchXML attribute logical name. |
| `HostFilterCondition.operator` | `HostFilterOperator` | Curated FetchXML operator subset (`eq`, `neq`, `in`, `not-in`, `gt`, `lt`, `ge`, `le`, `like`, `not-like`, `null`, `not-null`, `on`, `on-or-after`, `on-or-before`, `between`, `not-between`, `eq-userid`, `eq-userteams`). |
| `HostFilterCondition.value` | `scalar \| array \| undefined` | Scalar for single-value operators; array for `in` / `not-in` / `between` / `not-between`; ignored for valueless operators. |
| `onRecordsLoaded` | `(records) => void` | Fires after every records page resolves. Used by hosts to derive aggregate state (e.g. Calendar widget dot indicators). |

Behavioral guarantees:

- Empty/undefined `hostFilters` → composition is identical to no overlay (zero-cost).
- Invalid conditions (missing attribute, missing required value) are silently SKIPPED — the rest of the query still runs.
- Composition is memoized on `hostFilters` identity — pass a stable array (e.g. via `useMemo`) to avoid spurious re-fetches.
- Mixed declarative + imperative is supported: a configjson with `behavior.parentContextFilter` can coexist with a host-supplied `hostFilters` array. Order is `base → parentContext → hostFilters → chips`.

When to choose host filters vs. configjson chips:

| Choice | When |
|---|---|
| **Configjson `filterChips`** | The filter UI belongs to the grid itself (per-column chevron menus + the chip strip on top). User-driven, framework-rendered. |
| **`behavior.parentContextFilter`** | A single declarative parent scope (e.g. drill-through from Matter → KPIs). Stored in the configuration record, not the host. |
| **`hostFilters` prop** | The filter UI lives outside the grid (e.g. the Calendar widget's filter row + the calendar strip). Host-owned, prop-driven. |

---

## 6.5. Row-open contract — `defaultRecordOpen` + `onRecordOpen` override (added 2026-07-01 by R2 FR-18)

### Default behavior (`defaultRecordOpen`)

When no host handler is supplied, the framework's built-in `defaultRecordOpen` fires on every row-click. As of ai-spaarke-ai-workspace-UI-r2 (2026-07-01, FR-03 / FR-20), it always routes through:

```typescript
Xrm.Navigation.navigateTo(
  { pageType: "entityrecord", entityName, entityId, formId? },
  {
    target: 2,
    position: 1,
    width: { value: 85, unit: '%' },
    height: { value: 85, unit: '%' },
  }
);
```

This is **Layout 1** (per [`docs/standards/MODAL-DECISION-CRITERIA.md`](../standards/MODAL-DECISION-CRITERIA.md)) — a single 85% × 85% modal geometry for every entity, no per-record or per-entity variation. Prior variants (`rowOpen.type: "formDialog"` with `formDialogWidthPercent/HeightPercent` overrides; fallback `window.open('_blank')` to `main.aspx`) were retired in R2.

### `configjson.rowOpen.formId` (R2 FR-01 / FR-02)

Optional string field on `configjson.rowOpen`. When set to a valid `formid` GUID from the Dataverse `systemform` table, the framework forwards it as `pageInput.formId` on the `Xrm.Navigation.navigateTo` call — opening the specified form variant instead of the user's default main form. When absent, the default main form opens.

Example — configjson pointing at a specific "Workspace" form variant:

```json
{
  "_version": "1.0",
  "source": { "type": "savedquery", "savedQueryId": "..." },
  "rowOpen": {
    "type": "formDialog",
    "formId": "11111111-2222-3333-4444-555555555555"
  }
}
```

Backward compatibility: records without `formId` deserialize identically to today. As of 2026-07-01, no shipped `sprk_gridconfiguration` record sets `formId` — the field is available for future maker-authored "Workspace" form variants (out of R2 scope).

### `formDialogWidthPercent` / `formDialogHeightPercent` (deprecated in R2)

Retained in the schema for backward-compatible deserialization but **IGNORED** at runtime per FR-20 ("one size for every entity, do not vary per-entity"). Marked `@deprecated` in [`DataGridConfiguration.ts`](../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts). A future project may remove them entirely once no operator config references them.

### Host escape hatch — the `onRecordOpen` prop

Hosts that need custom row-click behavior (registered side panes, bespoke React dialogs, non-`sprk_todo`-style records) can pass an `onRecordOpen` prop to `<DataGrid>` — the framework's default is bypassed entirely and the host owns the open call.

```typescript
<DataGrid
  configId={configId}
  dataverseClient={dataverseClient}
  onRecordOpen={(recordId, record, ctx) => {
    // custom behavior — Layout 1 default is bypassed
  }}
/>
```

**Use sparingly**: the framework's default is the standard for a reason (Layout 1 unification, dark-mode compatibility, dirty-check-free simplicity). If a host needs a custom `onRecordOpen`, the reason SHOULD appear in the project's spec.md "ADR Tensions" section per [`CLAUDE.md` §6.5](../../CLAUDE.md#65-adr-conflict-resolution-protocol-binding--added-2026-06-29). Audit as of 2026-07-01: **no** production consumer under `src/solutions/**` passes an `onRecordOpen` override — every consumer inherits the framework default.

---

## 7. Applicable ADRs

| ADR | What it constrains |
|---|---|
| [ADR-006](../../.claude/adr/ADR-006-pcf-over-webresources.md) | New UI defaults to Custom Pages, NOT PCFs. The PCF predecessor retires in Phase F. |
| [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) | The 5 BFF passthrough endpoints (`/api/dataverse/*`) all use the standard endpoint-authorization filter pattern. |
| [ADR-012](../../.claude/adr/ADR-012-shared-components.md) | The framework lives in `@spaarke/ui-components`, NOT in any individual host. `IDataverseClient` is the canonical Dataverse contract for shared components. |
| [ADR-019](../../.claude/adr/ADR-019-problemdetails.md) | BFF passthrough errors emit ProblemDetails. |
| [ADR-021](../../.claude/adr/ADR-021-fluent-design-system.md) | Fluent v9 only. NO raw hex. Dark mode on every portal surface (`applyStylesToPortals={true}` re-wraps on Popover, Menu, Dialog, Combobox). |
| [ADR-022](../../.claude/adr/ADR-022-pcf-platform-libraries.md) | Framework code MUST be React-16-safe so PCF hosts can consume it. Custom Page hosts may use React 18 / 19. |
| [ADR-026](../../.claude/adr/ADR-026-full-page-custom-page-standard.md) | Full-page Custom Page = Vite + `vite-plugin-singlefile` + React 19. The Custom Page shell is presentational only. |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | `BffDataverseClient` uses `@spaarke/auth.authenticatedFetch` exclusively. |
| [ADR-029](../../.claude/adr/ADR-029-bff-publish-hygiene.md) | The 5 passthrough endpoints stay within the BFF publish-size baseline. |

---

## 8. Patterns + procedures referenced by this framework

| Pattern | Where |
|---|---|
| Parent-context filter overlay | [`projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md`](../../projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md) — promotes to `.claude/patterns/datagrid/` once stable |
| Fluent v9 component authoring | [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) |
| Fluent v9 portal dark-mode gotcha | [`.claude/patterns/ui/fluent-v9-portal-gotcha.md`](../../.claude/patterns/ui/fluent-v9-portal-gotcha.md) |
| Dataverse Web API client | [`.claude/patterns/dataverse/web-api-client.md`](../../.claude/patterns/dataverse/web-api-client.md) |
| Custom Page launch from VisualHost CardChrome | [`docs/architecture/VISUALHOST-ARCHITECTURE.md`](VISUALHOST-ARCHITECTURE.md) |

---

## 9. Non-goals (R1)

The framework deliberately does **not** ship these in R1. They are deferred or out of scope:

- **Per-user grid state persistence** (column widths, view choice, filter state across sessions). Explicit non-goal in spec.
- **Server-side aggregations** (sum / count rows). Use BFF endpoints or a savedquery rollup field.
- **Editable cells** / inline edit. Power Apps OOB does not have inline edit on read views; row-open `navigateToForm` is the supported edit path.
- **Custom command icons outside Fluent v9 icon registry**. `icon: 'Calendar20Regular'` is the supported shape; bring-your-own-SVG is not in scope.
- **Mixed-entity grids** (one grid showing rows from > 1 entity). Each grid is scoped to one `sprk_entitylogicalname`.

---

## 10. Pointers

| Topic | File |
|---|---|
| Configuration recipe + worked examples | [`DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`](../guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md) |
| Parent-context filter pattern (deep) | [`projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md`](../../projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md) |
| Project home + design + tasks | [`projects/spaarke-datagrid-framework-r1/`](../../projects/spaarke-datagrid-framework-r1/) |
| Legacy PCF being retired | [`universal-dataset-grid-architecture.md`](universal-dataset-grid-architecture.md) |
| Shared library overview | [`shared-ui-components-architecture.md`](shared-ui-components-architecture.md) |
| Custom Page standard | [`code-pages-architecture.md`](code-pages-architecture.md) |
| VisualHost (the parent CardChrome that opens drill-through Custom Pages) | [`VISUALHOST-ARCHITECTURE.md`](VISUALHOST-ARCHITECTURE.md) |
