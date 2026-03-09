# EventsPage Grid Migration Guide

> **Purpose**: Step-by-step guide for migrating EventsPage from bespoke `GridSection.tsx` (raw `<table>`) to the standardized `UniversalDatasetGrid` component.
>
> **Reference Implementation**: Semantic Search Code Page — completed this migration first.
>
> **Created**: 2026-02-26
> **Spec**: See `universal-dataset-grid-adaptation-spec.md` for full architecture.

---

## Overview

The EventsPage (`src/solutions/EventsPage/`) currently uses a **1709-line `GridSection.tsx`** with a raw HTML `<table>` and custom rendering logic. This guide migrates it to the shared `UniversalDatasetGrid` component, reducing code, standardizing patterns, and gaining built-in features (virtualization, field security, Card/List views, dark mode).

### What Changes

| Aspect | Before (Current) | After (Migrated) |
|--------|-----------------|-------------------|
| Grid component | Raw `<table>` in `GridSection.tsx` (1709 lines) | `<UniversalDatasetGrid>` from `@spaarke/ui-components` |
| Data fetching | FetchXML in GridSection | FetchXML passed to `headlessConfig` prop |
| Column definitions | `parseLayoutXml()` → `LayoutColumn[]` | `parseLayoutXml()` → `IDatasetColumn[]` (thin adapter) |
| Cell rendering | Bespoke inline renderers | `ColumnRendererService` + registered custom renderers |
| Column filtering | `ColumnHeaderMenu` (bespoke) | To be extracted to shared library (Phase 3) |
| Virtualization | None | Built-in (>1000 records auto-virtualizes) |

### What Does NOT Change

| Feature | Why |
|---------|-----|
| **Side panes** (`Xrm.App.sidePanes`) | Side panes are in `App.tsx`, not in the grid. The grid fires `onRecordClick(recordId)` and `App.tsx` decides to open a side pane. This callback pattern is identical in `UniversalDatasetGrid`. |
| **Calendar integration** | Calendar is a side pane web resource that communicates via `postMessage`. Unrelated to grid component. |
| **Command bar** (bulk status actions) | EventsPage uses its own `CommandBar` in `App.tsx`. Can optionally use `UniversalDatasetGrid`'s built-in `CommandToolbar`, but not required. |
| **View selector** | `ViewSelectorDropdown` stays in `App.tsx`. It provides `viewId` which drives column/data configuration — same pattern, just column output format changes. |
| **FetchXML construction** | `FetchXmlService.ts` continues to build queries. The FetchXML is passed to `headlessConfig.fetchXml`. |
| **Date filter injection** | `mergeDateFilterIntoFetchXml()` continues to work — it modifies FetchXML before passing to grid. |
| **Context filter** | Drill-through filters (e.g., `sprk_regardingrecordid`) are injected into FetchXML, not the grid. |

---

## Prerequisites

Before starting migration, verify the shared library has these features (implemented in Phase 1):

- [ ] `IExternalDataConfig` interface in `DatasetTypes.ts`
- [ ] `useExternalDataMode` hook in `hooks/`
- [ ] `registerRenderer()` / `unregisterRenderer()` API on `ColumnRendererService`
- [ ] Mode 3 (external data) routing in `UniversalDatasetGrid.tsx`

**For EventsPage specifically** (headless mode already exists):
- [ ] Confirm `useHeadlessMode` hook works with EventsPage FetchXML queries
- [ ] Confirm `ColumnRendererService` handles OData formatted values

---

## Migration Phases

### Phase 1: Register Custom Renderers (Low Risk)

Before touching the grid, register EventsPage-specific renderers. This can be done in `App.tsx` or a new `eventRenderers.ts` init file.

