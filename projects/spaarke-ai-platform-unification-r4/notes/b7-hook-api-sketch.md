# B-7: `useEventsBulkActions` hook API design sketch

> **Task**: 063 — extract inline bulk-action logic from `EventsPage/src/App.tsx`
> and the duplicate copy in `CalendarWorkspaceWidget.tsx` into a single shared
> hook in `@spaarke/events-components/src/hooks/useEventsBulkActions.ts`.
> **Date**: 2026-05-26
> **Status**: Sketched + implemented

---

## Sources surveyed

| Source | Lines | What it has |
|---|---|---|
| `src/solutions/EventsPage/src/App.tsx` | 200-225, 327-358, 717-984 | `EventStatus` enum, `StateCode` enum, `getXrm()`, `executeBulkStatusUpdate`, `executeBulkArchive`, five wrappers (`completeSelectedEvents`, `closeSelectedEvents`, `cancelSelectedEvents`, `putOnHoldSelectedEvents`, `archiveSelectedEvents`) |
| `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` | 120-140, 442-524, 798-886 | Re-declared `EVENT_ENTITY_NAME`, `EventStatus`, `StateCode`, `getXrm()`, `bulkStatusUpdate`, `bulkArchive`, `confirmDialog`, plus five `useCallback` toolbar handlers (`onComplete`, `onClose`, `onCancel`, `onOnHold`, `onArchive`) |

**Duplication scope**: ~120 lines of bulk-action logic duplicated across the two files. The constants (`EventStatus`, `StateCode`, `EVENT_ENTITY_NAME`) are duplicated verbatim.

## Hook contract

```typescript
// Constants — re-exported so consumers can drop their inline copies
export const EventStatus = {
  DRAFT: 0,
  OPEN: 1,
  COMPLETED: 2,
  CLOSED: 3,
  ON_HOLD: 4,
  CANCELLED: 5,
  REASSIGNED: 6,
  ARCHIVED: 7,
} as const;
export type EventStatusValue = typeof EventStatus[keyof typeof EventStatus];

export const StateCode = {
  ACTIVE: 0,
  INACTIVE: 1,
} as const;

// Default entity logical name — overridable via deps
export const EVENT_ENTITY_NAME = "sprk_event";

// Dependencies injected by host (ADR-012 context-agnostic)
export interface UseEventsBulkActionsDeps {
  /** Returns the Xrm global, with cross-frame fallback. Both consumers already implement this. */
  getXrm: () => XrmLike | null;
  /** Optional override; defaults to "sprk_event". */
  entityName?: string;
}

// Action API returned by the hook
export interface UseEventsBulkActionsApi {
  // Low-level primitives — no dialog, fire-and-return-bool
  updateStatus: (
    eventIds: string[],
    newStatus: number,
    statusLabel: string,
    additionalFields?: Record<string, unknown>,
  ) => Promise<boolean>;
  archiveEvents: (eventIds: string[]) => Promise<boolean>;

  // High-level: confirm-dialog + status update for each canonical action
  completeEvents: (eventIds: string[]) => Promise<boolean>;
  closeEvents: (eventIds: string[]) => Promise<boolean>;
  cancelEvents: (eventIds: string[]) => Promise<boolean>;
  holdEvents: (eventIds: string[]) => Promise<boolean>;
  archiveSelectedEvents: (eventIds: string[]) => Promise<boolean>;
}

// Hook
export function useEventsBulkActions(
  deps: UseEventsBulkActionsDeps,
): UseEventsBulkActionsApi;
```

### Why this shape

1. **Single source of truth for constants** — `EventStatus` / `StateCode` / `EVENT_ENTITY_NAME` are re-exported from the hook module, so both `App.tsx` and `CalendarWorkspaceWidget.tsx` drop their inline copies.
2. **`getXrm` injected** — the cross-frame Xrm lookup is consumer territory (EventsPage has a more elaborate version that checks `Xrm.App.sidePanes`; CalendarWidget checks `Xrm.WebApi` only). The hook accepts whichever the host provides — context-agnostic per ADR-012.
3. **No host-specific imports inside hook** — no `useEventsPageContext`, no `@spaarke/auth`, no `Xrm` global access. All side effects go through the injected `getXrm()`.
4. **No BFF dependency** — bulk operations are Xrm.WebApi (Dataverse direct, per ADR-028 `Xrm.WebApi` exception). The hook needs neither `authenticatedFetch` nor a BFF base URL.
5. **Returns stable function refs** — uses `React.useCallback` keyed on `entityName` so consumers can pass the action handlers as deps to their own `useCallback`s without inducing re-renders.
6. **Confirm dialog wording preserved from EventsPage** — see deviations note below.

