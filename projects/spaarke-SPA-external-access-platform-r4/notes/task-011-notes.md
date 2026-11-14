# Task 011 — levels and Tasks in the SPA

> Status: implemented, not committed (the owner commits after review). SPA (`src/client/external-spa`) plus one shared-lib
> icon map. No BFF, no `.claude/`, no `InvoicesWidget.tsx`, no DataGrid.

## What changed

### 1. Labels come from `/me`; nothing is inferred

- "Your access" in the record header shows `ACCESS_LEVEL_LABELS[level]` ("View Only" / "Collaborate" / "Full Access") and
  nothing else.
  - Matter and work assignment: the `/me` `records[{recordType, recordId, accessLevel}]` entry (task 008). `recordType`
    is `matter` / `workassignment`; the id compares without case.
  - Project: the `/me` `projects` entry, as before.
  - A record `/me` has no level for (older BFF, not listed, unrecognised level, invoice, service request, document)
    shows no value. A guessed project level (the View Only fallback the actions use) is not shown either.
- Removed: the "Can contribute" and "Read only" inference (`RECORD_ACCESS_TEXT.canContribute` / `.readOnly`).
  `src/` has no "Can contribute" string (source scan).
- **uac-r2 A2 kept.** For a `writeAccessPerRecord` caller the label is the composed READ view and is display only.
  - `decidingLevelOf` still returns `undefined` for them; their write actions keep coming from each list's `canCreate`.
  - `readOnlyNoteOf` names a grant level ("needs Collaborate access") only when `levelDecides`, that is when the caller
    is not per-record. A per-record View Only caller gets the neutral line.
- **D-11 #2718.** The five duplicated label maps now read the one exported `ACCESS_LEVEL_LABELS`: `ProjectPage`,
  `ContactsOrganizations`, `OutsideCounselDashboard`, `InviteUserDialog`, `recordAccess`. A source scan keeps the
  literals "View Only" and "Full Access" in `recordAccess.ts` only.

### 2. Tasks and Events

- `TasksEventsSection` still renders ONE list (`EventsCalendar`, one read). It splits the rows into two groups:
  **Tasks** (`isTask === true`) first, then **Events** (the rest). Test ids `events-group-tasks`, `events-group-events`.
- Two section actions, in the section header: **Add task** (`addTask`) then **Add event** (`addEvent`).
  - `ROOT_SECTION_ACTIONS.tasksEvents = ['addTask','addEvent']` for project, matter and work assignment.
  - Add event: offered on `canCreate`, sends no type (unchanged).
  - Add task: offered only when the list says `canCreateTask === true` (and the event right is not false).
    `createTaskRightOf(eventRight, listCanCreateTask)`: false if the event right is false, unknown if the event right is
    unknown, otherwise the explicit boolean, and unknown when the list did not say. Unknown hides it.
  - Add task calls `createRootEvent` with `eventType: 'task'`. The new row lands under Tasks.
- The type `Select` in `EventsCalendar` (never sent) is gone. One dialog component, `kind: 'event' | 'task'`, with its
  own title, create label, required-name message and error text.
- Busy hold: each dialog reports through its own stable reporter; the list OR-combines them into the template's single
  `onBusyChange`, so one create going idle never releases the other's hold.
- Outside the template (no reporter offered) the list shows its own "Create Task" / "Create Event" buttons, as before.
- Empty states: "No tasks yet" / "No events yet" with "Use Add task above to add the first task." /
  "Use Add event above to add the first event." in the template (no toolbar, no to-do).
  A group that is empty inside a truncated list renders nothing; one notice names "tasks and events".
- Delete-own works per group ("Delete task X" / "Delete event X").
- **ProjectPage Calendar tab: split.** It renders the same `EventsCalendar`, so it gets the same two groups and its own
  Create Task / Create Event buttons with no code of its own. That was the smaller consistent change.
- Shared lib: `RECORD_ACTION_ICONS.addTodo` replaced by `addTask` (`TaskListAdd20Regular`).

### 3. Types and mocks

- `web-api-client.ts`: `ODataEvent.isTask`, `ODataCollectionResponse.canCreateTask`, `ListResult.canCreateTask`
  (`getCollection` carries it only when it is a boolean), `CreateEventPayload.eventType?: 'task'`.
- `auth/bff-client.ts`: `ExternalUserContextResponse.records`.
- Mocks: `MOCK_USER.records` (a matter Collaborate, a work assignment View Only); a task event `evt-006`; GET events
  returns `canCreate` and `canCreateTask`; POST events sets `isTask` from `eventType` (projects, matters, work assignments).

## Files

Under `src/client/external-spa/`:

- **Src:** `api/web-api-client.ts`, `auth/bff-client.ts`, `components/{EventsCalendar,ContactsOrganizations,InviteUserDialog}.tsx`,
  `components/record/{recordAccess.ts,RecordTemplateModal.tsx,recordTemplate.types.ts,recordTemplates.ts}`,
  `components/record/sections/TasksEventsSection.tsx`, `mocks/{mock-data,mock-service}.ts`,
  `pages/{ProjectPage,OutsideCounselDashboard}.tsx`.
- **New test:** `tests/RecordAccess.levels.test.ts`.
- **Changed tests:** `RecordTemplateAccess` (labels, A2, Tasks and Events, busy hold), `RecordTemplateModal`,
  `RecordTemplateSections`, `DetailEventsRoots`, `TasksEventsSection.busyHold` (rewritten for the two-reporter seam).
- **Shared lib:** `src/client/shared/Spaarke.UI.Components/src/components/RecordCommandBar/{recordActionIcons.ts,__tests__/RecordCommandBar.test.tsx}`.

