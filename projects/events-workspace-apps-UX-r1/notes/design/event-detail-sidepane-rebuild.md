# EventDetailSidePane Rebuild: HTML Web Resource Approach

> **Date**: 2026-02-19
> **Status**: Implementation Complete
> **Branch**: work/events-workspace-apps-UX-r1
> **Phase**: 10 (OOB Visual Parity) → Phase 11 (Side Pane Modernization)

---

## Executive Summary

Replace the OOB Power Apps `entityrecord` side pane with a React HTML web resource side pane, using the existing EventDetailSidePane solution (v1.0.6) as the foundation. Add BroadcastChannel communication, persistence across tab switches, navigation-aware cleanup, and extract reusable scaffolding for platform-wide side pane support.

---

## Current State

### What Exists Today

| Component | Location | Version | Technology |
|-----------|----------|---------|------------|
| **EventsPage** | `src/solutions/EventsPage/` | 2.17.0+ | React 18 + Vite Custom Page |
| **CalendarSidePane** | `src/solutions/CalendarSidePane/` | 1.0.6 | React 18 + Vite single-file HTML |
| **EventDetailSidePane** | `src/solutions/EventDetailSidePane/` | 1.0.6 | React 18 + Vite single-file HTML |
| **OOB Form Enhancer** | `src/client/webresources/js/sprk_event_sidepane_form.js` | 1.30.0 | Vanilla JS injected into OOB forms |
| **Event Type Config** | `sprk_eventtype.sprk_fieldconfigjson` | — | Dataverse field (created) |

### Current Side Pane Approach in EventsPage

```
EventsPage (App.tsx)
├── registerCalendarPane()       → pageType: "webresource" ✅ (working)
├── openEventDetailPane()        → pageType: "entityrecord" ❌ (to be replaced)
│   └── uses getFormGuidForEventType() to pick OOB form
├── closeCalendarPane()          → mutual exclusivity
├── closeEventDetailPane()       → mutual exclusivity
└── closeAllSidePanes()          → navigation cleanup
```

### Existing EventDetailSidePane Components (Ready to Use)

```
src/solutions/EventDetailSidePane/src/
├── App.tsx                    # Root: sections + dirty tracking + save + read-only
├── main.tsx                   # React 18 createRoot entry
├── components/
│   ├── HeaderSection.tsx      # Event name (inline edit), type badge, parent link, close
│   ├── StatusSection.tsx      # Status reason (segmented buttons) — always visible
│   ├── KeyFieldsSection.tsx   # Due date, priority, owner — always visible
│   ├── DatesSection.tsx       # Base date, final due date, remind at — EVENT TYPE CONDITIONAL
│   ├── DescriptionSection.tsx # Multiline text editor — EVENT TYPE CONDITIONAL
│   ├── RelatedEventSection.tsx# Related event lookup — EVENT TYPE CONDITIONAL
│   ├── HistorySection.tsx     # Audit trail (lazy loaded) — EVENT TYPE CONDITIONAL
│   ├── CollapsibleSection.tsx # Reusable accordion (controlled + uncontrolled)
│   ├── Footer.tsx             # Save button + messages + rollback actions
│   └── UnsavedChangesDialog.tsx # Save/discard/cancel before closing
├── hooks/
│   ├── useEventTypeConfig.ts  # 566 lines — reads sprk_fieldconfigjson from Event Type
│   ├── useOptimisticUpdate.ts # Save with rollback, grid callback notification
│   └── useRecordAccess.ts     # RetrievePrincipalAccess for write permission
├── services/
│   ├── eventService.ts        # Load/save Event records via Xrm.WebApi
│   └── sidePaneService.ts     # Close side pane, navigate to parent
├── types/
│   └── EventRecord.ts         # IEventRecord interface, enums, field constants
├── utils/
│   └── parseParams.ts         # Parse eventId/eventType from URL params + data= encoding
└── providers/
    └── ThemeProvider.ts        # Dark mode auto-detection
```

### Key Hook: useEventTypeConfig

Reads `sprk_eventtype.sprk_fieldconfigjson` and returns:
- `isSectionVisible(sectionName)` — controls section rendering
- `isFieldVisible(fieldName)` — controls field rendering
- `isFieldRequired(fieldName)` — controls required validation
- `getSectionCollapseState(sectionName)` — default expand/collapse

