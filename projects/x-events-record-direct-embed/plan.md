# Implementation Plan: Context-Aware Events Page Embed

> **Project**: events-record-direct-embed
> **Spec**: [spec.md](spec.md)
> **Estimated Tasks**: 8 implementation + 1 build/deploy

---

## Phase 1: Core Infrastructure (Tasks 001-003)

### Task 001: Extend Context Parsing for Embedded Mode

**Goal**: Parse `mode=embedded`, `entityName`, and `recordId` from URL data parameter.

**Files**:
- `EventsPage/src/App.tsx` — extend `parseDrillThroughParams()` and add `IS_EMBEDDED_MODE`

**Steps**:
1. Extend `DrillThroughParams` interface with `entityName: string | null` and `recordId: string | null`
2. Update `parseDrillThroughParams()` to extract `entityName` and `recordId` keys
3. Add `IS_EMBEDDED_MODE = DRILL_THROUGH_PARAMS.mode === "embedded"` constant
4. When `IS_EMBEDDED_MODE` is true, auto-set `contextFilter`:
   - `fieldName = "sprk_regardingrecordid"`
   - `value = recordId`
5. Verify existing `IS_DIALOG_MODE` still works (no regression)

**Acceptance**:
- `?data=mode=embedded&entityName=sprk_matter&recordId=abc-123` correctly populates params
- GridSection receives contextFilter and filters events

---

### Task 002: Entity-Specific View Discovery

**Goal**: Replace hardcoded view list with dynamic discovery based on entity prefix naming convention.

**Files**:
- `EventsPage/src/config/eventConfig.ts` — add `discoverEntityViews()` function
- `EventsPage/src/App.tsx` — call discovery on mount in embedded mode, pass dynamic views to ViewToolbar

**Steps**:
1. Add `discoverEntityViews(entityName: string): Promise<IEventViewConfig[]>` to eventConfig.ts:
   - Query `savedqueries` filtered by `returnedtypecode eq 'sprk_event'` and `startswith(name, '{Prefix}-')`
   - Map entity logical name to display prefix (e.g., `sprk_matter` → `Matter`)
   - Parse results into `IEventViewConfig[]`
   - Strip prefix from display name (e.g., `Matter-All Tasks` → `All Tasks`)
2. Add state for dynamic views in App.tsx: `const [availableViews, setAvailableViews] = useState(EVENT_VIEWS)`
3. On mount in embedded mode, call `discoverEntityViews()` and update state
4. If no entity views found, fall back to `EVENT_VIEWS` (system defaults)
5. Pass `availableViews` to ViewToolbar instead of static `EVENT_VIEWS`

**Acceptance**:
- Embedded in Matter with `Matter-All Events` view in Dataverse → view selector shows it
- Embedded in entity with no custom views → falls back to system views
- System page → still shows hardcoded 4 views (no discovery query)

---

### Task 003: +New Event Pre-Fill Parent Context

**Goal**: When user clicks +New Event in embedded mode, pre-fill `sprk_regardingrecordid` with the parent record.

**Files**:
- `EventsPage/src/App.tsx` — modify `handleNewEvent()` / quick create call

**Steps**:
1. In embedded mode, fetch parent record display name on mount:
   - `Xrm.WebApi.retrieveRecord(entityName, recordId, "?$select=...name...")`
   - Cache in ref: `parentRecordNameRef.current = result[nameField]`
   - Name field mapping: `sprk_matter` → `sprk_name`, `sprk_project` → `sprk_name`, etc.
   - Generic fallback: try common name fields (`name`, `sprk_name`, `subject`)
2. Modify `openQuickCreate()` to include `createFromEntity` when in embedded mode:
   ```typescript
   createFromEntity: {
     entityType: entityName,
     id: recordId,
     name: parentRecordNameRef.current
   }
   ```
3. After quick create success, refresh grid (existing pattern)

**Acceptance**:
- Click +New Event in Matter → quick create form has matter pre-filled in regarding field
- Click +New Event in system page → no pre-fill (current behavior)

---

## Phase 2: Side Pane Lifecycle (Tasks 004-005)

### Task 004: Create `useSidePaneLifecycle` Hook

**Goal**: Extract side pane cleanup into a reusable hook that detects tab switches and closes panes.

**Files**:
- *New*: `EventsPage/src/hooks/useSidePaneLifecycle.ts`
- `EventsPage/src/App.tsx` — refactor existing cleanup to use the hook

**Steps**:
1. Create `useSidePaneLifecycle` hook with options:
   ```typescript
   interface SidePaneLifecycleOptions {
     paneIds: string[];
     onCleanup?: () => void;
     enableVisibilityDetection?: boolean;  // default: true when embedded
     enableUrlPolling?: boolean;            // default: true
     pollingIntervalMs?: number;            // default: 200
     isEmbedded: boolean;
   }
   ```
2. Move existing cleanup logic from App.tsx into the hook:
   - `checkForNavigation()` URL polling
   - `beforeunload` / `pagehide` handlers
   - `closeAllSidePanes()` function
3. Add `visibilitychange` detection (new for embedded mode):
   - When `document.visibilityState === "hidden"` AND `isEmbedded === true`, close panes
   - Debounce 100ms to avoid false positives from transient visibility changes
4. Hook returns `{ closeAllPanes: () => void }` for imperative use
5. Wire up in App.tsx — replace inline cleanup with hook call

