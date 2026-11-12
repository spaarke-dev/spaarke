# Task 006 — Per-record access: hide, don't disable; "Your access" (FR-06, FR-07, NFR-04)

Rigor FULL (implementer sub-agent). No commit, push or stash. Nothing under `.claude/`, `tasks/`, `TASK-INDEX.md`,
`current-task.md` or the project `CLAUDE.md` was touched. No BFF code was changed.

## Outcome

On matters and work assignments, Add to-do and Add event are now offered when, and only when, the record's own list
answered `canCreate: true`. Before this task they were never offered: `TasksEventsSection` forced `AccessLevel.ViewOnly`,
and SmartTodo and EventsCalendar had `root === 'projects'` gates. The creates post to `/{root}/{id}/todos|events`.

Upload, New message, Add to-do and Add event inside the record template now follow one rule, `createRightOf`. "Your
access" is filled in the header, and each read-only section shows one line explaining why. A caller without write sees
Refresh and Copy link in the toolbar, plus the per-row Download, and nothing disabled. ProjectPage is unchanged: its
tests pass unmodified.

Greps run in `src/client/external-spa/src`:
- No `AccessLevel.ViewOnly` is passed as a prop.
- `root === 'projects'` no longer appears in `SmartTodo.tsx` or `EventsCalendar.tsx`.
- The remaining `root === 'projects'` hits are:
  - `web-api-client.ts` `createRootEvent`: keeps the project route's body bind. It is a body, not a gate.
  - `DocumentLibrary.tsx` `uploadFromList`: the R3 standalone rule, unchanged.

## Files

**New**
- `src/client/external-spa/src/components/record/recordAccess.ts`. One place for:
  - `createRightOf`: one create action's right.
  - `decidingLevelOf` / `recordLevelOf`: the project `/me` level, fail-safe for gating and strict for the header.
  - `createRightsOf` / `isCreateActionOffered`: what the template knows from the lists' offers.
  - `yourAccessOf`: the header value.
  - `readOnlyNoteOf`: the section line.
  - `RECORD_ACCESS_TEXT`: every text, in one map.
- `src/client/external-spa/tests/RecordTemplateAccess.test.tsx`: 41 tests.

**Modified (src)**
- `api/web-api-client.ts`:
  - New `createRootTodo(root, id, payload)` and `createRootEvent(root, id, payload)`.
  - `createTodo` / `createEvent` are now thin project wrappers.
  - `createRootEvent` adds `sprk_RegardingProject@odata.bind` for `projects` only. The project body is byte-identical to
    before. The matter/WA body has only `sprk_name` and `sprk_duedate`, which `CreateExternalEventRequest` accepts.
- `api/messages-client.ts`: `ExternalMessagesResponse.canCreate?`.
- `components/createAction.tsx`: `CreateActionOffer.right?`. `useOfferCreateAction(report, visible, open, right?)`
  re-reports when `right` changes.
- `components/SmartTodo.tsx`:
  - `accessLevel` is optional. Hosts pass it only where a `/me` level decides.
  - The create goes through `createRootTodo`.
  - Template mode: `allowCreate = createRightOf(...) === true`.
  - Standalone mode: the old rule, `levelAllows ?? listCanCreate === true`.
  - Toggle: `accessLevel !== undefined && (writeAccessPerRecord || canEdit(accessLevel))`, which replaces the
    `root === 'projects'` gate.
- `components/EventsCalendar.tsx`:
  - The same pattern as SmartTodo.
  - `CreateEventDialog` takes `root` / `recordId` and posts through `createRootEvent`. The dialog no longer builds the
    bind itself.
- `components/DocumentLibrary.tsx`:
  - In template mode, Upload is offered on `createRightOf({loaded, listCanCreate, levelAllows: canUpload}) === true`.
  - The empty-state copy follows the same answer.
  - The standalone own button is unchanged.
