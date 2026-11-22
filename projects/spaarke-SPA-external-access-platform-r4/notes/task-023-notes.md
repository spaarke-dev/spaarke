# Task 023 — notes

## Step 1 — feasibility (2026-11-21): escalation trigger fired

Owner go 2026-11-21 "Yes start 023". Rigor FULL; sonnet-tier task run in the Opus main session.

Question: can `DocumentLibrary`'s rows reach the shared grid (`<ExternalDataGrid configId="3af4102c…">` + `hostFilters`
on the parent lookup) for every root it serves, with no new `sprk_gridconfiguration`, no module scope change, no BFF
change and no shared-DataGrid change (POML constraints)?

| Root | Answer | Why |
|---|---|---|
| projects, matters, workassignments | Rows: yes. The list as it is today: **no** | The `documents` module scopes by `sprk_project` / `sprk_matter` / `sprk_workassignment` (`ExternalAccessModule.cs:534-539`) and adds `hasFile` (`ExternalModuleDataEndpoints.cs:495-505`), so a host filter on the parent lookup gives the rows. But see the three blockers below. |
| servicerequests | **No** | The module has no service-request scope dimension; a service request's documents are reachable only through its own record route. |
| invoices (until 021) | n/a | `DocumentLibrary` has no invoice root (invoice files are task 021's list). |

Blockers for the P/M/W roots too:
1. **Per-row actions (Download, Version history, Delete).** `overrides.columnRenderers` replaces the cell of a column that
   is *in the grid configuration* (`DataGrid.tsx:1236`); there is no host-added column, and `onRecordAction` is accepted
   but not wired (`DataGrid.tsx:740`, `_onRecordAction`). An actions cell needs a configuration column for it (new or
   edited `sprk_gridconfiguration`, and 3af4102c also backs the workspace Documents widget) or a shared-grid change.
2. **AI Summary column.** `sprk_summary` is not on the documents module's `ReadableColumns` (`ExternalAccessModule.cs:548-555`),
   so the grid cannot show it without a BFF allow-list change (UAC review, FR-18).
3. **Row-set parity.** The list today reads the record route (`/{root}/{id}/documents`), which checks the record itself;
   the module fetch is the caller's accessible-root set narrowed by a client-side host filter. Probably the same rows,
   but it is a different authorization path and would need uac-r2's view before it replaces the record route.

What already holds without the grid move:
- FR-15 on the Document record: Download hidden (`record/actions/DocumentDownloadAction.tsx`) and Preview hidden
  (`record/sections/PreviewSection.tsx:35`) when `hasFile === false`. In the list: `DocumentLibrary.tsx:1082`.
- D-12 (task 009) already sized the Fluent grid's columns so they do not overlap; what #2490 still lacks is resize and a
  tooltip on every truncated cell.

Escalated to the owner (🔔 Human Input Required) with options A–D; recommendation D. Waiting.

## D-23 (#2928)

Independent of the decision: hoist one `readFromParents(parents, read)` helper used by `downloadDocumentFromParents`
(`DocumentLibrary.tsx`) and `readDocumentFromParents` (`record/sections/DocumentPreview.tsx`). Not started (stop point).

## 023a

Shared DataGrid host options (design `task-023-design.md` §3.1). Rigor FULL (shared `.tsx`, 34 consumers). Executed by a
sub-agent (Opus, high effort) under task-execute; POML status / TASK-INDEX left to the main session.

**Surface (all optional; types exported from `components/DataGrid/index.ts`, reached by the package barrel through
`components/index.ts` `export * from './DataGrid'`; DataGrid is not in `pcf-safe`, hooks are React-16-safe anyway):**
- `rowSource?: DataGridRowSource` = `(req: { page; pageSize; signal: AbortSignal }) => Promise<{ rows; hasMore; truncated? }>`
  and `rowSourceKey?: string` — `useHostRowSource.ts` (new).
- `rowActions?: ReadonlyArray<DataGridRowAction>` = `{ id; label; icon: ReactElement; isVisible?(record) }` — wires
  `onRecordAction` (was `_onRecordAction`) — `RowActionsCell.tsx` (new).
- `truncateWithTooltip?: boolean` prop + `DisplayConfig.truncateWithTooltip?: boolean` — `TruncatedCellText.tsx` (new).

**Decisions:**
- Row source: separate hook rather than branching `useLazyLoad` (smaller diff; `useLazyLoad` untouched). Both hooks are
  always called; with `rowSource` the fetchXml memo returns `''` (so `useLazyLoad` is a no-op), chip descriptors are `[]`
  and `membershipActive` is false (resolver never called). Without `rowSource` every one of those is the old value.
- The latest `rowSource` is read per request (ref), so an inline host callback does not reload; only `rowSourceKey`,
  the effective page size or enable (config resolved) reloads page 1. Refresh (command bar) also reloads page 1.
- Abort: one `AbortController` per request; aborted on unmount, key change and reset; late/superseded answers ignored.
- A rejected page sets the error and `hasMore = false`, so the sentinel does not re-request the failing page in a loop
  (Refresh or a key change retries). Rows already shown stay. Error banner = the grid's existing `errorBanner` style,
  `role="alert"`, rendered above the rows inside the inner card (headers stay).
- Truncated notice: Fluent `MessageBar` warning, wording mirrors the SPA's `TruncatedListNotice` ("This list is
  incomplete: only N rows could be shown …"; 0 rows → "could not be loaded just now …"). The empty-state message is
  suppressed while the error banner or the notice shows.
- Row actions: inline limit 3; past it 2 inline + "More actions" menu. Column width = slots × 32 + gaps + 24 padding
  (92 px for 2 actions, 128 px for 3+), forced with an inline style on header and body cells; header cell gets
  `aside={null}` (no resize handle) and the column has no `compare` (Fluent treats it as not sortable). The width is
  taken off the shared space in `columnSizingOptions` only when the column exists. Clicks stop propagation so they do
  not toggle row selection (also for menu items, which bubble through the React portal). `recordId` =
  primary id (fallback `_rowId`), `record` = the grid item (same object `onRecordOpen` gets).
- Truncate: tooltip only when `scrollWidth > clientWidth` on the cell span or its link child at show time;
  `relationship="inaccessible"` because the full text is already in the DOM. Applies to the primary-name link and every
  `renderCellValue` cell; host `columnRenderers` cells are left alone. Prop wins over config when set.

**Tests (KEEP, ADR-038 pairs):** `DataGrid.rowSource.test.tsx` (10), `DataGrid.rowActions.test.tsx` (7),
`DataGrid.truncateTooltip.test.tsx` (7). Against the pre-change `DataGrid.tsx`: 17 fail, 7 pass — the 7 are exactly
the must-not-fire controls. With the change: 24/24.

**For the reviewer / follow-ups (not fixed, out of scope):**
- Pre-existing: the FetchXML path never shows a page error — `useLazyLoad.error` is not read by `DataGrid.tsx`, so a
  failed query renders as an empty grid. Left as is (would change the 34 consumers); the host path shows its banner.
- Pre-existing overlap: `DataGridConfiguration.secondaryActions` (config-level per-row actions, string icon names, no
  per-row predicate) is resolved but never rendered. `rowActions` is the host-side seam the design chose; the two
  should be reconciled if anyone renders `secondaryActions`.
- Docs: "Host options" subsection in `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` §6; one line for
  `display.truncateWithTooltip` in `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md`'s full-schema block.

### 023a review fixes

Four review findings on the uncommitted 023a change, each with a must-fire test (verified failing against the pre-fix source) and its must-not-fire pair. Still uncommitted.
- **Space / Enter on a folded menu item toggled the row.** Fluent's `DataGridRow.onKeyDown` toggles selection on Space for a non-interactive target (a menu item is a `div`), and the popover's keydown reaches the row through the React portal. `RowActionsCell`'s wrapper now also stops propagation of Space / Enter keydown (other keys still bubble). Tests: `keyboard activation does not toggle the row selection` (Space/Enter on a menu item: fires once, row `aria-selected` false; inline button; control: Space on the row itself still selects). Note Fluent activates a `div` menu item on Enter keydown and Space keyup, so the Space test sends both; the Enter case passes before the fix too (the row never toggled on Enter), it is the pair.
- **Truncate tooltip never showed on keyboard focus.** With `focusMode="composite"` focus lands on the gridcell, not the inner span. `TruncatedCellText` now listens for `focusin` / `focusout` on its enclosing `[role="gridcell"]` (`useEffect`, React-16-safe) and shows the tooltip while the cell has focus, only if the content overflows, same test as hover. The primary-name link still opens the record. `DisplayConfig.truncateWithTooltip` JSDoc and the architecture doc now say "hover, or when the cell receives keyboard focus".
- **Truncated-list notice wording.** New optional prop `rowNoun?: { one; other }` (default row / rows), exported as `DataGridRowNoun`. "only 1 row could be shown. Some rows are not listed here."; zero rows says "The {other} could not be loaded just now...". Same sentence shape as the SPA's `TruncatedListNotice` but with the singular fixed (the SPA component still prints "only 1 documents"; not edited, listed for the main session). `DataGrid.rowSource.test.tsx` updated (pinned text), plus tests for 1 vs N, zero rows, and a custom noun.
- **K items.** `useHostRowSource` ends paging when a page returns zero rows even if `hasMore` is true (test: page 2 empty + `hasMore: true`, no page 3; control: non-empty pages keep paging). `DataGridRowSource` JSDoc: the host must reject with a user-fit message (the banner shows `Error.message` as-is) and every row must carry the configuration's primary id attribute or `onRecordAction` / `onRecordOpen` get a synthetic `__row_N` id.

Results: `npx jest src/components/DataGrid` 16 suites / 160 tests pass; `npx tsc --noEmit` clean; `npm run build` clean. Against the pre-fix source the new tests fail 6 of 36 in the three affected suites (Space on menu item, empty page paging, rowNoun, the pinned notice text, two focus tooltip cases).

## 023b

Task 023b (design §3.3): the "External record documents" grid configuration is defined in the repo, an apply script creates it, and the BFF admits its id. Status here is code and tests only. Nothing was run against any environment (no Dataverse or Azure call, read or write). POML status, TASK-INDEX and `current-task.md` are not touched (main session).

### What changed

| File | Change |
|---|---|
| `config/external-record-documents.gridconfiguration.json` (new) | The definition. A wrapper (`id`, `name`, `entity`, `description`) around `configJson`, which is exactly what the shared DataGrid reads from `sprk_configjson` (v1.0). Only `configJson` is written to the row. |
| `scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1` (new) | Modelled on `Apply-WorklistGridConfiguration.ps1`: lint, `-WhatIf`, `-Verify` (read-only), `-RequireRow`, idempotent create/update by fixed id, read-back compare. Adds `-LintOnly` (offline: no environment, token or network). Not run against any environment. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs` | One `Guid.Parse` line plus a comment in `OutsideCounselGridConfigurationIds` (cites R4 task 023b). No scope, ReadableColumns, route or DI change. |
| `tests/integration/auth/UnifiedAccessControl/ExternalRecordDocumentsGridConfigurationTests.cs` (new) | 13 test cases (below). |

### The definition (what the grid reads)

- Row: id `286be8b4-f96f-4d5a-b1d5-82fd83ca9693`, name "External record documents", entity `sprk_document`.
- `source`: inline. FetchXML selects `sprk_documentid`, `sprk_documentname`, `sprk_documenttype`, `createdon`, plus the three scope attributes `sprk_project`, `sprk_matter`, `sprk_workassignment` (needed by the BFF's Tier-2 scoping if any other host runs it through the module fetch; hidden in `columns`, not in the layout), ordered by `createdon` desc. Same shape as 3af4102c's live source (r3 task-002 notes, "Live capture 2026-10-20" and the later width changes).
- `layoutXml` cells, in order: Name `sprk_documentname` 300, Type `sprk_documenttype` 150 (badge), Date Added `createdon` 140 (date), AI Summary `sprk_summary` 360.
- `display`: title Documents, empty-state text, `truncateWithTooltip: true` (the `DisplayConfig` flag 023a adds; the key name matches `DataGrid.tsx` in this worktree).
- `rowOpen`: `formDialog`, as every existing external grid has it (the SPA hands the grid `onRecordOpen`).
- `commandBar`: every default command off. Export to Excel is off on purpose: it would re-run the FetchXML through the module path (a different authorization path) and drop AI Summary, so a host-fed grid must not offer it. Refresh is off because the section refreshes through `rowSourceKey` (023a). Both are one flag each if 023c wants them.
- `behavior`: selection none, pageSize 50, sorting and resize on.
- Compact `configJson` is 1,497 of 20,000 characters (`sprk_configjson` limit).

### `sprk_summary` (decision, no ReadableColumns change)

`sprk_summary` is not on the `documents` module's `ReadableColumns`. Selecting it in a FetchXML fails the whole read with 400 `DV_FETCHXML_COLUMN_NOT_PERMITTED`. So it is a layout cell and a `columns` entry only, never a FetchXML attribute. Its values come from the host's rows (the record route's DTO already carries `sprk_summary`, `ExternalProjectDtos.cs:173`). Consequence, stated plainly: a host that ignores `rowSource` and runs this FetchXML itself gets an empty AI Summary column. That host would need the column added to the documents module (UAC review, FR-18); that is not done and not needed for the SPA. `ReadableColumns` is not widened.

### Offline checks run (no environment)

| Check | Result |
|---|---|
| `pwsh scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1 -LintOnly` | `Definition OK`, exit 0 |
| Lint must-fire (six seeded defects: sprk_summary in the FetchXML, a pointer column in the FetchXML, tooltip off, wrong id, broken FetchXML, a missing layout cell) | each throws `LINT FAIL` with its own message |
| `-WhatIf` / `-Verify` / apply paths against a stubbed `az` and `Invoke-RestMethod` (offline harness, scratch only) | absent + `-WhatIf`: GET only; absent + apply: GET, POST, GET (READ-BACK OK); identical + apply: UNCHANGED, GET only; drifted + `-WhatIf`: GET only; drifted + apply: GET, PATCH, GET; `-Verify` reports ABSENT / IN SYNC / DRIFT with GET only |

### Tests (`ExternalRecordDocumentsGridConfigurationTests`, 13 cases)

Under `tests/integration/auth/**` (KEEP, ADR-038 path 1). They drive the REAL production registry and the REAL `GetScopedRecordCoreAsync`, with a counting double for the one app-only read.

| Test | Pair |
|---|---|
| `GetScopedRecord_ForTheExternalRecordDocumentsGridConfiguration_ReadsItsConfigJson` | must-fire: new id is admitted, one read |
| `GetScopedRecord_ForTheSixExistingGridConfigurations_StillReadsThem` (6 rows) | must-fire: no regression on the six existing ids |
| `GetScopedRecord_ForAnUnlistedGridConfigurationId_RefusesWithoutReading` | must-not-fire: 403 `sdap.external.module.record_not_in_accessible_set`, zero reads |
| `GetScopedRecord_ForAnIdOneDigitOffTheNewOne_RefusesWithoutReading` | must-not-fire: the exact id is pinned |
| `ProductionModule_GridConfiguration_AdmitsExactlyTheSevenOutsideCounselIds` | the set is exactly the six plus the new one |
| `Definition_NamesTheAdmittedIdTheDocumentEntityAndTheOwnersColumns` | file id = BFF id; entity; v1.0; inline; tooltip on; the four cells and labels |
| `Definition_FetchXml_IsAdmittedInFullByTheDocumentsModule` | must-not-fire: the real documents guard admits it |
| `Definition_FetchXml_WithSprkSummaryAdded_IsRefusedAsColumnNotPermitted` | must-fire: why `sprk_summary` stays layout-only |

Fails before, passes after: with the old `ExternalAccessModule.cs` restored from HEAD, 2 of 13 fail (`...ReadsItsConfigJson`, `...AdmitsExactlyTheSevenOutsideCounselIds`); with the change, 13 of 13 pass. The other cases are must-not-fire or definition checks that pass either way, by design. Adjacent suites (`ExternalModuleColumnAllowListTests`, `ExternalModuleSavedQueryScopeTests`, `WorklistGridConfigurationTests`) were run together with the new class: 219 passed, 0 failed.

### Conflict check (2026-11-22)

`git fetch origin master`, `gh pr list --state open` (35 PRs, files included): no open PR touches `ExternalAccessModule.cs`. The only open PR on the external-access surface is #1494 (`GrantExpiryReminderJob.cs`, an unrelated file). `git log HEAD..origin/master` for `ExternalAccessModule.cs` and `ExternalModuleColumnAllowListTests.cs`: empty (master has not moved on either).

### Publish size

Not measured with the full two-worktree procedure. Reason: the whole BFF change is one `Guid.Parse("...")` string literal in a static set; no package, project reference, DI registration, endpoint or background work changed, so the delta is tens of bytes of IL (rounds to 0 MB). Procedure to run if a number is wanted: `.claude/rules/bff-hygiene.md` item 4 (fresh short-path worktrees of master and this branch, `Compress-Archive`, equal file counts). `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. No NuGet change, so the CVE check is unchanged.

### PR text (ready to paste)

**Placement Justification (bff-extensions.md).** In BFF, in place: one id is added to the existing static allow-set of the existing `grid-configuration` external module. Nothing new is created.

**§11 (CLAUDE.md).**
1. Existing: `OutsideCounselGridConfigurationIds` and the `grid-configuration` module (`ExternalAccessModule.cs`), which already gate every external grid-configuration read.
2. Extension: yes, it is an extension: one more id in that set. A separate module or a new allow mechanism would duplicate it.
3. Cost of doing nothing: the SPA's DataGrid loads its configuration with `retrieveRecord('sprk_gridconfiguration', id, ['sprk_configjson'])` through this module; an id outside the set answers 403 `sdap.external.module.record_not_in_accessible_set`, so the record template's Documents grid (task 023c) could not load its columns.

**What the reviewer (uac-r2) is asked to check.** No scope-dimension, `ReadableColumns` or route change (FR-18: no new readable column). The new row holds column and layout metadata only: no tenant data, no Graph pointer (same reasoning as the six existing ids). The grid's rows come from the existing record route (`ListDocumentsForParent`), which keeps its own checks; this PR does not move any list onto the module fetch. `sprk_summary` is deliberately not added to the `documents` `ReadableColumns`.

**Live steps (task 028, owner approval and rollback per project CLAUDE.md §5).**
1. BFF with this id admitted, deployed through uac-r2 (owner go per SHA).
2. `pwsh scripts/Apply-ExternalRecordDocumentsGridConfiguration.ps1 -DataverseUrl <dev> -WhatIf`, then without `-WhatIf`, then `-Verify -RequireRow`.
3. The SPA that mounts the grid (023c).

Rollback: DELETE the `sprk_gridconfiguration` row by id `286be8b4-f96f-4d5a-b1d5-82fd83ca9693` (nothing references it until the SPA with the grid is deployed; roll the SPA back first if it is). Admission with no row is harmless: the config load misses and the grid falls back to defaults.

### Uncertain / for the main session

- `configJson` wrapper: the repo file wraps `configJson` with id/name/entity so one file defines the whole row; the worklist precedent hard-codes name and entity in the script. If the bare `sprk_configjson` file is preferred, it is a small change to the script and the test's path expressions.
- `rowOpen: formDialog`, `refresh` off and `exportExcel` off are my reading of "host-fed grid"; 023c should confirm against the final 023a seam (whether the grid's own Refresh re-calls `rowSource`).
- `sprk_summary` is not in the repo's `sprk_Document/Entity.xml` snapshot (the BFF DTO reads it, so it exists live). The script and tests do not check it against a schema; `-Verify` does not run the FetchXML (it would read document rows).
- The apply script was never run against an environment; its apply paths were exercised only against a stubbed `az` and `Invoke-RestMethod`.
- Not done by design: POML status, TASK-INDEX, `current-task.md`, commit, push.


**Verification (2026-11-22):**
- Shared lib `npx jest` (full): before 425 suites / 6859 tests, 10 failed; after 428 suites / 6883 tests (+24), 11
  failed. Same two failing suites both times, neither touches DataGrid: `src/__tests__/documentOperations.deleteConflict.test.ts`
  (9 failures, pre-existing, also fail alone — `sprk_DocumentOperations.js` 409 message tests) and
  `OntologyAdmin/Rules/__tests__/Rules.test.tsx` (load timeouts; passes alone). `npm run build` (tsc) clean; eslint on
  changed files: 0 errors (3 pre-existing "unused eslint-disable" warnings in DataGrid.tsx).
- External SPA `vitest run`: 68 files / 1910 tests; 26 failures in 14 auth/bootstrap files under machine load (#2275
  pattern, collect 3104 s); those 14 files re-run alone: 492/492 pass. `npm run build` and `tsc --noEmit` clean.
  No SPA file changed; `ExternalDataGrid.tsx` untouched.

### 023b publish-size (review finding F4, measured 2026-11-22)

Fresh detached worktrees at short paths: `C:\wt23m` = origin/master `bc4565a1b5`; `C:\wt23b` = branch HEAD `af77ef1f95` plus the uncommitted 023b BFF diff.
- **Measurement:**
  - command `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`;
  - framework-dependent, default RID;
  - Release;
  - `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`;
  - PDBs included.
- **Result:** master 40.603 MB, 191 files; branch 40.603 MB, 191 files. Delta 0.000 MB, file counts match, well under the 60 MB ceiling.
- **Packages:** none added, so the CVE check is unchanged.
