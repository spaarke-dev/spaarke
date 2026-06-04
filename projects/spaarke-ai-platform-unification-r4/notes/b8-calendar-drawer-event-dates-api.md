# B-8: CalendarDrawer.eventDates API Upgrade — Implementation Notes

> **Task**: 064 (R4 B-8) — Upgrade `CalendarDrawer.eventDates` from `string[]` to `IEventDateInfo[]`
> **Date**: 2026-05-26
> **Status**: Complete

---

## Summary

Closed the type-drift opened by R3 task 114: `CalendarDrawer.tsx` was typed
`eventDates: string[]` but cast internally to `IEventDateInfo[]` via
`as unknown as IEventDateInfo[]`. The cast is removed; prop type is now
`IEventDateInfo[]`; rendering layer (in `CalendarSection`) was extended to
surface count badges (when `count > 1`) and overdue indicators (when
`overdue === true`) per FR-11.

---

## Scope Decisions

### IEventDateInfo extended with optional `overdue`

The pre-existing `IEventDateInfo` shape was `{ date: string; count: number }`.
FR-11 acceptance criteria (and POML step 4) call for **overdue indicators**,
but the type had no `overdue` field. Two options were considered:

1. **(Chosen)** Add `overdue?: boolean` to `IEventDateInfo` (additive, optional,
   backward-compatible). Call sites that don't surface overdue events simply
   omit the field; renderer guards on `=== true`. Enables future call-site
   wiring (Events overdue logic) without further type churn.
2. (Deferred) Keep type at `{date, count}` only and defer overdue rendering
   entirely to task 067 (B-11). Would have shipped a narrower B-8 but left
   FR-11 acceptance incomplete.

Option 1 is additive — no consumer breakage. Verified via `tsc --noEmit` on:
- `@spaarke/events-components` (the lib)
- `EventsPage` (Vite consumer)
- `CalendarSidePane` (Vite consumer)
- `SpaarkeAi` (Vite consumer)

All built clean.

### No JSX call-site updates needed

POML step 2 required a grep inventory of `<CalendarDrawer` JSX usage. Result:
**zero JSX call sites** in the entire codebase. `CalendarDrawer` is exported
from the `@spaarke/events-components` barrel but not currently rendered
anywhere (it was authored for the EventsPage sidepane but the consumer never
landed — see R3 notes/deploys/2026-05-20-deploy.md row referencing the
original cast). Therefore the "update each call site" step in the POML
collapsed to "update the cast site inside CalendarDrawer itself".

This is recorded so reviewers don't expect call-site diffs in the PR.

### Rendering lives in CalendarSection, not CalendarDrawer

CalendarDrawer is a thin overlay wrapper around CalendarSection (the actual
calendar grid renderer). CalendarSection already maintained an internal
`eventDateMap` keyed by date and previously stored only `count`. The
count-badge and overdue-indicator rendering naturally belongs in
CalendarSection's day-cell render loop — not in CalendarDrawer (which has no
day-cell DOM of its own). Per POML step 4 ("Inside `CalendarDrawer.tsx` (or
its `CalendarSection` consumer)"), this placement is explicitly allowed.

CalendarSection's `eventDateMap` value type was widened from `number` →
`IEventDateInfo`. All map consumers go through `.get()` (returns
`IEventDateInfo | undefined`) and use optional-chain access on the result —
no breakage.

---

## ADR Compliance

- **ADR-012** (shared-component context-agnostic): ✅ — `IEventDateInfo`
  extension is additive + optional; CalendarDrawer remains a context-agnostic
  presentational component; no host-specific logic added.
- **ADR-021** (Fluent v9 tokens only): ✅ — All new styling uses v9 semantic
  tokens (`colorStatusDangerForeground1`, `colorNeutralForegroundOnBrand`).
  Badge component is Fluent v9 `CounterBadge` (semantic colors via
  `color="informative"`). Overdue indicator is Fluent v9 `ErrorCircle12Filled`
  icon. No hex / rgba / Fluent v8.
- **ADR-022** (React 19): ✅ — No legacy hook patterns; uses standard
  `React.useMemo` + JSX.

---

## Files Modified

1. `src/client/shared/Spaarke.Events.Components/src/components/CalendarSection/CalendarSection.tsx`
   - Added `overdue?: boolean` to `IEventDateInfo`
   - Added Fluent v9 `CounterBadge` + `ErrorCircle12Filled` imports
   - Widened `eventDateMap` value type from `number` → `IEventDateInfo`
   - Added `countBadgeWrapper`, `overdueIndicator`, `overdueIndicatorOnBrand`
     styles (all token-based)
   - Day-cell render loop: count badge (when `count > 1`), overdue indicator
     (when `overdue === true`)
2. `src/client/shared/Spaarke.Events.Components/src/components/CalendarSection/CalendarDrawer.tsx`
   - Prop type `eventDates: string[]` → `eventDates: IEventDateInfo[]`
   - Removed `as unknown as IEventDateInfo[]` cast at line ~124
   - Removed historical R3-task-114 comment (preserved in deploy notes archive)
   - Added JSDoc explaining backward-compat path (`[{date, count: 1}]` for dot-only render)

---

## Carry-overs to Task 067 (B-11)

One redundant cast was discovered in `CalendarWorkspaceWidget.tsx` line 1080:

```tsx
<CalendarSection
  eventDates={eventDates as IEventDateInfo[]}
  ...
/>
```

The `eventDates` here comes from `useEventsPageContext()` and is already
typed `IEventDateInfo[]` (see `EventsPageContext.tsx` line 56). The cast
is redundant but innocuous (no type-narrowing change). Left as-is because
task 067 (B-11) is explicitly the "type-drift casts cleanup" task and the
POML constraints note: *"if call sites cannot easily produce IEventDateInfo[],
task 067 (B-11) MAY pick up the residual type-tightening — document the
boundary."*

This is documented now so task 067 has a clean inventory item.

---

## Build + Lint Verification

- `npm run build` on `@spaarke/events-components`: ✅ 0 errors
- `npm run build` on `EventsPage`: ✅ 0 errors (1199.68 kB / gzip 332.40 kB — no measurable delta vs B-7 baseline)
- `npm run build` on `CalendarSidePane`: ✅ 0 errors (1100.11 kB / gzip 306.51 kB)
- `npm run build` on `SpaarkeAi`: ✅ 0 errors (3455.39 kB / gzip 923.11 kB)
- Lint: `tsc --noEmit` passes on all packages. ESLint not installed locally
  in consumer packages — relies on `tsc` as type lint per project convention.

---

## UI Smoke Tests

Deferred to Phase 7 manual verification per task-execute guardrails (no Chrome
integration in this session). The `<ui-tests>` block in the POML defines:

1. Event-count badges (count > 1) render via Fluent v9 Badge
2. Overdue indicators render via v9 dangerForeground1
3. Dark mode compliance (ADR-021)
4. Behavior parity (date selection + month nav)

All four are codified in the implementation but require live D365 environment
to verify visually. Recommended: run during R4 wrap-up after B-7 → B-10 stack
deploys.
