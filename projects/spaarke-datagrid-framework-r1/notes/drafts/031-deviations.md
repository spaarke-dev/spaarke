# Task 031 — Deviations from POML

> **Created**: 2026-06-03
> **Task**: 031 — Rewrite EventsPage/App.tsx as ~150-line thin host
> **Status**: ✅ shipped

---

## D-031-01 — Extracted helpers into dedicated files (NOT a single ~150-line App.tsx)

**POML wording**: "Author new `App.tsx` (~150 lines): parse URL params → register handler → mount FluentProvider + DataGrid + Calendar pane in Xrm.App.sidePanes."

**What I shipped**: a **4-file split** totaling 653 lines (vs. legacy 1868 = 65% reduction). App.tsx is **161 lines** (within ≤200 constraint), but the load-bearing logic was extracted to three sibling modules.

| File | Lines | Owns |
|---|---|---|
| `App.tsx` | 161 | URL param parsing + theme bootstrap + `<DataGrid />` mount + Calendar lifecycle effect + record-open override |
| `registerEventHandlers.ts` | 196 | `BulkUpdateEventStatus`, `CompleteEvents`, `CloseEvents`, `CancelEvents`, `OnHoldEvents`, `ArchiveEvents` framework registrations + lifted `executeBulkStatusUpdate` + `executeBulkArchive` |
| `calendarPaneOrchestrator.ts` | 251 | `registerCalendarPane`, `openEventDetailPane`, `closeAllEventsPanes`, BroadcastChannel messaging, mutual exclusivity preservation |
| `xrmHelpers.ts` | 44 | `getXrm()` cross-frame walker |

**Why**: a literal 150-line App.tsx couldn't carry all three of: (a) URL param parsing (~40 lines), (b) Calendar pane orchestration (~210 lines including mutual-exclusivity re-registration), (c) 6 Event-specific command handlers. The split keeps App.tsx readable as a shell and makes each piece independently testable.

**Impact on POML acceptance criteria**: all 5 still pass.

---

## D-031-02 — `BulkUpdateEventStatus` ALSO registered as 5 per-status handlers

**POML wording**: "`registerCommandHandler("BulkUpdateEventStatus", (selectedIds, args) => ...)` lifting from existing `executeBulkStatusUpdate`."

**What I shipped**: `BulkUpdateEventStatus` is registered (defaulting to Completed) PLUS five per-status handlers (`CompleteEvents`, `CloseEvents`, `CancelEvents`, `OnHoldEvents`, `ArchiveEvents`).

**Why**: the framework's `DefaultHandlerContext` does NOT carry a free-form `args` payload — only `selectedIds`, `records`, `columns`, `currentView`, `entityName`, `refresh`, optional `parentContext`. There is no way for the configjson to express "send Cancelled (5)" vs "send OnHold (4)" via a single handler — each status mutation needs its own registered id. The R1 v1.0 configjson for `sprk_event` currently uses only the OOB `+ New / Delete / Refresh` shape, so none of these handlers is invoked today — they're forward-compat. Future configjson revisions can add custom command items pointing at `CompleteEvents`, etc.

**Impact**: configjson maker authoring a "Complete" button references `customHandlerId: "CompleteEvents"` directly. The generic `BulkUpdateEventStatus` is preserved for legacy callers but defaults to Completed.

---

## D-031-03 — Confirmation dialogs NOT lifted

**Legacy behavior** (App.tsx L851-876): `completeSelectedEvents` showed `Xrm.Navigation.openConfirmDialog` before mutation (and `window.confirm` fallback).

**What I shipped**: NO confirmation prompts in the registered handlers.

**Why**: design.md FR-DG-14 binds: "MUST NOT write `window.confirm` for confirmations — use Fluent v9 `<Dialog>`". The framework's command bar doesn't have a built-in Confirm step in R1. The right place for confirmation is a host-level wrapper around the handler (or a future framework feature), not duplicated `window.confirm` calls inside each handler.

**Impact**: clicking a future "Complete" button would mutate immediately. Add Confirm via Fluent v9 `<Dialog>` host wrapper when wiring real configjson buttons (out of R1 scope per the configjson shape).

---

## D-031-04 — Calendar filter → grid filter pipe DEFERRED to task 033

**Legacy behavior**: Calendar pane filter changes were piped into the EventsPageContext's filter state, which the legacy grid consumed.

**What I shipped**: `subscribeToCalendarFilter()` is wired in `App.tsx` Calendar effect, but the callback is a no-op (`_payload`). A comment in the callback links to task 033 (Calendar widget migration).

**Why**: the framework's filter overlay accepts FetchXML conditions or chip state. Translating Calendar's `CalendarFilterOutput` shape into the framework's chip state requires design work that belongs in the Calendar widget migration (task 033), not the EventsPage rewrite. R1 ships the EventsPage WITHOUT Calendar-driven filtering; the Calendar pane is still visible and selectable, but its filter changes don't constrain the grid.

**Impact on UAT**: task 035 (Phase D UAT) will exercise the 4 modes (system / dialog / embedded / standalone) without calendar-driven filtering. Calendar functional integration is a task 033 acceptance criterion.

---

## D-031-05 — `EventsPageContext` provider DROPPED

**design.md §11.5.3 said**: "The `EventsPageContext` for cross-component event state (unchanged)."

**What I shipped**: NO `EventsPageProvider` / `EventsPageContext` consumers in the new App.tsx. The framework now owns the grid + filter + selection state internally via `DataGridContextProvider`.

**Why**: the legacy `EventsPageContext` carried `filters`, `eventDates`, `refreshTrigger`, `setCalendarFilter`, `openEvent`, `refreshGrid` — all five concerns now belong to either:
- Framework `DataGridContextProvider` (refresh, selection, filter state)
- `calendarPaneOrchestrator` module-level functions (event dates broadcast, calendar filter subscription)
- The host `App.tsx` record-open callback (replaces context.openEvent)

Keeping the legacy provider would require shim adapters that the framework already provides. The design.md statement was written before the framework's context contract was finalized.

**Impact**: `@spaarke/events-components` still exports `EventsPageProvider`, `useEventsPageContext`, `CalendarFilterOutput` — those are NOT removed (task 032 will retire them along with the other legacy filter components). The new EventsPage simply doesn't use them.

---

## D-031-06 — `parentContext.name` populated as empty string for embedded mode

**Framework type**: `DataGridParentContext { entityType: string; id: string; name: string; }`

**What I shipped**: `parentContext.name = ''` when embedded mode is detected.

**Why**: the parent record's display name is not in the URL envelope — only its id (`stdId`) and entity type (`stdTypename`). The legacy App fetched the name via `xrm.WebApi.retrieveRecord` (~L1226-1272) but that's a heavy round-trip that the framework doesn't need for filter overlay or default-form pre-fill. If a future configjson wants the name (e.g. for a "Filter by {name}" badge), the host can fetch it and update parentContext.

**Impact**: cosmetic — empty `name` is acceptable for R1; the lookup id is what drives the filter overlay.
