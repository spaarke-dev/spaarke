# Spaarke DataGrid Framework — Design (R1)

> **Status**: Draft for review
> **Author**: AI-assisted design pass (Ralph Schroeder, 2026-06-01)
> **Branch**: `work/spaarke-matter-ui-enhancement-r1` (this design surfaced during the Matter UI drill-through work)

---

## 1. Purpose

Provide a single, configuration-driven dataset grid framework that any Spaarke surface — MDA Custom Pages, SpaarkeAi workspaces, MCP App widgets, future external SPAs — can use to display, filter, and act on any Dataverse entity, without per-entity TypeScript code.

The framework's contract is one Dataverse record (`sprk_gridconfiguration`) plus an injectable Dataverse client. New grids ship as **a Dataverse configuration record + a ~50-line host shell**, not as new TypeScript components.

## 2. Goals

| # | Goal |
|---|---|
| G1 | One `<DataGrid configId={...} />` component that renders any entity, driven by Dataverse savedquery + entity metadata + `sprk_gridconfiguration.sprk_configjson` |
| G2 | Works in MDA (Custom Page, PCF host) AND outside MDA (Code Page SPA, SpaarkeAi workspace, MCP App widget, test harness) — via an `IDataverseClient` abstraction |
| G3 | Auto-discovers entity savedqueries (no config drift when a Dataverse admin adds a view) |
| G4 | Standardized command bar + filter chips, configurable per entity via `sprk_configjson` — with sensible defaults derived from entity metadata |
| G5 | Host-extension model for side panes, wizards, AI assistants (sprkchat / playbook launcher) — grid emits, host decides |
| G6 | Reuses existing assets in `@spaarke/ui-components` (services, primitives), `SemanticSearch` code page (SearchResultsGrid pattern), `SemanticSearchControl` PCF (filter/command UX), and `@spaarke/events-components` (FetchXmlService, parseLayoutXml) — does not duplicate them |
| G7 | EventsPage migrates onto this framework with no UX regression (Calendar pane preserved, side-pane wiring preserved as host concern) |
| G8 | Drives the Matter Health + Budget Performance drill-throughs that triggered this design (each = one `sprk_gridconfiguration` record + one ~50-line Custom Page shell) |
| **G9** | **Built on Fluent v9 native `DataGrid` primitive** — exploits its built-in selection / sort / resize / keyboard / density / sticky-header behaviors instead of hand-rolling. See §11.5.1. |
| **G10** | **Exact visual parity with the MDA Power Apps grid control** — pixel-level, both light + dark themes. See §11.5.2 for codified tokens. |
| **G11** | **Every UI task invokes the `/fluent-v9-component` skill** as Step 0.5. ADR-021 + portal-gotcha + token-only compliance is the acceptance gate. See §11.5.4. |

## 3. Non-goals

- NOT replacing `SemanticSearchControl` PCF (matter form embed). That stays as-is.
- NOT replacing OOB grids in entity main views. Spaarke views via Custom Pages only.
- NOT rebuilding the older `UniversalDatasetGrid` PCF — services harvested; old PCF shell retired separately.
- NOT introducing per-user grid customization (column resize/save/reorder per user). Out of scope for R1.
- NOT a charting library — VisualHost remains the canonical chart framework.

## 4. Glossary

| Term | Meaning |
|---|---|
| **savedquery** | Dataverse OOB entity that stores system + personal views. Contains `fetchxml`, `layoutxml`, `returnedtypecode` (entity), `name`. Authored in Power Apps Maker. |
| **layoutXml** | XML inside savedquery declaring `<row id="...primary id field..."/>`, `<grid jump="...primary name field..."/>`, and `<cell name="..." width="..."/>` per column. |
| **`sprk_gridconfiguration`** | Spaarke custom entity. ONE record per (entity, view) pair, or one per entity for entity-wide defaults. Carries `sprk_configjson`. |
| **`sprk_configjson`** | 1 MB multiline JSON on `sprk_gridconfiguration`. Carries ALL framework knobs (filter chip whitelist, command bar customization, row-open behavior, secondary actions). |
| **filter chip** | Toolbar quick-filter dropdown (e.g., "Status: [▼]"). Auto-derivable from layoutXml column whose `AttributeType ∈ {OptionSet, Status, State, Lookup, DateTime, Boolean}`. |
| **`IDataverseClient`** | Injectable abstraction over Dataverse access. Implementations: `XrmDataverseClient` (uses Xrm.WebApi via `window` or `window.parent`) and `BffDataverseClient` (uses `authenticatedFetch` from `@spaarke/auth` against BFF passthrough endpoints). |
| **Host extension slot** | The grid emits events (`onRecordOpen`, `onRecordAction`, `onCommandInvoke`); the host wires each event to a concrete UX (side pane, wizard, navigate, sprkchat, playbook). No grid code change to support a new UX. |

## 5. Current state inventory

### 5.1 Three coexisting dataset grids

| Implementation | Path | Tech | Used by | Disposition |
|---|---|---|---|---|
| `UniversalDatasetGrid` | `src/client/shared/Spaarke.UI.Components/src/components/DatasetGrid/` + `src/client/pcf/UniversalDatasetGrid/` | React, PCF-bindable + headless mode | `SpeAdminApp` dashboard | **Components retired; services harvested.** |
| `GridSection` | `src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx` (1,795 lines) | React 19, Custom Page | `EventsPage` Custom Page | **Lift generic concerns to new framework; thin Event shell retained.** |
| `SearchResultsGrid` | `src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx` (476 lines) | React 18, Fluent v9 native `DataGrid` | `SemanticSearch` Code Page | **Closest existing impl to target. Migrate to consume new framework after framework lands.** |
| `SemanticSearchControl/ListView` | `src/client/pcf/SemanticSearchControl/` | PCF, hand-rolled | Matter form embed | **OUT OF SCOPE. Stays. Not touched.** |

### 5.2 `sprk_gridconfiguration` actual schema (verified via Dataverse 2026-06-01)

Live entity has ONLY:
- `sprk_gridconfigurationid` (PK)
- `sprk_name`
- `sprk_entitylogicalname`
- `sprk_configjson` (multiline, 1 MB)
- `sprk_isdefault` (boolean)
- `sprk_sortorder` (integer)
- Standard system fields (statecode, statuscode, ownerid, createdon, modifiedon)

The schema doc at `src/solutions/SpaarkeCore/entities/sprk_gridconfiguration/entity-schema.md` was aspirational — `sprk_viewtype`, `sprk_savedviewid`, `sprk_fetchxml`, `sprk_layoutxml` were planned but never added. **R1 keeps the entity as-is** and routes all view-source info through `sprk_configjson`.

### 5.3 Existing services / primitives in `@spaarke/ui-components` (harvestable)