**Config JSON structure** (stored on each Event Type record):
```json
{
  "visibleFields": ["sprk_eventname", "sprk_duedate"],
  "hiddenFields": ["sprk_location"],
  "requiredFields": ["sprk_duedate"],
  "optionalFields": ["sprk_description"],
  "hiddenSections": ["relatedEvent", "history"],
  "sectionDefaults": {
    "dates": "expanded",
    "description": "collapsed"
  }
}
```

**Priority cascade**: hiddenFields → visibleFields → optionalFields → requiredFields (highest)

---

## Design: What Changes

### Phase 1: Wire EventDetailSidePane into EventsPage

**Goal**: Replace OOB `entityrecord` pane with `webresource` pane.

#### Changes to `EventsPage/src/config/eventConfig.ts`

```diff
+ /** Event detail side pane web resource name */
+ export const EVENT_DETAIL_WEB_RESOURCE_NAME = "sprk_eventdetailsidepane.html";

  // EVENT_TYPE_FORM_MAPPINGS — keep for reference but no longer used by side pane
  // (still used if we ever need to open full form via Xrm.Navigation.openForm)
```

#### Changes to `EventsPage/src/App.tsx` — `openEventDetailPane()`

**Before** (current):
```typescript
const navigationOptions = {
  pageType: "entityrecord",
  entityName: EVENT_ENTITY_NAME,
  entityId: cleanEventId,
  formId: formId,
};
```

**After** (new):
```typescript
const navigationOptions = {
  pageType: "webresource",
  webresourceName: EVENT_DETAIL_WEB_RESOURCE_NAME,
  data: `eventId=${cleanEventId}&eventType=${eventTypeId || ""}`,
};
```

**Key behaviors to preserve**:
- Mutual exclusivity with Calendar pane (already works via close/re-register pattern)
- Calendar pane re-registration when Event pane opens
- `closeAllSidePanes()` on navigation away (already handles both pane IDs)

#### Parameter Passing

The EventDetailSidePane's `parseParams.ts` already handles the `data=` query string format:
```typescript
// Already supports: ?data=eventId=xxx&eventType=yyy
if (dataParam.includes("=")) {
  const dataParams = new URLSearchParams(dataParam);
  eventId = dataParams.get("eventId");
  eventType = dataParams.get("eventType");
}
```

No changes needed to `parseParams.ts`.

---

### Phase 2: Persistence & Context Awareness

**Problem**: When the user switches between Calendar tab and Event tab in the side pane menu, the web resource iframe gets recreated. This loses:
- Current edit state (dirty fields)
- Section expand/collapse state
- Scroll position
- Footer messages

**Solution**: BroadcastChannel communication + sessionStorage persistence.

#### 2A. BroadcastChannel Integration (EventDetailSidePane → EventsPage)

Create `src/solutions/EventDetailSidePane/src/utils/broadcastChannel.ts`:

```typescript
const EVENTS_CHANNEL_NAME = "spaarke-events-page-channel";

// Message types
export const EVENT_DETAIL_MESSAGE_TYPES = {
  EVENT_SAVED: "EVENT_DETAIL_SAVED",          // Event was saved
  EVENT_OPENED: "EVENT_DETAIL_OPENED",        // Side pane loaded an event
  EVENT_CLOSED: "EVENT_DETAIL_CLOSED",        // Side pane closing
  EVENT_DIRTY_STATE: "EVENT_DETAIL_DIRTY",    // Dirty state changed
} as const;

// Outbound: Notify parent when event is saved
export function sendEventSaved(eventId: string, updatedFields: Record<string, unknown>): void;

// Outbound: Notify parent when event is opened
export function sendEventOpened(eventId: string, eventTypeId: string): void;

// Outbound: Notify parent of dirty state (for unsaved changes warning)
export function sendDirtyStateChanged(eventId: string, isDirty: boolean): void;

// Inbound: Listen for navigation commands from parent
export function setupEventDetailListener(
  onNavigateToEvent: (eventId: string, eventTypeId: string) => void
): () => void;
```

