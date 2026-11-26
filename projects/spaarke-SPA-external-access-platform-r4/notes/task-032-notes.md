# Task 032 notes: DataGrid opt-in persisted widths/sort + Clear all (FR-19, #2489)

Status: implemented and verified in the working tree, not committed (per brief). POML status, TASK-INDEX and current-task.md are NOT updated (the caller does Steps 10-11).
Rigor FULL, sonnet @ high, directional steps. Starting point: PR #3000's `userColumnWidths` state in `DataGrid.tsx`; built on it, not re-derived.

## What was built

| File | Change |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` | Three optional props + types `DataGridSettings`, `DataGridSortSetting`; state and effects (see API) |
| `.../DataGrid/index.ts` | Exports the two new types |
| `.../DataGrid/__tests__/DataGrid.persistedSettings.test.tsx` | NEW, 15 cases |
| `src/client/external-spa/src/services/gridSettingsStore.ts` | NEW: per-browser `localStorage` store, fail-open |
| `src/client/external-spa/src/widgets/useGridSettings.ts` | NEW: hook (load, save, reset, builds the Reset columns host command). A second small file beyond the POML's list: the store is pure, the hook needs React |
| `src/client/external-spa/src/widgets/GridWidgetBody.tsx` | Uses the hook; Reset columns appended after the host's own commands |
| `src/client/external-spa/src/components/DocumentLibrary.tsx` | Same for the record Documents grid |
| `src/client/external-spa/tests/GridSettings.test.tsx` | NEW, 27 cases |
| `src/client/external-spa/tests/GridWidgetBody.hostCommands.test.tsx` | 2 assertions updated (see Deviations) |

`ExternalDataGrid.tsx` is NOT edited (the new props are ordinary `DataGridProps`, which its `Omit<DataGridProps,'showViewSelector'>` type carries). `RECORD_ACTION_ICONS` is NOT edited.

## Prop API (PROPOSAL, needs the dataset-grid framework owners' agreement)

No framework owner has an active session, so the agreement was not obtained. It is implemented as proposed and listed under "needs owner". Not recorded as agreed.

```ts
export interface DataGridSortSetting { columnId: string; direction: 'ascending' | 'descending' }
export interface DataGridSettings {
  widths?: Readonly<Record<string, number>>;  // column name -> px, only columns the user resized
  sort?: DataGridSortSetting;                  // absent = the configured order
}
// on DataGridProps, all optional:
initialSettings?: DataGridSettings;                  // read ONCE at mount
onSettingsChange?: (settings: DataGridSettings) => void;
settingsResetKey?: number | string;                  // a CHANGE (not mount) returns to configured widths + order
```

Semantics:
- `initialSettings.widths` seeds the same `userColumnWidths` map a drag fills. So they are "dragged widths that were kept": clamped to the column minimum, auto-fit goes off while any exists, undragged columns keep the container fit. Unusable entries (unknown column, non-finite, <= 0, non-number) are dropped; a sort on an unknown column, or on a grid with `enableSorting` off, is not applied or reported.
- `initialSettings.sort` becomes the grid's sort state. Read once; a later change is ignored (documented).
- `onSettingsChange` fires when the (sanitised) widths or sort differ from what was last reported or handed in: a resize step, a sort click, a reset that cleared something. NOT on mount, not in StrictMode, not for an equal value. It can fire on every pointer step of a drag (the host should be cheap or debounce; the SPA store is a tiny sync write, not debounced).
- `settingsResetKey`: when the value changes the grid clears `userColumnWidths` (auto-fit returns) and the user sort, and reports empty settings. The host owns the command (`hostCommands` slot, already generic) and clears its own store.
- The grid stores nothing (a grep of the DataGrid folder, excluding tests, finds no `localStorage`/`sessionStorage`).

Why additive: (1) every prop is optional and nothing reads them when absent; (2) when none of the three is passed, the Fluent `sortState`/`onSortChange` props are not passed at all (a conditional spread), so Fluent's sort stays uncontrolled exactly as before; (3) the only change on the default path is the `useState` initialiser for `userColumnWidths` (now `sanitizeInitialWidths(undefined)` = `{}`, the same value) and effects that no-op; (4) no existing prop changes meaning; (5) the types are new names, exported from the DataGrid index.

Alternatives considered and rejected: a controlled `sortState`/`columnWidths` pair (forces every host to own state); an imperative `ref.reset()` (the shared lib has no ref API on DataGrid; the key idiom matches the existing `rowSourceKey`); a single `persistence={{...}}` object (identity churn; three props read more simply and match how `hostCommands`/`rowSource` are documented).

## Decisions

1. **Reset columns icon: `RECORD_ACTION_ICONS.refresh`** (ArrowClockwise). The map has no reset/undo entry, and the map is frozen and pinned by a `toEqual` test in `RecordCommandBar.test.tsx`; adding one is a reviewed change to task 001's constant. The grid's own Refresh uses a different glyph (ArrowSync), so the two are distinguishable. Needs owner: accept `refresh`, or add `resetColumns: ArrowReset20Regular` to the map (and its pinned test).
2. **Reset is a `hostCommands` entry** (the generic slot already shipped for Submit invoice), id `reset-columns`, label `Reset columns`. The command bar renders the label as visible text, `aria-label` and tooltip (tested).
3. **Store key** `sprk.ext.gridSettings.v1.{configId lower-cased}`, value `{v:1, widths, sort?}`. Per POML the key is the config id only, NOT per user. (`recordSectionState.ts` hashes a user id into its key; a column width is not personal data, and the POML says per grid.) Two people sharing one browser share a grid's widths. Says the contrary if the owner wants per-user.
4. **Empty settings clear the entry** (a reset grid leaves no trace). Malformed JSON, wrong version, wrong shape, a throwing `localStorage` getter or `getItem`/`setItem`/`removeItem`: defaults, silent.
5. **Remount gets the latest settings**: the hook keeps what the grid last reported (a ref), not just what it loaded, because GridWidgetBody remounts the grid after a delete.
6. DocumentLibrary's Documents grid also persists (key = `RECORD_DOCUMENTS_GRID`, one entry for every record's Documents list, "per grid configuration").

## Deviations

- `GridWidgetBody.hostCommands.test.tsx` (existing SPA test) had two assertions that pin "no hostCommands in, none out" and identity. They are updated: the host's commands arrive unchanged and first, then `reset-columns`; with none passed, the grid gets only `reset-columns`. This is the intended contract change, not a regression.
- Added `widgets/useGridSettings.ts` (not in the POML outputs).
- Step 6 `dotnet test ... ExternalSpaGridViewSelectorGuardTests`: see Gates.
- POML step 1/escalation (owners' agreement): not obtained, proposal above.
- No UI test in a browser (no live action allowed).

## Behaviour change for other hosts

None on the default path. With none of the three props: Fluent sort uncontrolled, `userColumnWidths` starts `{}`, no `onSettingsChange` calls, `ExternalDataGrid.tsx` untouched, existing DataGrid suites pass unchanged (207 = 192 existing + 15 new).

SPA visible changes (intended): every external grid built by `createGridWidgetBody` (Matters, Projects, Work Assignments, Service Requests, Documents, Invoices widgets) and the record Documents list now show a "Reset columns" command, and keep widths and sort across reloads. Two things for the owner to eyeball in the live UI:
- the Documents grid's toolbar was empty (all defaults off); it now holds this one button (this also touches the open D-32 #3009 "empty band above the Documents grid" look);
- on a widget grid with the config's default commands plus a host command (Invoices: Submit invoice), `inlineLimit` is 3, so Reset columns can land in the overflow menu rather than inline. It is appended last so the host's own command keeps its place.

## Gates (run from this worktree)

| Check | Result |
|---|---|
| Shared lib `npx tsc --noEmit` | exit 0 |
| `npx jest src/components/DataGrid` | 22 suites, 207 tests passed (192 existing incl. columnResize 14, + 15 new) |
| `node scripts/quality/client-lint/client-lint.mjs check --package ui-components` | ok |
| SPA `npx tsc --noEmit -p tsconfig.json` | exit 0 |
| SPA eslint on the 6 changed/new files (+ both tests) | exit 0, no output |
| SPA vitest: GridSettings, GridWidgetBody.hostCommands, DocumentLibrary.*, DetailDocumentsDelete, DocumentDetail.*, RecordTemplate*, TruncatedLists, ProjectPage.*, DetailEventsRoots | 19 files, 570 tests passed. `ProjectPage.perRecordWrite.test.tsx` failed once while another job ran in parallel and passed alone and in the re-run (load flake, project gotcha) |
| `dotnet test tests/Spaarke.ArchTests --filter ExternalSpaGridViewSelectorGuardTests` | passed: 0 failed, 231 passed, 231 total (the filter matched the guard's cases; run after my edits, `ExternalDataGrid.tsx` untouched) |

Fail-before / pass-after (old file copied back, run, mine restored):
- Shared `DataGrid.persistedSettings.test.tsx`: old `DataGrid.tsx` -> 10 failed / 5 passed of 15 (the 5 passes are the must-not-fire controls and cases whose "ignore" outcome the old grid satisfies trivially); new -> 15/15.
- SPA `GridSettings.test.tsx`: old `GridWidgetBody.tsx` + `DocumentLibrary.tsx` (store kept) -> 9 failed / 18 passed of 27 (the 18 are the store unit tests); new -> 27/27.
- `GridWidgetBody.hostCommands.test.tsx`: the old assertions fail against the new body (2/2), the updated ones pass 2/2.

## Step 9.5

code-review (inline, no agents), scope = the 6 source files + tests:
- F1-F4: none found.
- Metrics/cohesion: `DataGrid.tsx` ~1990 lines, cohesive grid component; this change adds one cohesive "user settings" cluster (~100 lines incl. 3 small module-scope helpers). Not a decomposition trigger (§11.5). Other new files are small and single-purpose.
- K4/K2-style notes (known limits, no fix): `onSettingsChange` fires per drag step and the SPA writes `localStorage` each time (tiny sync write; debounce only if profiling says so); settings are shared by all users of one browser (decision 3); two tabs on the same grid overwrite each other's entry (last writer wins); sort persists as a client-side sort over loaded rows only (existing behaviour of this grid's sort, lazily-paged lists sort what is loaded).
- AI smells: no single-implementation interface, no log-rethrow, no needless null checks (the `typeof w === 'number'` narrowing is on external/untrusted data), no code-restating comments left.
- Pattern grep (Step 9.5 rule 6): the "silent storage failure" pattern, `localStorage` in `external-spa/src`, exists elsewhere (`recordSectionState.ts`, `backend-selection.ts`, theme); all are already try/catch'd, none changed.
- Component justification (§11): existing = `userColumnWidths` state + `hostCommands` slot (reused); extension = optional props on the existing grid, store is SPA-side by ADR-012; cost of nothing = widths/sort reset on every reload (#2489). No new package, endpoint, DI or Dataverse column.

adr-check (inline): ADR-012 compliant (props are plain data and callbacks; no storage, Xrm or entity literals in the shared lib, types exported from the index); ADR-021 compliant (no styles added; the only visual is the existing Fluent command-bar button); ADR-022 compliant (no `useId`, `useSyncExternalStore`, `createRoot`); ADR-051 unaffected (paging untouched); ADR-038 (tests use the existing network-seam mocks only; every must-fire has a must-not-fire pair). No violations, no Path A/B/C needed. The `ExternalSpaGridViewSelectorGuardTests` pin: `ExternalDataGrid.tsx` unchanged and the SPA never imports the shared grid's module (types are read off the wrapper's props).

## Needs owner

1. Agreement of the prop API above by the dataset-grid framework owners (`initialSettings`, `onSettingsChange`, `settingsResetKey`, the two types) per POML escalation and spec FR-19. Not obtained: no active session.
2. Reset columns icon: reuse `refresh` (as built) or add `resetColumns` to `RECORD_ACTION_ICONS` (and its pinned `toEqual` test).
3. Per-browser (as the POML says) vs per-user key (decision 3).
4. Live check (not done, no live actions): widths/sort persist across a real reload on dev; Reset columns shows inline (not in the overflow menu) on the Invoices grid; dark and high-contrast look; "Storage blocked" in a real browser.

## Out-of-scope defects found

None.

rework: fix-round-2 task 032

## Review fix round 1

- **F2 prop docs.** `DataGrid.tsx` `onSettingsChange` doc corrected to the code: the sort is controlled whenever ANY of `initialSettings` / `onSettingsChange` / `settingsResetKey` is passed (`settingsEnabled`); "not called on mount" now says it is not called merely because the grid mounted with `initialSettings`, but that pruning a stored width for an unknown column once the columns load IS reported. No behaviour change.
- **Huge stored widths.** Widths above 2000 px are dropped (not clamped), like any other invalid value: `gridSettingsStore.ts` (`isUsableWidth`, so both load and save) and `DataGrid.tsx` `sanitizeInitialWidths` (`MAX_INITIAL_COLUMN_WIDTH_PX`, `initialSettings` doc updated). 2000 itself is kept.
- Tests: `GridSettings.test.tsx` (load drops 1e9 and 2001, keeps 2000, an entry of only huge widths is nothing, save never writes one) and `DataGrid.persistedSettings.test.tsx` (huge widths keep the container fit; 2000 is kept).


## Owner decisions 2026-11-23

1. **DataGrid prop API approved.** The owner APPROVED the shared grid's opt-in prop API (`initialSettings` / `onSettingsChange` / `settingsResetKey`, and the types `DataGridSettings` / `DataGridSortSetting`) on behalf of the dataset-grid framework owners. Needs-owner item 1 and review finding F4 are closed.
2. **"Clear all" replaces "Reset columns".**
   - The toolbar command is now label `Clear all`, id `clear-all` (`CLEAR_ALL_LABEL` / `CLEAR_ALL_COMMAND_ID` in `useGridSettings.ts`). The storage key `sprk.ext.gridSettings.v1.{configId}` is unchanged.
   - It gets its own icon: `RECORD_ACTION_ICONS.reset` (`ArrowReset20Regular`) was added to the map (the pinned test is now `iconMap_HoldsExactlyTheFifteenFr05Entries`). Needs-owner item 2 is closed.
   - It clears everything the user set on the grid: widths, sort AND the column filters. A changed `settingsResetKey` now also resets the shared grid's filter chip state (`DataGrid.tsx`); the key still does nothing on mount or while it stays the same. Filters are not part of `DataGridSettings` (nothing about them is stored or reported).
   - A filtered column header is announced as well as drawn: the column-options trigger's name is `<label> column options, filtered` (`HeaderCellContent.tsx`); the visual `Filter16Filled` glyph is unchanged and stays `aria-hidden`.
   - Tests: `DataGrid.persistedSettings.test.tsx` (reset clears the filter: indicator, name and the FetchXML; the same key keeps it), `HeaderCellContent.filteredAnnouncement.test.tsx` (new), `RecordCommandBar.test.tsx` (map pin), `GridSettings.test.tsx` (command icon is `reset`, not `refresh`) and `GridWidgetBody.hostCommands.test.tsx` (renamed command).

rework: fix-round-2 task 032
