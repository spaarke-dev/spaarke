# Task 033 notes: DataGrid Money columns sort by value, Export writes the displayed text (FR-20, #2446, #2447)

Status: implemented and verified in the working tree, NOT committed (per brief). POML status, TASK-INDEX and current-task.md are not touched (the main session closes the task after review and merge).
Rigor FULL, sonnet @ high, directional steps. Run in the main worktree by an implementer agent, no sub-agents.

Framework owners' OK: APPROVED by the owner on behalf of the dataset-grid owners (2026-11-24)

## Value-shape finding (step 1)

What a Money cell holds when it reaches the grid, from the code that sends it:

- `src/server/api/Sprk.Bff.Api/Services/Dataverse/FetchService.cs`, `ProjectEntity` (~`:150-180`, remarks at `:138-149`): attributes are copied as raw SDK values and NOT flattened. System.Text.Json (Web defaults) writes `Money` through its public properties, so on the wire a Money column is the object `{ "value": 1200, "extensionData": null }`, never a bare number. The display text is in `row["@formattedValues"][column]` (the fetch's `FormattedValues`, which the code comment says "carries the display strings for OptionSet, EntityReference, DateTime, Money attributes").
- `BffDataverseClient` returns that JSON as it arrives (same remarks), so the external SPA's grids see the object form. Task 028 found the same.
- A Money column's renderer is `currency`: `configResolution.ts:205` maps attribute type `Money` to `currency`. So `renderer === 'currency'` is the one test for "Money column" (a config override to another renderer opts the column out, for display, sort and export alike).

Why the old comparator mis-sorted: it compared numerically only when BOTH raw values were JS numbers. A Money object failed that test and fell through to the display-string compare (`renderCellValue` + `formattedLabelOf`), so "$10.00" came before "$9.00" and "$100.00" before "$9.00" as text. Why the old export mis-wrote: `exportCsv` wrote `record[column.name]`; for the SDK object that is `{"value":1200,"extensionData":null}` JSON (it has no `.name`/`.label`), for a plain number a bare `1200`.

The comparator handles three shapes (`moneyAmountOf`): the SDK object (`{ value }`, `{ Value }` too), a plain finite number, a numeric string. Anything else has no amount (a display label like "$1,200.00", other objects, arrays, `null`, `undefined`, `''`, NaN/Infinity, `'0x10'`).

I read `src/client/external-spa/src/utils/rawWholeNumber.ts` for the unwrapping pattern. The shared lib has its own small helper (`moneyAmountOf` in `DataGrid.tsx`); nothing is imported from the SPA. It differs on purpose: it accepts decimals and numeric strings, which a whole-number helper does not.

## What was built

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` | New exported `moneyAmountOf`, `compareColumnValues` (the old comparator extracted verbatim, plus a Money branch first), `moneyExportText`; the column `compare` now calls `compareColumnValues`; `exportCellText={moneyExportText}` on the command bar |
| `.../DataGrid/commandBar/csvExport.ts` | `exportCsv` gets an OPTIONAL 5th argument `cellText`, and the exported type `ExportCellText`. Absent = byte-identical output |
| `.../DataGrid/commandBar/defaults.ts` | `DefaultHandlerContext.exportCellText?` (optional); `defaultExportExcelHandler` passes it on |
| `.../DataGrid/commandBar/CommandBar.tsx` | Optional prop `exportCellText`, placed in the handler context |
| `.../DataGrid/index.ts` | Exports the `ExportCellText` type |
| `.../DataGrid/__tests__/DataGrid.moneySort.test.ts` | NEW, 34 cases (pure) |
| `.../DataGrid/__tests__/DataGrid.moneySort.render.test.tsx` | NEW, 7 cases (rendered grid; see "Test scope") |
| `.../DataGrid/commandBar/__tests__/csvExport.test.ts` | 3 cases appended (`cellText`); existing cases unchanged |
| `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` | One paragraph describing the behaviour (docs updated with the behaviour) |

`DataGridProps` is NOT changed (the new prop is on `CommandBarProps`, an internal seam; the grid wires it). Filename, BOM, CRLF and quoting rules are untouched.

Why a hook (`cellText`) and not formatting inside `csvExport.ts`: `renderCellValue` lives in `DataGrid.tsx`, which imports the command bar, so importing it back into `csvExport.ts` would be a circular import. The grid hands the command bar a function instead. The existing `csvExport.test.ts` also has a `currency` column exported as a bare `100` and must pass unchanged, so the Money text cannot be the default for `exportCsv` itself; it is opt-in per call, and the grid always opts in.

## Empty-value sort rule (stated and tested)

A Money row with no amount (`null`, `undefined`, a missing key, `''`, or text with no number) sorts LOWEST: first when ascending, last when descending. Two empty rows tie. This is the place an empty cell took before (its display text is `''`, which `localeCompare`s below any "$..."), so it is "as today's empties do". It is consistent in both directions because Fluent descending is `compare * -1` (`useTableSort.js`), so the empties mirror. Tested in `DataGrid.moneySort.test.ts` (pure, both directions) and in the rendered grid (Fluent click sort and the controlled sort).

## How it works with both sort paths

Fluent's `useTableSort` (`node_modules/@fluentui/react-table/lib/hooks/useTableSort.js:63-67`) calls `column.compare(a, b) * (ascending ? 1 : -1)` for the sort state it is given. Task 032's controlled path only changes who holds the state (`userSort` -> `effectiveSort` -> `fluentSortState` -> `sortState`); the comparator is the same function. Both are tested in the rendered grid: a header click (uncontrolled, ascending then descending) and `initialSettings.sort` (controlled, ascending and descending).

## Tests: fail before, pass after

Method: the old `DataGrid.tsx`, `csvExport.ts`, `defaults.ts`, `CommandBar.tsx`, `index.ts` (from `HEAD`) were put back, the three suites run, then my files restored (copies kept in the scratchpad, restore verified by grep).

| Run | Result |
|---|---|
| Before (old sources, new tests) | 3 suites failed; 37 failed / 28 passed of 65 |
| After | 3 suites passed; 65 / 65 |
| Whole `src/components/DataGrid` folder, after | 26 suites, 259 tests passed (44 of them new in this task) |

Of the 28 that pass before: they are the pre-existing `csvExport` cases (unchanged), the must-not-fire controls whose "unchanged" outcome the old code satisfies (the text column holding digits in the rendered grid, and the "declines/no hook" case), by design. The behaviour tests fail before: rendered Fluent sort and controlled sort (2 of 2), export as displayed and "every other column exports as before" (2 of 2; the second fails before only because Money text differs), and every Money comparator case.

## Test scope (acceptance criterion 7)

Stated contract and criteria: $9 / $10 / $100 ascending and descending; empties in both directions; non-Money number, text, option-set and date columns sort as before; Money export text equals the displayed text; non-Money export unchanged; both sort paths (named in the brief).
Beyond that, one line each:
- `moneyAmountOf` table (19 rows): the wire shape (object, plain number, numeric string) is the root of the bug, so each accepted and rejected shape is pinned.
- Negative and zero amounts: guards a falsy-value (`||`) mistake in the comparator.
- `DataGrid.moneySort.render.test.tsx` (second new file; the POML lists one): the pure test cannot prove Fluent's click sort, the controlled sort or the Export button wiring (`DataGrid` -> `CommandBar` -> `defaults` -> `exportCsv`), which are exactly where an extraction like this breaks.

## Check results

| Check | Result |
|---|---|
| `src/client/shared/Spaarke.UI.Components`: `npx tsc --noEmit` | exit 0 |
| `npx jest src/components/DataGrid` | 26 suites, 259 passed |
| `npx eslint` on the 8 changed/new files | 0 errors (26 warnings, all "unused eslint-disable directive"; the same pre-existing kind appears in untouched lines of `CommandBar.tsx`, `defaults.ts`, `csvExport.test.ts`) |
| `npx prettier --check` on the changed files | clean |
| `node scripts/quality/client-lint/client-lint.mjs check --package ui-components` (repo root) | ok, no prune needed |
| `src/client/external-spa`: `npm run build` | ok (vite, 2620 modules); tracked tree unchanged by the build |

## Step 9.5 (inline code-review + adr-check, no agents)

- F1-F4: none found.
- ADR-012: no Xrm, no entity names, no data fetching in the shared lib; `DataGridProps` unchanged; the new hook is a plain callback; types exported. ADR-021: no styling touched. ADR-022: nothing React-version specific. ADR-038: tests use the existing network-seam mocks only (the `Blob` shim and `URL.createObjectURL` stub are test-environment seams, as in `csvExport.test.ts`); each must-fire has a must-not-fire pair. No violation, no Path A/B/C.
- §11: existing = `renderCellValue`/`formattedLabelOf` and `exportCsv` (reused, extended by one optional argument); no new component, endpoint, DI, package or Dataverse column. Cost of nothing = #2446 / #2447.
- Step 9.5 rule 6 pattern grep (a `compare`/`localeCompare` on a Money-shaped object elsewhere in the shared lib): matches in `CommunicationTimeline.buildTimeline.ts`, `ViewService.ts`, `TagFilter.tsx`, `decisionRecordsView.ts`; none sorts grid Money values, listed only for completeness, not opened.
- Escalation trigger (formatted export for every column type): not fired. Export changed for `currency` columns only; `moneyExportText` returns `undefined` for every other renderer (tested for 7 renderers).

Known limits (K, no fix, one line each):
- K2: a Money cell drawn by a host `overrides.columnRenderers` entry (a React node) is exported through `renderCellValue`, as the brief says, not the custom node's text.
- K2: a non-empty Money value that carries no readable amount (an unlabelled non-numeric string) sorts as empty.
- K2: amounts in different currencies sort by raw amount (no conversion); grids show one currency per record set today.
- K4: a column the configuration overrides to a non-`currency` renderer is not treated as Money, even if its attribute type is Money (display, sort and export agree).

## Needs owner / main session

1. Framework owners' OK: approved by the owner, 2026-11-24 (see the line at the top).
2. Live check not done (no live actions): on the dev Invoices grid, sort Total amount ascending/descending, Export to CSV, compare an exported Total amount with the cell on screen, console clean, dark mode unchanged.

## Out-of-scope defects found

Two pre-existing defects found by the adversarial review, both outside FR-20's Money scope, filed:
- D-34 #3045: the CSV export writes option-set cells and nameless lookups as JSON. Pinned as current behaviour by the render test's must-not-fire case, which cites the issue. Formatted export beyond Money is this POML's escalation trigger, so it was raised with the owner rather than fixed here.
- D-35 #3046: an unlabelled currency cell is always formatted as USD; since this task, the CSV export carries the same "$" text.

One more was found and fixed in scope (below).

## Fixed in scope (coordinator request after the first report)

- Defect (pre-existing): a Money object with NO `@formattedValues` label did not display as money. `renderCellValue`'s `currency` branch did `Number(value)` on the SDK object `{ value, extensionData }`, got NaN and returned `String(value)`, so the cell showed `[object Object]` (the first report said "raw JSON"; reading the branch again, it is `String(value)`). The Money export, which uses the same path, wrote the same text.
- Fix: in the `currency` branch, an object value is unwrapped with `moneyAmountOf` before formatting (`DataGrid.tsx`). It runs only when there is no usable label, because a label returns earlier (`usesFormattedLabel`), so labelled cells are unchanged; a plain number and a numeric string take the old path; an object with no readable amount keeps the old fallback text. Export follows through `moneyExportText` with no further change.
- Tests (regression + must-not-fire): `DataGrid.moneySort.test.ts` +3 (unlabelled object shows the client currency format and not JSON; a labelled object still shows its label; a plain number and a numeric string format as before); `DataGrid.moneySort.render.test.tsx` +2 (an unlabelled `{ value: 1234.5, extensionData: null }` cell shows the client currency format and exports the same text; a labelled cell still shows its label, not the client format).
- Fail before / pass after: with only the `renderCellValue` change reverted, the two moneySort suites ran 2 failed / 39 passed of 41 (the two failures are the two unlabelled-object regression tests; the must-not-fire cases pass before, by design). With the fix: all pass. Whole `src/components/DataGrid` folder: 26 suites, 259 passed (was 254; 44 new in this task). `npx tsc --noEmit` exit 0; `client-lint check --package ui-components` ok; eslint 0 errors and prettier clean on the touched files.

## Adversarial review (opus, one pass) and fix round 2 (main session)

- F2: six unused `eslint-disable-next-line` directives in `DataGrid.moneySort.render.test.tsx` removed.
- K1 → fixed: numeric text that overflows a double (`"1e400"`) read as Infinity, so the comparator could compute Infinity - Infinity. `finiteNumberOf` now returns null for a non-finite result. Regression: `DataGrid.moneySort.test.ts` +2 `moneyAmountOf` cases (overflowing string, object carrying one).
- After the round: `src/components/DataGrid` 26 suites, 261 passed; `tsc --noEmit` exit 0; eslint 0 errors (remaining warnings are older `no-console` directives in untouched lines); `client-lint check --package ui-components` ok.

rework: fix-round-2 task 033