- `components/detail/RecordMessages.tsx`:
  - The thread keeps `canCreate` from its read.
  - In template mode, New message is offered only on `canCreate === true` (the thread is loaded and said so).
  - The open dialog closes through `hide()` if a re-read withdraws it.
  - The empty text no longer points at a toolbar button that is not there ("No messages yet.").
  - The inline (R3 page) mode is unchanged.
- `components/record/recordTemplate.types.ts`: `RecordSectionContext.readOnlyNote?` and `RecordHostAction.right?`.
- `components/record/RecordTemplateModal.tsx`:
  - The toolbar shows a create action only when `isCreateActionOffered`, meaning the offer is visible AND the reported
    right is `true`.
  - The header gets `access = yourAccessOf(...)`.
  - Each section gets `readOnlyNote = readOnlyNoteOf(...)`.
- `components/record/sections/RecordSectionCard.tsx`: an optional `note` line, using tokens only (`fontSizeBase200`,
  `colorNeutralForeground3`), test id `record-access-note-{section}`.
- `components/record/sections/TasksEventsSection.tsx`:
  - The forced `AccessLevel.ViewOnly` is removed.
  - It passes `decidingLevelOf(...)` (a level for projects only), `writeAccessPerRecord` and the note.
- `components/record/sections/DocumentsSection.tsx`:
  - `uploadOverride` now uses the shared `decidingLevelOf`. The rule is unchanged: an unknown `/me` or a missing entry
    is View Only.
  - Passes the note.
- `components/record/sections/MessagesSection.tsx`: passes the note.

**Modified (tests)**, with the reason for each changed assertion:
- `tests/PerRecordWriteAccess.test.tsx`: the matter toggle test now renders with no `accessLevel` (hosts pass none for a
  matter), and asserts no toggle. Reason: nothing answers whether a matter to-do may be PATCHed (see "Toggle finding").
  The old test pinned the toggle under the removed ViewOnly hard-code.
- `tests/DetailEventsTodosRoots.test.tsx`:
  - The standalone test now asserts, as three distinct must-fire/must-not-fire cases:
    - a matter list with no `canCreate` and no level offers nothing;
    - a WA list with `canCreate: true` offers Create Event and Create new task;
    - a project at Collaborate offers both.
  - The template "read-only" test now also asserts that the toolbar's Add event / Add to-do are absent.
  - The fixture's lists send no `canCreate`, so these are fail-closed cases.
- `tests/RecordTemplateChildDialogs.test.tsx`:
  - Message reads answer `canCreate: true`. The requester may post, and New message is now offered only on that answer.
  - The task 004 F2 test is rewritten. It opened New message while the first thread read was pending, which is now
    impossible because loading means not offered. The same orphan-dialog risk is now driven by a re-read that withdraws
    the right while the dialog is open: read 1 may post, read 2 may not, read 3 may again. The test asserts:
    - the dialog closes;
    - focus is inside the record, not on the body;
    - the button is gone;
    - Refresh brings it back with the dialog still closed;
    - Esc closes the record.
  - The Refresh click is wrapped in `act`, as the original refused read was. See out-of-scope finding 4 for why.
- `tests/RecordTemplateSections.test.tsx`: message reads answer `canCreate: true`, so New message is offered as before.

## Decisions and deviations

1. **The template rule and the standalone rule differ, on purpose.**
   - In the template, `createRightOf` applies:
     - not loaded → `null`;
     - no level → the list's boolean, and a missing one is `null`;
     - with a level (project, a caller without `writeAccessPerRecord`) → `levelAllows && listCanCreate !== false`.
   - Standalone (ProjectPage) keeps `levelAllows ?? listCanCreate === true`.
   - Why: the POML asks that the level and the list agree when both exist. The brief binds ProjectPage to its current
     behaviour and tests. Agreement is applied only where the brief allows it.
   - The gap this leaves on ProjectPage is out-of-scope finding 1.
2. **Level presence replaces the root literal.** `accessLevel` is passed only where `/me` has a level (a project).
   `decidingLevelOf` returns `undefined` for every other root and for every `writeAccessPerRecord` caller.
