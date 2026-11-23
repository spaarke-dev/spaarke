# Task 004 — Child dialogs inside the parent SprkModal (FR-03)

Rigor FULL (sub-agent, sonnet / high). No commit, push or stash. Nothing under `.claude/` or `src/client/shared/**` touched.

## Outcome

No record child dialog is a raw Fluent `Dialog` any more. Each is a SprkModal preset rendered by the component that owns
it, so each is a React descendant of `RecordTemplateModal` (the portal moves the DOM, not the React tree). The toolbar's
Upload, New message, Add to-do and Add event open them. Negative grep: no `<Dialog` and no `Dialog*` import from
`@fluentui/react-components` remains in `DocumentLibrary.tsx`, `EventsCalendar.tsx` or `SmartTodo.tsx` (grep exit 1).

## Files

**New**
- `src/client/external-spa/src/components/createAction.tsx` — the shared wiring (below).
- `src/client/external-spa/tests/RecordTemplateChildDialogs.test.tsx` — 17 tests.

**Modified**
- `components/DocumentLibrary.tsx` — `UploadDialog` is a `FormModal`; `VersionHistoryPanel` is a base `SprkModal`; new props
  `onCreateActionChange`, `onBusyChange`.
- `components/EventsCalendar.tsx` — `CreateEventDialog` is a `FormModal`; same two props.
- `components/SmartTodo.tsx` — `CreateTaskDialog` is a `FormModal`; same two props.
- `components/detail/RecordMessages.tsx` — the compose box renders in a `FormModal` ("New message") when the host passes
  `onCreateActionChange`; the inline box is unchanged otherwise.
- `components/DeleteOwn.tsx` — the existing `ConfirmModal` now returns focus to the control that asked when it closes.
  (`ServiceRequestActions.tsx` is unchanged after fix round 1: the Esc wrapper it had was removed.)
- `record/recordTemplate.types.ts`, `record/RecordTemplateModal.tsx` — `RecordSectionContext.offerAction(actionId)`.
- `record/sections/{Documents,Files,Messages,TasksEvents}Section.tsx` — pass `offerAction(...)` / `onBusyChange` down.
- Tests adapted: `tests/DetailDocumentsDelete.test.tsx`, `tests/RecordTemplateSections.test.tsx` (list below).
- `notes/defer-issues.md` — D-6, D-7 (Issue: PENDING).

## Decisions

1. **The list or thread owns its dialog and offers the toolbar action to the template ("section-owned offer").** Each
   section receives `offerAction(actionId)` from the template (a callback only when that action is in the template's
   toolbar) and passes it to the list as `onCreateActionChange`. The list reports `{visible, onClick}` with
   `useOfferCreateAction`. Reason: the visibility rule and the state the dialog needs (the list read, the refresh after a
   create, the in-flight flag) live inside those components; an action host in `record/actions/` would need a second read
   or another channel, and the dialog would no longer sit under the section that refreshes. The rule itself is not moved:
   each list passes the same boolean its own button used (see "Visibility").
2. **Hosts that do not pass `onCreateActionChange` are unchanged.** `ownUploadButton`, `ownCreateButton` and
   `composeInDialog` are all `onCreateActionChange === undefined`/`!== undefined`. ProjectPage, the other pages and R3's
   own pages keep their buttons and inline compose box.
3. **Presets.** Upload, Create Event, Create Task and New message are `FormModal` (Cancel in `footerStart`, submit in
   `footer`, `busy`, `submitDisabled`); delete is the existing `ConfirmModal`; the recall box is the existing `FormModal`.
   Version history is the base `SprkModal` (size sm, light dismiss, one primary Close on the right). **BrowseModal does not
   fit it:** BrowseModal is the record-browse preset (N-of-M navigation, stage and metadata grid); version history is a
   read-only list of one document's versions with nothing to page through.
4. **The record's own controls keep no create buttons in the template when the toolbar lists the action.** The Files
   section of an invoice keeps its own Upload button because the invoice toolbar has no `upload` (`FilesSection` passes only
   `onBusyChange`).
5. **Busy.** Each dialog reports its post in flight through `onBusyChange` (an effect, plus a cleanup on unmount), so the
   template's existing hold (paging, Close, Refresh) applies; Cancel and the submit are disabled by the preset's `busy`;
   the presets already ignore Esc. A failed post leaves the dialog open with the existing error text and, for messages, the
   draft.
6. **`loadedOnce` in `DocumentLibrary`.** The Upload offer stays available during a refresh after the first successful
   read, so the toolbar button does not blink out while a re-read is in flight.

## Visibility (which action shows when)

The rule is whatever each list's own button used before; task 006 changes its source and is not pre-empted.