#### 2B. SessionStorage Persistence (Survive Tab Switching)

**Problem detail**: Xrm.App.sidePanes with `webresource` pageType destroys and recreates the iframe when switching between side pane tabs. The URL parameters are preserved by Dataverse, but React state is lost.

**Strategy**: Persist edit state to sessionStorage on every change. Restore on mount if the same eventId is being loaded.

Create `src/solutions/EventDetailSidePane/src/utils/sessionPersistence.ts`:

```typescript
const SESSION_KEY = "sprk_eventdetail_state";

interface PersistedState {
  eventId: string;
  currentValues: Partial<IEventRecord>;
  sectionStates: {
    datesExpanded?: boolean;
    relatedEventExpanded?: boolean;
    descriptionExpanded?: boolean;
    historyExpanded?: boolean;
  };
  scrollPosition: number;
  timestamp: number; // For staleness check
}

// Save state (called on every field change, debounced)
export function persistState(state: PersistedState): void;

// Restore state (called on mount, returns null if stale or different eventId)
export function restoreState(eventId: string): PersistedState | null;

// Clear state (called on save success or close)
export function clearPersistedState(): void;
```

**Integration into App.tsx**:

```typescript
// On mount: Check for persisted state
React.useEffect(() => {
  if (params.eventId) {
    const persisted = restoreState(params.eventId);
    if (persisted) {
      // Restore field values (but still fetch fresh from server for comparison)
      setCurrentValues(persisted.currentValues);
      setDatesExpanded(persisted.sectionStates.datesExpanded);
      // ... etc
      console.log("[EventDetailSidePane] Restored persisted state for", params.eventId);
    }
  }
}, [params.eventId]);

// On field change: Persist (debounced 300ms)
React.useEffect(() => {
  const timer = setTimeout(() => {
    if (params.eventId) {
      persistState({
        eventId: params.eventId,
        currentValues,
        sectionStates: { datesExpanded, relatedEventExpanded, ... },
        scrollPosition: contentRef.current?.scrollTop ?? 0,
        timestamp: Date.now(),
      });
    }
  }, 300);
  return () => clearTimeout(timer);
}, [currentValues, datesExpanded, ...]);

// On save success: Clear persisted state
// On close: Clear persisted state
```

**Staleness**: Discard persisted state older than 30 minutes.

#### 2C. EventsPage Navigation-Away Cleanup (Already Exists)

The existing `closeAllSidePanes()` in EventsPage handles:
- `beforeunload` event
- `pagehide` event
- Parent URL polling (200ms interval)
- `hashchange` / `popstate` on parent window
- React cleanup on unmount

**Enhancement needed**: When closing the Event pane via `closeAllSidePanes()`, also clear the sessionStorage for the Event detail state to prevent stale restoration when returning:

```typescript
// In closeAllSidePanes():
try {
  sessionStorage.removeItem("sprk_eventdetail_state");
  sessionStorage.removeItem(CALENDAR_FILTER_STATE_KEY);
} catch (err) { /* ignore */ }
```

#### 2D. EventsPage Grid Update on Event Save

**Current**: OOB form saves trigger Dataverse's built-in grid refresh.
**New**: React side pane must explicitly notify EventsPage to refresh.

**Approach**: BroadcastChannel message `EVENT_DETAIL_SAVED`.

In EventsPage, add listener:
```typescript
React.useEffect(() => {
  const channel = new BroadcastChannel(EVENTS_CHANNEL_NAME);
  channel.onmessage = (event) => {
    if (event.data?.type === "EVENT_DETAIL_SAVED") {
      console.log("[EventsPage] Event saved, refreshing grid");
      refreshGrid();
    }
  };
  return () => channel.close();
}, [refreshGrid]);
```

#### 2E. In-Place Navigation (Switch Events Without Closing Pane)

**Current OOB behavior**: `existingPane.navigate(newOptions)` reloads the form for a different event.
**New behavior**: Need to handle re-navigation when user clicks a different grid row while the pane is open.

Two approaches:
1. **Re-navigate the pane** (simple): `existingPane.navigate()` with new URL params → iframe reloads
2. **BroadcastChannel navigation** (no reload): Send message to existing pane to switch events