| File | Purpose | R1 disposition |
|---|---|---|
| `services/CommandRegistry.ts` | Command bar action registration | **Reuse** |
| `services/CommandExecutor.ts` | Invokes commands with context | **Reuse** |
| `services/CustomCommandFactory.ts` | Builds command instances from config | **Reuse** |
| `services/PrivilegeService.ts` | Xrm-based privilege checks (Create/Read/Write/Delete per entity) | **Reuse — drives optional security gating on commands** |
| `services/EntityConfigurationService.ts` | Loads + caches entity-level config | **Reuse, generalize for IDataverseClient** |
| `services/ConfigurationService.ts` | Resolves `sprk_gridconfiguration` records | **Reuse, extend for view-selector hierarchy** |
| `services/ViewService.ts` | Resolves Dataverse savedquery views | **Reuse** |
| `services/FetchXmlService.ts` | Executes FetchXML via Xrm.WebApi | **Generalize to IDataverseClient** |
| `services/ColumnRendererService.tsx` | Per-dataType cell rendering | **Reuse, extend** |
| `components/Toolbar/CommandToolbar` | Command bar UI | **Reuse if Fluent v9 compliant; otherwise rewrite** |
| `components/SidePane/` | Side pane primitive | **Reuse for default `rowOpen.type === "sidePane"` handler** |
| `components/Wizard/`, all wizard implementations | Wizard primitives | **Reuse for default `secondaryActions.kind === "wizard"` handler** |
| `components/SprkChat/` | sprkchat embed | **Reuse for `secondaryActions.kind === "ai-assistant"` handler** |
| `components/Playbook/`, `PlaybookLibraryShell/` | Playbook launcher | **Reuse for `secondaryActions.kind === "playbook"` handler** |
| `components/DatasetGrid/GridView`, `CardView`, `ListView`, `VirtualizedListView`, `VirtualizedGridView` | UDG render layers | **Retire — replaced by new Fluent v9 native DataGrid** |

### 5.4 Existing services / primitives in `@spaarke/events-components` (harvestable)

| File | Purpose | R1 disposition |
|---|---|---|
| `services/FetchXmlService.ts` (Events copy) | Executes FetchXML, `parseLayoutXml`, `injectContextFilter`, `ensureRequiredAttributes`, `mergeDateFilterIntoFetchXml` | **Lift to `@spaarke/ui-components` after de-Event-typing** |
| `components/ColumnHeaderMenu/`, `ColumnFilterHeader/` | Column header dropdown (sort + filter) | **Lift to `@spaarke/ui-components` after Fluent v9 portal fix (applyStylesToPortals)** |
| `components/CalendarSection/` | Calendar filter UI | **Stays in events-components — Event-host-specific concern, NOT a generic chip** |
| `components/AssignedToFilter/`, `RecordTypeFilter/`, `StatusFilter/` | Event-specific filter chips | **Retire — replaced by generic chip primitives driven by `sprk_configjson`** |
| `components/ViewSelectorDropdown/` | View selector | **Reuse; generalize to derive from savedquery + sprk_gridconfiguration** |
| `components/GridSection/` | Main grid | **Retire — replaced by new `<DataGrid />`** |

### 5.5 Existing primitives in `SemanticSearchControl` PCF (harvestable patterns, not code copy)

The PCF is OUT OF SCOPE for migration, but its UX patterns (carefully polished across v1.1.x) are the design reference:
- `CommandBar.tsx` — toolbar layout (left: chart title; right: AI + action icons)
- `BulkActionBar.tsx` — selection-driven secondary command bar
- `FilterDropdown.tsx` — multi-select dropdown filter chip (Fluent v9 `Menu` + `MenuItemCheckbox`)
- `DateRangeFilter.tsx` — date range filter chip
- `FilterPanel.tsx` — filter chip strip layout

These inform the framework's chip + command primitives. The framework versions live in `@spaarke/ui-components`.

### 5.6 Existing primitives in `SemanticSearch` code page (closest to target)

`SearchResultsGrid.tsx` uses Fluent v9 native `DataGrid` (`DataGrid`, `DataGridHeader`, `DataGridRow`, `DataGridHeaderCell`, `DataGridBody`, `DataGridCell`, `createTableColumn`) plus `IDatasetRecord` shape (from `adapters/searchResultAdapter.ts`) and `IDatasetColumn` shape (from `hooks/useSearchViewDefinitions.ts`), with `renderByDataType(value, dataType)` switching over `Percentage | DateAndTime.DateOnly | Currency | StringArray | FileType | EntityLink | default`.

**This is the canonical primitive R1 builds on.** No new table primitive — use Fluent v9 native `DataGrid` directly.

## 6. Contract — the `<DataGrid />` component

### 6.1 Props

```ts
interface IDataGridProps {
  // REQUIRED: pointer to the sprk_gridconfiguration record
  configId: string;

  // OPTIONAL: drill-through context filter (e.g., current Matter on form embed)
  parentContext?: {
    entityType: string;
    id: string;
    name: string;
  };

  // OPTIONAL: data access. Defaults to XrmDataverseClient (autoresolved from window / window.parent)
  dataverseClient?: IDataverseClient;

  // OPTIONAL: host event handlers. If omitted, framework supplies sensible defaults
  // derived from sprk_configjson.rowOpen / .secondaryActions / .commandBar
  onRecordOpen?: (recordId: string, record: Record<string, unknown>, ctx: HostContext) => void;
  onRecordAction?: (actionId: string, recordId: string, record: Record<string, unknown>, ctx: HostContext) => void;
  onCommandInvoke?: (commandId: string, selectedIds: string[], ctx: HostContext) => void;

  // OPTIONAL: surface-level overrides (rarely used, escape hatch)
  overrides?: {
    columnRenderers?: { [fieldName: string]: (value: unknown, record: Record<string, unknown>) => React.ReactNode };
    statusBadgeMap?:  { [optionValue: number]: BadgeAppearance };
    filterChipsAllowlist?: string[]; // restrict to subset of columns
  };
}

interface HostContext {
  configId: string;
  entityName: string;
  parentContext?: { entityType: string; id: string; name: string };
  selectedIds: string[];
}
```

### 6.2 `IDataverseClient` interface

