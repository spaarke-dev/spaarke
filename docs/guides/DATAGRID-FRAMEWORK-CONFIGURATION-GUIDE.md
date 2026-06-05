# DataGrid Framework — Configuration Guide

> **Audience**: Power Apps makers authoring `sprk_gridconfiguration` records, and developers wiring a new grid into a Custom Page or workspace widget.
> **Architecture context**: [`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)
> **Schema reference**: [`DataGridConfiguration.ts`](../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts) (v1.0)

---

## Decision flow — am I in scope?

```
Need a grid of related records for a Spaarke entity?
  │
  ├── Standalone view (no parent record)?
  │      → Author one config record. Host in a workspace widget or stand-alone Custom Page.
  │
  ├── Drill-through from a parent record (Matter → KPI Assessments, etc.)?
  │      → Author one config record + set behavior.parentContextFilter.
  │        Host in a Custom Page launched via VisualHost CardChrome.
  │
  └── Replacing a legacy DatasetGrid / UDG PCF / EventsPage table?
         → Author a v1.0 config that matches the legacy behavior.
           See parent-context-pattern.md for filtered grids.
```

If none of the above fit (e.g. multi-entity rollups, editable cells), you are outside R1 scope — file a follow-up project.

---

## The five things you actually configure

A `sprk_gridconfiguration` record is small. The configjson body has nine top-level keys, and only **`_version` + `source`** are required. Everything else is an override.

| Key | Required | Purpose |
|---|---|---|
| `_version` | ✅ | Must be `"1.0"`. The runtime guard rejects anything else. |
| `source` | ✅ | Where the FetchXML comes from (savedquery / inline / savedquery-set). |
| `display` | optional | Title, density, custom empty-state message. |
| `filterChips` | optional | How column filter chips are derived. Default = `auto`. |
| `commandBar` | optional | Which default commands appear; add custom buttons. |
| `rowOpen` | optional | What happens on row click (default = navigate to form). |
| `secondaryActions` | optional | Per-row + bulk action buttons. |
| `columns` | optional | Per-column overrides keyed by logical name. |
| `behavior` | optional | Selection mode, page size, **parent-context filter** overlay. |

Lean configs are good configs. Authoring a 200-line configjson with every override set is an anti-pattern — the framework's defaults match Power Apps OOB.

---

## Step 1 — Create the configuration record

In the maker portal or via Dataverse MCP / Web API:

| Field | Value |
|---|---|
| **Name** (`sprk_name`) | Friendly label — e.g. `KPI Assessment Matter Health` |
| **Entity Logical Name** (`sprk_entitylogicalname`) | The CHILD entity the grid lists — e.g. `sprk_kpiassessment` |
| **Is Default** (`sprk_isdefault`) | `Yes` if this is the canonical config for the entity (one default per entity) |
| **Sort Order** (`sprk_sortorder`) | Integer tie-breaker. `100` is the convention. |
| **Config JSON** (`sprk_configjson`) | The body — see Step 2 |

The record GUID is what host shells reference (`<DataGrid configId="…" />`). Capture it after create.

---

## Step 2 — Author the minimum-viable configjson

The smallest valid config:

```json
{
  "_version": "1.0",
  "source": {
    "type": "savedquery",
    "savedQueryId": "a3f6d045-9a5e-f111-ab0c-7c1e521545d7"
  }
}
```

That's enough to render a working grid. The framework will:

- Resolve the savedquery (columns, base filter, sort)
- Derive column labels from entity metadata `DisplayName`
- Auto-discover filter chips for OptionSet / Status / State / Lookup / DateTime / Boolean columns
- Use Power Apps OOB defaults for density, paging (100), command bar (`+ New / Refresh / Export to Excel`), row open (`navigateToForm`)
- Show the framework's localized fallback empty-state message

Iterate from there — add only the overrides you need.

---

## Step 3 — Pick a `source`

Three shapes. Pick exactly one.

### `savedquery` — reference one specific view by GUID (most common)

```json
"source": {
  "type": "savedquery",
  "savedQueryId": "<savedquery record id>"
}
```

Use when you want a specific view (e.g. `Active KPI Assessments`, or a dedicated `KPI Assessment - Matter Context` view).

### `savedquery-set` — auto-pick the entity's default view

```json
"source": {
  "type": "savedquery-set",
  "entityLogicalName": "sprk_kpiassessment"
}
```

Use when you want whatever Dataverse considers the default view at render time. Removes config drift when an admin renames or replaces the default view.

### `inline` — embed FetchXML + layoutXml directly

```json
"source": {
  "type": "inline",
  "fetchXml": "<fetch …><entity name='…'><attribute …/></entity></fetch>",
  "layoutXml": "<grid name='…'><row name='result'>…</row></grid>"
}
```

Use when the config owns the query (no Dataverse savedquery record exists) — e.g. SemanticSearch results.

> ⚠ **Do NOT** embed `<condition value='@MatterId'/>` placeholders in inline FetchXML. Dataverse rejects placeholders at save time. Use `behavior.parentContextFilter` (Step 4) instead.

---

## Step 4 — If filtering by a parent record: `behavior.parentContextFilter`

For drill-through grids (Matter → child records), add a parent-context filter overlay:

```json
"behavior": {
  "parentContextFilter": {
    "attribute": "sprk_matter",
    "parentContextKey": "matterId",
    "operator": "eq"
  }
}
```

| Field | Value |
|---|---|
| `attribute` | The **child entity's lookup attribute name** (e.g. `sprk_matter`, `sprk_regardingmatter`). Inspect the entity metadata — NOT all child lookups are named `sprk_matter`. |
| `parentContextKey` | The key in the `parentContext` prop the host shell passes (typically `matterId`). |
| `operator` | `eq` for single parent (the common case). `in` is supported. |

The parent context flows from VisualHost → URL `data=` envelope → Custom Page shell → `<DataGrid parentContext={{ matterId }} />`. The framework injects the condition into the base FetchXML at runtime. See [`parent-context-pattern.md`](../../projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md) for the full architecture.

**Don't forget the data-side hook**: the VisualHost `sprk_chartdefinition` record's `sprk_contextfieldname` field must be set to the **lookup column reference** (e.g. `_sprk_matter_value`) — otherwise the URL envelope arrives without `filterValue` and the parent context is empty. The pattern doc has the per-entity table (KPI Assessment → `_sprk_matter_value`, Event → `_sprk_regardingmatter_value`).

---

## Step 4b — If the host owns its own filter UI: `hostFilters` prop

When the host (a workspace widget, a Code Page with custom filter chrome, etc.) renders its own filter row/calendar/etc. and needs to translate that state into FetchXML, pass conditions via the **`hostFilters`** prop. This is the imperative companion to `behavior.parentContextFilter` — declarative configjson stays clean; the host-specific logic stays in the host.

```tsx
<DataGrid
  configId="…"
  hostFilters={[
    { attribute: 'sprk_eventtype_ref', operator: 'eq',      value: applied.eventTypeId },
    { attribute: 'sprk_eventstatus',   operator: 'in',      value: applied.statusValues },
    { attribute: applied.dateField,    operator: 'between', value: [applied.from, applied.to] },
  ]}
  onRecordsLoaded={records => deriveCalendarDots(records)}
/>
```

| Field | Value |
|---|---|
| `attribute` | FetchXML attribute logical name on the entity. |
| `operator` | One of `eq`, `neq`, `in`, `not-in`, `gt`, `lt`, `ge`, `le`, `like`, `not-like`, `null`, `not-null`, `on`, `on-or-after`, `on-or-before`, `between`, `not-between`, `eq-userid`, `eq-userteams`. |
| `value` | Scalar for single-value operators; array for `in` / `not-in` / `between` / `not-between`; omitted for valueless operators (`null`, `not-null`, `eq-userid`, `eq-userteams`). |

Behavioral notes:

- Empty / undefined `hostFilters` is a no-op (no overlay applied).
- Invalid entries (missing attribute, missing required value) are silently skipped — the rest of the query still runs.
- Pass a **memoized** array (`useMemo`) when the array contents change frequently — the framework re-runs the FetchXML composition pipeline when the prop identity changes.
- Composition order: `base → parentContextFilter → hostFilters → chips`. Mixing all three is supported.

`onRecordsLoaded` is the matched callback: fires every time a records page resolves, with the full accumulated array. Use it to derive aggregate UI state (the canonical example is the Calendar widget's per-date event counts). Mirrors the legacy `GridSection.onRecordsLoaded` contract.

When should I reach for this? See the [decision table in the architecture doc](../architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md#host-filters-imperative-third-composition-layer).

---

## Step 5 — Customize what makers most often want

### Empty state message

```json
"display": { "emptyStateMessage": "No invoices for this matter." }
```

Shown both when the underlying view returns zero rows AND when a filter narrows results to zero. The column header row + filter chevrons stay visible so the user can clear the filter.

### Density

```json
"display": { "densityDefault": "compact" }
```

`compact` matches OOB Matter forms; `comfortable` is for read-heavy contexts.

### Command bar

```json
"commandBar": {
  "showDefaultCommands": {
    "newRecord": true,
    "refresh": true,
    "exportExcel": true,
    "delete": false,
    "editColumns": false,
    "editFilters": false
  }
}
```

To add a CUSTOM command, register a handler from the host shell (see Step 6) and reference it:

```json
"commandBar": {
  "primary": [
    {
      "id": "mark-paid",
      "label": "Mark paid",
      "icon": "Money20Regular",
      "action": "custom",
      "customHandlerId": "mark-invoice-paid",
      "requiresSelection": "multi",
      "privilege": "Write",
      "appearance": "primary"
    }
  ]
}
```

### Row open (what happens on row click)

```json
"rowOpen": { "type": "navigateToForm" }
```

For the EventsPage record-link-not-opening bug fix, use:

```json
"rowOpen": {
  "type": "webResource",
  "webResource": "sprk_eventeditpage.html",
  "dataParams": ["sprk_eventid", "matterId"]
}
```

The `webResource` type uses `Xrm.Navigation.navigateTo({pageType:'webresource', …})` so the opened surface clears the dialog correctly.

### Per-column overrides

```json
"columns": {
  "sprk_totalamount": { "renderer": "currency", "align": "right" },
  "sprk_completionrate": { "renderer": "percentage", "align": "right" },
  "createdon": { "renderer": "date", "width": 120 }
}
```

Renderer kinds: `default | currency | percentage | badge | link | date | datetime | avatar | icon | <custom>`. Custom renderers require host registration (see Step 6).

### Filter chip discovery

Default = `auto` (every chip-eligible column gets a chip). To restrict or override:

```json
"filterChips": {
  "mode": "denylist",
  "denylist": ["createdby", "modifiedon"]
}
```

`allowlist` is the inverse; `explicit` lets you author the full list with per-chip overrides (custom label, value source override).

---

## Step 6 — Wire the host shell

### Drill-through Custom Page

Copy one of the reference shells verbatim and change the `CONFIG_ID`:

- [`src/solutions/sprk_kpiassessmentspage/src/main.tsx`](../../src/solutions/sprk_kpiassessmentspage/src/main.tsx)
- [`src/solutions/sprk_invoicespage/src/main.tsx`](../../src/solutions/sprk_invoicespage/src/main.tsx)

The shell is ~50 lines and does three things: parse the URL `data=` envelope for `matterId`, build `parentContext`, mount `<DataGrid configId=… parentContext=… dataverseClient={new XrmDataverseClient()} />`.

Then update the VisualHost `sprk_chartdefinition` record:

- `sprk_drillthroughtarget` = web-resource name (e.g. `sprk_kpiassessmentspage.html`)
- `sprk_contextfieldname` = lookup column reference (`_sprk_matter_value` or `_sprk_regardingmatter_value`)

### Workspace widget

Wrap `<DataGrid>` in a widget shim that owns the configId + Dataverse client. See [`BUILD-A-NEW-WORKSPACE-WIDGET.md`](BUILD-A-NEW-WORKSPACE-WIDGET.md) for the canonical pattern (Pattern D — shared-lib widget + thin LW shim).

### Registering custom command / row-open / secondary-action handlers

From the host shell (BEFORE mounting `<DataGrid>`):

```typescript
import { registerCommandHandler, registerWizard } from '@spaarke/ui-components';

registerCommandHandler('mark-invoice-paid', async ({ selectedRecords, refresh }) => {
  // Your handler logic
  await refresh();
});

registerWizard('upload-invoice', UploadInvoiceWizard);
```

Handlers are referenced from configjson by ID. They are looked up at click time, so a missing handler degrades gracefully (button disabled with a tooltip).

---

## Step 7 — Deploy

| Artifact | How |
|---|---|
| Configuration record (`sprk_gridconfiguration`) | Export from solution / Dataverse MCP / maker portal. Lives in `spaarke_core` solution. |
| Updated VisualHost `sprk_chartdefinition` (drill-through only) | Same path. |
| Custom Page shell bundle (`sprk_<name>page.html`) | `npm run build` in `src/solutions/sprk_<name>page` → upload web resource via Spaarke deploy script. |
| Framework code | Ships with whatever solution carries `@spaarke/ui-components` — no per-grid build step. |

Reference deploy script: `%TEMP%/dv-deploy-r1/Deploy-DatagridFrameworkCodePages.ps1` (R1; the canonical script will move into `scripts/` once stabilized).

---

## Worked example — Matter → KPI Assessments

The production R1 configuration record (`3019a06e-9b5e-f111-ab0c-7c1e521545d7`):

```json
{
  "_version": "1.0",
  "source": {
    "type": "savedquery",
    "savedQueryId": "a3f6d045-9a5e-f111-ab0c-7c1e521545d7"
  },
  "display": {
    "title": "KPI Assessments",
    "densityDefault": "compact",
    "emptyStateMessage": "No KPI assessments for this matter."
  },
  "filterChips": { "mode": "auto" },
  "commandBar": {
    "showDefaultCommands": {
      "newRecord": true,
      "refresh": true,
      "exportExcel": true,
      "delete": false,
      "editColumns": false,
      "editFilters": false
    }
  },
  "rowOpen": { "type": "navigateToForm" },
  "secondaryActions": [
    {
      "id": "ask-sprkchat-kpi",
      "label": "Ask Sprkchat",
      "icon": "Chat20Regular",
      "kind": "ai-assistant",
      "requiresSelection": "single",
      "aiAssistantId": "default"
    }
  ],
  "behavior": {
    "selectionMode": "multi",
    "pageSize": 100,
    "enableSorting": true,
    "enableColumnResize": true,
    "enableKeyboardNavigation": true,
    "parentContextFilter": {
      "attribute": "sprk_matter",
      "parentContextKey": "matterId",
      "operator": "eq"
    }
  }
}
```

Things to notice:

- Lean. No `columns` overrides — Power Apps DisplayName + entity metadata are sufficient.
- `filterChips: { mode: "auto" }` — chips appear automatically per eligible column.
- `delete: false` — intentional. KPI Assessments are immutable once authored.
- `parentContextFilter.attribute = "sprk_matter"` — matches the lookup on the child entity. (For Event drill-through, this would be `sprk_regardingmatter`.)
- `secondaryActions[]` adds an `Ask Sprkchat` button on each row when one row is selected.

---

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| Grid renders unfiltered (all records, not just the parent's) | DevTools Console → `[DataGrid] fetchXml composition`. Is `parentContext.matterId` empty? → `sprk_chartdefinition.sprk_contextfieldname` is not set. Is `hasParentFilterMatch: false`? → `behavior.parentContextFilter.attribute` doesn't match the lookup attribute on the child entity. |
| "Failed to fetch" error | Network tab → request payload. Most R1 cause was Dataverse rejecting `top` + `page` together — fixed in `useLazyLoad.ts`. If new, check FetchXML validity in XrmToolBox. |
| Column labels show technical names (`sprk_completionrate` instead of `Completion Rate`) | Entity metadata didn't load — `XrmDataverseClient.retrieveEntityMetadata` returning 0 attributes. Confirmed working in Spaarke env via `Xrm.WebApi.retrieveMultipleRecords('EntityDefinition', …)` fallback. |
| Column header chevron menu missing | Column is not chip-eligible (e.g. text without metadata). Framework falls back to text-chip for every column when metadata is thin; verify `chipDescriptors` in DevTools React inspector. |
| Filter applied → 0 rows → grid disappears | Should be FIXED in R1 (`DataGrid.tsx` Phase C UAT). Header row always renders so the chevron is reachable. If reproducible, file as a regression. |
| Column header drop-shadow not visible | Round-23 inline `filter: drop-shadow` on `<MenuPopover>` is the brute-force fix. If clipped again, check whether the host wrapper has new `overflow: hidden`. |
| Custom command not firing | Did the host shell call `registerCommandHandler('<id>', …)` BEFORE mounting `<DataGrid>`? Lookups happen at click time, so registration order matters. |
| Dark mode not propagating into popover | Every Popover / Menu / Dialog / Combobox surface in the framework re-wraps with `<FluentProvider applyStylesToPortals={true} theme={theme}>`. The host MUST pass `theme={resolvedTheme}` to `<DataGrid>` for portal surfaces to resolve. |
| Config edits in Dataverse don't appear | Web-resource cache. Hard refresh the dialog (Ctrl+F5). Custom Pages cache aggressively in MDA. |

---

## Pointers

- Architecture overview → [`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)
- Parent-context filter (deep dive) → [`projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md`](../../projects/spaarke-datagrid-framework-r1/notes/parent-context-pattern.md)
- Schema source of truth → [`DataGridConfiguration.ts`](../../src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts)
- Custom Page standard → [`code-pages-architecture.md`](../architecture/code-pages-architecture.md), [ADR-026](../../.claude/adr/ADR-026-full-page-custom-page-standard.md)
- Workspace widget pattern → [`BUILD-A-NEW-WORKSPACE-WIDGET.md`](BUILD-A-NEW-WORKSPACE-WIDGET.md)
- VisualHost CardChrome (launches drill-through) → [`VISUALHOST-ARCHITECTURE.md`](../architecture/VISUALHOST-ARCHITECTURE.md)
