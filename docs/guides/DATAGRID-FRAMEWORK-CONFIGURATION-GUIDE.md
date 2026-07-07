# DataGrid Framework — Configuration Guide

> **Last Updated**: July 7, 2026
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

### Starter templates (recommended)

Three copy-paste templates live under [`scripts/config-templates/`](../../scripts/config-templates/). Pick the one closest to your shape, replace the `<placeholder>` markers (angle-bracketed values like `<savedquery-id>`, `<entity-name>`, `<parent-context-key>`), then paste into the `sprk_configjson` field on your new record.

| Template | When to use |
|---|---|
| [`entity-list-basic.json`](../../scripts/config-templates/entity-list-basic.json) | Standalone grid of a single entity's records. Minimum viable — `_version`, `source`, `display.title`, `rowOpen`, `behavior.pageSize`. Best starting point for most new configs. |
| [`entity-list-drill-through.json`](../../scripts/config-templates/entity-list-drill-through.json) | Grid scoped to a parent record (e.g. Matter → KPI Assessments). Adds `behavior.parentContextFilter`, column overrides, and a `secondaryActions[]` entry. |
| [`entity-list-full.json`](../../scripts/config-templates/entity-list-full.json) | Reference only — every top-level key populated with `$comment` annotations. Use as a lookup, **not** as a starting point (lean configs are good configs).|

The templates use `$comment` keys for inline documentation. The framework tolerates unknown keys at parse time, so `$comment` is safe to leave in the record — remove it if you prefer stricter payloads.

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

## Step 3.5 — Restrict which views appear in the view-switcher (`availableViews`)

By default the grid header's **view-switcher dropdown lists every active saved query for the entity** (e.g. `sprk_event` shows "All Events", "All Tasks", "All Deadlines", "Inactive Events", "My Events Open", …). To restrict the picker to a curated subset, add an **`availableViews`** allowlist of saved-query GUIDs to the `source`:

```json
"source": {
  "type": "savedquery",
  "savedQueryId": "<All Tasks view GUID>",   // the view shown first (the default)
  "availableViews": [
    "<All Tasks view GUID>",
    "<All Deadlines view GUID>"
  ]
}
```

Now the switcher offers only "All Tasks" and "All Deadlines". Behavior:

- **Empty array or omitted** = no restriction (all sibling views show) — the safe default, avoids an empty picker.
- GUIDs are matched case-insensitively and tolerate `{}` braces.
- The `savedQueryId` you set is the **default view** the grid opens on; include it in `availableViews` (or it won't be switch-back-able).

> 🚨 **Gotcha — the allowlist is only honored for `source.type: "savedquery"`.** If your config uses
> `savedquery-set` (Step 3), `availableViews` is **silently ignored** and the picker shows every view.
> This is the #1 "why are all views showing?" cause. To restrict, switch the source to `savedquery`
> with an explicit `savedQueryId` + `availableViews` (as above). *(A per-placement `availableViewsAllowlist`
> prop on `<DataGrid>` also exists and works with any source type, but it requires host-shell code — the
> config-level allowlist is the maker-friendly path.)*

**Drill-through tip:** for a drill-through page whose only job is "show related records," a curated
2–4 view allowlist (or a single-view config with no siblings) keeps the dialog focused. Point the chart
definition's `sprk_baseviewid` at the same default view if you want VisualHost and the grid to agree.

### Operator-friendly alternative: set the allowlist on the VisualHost chart definition

For **drill-through dialogs specifically**, the allowlist can be set on the `sprk_chartdefinition` record
(no grid-config edit, no code) via the **`sprk_drillthroughviews`** field — a delimited (`;` or `,`) list
of saved-query GUIDs. VisualHost forwards it as an `availableViews` envelope param; `DataGridPageShell`
parses it and passes it to `<DataGrid availableViewsAllowlist>`. Precedence: it maps to the framework's
**instance-level** allowlist, so it **works with any source type (including `savedquery-set`)** and
overrides the config-level `source.availableViews` when both are set. This is the right granularity for
"restrict only the drill-through picker" — the shared grid config (used by workspace widgets too) stays
untouched. See [VISUALHOST-SETUP-GUIDE.md](VISUALHOST-SETUP-GUIDE.md#setting-up-drill-through-into-a-datagrid-framework-page-events-invoices-kpi-assessments).

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

**Post-R2 (2026-07-01)**: the framework's `defaultRecordOpen` always opens Layout 1 (`Xrm.Navigation.navigateTo` at **85% × 85%** centered, target 2, position 1) regardless of `rowOpen.type`. This is the R2 FR-20 binding — "one modal size for every entity, do not vary per-entity". See [`docs/standards/MODAL-DECISION-CRITERIA.md`](../standards/MODAL-DECISION-CRITERIA.md) for the two-layout standard.

Because the framework unifies the behavior, `rowOpen` is only needed when you want to:
- Explicitly document intent (recommended)
- Open a **specific form variant** via `formId`
- Use a **non-default** open path (sidePane / webResource / custom)

Minimum recommended shape for entity-list widgets:

```json
"rowOpen": { "type": "formDialog" }
```

To open a specific form variant (e.g. a "Workspace" simplified form authored by a maker), add `formId`:

```json
"rowOpen": {
  "type": "formDialog",
  "formId": "11111111-2222-3333-4444-555555555555"
}
```

When absent, the framework opens the user's default main form for the entity.

**Retired R2** (still deserializes for backward compat but IGNORED at runtime):

- `formDialogWidthPercent` / `formDialogHeightPercent` — R2 FR-20 unified to 85%×85% for every Layout 1 open.
- Legacy `window.open('_blank')` fallback (was: `type` != `formDialog` → new tab) — removed; every row-click now routes through `Xrm.Navigation.navigateTo`.

**Non-default alternates** (require host-side registration — see Step 6):

```json
"rowOpen": {
  "type": "webResource",
  "webResource": "sprk_eventeditpage.html",
  "dataParams": ["sprk_eventid", "matterId"]
}
```

The `webResource` type uses `Xrm.Navigation.navigateTo({pageType:'webresource', …})` — the bug-fix path for the EventsPage record-link-not-opening issue.

### Secondary actions (per-row + bulk)

Add AI/Playbook/wizard/navigate/custom actions that appear on row hover or in bulk-select mode:

```json
"secondaryActions": [
  {
    "id": "ask-sprkchat-invoice",
    "label": "Ask Sprkchat",
    "icon": "Chat20Regular",
    "kind": "ai-assistant",
    "requiresSelection": "single",
    "aiAssistantId": "default",
    "visible": "row-hover"
  },
  {
    "id": "review-playbook",
    "label": "Review",
    "icon": "DocumentSearch20Regular",
    "kind": "playbook",
    "requiresSelection": "single",
    "privilege": "Read",
    "playbookId": "invoice-review-default"
  }
]
```

Kinds: `ai-assistant` (launches SprkChat with the row context), `playbook` (fires a Spaarke playbook), `wizard` (opens a registered wizard component), `navigate` (opens a related record by lookup field), `custom` (calls a host-registered handler).

Visibility modes: `always` (permanent button), `row-hover` (appears on hover — default for row actions), `bulk-only` (only when 2+ rows selected).

### Behavior — pagination + selection + parent-context filter

```json
"behavior": {
  "selectionMode": "multi",
  "pageSize": 25,
  "enableSorting": true,
  "enableColumnResize": true,
  "enableKeyboardNavigation": true
}
```

**Field-by-field**:

| Field | Type | Default | Purpose |
|---|---|---|---|
| `selectionMode` | `'none' \| 'single' \| 'multi'` | `'multi'` | Row selection model. `'none'` hides the checkbox column entirely. |
| `pageSize` | number | **`25`** (FR-07, spaarke-dataset-grid-framework-r2) | **Records per FetchXML page — controls lazy-load chunk size**. Lower = more scrolling; higher = fewer round trips. `25` matches the workspace-widget majority use case; drill-through / full-page grids should override explicitly to `50–100`. |
| `enableSorting` | boolean | `true` | Column-header click sorts. Set `false` to lock the savedquery's sort order. |
| `enableColumnResize` | boolean | `true` | Drag column edges to resize. |
| `enableKeyboardNavigation` | boolean | `true` | Arrow keys move the row focus; Enter opens the row. |

**Parent-context filter** — see Step 4 above. When set, the framework injects a `<condition>` into the savedquery's FetchXML at render time:

```json
"behavior": {
  "parentContextFilter": {
    "attribute": "sprk_matter",
    "parentContextKey": "matterId",
    "operator": "eq"
  }
}
```

**pageSize tuning tips**:
- If the widget lives inside a workspace section with clamped height (~480px), pick `pageSize` so the first page fills the visible area with a small overflow (e.g. `25` for standard row density; `40` for compact density with narrow rows).
- If the widget is a full-page grid (drill-through code page like `sprk_invoicespage`), use `50–100` — fewer network trips outweigh scroll depth concerns.
- The framework always uses `useLazyLoad` — subsequent pages fetch via IntersectionObserver on a sentinel `<div>` at the bottom of the grid body. You never need to opt in to lazy loading; you only tune the chunk size.

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

## Step 5.5 — Full annotated template (copy-paste starter)

Every override in one place, with defaults and comments. Copy this as your starting point, then **delete every key you're not overriding** so your config record stays minimal and picks up framework default changes going forward.

**Why not populate every field on every record?** Records that explicitly set defaults DIVERGE from the framework when defaults evolve (e.g., FR-07 changed the default `pageSize` from `100` to `25`; records with an explicit `"pageSize": 100` are "stuck" on the old value until the record is re-authored). Keeping the record minimal preserves the framework's ability to change defaults centrally.

```jsonc
{
  "_version": "1.0",                       // REQUIRED. Must be "1.0" — runtime guard rejects anything else.

  // ─── SOURCE (REQUIRED — pick ONE variant) ─────────────────────────────────
  "source": {
    "type": "savedquery",                  // "savedquery" | "savedquery-set" | "inline"
    "savedQueryId": "<guid>"               // for type="savedquery" — a specific savedquery
    // "entityLogicalName": "sprk_...",    // for type="savedquery-set" — auto-discover all active savedqueries
    // "fetchXml": "<fetch>...</fetch>",   // for type="inline" — provide fetchXml + layoutXml directly
    // "layoutXml": "<grid>...</grid>"
  },

  // ─── DISPLAY (optional) ────────────────────────────────────────────────────
  "display": {
    "title": "Custom Header Title",        // Override savedquery name in header. Default: savedquery name.
    "icon": "Calendar20Regular",           // Fluent v9 icon in header. Default: no icon.
    "densityDefault": "comfortable",       // "comfortable" | "compact". Default: "comfortable".
    "emptyStateMessage": "No records."     // Custom "no results" message. Default: framework localized fallback.
  },

  // ─── FILTER CHIPS (optional — default is auto-derive) ──────────────────────
  "filterChips": {
    "mode": "auto",                        // "auto" | "allowlist" | "denylist" | "explicit". Default: "auto".
    "allowlist": ["sprk_status"],          // Attribute logical names — only these become chips (mode="allowlist").
    "denylist":  ["createdby"],            // Attribute logical names — these are EXCLUDED (mode="denylist").
    "explicit": [                          // Full manual authoring (mode="explicit").
      {
        "field": "sprk_regarding",
        "kind": "lookup-multi",            // "optionset-multi" | "lookup-multi" | "date-range" | "text" | "bool"
        "label": "Regarding",              // Optional label override
        "valueSource": { "type": "systemusers" },  // Optional value source override
        "valueColors": { "100000000": "filled" }   // Optional per-option badge appearance
      }
    ],
    "showClearAll": true                   // Show "Clear all" chip. Default: true.
  },

  // ─── COMMAND BAR (optional) ────────────────────────────────────────────────
  "commandBar": {
    "showDefaultCommands": {               // Toggle framework defaults. Omitted = framework default (typically true).
      "newRecord":    true,
      "refresh":      true,
      "exportExcel":  true,
      "delete":       false,
      "editColumns":  false,
      "editFilters":  false
    },
    "primary": [                           // Left-aligned custom buttons (always visible).
      {
        "id": "mark-paid",
        "label": "Mark paid",
        "icon": "Money20Regular",
        "action": "custom",                // "create-form" | "delete-selected" | "refresh" | "export-excel" | "edit-columns" | "edit-filters" | "custom"
        "customHandlerId": "mark-invoice-paid",  // Required when action="custom"
        "requiresSelection": "multi",      // "single" | "multi" | false. Default: false.
        "privilege": "Write",              // "Read" | "Write" | "Create" | "Delete". Optional security gate.
        "appearance": "primary",           // "subtle" | "primary" | "secondary". Default: "subtle".
        "divider": false                   // Render a vertical divider BEFORE this item. Default: false.
      }
    ],
    "secondary": []                        // Right-aligned / overflow-menu buttons. Same shape as primary.
  },

  // ─── ROW OPEN (optional — R2 default is Layout 1 at 85%×85%) ──────────────
  "rowOpen": {
    "type": "formDialog",                  // Documented value. Framework unifies to Layout 1 regardless (R2 FR-20).
    "formId": "<form-guid>",               // R2 FR-01: open a specific form variant. Optional.
    // For type="webResource":
    // "webResource": "sprk_edit.html",
    // "dataParams": ["fieldName", "matterId"],
    // For type="sidePane":
    // "paneId": "my-pane", "paneTitle": "Details", "webResourceName": "sprk_pane.html", "width": 480,
    // For type="wizard": "wizardName": "MyWizard",
    // For type="dialog": "dialogComponent": "MyDialog",
    // For type="custom": "customHandlerId": "my-handler",
    "passContext": ["matterId"]            // Keys from parentContext to forward to the opened surface. Optional.
    // DEPRECATED (retained for backward-compat; ignored at runtime per R2 FR-20):
    // "formDialogWidthPercent": 80,
    // "formDialogHeightPercent": 80
  },

  // ─── SECONDARY ACTIONS (optional — per-row + bulk) ────────────────────────
  "secondaryActions": [
    {
      "id": "ask-sprkchat",
      "label": "Ask Sprkchat",
      "icon": "Chat20Regular",
      "kind": "ai-assistant",              // "ai-assistant" | "playbook" | "wizard" | "navigate" | "custom"
      "requiresSelection": "single",       // "single" | "multi" | false
      "privilege": "Read",                 // Optional security gate
      "visible": "row-hover",              // "always" | "row-hover" | "bulk-only". Default: "row-hover".
      "aiAssistantId": "default"           // Kind-specific config field
      // "playbookId":       "invoice-review-default",     // for kind="playbook"
      // "wizardName":       "InvoiceReviewWizard",         // for kind="wizard"
      // "navigateTarget":   { "entity": "sprk_matter", "idField": "sprk_regardingmatter" },  // for kind="navigate"
      // "customHandlerId":  "my-handler"                   // for kind="custom"
    }
  ],

  // ─── COLUMNS (optional — per-column overrides keyed by logical name) ──────
  "columns": {
    "sprk_totalamount":    { "renderer": "currency",   "align": "right", "width": 120 },
    "sprk_completionrate": { "renderer": "percentage", "align": "right" },
    "createdon":           { "renderer": "date",       "width": 120 },
    "modifiedby":          { "hidden": true },
    "sprk_status":         { "renderer": "badge", "label": "Status", "tooltip": "Record lifecycle status" }
    // Renderers: "default" | "currency" | "percentage" | "badge" | "link" | "date" | "datetime" | "avatar" | "icon" | "<custom-renderer-id>"
    // Overridable fields per column: label, width, renderer, align ("left"|"center"|"right"), tooltip, hidden
  },

  // ─── BEHAVIOR (optional — interaction knobs) ──────────────────────────────
  "behavior": {
    "selectionMode": "multi",              // "none" | "single" | "multi". Default: "multi".
    "pageSize": 25,                        // Records per FetchXML page. Framework runtime default: 25 (FR-07). Override to 50-100 for drill-through / full-page grids.
    "enableSorting": true,                 // Column-header click sorts. Default: true.
    "enableColumnResize": true,            // Drag column edges. Default: true.
    "enableKeyboardNavigation": true,      // Arrow keys move focus. Default: true.
    "parentContextFilter": {               // Drill-through parent filter. See Step 4.
      "attribute": "sprk_matter",
      "parentContextKey": "matterId",
      "operator": "eq"                     // "eq" | "neq" | "in" | "eq-userid" | "eq-userteams". Default: "eq".
    }
  }
}
```

**Live reference records** — real records you can inspect via Dataverse:

| Config record | Pattern | GUID (spaarkedev1) |
|---|---|---|
| Documents workspace widget | Minimal — source + display + behavior.pageSize | `1cdd19d2-3964-f111-ab0c-7ced8ddc4cc6` |
| Matters / Projects / Work Assignments workspace | Minimal — same shape as Documents | see [`projects/ai-spaarke-ai-workspace-UI-r2/notes/config-record-audit.md`](../../projects/ai-spaarke-ai-workspace-UI-r2/notes/config-record-audit.md) |
| Communications workspace widget | Minimal + rowOpen.formDialog + pageSize=25 | `e1826c4c-9575-f111-ab0e-7ced8ddc4a05` |
| Invoice Matter Budget Performance (rich) | Full — filterChips + commandBar overrides + secondaryActions + behavior.parentContextFilter | `d021827b-9b5e-f111-ab0c-7c1e521545d7` |

---

## Step 6 — Wire the host shell

### Drill-through Custom Page

Copy one of the reference shells verbatim and change the `CONFIG_ID`:

- [`src/solutions/sprk_kpiassessmentspage/src/main.tsx`](../../src/solutions/sprk_kpiassessmentspage/src/main.tsx)
- [`src/solutions/sprk_invoicespage/src/main.tsx`](../../src/solutions/sprk_invoicespage/src/main.tsx)

The shell is ~50 lines and does three things: parse the URL `data=` envelope for `matterId`, build `parentContext`, mount `<DataGrid configId=… parentContext=… dataverseClient={new XrmDataverseClient()} />`.

> 🚨 **The shell MUST read `filterValue` from the envelope — this is the #1 drill-through bug.**
> VisualHost's expand button opens the page via `Xrm.Navigation.navigateTo({pageType:'webresource'})`
> with a form-encoded `data=` envelope whose keys are `entityName`, `filterField`, `filterValue`,
> `viewId`, `mode`. **The parent record id arrives as `filterValue`** — and `entityName` is the
> *chart's* reporting entity (e.g. `sprk_event`), **NOT** the parent entity. A shell that instead
> looks for a `recordId` param, or gates on `entityName === 'sprk_matter'`, will receive `undefined`
> parent context and render the grid **unfiltered (all records)**. Copy the reference shells verbatim:
> [`sprk_invoicespage/main.tsx` `parseMatterId()`](../../src/solutions/sprk_invoicespage/src/main.tsx) reads
> `params.get("filterValue")` and exposes it under the config's `parentContextKey` (`matterId`).

Then update the VisualHost `sprk_chartdefinition` record:

- `sprk_drillthroughtarget` = web-resource name (e.g. `sprk_kpiassessmentspage.html`)
- `sprk_contextfieldname` = lookup column reference (`_sprk_matter_value` or `_sprk_regardingmatter_value`) — **must be non-empty**, or VisualHost omits `filterValue` from the envelope entirely and the grid renders unfiltered.

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

## Recipe — build a VisualHost drill-through into a related-records grid

This is the end-to-end procedure for "click the ⤢ expand button on a chart on a *parent* form → open a
dialog showing only the *child* records related to that parent." It ties together the chart definition
(VisualHost side) and the grid config + page shell (this framework). See also the companion
[VISUALHOST-SETUP-GUIDE.md drill-through section](VISUALHOST-SETUP-GUIDE.md#setting-up-drill-through-into-a-datagrid-framework-page-events-invoices-kpi-assessments).

### The three moving parts

| # | Piece | Record / file | Key setting |
|---|---|---|---|
| 1 | **Chart definition** (VisualHost) | `sprk_chartdefinition` | `sprk_drillthroughtarget` = `<page>.html`; `sprk_contextfieldname` = `_<childLookup>_value` (**must be non-empty**) |
| 2 | **Grid config** (this framework) | `sprk_gridconfiguration.sprk_configjson` | `behavior.parentContextFilter` = `{ attribute:"<childLookup>", parentContextKey:"matterId", operator:"eq" }` |
| 3 | **Page shell** (Custom Page web resource) | `src/solutions/<name>/` | parses `filterValue` from the envelope, exposes it as `parentContext.matterId`, mounts `<DataGrid>` |

VisualHost opens the page via `navigateTo({pageType:'webresource'})` with a form-encoded `data=` envelope
(`entityName`, `filterField`, `filterValue`, `viewId`, `mode`). **The parent GUID is in `filterValue`**, and
`entityName` is the *chart's* entity, not the parent — see the Step 6 warning. The framework then overlays
`<condition attribute="<childLookup>" operator="eq" value="<parentGuid>"/>` onto the base view's FetchXML.

### Step-by-step

1. **Find the child entity's lookup back to the parent.** It is **not** uniformly `sprk_matter` — inspect metadata (Dataverse MCP `describe('tables/<entity>')` or the maker portal). Verified examples:

   | Drill-through (from a Matter form) | Child entity | Child lookup → parent | `sprk_contextfieldname` | `parentContextFilter.attribute` |
   |---|---|---|---|---|
   | Matter → Events / Tasks | `sprk_event` | `sprk_regardingmatter` | `_sprk_regardingmatter_value` | `sprk_regardingmatter` |
   | Matter → KPI Assessments | `sprk_kpiassessment` | `sprk_matter` | `_sprk_matter_value` | `sprk_matter` |
   | Matter → Report Cards | `sprk_reportcard` | `sprk_regardingmatter` | `_sprk_regardingmatter_value` | `sprk_regardingmatter` |
   | Matter → Invoices | `sprk_invoice` | `sprk_matter` | `_sprk_matter_value` | `sprk_matter` |
   | Invoice → Invoice Line Items | `sprk_invoicelineitem` | `sprk_invoice` | `_sprk_invoice_value` | `sprk_invoice` |

   > ⚠ **Two-hop relationships aren't drop-in.** `sprk_invoicelineitem` has **no lookup to Matter** — only to `sprk_invoice`. `parentContextFilter` injects **one** condition on a **direct** attribute of the child, so you can filter line items by *invoice* (drill from an Invoice), but "line items for a Matter" needs a link-entity in inline FetchXML, which is outside the single-condition overlay.

2. **Does a page shell already exist for that child entity?** Check the registry in [`scripts/Deploy-AllDataGridConsumers.ps1`](../../scripts/Deploy-AllDataGridConsumers.ps1). Current shells: `sprk_eventspage.html` (Event), `sprk_invoicespage.html` (Invoice), `sprk_kpiassessmentspage.html` (KPI Assessment). If yes → reuse it; skip to step 4.

3. **If no shell exists, create one.** Copy [`src/solutions/sprk_invoicespage`](../../src/solutions/sprk_invoicespage/src/main.tsx) verbatim, change `CONFIG_ID`, keep `parseMatterId()` (it reads `filterValue`). Add the new page to the `Deploy-AllDataGridConsumers.ps1` registry. Build + deploy.

   > For a shell whose parent is **not** a Matter (e.g. Invoice → Line Items), rename the exposed key: expose `parentContext.invoiceId = filterValue` and set the grid config's `parentContextKey: "invoiceId"`. The key is just a label that must match between shell and config.

4. **Author (or reuse) the `sprk_gridconfiguration` record** for the child entity (Steps 1–5 above). Add `behavior.parentContextFilter` with the `attribute` from the table. Optionally restrict the view picker with `availableViews` (Step 3.5).

5. **Configure the chart definition:** `sprk_drillthroughtarget = <page>.html`, `sprk_contextfieldname = _<childLookup>_value`.

6. **Verify:** open the dialog → DevTools console → `[DataGrid] fetchXml composition` shows `parentContext.matterId` populated and `hasParentFilterMatch: true`.

---

## Troubleshooting

| Symptom | First thing to check |
|---|---|
| Grid renders unfiltered (all records, not just the parent's) | DevTools Console → `[DataGrid] fetchXml composition`. Is `parentContext.matterId` empty? → EITHER the page shell doesn't parse `filterValue` from the VisualHost `data=` envelope (see Step 6 — the shell must read `filterValue`, not `recordId`, and must not gate on `entityName === 'sprk_matter'` since VisualHost sends the *chart's* entity), OR `sprk_chartdefinition.sprk_contextfieldname` is blank (VisualHost then omits `filterValue`). Is `hasParentFilterMatch: false`? → `behavior.parentContextFilter.attribute` doesn't match the lookup attribute on the child entity. |
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