**Recommended**: Option 1 for simplicity. The iframe reload is fast (~200ms for single-file HTML), and sessionStorage ensures we handle the transition cleanly:
- Before navigate: Clear persisted state (old event)
- Side pane reloads with new eventId → fetches fresh data

```typescript
// In openEventDetailPane():
if (existingPane) {
  // Clear any persisted state for the OLD event before switching
  try { sessionStorage.removeItem("sprk_eventdetail_state"); } catch { /* */ }

  await existingPane.navigate({
    pageType: "webresource",
    webresourceName: EVENT_DETAIL_WEB_RESOURCE_NAME,
    data: `eventId=${cleanEventId}&eventType=${eventTypeId || ""}`,
  });
  existingPane.select();
}
```

---

### Phase 3: Reusable Scaffolding for Spaarke Platform

**Goal**: Extract patterns so that Matter side pane, Project side pane, etc. follow the same architecture.

#### 3A. Shared Hooks → `@spaarke/ui-components`

| Hook | Purpose | Generalization |
|------|---------|----------------|
| `useEntityTypeConfig<T>` | Read config JSON from any entity's type record | Replace `useEventTypeConfig` with generic version |
| `useDirtyFields<T>` | Track field changes, compute dirty state, build PATCH payload | Extract from `eventService.getDirtyFields()` |
| `useOptimisticSave` | Save with rollback, grid notification | Extract from `useOptimisticUpdate` |
| `useRecordAccess` | Check write permission, compute read-only | Already generic enough — move as-is |
| `useSidePanePersistence` | SessionStorage persist/restore for tab switching | New, extracted from Phase 2 work |

**Generic useEntityTypeConfig signature**:
```typescript
function useEntityTypeConfig<TConfig>(options: {
  entityName: string;          // e.g., "sprk_eventtype", "sprk_mattertype"
  recordId: string | undefined;
  configFieldName: string;     // e.g., "sprk_fieldconfigjson", "sprk_sidepaneconfigjson"
  selectFields: string;        // OData $select fields
  defaultConfig: TConfig;      // Fallback when no config found
}): {
  isLoading: boolean;
  error: string | null;
  config: TConfig | null;
  // Entity-specific helpers provided via configResolver parameter
};
```

#### 3B. Shared Components → `@spaarke/ui-components`

| Component | Purpose | Already Exists? |
|-----------|---------|-----------------|
| `SidePaneShell` | Layout: header + scrollable body + sticky footer | New (extract from App.tsx pattern) |
| `CollapsibleSection` | Accordion section with expand/collapse | Yes (move from EventDetailSidePane) |
| `SidePaneFooter` | Save button + messages + rollback | Generalize from `Footer.tsx` |
| `UnsavedChangesDialog` | Confirm save/discard/cancel | Yes (move as-is) |
| `ReadOnlyBanner` | Permission-based read-only indicator | Extract from App.tsx |

**SidePaneShell usage pattern**:
```tsx
<SidePaneShell
  header={<HeaderSection ... />}
  footer={<SidePaneFooter isDirty={isDirty} onSave={save} />}
  isReadOnly={isReadOnly}
  readOnlyMessage="You do not have permission to edit this record"
>
  {/* Scrollable content — entity-specific sections */}
  <StatusSection ... />
  <KeyFieldsSection ... />
  {config.isSectionVisible("dates") && <DatesSection ... />}
</SidePaneShell>
```

#### 3C. Side Pane Communication Protocol

Standardize the BroadcastChannel message format across all side panes:

```typescript
interface SidePaneMessage {
  source: string;              // "event-detail" | "calendar" | "matter-detail" | ...
  type: string;                // "SAVED" | "OPENED" | "CLOSED" | "DIRTY_STATE" | "FILTER_CHANGED"
  payload: Record<string, unknown>;
}
```

Channel naming: `spaarke-${pageName}-channel` (e.g., `spaarke-events-page-channel`)

#### 3D. Vite Build Template

Standardize the Vite + single-file approach across all side panes:

```
vite.config.ts template:
- viteSingleFile() plugin
- assetsInlineLimit: 100000000
- manualChunks: undefined
- base: "./"
- sourcemap: false
```

