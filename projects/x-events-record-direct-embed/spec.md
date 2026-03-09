# Context-Aware Events Page Embed

> **Project**: events-record-direct-embed
> **Date**: 2026-02-24
> **Status**: Specification
> **Branch**: work/events-workspace-apps-UX-r1

---

## Executive Summary

Adapt the existing `EventsPage.html` web resource to serve as a **context-aware, embedded Events tab** inside any Dataverse entity form — Matter, Project, Invoice, Work Assignment, or any future entity. When embedded, the grid auto-filters to events related to the current record, the view selector shows entity-specific views, side panes (Calendar + Event Detail) function correctly, and +New pre-fills the parent context. Side panes must close when the user navigates to a different tab in the host form.

This is **not a fork** — the existing EventsPage gains an "embedded mode" that activates when it detects parent-record context in its URL parameters.

---

## Requirements

### R1: Parse Embedded Context from URL

When EventsPage.html is placed on a form tab, Dataverse passes the record ID via the `data` parameter using the `{!entityid}` placeholder.

**Data parameter format** (configured on the web resource tab properties):

```
mode=embedded&entityName=sprk_matter&recordId={!entityid}
```

At runtime Dataverse replaces `{!entityid}` with the actual GUID, e.g.:

```
mode=embedded&entityName=sprk_matter&recordId=a1b2c3d4-e5f6-7890-abcd-ef1234567890
```

**Parsed context:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `mode` | `"embedded"` | Distinguishes from system-level Events page and drill-through dialog |
| `entityName` | `string` | Dataverse logical name of the host entity (e.g., `sprk_matter`) |
| `recordId` | `string` | GUID of the current record |

**Compatibility**: Existing modes (`dialog` for drill-through, absent for system page) continue to work unchanged.

### R2: Auto-Apply Context Filter

When `mode=embedded` and `recordId` is present:

- GridSection receives a `contextFilter` with `fieldName = "sprk_regardingrecordid"` and `value = recordId`
- FetchXML injection adds `<condition attribute="sprk_regardingrecordid" operator="eq" value="{recordId}" />`
- OData fallback adds `sprk_regardingrecordid eq '{recordId}'`

This reuses the existing `ContextFilter` interface and injection logic already built for drill-through.

### R3: Entity-Specific View Filtering

Instead of hardcoded view GUIDs, the ViewSelector discovers views dynamically based on a **naming convention**:

**Convention**: `{EntityPrefix}-{ViewName}`

| Entity | Prefix | Example Views |
|--------|--------|---------------|
| Matter | `Matter` | `Matter-All Events`, `Matter-All Tasks`, `Matter-All Tasks Open`, `Matter-Active Events` |
| Project | `Project` | `Project-All Events`, `Project-All Tasks` |
| Invoice | `Invoice` | `Invoice-All Events`, `Invoice-All Deadlines` |
| *(system)* | *(none)* | `Active Events`, `All Events`, `All Tasks`, `All Tasks Open` |

**Discovery mechanism**:

1. On mount (embedded mode), query Dataverse `savedquery` entity:
   ```
   GET /api/data/v9.2/savedqueries?$filter=returnedtypecode eq 'sprk_event'
     and startswith(name, '{EntityPrefix}-')
     and statecode eq 0
   &$select=savedqueryid,name,isdefault
   &$orderby=name
   ```
2. Map results to `IEventViewConfig[]` (same interface as static config)
3. If no entity-specific views found, fall back to the 4 system views from `eventConfig.ts`

**View creation** is a Dataverse admin task — no code needed. The naming convention is the contract.

### R4: Pre-Fill Parent Context on +New

When the user clicks "+New Event" in embedded mode:

- The `openQuickCreate()` call includes a `parentetwname` (parent entity name) and pre-filled lookup field:
  ```typescript
  Xrm.Navigation.openForm({
    entityName: "sprk_event",
    useQuickCreateForm: true,
    createFromEntity: {
      entityType: entityName,    // e.g., "sprk_matter"
      id: recordId,
      name: recordDisplayName    // fetched once on mount
    }
  });
  ```