3. **Two gates, deliberately.** Each list reports `visible = right === true` and `right`. The template independently
   shows a create action only when `right === true`. Each gate is pinned on its own (see "Mutation checks").
4. **New message follows the thread on every root, with no `/me` level.** The thread's `canCreate` is the message POST's
   own decision (task 002, `CanPostAsync`, every root), and `/me` carries no posting level. On a View Only project:
   - if the thread says the caller may post, New message is offered;
   - Upload / Add to-do / Add event are hidden by the level;
   - the header still says "View Only".
   This is pinned by a test.
5. **"Your access"**:
   - project, caller without `writeAccessPerRecord`, recognised `/me` label → that label ("View Only" / "Collaborate" /
     "Full Access");
   - otherwise, any list `true` → "Can contribute";
   - every list in the toolbar answered and all `false` → "Read only";
   - otherwise (`/me` unknown, any list unanswered) → no fact at all.
   `recordLevelOf` is strict: a missing project entry or unknown label is not shown. Gating uses `decidingLevelOf`,
   which stays fail-safe (View Only).
6. **Read-only line wording.**
   - A `writeAccessPerRecord` caller gets lines that name no grant level, e.g. "You can view these documents, but not
     upload to this record."
   - Others get "… Uploading needs Collaborate access."
   - Tasks & events has three variants: both read-only, to-dos only, events only.
   - A section whose list has not answered says nothing.
