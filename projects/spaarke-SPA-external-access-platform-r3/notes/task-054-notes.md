# Task 054 - Documents on the detail view and delete-own (FR-24, FR-26 client)

**Status (2026-10-11):** implemented (rigor FULL, sonnet, effort high); SPA plus two small server additions (`/me` `systemUserId`, document list `canCreate`); review fix round applied (see the last section). Left UNCOMMITTED for the main session to commit by path. The POML `<ui-tests>` (live browser) were not run: no live session from this agent.

## Scope adjustments decided by the main session (deviations from the POML)

1. **Service-request delete is NOT built.** The merged server maps DELETE only for `/documents/{id}`, `/todos/{id}` and `/events/{id}` (task 032 escalated the service-request delete). A service request shows no Delete action. The seam is a comment in `web-api-client.ts` (above the delete calls: "no `deleteOwnServiceRequest`...") and one in `RecordDetailModal.tsx` where the sections render. Documents ON a service request are deletable like any other document (they are `sprk_document` rows).
2. **Invoice documents are not added** (owner decision 031 pending). `RecordDetailFields` gives the invoice entity no `documentsRoot`, so the invoice detail has no documents section, as before. The POML's AC 1 lists Invoice; it stays unmet until the owner decides.
3. **Messages have no delete** (as the POML says). No code path adds one; a test asserts no delete/remove control in a thread, including on the caller's own message.

## Server contract used (read from merged code, not docs)

- **Routes** (`ExternalProjectDataEndpoints.cs`): `GET|POST /api/v1/external/{projects|matters|workassignments|servicerequests}/{id}/documents`, `GET .../documents/{documentId}/content`, `GET .../versions`; `DELETE /api/v1/external/documents/{id}`, `/todos/{id}`, `/events/{id}`.
- **"Created by me" signal:** there is no `isMine` flag on documents, to-dos or events. The list DTOs (`ExternalDocumentDto`, `ExternalTodoDto`, `ExternalEventDto`) carry two stamps, `_sprk_createdbyperson_value` (systemuser) and `_sprk_createdbycontact_value` (contact). The client compares them with the signed-in principal. The rule mirrors `ExternalDataService.IsCreatedByCaller`:
  - a contact principal matches `createdbycontact` = its contact id, and never a person stamp;
  - a systemuser principal matches `createdbyperson` = its systemuser id, or `createdbycontact` = its linked contact;
  - a row with neither stamp is nobody's; an empty id (including the empty GUID) never matches; ids compare case-insensitively; names are never compared.
- **Where the principal comes from:** `GET /api/v1/external/me` (`getExternalUserContext`, `bffApiCall`) returns `contactId` and the projects with levels.
- **Delete answers:** 204; 403 uniform (`reasonCode` `sdap.access.deny.delete_own`); 409 `document_locked` or the pointer-check code (`document_storage_unverified`); 503 (nothing deleted, retry); 500 (`document_row_not_deleted`, `document_delete_failed`). Every problem body uses `reasonCode`.

## What changed (all under `src/client/external-spa/`)