The four `InvoiceSubmission/__tests__/*` files in the working tree are task 010 part 3 work, not this task.

## Test results

- `npm run typecheck`: clean. `npm run lint`: clean (0 warnings).
- `npx vitest run` (external-spa): **311 files, 1616 tests, 1616 passed, 0 failed.**
- Shared lib `RecordCommandBar` jest: 14/14.
- Fail-before (the new tests, run against the pre-change behaviour by mutating the source, then restoring from a backup):

  | Mutation | Failed |
  |---|---|
  | `recordLevelOf` returns nothing for matter/WA | the label must-fire tests, the A2 label-shown tests, the level unit tests |
  | `createTaskRightOf` true for an unknown `canCreateTask` | its truth-table rows and the "Add task hidden when unknown" tests |
  | `levelDecides` always true | the per-record neutral-line tests |
  | the `eventType: 'task'` spread removed | "Add task sends eventType task" (matter, WA, project) |
  | Add event also sends `eventType` | "Add event sends NO eventType" x3 (confirmed alone) |
  | all rows treated as events | the grouping tests |
  | `addTodo` in the shared map (old expected map vs new code) | `RecordCommandBar` icon-map test, by construction (not run) |


## Decisions

- "Read only" went with "Can contribute": it was the same inference (all lists said no), and the label now comes only
  from `/me`.
- The per-record label is display only (A2); the grant wording needs `levelDecides`.
- Add task needs an explicit `canCreateTask`. An older BFF that does not send it shows no Add task (fail closed).

## Open items

- The dialog has a name field only; the Description field is still never sent (unchanged).
- Against a BFF that predates task 008, `eventType: 'task'` would be rejected or ignored; the button is hidden there
  because `canCreateTask` is absent. Deploy order: BFF (008) before the SPA.
- `addTodo` is gone from the shared map; any other consumer of `RECORD_ACTION_ICONS.addTodo` would fail to compile
  (none in this repo).
- The external to-do BFF routes are still retained (D-13 #2800).
- The owner closes D-6 #2703 and D-11 #2718.
- Task 010 part 3 (InvoicesWidget, DataGrid) is untouched here; its uncommitted shared-lib test edits were not run
  separately.

## Review round 1

- **F1: truncated list kept the group's own create button hidden.** `EventsCalendar` no longer returns null for an empty
  group in a truncated list when the caller may create there and the list owns its buttons (ProjectPage Calendar tab).
  - The group keeps its heading and its own Create Task / Create Event button; only the empty-state text is dropped (a
    cut-short list claims nothing).
  - With no button to show (no create right, or inside the template where the section action is the entry point) the group
    still renders nothing.
  - Tests (`RecordTemplateAccess`, "a truncated list keeps each group's own create button"): empty Tasks group +
    `canCreateTask` true shows Create Task; false hides the button and the group; the same for Create Event with `canCreate`;
    0 rows + truncated (the invoice-hiding fault) keeps both buttons and shows no "No ... yet"; a caller who may not
    create sees only the notice.
- **F2: the two-dialog busy hold now has proof.** The combiner is extracted and exported as `createBusyReporters`
  (`EventsCalendar.tsx`); the component uses it. Tests drive it directly: task true, event true, event false keeps the
  last report true; task false releases. The mirror order, repeated values and a lone dialog are covered. The comment in
  `TasksEventsSection.busyHold.test.tsx` now points at them.
- **Stale comments reworded** (`EventsCalendar.tsx` accessLevel prop, `recordAccess.ts` header, `levelAllows`,
  `levelGuessed`, `recordLevelOf`, `readOnlyNoteOf`, `RecordTemplateModal.tsx`): a level DECIDES writes only for a project
  caller without `writeAccessPerRecord`; a matter or work assignment `/me` `records` level is display only; Add task
  follows `canCreateTask`.
- **Decision (main session): no "Your access" line for `writeAccessPerRecord` callers.** `yourAccessOf` returns null for
  them (the pre-011 behaviour). `recordLevelOf` still knows the level; `decidingLevelOf` still gives none; their creates
  follow `canCreate` / `canCreateTask`. Contact callers keep the label. Tests updated (project, matter and WA, both
  directions, plus a unit test that a contact with the same entries keeps "View Only").
- **Shared lib version.** Removing `addTodo` from the exported `RECORD_ACTION_ICONS` is breaking, so
  `Spaarke.UI.Components/package.json` goes 2.4.0 -> 3.0.0 (`src/client/shared/CLAUDE.md`: "bump the package version on
  breaking changes"). Every consumer depends on it as `file:../...Spaarke.UI.Components`, so none pins a range. Their lock
  files carry `"version": "2.4.0"` as link metadata (external-spa and nine PCF locks); they were not touched (the previous
  bump, 65cbf55f74, did not touch them either) and refresh on the next install. No code outside this repo's shared lib used
  `addTodo` as an icon key.
- **Fail-before:** with the truncated guard restored to the old form, the OR in the combiner dropped, and the
  `writeAccessPerRecord` rule removed, 13 of the new or changed tests fail (the combiner overlap, three truncated cases, the
  level-line tests for project, matter and WA, and the held-create tests); restored afterwards.
- **Results after round 1:** typecheck clean, lint clean; external-spa vitest 313 files, 1626 tests: 1625 passed on the
  full run, and the one failure (`DocumentUploadPage.externalUpload`, a load timeout) passes alone (25/25), so 1626/1626.
  Shared lib `RecordCommandBar` jest 14/14.