7. **The Create Event "type" select stays unsent** (D-6, #2703). No field the server does not accept is sent.

## Toggle finding (brief step 3)

No server answer exists for "may this caller PATCH this matter/WA to-do":
- `ExternalTodoDto` has no per-row right.
- The to-do list returns only `canCreate`, the create right, which is not the update right.
- `PATCH /todos/{id}` exists for every root but is not advertised.

So the status toggle stays hidden on matters and work assignments, for every caller, and no BFF code was added.

**Corrected in fix round 1.** The original text here claimed that "on a project the `/me` level answers it". It does
not. `PATCH /todos/{id}` also checks `WorkforceRoleCeilingGate.UpdateTodo`
(`src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforceRoleCeilingGate.cs` ~:133), which refuses a
workforce contact whose role lacks the privilege, and refuses any to-do whose root is ambiguous. A Collaborate level
says nothing about either. So **inside the record template the toggle is hidden on every root, projects included**.
The standalone ProjectPage keeps its pre-task rule unchanged: the level, or a per-record caller where the PATCH decides
(out-of-scope finding 2).

Tests:
- `RecordTemplateAccess`, "a matter to-do offers no status toggle even to a Create holder";
- "in the record template a project to-do offers no status toggle either, not even to a Collaborate or Full Access
  caller" (must-not-fire);
- its must-fire twin, "outside the template (the project page) the toggle is unchanged: offered to a Collaborate caller,
  not to View Only";
- `PerRecordWriteAccess` (matter, per-record caller, no toggle).

## Visibility = contract (task 002)

The SPA's visibility table equals the server's 201/403 table, because the SPA shows exactly the list's `canCreate` and
task 002 proves that `canCreate` equals the POST's outcome.

| Server fact (task 002 test) | Server outcome | SPA (task 006 test) |
|---|---|---|
| `ExternalRootEventCreateContractTests.List_ReportsCanCreate_AndTheCreateRouteAgrees`: matters/WA × events/todos, Collaborate → `canCreate: true` | POST 201 | `RecordTemplateAccess` "Create holder … are offered" (matter, WA); "creates go to the record route" (POST URL asserted) |
| same, ViewOnly → `canCreate: false` | POST 403 `insufficient_rights` (`CreateEvent_ViewOnlyOnTheRoot_Is403InsufficientRights_AndWritesNothing`) | "View-only … only Refresh and Copy link; nothing disabled" |
| `List_ForAWorkforceContact_CanCreate_FollowsTheCreateRoutesCeiling`: one child's ceiling removed → that list `false`, the other `true` | that POST 403 ceiling (`CreateEvent_WorkforceContactWhoseRoleLacksThePrivilege_Is403Ceiling_AndWritesNothing`), the other 201 | "each list decides its own action: to-dos yes, events no" and its mirror |
| `ExternalTodoScopeTests.Fr20_TodoList_CanCreate_IsTheCreateRoutesDataverseAnswer`, `Fr20_RootEventList_CanCreate_IsTheCreateRoutesDataverseAnswer`, `Fr20_TodoList_DataverseGivesNoAnswer_OverTheWire_ReportsCanCreateFalse`, `Fr20_TodoList_AProbeThatFaults_ReportsCanCreateFalse` | Dataverse-decided caller: list = POST answer; no answer → `false` | `writeAccessPerRecord` cases (all of the matter/WA matrix use it) |
| `ExternalRecordDocumentRoutesTests.List_ReportsCanCreate_FromTheSameRightsTheUploadRouteChecks`, `Fr20_List_CanCreate_IsTheUploadRoutesDataverseAnswer`, `List_ForAWorkforceContact_CanCreate_FollowsTheUploadRoutesCeiling` | upload 201/403 | Upload offered / hidden in the matrix |
| `ExternalMessageEndpointsTests.Thread_ForAPartner_ReportsCanCreate_FromTheRightsThePostRouteChecks`, `Thread_ForAWorkforceContact_CanCreate_FollowsThePostRoutesCeiling`, `Thread_ForADataverseDecidedCaller_CanCreate_IsThePostRoutesDataverseAnswer`. These cover the thread's `canCreate` ONLY; none of them POSTs (corrected in fix round 1). | message POST 201/403, proven in `ExternalMessageSendTests`: `AccessibleRecord_ContactPost_Is201_OneRowRegardingTheRecord_AndTheThreadReadShowsItWithTheSender`, `ViewOnlyProjectCaller_Is403InsufficientRights_AndCreatesNoRow`, `ReadOnlyMatterOrWorkAssignmentCaller_Is403InsufficientRights_AndCreatesNoRow`, `InvoiceUnderAReadOnlyMatter_Is403InsufficientRights_AndCreatesNoRow`, `Fr20_AppendToInDataverse_Is201`, `Fr20_ARealDenial_OverTheWire_StaysA403_AndCreatesNoRow`, `Requester_PostsOnTheirOwnServiceRequest_Is201_RegardingTheRequest`. No test there pins the workforce role-ceiling 403 on the message POST by name (gap, see fix round 1). | New message offered / hidden in the matrix; "New message follows the thread on every root" |
| list failed / still loading / `canCreate` absent | (no answer) | "fail closed" block: hidden, no line, no header |

The one place the SPA may still offer an action the server refuses is ProjectPage (finding 1). The template applies
agreement there.

## Mutation checks

Each mutation was applied to the source, the tests were run, and the original file was copied back. No `git stash` was
used.

| # | Mutation | Result |
|---|---|---|
| M1 | SmartTodo template gate → `levelAllows ?? listCanCreate !== false` | first pass: survived (masked by the template gate); after: 1 failed |
| M2 | `createRightOf`, no level: silent → `true` | 2 failed |
| M3 | RecordMessages `offered = thread.status === 'ready'` (ignores `canCreate`) | first pass: survived (masked); after: 1 failed |
| M4 | toggle without the level condition | 2 failed (incl. `PerRecordWriteAccess`) |
| M5 | "Read only" on any `false` instead of all | 1 failed |
| M6 | project: level ignores the list (`return levelAllows`) | 1 failed |
| M7 | read-only wording ignores `writeAccessPerRecord` | 7 failed |
| M8 | template `isCreateActionOffered` ignores `right` | first pass: survived (masked by the list gates); after: 3 failed |
| M9 | EventsCalendar template gate optimistic | first pass: survived (masked); after: 1 failed |
| M10 | DocumentLibrary offer uses `showUpload` / `right: true` | 11 failed |

The four first-pass survivors were real coverage gaps. Each was masked by the other, redundant gate. The added
"each gate holds on its own" block pins:
- each list's offer at the `onCreateActionChange` seam (`canCreate` true / false / omitted → visible true / false / false,
  right true / false / null);
