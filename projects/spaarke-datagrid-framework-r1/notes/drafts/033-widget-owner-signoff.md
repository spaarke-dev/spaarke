# Task 033 — Widget owner sign-off (UQ-06)

> **Created**: 2026-06-03
> **Task**: 033 — SpaarkeAi Calendar widget migrate to new DataGrid
> **Widget owner**: Ralph Schroeder (project owner)
> **Status**: 🔔 awaiting sign-off

---

## What the widget is today

`src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` — **1,220 lines**.

It's the SpaarkeAi workspace Calendar widget — consumed by `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` (the ONLY external consumer, mounted with no props as `React.createElement(CalendarWorkspaceWidget)`).

Internal anatomy:
- **Filter row** (~110 lines, L954-1064): 5 dropdowns (Event Type / Status / Date Field / From / To) + Apply/Clear buttons + collapse chevron
- **Calendar strip** (~30 lines, L1067-1099): responsive 1–5 month horizontal strip with month nav
- **Toolbar** (~55 lines, L1102-1155): New / Delete / Complete / Close / Cancel / OnHold / Archive / Refresh / Calendar (clear) / Open list — operate on grid row selection
- **Grid section** (~10 lines mount, L1162-1172): `<GridSection ... />` — the part we're replacing
- **State + handlers** (~600 lines): pending/applied filter state, 6 bulk-status callbacks, event-date computation, calendar-driven filter dispatch
- **EventsPageProvider wrapper** at top-level

---

## The migration

**Replace** `<GridSection ... />` at L1162 with:

```tsx
<DataGrid
  configId="e15c2b93-a05f-f111-a825-70a8a59455f4"
  dataverseClient={new XrmDataverseClient()}
  onRecordOpen={(recordId, record) => { /* navigate to event form */ }}
  /* TBD — see open question 1 */
/>
```

