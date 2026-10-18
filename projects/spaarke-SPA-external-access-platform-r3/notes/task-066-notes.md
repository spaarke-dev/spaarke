# Task 066 notes - document detail Download, visible download failures

Owner check 3 items 2-3. Owner decision 2026-10-13: download/open is enough for R3; inline preview moves to R4 (DEF-003).

## What changed (all under `src/client/external-spa/`)

| File | Change |
|---|---|
| `src/components/DocumentLibrary.tsx` | Extracted the download into `downloadDocumentFile(root, recordId, documentId, fileName)` (throws on failure) and added `downloadDocumentFromParents` and `downloadFailureMessage`. `handleDownload` now calls the helper and shows a failure in a `MessageBar` (`data-testid="document-download-error"`) instead of only `console.error`. |
| `src/components/detail/RecordDetailFields.tsx` | `documentParentCandidates(record)`: the parents a document row is filed to, in the BFF's order. |
| `src/components/detail/RecordDetailModal.tsx` | `DocumentDownload` section on a `sprk_document` detail: a Download button plus an error `MessageBar` (`record-detail-download-error`). |
| `tests/DocumentDownload.test.tsx` | New: 11 tests. |

No new BFF route, no SPE URL or signed link. Grid selection is untouched (`selectionMode` stays `none`); Download is an action in the document detail, not a selection command.

## Parent-selection order (mirrors the BFF)

Order: **project, then matter, then work assignment** (typed lookups `sprk_project`, `sprk_matter`, `sprk_workassignment`).

Source lines mirrored:
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs:482-490` - `DocumentRootLinks.Links`, the one place the six document root links are enumerated: Project, RelatedProject, Matter, RelatedMatter, WorkAssignment, RelatedWorkAssignment.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs:401-406` - the documents module's `ScopeDimensions`, same order (project, matter, work assignment).

Only the TYPED lookups are candidates. The content route (`ExternalProjectDataEndpoints.cs:585-630`, `DownloadDocumentForParent`) scopes the document to the route's parent through the route's own lookup (`GetDocumentParentAndNameAsync` reads `binding.LookupValueAttribute`; `DocumentScopeRead.IsUnder`, ExternalDataService.cs:533), so a `sprk_related*` twin cannot satisfy it. Read on the parent is `RightsForDocumentParentAsync` (:560-563 / :604-611).

## "The parent the caller can read" - how the SPA picks it

The BFF has no "pick a parent" API and the SPA cannot tell which linked parent the caller may read: `/me` lists projects only, and a document can be filed to several roots (a grid row is visible if ANY root is readable). So `downloadDocumentFromParents` asks the candidates in the BFF's order and moves on to the next ONLY on a 403; any other failure (5xx, 404, network) stops at once, and the last 403 is shown if every parent refuses. The server stays the authority (Read on the parent plus document-to-parent scoping, before any SPE read).

Cost of this choice: a document filed to an unreadable project and a readable matter makes one extra 403 round trip (and one server-side `[EXT-DOWNLOAD] denied` warning) before the matter succeeds. Accepted: it is a user-initiated single download, and the alternative (guessing from `/me`) would hide Download wrongly for matter and work-assignment grants.

## Decisions

- Failure text is by status (403 / 404 / 5xx / generic), never the server's `detail`. The delete messages follow the same rule, and a download refusal must not echo text that names a record or a rule.
- The lookup id is read from an entity reference (`{ id }` / `{ Id }`, as the module fetch serialises a lookup), a bare id string, or the Web API `_x_value` form, as `requestedByIdOf` (`delete-own.ts`) already does. A malformed or empty-GUID id is skipped.
- A document with no usable parent link renders no Download section at all (acceptance criterion 2).
- DocumentLibrary itself is unchanged for a project's tab and the five other record types; only its download body moved into the helper.

## Tests (`tests/DocumentDownload.test.tsx`)

Scope: the stated contract plus the named edge cases (no readable parent, failed download in the detail and in DocumentLibrary). Two extra cases, justified: the 403 fall-through and the "stop on non-403" rule exist because the parent choice is a client-side guess (see above); the retry-clears-message case guards a stale error staying on screen.

Failed before the change: 10 of 11 (the "no parent link shows no Download" case passes before and after by design - it is the negative control).

## Gotcha found while doing it (fixed)

`DocumentLibrary` returns different trees for the list and the empty state, and React matches siblings by index. The first version put the download error slot only in the list render, which shifted `DeleteOwnConfirm` one index away from its empty-state position. Deleting the last document then remounted the confirmation and left the page `aria-hidden` (existing test `DetailDocumentsDelete` "deleting the last document leaves the empty state" failed). The same slot now exists in both renders (it also keeps an error visible if the list empties). Keep sibling positions aligned if either render is edited again.

## Results

typecheck 0, lint 0, vitest 36 files / 1004 tests pass (11 new), `npm run build` 0, `check:dist` clean (1 file).

## Known limits / not done

- Inline preview: R4 (DEF-003).
- The live grid config `3af4102c` stays `selectionMode: none` (project constraint).
- UI tests listed in the POML (`ui-tests`: browser download, dark mode) were not run: no deployed environment in this run (no live commands, no deploy). Colours come from Fluent v9 tokens only (the new section reuses `useStyles` section/heading tokens; the button is a stock Fluent `Button`).

## Review round (adversarial pass, 2026-10-13; fixes by the main session)

No F-class finding. The data path was traced end to end: the detail window reads `sprk_project`/`sprk_matter`/`sprk_workassignment` as allowed module columns, and every row the grid shows has at least one readable parent.

| Finding | Class | Action |
|---|---|---|
| Fixed parent order costs one extra 403 (and a console network line) when the readable parent is not first | K1 | Accepted (server stays the authority) |
| No test that the blob URL is revoked after the extraction | K2 | Fixed: the first detail test asserts `revokeObjectURL('blob:test')` |
| `console.error` logged the ApiError, whose message is the server's error body | K3 | Fixed: both download logs print only the HTTP status (`downloadFailureLogText`) |
| One shared download error, not naming the document | K4 | Accepted |
| Advisory: `lookupId` duplicates `requestedByIdOf` (delete-own.ts) | §11 | Not merged: `lookupId` feeds a URL path and needs a strict GUID; `requestedByIdOf` lowercases for an equality check. A merged reader would change one of them |

After the fixes: typecheck 0, lint 0, `DocumentDownload` + `DetailDocumentsDelete` 105/105, build 0, check:dist clean (the full suite was 1004/1004 before the fixes, which touch only two log lines and one assertion).