- the template's check, using a probe section that offers `visible: true` with right true / false / null / undefined.

## Test results

All commands were run in `src/client/external-spa`.
- `npm run typecheck`: clean.
- `npm run lint`: clean.
- `npx prettier --check src tests`: clean for every file this task touched. `tests/OutsideCounselDashboard.projectName.test.tsx`
  was already unformatted and is not changed by this task.
- `npx vitest run` (full suite): 51 files, 1408 passed and 1 failed of 1409. The failure was
  `DocumentUploadPage.externalUpload` "401 (after the one fresh-token retry)…". Re-run alone it passes 25/25. It is
  timing under full-suite load, and that page does not use any code this task changed.
- The affected suites passed together, 361/361:
  - `PerRecordWriteAccess`, `DetailEventsTodosRoots`, `RecordTemplateChildDialogs`, `RecordTemplateSections`,
    `RecordTemplateModal`, `RecordMessages`;
  - `DetailDocumentsDelete`, `DocumentLibrary.*`, `SmartTodo.dueDate`, `TasksEventsSection.busyHold`;
  - `ProjectPage.contactGrant` and `ProjectPage.perRecordWrite`, both unmodified.
- New `RecordTemplateAccess.test.tsx`: 41/41.
- Fail-before evidence comes from the per-gate mutation table above. Each gate was reverted on its own, and its tests
  failed.
- The whole new file was not run against the pre-task tree. Its must-fire cases depend on code that did not exist then:
  `recordAccess.ts`, the `right` field and the `record-access-note-*` ids. Pre-task, `TasksEventsSection` forced
  ViewOnly, so on a matter or WA Add to-do / Add event could not be offered at all.
- BFF suites were not run, as the brief asked (the main session runs them).

## Out-of-scope findings (not fixed; each with a failure scenario)

1. **ProjectPage ignores a list's `canCreate: false` for a level-decided caller.** Its standalone rule is
   `levelAllows ?? …`, which the brief keeps unchanged.
   - Scenario: a workforce contact without `writeAccessPerRecord` holds Collaborate on a project, but their role ceiling
     lacks Create on `sprk_event`.
   - The event list answers `canCreate: false`, yet ProjectPage shows "Create Event", and the POST answers 403
     (ceiling).
   - The record template does not have this gap (agreement rule).
   - Fix: apply `createRightOf` in standalone mode too. It changes ProjectPage tests, so it needs an owner decision.
2. **The project to-do toggle for a `writeAccessPerRecord` caller can 403.** This is pre-existing and accepted in fix
   round 2; nothing advertises the PATCH right.
   - Scenario: a licensed systemuser with read-only Dataverse rights on a project to-do clicks the toggle.
   - The PATCH answers 403, and the toggle reverts with "You do not have permission to change this task."
   - Fix: a per-row (or per-list) PATCH answer from the BFF.
3. **The grant wording can mislead a ceiling-denied contact.** Scenario: a workforce contact without
   `writeAccessPerRecord`, at Collaborate, has their upload ceiling-denied. They read "Uploading needs Collaborate
   access." while already holding Collaborate. The text cannot tell a level denial from a ceiling denial, because the
   list says only `false`. Fix: the BFF could say why (a reason code), or the wording could be neutral for every caller.
4. **`restoreFocusTo` (task 004) can focus a control that is about to be removed.**
   - `hide()` defers the focus move with `setTimeout(0)`.
   - When the offer withdrawal re-renders the toolbar in a separate, later React task, the opener is still connected
     when the timeout fires. It gets focus, then it is removed, and focus falls to the page body.
   - Seen in the rewritten F2 test when the re-read resolved outside `act`.
   - Scenario: a re-read withdraws New message while its dialog is open. Today no user path re-reads the thread while a
     modal child is open: the toolbar is behind the modal, and send closes first. So this is latent.
   - Fix: check after the toolbar commit, e.g. re-check `isConnected` in a second frame, or restore to the parent
     dialog when the opener's action is no longer offered.
