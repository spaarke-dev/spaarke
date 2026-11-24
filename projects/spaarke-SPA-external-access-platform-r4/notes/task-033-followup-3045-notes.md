# Task 033 follow-up: DataGrid CSV export writes the text each cell shows (D-34, #3045)

Status: implemented and verified; committed after PR #3048 merged (2026-11-24), in its own PR. The rounds below are in order; the last round's limits are the current ones. sonnet @ high, no sub-agents. The owner approved the fix and the shared-grid export change on behalf of the dataset-grid owners (2026-11-24).

## Defect

Export to Excel wrote an option-set cell, and a lookup with no `name`, as raw JSON (`{"value":1,"extensionData":null}`), because task 033 only routed Money columns through the grid's `exportCellText` hook; every other column fell to `escapeCsvField`'s object fallback (`JSON.stringify`). The grid itself drew the `@formattedValues` label for those cells (task 061), but drew an unlabelled option-set object as JSON too (`renderCellValue`'s default branch), and `[object Object]` for an object under the `currency` / `percentage` renderers.

## Behaviour chosen

Export now goes through the one display path, `renderCellValue(value, renderer, formattedLabelOf(record, name))`.

| Cell | Display (grid) | Export (CSV) |
|---|---|---|
| Option set / lookup / Money with an `@formattedValues` label the grid uses | label | the label |
| Lookup `{ id, name }`, no label | name | the name |
| Object with a `label` string, no name | the label (new: `csvExport` already used `label`, display did not) | the label |
| **Object with no label, no name** (an unlabelled option set `{ value: 1, extensionData: null }`, `{ id }`, `{}`) | **empty** (was JSON) | **empty string** (was JSON) |
| Array | readable items, comma separated (was JSON) | same text |
| Money object, no label | client currency format of its amount (unchanged, task 033) | same |
| Plain string / number / boolean, no label | `String(value)` | byte-identical to before |
| Date / dateonly / datetime / percentage, plain value | locale text (unchanged) | **raw value, as before** (ISO date, 0-1 fraction) |
| `Date` instance | unchanged | raw (`exportCsv` writes the ISO string) |
| Primary-name column | drawn with the `default` renderer | exported with it (a label wins; empty stays empty, the grid's "(no name)" is a display placeholder only) |
| Column drawn by a host `overrides.columnRenderers` React node | the node | raw value, as today (K2) |

Choice for an unlabelled option set: I did NOT unwrap `{ value }` to its number (the number means nothing without its label). The display and the export are both empty, as the brief ordered ("prefer the formatted label, else empty").

Code:
- `DataGrid.tsx`: `moneyExportText` generalised and renamed to `cellExportText` (same `(record, column)` shape, so the `exportCellText` hook signature and `exportCsv`'s `cellText` argument are unchanged). New private `objectCellText`; `renderCellValue` returns it for any non-`Date` object outside the `currency` renderer, and for a Money object with no readable amount; the default branch is now `String(value)`. The grid wires a small `useCallback` closure (`exportCellText`) that applies K2 for host-rendered columns and otherwise calls `cellExportText`.
- `commandBar/csvExport.ts`: comment only (the `cellText` doc says every column, not Money). `escapeCsvField` and all quoting (RFC 4180 quotes, doubled quotes, CRLF, BOM) are unchanged (round 3 below adds a text guard).
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`: the task 033 "Money columns" paragraph now says every column exports its displayed text.
- Not changed: `CommandBar.tsx`, `defaults.ts`, `index.ts` (the `ExportCellText` type and the optional `exportCellText` prop stay as they were).

Judgement call to flag: a plain number or boolean that carries a `@formattedValues` label the grid uses (for example a numeric option set from `Xrm.WebApi`, value `1`, label "Draft", or a decimal with a server-formatted string) now exports the label, as the grid draws it, where it used to export the raw `1`. This follows the brief's rule ("exports the text the cell shows"). A numeric column whose server label is a formatted string such as "1,234" therefore exports that text rather than the bare number.

## Files changed

- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/commandBar/csvExport.ts` (comment)
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/__tests__/DataGrid.moneySort.test.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/__tests__/DataGrid.moneySort.render.test.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/__tests__/DataGrid.formattedValues.test.tsx` (it pinned the old JSON display; updated)
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`
- this file

## Tests

- `DataGrid.moneySort.render.test.tsx` (rendered grid, Export to Excel): the pinned must-not-fire case that cited D-34 now expects the label (`INV-ten,$10.00,Draft,10,"Smith, Jo"` and the other three rows); new: an unlabelled option set and a nameless lookup export nothing and the cells show nothing (no JSON, no `[object Object]`); a lookup with a name exports the name, with a label the label; text and number columns (and a plain number under Money) unchanged, including CSV quote escaping; K2 pinned (a host-rendered column keeps the raw-value export, other columns are as displayed).
- `DataGrid.moneySort.test.ts` (pure): `moneyExportText` cases moved to `cellExportText` (Money cases kept; the "declines every other renderer" case is replaced because it is the behaviour that changed); new cases: option set label under three renderers, lookup label vs name, unlabelled objects empty under every renderer, `{ value, label }`, primary-name column, plain string/number/boolean/null unchanged, plain date and percentage keep the raw value (even with a server label), `Date` instance left to `exportCsv`.
- `DataGrid.formattedValues.test.tsx`: the display tests that pinned JSON for an unlabelled object now pin empty; new cases for a `label`-only object and an array.

Counts: see the checks table below.

### Fail before / pass after

Only `DataGrid.tsx` was put back to `HEAD`, then the three changed suites run (copies of my version kept in the scratchpad and restored; restore verified with `git diff --stat`):

| Run | Result |
|---|---|
| Before (HEAD `DataGrid.tsx`, new tests) | 3 suites failed; 21 failed / 56 passed of 77 |
| After | 3 suites passed; 77 / 77 |

Failing before: the 12 pure `cellExportText` cases (the function did not exist, so these prove only that the old source lacks it), the 4 rendered export cases (option set exports label, unlabelled option set and nameless lookup empty, lookup name/label, text and number unchanged: the old export wrote the JSON), the 2 rendered display cases in `DataGrid.formattedValues.test.tsx`, and 3 `renderCellValue` display cases. The K2 case and the date/percentage must-not-fire cases pass before, by design.

## Checks

Run from `src/client/shared/Spaarke.UI.Components` unless noted.

| Check | Result |
|---|---|
| `npx jest src/components/DataGrid` | 26 suites, 276 tests (was 261): 275 passed, 1 failed (`DataGrid.persistedSettings` "keeps the column filters while the key stays the same", an untouched suite; it timed out while tsc/eslint/client-lint ran in parallel, and the suite passes 18/18 when run alone, twice) |
| `npx tsc --noEmit` | exit 0 |
| `npx eslint` on the 5 touched source/test files | 0 errors; 4 warnings, all pre-existing "unused eslint-disable directive (no-console)" in untouched lines of `DataGrid.tsx` (none added) |
| `npx prettier --check` on the touched files | clean |
| repo root `node scripts/quality/client-lint/client-lint.mjs check --package ui-components` | ok |
| `src/client/external-spa`: `npx tsc --noEmit` | exit 0 |

## Known limits

- K2: a column drawn by a host `overrides.columnRenderers` React node keeps the raw-value export (an option set there still exports as JSON, a lookup as its name/label via `escapeCsvField`). Fixing it would need the node's text, which a React node does not have; the alternative (export the label for it) would change what hosts get today, so it is left as the brief says.
- K2: dates and percentages export the raw ISO date / 0-1 fraction, not the locale text the grid draws, on purpose (a spreadsheet wants the machine value, and the brief keeps dates as before). An object in a date or percentage column exports empty.
- K2: an unlabelled currency cell is still formatted as USD in display and export (D-35 #3046, not touched).
- K2: `exportCsv` called directly by another consumer without `cellText` still writes JSON for an object with no name/label (`escapeCsvField` unchanged); only the DataGrid's own Export command goes through the display path.
- K3: not verified live (no dev-environment export done); verified by the rendered-grid tests only.

## Review fix round (F1, F2, K5)

Supersedes the K2 host-renderer and direct-`exportCsv` limits above (both are fixed here). Still uncommitted.

**F1 (host-rendered columns exported JSON).** The Invoices widget draws `sprk_reviewdetail` (an option set, `{ value: 659490000, extensionData: null }`) through `overrides.columnRenderers`; the first version skipped `cellExportText` for such columns, so the CSV got JSON while the cell showed "Waiting for billing terms". Now the grid's `exportCellText` closure (`DataGrid.tsx`) calls the host renderer with the same arguments the cell render uses, `(record[column.name], record)`:
- it returns a string: that string is exported (it is the cell text, and `reviewDetailCellText` applies the FR-11 statuscode gate, so a closed gate exports an empty field);
- it returns anything else (an element) and the raw value is an object (not a `Date`), or the column is Money: `cellExportText` (label, name, Money text, or empty; never JSON);
- otherwise (element + primitive value): `undefined`, today's raw export.
A renderer that throws is treated as returning no text. The primary-name column never goes through a host renderer (the cell render checks it first).

**F2a.** Pure tests pin a labelled number/boolean: `true` + "Yes" and `1234` + "1,234" export the label; the same values with no label export `true` / `1234`.

**F2b.** `escapeCsvField` (`commandBar/csvExport.ts`) now calls the shared rule instead of its own name/label/JSON chain. The rule moved to a new import-free module `DataGrid/objectCellText.ts` (exported `objectCellText`), used by `renderCellValue` and by `escapeCsvField`, so display and export cannot drift; there is no import cycle. `exportCsv`'s signature is unchanged. Consequence: a direct `exportCsv` caller with no `cellText` now writes an empty field, not JSON, for an object with no name/label (two existing tests that pinned the JSON were updated: `escapeCsvField` arbitrary object, and "a hook that always returns undefined gives the same file as no hook", where the Money object is now an empty field).

**K5.** `objectCellText` unwraps an SDK `AliasedValue` (an object with a string `attributeLogicalName` and a `value`) and applies the same rule to the inner value (string/number as text, null empty, a lookup its name, an unlabelled object empty). An object that only has `entityLogicalName` + `value` is not unwrapped. Also in the shared rule: a `Date` inside an array is its ISO text.

Files touched this round: `DataGrid.tsx`, `objectCellText.ts` (new), `commandBar/csvExport.ts`, and the tests `DataGrid.moneySort.render.test.tsx`, `DataGrid.moneySort.test.ts`, `DataGrid.formattedValues.test.tsx`, `commandBar/__tests__/csvExport.test.ts`.

**Tests.** Rendered grid (Export to Excel): the K2 case that pinned JSON is replaced by (1) a string-returning renderer exports its string (also: a closed-gate row exports empty, the renderer is called with the cell's value and record, no `extensionData` in the file); (2) must-not-fire: an element-returning renderer with a primitive value is unchanged; (3) an element-returning renderer with an object value exports its label / empty, never JSON. Pure: labelled number/boolean, AliasedValue (`cellExportText`, `renderCellValue`, `escapeCsvField`), the two csvExport updates. There is no Invoices export seam in the SPA (its tests substitute the shared grid), so no SPA-side export test; the SPA's renderer contract (returns a string for the row) is already pinned in `external-spa/tests/ReviewDetail.test.tsx`.

**Fail before (F1).** With only the `exportCellText` closure put back to the first version (host-rendered column returns `undefined`), the rendered export suite ran 2 failed / 11 passed of 13: the string-renderer case and the element-renderer-with-object case fail (the file held the option-set JSON); the element-with-primitive must-not-fire case passes before, by design. With the fix: 13 / 13.

**Checks.**

| Check | Result |
|---|---|
| `npx jest src/components/DataGrid` | 26 suites, 282 / 282 passed (the earlier `persistedSettings` flake did not recur when nothing ran in parallel) |
| `npx tsc --noEmit` (ui-components) | exit 0 |
| `npx eslint` on the 7 touched files | 0 errors; 9 warnings, all pre-existing "unused eslint-disable directive" (4 in `DataGrid.tsx`, 5 in `csvExport.test.ts`), none added |
| `npx prettier --check` on the touched files | clean |
| `client-lint check --package ui-components` | ok |
| `external-spa`: `npx tsc --noEmit` | exit 0 |
| `external-spa`: `npx vitest run tests/ReviewDetail.test.tsx tests/GridWidgetBody.hostCommands.test.tsx` | 2 files, 44 / 44 passed |

Known limits after this round: dates and percentages still export the raw ISO date / 0-1 fraction (owner decision pending); an unlabelled currency cell is still USD (D-35 #3046); a host renderer that returns an element for a primitive value keeps the raw value; not verified on a live dev grid.

## Round 3: owner decisions (2026-11-24)

The owner answered the two open questions: "dates should be formatted as shown not raw utc", and fix the export text-hardening finding in this PR (the finding's detail was reported to the owner privately; it predates task 033).

**Dates.** `cellExportText` (`DataGrid.tsx`) now returns the text `renderCellValue` draws for the `date`, `dateonly` and `datetime` renderers: a Date Only value keeps its calendar day (task 098 rule), a date-time is the viewer's local date and time, and the client format still wins over a server label, as on screen. An empty or invalid date is an empty field (never "Invalid Date"). The host-renderer branch of the grid's `exportCellText` treats a date column like Money: an element-returning host renderer still exports the date the grid would draw. Percentages keep the 0-1 fraction, and a `Date` instance in a non-date column keeps `exportCsv`'s ISO text.

**Text guard.** `escapeCsvField` (`commandBar/csvExport.ts`) passes text (not number, bigint or boolean values) through a new exported `asSpreadsheetText`: text whose first character is `=`, `+`, `-`, `@`, tab or CR gets a leading apostrophe, so a spreadsheet shows it as written. Signed amounts ("-$1,200.00", "-1.200,00 €", "+5%"), signed exponent numbers ("-1e-7") and a lone sign are unchanged, so negative amounts stay numbers. It applies to header labels, raw values, object names/labels and the grid's `cellText`, before RFC 4180 quoting. The grid's cells on screen are unchanged.

**Pre-existing flake fixed (task 032 test).** `DataGrid.persistedSettings` "keeps the column filters while the key stays the same" failed under the parallel suite (4 of 4 runs alone on the committed code, before any change of mine): its `applyTextFilter` waited a fixed 120 ms for the filter popover, and a late grid re-render could close the popover before the textbox was read. The helper now waits for the popover's textbox and Apply together and reopens it (at most 3 times). After: 295 / 295 in two full parallel runs.

**Tests.** `csvExport.test.ts`: `asSpreadsheetText` must-fire (each lead character) and must-not-fire (amounts, exponent numbers, lone sign, ordinary text); `escapeCsvField` with quoting and object names; number/boolean values untouched; the `cellText` hook's text is guarded. `DataGrid.moneySort.test.ts`: the date must-not-fire case is replaced by dates exported as drawn (date, dateonly, datetime, a `Date` instance), the Date Only calendar day, empty/invalid dates empty; percentage and non-date `Date` must-not-fire kept. `DataGrid.moneySort.render.test.tsx` (rendered grid, metadata can now be swapped): a date-time column exports the on-screen local text, a Date Only column its calendar day, an element-returning host renderer on a date column still exports the date; an evaluated-lead text and lookup name get the apostrophe while the cell on screen is unchanged. Fail before: the new tests import `asSpreadsheetText` (absent before) and assert date text where the old `cellExportText` returned undefined.

**Checks.**

| Check | Result |
|---|---|
| `npx jest src/components/DataGrid` | 26 suites, 295 / 295, two full parallel runs |
| `npx tsc --noEmit` (ui-components) | exit 0 |
| `npx eslint` on the touched files | 0 errors; 9 pre-existing "unused eslint-disable directive" warnings, none added |
| `npx prettier --check` | clean |
| `client-lint check --package ui-components` | ok |
| `external-spa`: `npx tsc --noEmit` | exit 0 |
| `external-spa`: `npx vitest run ReviewDetail GridWidgetBody` | 2 files, 44 / 44 |

Known limits now: percentages export the 0-1 fraction; an unlabelled currency cell is USD (D-35 #3046); a host renderer that returns an element for a primitive, non-date value keeps the raw value; an exported date is the viewer's locale text, so a spreadsheet in another locale may read it as text; not verified on a live dev grid.

## Round 3 review (opus, one independent pass, 2026-11-24)

The export text-hardening detail was reported to the owner privately, not in the repo (public repository); only the behaviour is recorded here.

**F1 fixed.** `asSpreadsheetText` now also checks each part of a field after a `;`, tab or line break, and covers the full-width forms of the lead characters. Ordinary text and signed amounts are unchanged. Tests in `csvExport.test.ts` (must-fire per separator and full-width form; must-not-fire for ordinary text and amounts after a separator). Fail before: the previous guard left those inputs unchanged.

**F2 fixed.** The Date Only export assertion moved to `dateOnlyColumns.tz.test.ts` (America/New_York environment), so a UTC CI runner cannot pass it trivially; it also asserts the result is not the previous day.

**Known limits (K):**
- An exported date is the viewer's locale text (owner decision "as shown"), so a spreadsheet set to another locale may read a day/month date differently (for example `10/2/2026`). Raised with the owner: keep as shown, or write Date Only values as `yyyy-mm-dd`.
- A phone number such as `+1 (555) 123-4567` is exported with a leading apostrophe.
- The task 032 test helper `applyTextFilter` reopens the filter popover (at most 3 times), so no test pins "the first open stays open".
- Further hardening notes were reported to the owner privately.

Checks after the fixes: grid jest 26 suites, 299 / 299; tsc exit 0; eslint 0 errors; prettier clean.