```typescript
// src/solutions/EventsPage/src/config/eventRenderers.ts

import { ColumnRendererService } from "@spaarke/ui-components";
import { Badge, Link } from "@fluentui/react-components";

/**
 * Register EventsPage-specific column renderers.
 * Call once at app initialization (before first render).
 */
export function registerEventRenderers(): void {

  // Event Name — clickable link that opens side pane
  // Note: The actual side pane logic is in onRecordClick, not here.
  // This renderer just makes the name look like a link.
  ColumnRendererService.registerRenderer("EventName", (value, record) => {
    if (!value) return "—";
    return (
      <Link style={{ cursor: "pointer" }}>
        {String(value)}
      </Link>
    );
  });

  // Regarding — navigable lookup link
  ColumnRendererService.registerRenderer("RegardingLink", (value, record) => {
    const name = record.sprk_regardingrecordname;
    if (!name) return "—";
    return (
      <Link style={{ cursor: "pointer" }}>
        {String(name)}
      </Link>
    );
  });

  // Event Status — colored badge
  ColumnRendererService.registerRenderer("EventStatus", (value, record, column) => {
    if (value == null) return "";

    const formattedValue = record[
      `${column.name}@OData.Community.Display.V1.FormattedValue`
    ] || String(value);

    // Status value → color mapping
    const colorMap: Record<number, string> = {
      0: "subtle",      // Draft
      1: "brand",       // Open
      2: "success",     // Completed
      3: "informative", // Closed
      4: "warning",     // On Hold
      5: "danger",      // Cancelled
      6: "informative", // Reassigned
      7: "subtle",      // Archived
    };

    const appearanceMap: Record<number, string> = {
      2: "filled",   // Completed
      3: "ghost",    // Closed
      5: "ghost",    // Cancelled
      7: "ghost",    // Archived
    };

    const color = colorMap[value as number] || "informative";
    const appearance = appearanceMap[value as number] || "tint";

    return (
      <Badge appearance={appearance as any} color={color as any}>
        {formattedValue}
      </Badge>
    );
  });

  // Priority — colored text badge
  ColumnRendererService.registerRenderer("EventPriority", (value, record, column) => {
    if (value == null) return "";

    const formattedValue = record[
      `${column.name}@OData.Community.Display.V1.FormattedValue`
    ] || String(value);

    const color = value === 1 ? "danger" : value === 3 ? "subtle" : "informative";

    return (
      <Badge appearance="outline" color={color as any}>
        {formattedValue}
      </Badge>
    );
  });
}
```

**Call at init**:
```typescript
// App.tsx — add to component mount or top-level
import { registerEventRenderers } from "./config/eventRenderers";
registerEventRenderers();
```

### Phase 2: Map LayoutColumn → IDatasetColumn

Create an adapter that converts the existing `parseLayoutXml()` output to `IDatasetColumn[]`:

```typescript
// src/solutions/EventsPage/src/adapters/columnAdapter.ts

import type { IDatasetColumn } from "@spaarke/ui-components";
import type { LayoutColumn } from "../services/FetchXmlService";

/** Map known event fields to their renderer data type. */
const FIELD_TYPE_MAP: Record<string, string> = {
  "sprk_eventname": "EventName",
  "sprk_regardingrecordname": "RegardingLink",
  "sprk_eventstatus": "EventStatus",
  "sprk_priority": "EventPriority",
  "sprk_duedate": "DateAndTime.DateOnly",
  "createdon": "DateAndTime.DateAndTime",
  "modifiedon": "DateAndTime.DateAndTime",
  // Lookups
  "ownerid": "Lookup.Simple",
  "sprk_eventtype_ref": "Lookup.Simple",
  "sprk_regardingrecordtype": "Lookup.Simple",
};

/**
 * Convert LayoutColumn[] (from parseLayoutXml) to IDatasetColumn[].
 *
 * This adapter bridges the existing Dataverse savedquery layoutXml
 * parsing with the UniversalDatasetGrid column format.
 */
export function mapLayoutColumnsToDatasetColumns(
  layoutColumns: LayoutColumn[]
): IDatasetColumn[] {
  return layoutColumns.map((lc) => ({
    name: lc.name,
    displayName: lc.label,
    dataType: FIELD_TYPE_MAP[lc.name] || (lc.isLookup ? "Lookup.Simple" : "SingleLine.Text"),
    visualSizeFactor: lc.width / 100,
    isKey: lc.name === "sprk_eventid",
    isPrimary: lc.name === "sprk_eventname",
  }));
}
```

### Phase 3: Replace GridSection Table with UniversalDatasetGrid

**Option A: Headless Mode** (recommended — grid fetches data via WebAPI)