```ts
interface IDataverseClient {
  retrieveSavedQuery(savedQueryId: string): Promise<{
    entityName: string;
    fetchXml: string;
    layoutXml: string;
    name: string;
  }>;

  retrieveSavedQueriesForEntity(entityName: string): Promise<Array<{
    id: string;
    name: string;
    isDefault: boolean;
    queryType: number;
  }>>;

  retrieveEntityMetadata(entityName: string): Promise<{
    primaryIdAttribute: string;
    primaryNameAttribute: string;
    attributes: {
      [logicalName: string]: {
        attributeType: 'String' | 'Integer' | 'Money' | 'DateTime' | 'Lookup' | 'Picklist' | 'Status' | 'State' | 'Boolean' | 'Decimal' | string;
        format?: string;        // e.g., 'Email' | 'Phone' | 'Url' | 'TextArea'
        isPrimaryName?: boolean;
        isPrimaryId?: boolean;
        optionSet?: { value: number; label: string; color?: string }[];
      };
    };
  }>;

  retrieveMultipleRecords<T = Record<string, unknown>>(
    entityName: string,
    fetchXml: string
  ): Promise<{ entities: T[]; moreRecords: boolean; pagingCookie?: string }>;

  retrieveRecord<T = Record<string, unknown>>(
    entityName: string,
    id: string,
    select?: string[]
  ): Promise<T>;
}
```

**Two implementations**:
- `XrmDataverseClient` — wraps `Xrm.WebApi` + `Xrm.Utility.getEntityMetadata`. Auto-walks `window` / `window.parent` for the Xrm object (Custom Page iframe case). Same `getXrm()` pattern as existing services.
- `BffDataverseClient` — uses `authenticatedFetch` from `@spaarke/auth`. Requires four new BFF endpoints (§9).

### 6.3 `sprk_configjson` schema (R1)

```ts
type GridConfigJson = {
  _version: '1.0';

  // ───── SOURCE (one of) ─────
  source:
    | { type: 'savedquery'; savedQueryId: string }                                        // common case
    | { type: 'inline'; fetchXml: string; layoutXml: string }                              // ad-hoc
    | { type: 'savedquery-set'; entityLogicalName: string };                              // entity-wide: discover savedqueries dynamically

  // ───── DISPLAY ─────
  display?: {
    title?: string;                  // override savedquery name (default: savedquery name)
    icon?: string;                   // Fluent v9 icon name
    densityDefault?: 'comfortable' | 'compact';
    emptyStateMessage?: string;
  };

  // ───── FILTER CHIPS ─────
  // Default behavior: every layoutXml column whose AttributeType is OptionSet/Status/State/Lookup/DateTime/Boolean
  // is auto-promoted to a filter chip. Use these to override:
  filterChips?: {
    mode: 'auto' | 'allowlist' | 'denylist' | 'explicit';
    allowlist?: string[];            // logicalNames (mode=allowlist)
    denylist?: string[];             // logicalNames (mode=denylist)
    explicit?: Array<{               // mode=explicit — full control
      field: string;
      kind: 'optionset-multi' | 'lookup-multi' | 'date-range' | 'text' | 'bool';
      label?: string;                // override display label
      valueSource?: { type: 'systemusers' } | { type: 'entity'; entity: string; nameField: string };
      valueColors?: { [optionValue: number]: BadgeAppearance };
    }>;
    showClearAll?: boolean;          // default true
  };

  // ───── COMMAND BAR ─────
  commandBar?: {
    primary?: CommandBarItem[];       // visible chips, left-aligned
    secondary?: CommandBarItem[];     // overflow menu / right side
    showDefaultCommands?: {            // toggle framework defaults
      newRecord?: boolean;            // default true if entity is createable
      delete?: boolean;               // default true if entity is deleteable
      refresh?: boolean;              // default true
      exportExcel?: boolean;          // default true
      editColumns?: boolean;          // default true (user can show/hide columns)
      editFilters?: boolean;          // default true
    };
  };

  // ───── ROW OPEN BEHAVIOR ─────
  rowOpen?: {
    type: 'sidePane' | 'wizard' | 'navigateToForm' | 'dialog' | 'webResource' | 'custom';

    // type=sidePane:
    paneId?: string;
    paneTitle?: string;
    webResourceName?: string;
    width?: number;

    // type=wizard:
    wizardName?: string;              // resolved against registered wizards

    // type=dialog:
    dialogComponent?: string;         // resolved against registered dialog components

    // type=webResource:
    webResource?: string;
    dataParams?: string[];            // field names from the row + parentContext to pass as URL params

    // type=custom:
    customHandlerId?: string;         // host-registered handler

    // Always:
    passContext?: string[];           // keys from parentContext to propagate (e.g., ["parentMatterId"])
  };

  // ───── SECONDARY ACTIONS (per-row + bulk) ─────
  secondaryActions?: Array<{
    id: string;
    label: string;
    icon: string;
    kind: 'ai-assistant' | 'playbook' | 'wizard' | 'navigate' | 'custom';
    requiresSelection?: 'single' | 'multi' | false;
    privilege?: 'Read' | 'Write' | 'Create' | 'Delete' | 'Append' | 'AppendTo'; // optional security gate
    visible?: 'always' | 'row-hover' | 'bulk-only';                              // when to surface

    // kind-specific config:
    aiAssistantId?: string;           // for ai-assistant
    playbookId?: string;              // for playbook
    wizardName?: string;              // for wizard
    navigateTarget?: { entity: string; idField: string };  // for navigate
    customHandlerId?: string;         // for custom
  }>;

  // ───── COLUMN OVERRIDES ─────
  columns?: {
    [logicalName: string]: {
      label?: string;                 // override display label
      width?: number;                 // override width
      renderer?: 'default' | 'currency' | 'percentage' | 'badge' | 'link' | 'date' | 'datetime' | 'avatar' | 'icon' | string;
      align?: 'left' | 'center' | 'right';
      tooltip?: string;
      hidden?: boolean;
    };
  };

  // ───── BEHAVIOR ─────
  behavior?: {
    selectionMode?: 'none' | 'single' | 'multi'; // default 'multi'
    pageSize?: number;                            // default 50
    enableSorting?: boolean;                      // default true
    enableColumnResize?: boolean;                 // default true
    enableKeyboardNavigation?: boolean;           // default true
  };
};

type CommandBarItem = {
  id: string;
  label: string;
  icon: string;
  action: 'create-form' | 'delete-selected' | 'refresh' | 'export-excel' | 'edit-columns' | 'edit-filters' | 'custom';
  customHandlerId?: string;
  requiresSelection?: 'single' | 'multi' | false;
  privilege?: 'Read' | 'Write' | 'Create' | 'Delete';
  appearance?: 'subtle' | 'primary' | 'secondary';
  divider?: boolean;     // render a divider before this item
};
```

### 6.4 Resolution order (what wins when configs disagree)

For any given (entity, view) the framework resolves config in this priority:

1. **`overrides` prop** on `<DataGrid />` — explicit per-host overrides (highest)
2. **`sprk_gridconfiguration.sprk_configjson`** for the matching record
3. **Entity metadata defaults** — derived from `IDataverseClient.retrieveEntityMetadata`
4. **layoutXml column declarations** — derived from `IDataverseClient.retrieveSavedQuery`
5. **Framework defaults** — hardcoded sensible behavior (lowest)