- The `sprk_regardingrecordid` (polymorphic lookup) auto-populates with the parent record
- After creation, the grid refreshes (existing BroadcastChannel pattern)

### R5: Side Panes in Embedded Mode

Both Calendar and Event Detail side panes **work in embedded mode** (this reverses the earlier assessment that suggested skipping them).

**Behavior**:
- Calendar side pane registers as collapsible menu item (existing pattern)
- Event Detail side pane opens when user clicks a row (existing pattern)
- Calendar filter applies to the already-context-filtered grid (additive filtering)
- BroadcastChannel communication works across iframes (same origin)

### R6: Side Pane Tab-Switch Cleanup (System-Wide Feature)

**Critical requirement**: When a user clicks away from the Events tab to another tab in the host form (e.g., Overview, Contacts, Documents), all side panes opened by EventsPage must close.

**Detection strategy** (extends existing v2.17.0 navigation detection):

The existing `checkForNavigation()` URL-polling pattern (200ms interval) already watches for parent URL changes. For embedded mode, we extend this:

1. **Primary: `visibilitychange` on the web resource iframe** — when Dataverse hides the tab's iframe, the document fires `visibilitychange` with `document.visibilityState === "hidden"`. Close all side panes.

2. **Secondary: Parent URL polling** — the existing 200ms interval catches SPA-style navigation that doesn't trigger visibility events.

3. **Tertiary: `pagehide` / `beforeunload`** — existing handlers remain as safety net for hard navigation.

**Implementation**: Extract the cleanup logic into a reusable `useSidePaneLifecycle` hook that any web resource can use:

```typescript
interface SidePaneLifecycleOptions {
  paneIds: string[];           // IDs of panes to close
  onCleanup?: () => void;      // Additional cleanup callback
  enableVisibilityDetection?: boolean;  // Default: true in embedded mode
  enableUrlPolling?: boolean;   // Default: true
  pollingIntervalMs?: number;   // Default: 200
}

function useSidePaneLifecycle(options: SidePaneLifecycleOptions): void;
```

**Reuse**: This hook lives in a shared location so CalendarSidePane, EventDetailSidePane, and future side pane components can all use it. When EventsPage is embedded in Matter, Project, Invoice, or any other entity form, the hook automatically handles tab-switch cleanup.

### R7: Dataverse Form Tab Configuration

For each entity that embeds the Events page, an admin adds a **web resource tab** to the form:

| Setting | Value |
|---------|-------|
| Web Resource | `sprk_eventspage.html` |
| Name | `Events` (or `Calendar` — admin's choice) |
| Data | `mode=embedded&entityName=sprk_matter&recordId={!entityid}` |
| Restrict cross-frame scripting | **Unchecked** (required for Xrm access) |

This is a one-time configuration per entity form. No code deployment needed to add a new entity — only a form customization with the correct `entityName` value in the data parameter.

### R8: Flexible Entity Design

The entire system is entity-agnostic:

| Concern | How It's Entity-Agnostic |
|---------|--------------------------|
| Context parsing | `entityName` + `recordId` from URL — any entity works |
| Grid filtering | `sprk_regardingrecordid` is a polymorphic lookup — accepts any entity |
| View discovery | Name prefix convention — admin creates views per entity |
| +New pre-fill | `createFromEntity` API accepts any entity type |
| Side pane cleanup | Tab-switch detection is DOM/URL-based — entity-independent |

**Adding a new entity** requires only:
1. Add a web resource tab to the entity's form (R7)
2. Create entity-specific views with the naming prefix (optional — falls back to system views)

No code changes needed.

---

## Technical Approach

### Mode Detection

Extend `parseDrillThroughParams()` to recognize three modes:

| Mode | Condition | Behavior |
|------|-----------|----------|
| **system** | No `mode` param (or `mode` absent) | Full system Events page — current behavior |
| **dialog** | `mode=dialog` | Drill-through popup — no side panes, no calendar |
| **embedded** | `mode=embedded` | Context-aware tab — filtered grid, entity views, side pane cleanup |

### Data Flow (Embedded Mode)

```
Dataverse Form Tab
  └─ sprk_eventspage.html?data=mode=embedded&entityName=sprk_matter&recordId={!entityid}
       │
       ├─ parseDrillThroughParams()
       │    → mode: "embedded"
       │    → entityName: "sprk_matter"
       │    → recordId: "a1b2c3d4..."
       │
       ├─ IS_EMBEDDED_MODE = true
       │
       ├─ Context filter applied to GridSection
       │    → sprk_regardingrecordid eq 'a1b2c3d4...'
       │
       ├─ View discovery
       │    → Query savedquery WHERE name LIKE 'Matter-%'
       │    → Fall back to system views if none found
       │
       ├─ +New Event pre-fills sprk_regardingrecordid
       │
       ├─ Calendar side pane registers (collapsible)
       │
       ├─ Event detail side pane opens on row click
       │
       └─ useSidePaneLifecycle()
            → visibilitychange → close panes
            → URL polling → close panes
            → beforeunload → close panes
```

### UI Adjustments in Embedded Mode

| Element | System Mode | Embedded Mode |
|---------|------------|---------------|
| Command bar | Full (New, Refresh, Views, Excel) | Same — all actions available |
| View selector | 4 hardcoded system views | Entity-specific views (discovered) + system fallback |
| Calendar side pane | Always registered | Registered (collapsible) |
| Grid columns | Full column set | Same (view-driven) |
| Page title | "Events" | Hidden (form tab provides title) |

### Files to Modify

| File | Change |
|------|--------|
| `EventsPage/src/App.tsx` | Add embedded mode detection, context filter wiring, view discovery, +New pre-fill, `useSidePaneLifecycle` |
| `EventsPage/src/config/eventConfig.ts` | Add `discoverEntityViews()` function, entity prefix mapping |
| `EventsPage/src/components/GridSection.tsx` | No changes (contextFilter already supported) |
| `EventsPage/src/components/ViewToolbar.tsx` | Accept dynamic views array instead of static config |
| *New*: `shared/hooks/useSidePaneLifecycle.ts` | Reusable hook for tab-switch pane cleanup |

### Files NOT Changed

| File | Why |
|------|-----|
| `CalendarSidePane/` | Works as-is — receives filter messages from parent |
| `EventDetailSidePane/` | Works as-is — opens event by ID from parent |
| `GridSection.tsx` | ContextFilter interface + injection already built |
| `broadcastChannel.ts` | Message protocol unchanged |
| `sessionPersistence.ts` | Tab-switch persistence already works |

---

## Success Criteria

1. **Embedded in Matter form**: Events tab shows only events where `sprk_regardingrecordid = {matterId}`
2. **View selector**: Shows `Matter-*` views when available, falls back to system views
3. **+New Event**: Pre-fills `sprk_regardingrecordid` with current matter
4. **Calendar side pane**: Filters embedded grid by date range (additive)
5. **Event detail side pane**: Opens on row click, saves correctly
6. **Tab switch**: All side panes close when user clicks Overview, Contacts, etc.
7. **System page unchanged**: Events entity page continues to work as before
8. **Dialog mode unchanged**: Drill-through popup continues to work
9. **New entity in <5 min**: Adding Events tab to a new entity requires only form customization + optional views

---

## Out of Scope

- Creating the actual Dataverse system views (admin task)
- Modifying the `sprk_regardingrecordid` field schema (already exists as polymorphic lookup)
- Changes to CalendarSidePane or EventDetailSidePane codebases
- Mobile layout adaptations
- Row-level security (handled by Dataverse)