| Action | Shown when | Where the rule lives |
|---|---|---|
| Upload | the documents list is loaded (or re-reading after a first load), has no error, and the BFF/level says the caller can create (`showUpload`) | `DocumentLibrary` (`useOfferCreateAction(..., showUpload && !error && (!loading \|\| loadedOnce))`) |
| New message | the thread has not been refused or found unavailable (`showCompose`) | `RecordMessages` |
| Add event | `canCreate && !loading && !accessFailure && !loadError` | `EventsCalendar` |
| Add to-do | `allowCreate` | `SmartTodo` |

**Add to-do and Add event are never visible inside the template yet.** `TasksEventsSection.tsx` still passes
`AccessLevel.ViewOnly` to both lists (task 006 removes it and owns the rule); the props and the dialogs are wired and are
exercised by the standalone tests with `Collaborate`. Per the brief the `ViewOnly` props were not removed.
Toolbars with the action: Upload (matter, project, work assignment, service request), New message (matter, project, work
assignment, invoice, service request), Add to-do / Add event (matter, project, work assignment).

## Deviations to flag to the owner

1. **Esc follows the modal standard (owner decision 2026-11-11, Path C: comply).** `FormModal` is `dismiss=explicit` and
   `ConfirmModal` is `dismiss=alert` (MODAL-DESIGN-SYSTEM.md, ADR-050): both ignore Esc so a stray Esc never discards a
   draft, and Fluent keeps that Esc from reaching the parent record. The first cut of this task overrode that with an
   `EscapeClosesDialog` wrapper so Esc cancelled the child (FR-03 read as "Esc closes the child first, then the parent");
   the owner chose to comply instead, so the wrapper and every use of it are gone and no Esc handler is added to any
   preset's children. Required behaviour while a child is open: Esc does nothing to the child (stays open, typed input
   intact) and never reaches the record; Cancel and the x close the child and return focus to the invoking toolbar button;
   Esc then closes the record. Version history (base `SprkModal`, light dismiss) keeps its own Esc-closes. The main session
   amends FR-03 in spec.md.
2. **Focus return is done locally.** Fluent restores focus only to a `DialogTrigger`; a controlled dialog opened from a
   toolbar button leaves focus on the page body, and an Esc pressed there reaches no dialog, so the Esc that should close the
   record could not. `useChildDialog` records the invoker on open and `restoreFocusTo` returns focus to it on close
   (`setTimeout(0)`, only if still connected; since fix round 1, to the record dialog around it when the invoker is gone). Not verifiable in a real browser from here, only in jsdom (see Risks).
3. **Empty-state wording changed in template mode only.** Messages: "No messages yet. Use New message in the toolbar to
   write the first one." Events and to-do: the hint names the toolbar action ("Use Add event in the toolbar…", "Use "Add
   to-do" in the toolbar…"). Without `onCreateActionChange` the old texts are unchanged.
4. **Pre-existing, not fixed:** Create Event collects an event type and never sends it (D-6).

## Tests

### New: `tests/RecordTemplateChildDialogs.test.tsx` (17)

Drives the real `RecordTemplateModal` for a service request opened by its requester (toolbar: Upload, New message, Delete
request), the real Documents and Messages sections, `DocumentLibrary`, `RecordMessages`, `SprkModal` and Fluent's dialog
and tabster focus manager. Substituted: `bffApiCall` and jsdom layout (the same `offsetParent` / `getBoundingClientRect`
stubs `DetailDocumentsDelete.test.tsx` uses, so tabster sees a visible tree).

- One form dialog (Upload) and one confirm dialog (Delete request), as the brief requires, plus New message, version
  history, and Add event / Add to-do through the lists' own seam.
- Tab and Shift+Tab never leave the child: `tabWrapsInside` presses Tab on the outermost tabbable (tabster's end
  sentinel) and Shift+Tab on the first, and requires focus to land on the first and last REAL control inside the dialog.
- Esc (modal standard): with focus asserted inside the child, Esc leaves the child AND the record open and keeps the typed
  value (the chosen file for Upload, the draft for New message); Cancel closes the child with focus back on the toolbar
  button; a later Esc calls the record's `onClose` once. For Upload (form), New message (form) and Delete request (confirm).
- Cancel is in the left footer slot (first child of the footer, primary in the second) for Upload, New message, Delete
  request, Create Event and Create Task.