Practical impact: a `sprk_gridconfiguration` record is OPTIONAL. If none exists for the (entity, view) pair, the framework falls back to entity metadata + layoutXml and renders a working default grid. **Adding a new savedquery in Dataverse = zero-code new grid view.**

## 7. Host extension model (#1 of your clarifications)

The grid does NOT own side panes, wizards, or AI launchers. It emits three event types, each carrying full row + parent context. The host wires events to concrete UX.

### 7.1 The three event signals

```ts
// Fires when user clicks the primary "open record" affordance (the row link).
onRecordOpen?: (recordId, record, ctx: HostContext) => void;

// Fires when user invokes a row-scoped secondary action (sprk_configjson.secondaryActions).
onRecordAction?: (actionId, recordId, record, ctx: HostContext) => void;

// Fires when user invokes a command bar action (sprk_configjson.commandBar).
onCommandInvoke?: (commandId, selectedIds, ctx: HostContext) => void;
```

### 7.2 Framework-default handlers (when host does not override)

| `sprk_configjson` setting | Default handler |
|---|---|
| `rowOpen.type = "sidePane"` | Opens `Xrm.App.sidePanes` with `webResourceName` (when running in MDA Custom Page); falls back to `Xrm.Navigation.openForm({entityName, entityId})` otherwise |
| `rowOpen.type = "wizard"` | Opens `<Wizard wizardName={...} parentContext={...} />` from `@spaarke/ui-components/Wizard` in a Fluent `Dialog` |
| `rowOpen.type = "navigateToForm"` | `Xrm.Navigation.openForm({entityName, entityId})` |
| `rowOpen.type = "dialog"` | Renders host-registered dialog component in a Fluent `Dialog` |
| `rowOpen.type = "webResource"` | `Xrm.Navigation.navigateTo({pageType:"webresource", webresourceName, data})` — **this is the fix for the EventsPage record-link-not-opening bug when in dialog mode** |
| `rowOpen.type = "custom"` | Calls host-registered handler by `customHandlerId` |
| `secondaryActions[].kind = "ai-assistant"` | Opens sprkchat side panel via existing `@spaarke/ui-components/SprkChat` with row + parent context |
| `secondaryActions[].kind = "playbook"` | Opens `<PlaybookLibraryShell playbookId={...} parentContext={...} />` in side pane or dialog |
| `secondaryActions[].kind = "wizard"` | Same as `rowOpen.type = "wizard"` |
| `secondaryActions[].kind = "navigate"` | `Xrm.Navigation.openForm({entityName, entityId})` |
| `secondaryActions[].kind = "custom"` | Calls host-registered handler |

### 7.3 Host registration API

Hosts can register custom handlers + custom wizards / dialogs at module load:

```ts
import { registerCommandHandler, registerWizard, registerDialog } from '@spaarke/ui-components/DataGrid';

registerCommandHandler('MarkInvoicesPaid', async (ctx) => {
  // ctx = { entityName, selectedIds, parentContext }
  await fetch('/api/invoices/mark-paid', { method: 'POST', body: JSON.stringify(ctx.selectedIds) });
  ctx.dataGrid.refresh();
});

registerWizard('PayInvoiceWizard', PayInvoiceWizard);   // React component
```

The Events page registers its custom side pane wiring + Calendar pane this way, NOT by importing the grid and bypassing the event signals. **`@spaarke/events-components` becomes a thin host that registers Event-specific handlers + the Calendar pane; it does NOT extend the grid.**

## 8. Filter chip subsystem (#2 + #4 of your clarifications)

### 8.1 Auto-derivation from layoutXml + entity metadata

For each `<cell name="X" />` in layoutXml, the framework inspects `entityMetadata.attributes[X].attributeType`:

| AttributeType | Auto-derived chip kind |
|---|---|
| `Picklist` / `Status` / `State` | `optionset-multi` (Fluent `Menu` + `MenuItemCheckbox` per option, color from metadata) |
| `Lookup` | `lookup-multi` (Fluent `Combobox` w/ async lookup search via IDataverseClient) |
| `DateTime` | `date-range` (Fluent `Calendar` for from/to) |
| `Boolean` | `bool` (Fluent `Dropdown` Yes/No/Any) |
| Everything else | NO chip (does not pollute the toolbar) |

This means most entities get a working filter strip with **zero `sprk_configjson` authoring**. The user (or `sprk_configjson.filterChips`) only intervenes to:
- Restrict chips to a subset (`allowlist`)
- Override labels or colors
- Add a chip for a field NOT in the layout (rare — `explicit` mode)

### 8.2 Chip primitives (live in `@spaarke/ui-components`)

| Primitive | Fluent v9 building block | Notes |
|---|---|---|
| `<LookupMultiFilterChip />` | `Combobox` with multi-select | Async option load via `IDataverseClient.retrieveMultipleRecords` |
| `<OptionSetMultiFilterChip />` | `Menu` + `MenuItemCheckbox` | Static options from entity metadata |
| `<DateRangeFilterChip />` | `Popover` + `Calendar` | UTC-bounds conversion handled (same logic as `GridSection.localDateToUtcBounds`) |
| `<TextFilterChip />` | `Popover` + `Input` | `contains` operator |
| `<BoolFilterChip />` | `Dropdown` | Yes / No / Any |

All primitives observe ADR-021 (Fluent v9 only, no hex), use `applyStylesToPortals` on their inner `FluentProvider` wrap, support light/dark/HC themes automatically.

### 8.3 Event-specific patterns retire

Today's `AssignedToFilter`, `RecordTypeFilter`, `StatusFilter` in `@spaarke/events-components` become THREE auto-derived chips (`lookup-multi` over systemusers, `optionset-multi` over `sprk_eventtype_ref`, `optionset-multi` over `sprk_eventstatus`). Their files retire after the migration. The Calendar pane stays — it's a side pane, not a chip.

## 9. BFF passthrough endpoints (#G2 — outside MDA)

Required for `BffDataverseClient`. Four endpoints in `Sprk.Bff.Api/Api/Dataverse/`:

| Endpoint | Returns | Caching |
|---|---|---|
| `GET /api/dataverse/savedquery/{id}` | `{ entityName, fetchXml, layoutXml, name }` | `IDistributedCache` w/ 1h TTL (savedqueries change rarely) |
| `GET /api/dataverse/savedqueries/{entity}` | `[{ id, name, isDefault, queryType }]` | 1h |
| `GET /api/dataverse/metadata/{entity}` | full entity metadata projection | `IDistributedCache` w/ 6h TTL (entity metadata changes only on solution import) |
| `POST /api/dataverse/fetch` `{ entityName, fetchXml }` | `{ entities, moreRecords, pagingCookie }` | NO caching — live data |
| `GET /api/dataverse/record/{entity}/{id}?$select=...` | record | NO caching |