```typescript
// In the parent component that currently renders <GridSection>:

import { UniversalDatasetGrid } from "@spaarke/ui-components";

// The enhanced FetchXML (with date filters, context filters injected)
const enhancedFetchXml = useMemo(() => {
  let xml = viewDefinition?.fetchXml || DEFAULT_FETCHXML;
  xml = mergeDateFilterIntoFetchXml(xml, calendarFilter);
  xml = injectContextFilter(xml, regardingRecordId);
  return xml;
}, [viewDefinition, calendarFilter, regardingRecordId]);

// Dynamic columns from view layoutXml
const gridColumns = useMemo(() => {
  if (dynamicColumns.length > 0) {
    return mapLayoutColumnsToDatasetColumns(dynamicColumns);
  }
  return mapLayoutColumnsToDatasetColumns(DEFAULT_COLUMNS);
}, [dynamicColumns]);

return (
  <UniversalDatasetGrid
    headlessConfig={{
      webAPI: Xrm.WebApi,
      entityName: "sprk_event",
      fetchXml: enhancedFetchXml,
      pageSize: 50,
    }}
    config={{
      viewMode: "Grid",
      selectionMode: "Multiple",
      showToolbar: false,  // EventsPage has its own CommandBar
      enabledCommands: [],
      scrollBehavior: "Infinite",
      rowHeight: 44,
      enableVirtualization: true,
      theme: "Auto",
    }}
    selectedRecordIds={selectedEventIds}
    onSelectionChange={handleSelectionChange}
    onRecordClick={handleEventClick}  // Opens side pane — unchanged
    context={context}
  />
);
```

**Option B: External Data Mode** (if you need full control of fetching)

If you need to keep the existing `fetchEvents()` logic (e.g., for mock data support, custom OData fallback, or complex filter building), use external data mode instead:

```typescript
// Map fetched events to IDatasetRecord
const gridRecords = useMemo(
  () => events.map(evt => ({
    id: evt.sprk_eventid,
    entityName: "sprk_event",
    ...evt,  // All OData fields + formatted values pass through
  })),
  [events]
);

return (
  <UniversalDatasetGrid
    externalConfig={{
      records: gridRecords,
      columns: gridColumns,
      loading: isLoading,
      hasNextPage: hasMoreEvents,
      onLoadNextPage: loadMoreEvents,
      onRefresh: refreshEvents,
    }}
    config={gridConfig}
    selectedRecordIds={selectedEventIds}
    onSelectionChange={handleSelectionChange}
    onRecordClick={handleEventClick}
  />
);
```

### Phase 4: Remove Old Code

Once migration is verified:
1. Delete inline rendering logic from `GridSection.tsx` (status badges, priority colors, etc.)
2. Keep `GridSection.tsx` as a thin wrapper or inline the `UniversalDatasetGrid` into the parent
3. `ColumnHeaderMenu` — keep using the existing one until it's extracted to shared library

---

## Side Panes — No Migration Needed

The `Xrm.App.sidePanes` integration is entirely in `App.tsx` functions:

| Function | Location | What it Does |
|----------|----------|-------------|
| `openEventDetailPane(eventId, formId)` | `App.tsx:330` | Creates/navigates side pane for event detail form |
| `closeEventDetailPane()` | `App.tsx:440` | Closes event detail side pane |
| `registerCalendarPane()` | `App.tsx:493` | Registers calendar as side pane on page load |
| `openCalendarPane()` | `App.tsx:539` | Opens calendar side pane, closes event pane |
| `closeCalendarPane()` | `App.tsx:569` | Closes calendar side pane |

These are called from `handleEventClick` (the `onRecordClick` handler), NOT from inside the grid. The grid only fires the click event with a `recordId`. This pattern is identical for `UniversalDatasetGrid`:

```typescript
// Before migration:
<GridSection onEventClick={handleEventClick} />
// handleEventClick calls openEventDetailPane(eventId, formId)

// After migration:
<UniversalDatasetGrid onRecordClick={handleEventClick} />
// handleEventClick still calls openEventDetailPane(eventId, formId)
// Zero changes needed to side pane logic.
```

---

## Column Filtering — Phased Approach

The EventsPage has `ColumnHeaderMenu.tsx` (597 lines) for per-column filtering with text/choice/date filter types.

**Immediate**: Keep using the EventsPage `ColumnHeaderMenu` directly — it renders in the header area outside the grid, so it can coexist.

**Future (shared library Phase 4)**: Extract `ColumnHeaderMenu` into `@spaarke/ui-components` as `DatasetGrid/ColumnHeaderMenu.tsx`. Then both EventsPage and other consumers can use it. The `GridView.tsx` header cell rendering would need a hook point for custom header menus.