This is already consistent between CalendarSidePane and EventDetailSidePane.

---

## Task Breakdown

### Phase 1: Wire EventDetailSidePane (Tasks 100-102)

| Task | Description | Files Modified | Effort |
|------|-------------|----------------|--------|
| **100** | Update EventsPage to use webresource pageType for Event pane | `EventsPage/src/App.tsx`, `EventsPage/src/config/eventConfig.ts` | 1-2 hrs |
| **101** | Build & deploy EventDetailSidePane as web resource | `EventDetailSidePane/` → build → deploy `sprk_eventdetailsidepane.html` | 1 hr |
| **102** | Verify parameter passing and Event Type config loading | Manual testing: click grid rows, verify correct sections per Event Type | 1 hr |

### Phase 2: Persistence & Communication (Tasks 103-106)

| Task | Description | Files Modified | Effort |
|------|-------------|----------------|--------|
| **103** | Add BroadcastChannel communication to EventDetailSidePane | New: `EventDetailSidePane/src/utils/broadcastChannel.ts`, modify `App.tsx` | 2-3 hrs |
| **104** | Add sessionStorage persistence for tab-switch survival | New: `EventDetailSidePane/src/utils/sessionPersistence.ts`, modify `App.tsx` | 2-3 hrs |
| **105** | Add grid refresh listener in EventsPage for EVENT_SAVED messages | `EventsPage/src/App.tsx` (add BroadcastChannel listener) | 1 hr |
| **106** | Update navigation cleanup for sessionStorage + side pane state | `EventsPage/src/App.tsx` (enhance `closeAllSidePanes`) | 1 hr |

### Phase 3: Reusable Scaffolding (Tasks 107-110)

| Task | Description | Files Modified | Effort |
|------|-------------|----------------|--------|
| **107** | Extract useEntityTypeConfig generic hook to shared library | New: `shared/Spaarke.UI.Components/src/hooks/useEntityTypeConfig.ts` | 2-3 hrs |
| **108** | Extract SidePaneShell, CollapsibleSection, SidePaneFooter to shared | New/Move to `shared/Spaarke.UI.Components/src/components/` | 2-3 hrs |
| **109** | Extract useDirtyFields, useOptimisticSave to shared hooks | New: `shared/Spaarke.UI.Components/src/hooks/` | 2 hrs |
| **110** | Refactor EventDetailSidePane to consume shared components/hooks | `EventDetailSidePane/src/` → import from `@spaarke/ui-components` | 2-3 hrs |

### Verification (Task 111)

| Task | Description | Effort |
|------|-------------|--------|
| **111** | End-to-end testing: all Event Types, tab switching, navigation, save | 2-3 hrs |

---

## Persistence & Context Awareness: Detailed Scenarios

### Scenario 1: User Switches Between Calendar and Event Tabs

```
1. User clicks grid row → Event pane opens (eventId=abc, Calendar deselected)
2. User edits "Due Date" field (dirty state)
3. User clicks Calendar icon in side pane menu
   → Calendar pane selected, Event pane deselected (NOT closed)
   → EventDetailSidePane saves dirty state to sessionStorage
4. User interacts with Calendar, selects date filter
5. User clicks Event icon in side pane menu
   → Event pane re-selected
   → IF iframe was preserved: state intact (nothing to do)
   → IF iframe was recreated: restore from sessionStorage
   → Dirty state restored, user continues editing
```

**Key insight**: Dataverse may or may not destroy the iframe when deselecting a pane. SessionStorage handles both cases safely.

### Scenario 2: User Clicks Different Grid Row While Event Pane Open

```
1. Event pane showing eventId=abc (has unsaved changes)
2. User clicks grid row for eventId=xyz
   → EventsPage calls openEventDetailPane("xyz", typeId)
   → IF dirty: show UnsavedChangesDialog (Save/Discard/Cancel)
   → IF not dirty: navigate pane to new event
3. After navigation:
   → Clear sessionStorage for old event
   → Pane reloads with new eventId from URL params
   → Fetch fresh data from Dataverse
```

**Enhancement needed in EventsPage**: Before navigating to a new event, check dirty state via BroadcastChannel:

