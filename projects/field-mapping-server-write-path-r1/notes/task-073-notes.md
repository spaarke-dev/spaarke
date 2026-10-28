# Task 073 — RecordFamilyModal (FR-32)

Branch `task/fm-073` (from the project work branch tip `1bf631c84b`). Rigor FULL. Model tier sonnet, effort high.

## What was built

`src/client/shared/Spaarke.UI.Components/src/components/RecordFamilyModal/`

| File | Role |
|---|---|
| `RecordFamilyModal.tsx` | The modal, composed on `SprkModal/presets/BrowseModal` (ADR-050 Layout 2). |
| `recordFamilyService.ts` | FR-31 types, route builder, `fetchRecordFamily`, defensive body parser, de-dupe merge. |
| `index.ts` | Exports (also re-exported from `components/index.ts`, hence from `src/index.ts`). |
| `__tests__/RecordFamilyModal.test.tsx` | 15 tests. |

## Behaviour

- Reads `GET /api/v1/records/{table}/{canonical id}/family` on open (never while closed), through the injected
  `authenticatedFetch` (type `ResponseFetchFn`, so both a throwing and a returning fetch are handled). It sends no
  `kind` or `direction`: the defaults are the full Filing family (owner decision).
- Columns: name, record number, record type, status, modified on. A null value is a blank cell. Rows are grouped by
  level (`depth`, ascending) then table, one table header row per group ("Level 1 · Project"); the opened record
  carries a "This record" badge.
- Truncation: `truncated=true` shows an info notice (no counts) and "Load more" when a continuation exists. Load more
  sends `?continuation=` (URL-encoded) and appends de-duplicated by (table, id). A failed load more keeps the rows and
  shows a fixed inline error.
- Any failure on the first load (404, 400, 401, network, malformed body) is ONE fixed text: "The family could not be
  loaded. Close this window and try again." Nothing distinguishes a 404 from a network error (FR-31 uniform 404).
- Selection: a checkbox per row, kept across Load more. Toolbar "Push field updates" / "Update Assigned To" call
  `onAction(action, rows)` with exactly the ticked `{ table, id }` pairs (ids as the server returned them), in display
  order. Disabled with no selection. With no `onAction` they are disabled with the tooltip "Available after the push
  update". Nothing is called on open. The modal never calls `/field-mappings/push`.
- The BrowseModal header navigator (`‹ N of M ›`, required by the preset) steps a row highlight (starts on the opened
  record); the meta column shows records shown, selected, and the highlighted row's table and level.
- Stale-answer guard: a sequence number drops the answer of a load that was superseded (other record, closed,
  unmounted). A host passing a new `authenticatedFetch` function each render does not retrigger the load (ref).

## FR-31 contract as coded (task 072 must match)

```
IFamilyResponse { anchor: {id, table, name?}; root?: {id, table, name?} | null; truncated: boolean;
                  continuation?: string | null; members: IFamilyMember[] }
IFamilyMember   { id, table, name, recordNumber, recordType, status, modifiedOn, kind, edgeKind, parentId, depth }
```

Field names come from task 072's constraint line (`parentId`, `recordNumber`, ...). The exact JSON shape of `anchor` and
`root` is not written down anywhere; the component needs only `anchor.id` and `anchor.table`.

## Open questions for 072 / 074 / 040

1. `anchor` / `root` shape: coded as `{ id, table, name? }`. If 072 returns something else, change `asRef` in
   `recordFamilyService.ts` only.
2. `depth` numbering: the group header prints the server's number ("Level 0 · Matter"). If 072 numbers ancestors
   negatively or starts at the anchor, the label wants a rename (a one-line change in `groupFamily` / the header).
3. Selectable rows: every row has a checkbox, including the opened record and its ancestors. Push/Assigned To act only
   below the source, and the server refuses any other id per id (FR-34), so this is safe; task 040 may want the opened
   record and ancestors not selectable. Not decided here.
4. Continuation field name on the wire: `continuation` (request query and response), per FR-31 / 071.

## Checks

- Tests: 15 pass (`RecordFamilyModal.test.tsx`); with the SprkModal suites 115 pass / 12 suites. Mutation proof: with
  the de-dupe removed, the selection filter removed and the disabled state removed, 5 tests fail; restored, 15 pass.
  Before the component existed, the suite failed to load (module not found).
- Lint: `eslint src/components/RecordFamilyModal src/components/index.ts` clean.
- Typecheck: `tsc` on the package shows no errors in the new files. Existing errors in other files
  (`@spaarke/sdap-client`, `@spaarke/auth` not resolvable in this worktree) are not from this change. The test file was
  type-checked separately; the only noise is the existing helper's `rootDir` message for `Spaarke.Auth/src`.
- Dark mode: the suite renders under `webDarkTheme` with no console errors, and a source scan asserts no hex / rgb /
  hsl colours in the component sources. Colours are `tokens.*` only; no icons.

## Bundle / size

Library component only; no BFF change. Fluent `Table*`, `Checkbox`, `Badge` are imported from
`@fluentui/react-components` (already a dependency; tree-shaken). The TrackingFieldTrio PCF bundle delta is measured in
task 074 when the modal is wired. The Table family may be new to that bundle, so 074 should check it.

## Rework line

Rework: fix-round-1 (independent review of 1c3e3d7d7c): 2 F2 and 1 F1 fixed. Before that, 2 test-expectation fixes after
the first run (a duplicate "Current record" meta item that repeated the name, now removed; the anchor badge in the name
cell) and 1 tooltip assertion loosened (two buttons share the tooltip text).

## Fix round 1 (review of 1c3e3d7d7c)

- F2 `recordFamilyService.ts`: `parseFamilyResponse` now throws (so the modal shows its one fixed error state) when a
  member is not an object, lacks an id or table, or has a depth that is not a non-negative integer (no default to 0),
  and when the anchor ({table, normalised id}) is not among the members. The anchor check applies to the FIRST page
  only (`requireAnchor`, off when a continuation was sent): a "load more" page carries only the records beyond the
  first, and 071 guarantees the anchor in the first view, not in every page. Requiring it on a later page would break
  load more.
- F1 `RecordFamilyModal.tsx`: `TABLE_LABELS` now covers the three roots and every child table in
  `SecureChildLineage.cs` (32 tables). The fallback strips `sprk_`, splits on `_` and title-cases each word.
- F2 `RecordFamilyModal.tsx`: both rejection handlers (first load, load more) `console.warn` the cause and still render
  only the fixed text; the header comment now says so.
- Tests: 22 in the suite (was 15): 5 malformed-body cases (missing table, depth as a string, negative, fractional,
  anchor absent), the console.warn case, and a label case. All 7 fail against 1c3e3d7d7c's sources. With the SprkModal
  suites: 122 passed / 12 suites. ESLint clean.

## Known limits (from the review; not fixed in task 073)

- A refused or invalid continuation shows the inline error but offers no way to restart the load; the person closes and
  reopens the modal.
- The id-format reload: a change of `recordId` only in brace/case form (same record) still reloads the family.
- Anchor and ancestors are selectable; deferred to task 040 (the server refuses ids outside the source's children).
- Test gaps: no test for the highlight navigator, the meta column, a stale answer after a record change, or a failed
  load more clearing on retry.
- `selectCell` has a 40px `minWidth` literal instead of a spacing token.
- Dates use the browser locale (`toLocaleDateString(undefined, ...)`).
- The header navigator only moves a row highlight (BrowseModal requires `nav`); it does not open or act on a record.
