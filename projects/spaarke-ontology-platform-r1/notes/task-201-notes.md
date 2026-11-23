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

## Fix round 1 (review findings F1 widths, F1 wrapping scope)

Real table width in the Console: centre pane is 50% of the window (min 320 px) minus 2 x 16 px padding: ~600 px at 1280x720, ~680 px at
1440x900. Designed for 560-1100 px. Cells have 8 px horizontal padding each side, so a column needs content + 16 px.

### Widths (pixels for fixed-content columns; the wide text column has none and takes the rest)

Rules (fixed total 480 px): Lane 64 ("Decide" ~42 px + 16), On / Off 76 ("Retired" badge ~52 px, header ~52 px, + 16), Version 72 (header
~50 px + 16), Open items 76 (header wraps "Open / items", "Could not be read" wraps on spaces), Last fired 88 (date/time lines ~64 px at
12 px + 16; header wraps), Action rate 104 ("Needs a look" badge ~82 px, header word ~76 px, + 16). Rule: no width.
Rule column width = table width - 480: 80 px at 560, 120 at 600, 200 at 680, 620 at 1100.

Triage categories (fixed total 520 px): Category 88, On / Off 104 (Fluent Switch measured from its styles: 56 px track box + label
4 + ~20 + 8 = ~88, + 16), Read by 72, Last N days 72, Recall 104 ("Set too small" badge ~82 px + 16), Edit 80 (small Button min-width
64 px + 16). Guidance: no width. Guidance = table width - 520: 40 px at 560, 80 at 600, 160 at 680, 280 at 800, 580 at 1100.

Text widths are estimates (Segoe UI 14 px semibold ~7.2 px/char, 12 px ~5.8 px/char) from arithmetic, not measured in a browser.

### Minimum table width at which nothing overflows (target <= 560)

- Now: Rules 480 px of fixed columns, so no fixed column overflows from 496 px (Rule keeps >= 16 px); the Rule column is useful (>= 120 px)
  from 600 px. Triage 520 px of fixed columns, no overflow from 536 px; Guidance is cramped (80 px) at 600 and reads well from ~680 (160).
- master (percentages): needs width >= content/percent for the tightest column: Rules Version 66 px / 7% = ~943 px, Open items 88 / 9% =
  ~978 px, Lane 58 / 8% = ~725 px; so ~980 px. Triage Edit 80 / 10% = 800 px, switch ~104 / 11% = ~945 px; so ~945 px.
  So the overflow floor drops from ~980 / ~945 px to 496 / 536 px.
- Trade-off, unresolved: the Triage table has seven columns and ~520 px of them cannot shrink (switch, Edit button, badges). At the
  default centre pane (~600 px) Guidance gets ~80 px and its 140-character preview wraps to many short lines (the full text is in the tooltip).
  Percent widths gave Guidance more at 600 px but overflowed the others. Options for the owner: a shorter preview (~60 chars), moving
  Read by under the category name at narrow widths, or letting the pane resize. Not done: outside this finding's scope.

### Wrapping scope

`overflowWrap: 'anywhere'` removed from the shared Rules `cell`. It is now on `ruleStack` (the code/name div inside the Rule cell) only;
the Action rate stack has none. Classification: removed from the shared `col2` class (which also styled the header); it is on a new
`guidance` class on the Guidance body cell only, plus the `rules` stack of rule codes in Read by (a long unbroken rule code in a 56 px
content width would spill into Last N days; this is a stack div, same class of fix). Lane, On / Off, Version cells are `white-space:
nowrap` (their widths fit the content). Cell keeps only the vertical token padding.

### Tests changed (new files from round 0, extended; existing tests untouched)

- `RuleListLayout.test.tsx`: replaced "Rule header has the largest width" with: each fixed column has a px computed width >= its minimum
  (Lane 64, On/Off 76, Version 72, Open items 76, Last fired 88, Action rate 104) and the body cell has the same width; Rule header has no
  px/% width and the fixed total is <= 480; `overflow-wrap: anywhere` is on the Rule stack and on no cell and not the Action rate stack;
  Lane/On-Off/Version cells are nowrap. The clipping checks (cell computed `display` is `table-cell`, not hidden, stack not clipped) kept.
- `ClassificationLayout.test.tsx`: replaced "Guidance has the largest width" with px minimums (Category 88, On/Off 104, Read by 72, Last N days
  72, Recall 104, Edit 80 - the Edit column >= 80 as the 64 px button + padding), Guidance has no width and fixed total <= 520, and
  `overflow-wrap: anywhere` is on the Guidance cell only.
- Before/after: against origin/master components 19 of 21 fail (the two content-render tests pass); against the previous tip a4a37d7111
  16 of 21 fail; with this change 21 of 21 pass.

### Results (fix round 1)

- `npx jest src/components/OntologyAdmin`: 10 suites, 242 tests pass.
- `npx tsc --noEmit` (Spaarke.UI.Components): exit 0.
- `client-lint check --package ui-components`: ok.
- `npm run build` in `src/solutions/SpaarkeAi`: completed (ribbon build complete, no error output).
- Unverified: no browser measurement; widths are arithmetic from Fluent styles. Main session to check 1280x720 and 1440x900 on dev.