```typescript
// EventsPage sends: "Are you dirty?"
// EventDetailSidePane responds: "Yes, eventId=abc has unsaved changes"
// EventsPage shows confirm dialog OR lets EventDetailSidePane handle it
```

**Simpler approach**: Let EventDetailSidePane handle its own unsaved changes dialog internally. When the iframe navigates away, `beforeunload` fires → if dirty, persist to sessionStorage. Next load checks: if persisted eventId !== new eventId → discard persisted state.

### Scenario 3: User Navigates Away from Events Module

```
1. Event pane open with unsaved changes
2. User clicks "Matters" in site map (navigates away from Events)
   → EventsPage's closeAllSidePanes() fires (via URL polling or beforeunload)
   → Both panes closed
   → sessionStorage cleared for both Event and Calendar state
3. User returns to Events module later
   → Clean slate: Calendar registers, no Event pane open
```

### Scenario 4: User Closes Event Pane with X Button

```
1. Event pane open with unsaved changes
2. User clicks X button in side pane header
   → EventDetailSidePane's handleCloseRequest() fires
   → IF dirty: UnsavedChangesDialog (Save/Discard/Cancel)
   → IF "Save": save changes, close pane, clear sessionStorage
   → IF "Discard": close pane, clear sessionStorage
   → IF "Cancel": return to editing
```

---

## Dependencies & Risks

| Risk | Mitigation |
|------|------------|
| Xrm.App.sidePanes iframe lifecycle varies by Dataverse version | SessionStorage persistence handles both preserve and recreate scenarios |
| BroadcastChannel not supported in IE11 | Not a concern — Dataverse requires Edge/Chrome |
| Web resource `data` parameter size limit (~2000 chars) | Only passing eventId + eventType GUIDs (< 100 chars) |
| Side pane width different for webresource vs entityrecord | Use same PANE_WIDTH (400px) — already configured |
| Navigation detection polling (200ms) performance | Already proven working in v2.15.0, minimal overhead |

---

## Files Summary

### Modified Files

| File | Changes |
|------|---------|
| `src/solutions/EventsPage/src/App.tsx` | Replace entityrecord with webresource, add EVENT_SAVED listener, enhance cleanup |
| `src/solutions/EventsPage/src/config/eventConfig.ts` | Add EVENT_DETAIL_WEB_RESOURCE_NAME constant |
| `src/solutions/EventDetailSidePane/src/App.tsx` | Add sessionStorage persistence, BroadcastChannel send on save |
| `src/solutions/EventDetailSidePane/package.json` | Version bump |

### New Files

| File | Purpose |
|------|---------|
| `src/solutions/EventDetailSidePane/src/utils/broadcastChannel.ts` | BroadcastChannel send/receive for cross-iframe communication |
| `src/solutions/EventDetailSidePane/src/utils/sessionPersistence.ts` | SessionStorage persist/restore for tab-switch survival |
| `src/client/shared/Spaarke.UI.Components/src/hooks/useEntityTypeConfig.ts` | Generic config JSON loader (Phase 3) |
| `src/client/shared/Spaarke.UI.Components/src/hooks/useDirtyFields.ts` | Generic dirty field tracking (Phase 3) |
| `src/client/shared/Spaarke.UI.Components/src/hooks/useOptimisticSave.ts` | Generic optimistic save (Phase 3) |
| `src/client/shared/Spaarke.UI.Components/src/components/SidePaneShell.tsx` | Reusable side pane layout shell (Phase 3) |

### Deprecated / Removed

| File | Action |
|------|--------|
| `src/client/webresources/js/sprk_event_sidepane_form.js` | No longer needed (OOB form enhancer) |
| `eventConfig.ts` → `EVENT_TYPE_FORM_MAPPINGS` | Keep for reference, no longer drives side pane |

---

## Dataverse Prerequisites

| Item | Status | Notes |
|------|--------|-------|
| `sprk_fieldconfigjson` field on `sprk_eventtype` | Created | Needs JSON populated per Event Type |
| `sprk_eventdetailsidepane.html` web resource | To deploy | Build from EventDetailSidePane solution |
| Event Type records populated with config JSON | To do | 11 Event Type records need sprk_fieldconfigjson values |