**Acceptance**:
- Embedded: switch from Events tab to Overview → all side panes close
- Embedded: click within Events tab (column sort, filter) → panes stay open
- System page: navigate away → panes close (existing behavior preserved)

---

### Task 005: Side Pane Registration in Embedded Mode

**Goal**: Ensure Calendar and Event Detail side panes register and function correctly in embedded mode.

**Files**:
- `EventsPage/src/App.tsx` — adjust `registerCalendarPane()` and `openEventDetailPane()` for embedded context

**Steps**:
1. In embedded mode, register Calendar pane on mount (same as system mode)
2. Verify BroadcastChannel messages work across iframes in embedded context:
   - CalendarSidePane sends `CALENDAR_FILTER_CHANGED` → EventsPage receives and filters grid
   - EventsPage sends `CALENDAR_EVENTS_UPDATE` → CalendarSidePane receives and highlights dates
   - EventDetailSidePane sends `EVENT_DETAIL_SAVED` → EventsPage refreshes grid
3. Calendar filter is **additive** to context filter:
   - Context filter: `sprk_regardingrecordid eq '{recordId}'`
   - Calendar filter: `AND sprk_duedate ge '2026-03-01' AND sprk_duedate le '2026-03-31'`
4. Verify Event Detail side pane opens and saves correctly when parent is embedded
5. Test side pane mutual exclusivity still works

**Acceptance**:
- Calendar filter narrows embedded grid (context + date range)
- Event detail opens on row click, saves, grid refreshes
- Opening event detail selects it, calendar becomes deselected (menu item persists)

---

## Phase 3: UI Polish (Tasks 006-007)

### Task 006: UI Adjustments for Embedded Mode

**Goal**: Small UI tweaks that make the embedded experience feel native to the host form.

**Files**:
- `EventsPage/src/App.tsx` — conditional rendering based on `IS_EMBEDDED_MODE`

**Steps**:
1. Hide page-level title/header in embedded mode (the form tab provides the title)
2. Verify command bar works correctly (New, Refresh, Views, Excel Templates)
3. Ensure grid fills available tab space (no double scrollbars)
4. Test with different form tab heights (short Matter forms vs. tall Project forms)
5. Verify dark mode works in embedded context (inherits from Dataverse theme)

**Acceptance**:
- No redundant title in embedded mode
- Grid fills tab area cleanly
- Dark mode renders correctly

---

### Task 007: Entity Display Name Resolution

**Goal**: Show meaningful labels for the parent context (e.g., "Events for REAL-2026-123456.02").

**Files**:
- `EventsPage/src/App.tsx` — fetch and display parent record name

**Steps**:
1. On mount in embedded mode, resolve parent record display name
2. Show in ViewToolbar subtitle or grid header: "Filtered to: {parentDisplayName}"
3. Fallback: show entity display name if record name fetch fails (e.g., "Filtered to: Matter")
4. System mode: no subtitle (current behavior)

**Acceptance**:
- Embedded in Matter → "Filtered to: REAL-2026-123456.02" shown near view selector
- System page → no filter subtitle

---

## Phase 4: Build & Deploy (Task 008)

### Task 008: Build, Test, and Deploy

**Goal**: Build the updated EventsPage, deploy to dev, configure a test form tab.

**Steps**:
1. Run `npx tsc --noEmit` — verify no TypeScript errors
2. Run `npm run build` — produce updated `dist/index.html`
3. Deploy web resource to Dataverse dev environment
4. Configure Matter form:
   - Add web resource tab `sprk_eventspage.html`
   - Data: `mode=embedded&entityName=sprk_matter&recordId={!entityid}`
   - Uncheck "Restrict cross-frame scripting"
5. Create test views: `Matter-All Events`, `Matter-All Tasks`, `Matter-Active Events`
6. Test end-to-end:
   - Open a Matter record → Events tab
   - Verify grid shows only that matter's events
   - Verify view selector shows Matter-specific views
   - Click +New → verify matter pre-fills
   - Open event in side pane → verify edit/save works
   - Switch to Overview tab → verify side panes close
   - Return to Events tab → verify panes can re-register

**Acceptance**:
- All 9 success criteria from spec.md pass
- System Events page unchanged (regression check)
- Dialog drill-through unchanged (regression check)

---

## Dependency Graph

```
Task 001 (Context Parsing)
  ├── Task 002 (View Discovery) — needs entityName from 001
  ├── Task 003 (+New Pre-Fill) — needs entityName + recordId from 001
  └── Task 005 (Side Pane Registration) — needs IS_EMBEDDED_MODE from 001

Task 004 (useSidePaneLifecycle Hook) — independent, can run parallel with 001

Task 006 (UI Polish) — after 001 + 002
Task 007 (Display Name) — after 001

Task 008 (Build/Deploy) — after all others
```

**Parallel opportunities**: Tasks 001 and 004 can run in parallel. Tasks 002, 003, and 005 can run in parallel after 001 completes.

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| `visibilitychange` fires on side pane menu click (false positive) | Debounce 100ms; only act when `isEmbedded && hidden`; test thoroughly |
| `savedquery` API returns no entity views | Graceful fallback to system views (already built) |
| `createFromEntity` doesn't populate polymorphic lookup | Test with `sprk_regardingrecordid`; fall back to manual field set if needed |
| Cross-origin iframe restrictions block Xrm access | Ensure "Restrict cross-frame scripting" is unchecked on form tab |
| Calendar filter + context filter interaction | Filters are additive — FetchXML injection supports multiple conditions |