**Auth**: standard OBO via `@spaarke/auth` → BFF authenticatedFetch → BFF resolves caller's claims, hits Dataverse via app-only `ServiceClient` with `CallerId` impersonation (existing pattern).

**ADR-008 endpoint filter**: each endpoint takes an authorization filter that checks the caller has `Read` privilege on the target entity (cheap — entity metadata is already cached).

**Placement justification** (per `.claude/constraints/bff-extensions.md`): these are pure Dataverse passthrough + cache. They live in the BFF because (a) browser → Dataverse direct calls outside MDA require per-tab MSAL for `dynamics.com` scope, doubling auth complexity, and (b) caching is far more effective server-side (one cache vs. N browsers). They do NOT belong in a separate microservice — they're a thin proxy.

## 10. Migration plan

### Phase A — Foundation (build framework, no migration yet)

| Task | Output |
|---|---|
| A1 | `IDataverseClient` interface + `XrmDataverseClient` impl in `@spaarke/ui-components/services` |
| A2 | Generalize `FetchXmlService`, `parseLayoutXml`, `injectContextFilter` to take `IDataverseClient` (remove hardcoded `sprk_eventid`, read `<row id>` from layoutXml) |
| A3 | Build chip primitives (`LookupMultiFilterChip`, `OptionSetMultiFilterChip`, `DateRangeFilterChip`, `TextFilterChip`, `BoolFilterChip`) |
| A4 | Build `<DataGrid />` core on Fluent v9 native `DataGrid` primitive (model after `SearchResultsGrid.tsx`) |
| A5 | `ConfigurationService` reads `sprk_gridconfiguration`, merges with metadata + layoutXml defaults |
| A6 | Command bar primitive + `registerCommandHandler` / `registerWizard` / `registerDialog` registry |
| A7 | Lift `ColumnHeaderMenu` to `@spaarke/ui-components` with `applyStylesToPortals` fix |
| A8 | Storybook + unit tests for all primitives (axe a11y, light/dark mode) |

### Phase B — BFF passthrough (unlocks non-MDA)

| Task | Output |
|---|---|
| B1 | Four `Api/Dataverse/` endpoints + `IDistributedCache` wiring |
| B2 | `BffDataverseClient` impl in `@spaarke/ui-components/services` |
| B3 | Integration tests against dev BFF |

### Phase C — First production consumer: Matter UI drill-throughs

| Task | Output |
|---|---|
| C1 | Author 2 `sprk_gridconfiguration` records: one for `sprk_kpiassessment` (Matter Health), one for `sprk_invoice` (Budget Performance). Author 2 savedqueries in Dataverse for the column layouts. |
| C2 | Build `sprk_kpiassessmentspage.html` Code Page (~50 lines: thin shell around `<DataGrid />`) |
| C3 | Build `sprk_invoicespage.html` Code Page (~50 lines: ditto) |
| C4 | Update Matter Health + Budget Performance chart-def `sprk_drillthroughtarget` to the new Code Pages |
| C5 | UAT |

### Phase D — Migrate EventsPage onto framework

| Task | Output |
|---|---|
| D1 | Author `sprk_gridconfiguration` for `sprk_event` with full `sprk_configjson` (chips + commands + rowOpen=sidePane + secondaryActions) |
| D2 | Rewrite `EventsPage/App.tsx` as ~150-line host: registers Event-specific handlers (`openEventDetailPane`, Calendar pane mount), passes `configId` to `<DataGrid />` |
| D3 | Retire `@spaarke/events-components/GridSection`, `AssignedToFilter`, `RecordTypeFilter`, `StatusFilter`. Keep `CalendarSection`, `ViewSelectorDropdown`, `EventsPageContext`, `useViewSelection`. |
| D4 | UAT — including dialog-mode drill-through from VisualHost (closes the record-link-not-opening bug) |
| D5 | SpaarkeAi Calendar widget consumes the same `<DataGrid />` (replaces its current GridSection usage) |

### Phase E — Migrate SemanticSearch code page

| Task | Output |
|---|---|
| E1 | Update SS `sprk_gridconfiguration.sprk_configjson` from `_type: semantic-search-view` to new schema |
| E2 | Refactor `SearchResultsGrid.tsx` to consume new `<DataGrid />` (or retire entirely) |
| E3 | UAT |

### Phase F — Retire legacy

| Task | Output |
|---|---|
| F1 | Remove `@spaarke/ui-components/components/DatasetGrid/{GridView,CardView,ListView,VirtualizedListView,VirtualizedGridView}.tsx` |
| F2 | Retire `src/client/pcf/UniversalDatasetGrid/` (the PCF shell). Migrate `SpeAdminApp/DashboardPage` to use new `<DataGrid />` instead. |
| F3 | Final UAT |

## 11. Non-impacts (explicit safety statements)

- **SemanticSearchControl PCF (`src/client/pcf/SemanticSearchControl/`) is NOT touched.** Its self-contained grid (ListView + ResultCard + CommandBar + FilterPanel) keeps shipping in v1.1.x. No imports change. No deployment side effect.
- **VisualHost PCF is NOT touched.** It continues to drill via `sprk_drillthroughtarget`. The only change is the *target* of two drill-throughs (Matter Health + Budget Performance) shift from entity names to Code Page web resources.
- **OOB sub-grids on entity forms are NOT replaced.** Spaarke views only appear in Custom Pages + workspace widgets.
- **Existing `sprk_gridconfiguration` records (Semantic Search)** stay readable during Phase A-D; Phase E migrates them. No multi-schema backward-compat code carried forward.

## 11.5 Implementation requirements (BINDING)

These are NOT design preferences — they are pre-Phase-A constraints every task must respect.

### 11.5.1 Use Fluent v9 native `DataGrid` features — do NOT hand-roll

The framework MUST build on `@fluentui/react-components` `DataGrid` and surface every feature it ships with — selection, sorting, column resize, keyboard navigation, density, sticky header, virtualization. The pattern is the same as `SemanticSearch/SearchResultsGrid.tsx` ([src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx](src/client/code-pages/SemanticSearch/src/components/SearchResultsGrid.tsx)) but generalized.