Same `EVENT_CONFIG_ID` as EventsPage (task 030's record). The Calendar widget gets the same Event grid configuration as EventsPage — column set, filter chips, command bar, row open type. **`@spaarke/events-components/GridSection` becomes orphaned** and can be deleted (the task 032 deferred cleanup).

---

## 🔔 Open design decisions — please pick

These need your call before I touch code.

### Q1 — Widget toolbar vs. DataGrid command bar (selection wiring)

The widget has its **own toolbar** at L1102-1155 with Delete / Complete / Close / Cancel / OnHold / Archive / Refresh that act on the selected grid rows. The new DataGrid has its **own command bar** built from the configjson (currently `+ New / Delete / Refresh`).

**Option A — Drop the widget toolbar, rely on DataGrid's command bar.**
- Pros: clean, single source of truth
- Cons: widget UI changes visibly; the OOB Power Apps grid command bar style is different from the widget's current chip-style toolbar; the configjson currently doesn't expose Complete/Close/Cancel/OnHold/Archive (would need configjson update referencing the per-status handlers I registered in task 031)
- Net: ~80 fewer lines in the widget; matches the EventsPage UX exactly

**Option B — Keep the widget toolbar; bridge it to DataGrid's internal selection.**
- Pros: visual continuity for existing Calendar widget users
- Cons: need to wire DataGrid → widget `selectedIds` state via `onSelectionChange` callback (the DataGrid does expose this); duplicates command logic
- Net: ~10 lines added; UX preserved

**Recommended: Option A** (cleaner, matches EventsPage, the configjson can be updated separately if Complete/Close/etc. need to come back).

### Q2 — Filter row + calendar-driven filtering

The widget's filter row (Event Type / Status / Date Field / From / To + Apply/Clear) currently drives grid filtering via `EventsPageContext` dispatch (~150 lines of effect + state). The new DataGrid owns all filter state internally via configjson chips — **the dispatch loop becomes a no-op**.

**Option A — Remove the filter row entirely.**
- Pros: DataGrid's column-header chevron filters supersede; ~150 fewer lines
- Cons: significant UX change — users lose the pending/applied two-phase filter and the explicit date-range fields

**Option B — Keep the filter row, but rewire it as a calendar-only chrome.**
- The dropdowns + Apply/Clear stay; they update `selectedDate` + calendar visual state ONLY (no grid filter dispatch)
- Cons: users may be confused that the filter row doesn't filter the grid; UX smell

**Option C — Keep the filter row AND pipe its state into DataGrid via a host-injected FetchXML overlay.**
- DataGrid framework has `parentContext` + `behavior.parentContextFilter` overlay mechanism — could extend to accept widget-side overlay
- Cons: requires a small framework extension; out of R1 scope strictly

**Recommended: Option A** — remove the filter row, rely on DataGrid's column-header chevron filters. Aligns with EventsPage and the Phase C pattern. (If user advocacy for the pending/applied two-phase UX is strong, Option C is the right long-term answer for a follow-up project.)

### Q3 — Calendar event-date counting (the dot indicators)

The calendar strip shows colored dots on dates that have events (count-driven coloring). Currently `handleRecordsLoaded` at L585-620 derives the counts from records that `GridSection` returns via `onRecordsLoaded`.

**Solution**: the new DataGrid exposes `onRecordsLoaded?: (records) => void` callback — same shape as GridSection. The widget's existing `handleRecordsLoaded` logic plugs in unchanged. ✅ **No design decision needed.**

### Q4 — `EventsPageProvider` wrapper

The widget currently wraps its inner layout in `<EventsPageProvider>` to dispatch filter state through `EventsPageContext`. With Options A1+A2 (drop widget toolbar + remove filter row), the provider becomes redundant — the layout has no consumers of `useEventsPageContext`.

**Recommended**: delete the `EventsPageProvider` wrapper from the widget. Other surfaces (the legacy EventsPage was a consumer but task 031 already removed it) don't depend on it from this widget's mount.

---

## Estimated outcome (assuming Q1=A, Q2=A, Q3=as-is, Q4=delete)

- **Line count**: 1220 → **~160-180 lines** (similar to task 031's EventsPage reduction)
- **Widget chrome preserved**: Calendar strip + month nav + collapse toggle
- **Widget chrome dropped**: filter row (150 lines), toolbar (55 lines), EventsPageProvider wrapper
- **External API unchanged**: `<CalendarWorkspaceWidget />` with optional `initialDateField` prop — LegalWorkspace consumer keeps working
- **Closing step**: delete `src/client/shared/Spaarke.Events.Components/src/components/GridSection/` and remove its barrel re-exports (the deferred cleanup from task 032)

## Estimated outcome (if you pick B/C variants)

Higher line count (300-500), more code paths to preserve, but tighter visual continuity. Still a net reduction from 1220.

---

## My recommendation

**Approve A1 + A2 + A4** (clean break — drop widget toolbar, drop filter row, drop provider). Q3 is automatic.

This makes the Calendar widget look + behave like the EventsPage grid — a single, native DataGrid that owns its own chrome — with the calendar strip on top as the dedicated calendar visual.

**If you have concerns about the Calendar widget UX changing visibly for current users, pick Option B/C variants on Q1 + Q2 and I'll preserve the chrome.**

## Signing off

Please respond with one of:
- **"approve all A"** — clean break, ~160-line widget
- **"approve A1 only / keep filter row"** — drop toolbar, keep filter row as calendar chrome
- **"approve A2 only / keep toolbar"** — drop filter row, keep toolbar with selection bridge
- **"keep everything"** — preserve toolbar + filter row, just swap GridSection for DataGrid
- **"defer task 033"** — pause; move to task 034 deploy (without Calendar widget migrate); resume in a follow-up project. GridSection retirement gets deferred too.

Note: per the POML literal step 1 (`🔔 Get widget owner sign-off (UQ-06)`), I cannot proceed without an explicit answer.