### sprk_fieldconfigjson Values per Event Type

| Event Type | Config JSON |
|------------|-------------|
| **Task** | `{"sectionDefaults":{"dates":"expanded","description":"expanded","relatedEvent":"collapsed","history":"collapsed"}}` |
| **Deadline** | `{"sectionDefaults":{"dates":"expanded","description":"collapsed","relatedEvent":"collapsed","history":"collapsed"}}` |
| **Reminder** | `{"hiddenSections":["relatedEvent"],"sectionDefaults":{"dates":"expanded","description":"collapsed","history":"collapsed"}}` |
| **Milestone** | `{"hiddenFields":["sprk_remindat"],"sectionDefaults":{"dates":"expanded","description":"expanded","relatedEvent":"collapsed","history":"collapsed"}}` |
| **Action** | `{"hiddenFields":["sprk_finalduedate","sprk_remindat"],"hiddenSections":["relatedEvent"],"sectionDefaults":{"dates":"expanded","description":"expanded","history":"collapsed"}}` |
| **Filing** | `{"hiddenFields":["sprk_finalduedate","sprk_remindat"],"hiddenSections":["relatedEvent","history"],"sectionDefaults":{"dates":"collapsed","description":"expanded"}}` |
| **Notification** | `{"hiddenFields":["sprk_basedate","sprk_finalduedate","sprk_remindat"],"hiddenSections":["dates","relatedEvent","history"],"sectionDefaults":{"description":"expanded"}}` |
| **Status Change** | `{"hiddenFields":["sprk_basedate","sprk_finalduedate","sprk_remindat"],"hiddenSections":["dates","relatedEvent"],"sectionDefaults":{"description":"expanded","history":"collapsed"}}` |
| **Approval** | `{"hiddenFields":["sprk_remindat"],"sectionDefaults":{"dates":"expanded","description":"expanded","relatedEvent":"collapsed","history":"collapsed"}}` |
| **Communication** | `{"hiddenFields":["sprk_basedate","sprk_finalduedate","sprk_remindat"],"hiddenSections":["dates","relatedEvent"],"sectionDefaults":{"description":"expanded","history":"collapsed"}}` |
| **Meeting** | `{"hiddenFields":["sprk_basedate","sprk_finalduedate"],"sectionDefaults":{"dates":"expanded","description":"expanded","relatedEvent":"collapsed","history":"collapsed"}}` |

> Note: These are starting configurations. Adjust based on UAT feedback. The JSON is stored per Event Type record and can be updated without code changes.

---

## Implementation Completion Summary

### Build Results

| Solution | Version | Bundle Size | Status |
|----------|---------|-------------|--------|
| EventsPage | 2.17.0+ | 608.63 kB | Built successfully |
| EventDetailSidePane | 2.0.0 | 739.79 kB | Built successfully |
| Shared Library (types) | — | N/A | Type check passed (peer dep warnings only) |

### Tasks Completed

| Task | Status | Notes |
|------|--------|-------|
| 100 | Completed | EventsPage switched from entityrecord to webresource pageType |
| 101 | Completed | EventsPage build verified |
| 102 | Completed | EventDetailSidePane v2.0.0 build verified |
| 103 | Completed | BroadcastChannel communication added |
| 104 | Completed | SessionStorage persistence for tab-switch survival |
| 105 | Completed | Grid refresh listener for EVENT_SAVED messages |
| 106 | Completed | Navigation cleanup updated for sessionStorage |
| 107 | Completed | useEntityTypeConfig generic hook extracted to shared library |
| 108 | Completed | SidePaneShell extracted to shared components |
| 109 | Completed | useDirtyFields + useOptimisticSave extracted to shared hooks |

### Remaining Deployment Steps

1. Deploy `sprk_eventdetailsidepane.html` web resource to Dataverse
2. Deploy updated EventsPage to Dataverse
3. Populate `sprk_fieldconfigjson` on 11 Event Type records (JSON values listed above)
4. Remove OOB form enhancer `sprk_event_sidepane_form.js` (no longer needed)
5. End-to-end testing: all Event Types, tab switching, navigation, save workflows

---

*Last updated: 2026-02-19*