| File | Change |
|---|---|
| `src/api/delete-own.ts` (new) | `CallerIdentity`, `callerIdentityOf`, `isCreatedByCaller` (the rule above), `deleteFailureOf` (status/reasonCode to user message; 401 to the sign-in state through `accessFailureOf`). |
| `src/api/web-api-client.ts` | `DocumentRoot` + `documentsPath` / `documentContentPath` / `documentVersionsPath`, `getRecordDocuments`, `deleteOwnDocument` / `deleteOwnTodo` / `deleteOwnEvent` (all `bffApiCall` on `/api/v1/external/**`); creator stamps on `ODataDocument` / `ODataTodo` / `ODataEvent`. `getDocuments` is unchanged in behaviour. |
| `src/components/DeleteOwn.tsx` (new) | `useDeleteOwn` + `DeleteOwnConfirm` + `DeleteFailureNotice`: the one confirm-then-delete flow. Confirmation is the shared `ConfirmModal` (`@spaarke/ui-components`, ADR-050): no light-dismiss, destructive Confirm, busy state. A same-tick lock sends one DELETE for a double click. |
| `src/components/DocumentLibrary.tsx` | New optional props `root`, `recordId`, `canUpload`, `canDownload`, `caller` (and `accessLevel` is optional). `projectId` still works as the project shorthand, so `ProjectPage` and its tests are unchanged. All paths come from the route helpers. Delete on own rows. Noun in messages follows the record ("project", "matter", "work assignment", "service request"); the project wording is byte-identical. |
| `src/components/SmartTodo.tsx`, `EventsCalendar.tsx` | New optional `caller` prop: Delete on own to-dos / events, independent of the access level (the detail view passes `ViewOnly`; a ViewOnly creator can still delete, because the server rule is Read on the parent). |
| `src/components/detail/RecordDetailFields.tsx` | `documentsBy` (module lookup) replaced by `documentsRoot` (route root): Project, Matter, Work Assignment, **Service Request** (new). |
| `src/components/detail/RecordDetailModal.tsx` | The documents section is now `DocumentLibrary` over the task 031 routes (module rollup removed for documents; invoices keep it). Reads `/me` once the record shows (`useCallerContext`) and passes `caller` to documents, events and tasks. Project Upload follows the caller's level from `/me`. |
| `src/hooks/useCallerContext.ts` (new) | Deferred, silent `/me` read; failure leaves identity unknown (no Delete, no project Upload). |
| `src/hooks/useAccessLevel.ts` | `mapAccessLevelString` exported (was private). |
| `src/auth/bff-client.ts` | `ExternalUserContextResponse.systemUserId?` (optional; null for CIAM and contact-only callers, never empty; see "Server addition"). |
| `tests/DetailDocumentsDelete.test.tsx` (new) | 58 tests (43 at first draft, 15 added in the review fix round). |
| `tests/RecordDetailModal.test.tsx` | The network mock answers the new reads (`/me`, document routes); 7 section tests updated for documents served by route; the service request case is split out (it now has a documents section). `getExternalUserContext` is routed to the mocked seam. No assertion was weakened. |

ADR-021: no colours of my own (grep-checked: no hex/rgb in the new files); the danger styling is `ConfirmModal`'s token class. ADR-012: `DocumentLibrary` and the shared `ConfirmModal` are reused; nothing under `src/client/shared` changed, so no jest run was needed.

## Behaviour