- The child is a React descendant of its section: a key pressed inside the dialog reaches a handler on the section.
- Same requests and results: multipart POST to the record documents route then the list is re-read; `{ sprk_body }` JSON to
  the messages route then the thread is re-read; Create Event and Create Task bodies; failures keep the dialog open with the
  existing error texts (413 "The file is too large to upload.", the message send text, "Failed to create event:", the
  server's message).
- Hold: an upload in flight disables the record's footer Close and the dialog's Cancel and Esc closes nothing; after the
  failure the record's Close is enabled again.

### Can-fail proof (the focus/Esc test is not vacuous)

Permanent controls in the file:
- `a non-trapped (non-blocking) dialog does not contain Tab` — the same `tabWrapsInside` probe against a
  `SprkModal nonBlocking` reports `false`.
- `a dialog rendered beside the section ... the ancestry probe is silent` — a sibling dialog does not reach the section.

Mutations run against the real components (each reverted, files byte-identical afterwards):
(Round 1's mutations are in "Fix round 1" below; the original `EscapeClosesDialog` mutation no longer applies.)
2. `restoreFocusTo` does not focus → (original run) 4 fail; re-run in fix round 1: 7 fail.
3. The Upload dialog replaced by `SprkModal nonBlocking` (no trap) → the Upload Tab test fails (with three others that
   depended on the real preset's buttons).
So the stop trigger "Fluent's focus trap is not observable in vitest/jsdom" did not apply: with layout stubs tabster's
trap is observable and the test fails without it.

### Changed assertions in existing tests (each with its reason)

`tests/DetailDocumentsDelete.test.tsx` — the template (record detail) tests only; 11 assertions. In the record template
the Upload entry point is the toolbar button "Upload documents", not the list's "Upload Document" button, so the
presence/absence assertion moved to where the button now is, keeping the same condition:
- L1071 (project, Collaborate), L1135 (matter canCreate), L1171 (work assignment Collaborate): `within(docs).getByRole('button', { name: /Upload Document/ })` →
  `await within(dialog|collaborate).findByRole('button', { name: 'Upload documents' })` (offered).
- L1106, L1122, L1150, L1164, L1183, L1195, L1215: `queryByRole(... /Upload Document/)).toBeNull()` →
  `queryByRole('button', { name: 'Upload documents' })` on the record dialog (not offered). Without this move these
  would pass for the wrong reason (the list button no longer renders in the template).
- L1244 (per-record caller, `offered`): the same swap for the true/false parameterised case.
The K3 Esc test (fix round 1) asserts the open child by role and not aria-hidden, under the layout stubs. Untouched: the invoice Files tests (L1990-2044; invoice has no toolbar Upload, its own button remains), the standalone
DocumentLibrary/ProjectPage/PerRecordWriteAccess tests.

`tests/RecordTemplateSections.test.tsx`:
- Grid test "opens the record template with the record's read-only fields": was "exactly one textarea in the modal, inside
  Messages". Now the record modal holds NO input at all (the compose box is not inline), and for entities with a thread the
  toolbar's New message opens a dialog holding exactly one textarea that is not inside the Messages section; Cancel closes
  it. (5 grids.) A 350 ms wait precedes the click so the child is not opened inside the parent modal's 250 ms hide pass.
- `while a message is being sent (review 051 K3)` (3 tests): `startSend` now types and sends in the New message dialog
  (opened from the toolbar, after the same 350 ms wait); the "Sending…" button is looked up on `screen` (it is in the
  child's portal); the parent's controls are read with `hidden: true` while the child is open; the Esc test asserts the
  record, the New message dialog and "Sending…" all remain. The assertions' intent (a send in flight holds paging, Close,
  ×, Esc) is unchanged.

### Test scope justification

The new file proves the focus/Esc/Cancel contract on one form and one confirm dialog through the real parent (the only
place the contract can break: tabster, portal React-tree membership and the hold). The send/refresh/failure payloads are
asserted next to it so a refactor to the presets cannot silently change a request. Add event / Add to-do go through the
lists' own seam because the template cannot offer them until task 006. These are scaffolding for the FR-03 contract; the
test-diet gate at project close decides what is kept.

## Results

- (First cut) `npm run typecheck`: clean. `npm run lint`: clean (0 errors, 0 warnings).
- (First cut) `npm test` (full, one run): 49 files, 1360 tests, all passed (new file: 17). An earlier full run had one failure in the K3 Esc test (below) and one unrelated timeout in `DocumentUploadPage.externalUpload` under load; neither recurred.
- `pwsh scripts/check-task-status-drift.ps1 spaarke-SPA-external-access-platform-r4`: exit 0.

## Risks / not verified

- Focus return and the Esc order are verified in jsdom only; not seen in a real browser (no live checks allowed).
- The nested-dialog timing: a child opened within ~250 ms of the parent opening is hidden by tabster's pass (the same
  quirk R3's recall dialog has; the existing tests use `settleModalizer`). Only reachable by a very fast keyboard user.
- Version-history Close sits on the right (PreviewModal/BrowseModal convention for a single primary).
- Add to-do / Add event unreachable in the template until task 006 changes the rule source.

## Fix round 1 (2026-11-11)

Owner decision (binding): Esc follows the modal standard, Path C (comply) - see Deviation 1. Review findings F2, F3, F4 and
the focus fallback fixed in the same round. Nothing committed, pushed or stashed; `.claude/` and `src/client/shared/**`
untouched.

### Esc: Path C

- Removed `EscapeClosesDialog` from `createAction.tsx` and from `DocumentLibrary` (Upload), `EventsCalendar` (Create Event),
  `SmartTodo` (Create Task), `RecordMessages` (New message) and `DeleteOwn` (Delete confirm); `ServiceRequestActions.tsx` is
  back to its pre-task text (its only change was the wrapper). `grep -rn EscapeClosesDialog src tests` now matches nothing.
- Tests rewritten (`RecordTemplateChildDialogs.test.tsx`): Upload (form), New message (form) and Delete request (confirm):
  Tab / Shift+Tab stay inside (Upload, Delete); with focus asserted inside the child, Esc leaves child AND record open and
  keeps the value (chosen file / draft); Cancel closes the child, focus returns to the toolbar button, a later Esc calls the
  record's `onClose` once; Cancel is in `footerStart` (unchanged test). Added: the x closes Upload with focus back.
  Version history keeps "Esc closes it".
- **Mutation (can fail):** an Esc-closes-the-child handler re-added to the Upload dialog -> the Upload Esc test fails (1);
  `restoreFocusTo` not focusing -> 7 tests fail (Upload Esc, x, Cancel, New message x2, the fallback unit test, Delete
  request). Files restored byte-identical afterwards.

### Findings

1. **F2 (New message orphaned by a refused read).** `RecordMessages.tsx`: when `showCompose` goes false while
   `composeDialog.open`, an effect calls `composeDialog.hide()` (open state reset, focus restored). Chosen over keeping the
   dialog mounted to show the refusal because it is the smaller change and keeps the offer rule and the denied state in the
   thread unchanged. Test: `a thread read that is refused while New message is open closes the dialog properly` (first read
   pending, New message opened, 403): no dialog, no New message button, focus inside the record (not <body>), Refresh brings
   the offer back and the dialog stays closed (open state was reset), Esc closes the record. Without the effect the test
   fails (focus assertion) - mutation run.
2. **F4 (shared busy key).** `TasksEventsSection.tsx`: each list now has its own hold (`reportEvents` / `reportTodos`, a ref
   of two booleans); the section reports busy while either is busy, so the template's one `section:tasksEvents` flag is
   released only when both are idle. Kept inside the section (no change to `RecordTemplateModal`). Tests:
   `tests/TasksEventsSection.busyHold.test.tsx` (4): one list idle does not release the other (both directions), both busy
   until the last settles, control (no list busy never reports busy). With the section passing `onBusyChange` straight
   through: 3 of 4 fail - mutation run.
3. **Focus fallback.** `createAction.tsx`: `currentFocusTarget()` now returns `{ element, dialog }` (the dialog is the nearest
   `[role=dialog], [role=alertdialog]` of the focused control - no entity literals); `restoreFocusTo` focuses the element
   if connected, else the dialog (given `tabindex=-1` if it has none) so a later Esc still reaches the record. Callers
   (`useChildDialog`, `DeleteOwn`, the version-history opener in `DocumentLibrary`) hold a `FocusTarget`. Tests: a unit test
   (element still there -> element; detached -> dialog; neither -> no-op) and the F2 test above. Without the dialog
   fallback both fail - mutation run.
4. **F3 (accessibility asserted).** `openChild` settles 350 ms and asserts `child.closest('[aria-hidden="true"]')` is null.
   In `RecordTemplateSections.test.tsx` the K3 send-hold block installs the layout stubs (moved to `tests/jsdomLayout.ts`,
   shared with the child-dialogs file), asserts the New message dialog by role and not aria-hidden (before and after Esc),
   presses Esc inside it with focus asserted there, and the two 20 ms sleeps became a microtask `flush()`. The two 350 ms
   waits stay, commented: a real tabster debounce timer must run. With `installLayout` made a no-op the three K3 tests fail
   on the aria-hidden assertion - mutation run.

### Checks

- `npm run typecheck` clean; `npm run lint` clean (src and tests); prettier applied to the touched files.
- Touched files: ChildDialogs 21, Sections 55, TasksEventsSection.busyHold 4 (new), DetailDocumentsDelete 96, RecordMessages 50, RecordTemplateModal 43: 269 passed.
- Full `npx vitest run` (one run): 50 files, 1368 tests; 1344 passed, 24 failed in 13 UNTOUCHED files (App.deepLinkSignIn, App.notificationBell, App.removedRoutes, DocumentUploadPage.externalUpload, invite-code, InvoiceSubmitPage, main-bootstrap, no-mock-identity, teams-home-auth, teams-real-msal, token-redirect-loop, workforce-auth, workforce-home-auth), every one a ~110-117 s timeout (machine load during the full run). Those 13 files re-run alone: 458 passed, 0 failed.
