# Task 229 notes: lane count cards (#1688, DI-46)

Rigor FULL, sonnet @ high, directional steps. Branch `fix/ontology-229-lane-count-cards` from origin/master c438119bca
(the POML's line numbers were taken at d43fc9e16d, so they have moved).

## Pre-start check (project CLAUDE.md section 2 UI; reconciliation B.6) against prototype HANDOFF @ ae1cc9f

- HANDOFF section 1.2: "Count filter cards per lane, split by what has to be done so the counts add up"; the card
  contents are in `v4-reconciliation.md` W-7: count, label, a note line (*oldest n days, n past due, next due*) and the All
  card's "n of m done today" progress. W-7 is MATCHES (MetricCard + MetricCardRow, no new component): nothing to change in the
  component choice.
- Wording source of truth: prototype `WorklistV3.tsx` @ ae1cc9f L75-81:
  - Decide: `overdue ? "N overdue" : "oldest N day(s)"`
  - Do, Coming due ("soon"): `next due <day> Oct` (the earliest `due` of the card's items; "next due -" when none)
  - Do, other cards: `N past due`
  - empty card: "Nothing waiting" in the prototype; the solution already shows no note on an empty (disabled) card and that is
    out of this issue's scope.
- Prototype "overdue" on a Decide item = `isOverdue(f)` (the evidence shows a missed due date, e.g. a lapsed SLA). The
  solution has one column for it: `WorklistItem.dueDate` (`sprk_duedate`), which the route passes through for any lane
  (`WorklistItemSummary.cs`). So a Decide item is overdue when its `dueDate` is before the viewer's today (same rule the Do
  lane uses). The inquiry has no due date (D-56) and a Decide item with no `dueDate` is never overdue.
- No conflict with D-227 (finding leads on decision surfaces) or any other binding decision: escalation trigger not fired,
  no new component.

## Findings

1. `noteFor` Coming-due card: there was no next-due note (it showed "N past due" or nothing). Now "next due 12 Oct",
   formatted by the same helper the Do lines use (`formatDueDay`, extracted from `formatDueState`; one formatter, not two).
2. Decide cards: "N overdue" when any item is past its `dueDate`, else the existing "oldest N days".
3. `ariaLabel`: appends ", n of m done today" when `card.progress` is set and its total is positive (the MetricCard hides
   the progress line when total is 0, so the label does the same).
4. Stale filter: **already handled in the host.** `WorklistWidget.tsx` has an effect (`setFilters` when
   `views[lane].filterKey !== prev[lane]`, "A filter whose cards emptied ... falls back to All, so it cannot come back
   unannounced") that writes the resolved key back into `filters`. `buildLaneCountCards` and `resolveLaneFilterKey` are pure and
   own no `selectedKey`; `WorklistLanePanel` is the only caller and passes the resolved `view.filterKey`. The POML's item (4)
   therefore needs no source change; the missing piece was a test that proves it: added the negative widget test (filter
   vanishes, falls back, items return, filter stays off). It passes on origin/master as well (it is a regression guard, not a
   fail-before test); the POML's fail-before criterion lists only the three wording/aria tests.

## Files

- `src/client/shared/Spaarke.UI.Components/src/components/Worklist/laneCountFilters.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/Worklist/composeIssueLine.ts` (`formatDueDay` extracted)
- tests: `Worklist/__tests__/laneCountFilters.test.tsx`, `Worklist/__tests__/WorklistWidget.test.tsx`

## Verification

- Worklist jest folder: 14 suites, 312 tests pass after the change (`npx jest src/components/Worklist` from
  `Spaarke.UI.Components`).
- Fail-before: the old `laneCountFilters.ts` and `composeIssueLine.ts` restored over the new tests: 7 tests fail in
  `laneCountFilters.test.tsx` (Coming-due note x3, "N overdue", aria progress x2 incl. the rendered accessible name; the
  existing Do-notes expectation was updated to the new Coming-due note); the widget negative test passes on the old code
  too, because the host effect already clears the filter (finding 4). Sources restored, 312 pass again.
- `node scripts/quality/client-lint/client-lint.mjs check --package ui-components`: ok.
- `tsc --noEmit -p tsconfig.json` (ui-components): no error in `Worklist/`; the package reports pre-existing errors only
  (unresolved `@spaarke/auth`, `unknown` catch variables in other folders) that this change does not touch.
- No BFF change, no new component, service, package or DI registration (root CLAUDE.md section 11 not engaged).

## Review (Step 9.5, self-run, no sub-agents)

- Class grep (rule 6): `buildLaneCountCards` has one caller (`WorklistLanePanel`); no other card builder carries a copy of
  the past-due loop (the one in `noteFor` was folded into `countPastDue`).
- Known limit K4: a Decide item shows "N overdue" only when the route sends `dueDate` for it; the inquiry has no due date
  (D-56), so today's Decide cards mostly keep "oldest N days". No defect: the card follows the data.
- Known limit K2: "next due" uses the viewer's locale for the month name (same as the Do lines' "due 2 Oct").

rework: none task 229