- **Upload:** shown when the caller can Create; hidden otherwise (a project follows the caller's level from `/me`; a matter, work assignment or service request follows the BFF's `canCreate` on the document list). A refusal shows in the upload dialog (the server's `detail` when present, else the 403 fallback "Your access level does not permit uploading documents to this {record}."). The Location header of the 201 is never followed (a refetch happens instead).
- **Download / version history:** shown to every caller who can open the detail (the server needs Read, which opening the detail already required). On the project page tab the old Collaborate+ gating is unchanged (see observation 2).
- **Delete:** the Delete icon appears only on own rows. Click, confirm dialog, one DELETE, 204 removes the row. Cancel or the x sends nothing. Any other answer closes the dialog, **keeps the row**, and shows an error bar (dismissible); 401 shows the shared sign-in state instead. Messages (`deleteFailureOf`):
  - 403: "You cannot delete this {noun}: only the person who created it can, and only while they can still see the record it belongs to. Nothing was deleted."
  - 409 `document_locked`: checked out, ask for check-in; other 409: stored file could not be verified, nothing deleted.
  - 503: nothing was deleted, try again in a moment.
  - 500: may be only partly removed, refresh and contact the record team.
  - anything else: could not be deleted, check your connection.
  - A server `detail` is never echoed (a 500 may name internals).

## Findings (not fixed here; for the owner or the main session)

1. **Resolved by the "Server addition" below.** `/me` now returns the caller's own `systemUserId`, so a workforce caller's person-stamped rows are recognised. (The first draft of this note said the field was missing; it was added server-side and this item no longer applies.)
2. **Resolved by the "Review fix round" below.** Upload on a matter or work assignment now follows the document list's `canCreate`, so a ViewOnly reader is not offered it. (The first draft showed Upload to every caller there.)
3. **Tab inconsistency, existing:** the project page Documents tab still hides Download for ViewOnly (Collaborate+ only, in `DocumentLibrary`'s default), while the server needs only Read and the detail view now shows it to every reader. The default was left alone (out of scope); the project page does not pass `caller`, so it has no Delete either. Extending Delete to the project page is passing `caller` there.
4. **Matter and work-assignment detail still show no to-dos or events** (task 039 adds those reads). Delete on them follows when that section is built; `SmartTodo` and `EventsCalendar` already take `caller`.
5. **`DocumentLibrary` on the detail view still shows the grid only for documents filed through the primary lookups** (task 031 known limit: documents filed only through a "related" lookup are not listed).
6. Dev mock mode (`mock-service.ts`) has no handler for the document routes or DELETE; the detail view is unreachable in mock mode anyway (task 051 deviation 5).

## Tests and results (in `src/client/external-spa`)

- `npm run typecheck`: exit 0. `npm run lint`: exit 0, 0 errors, 0 warnings. `npm run build`: exit 0 (`dist/assets/app.js` 1,391,017 bytes). `npm run check:dist`: exit 0 ("1 file(s) clean").
- First-draft run, superseded by the review fix round's counts below: `npx vitest run` (full): 26 files, 635 tests passed, 0 failed (before my changes to the existing detail tests, the same suite was 25 files, 592 tests with 7 failing in `RecordDetailModal.test.tsx` because documents moved to the routes; those were updated, not deleted).
- New: `tests/DetailDocumentsDelete.test.tsx`, 43 tests at first draft (58 now), behavioural (ADR-038): real components and shared modal, only the network seam, MSAL `useMsal` and the grid substituted.

| Acceptance criterion | Evidence (test names in `DetailDocumentsDelete.test.tsx`) |
|---|---|
| 1 upload + download per record type | `DocumentLibrary on each record type > ...` x4 roots (list route, download route, multipart upload route); the detail view tests for project, matter and service request |
| 2 no Create = no upload control; forced upload shows 403 | `a caller with no Create right sees no upload control` (empty and non-empty), `Project, ViewOnly: no Upload`, `a forced upload with no Create right shows the server 403 as an error` |
| 3 Delete only on own rows, asks first | `shows Delete only on the rows the caller created`, `a systemuser caller...`, `which rows are "mine"` (rule, 3 tests), to-do and event "only theirs" tests, `asks first; Cancel changes nothing and sends nothing`, `Project, Collaborate: ... Delete only on the caller's own document, task and event` |
| 4 row disappears on 204; 403 shows an error and the row stays | `Confirm sends one DELETE by id; a 204 removes the row`, `deleting the last document...`, to-do/event 204 + 403/503, 403/409/409/503/500 table, network failure, `confirm then delete in the detail view` (204 then 403) |
| 5 no message has Delete | `messages > a thread has no Delete action...`, and the messages assertion in the Collaborate detail test |
| Negatives (scope adjustments) | `Service request: ... the request itself has no Delete` (only the own document's Delete exists), `Service request with no documents of its own: no Delete control exists in the dialog at all`, `a document's own detail has no documents section and no Delete`, service request reads the servicerequests route only (and never the module fetch), `without a caller identity, no row offers Delete`, `/me fails: no Upload and no Delete`, 401 is the sign-in state |
| 6 gates | above |

**Fail-before proof.** I put the HEAD versions of the six changed source files back (`DocumentLibrary`, `SmartTodo`, `EventsCalendar`, `RecordDetailModal`, `RecordDetailFields`, `web-api-client`) and ran the new test file against them: **34 failed, 9 passed** of 43 (the 9 are the pure rule/message tests and the negative controls that hold both before and after: no caller means no Delete; no Create means no Upload; `/me` failing; the document's own detail). My versions were restored from a copy and the full suite was re-run afterwards (635 passed). No `git stash` was used.

## Known limits

- Live behaviour is unverified: no browser run, and the target columns need task 020 (`sprk_createdbycontact`) imported before the list reads return the stamps (task 032 deviation 5): until then the list is flagged truncated and no row is recognised as own.
- jsdom does no layout: the Delete icon placement in the grid, the dark-mode look and the nested confirm-over-modal stacking were not seen on screen. Confirm once in a browser and in the Teams tab.
- A 403 can mean "not yours", "lost Read on the parent" or "absent" (uniform by design), so the message names the two real causes.
- Delete is hard: there is no undo, and the dialog says so.

## Server addition: /me systemUserId

`GET /api/v1/external/me` now returns an optional `systemUserId` (`Guid?`, camelCase) — the caller's own `CallerPrincipal.SystemUserId`, the same property `ExternalDataService.IsCreatedByCaller` reads; null for CIAM partners and workforce contacts, never `Guid.Empty`. Files: `Api/ExternalAccess/ExternalUserContextEndpoint.cs`, `Api/ExternalAccess/Dtos/ExternalUserContextResponse.cs`, tests in `CallerPrincipalTests.cs` (`Me_*`). Existing members and their order are unchanged. The SPA's `delete-own.ts` already consumes the field; the server's delete check stays the authority.

- **Existing:** `/me` itself already returns the caller's identity (`ContactId`, `Email`, `Projects`).
- **Extension:** Yes — one optional member appended to the existing `ExternalUserContextResponse`; no new endpoint, service or DI registration.
- **Cost of doing nothing:** Licensed staff never see Delete on rows they created, because the SPA cannot match the `sprk_createdbyperson` stamp.

Also added to the document list: `canCreate` (see "Review fix round", fix 2).

## Review fix round

An adversarial review found five defects. All five are fixed here, still uncommitted. Final counts: SPA `npm run typecheck` exit 0, `npm run lint` exit 0 (0 problems), `npx vitest run` **26 files, 650 tests passed, 0 failed** (the run just before the last test edit failed 1 test, the `RecordDetailModal.test.tsx` malformed-id case, see fix 5), `npm run build` exit 0, `npm run check:dist` exit 0 ("1 file(s) clean"). `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ExternalAccess|FullyQualifiedName~CallerPrincipal"`: **2118 passed, 0 failed**. `dotnet test tests/Spaarke.ArchTests/`: **940 passed, 0 failed**. No test failed with `TaskCanceledException`.

### Fix 1 (F4): the failing "confirm then delete in the detail view" test - what it turned out to be

**A jsdom artefact, not a defect in `SprkModal`, `ConfirmModal` or 054's code. Nothing under `src/client/shared` changed** (so no jest run and no `node_modules` junction were needed). The reviewer's description (hidden "after the first open/close") was not the cause. What the probes showed:

1. The test passes alone and fails in the full file; it fails after earlier tests that opened a detail dialog without confirming (for example "Project, Collaborate" and "a document's own detail"), and pass or fail varied with how fast the test reached its second query. That is a debounce, not state left behind by the first confirmation.
2. Probe on the failing run: the confirmation surface had focus (`document.activeElement` was the `alertdialog` div) but tabster's `modalizer.activeId` stayed the OUTER detail dialog's id for 1.2 s; about 250 ms after the confirmation opened, tabster's debounced hide pass set `aria-hidden="true"` on its surface. The first confirmation is hidden the same way; the tests only passed when they queried inside that 250 ms window.
3. Cause, read in `tabster` 8.7 and Fluent `react-dialog` 9.17: Fluent's `useFocusFirstElement` calls `findFirstFocusable(surface)`. tabster rejects any element whose `offsetParent` is `null` (`isDisplayNone`) and treats a 0x0 `document.body.getBoundingClientRect()` as invisible (`isVisible`). jsdom does no layout, so both always hold and nothing is focusable. Fluent falls back to focusing the surface itself. tabster activates an already-focused modalizer only when the focused element is INSIDE it (`element !== focusedElement && contains`), so a surface that is focused itself never becomes the active modalizer; the outer dialog stays active and the confirmation is hidden. In a browser the first button is found and focused, which activates it.
4. **Proof.** Same code and same test file with only two geometry stubs (`HTMLElement.prototype.offsetParent` returns the parent; `body.getBoundingClientRect` returns 1024x768): the confirmation becomes the active modalizer, focus lands on a `BUTTON` inside it, and `aria-hidden` stays null for 600 ms. With the stubs removed the same suite fails 17 tests.
5. **Fix.** `DetailDocumentsDelete.test.tsx` installs those two stubs in `beforeEach` (restored in `afterEach`), with a comment citing the evidence. `confirmDialog()` now waits out tabster's 250 ms hide pass and asserts the dialog is not `aria-hidden` before the test queries inside it, so every confirmation test proves it is reachable by role (no `hidden: true` anywhere). Queries made at the instant a confirmation closes use `findByRole`, because tabster lifts the page's `aria-hidden` on the same 250 ms debounce (the real behaviour of a modal; before, jsdom never activated the modal, so nothing behind it was ever hidden).
6. New tests: `cancel, then delete in one detail view` and `two consecutive deletes in one detail view`. **Fail-before:** with the stubs disabled the file fails 17 of 58 (every confirmation test, including the three detail-view ones). The original test failed in the full-file run at line 891 before the change.
7. **Owed live (browser) checks** for the owner's UI test, because jsdom cannot show real focus or screen-reader behaviour: (a) in the detail dialog, Delete on a document, then **Cancel, then Delete again**: the second confirmation must take focus and be operable by keyboard (Tab stays inside it; Enter and Space press the focused button) and by a screen reader; (b) **two consecutive deletes** in one detail view; (c) Escape does not dismiss the confirmation (`dismiss="alert"`) and the detail dialog is operable again after it closes. If any of these fails in a real browser it is a Fluent nested-modalizer defect and belongs in the shared `SprkModal`; the evidence above says it will not.

### Fix 2 (F4, AC 2): Upload offered to a ViewOnly reader on matters and work assignments

- **Server** (`ExternalProjectDataEndpoints.cs` `ListDocumentsForParent`, `Dtos/ExternalProjectDtos.cs`): the list returns a new `ExternalDocumentListResponse` with the existing members in their order (`value`, `truncated`, same names and omission rule) plus `canCreate`, always written, computed from the same `rights` the upload route checks (`HasFlag(Create)`). The four list routes' `Produces` now name it. The caller has just passed the Read check on that parent, so it discloses nothing new. A project's `canCreate` is also returned; the SPA ignores it for projects (they keep their level from `/me`).
- **Client**: `getRecordDocuments` returns `canCreate` (`null` when the BFF did not say). `DocumentLibrary`: `showUpload = canUpload ?? listCanCreate ?? canUploadOrDownload(accessLevel)`, so a missing member fails closed. The detail view passes a `canUpload` override only for a project; a matter, work assignment and service request leave it to the list. The service request requester always has Create, so nothing changes there.
- **Server tests** (`ExternalRecordDocumentRoutesTests`): ViewOnly gives `canCreate=false`, Collaborate and FullAccess give `true` on projects, matters and work assignments (9 cases, also asserting the wire member is camelCase and after `value`, and that the upload route agrees: 403 when false, 201 when true), and the service request requester gets `true`. **Fail-before:** with `CanCreate = true` forced, the 3 ViewOnly cases fail.
- **Client tests**: ViewOnly matter and work assignment show no Upload (rows and empty state), Collaborate shows it, a BFF without the member shows none, a project ignores the list and follows `/me`, and a host `canUpload={false}` beats the list. **Fail-before** (old rule "Upload for every non-project root"): 5 tests fail.
- **Component justification (CLAUDE.md section 11):**
  - Existing: the documents list envelope (`ExternalCollectionResponse<T>`, `GetDocumentsAsync`).
  - Extension: one additive member, on a document-only copy of the envelope (the shared generic envelope was not touched, so no other list gains it).
  - Cost of doing nothing: ViewOnly readers of a matter or work assignment are offered an upload that always fails with a 403.

### Fix 3 (F2): stale comments

`auth/bff-client.ts` (`systemUserId?`) and `api/delete-own.ts` (`CallerIdentity.systemUserId`) now state the contract: optional, `null` for CIAM and contact-only callers, never empty. Findings 1 and 2 above no longer contradict "Server addition". No test (comments only).

### Fix 4 (F2): `DocumentLibrary` with no record id spun forever

`DocumentLibraryProps` is now a discriminated union, `{ projectId } | { root; recordId }`; the compiler refuses `<DocumentLibrary />`, `root` alone, `recordId` alone and `projectId` mixed with `root` (checked with a throwaway file under `src`: four TS2322 errors, the two valid shapes clean; the file was deleted). Tests are not in the `tsc` include, so this is a manual check, not a gate. Defensive branch: an empty id sets `loading` false and the error "Documents cannot be loaded: no record was given.", and makes no BFF call. Tests: `an empty projectId` and `an empty recordId` (2). **Fail-before:** both fail with the old early return (spinner forever).

### Fix 5 (F3): one `/me` per record paged, unmemoised identity

`RecordDetailModal` calls `useCallerContext(open)` (was `open && state.status === 'ready'`, which flipped on every page) and `caller = React.useMemo(() => callerIdentityOf(callerContext), [callerContext])`. Test: `paging to the next record does not read /me again`; **fail-before:** the old enable condition gives 2 `/me` reads. The memo has no direct test. Side effect, handled: `/me` is now read as soon as the dialog opens, even for a record that then fails to load, so `RecordDetailModal.test.tsx` "a row id that is not a GUID is never sent to the BFF" no longer asserts that `bffApiCall` was never called; it asserts that no call other than `/me` was made and that no call carries the injected id (the property it protects, that the id never reaches the BFF or FetchXML, is unchanged).

`useCallerContext` is a second reader of `GET /me`, beside `useExternalContext` (the app-level hook). That is acceptable: `useExternalContext` is the shell's page-level hook with a blocking error state; the detail view needs a silent, deferred read that never blocks the dialog and must not change what the shell shows. One extra `/me` per opening is cheap; folding the two into one shared context is a larger change and was left alone.

### Reviewer's K items (known limits, not fixed)

- **`Guid.Empty` systemuser:** `/me` never emits the empty GUID and the SPA treats it as unusable too, so such a principal is "no identity" (Delete withheld); the server's delete check remains the authority.
- **A silent `/me` failure:** `useCallerContext` swallows the error by design: no Delete anywhere and, for a project, no Upload, with no message. The server still authorises every action.
- **Project-page Documents tab difference:** the project page tab still hides Download for ViewOnly (Collaborate+ default) and offers no Delete (it passes no `caller`), while the detail view shows Download to every reader. Left as it was (finding 3).
- **Live and dark-mode check still owed:** nothing here was seen in a browser (jsdom has no layout): the Delete icon placement, dark mode, and the nested confirm-over-modal focus behaviour (fix 1, point 7) need one live pass by the owner, in a browser and in the Teams tab.

## Owner decisions 2026-10-11: SR delete + recall, invoice documents

Date: 2026-10-12. SPA half of the owner decisions (the BFF half is in `owner-decisions-2026-10-11-bff.md` and `task-031-notes.md`). Left UNCOMMITTED for the main session to commit by path. This supersedes scope adjustments 1 and 2 at the top of this file (service-request delete not built; invoice documents not added): both are now built.

### What was built (all under `src/client/external-spa/`)

1. **Delete a service request (the requester only).** `DELETE /api/v1/external/servicerequests/{id}` through the existing delete-own flow (`useDeleteOwn`, `DeleteOwnConfirm`, shared `ConfirmModal`).
   - **Who sees it.** `ServiceRequestActions` renders only when the record's `sprk_requestedby` is the caller's contact (`isRequestedByCaller`: reads the id out of the entity reference the module fetch returns, never a name; a missing id, a missing identity or a systemuser with no linked contact shows nothing). The `/me` identity is the one the detail view already reads (`useCallerContext`). The server's requester gate stays authoritative: a 403 shows the existing denial wording.
   - **204.** The detail view shows "This service request was deleted" in place of the request's sections (fields, actions, documents, messages); no further read for it is made. The grid is re-read when the dialog closes (see Review fixes 2026-10-12).
   - **409 `has_related_records`.** `deleteFailureOf` has a new case: "This service request already has activity (documents, messages or other records), so it can't be deleted. You can ask legal to recall it. Nothing was deleted." (the client's own wording, as for the other messages; the server's `detail` is not echoed). `DeleteFailure` now carries `reasonCode`. The request stays, and a warning bar offers **Send recall request**.
   - **Recall.** A short box (shared `FormModal`, size `sm`) with a prefilled, editable text ("Please recall this request."), the same 1 to 10,000 character rule and counter as the thread's compose box. Send posts through the EXISTING client, `sendMessage('servicerequests', id, text)` (`POST /api/v1/external/servicerequests/{id}/messages`, body `{ sprk_body }`); no new client or route. `sendErrorOf` (the thread's own error wording) is now exported from `RecordMessages.tsx` and reused. A lock closes the double-click gap (the server has no idempotency guard). On 201: the box closes, a success bar says "Legal has been asked to recall this request. You can follow the reply in Messages.", the 409 bar goes, and Delete is withdrawn (the request now has a message, so the server would answer 409 again). The visible thread re-reads through a new optional `RecordMessages` prop `refreshSignal` (never appends the 201 body, review 034 K3). A failure keeps the box open with the edited text and shows the reason inside it (401 shows the shared sign-in state). While the post is in flight the detail dialog's Close, x and browse buttons are held (same hold as a message send).
   - **503.** The generic error bar says nothing was deleted and to try again in a moment; Delete stays available and a retry works.
2. **Invoice documents on the detail view.** `DocumentRoot` gains `'invoices'`; `RECORD_DETAIL_ENTITIES.sprk_invoice.documentsRoot = 'invoices'`; `DocumentLibrary` names the noun "invoice". An invoice's detail now shows the Documents section over `/api/v1/external/invoices/{id}/documents` (list, download, versions, upload), with Upload gated by the list's `canCreate` exactly as for matters and work assignments (a missing `canCreate` fails closed; `uploadOverride` only overrides a project). Delete on an invoice document is the document's own delete route (created-by-me rule, unchanged).

### Files

Changed:
- `src/client/external-spa/src/api/delete-own.ts` (409 `has_related_records` wording and `REASON_HAS_RELATED_RECORDS`; `reasonCode` on the failure; `requestedByIdOf`, `isRequestedByCaller`)
- `src/client/external-spa/src/api/web-api-client.ts` (`deleteOwnServiceRequest`; `DocumentRoot` + `'invoices'`; comments)
- `src/client/external-spa/src/components/DocumentLibrary.tsx` (invoice noun)
- `src/client/external-spa/src/components/detail/RecordDetailFields.tsx` (invoice `documentsRoot`)
- `src/client/external-spa/src/components/detail/RecordDetailModal.tsx` (mounts `ServiceRequestActions`; deleted state; recall refresh and busy hold)
- `src/client/external-spa/src/components/detail/RecordMessages.tsx` (`refreshSignal`; exports `sendErrorOf` and `SendError`)
- `src/client/external-spa/tests/DetailDocumentsDelete.test.tsx` (harness: `invoice` grid, `record` overlay, message POST handler; 24 new tests)
- `src/client/external-spa/tests/RecordDetailModal.test.tsx` (the invoice no-documents case becomes the invoice-documents case; the network mock serves the invoices documents route)

New:
- `src/client/external-spa/src/components/detail/ServiceRequestActions.tsx`

### Test evidence (in `src/client/external-spa`)

- `npm run typecheck`: exit 0. `npm run lint`: exit 0, 0 problems. `npm run build`: exit 0. `npm run check:dist`: exit 0 ("1 file(s) clean").
- `npx vitest run` (full): **26 files, 674 tests passed, 0 failed**, run twice (95.16 s and 79.97 s), no flake. Before this change the suite was 650; `DetailDocumentsDelete.test.tsx` goes from 58 to 82 tests (24 new; the old "the request itself has no Delete" case is renamed "caller is not the requester"); `RecordDetailModal.test.tsx` keeps its count (the invoice case of one `it.each` is split into its own test).
- **Fail-before.** With `ServiceRequestActions` not mounted and the invoice `documentsRoot` removed (my versions restored from copies afterwards; no `git stash`), a representative subset of the new tests fails 8 of 8: requester sees Delete, 204, 409 notice, recall success, invoice list and the three `canCreate` cases (the SR ones after the 60 s `findBy` timeout, the invoice ones at once).
- Coverage map (in `DetailDocumentsDelete.test.tsx`): requester sees Delete / non-requester does not (3 cases) / other record types never / the rule reads ids, not names; asks first and Cancel sends nothing; 204 (one DELETE, deleted state, no thread); 403 (existing denial message); 503 (retry wording, Delete stays, retry succeeds); 409 (activity message, request stays, recall offered, nothing posted); recall posts the EDITED text exactly once to the messages route (a double click is one post); recall success (confirmation, box closes, thread re-read, Delete withdrawn); recall failure (error, box kept, text kept, retry works); blank or over-long cannot send, Cancel posts nothing; recall 403 wording; invoice list, download, `canCreate` true/false/missing, empty-state gating, multipart upload to the invoices route, delete of an own invoice document.
- The geometry stubs (`offsetParent`, body rect) from the task 054 review fix are reused. The nested recall box is queried through `recallDialog()`, which waits out tabster's 250 ms pass and asserts the box is not `aria-hidden`.

### Known limits

- **The grid behind the dialog is not refreshed after a 204.** The request's row stays in the grid until the grid reloads; SUPERSEDED by Review fixes (2026-10-12): as first written, paging away and back re-read the gone record and showed "You don't have access", not "was deleted" - the claim that paging back shows "was deleted" was false before that fix. `ExternalDataGrid` has no refresh handle and its wrapper text is pinned by `ExternalSpaGridViewSelectorGuardTests`, so it was not touched.
- **Recall is a request, not a deletion.** After a recall the request cannot be deleted from the SPA (the server would answer 409). Withdrawing the Delete action reflects that.
- The Delete decision uses the `sprk_requestedby` id the module fetch returns. The shape was read from `FetchResponseDto` / `FetchService` (an entity reference), not observed live; if a deployment serialises the lookup differently the action stays hidden (fail closed).
- Dev mock mode has no handler for the new DELETE (the detail view is unreachable in mock mode, task 051 deviation 5).

### Live browser checks owed (jsdom has no layout)

1. **Delete with no related records.** As the requester of a fresh service request with no documents, messages or other records: open the detail, "Delete request", confirm. Expect "was deleted", and after closing and reloading the grid the row is gone. As a different user the same request shows no Delete button.
2. **409 then recall.** On a request with a document or a message: Delete, confirm. Expect the activity message and "Send recall request". Open the box (prefilled), edit, Send. Expect the "Your recall request was posted to this request's messages for the legal team." bar, the new message in the thread, and no Delete button. Check keyboard focus in the nested box (Tab stays inside), and that a forced failure (offline) keeps the box and the text.
3. **Dark mode and the Teams theme** for the new delete button, the warning and success bars and the recall box; and the invoice Documents section.
4. **Invoice documents:** upload on an invoice under a matter the caller can Create on; download; and an invoice under a ViewOnly matter or project shows no Upload.

## Review fixes (2026-10-12)

All under `src/client/external-spa/`, uncommitted.

- **F3 - deleted record re-read as "You don't have access".** `useRecord` caches only the last key, so after a 204, paging to another record and back re-ran the read; the gone record gave `DeniedError` and the lock showed (the earlier note that paging back shows "was deleted" was wrong, it only held while the same record stayed on screen). `RecordDetailModal` now computes `isDeleted = !!id && deletedIds.has(id)`, passes `open && !isDeleted ? id : undefined` to `useRecord` (no read), renders the "was deleted" block first and guards the loading, denied, notfound, error and ready blocks with `!isDeleted`. Test: delete SR 2 of 3, page to 3, page back to 2: "was deleted", no further read for SR 2, no `alert`, no denied or notfound block. Fails before the fix.
- **F4 - grid kept the deleted row.** New optional prop `onRecordDeleted` on `RecordDetailModal`, called on the service request's 204 (the only record-level delete the modal has; document, task and event deletes are rows inside the lists and refresh themselves). `GridWidgetBody` records it and, on close, bumps `gridKey` (`<ExternalDataGrid key={gridKey} .../>`) so the grid remounts and re-reads. `ExternalDataGrid.tsx` is untouched; `ExternalSpaGridViewSelectorGuard` ArchTest filter: 220 passed. Tests: remount after delete-and-close with the deleted row gone; no remount on a plain close; `onRecordDeleted` fires once on 204, not on a 503. Fails before the fix.
- **K3 - 403 wording.** For noun `service request`: "Only the person who submitted this request can delete it. Nothing was deleted." (the trailing sentence kept so the message still says nothing changed). Other nouns unchanged. Tests: the 403 service-request flow and a `deleteFailureOf` unit test for the four nouns.
- **K4 - accessible name and recall wording.** The `aria-label` "Delete service request" is removed; the button's name is its visible text "Delete request". The confirmation title stays "Delete service request". The recall confirmation now reads "Your recall request was posted to this request's messages for the legal team." (the post is a thread message; no email is sent). Tests updated to the new button name and text.

Known limit (unchanged by these fixes): the grid re-read is a remount, so scroll position and any in-grid sort or filter reset after a delete.