### Behavioral parity

- Confirm dialog text + button labels: use EventsPage's canonical strings (the older / more polished set; "Keep Active" cancel label + `\n\n` in Archive). Calendar gains these strings as a tiny UX improvement of consolidation. Both consumers retain identical fallback to `window.confirm()` when `Xrm.Navigation.openConfirmDialog` is unavailable.
- Notification dispatch: identical — `xrm.App?.addGlobalNotification?.({ type: 2, level: 1, message, showCloseButton: true })`.
- Error handling: identical — `console.error` + (EventsPage only) `Xrm.Navigation.openAlertDialog`. Hook preserves the alert-dialog path for *both* consumers post-extraction (slight UX improvement for Calendar — see deviations).
- ID cleaning: identical — `id.replace(/[{}]/g, "")` before each update call.
- Archive sequence: identical — two sequential `updateRecord` calls per id (status=ARCHIVED first, then `statecode=INACTIVE, statuscode=2`).

### Deviations (documented, NOT bug fixes)

1. **Calendar widget Cancel cancel-label** was "Cancel" (unified `confirmDialog` helper); post-extraction it's "Keep Active" (EventsPage canonical). Operator-friendly: avoids ambiguity between "Cancel" (the action) and "Cancel" (close the dialog).
2. **Calendar widget Archive text** was `"Archive 3 event(s)? This will hide them from active views."` (plain space); post-extraction it's `"Archive 3 event(s)?\n\nThis will hide them from active views."` (paragraph break). EventsPage canonical.
3. **Calendar widget error path** previously had no `openAlertDialog` fallback (only `console.error`). Post-extraction it gains the EventsPage alert-dialog path. Failures are now visible to operators in the Calendar widget too.

These three are **intentional improvements that came along with consolidation**; flagged here for the R4 lessons-learned ledger. Not bug fixes smuggled in.

### Not in scope

- No memoization of `eventIds` arrays — caller responsibility.
- No selection-state management — caller owns `selectedIds` state.
- No grid-refresh trigger — caller calls `refreshGrid()` after a successful action (both consumers do this today).
- No `Xrm.App.sidePanes` interaction — completely consumer territory.
- No retry / partial-failure semantics — preserves today's all-or-nothing `Promise.all` shape.

---

## Carry-overs (capture for future tasks)

- **Calendar widget `EVENT_ENTITY_NAME` constant** (line 120) is now redundant — replaced by the hook's export. Removed in the rewire.
- **Calendar widget `STATUS_OPTIONS` array** (lines 154-163) duplicates EventStatus knowledge. Not consolidated in this task — that's a separate concern (filter-options table) and out of scope for the bulk-actions extraction.
- **`StatusFilter` component** has its own `STATUS_FILTER_OPTIONS` — also separate concern.
- **`alertDialog` error path on Calendar widget**: previously absent — now present (deviation #3 above). If operators find this noisy, change the hook to make the alert dialog opt-in via a `deps.showErrorDialog?: boolean` flag.

---

## Files affected

| File | Change |
|---|---|
| `src/client/shared/Spaarke.Events.Components/src/hooks/useEventsBulkActions.ts` | NEW |
| `src/client/shared/Spaarke.Events.Components/src/hooks/index.ts` | Re-export new hook |
| `src/client/shared/Spaarke.Events.Components/src/index.ts` | Already re-exports hooks barrel (lib already wired) |
| `src/solutions/EventsPage/src/App.tsx` | Remove inline implementations + import hook; rewire 5 handlers |
| `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` | Remove inline implementations + import hook; rewire 5 handlers |