5. **Stale doc in `RecordTemplateModal.tsx`.** The header comment still says Submit invoice "stays hidden until task 006
   wires it". Task 006's brief does not cover Submit invoice: it remains hidden (no handler, no host). The comment
   should name the task that owns it.
6. **A project header can say "Collaborate" while every list says no.** This follows the POML: the header shows the
   project level. Scenario: a ceiling-denied Collaborate contact sees "Your access: Collaborate" with no create actions
   (finding 1's caller, in the template). Not wrong, since the level is what was granted, but possibly confusing.

## Fix round 1

This round was done by an implementer sub-agent. Nothing was committed, pushed or stashed, and no BFF code was
changed. Each item below gives the change and its proof.

**Supersedes:** decision 6 (the read-only wording), the "Toggle finding" claim about projects (corrected in place
above), and out-of-scope findings 3 (now fixed by B) and 4 (now fixed by F).

| Item | Change | Tests (new or changed) |
|---|---|---|
| A | `SmartTodo`: `allowToggle = !inRecordTemplate && …`. In the template the toggle is hidden on every root, because `PATCH /todos/{id}` also checks `WorkforceRoleCeilingGate.UpdateTodo` (ceiling, ambiguous root) and nothing answers that. The standalone rule is unchanged. | "in the record template a project to-do offers no status toggle either…" (Collaborate / Full Access / View Only), plus the must-fire twin "outside the template … offered to a Collaborate caller, not to View Only". |
| B | `RECORD_ACCESS_TEXT.perRecordLines` is renamed to `neutralLines`. `readOnlyNoteOf` uses `grantLines` only when the strict `/me` level (`recordLevelOf`) is `ViewOnly`; every other case gets `neutralLines`. | The matter/WA partner test now expects the neutral lines. "Collaborate, but the list says no" asserts the neutral events line. The invoice thread line is asserted neutral. |
| C | New `recordAccessFactsOf` gives `{callerContext, level, levelGuessed}`. `levelGuessed` = a level-decided project caller whose `/me` has no entry, or an unrecognised label. In that case `yourAccessOf` returns null and `readOnlyNoteOf` drops the level-gated rights (upload, addTodo, addEvent). Gating is unchanged: still View Only, so hidden. | `it.each`: "has no entry" and "has an unrecognised level". Asserts: toolbar = Refresh, New message, Copy link; no "Your access"; no documents/tasksEvents line. |
| D | New `RecordSectionRightId 'filesUpload'`. `DocumentLibrary` takes `onUploadRightChange` (the same `createRightOf` right, `null` on unmount). `FilesSection` passes `reportRight('filesUpload')` and `readOnlyNote`. The modal adds `sectionRights` into `createRightsOf(…, sectionRights)`. Nothing is added to the toolbar. New lines `files` (grant and neutral). | Invoice: Files may, thread may not → "Can contribute", and the toolbar stays Refresh + Copy link. Files may not → the Files line is shown and no Upload is rendered. Both no → "Read only" and both lines. |
| E | `RecordMessages`: a refresh failure that is not a sign-in failure sets the thread's `canCreate` to null and keeps the messages. Comments fixed at the file header (~:37-41) and the offer block (~:445-449). | `RecordTemplateAccess`: "thread canCreate: true, then Refresh answers 500 / 403: New message is withdrawn, the messages stay, nothing is said". `RecordTemplateChildDialogs`: "a thread re-read that FAILS (500) while New message is open …: focus is on the record dialog". |
| F | `restoreFocusTo` goes to the record dialog when the opener is disconnected, when it cannot take focus (`activeElement !== element` after `focus()`), or when it is removed right after taking focus. For the last case, a `MutationObserver` on the record dialog moves focus to the dialog if the opener leaves and focus fell to `body`. The observer stops when focus moves to another element, or when the opener or the dialog is gone. | The `act` workaround in the F2 test is removed. The test now asserts `document.activeElement === parent` (the record dialog). Two unit tests: "still there as the child closes but removed right after" and "cannot take focus (disabled)". |
| G | No code change. Paging forgets the old answers, because the record's loading state unmounts every section and each list reports on mount. | "from matter A (every list may) to matter B (lists pending): no create action and no "Your access" until B answers", then B answers and the actions show. |
| H | The matter/WA Create-holder case runs for `meOf()` (partner) and for `meOf({writeAccessPerRecord:true})`. The contract-table citations are corrected (see "Visibility = contract"). | Mutation MH below. |
| I | `expectNoDisabledCreateControl` matches action names by case-insensitive regex, so it catches `Upload Document`, `Create Event`, `Add Task` and `Create new task`. It scans the whole dialog for `button[disabled], [aria-disabled="true"]`, except the SprkModal "Record navigation" group: SprkModal disables ‹ › at the ends of the browse set, and that is paging, not an action on the record. | Mutation MI below. |
| J | `RecordTemplateModal.tsx` header: Submit invoice stays hidden "until task 007 (FR-10) wires it" (TASK-INDEX row 007). The stale "Task 006 changes who may be offered what" line is removed. `EventsCalendar.tsx` dialog comment now refers to `listCreateRightOf`. | — |
| K | `recordAccess.ts` gets `levelVerdictOf(accessLevel, writeAccessPerRecord)` (uses `useAccessLevel.canCreate`) and `listCreateRightOf({inRecordTemplate, loaded, listCanCreate, levelAllows}) → {right, allowed}`. SmartTodo and EventsCalendar use both. EventsCalendar's inline `canCreate(level)` is gone. `ACCESS_LEVEL_LABELS` is exported, and `RECORD_ACCESS_TEXT.level` refers to it. The four other label copies are untouched. Behaviour is unchanged. | The ProjectPage tests pass unmodified. So do `DetailEventsTodosRoots` and `PerRecordWriteAccess`. |
| L | The "Collaborate, but the list says no" test waits for the events line instead of `settle(200)`. | — |

### Fail-before evidence

The whole pre-fix `src/` was copied back over the fixed tree, the two test files were run, and the fixed `src/` was
restored. A `diff -r` against the saved snapshot came back clean.

- Result: **15 failed / 61 passed of 76**.
- What failed:
  - every A, B, C, D and E test;
  - both new focus unit tests;
  - both New message focus integration tests.
- The F2 test without `act` fails before the fix, which proves the race.
- What passed before:
  - G (a regression pin);
  - H's partner case (pinned by mutation MH instead).

### Mutation checks

Each mutation was applied with `perl`, the named tests were run, and the original file was copied back.

| # | Mutation | Result |
|---|---|---|
| MA | `allowToggle` without `!inRecordTemplate` | 1 failed (the template toggle test) |
| MB | Wording back to "grant unless `writeAccessPerRecord`" | 6 failed (matter/WA partner lines, "Collaborate but list says no", 3 invoice) |
| MC | `levelGuessed = false` | 2 failed (both C cases) |
| MD | `FilesSection` does not pass `reportRight` | 3 failed (invoice) |
| ME | Refresh failure keeps `canCreate` | 3 failed (2 access, 1 child-dialog focus) |
| MH | `decidingLevelOf` returns `mapAccessLevelString(…)` for any loaded non-per-record caller before the root check | 10 failed, including both "Create holder … a partner" cases. The per-record cases survive, which is why H was needed. |
| MI | `DocumentLibrary` draws its own "Upload Document" in the template while read-only | New tests: 7+ failed (the View-only matter/WA/project cases, C, invoice). The pre-fix test file run against the same mutation caught **none** of it: its 3 failures were only the intended A/B behaviour changes. |
| MG1 / MG2 / MG3 | Remove the content `key`; remove the offer's unmount withdrawal; ignore `null` reports in the modal | All survive, as expected. Each list reports on mount, and the loading state unmounts the sections. G stays as a regression pin and kills no single-point mutation. |

### Results (`src/client/external-spa`)

- `npm run typecheck`: clean.
- `npm run lint`: clean.
- `npx prettier --write` was run on the touched files.
- `npx vitest run` (full suite): **51 files, 1423 / 1423 passed**. There were no load-timeout failures this run.
- `RecordTemplateAccess.test.tsx`: 52 tests (41 before this round).
- `RecordTemplateChildDialogs.test.tsx`: 24 tests.

### Left for the main session

- **No message-POST ceiling test by name.** `ExternalMessageSendTests` has no test that pins the workforce role-ceiling
  403 on the message POST. Only the thread's `canCreate` ceiling test exists
  (`Thread_ForAWorkforceContact_CanCreate_FollowsThePostRoutesCeiling`). So "message `canCreate: false` (ceiling) ⇔
  POST 403" is proven on the list side only.
- **The `restoreFocusTo` observer can outlive the dialog's use.** It stays alive while the opener keeps focus, and a
  `focusout` with no `relatedTarget` (focus going to the page) does not stop it.
  - Effect: if the opener is removed later, focus moves to the record dialog. That is harmless, and it keeps Esc in the
    record.
  - It ends when focus moves to another element, or when the opener or the dialog is gone.
- **Out of scope, unchanged:**
  - ProjectPage standalone buttons ignoring `canCreate: false` (finding 1);
  - the server PATCH-right answer (finding 2);
  - the project event body's bind (K4);
  - the four other level-label copies.

## Reviews and known limits (main session)

- **Adversarial pass 1 (authorization, Opus):** F1 template toggle offered on the `/me` level (PATCH ceiling) → fixed
  (A); F4 grant wording for Collaborate callers → fixed (B); F4 invoice Files outside the access model → fixed (D);
  F2 stale message right after a failed refresh → fixed (E); F2 record-switch test → added (G); F1 pre-existing
  ProjectPage creates vs `canCreate: false` → filed **D-9 #2716** (owner decision: the POML says both "ProjectPage
  unchanged" and "must agree with the list"); K2 project missing from `/me` → fixed as C; K4 Document record shows no
  "Your access" (no list answers; matches "unknown shows nothing").
- **Adversarial pass 2 (tests/contract, Opus):** F1 read-only wording (= B); F4 guessed level (C); F1 toggle (= A);
  F4 no partner must-fire on matter/WA → fixed (H); F2 disabled-control scan → fixed (I); F2 stale comments → fixed
  (J); F2 duplicated rule → moved into `recordAccess.ts` (K); label-map copies outside the task → filed **D-11 #2718**;
  K3 fixed wait → fixed (L).
- **Scoped re-check of the fix diff (Sonnet):** no F-class findings. Known limits:
  - K1 `createRightOf` with a level and a list that omits `canCreate` offers by the level — this is the POML's kept
    exception (a project caller without `writeAccessPerRecord` uses its `/me` level where its list sends no
    `canCreate`); unreachable today because task 002 sends `canCreate` on every project list.
  - K4 `restoreFocusTo`'s "cannot take focus" branch can pull focus to the record dialog even if another element took
    it first (only if a component restores focus elsewhere within the 0 ms timeout).
  - K4 the focus observer outlives a `focusout` to the page; benign (focus goes to the record dialog, Esc stays in the
    record).
  - K4 a View Only project whose thread allows posting shows "View Only" beside an offered New message (wording only).
- **Message-POST ceiling:** no test by name in `ExternalMessageSendTests`; the route's ceiling is pinned by
  `WorkforceCeilingRouteFamilyTests` (`PostMessageOn` per message root) and the list side by
  `Thread_ForAWorkforceContact_CanCreate_FollowsThePostRoutesCeiling` (K3).
- **Filed for follow-up:** D-9 #2716 (ProjectPage, owner question), D-10 #2717 (server answer for the to-do PATCH
  right; until then the toggle is hidden in the record view on every root), D-11 #2718 (label copies).
- **Checks (main session, final source):** `npm run typecheck` clean, `npm run lint` clean, `npx vitest run`
  51 files / 1423 passed.

rework: fix-round-1 task 006