---

## Dataverse View-Driven Columns via sprk_gridconfiguration

**Optional Enhancement**: Instead of (or in addition to) parsing `savedquery.layoutXml`, EventsPage can also store view definitions in `sprk_gridconfiguration`:

```json
{
  "_type": "events-page-view",
  "_version": 1,
  "domain": "sprk_event",
  "columns": [
    { "key": "sprk_eventname", "label": "Event Name", "width": 200, "dataType": "EventName" },
    { "key": "sprk_duedate", "label": "Due Date", "width": 150, "dataType": "DateAndTime.DateOnly" },
    { "key": "ownerid", "label": "Owner", "width": 150, "dataType": "Lookup.Simple" },
    { "key": "sprk_eventstatus", "label": "Status", "width": 100, "dataType": "EventStatus" },
    { "key": "sprk_priority", "label": "Priority", "width": 100, "dataType": "EventPriority" },
    { "key": "sprk_eventtype_ref", "label": "Event Type", "width": 120, "dataType": "Lookup.Simple" }
  ],
  "defaultSort": { "column": "sprk_duedate", "direction": "desc" }
}
```

**Benefits**: Admin-editable columns without code changes. Consistent with Semantic Search pattern.

**When to adopt**: After the core grid migration is stable. This is an enhancement, not a requirement.

---

## Migration Checklist

### Pre-Migration
- [ ] Shared library Phase 1 changes are available (external data mode, renderer registration)
- [ ] Team is familiar with `IDatasetRecord` and `IDatasetColumn` interfaces
- [ ] Custom renderer functions are written and tested

### Phase 1: Custom Renderers
- [ ] Create `config/eventRenderers.ts` with EventName, EventStatus, EventPriority, RegardingLink renderers
- [ ] Call `registerEventRenderers()` in `App.tsx` initialization
- [ ] Verify renderers work by testing with mock data

### Phase 2: Column Adapter
- [ ] Create `adapters/columnAdapter.ts` with `mapLayoutColumnsToDatasetColumns()`
- [ ] Map all known event fields to appropriate data types
- [ ] Verify `DEFAULT_COLUMNS` maps correctly

### Phase 3: Replace Grid
- [ ] Choose headless mode or external data mode (see options above)
- [ ] Replace `<GridSection>` with `<UniversalDatasetGrid>`
- [ ] Verify `onRecordClick` → side pane flow works unchanged
- [ ] Verify selection → bulk actions work unchanged
- [ ] Verify infinite scroll / pagination works
- [ ] Verify column sorting works
- [ ] Test with 500+ events for performance
- [ ] Feature-flag if needed: `const USE_UNIVERSAL_GRID = true;`

### Phase 4: Cleanup
- [ ] Remove old rendering logic from `GridSection.tsx`
- [ ] Remove old `ColumnHeaderMenu` integration (or keep alongside for now)
- [ ] Update EventsPage CLAUDE.md with new architecture notes
- [ ] Run code review + ADR check

### Verification
- [ ] All existing event operations work (open, edit, bulk status change)
- [ ] Calendar side pane interactions work (date filter, mutual exclusivity)
- [ ] Event detail side pane opens/closes correctly
- [ ] Dark mode renders correctly
- [ ] View switching (Active Events, All Events, etc.) works
- [ ] Column-level filtering works (if kept)
- [ ] Performance acceptable with 500+ events

---

## Files Reference

| File | Purpose | Action |
|------|---------|--------|
| `EventsPage/src/components/GridSection.tsx` | Current grid (1709 lines) | Replace with UniversalDatasetGrid |
| `EventsPage/src/components/ColumnHeaderMenu.tsx` | Column filtering (597 lines) | Keep or extract to shared lib |
| `EventsPage/src/App.tsx` | App shell + side panes | Minor changes (import, init renderers) |
| `EventsPage/src/config/eventConfig.ts` | View GUIDs, event type mappings | No changes |
| `EventsPage/src/services/FetchXmlService.ts` | FetchXML building, parseLayoutXml | No changes |
| `shared/Spaarke.UI.Components/...` | Shared library | Already updated (Phase 1) |

---

*This guide assumes the shared library Phase 1 changes are complete. Follow the Semantic Search implementation as the reference pattern.*