| Capability | Fluent v9 primitive | What we DO NOT write |
|---|---|---|
| Row selection (single, multi, "select all") | `<DataGrid selectionMode="multiselect" selectedItems onSelectionChange>` + `<DataGridSelectionCell />` | Custom `<input type="checkbox">` per row, manual select-all logic |
| Column sorting | `createTableColumn({ compare })` + `sortable: true` + `<DataGridHeaderCell>` (auto-renders sort indicator) | Custom sort arrows, click handlers on `<th>`, `useState<SortDirection>` |
| Column resize | `<DataGrid resizableColumns columnSizingOptions>` | Drag handles on `<th>`, ResizeObserver |
| Keyboard navigation | `<DataGrid focusMode="composite" \| "row_unstable">` | Manual `onKeyDown` switch on Arrow keys, Tab traps |
| Sticky header | Fluent v9 `DataGridHeader` is sticky-capable via its parent flex layout | Custom `position: sticky` on `<thead>` |
| Density | `<DataGrid size="extra-small" \| "small" \| "medium">` | Custom CSS variables for cell padding |
| Subtle row selection appearance | `<DataGrid subtleSelection>` (matches MDA visual) | Custom selected-row class |
| Row hover | Built-in via `DataGridRow` interactive state | Custom `:hover` rule |
| Column reorder | `<DataGrid columnSizingOptions>` (R2 — out of scope for R1 user customization per Q5) | — |

**Anti-pattern (do not repeat from `GridSection.tsx`)**: hand-rolled `<table>` with manual sort state, hand-coded checkbox column, custom sticky `<thead>`, custom sort arrows. That implementation predates Fluent v9 `DataGrid` adoption and is the primary reason `GridSection` is 1,795 lines.

**Storybook stories required** for the new `<DataGrid />` to demonstrate each built-in feature working — this is the acceptance gate for Phase A.

### 11.5.2 MDA Power Apps grid UI parity — EXACT match

The grid MUST visually match the Power Apps Model-Driven App native subgrid / main-view control. This is a hard requirement, not "approximate." Reference: the current `EventsPage` already targets this and ships several parity styles that MUST be lifted as design tokens / shared style constants in the framework:

| MDA spec | Value | Where it lives today |
|---|---|---|
| Cell font | **12px** `'Segoe UI', 'Segoe UI Web', Arial, sans-serif` | `GridSection.tsx` `styles.table` |
| Cell text weight | `tokens.fontWeightRegular` | `GridSection.tsx` `styles.td` |
| Cell padding | `10px 12px` | `GridSection.tsx` `styles.td` / `styles.th` |
| Header background | `tokens.colorNeutralBackground2` | `GridSection.tsx` `styles.tableHeader` |
| Header text | `tokens.colorNeutralForeground2` + `tokens.fontWeightSemibold` | `GridSection.tsx` `styles.th` |
| Header bottom border | `1px solid tokens.colorNeutralStroke1` | `GridSection.tsx` `styles.th` |
| Cell bottom border | `1px solid tokens.colorNeutralStroke2` | `GridSection.tsx` `styles.td` |
| Row hover background | `tokens.colorNeutralBackground1Hover` | `GridSection.tsx` `styles.tr` |
| Selected row background | `tokens.colorNeutralBackground1Selected` | `GridSection.tsx` `styles.trSelected` |
| Primary-name "open record" link color | `tokens.colorBrandForeground1`, no underline, semibold; underline on hover | `GridSection.tsx` `styles.eventNameLink` |
| Status badge appearance | Fluent v9 `<Badge appearance="filled\|outline\|tint">` (color picked from entity metadata `optionSet.color`) | `GridSection.tsx` `getStatusAppearance` |
| Outer container | `tokens.colorNeutralBackground1` + `1px solid tokens.colorNeutralStroke1` + `tokens.borderRadiusMedium` + `tokens.shadow4` | `EventsPage/App.tsx` `styles.listViewContainer` |
| Command bar container | Same border + radius + `tokens.shadow4` as List View | `EventsPage/App.tsx` `styles.commandBarWrapper` |
| Vertical gap between CommandBar and List View | `12px` (`shorthands.gap("12px")`) | `EventsPage/App.tsx` `styles.root` |
| Outer page padding | `12px 16px` | `EventsPage/App.tsx` `styles.root` |
| Column header dropdown chevron | Fluent v9 `MoreHorizontal20Regular` or `ChevronDown20Regular` (NOT a custom SVG) | `ColumnHeaderMenu.tsx` |
| Filter chip layout | `8px` gap between chips, label + dropdown trigger; `tokens.fontSizeBase200` | `EventsPage/App.tsx` `styles.filterToolbar` |

These are codified as shared design tokens in `@spaarke/ui-components/src/components/DataGrid/tokens.ts` (new file) so the framework, Storybook, and any consumer who needs custom cell rendering use ONE source of truth.

**Visual parity verification (Phase A acceptance gate)**:
- [ ] Side-by-side screenshot diff against a native MDA Events subgrid (light theme)
- [ ] Same diff in dark theme
- [ ] Same diff at 4 zoom levels (75% / 100% / 125% / 150%)
- [ ] No pixel-level regression vs. current `EventsPage` grid

### 11.5.3 CRUD reuse from EventsPage — lift, don't rewrite

The EventsPage already implements the four CRUD operations the framework needs as defaults. Lift them as-is into the framework's default command handlers (with minor generalization — replace `EVENT_ENTITY_NAME` with `entityName` from configjson, `sprk_eventstatus` with the field passed in `customHandlerId` payload).

