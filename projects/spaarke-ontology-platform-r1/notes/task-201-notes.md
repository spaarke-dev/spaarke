# Task 201 - Ontology admin Rules list and Triage categories table: clipped / cramped cells

Branch `fix/ontology-201-rule-list-clipping` (from origin/master fe150ac35a). Rigor FULL, sonnet @ high, no sub-agents.

## Root cause (differs slightly from the POML's guess)

Fluent's `Table` here is a native fixed-layout table (`display: table; table-layout: fixed`; `TableCell` is `display: table-cell; height: 44px`).
Two consequences:

1. **Rules list clip.** The Rule and Action-rate cells set `display: flex` (`stack`) on the `TableCell` itself. That takes the `td` out of
   table-cell layout, so the 44 px height stopped being a minimum and became a hard height, and `overflow: hidden` cut the second line.
   Fix: the cell stays a table cell; the code-over-name stack is a `div` INSIDE it. `overflow: hidden / textOverflow` removed (it never
   produced an ellipsis: no `nowrap`); `overflowWrap: anywhere` and token vertical padding added.
2. **Columns were all equal.** The `COLUMN_GROW` flex ratios did nothing in a native table, so every column got ~1/7 of the width (~120 px).
   Replaced with percentage `width` on the header and body cells (the width on a header cell sets the column in fixed layout).
   Rules: 29/8/10/7/9/17/20 (Rule has the largest share). Triage categories: 12/11/38/9/7/13/10 (Guidance largest; Edit 10% so the small
   button is not wider than its cell; On/Off 11% for the switch label).

## Scope addition (coordinator, same session)

Classification "Triage categories" table (`Classification/ClassificationArea.tsx`): cells were not clipped (no `display: flex`) but Guidance
was ~120 px, so a ~140-character preview wrapped to ~12 lines. Same width fix, `overflowWrap: anywhere` on Guidance.
Estimate at the measured ~840 px table: Guidance about 300 px, about 3-4 lines (was 12); wider panes give 2-3. Not measured in a browser.

## Class check (Step 9.5 rule 6)

Grep for `TableCell` given a flex/stack class across `src/**/*.tsx`: no match outside the two OntologyAdmin files fixed here.
Other Fluent `Table` users that rely on `flexGrow` ratios were not audited (outside scope); the pattern to look for is
`display: 'flex'` or `flexGrow` on a `Table*Cell` style in a table that is not `noNativeElements`.

## Tests (new files; existing tests untouched)

- `Rules/__tests__/RuleListLayout.test.tsx` - full code/name render; Rule and Action-rate cells computed `display` is `table-cell`, not
  `overflow: hidden`, stack wrapper has no hidden overflow or fixed height; Rule header has the largest computed width.
- `Classification/__tests__/ClassificationLayout.test.tsx` - guidance text renders; Guidance header has the largest computed width.
- jsdom cannot measure layout; it does resolve Griffel's injected stylesheet, so computed `display`, `overflow` and `width` are real.

Fail before the fix (same test files run against the unchanged components): Rules 3 failed (`display` received `flex` twice; width `NaN`
because no column width was set), Classification 1 failed (Guidance width `NaN`), 2 passed (text renders - they pin content, not layout).

## Results

- `npx jest src/components/OntologyAdmin` (Spaarke.UI.Components): 10 suites, 227 tests pass (includes the 6 new).
- `npx tsc --noEmit` (Spaarke.UI.Components, after building Spaarke.Auth and Spaarke.SdapClient): exit 0.
- `client-lint check`: `ui-components` ok. The other packages report FAILED on files this task does not touch (e.g. `PinnedMemoryEditDialog.tsx`,
  `EmailViewSelector.tsx`, `ReportDropdown.tsx`): their `node_modules` are not installed in this worktree, so type information is missing
  and `no-unnecessary-condition` fires. Not caused by this change; not verified against master with full installs.
- `npm run build` in `src/solutions/SpaarkeAi`: exit 0 (`tsc-surface-gate` 439 pre-existing shared-lib errors, surface-owned 0).

## Gates

- code-review (self, no sub-agents): no F-class finding. Behaviour unchanged (markup gains two wrapper `div`s; testids and handlers intact).
- adr-check: ADR-021 - Fluent tokens for spacing; widths are percentages (no token exists); no colours added.
- Known limit: the fix is unverified in a real browser; the main session checks 1440x900 and 1280x720 on dev (acceptance criterion 2).
  Unmeasured risk: at very narrow panes, fixed percentages with `table-layout: fixed` shrink every column proportionally; text wraps
  (`overflowWrap: anywhere`) but the Edit button (min-width 64 px) could spill if the table is under ~640 px.

rework: none task 201