| EventsPage source | Framework default command | Lift notes |
|---|---|---|
| [`openNewEventForm(parentContext?)` at App.tsx:461](src/solutions/EventsPage/src/App.tsx#L461) | `commandBar.action: "create-form"` | Generalize: `xrm.Navigation.openForm({ entityName, createFromEntity: parentContext })`. Parent-context pre-fill (when `parentContext` is present) ports directly. |
| [`deleteSelectedEvents(ids)` at App.tsx:687](src/solutions/EventsPage/src/App.tsx#L687) | `commandBar.action: "delete-selected"` | Generalize entity name. **REPLACE `window.confirm` with Fluent v9 `<Dialog>` confirmation** — confirmation pattern matches `@spaarke/ui-components/components/ChoiceDialog`. |
| [`executeBulkStatusUpdate(ids, status, label, ...)` at App.tsx:731](src/solutions/EventsPage/src/App.tsx#L731) | `commandBar.customHandlerId: "BulkUpdateField"` (registered framework handler) | Generic — pass `{ fieldName, value }` in the command's payload. Promise.all pattern stays. Global notification via `xrm.App.addGlobalNotification` stays. |
| [`openEventDetailPane(eventId, eventTypeId)` at App.tsx:375](src/solutions/EventsPage/src/App.tsx#L375) — side pane navigate | `rowOpen.type: "sidePane"` and `rowOpen.type: "webResource"` | Two-mode handler. Dialog-mode detection (the EventsPage record-link-not-opening bug) is fixed by the framework's `rowOpen.type: "webResource"` default handler, which uses `Xrm.Navigation.navigateTo({pageType:"webresource"...})` and works in dialog mode. |
| Calendar pane wiring (Xrm.App.sidePanes mutual exclusivity logic at App.tsx:375-453) | Stays in EventsPage host — NOT lifted | Host extension model §7 — Event-specific side pane mutual exclusivity stays with the host. |
| Filter chip components (`AssignedToFilter`, `RecordTypeFilter`, `StatusFilter`) | Replaced by auto-derived chips §8.1 | Retired. |

**BFF passthrough endpoints stay read-only in R1** (Q4 decision). Write operations route through `Xrm.WebApi` exclusively in R1. R2 introduces write passthrough if there's a non-MDA host that needs it.

### 11.5.4 `/fluent-v9-component` skill — BINDING for every UI task

Every task in this project that authors or modifies UI MUST invoke the `/fluent-v9-component` skill before writing code. This is a non-negotiable Step 0.5 per [src/client/pcf/CLAUDE.md](src/client/pcf/CLAUDE.md) §6.0 and `.claude/skills/fluent-v9-component/SKILL.md`.

**Specifically binding for this framework**:

| Skill checklist item | DataGrid-specific application |
|---|---|
| All colors / spacing / radius via `tokens.*` | Codified in `tokens.ts` per §11.5.2 |
| `makeStyles` at module scope | Every primitive |
| `mergeClasses(componentClasses, props.className)` — `props.className` LAST | Every public API surface |
| Shorthand properties via `shorthands.*` | `shorthands.border`, `shorthands.padding`, `shorthands.gap` everywhere |
| **Portal components paired with FluentProvider re-wrap OR `applyStylesToPortals={true}` on root** | **MANDATORY for `ColumnHeaderMenu`, `FilterChip` popovers, `Dialog` confirmations, `Menu` overflow.** The current `EventsPage` FluentProvider does NOT set `applyStylesToPortals` — this is the same gap surfaced in the surgical-fix backlog and gets resolved as part of Phase A. |
| PCF surfaces read `context.fluentDesignLanguage?.tokenTheme`; fall back to `webLightTheme` | N/A for R1 (Code Page hosts only); R2 if PCF host added |
| MDA disabled state uses `readOnly` + neutral-stroke override, NOT native `disabled` | Applies to row checkbox + command buttons when `selectionMode === "none"` |
| `Spaarke.UI.Components` is React-16-safe | DataGrid uses `JSXElement` from `@fluentui/react-components` (cross-version compat), no React-18-only hooks (no `useId`, no `useSyncExternalStore`) — framework must be PCF-droppable in R2 |
| No `teamsHighContrastTheme` | Windows High Contrast is automatic in Fluent v9; do not branch on it |

**Acceptance gate (Phase A close)**:
- [ ] `/fluent-v9-component` skill checklist all green per primitive
- [ ] axe a11y scan passes on Storybook stories for every primitive (`Toolbar/CommandBar`, `LookupMultiFilterChip`, `OptionSetMultiFilterChip`, `DateRangeFilterChip`, `TextFilterChip`, `BoolFilterChip`, `DataGrid` core)
- [ ] Dark mode parity screenshot for every primitive
- [ ] `applyStylesToPortals` verified on every popover-bearing primitive

## 12. Resolved decisions (locked-in for R1)

| # | Question | Decision |
|---|---|---|
| Q1 | Persist chip selections per-user across sessions? | **NO**. Session-only. (Per-user view state is R2.) |
| Q2 | Expose `useDataGridContext()` hook for host extensions? | **YES**. Hook returns `{ selectedIds, refresh, currentView, parentContext, dataverseClient, entityMetadata }`. Required by custom command handlers + secondary action handlers to reach grid state without prop drilling. |
| Q3 | Bulk-action commands — server-side OR client-side iteration? | **BOTH**. Default framework handlers (Delete, bulk status update) use **client-side `Promise.all` iteration** — same pattern as EventsPage `executeBulkStatusUpdate` ([App.tsx:731](src/solutions/EventsPage/src/App.tsx#L731)). Custom handlers can opt to call a single BFF endpoint instead. The framework supplies both `selectedIds` and full `records` arrays to handlers; handler decides. |
| Q4 | Does the existing Event grid have CRUD that can be reused? | **YES — lift directly into framework defaults.** See §11.5.3 for the harvest map. Framework writes use `Xrm.WebApi` (proven). `BffDataverseClient` stays read-only in R1; BFF write surface is R2. |
| Q5 | Column-level user customizations (show/hide, reorder, save view)? | **NO** for R1. Per §3 non-goals. R2 candidate. |
| Q6 | Density toggle (comfortable vs. compact)? | **YES, if easy** — Fluent v9 `DataGrid` ships with built-in density (`size` prop: `"extra-small" | "small" | "medium"`). Wire it through `display.densityDefault` in configjson + an optional toolbar toggle. Effectively free. |
| Q7 | Multi-select filter chip semantics — OR or AND? | **OR within a chip, AND across chips.** Standard query-builder semantics (matches MDA Advanced Find + the existing GridSection column filter behavior). |
| Q8 | "Export to Excel" — server-side OR client-side CSV? | **Client-side CSV** for R1. No BFF dependency, ships with the framework. Server-side Excel deferred to R2 if needed (would require Dataverse "Export to Excel" workflow). |

## 13. Success criteria

### Functional
- [ ] Matter Health + Budget Performance drill-through to working data grids, no per-entity TypeScript written
- [ ] EventsPage rewritten as a ~150-line host with no UX regression
- [ ] One `<DataGrid configId={...} />` works in: MDA Custom Page, SpaarkeAi workspace widget, standalone test harness (Storybook)
- [ ] Adding a new entity grid = create one `sprk_gridconfiguration` record + one savedquery + one ~50-line Code Page shell. No new TypeScript files in the framework.
- [ ] Zero impact on `SemanticSearchControl` PCF, VisualHost PCF, OOB grids
- [ ] BFF passthrough endpoints respect ADR-008 + ADR-028 + bff-extensions.md
- [ ] EventsPage record-link-not-opening bug closed (covered by §11.5.3 `rowOpen.type: "webResource"` default handler)

### Fluent v9 native exploitation (§11.5.1)
- [ ] Selection (single, multi, select-all) uses `<DataGrid selectionMode>` — no custom checkbox column
- [ ] Sort uses `createTableColumn({ compare })` + auto sort indicators — no custom sort arrows
- [ ] Column resize uses `resizableColumns` + `columnSizingOptions` — no custom drag handles
- [ ] Keyboard nav uses `focusMode` — no manual onKeyDown handlers
- [ ] Density toggle wired to `<DataGrid size>` — no custom CSS
- [ ] Storybook story per Fluent native feature, demonstrating it works in our wrapper

### MDA visual parity (§11.5.2)
- [ ] Design tokens codified in `@spaarke/ui-components/src/components/DataGrid/tokens.ts`
- [ ] Side-by-side screenshot diff vs. native MDA Events subgrid — light theme: PASS
- [ ] Same diff — dark theme: PASS
- [ ] Same diff at 75% / 100% / 125% / 150% zoom: PASS
- [ ] No pixel-level regression vs. current `EventsPage` grid

### Fluent v9 skill compliance (§11.5.4)
- [ ] `/fluent-v9-component` skill checklist green for every primitive
- [ ] axe a11y scan passes on every Storybook story
- [ ] `applyStylesToPortals={true}` verified on every popover-bearing primitive (`ColumnHeaderMenu`, all filter chips, `Dialog` confirmations, `Menu` overflow)
- [ ] All tokens via `tokens.*` — no raw hex anywhere in framework code
- [ ] React-16-safe (no `useId`, no `useSyncExternalStore`) per ADR-022 — framework droppable into PCF in R2

## 14. Related ADRs + constraints

- **ADR-012** — Shared component library: this work is the canonical example
- **ADR-021** — Fluent UI v9 + theming
- **ADR-022** — React versions: framework targets React 18+ in Code Page hosts; will be 16-safe via `JSXElement` typings for legacy PCF hosts if needed
- **ADR-028** — Spaarke Auth: `BffDataverseClient` uses `@spaarke/auth` `authenticatedFetch` ONLY
- **ADR-008** — Endpoint authorization filter pattern (BFF endpoints)
- **ADR-019** — ProblemDetails on BFF endpoint errors
- **`.claude/constraints/bff-extensions.md`** — Placement Justification (§9 above)
- **`.claude/skills/fluent-v9-component/SKILL.md`** — BINDING for every UI task in this project (§11.5.4). Must be invoked as Step 0.5 of every task that authors or modifies framework primitives or hosts.
- **`src/client/pcf/CLAUDE.md`** §6.0 — Fluent v9 mandate
- **`docs/architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`** — this work closes the "grid coupling gap" called out in §2A of that audit
- **`docs/architecture/SPAARKEAI-COMPONENT-MODEL.md`** — PaneEventBus contract (§7 host extension model aligns)
- **MDA Power Apps grid visual standard** — codified in §11.5.2 (no formal ADR; design tokens are the source of truth)

## 15. Risks

| Risk | Mitigation |
|---|---|
| Phase A is large; conflicting changes mid-flight | Develop on a sub-branch off matter-ui-r1; merge after each Phase boundary |
| EventsPage UX regression during Phase D | UAT all 4 EventsPage modes (system / dialog / embedded / standalone) against new framework before retiring GridSection |
| `Xrm.Utility.getEntityMetadata` is slower than expected | Cache metadata in module-level Map (entity metadata never changes at runtime) |
| BFF passthrough leaks Dataverse error shapes | Wrap in standard ProblemDetails (ADR-019) |
| Configuration drift: a `sprk_gridconfiguration` record stale vs. updated savedquery | Resolution order §6.4 fall-back chain handles this — metadata + layoutXml win when configjson references non-existent fields |
| Fluent v9 portal regression (popover not themed) | Storybook coverage for dark mode + applyStylesToPortals enforced in every primitive |

---

## Appendix: example `sprk_configjson` for `sprk_invoice` (Budget Performance drill-through)

```json
{
  "_version": "1.0",
  "source": { "type": "savedquery-set", "entityLogicalName": "sprk_invoice" },
  "display": {
    "title": "Invoices",
    "icon": "Money24Regular",
    "densityDefault": "comfortable"
  },
  "filterChips": {
    "mode": "auto",
    "showClearAll": true
  },
  "commandBar": {
    "primary": [
      { "id": "new",     "label": "+ New",   "icon": "Add24Regular",            "action": "create-form" },
      { "id": "refresh", "label": "Refresh", "icon": "ArrowClockwise24Regular", "action": "refresh" }
    ],
    "secondary": [
      { "id": "markpaid", "label": "Mark Paid", "icon": "CheckmarkCircle24Regular",
        "action": "custom", "customHandlerId": "MarkInvoicesPaid",
        "requiresSelection": "multi", "privilege": "Write" },
      { "id": "export",  "label": "Export", "icon": "ArrowDownload24Regular",   "action": "export-excel" }
    ]
  },
  "rowOpen": {
    "type": "navigateToForm",
    "passContext": ["parentMatterId"]
  },
  "secondaryActions": [
    { "id": "sprkchat", "label": "Ask about this invoice", "icon": "Sparkle24Regular",
      "kind": "ai-assistant", "aiAssistantId": "invoice-assistant",
      "visible": "row-hover" },
    { "id": "playbook-rev", "label": "Run review playbook", "icon": "Lightbulb24Regular",
      "kind": "playbook", "playbookId": "review-invoice-playbook",
      "requiresSelection": "single", "visible": "row-hover" }
  ]
}
```

## Appendix: example `sprk_configjson` for `sprk_event` (migrated EventsPage)

```json
{
  "_version": "1.0",
  "source": { "type": "savedquery-set", "entityLogicalName": "sprk_event" },
  "display": { "title": "Events", "icon": "CalendarLtr24Regular" },
  "filterChips": {
    "mode": "allowlist",
    "allowlist": ["_ownerid_value", "sprk_eventtype_ref", "sprk_eventstatus", "sprk_duedate"]
  },
  "commandBar": {
    "primary": [
      { "id": "new",     "label": "+ New",   "icon": "Add24Regular",            "action": "create-form" },
      { "id": "delete",  "label": "Delete",  "icon": "Delete24Regular",
        "action": "delete-selected", "requiresSelection": "multi", "privilege": "Delete" },
      { "id": "refresh", "label": "Refresh", "icon": "ArrowClockwise24Regular", "action": "refresh" }
    ]
  },
  "rowOpen": {
    "type": "webResource",
    "webResource": "sprk_eventdetailsidepane.html",
    "dataParams": ["sprk_eventid", "_sprk_eventtype_ref_value"]
  },
  "secondaryActions": [
    { "id": "sprkchat-event", "label": "Ask sprkchat", "icon": "Sparkle24Regular",
      "kind": "ai-assistant", "visible": "row-hover" }
  ]
}
```

Event-specific concerns the Events page host adds on TOP of the grid (not in configjson):
- Calendar pane in `Xrm.App.sidePanes` (mutual exclusivity with Event detail pane)
- Date filter from Calendar → merges into the grid's date-range chip state
- The `EventsPageContext` for cross-component event state (unchanged)
