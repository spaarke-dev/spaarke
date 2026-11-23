# Task 009 — section actions, To Do removed, Documents columns (D-12)

> Status: implemented, not committed (the owner commits after review). SPA only (`src/client/external-spa`); no BFF, no
> shared-library change.

## What changed

### 1. Create actions sit in their own section's header

- The config now has `sectionActions` next to `toolbar` (`recordTemplate.types.ts`). The record header keeps Refresh,
  Copy link, and the type's own actions (Download, Delete). Submit invoice stays in the header for now.
  - Project, matter and work assignment: `toolbar: ['refresh','submitInvoice','copyLink']`;
    `sectionActions: { documents: ['upload'], messages: ['newMessage'], tasksEvents: ['addEvent'] }`.
  - Invoice: `toolbar: ['refresh','copyLink']`; `sectionActions: { messages: ['newMessage'] }`.
  - Service request: `toolbar: ['refresh','copyLink','delete']`; `sectionActions: { documents: ['upload'], messages: ['newMessage'] }`.
  - Document: unchanged.
  - `RecordToolbarActionId` is a positive allow-list, so a config cannot name a retired id.
- `RecordTemplateModal` builds both places from one `actionOf(id)` (same text, icon and visibility rule).
  - `headerActionsOf(section)` maps the offered actions to `IHeaderToolbarSlot[]` and filters to `visible === true`, so
    an action is hidden, never disabled.
  - Each section gets the result as `RecordSectionContext.headerActions`.
  - The section passes it to `<RecordSectionCard actions={headerActions}>`, which renders the shared `HeaderToolbar`
    icon slots. No buttons are hand-built.
- The list→template seam is unchanged: `CreateActionReporter`, `useOfferCreateAction`, `offerAction(id)` and the
  per-action hosts.
  - Hosts now mount for every listed id: `recordActionIdsOf(config)` gives toolbar plus section ids.
  - The rights, "Your access" and the read-only lines work exactly as before.
- **Task 010** moves Submit invoice to Invoices with a config change only. It removes `submitInvoice` from `toolbar` and
  adds `sectionActions.invoices: ['submitInvoice']`. `InvoicesSection` already passes `headerActions`.
- Focus and Esc are unchanged. `useChildDialog` remembers the invoking control (now the section button) and gives focus
  back to it, or to the record dialog if it has gone. The ChildDialogs tests prove this for Upload and New message.

### 2. To Do removed from the SPA

- Deleted:
  - `src/components/SmartTodo.tsx` and its exports in `components/index.ts`.
  - The to-do read/write/delete functions, `ODataTodo` and the to-do routes in the header comment of `web-api-client.ts`.
  - `addTodo` from the action text, access helpers and templates.
  - The to-do mocks.
  - The `SmartTodo` on `ProjectPage`'s Calendar tab.
- Renamed: `EventsTodosRoot` → `EventsRoot` and `EVENTS_TODOS_ROOT_NOUN` → `EVENTS_ROOT_NOUN`.
- Tasks & events (`TasksEventsSection`) lists events only and passes `onBusyChange` straight to `EventsCalendar`.
- The read-only line for that section is now the events line ("You can view these events, but not add one." / "…
  Adding one needs Collaborate access.").
- Comments that mentioned to-dos were reworded (`delete-own.ts`, `DeleteOwn.tsx`, `EventsCalendar`, `DocumentLibrary`,
  `createAction`, `OutsideCounselDashboard`).
- **Enforced by** `tests/NoTodoSurface.test.ts`, a source scan of `src/` that includes comments.
  - Patterns: `sprk_todo` (not `sprk_todoflag`), `/todos`, any `Todo` identifier, the words `todo(s)`, `to-do(s)` and
    `To Do`.
  - Must-fire controls: 8. Must-not-fire controls: 5 (the legacy event column, an upper-case TODO work note, "nothing to
    do", the section title, `todayDate`).
  - It also asserts that `SmartTodo.tsx` stays gone.

### 3. Empty states

| Where | New text |
|---|---|
| Events, in the template | "…Use Add event above to add the first event." |
| Messages, in the template | "No messages yet. Use New message above to write the first one." |
| Documents, in the template | "…Use Upload above to add your first document." |

The project page keeps its own-button texts ("Use the Create Event / Upload button"). No text says "in the toolbar" or
"Add to-do" any more. The Preview card's "Use Download in the toolbar" stays: Download is still in the record header,
and that text is not an empty state.

### 4. D-12 #2769: Documents grid columns

- `DocumentLibrary` puts a per-column class on the header cell and the body cell (`columnClasses[columnId]`):

  | Column | Sizing |
  |---|---|
  | Name | `flex 2 1 0` |
  | AI Summary | `flex 1 1 0` |
  | Type | fixed 120px |
  | Date | fixed 112px |
  | Actions | fixed 112px |

  Every cell also gets `min-width: 0` and `overflow: hidden`.
- The name text, type badge (with the full type in its `title`) and summary truncate with an ellipsis. The summary
  button's inline `maxWidth: 300px` became `maxWidth: 100%`.
- **Test:** `tests/DocumentLibrary.columns.test.tsx` checks the Griffel atomic-class contract (the same method as
  `RecordTemplateModal.test.tsx`).

## Files

Under `src/client/external-spa/`:

- **Changed (src):**
  - `src/api/{web-api-client,delete-own}.ts`
  - `src/components/{DocumentLibrary,EventsCalendar,DeleteOwn,createAction}.tsx`
  - `src/components/index.ts`
  - `src/components/detail/RecordMessages.tsx`
  - `src/components/record/{RecordTemplateModal.tsx,recordAccess.ts,recordTemplate.types.ts,recordTemplates.ts}`
  - `src/components/record/sections/{Documents,Invoices,Messages,TasksEvents}Section.tsx`
  - `src/components/record/sections/RecordSectionCard.tsx` (comment only)
  - `src/mocks/{mock-data,mock-service}.ts`
  - `src/pages/{ProjectPage,OutsideCounselDashboard}.tsx`
- **Deleted:**
  - `src/components/SmartTodo.tsx`
  - `tests/SmartTodo.dueDate.test.tsx`
  - `tests/DetailEventsTodosRoots.test.tsx` (replaced by `tests/DetailEventsRoots.test.tsx`)
- **New tests:**
  - `tests/NoTodoSurface.test.ts`
  - `tests/DocumentLibrary.columns.test.tsx`
  - `tests/DetailEventsRoots.test.tsx`
- **Changed tests:**
  - `RecordTemplateAccess`: helpers `toolbarOf` / `sectionActionsOf` / `actionsOf`; the placement assertions; the
    offering probe now draws a `RecordSectionCard`.
  - `RecordTemplateChildDialogs`: `expectActionIn` for Upload, New message (section) and Delete request (record header).
  - `RecordTemplateModal`: config shape including `sectionActions`, and no create id in any header.
  - `RecordTemplateSections`
  - `DetailDocumentsDelete`
  - `PerRecordWriteAccess`
  - `TruncatedLists`
  - `TasksEventsSection.busyHold`: rewritten for one list.
  - `ProjectPage.{contactGrant,perRecordWrite,createAgreesWithList}`

## Test results (`src/client/external-spa`)

- `npm run typecheck`: clean.
- The changed test files type-check clean under a temporary tsconfig that includes them. `tests/` is not in the
  project tsconfig.
- `npm run lint`: clean.
- `npx prettier --check src tests`: clean except `tests/OutsideCounselDashboard.projectName.test.tsx`, which already
  failed and was not touched.
- `npx vitest run`:
  - Before: 1507/1507 passing (294 files).
  - After: **1487/1487 passing** (294 files).
  - The net −20 is the to-do tests removed, partly offset by the new tests.
- Fail-before proof, each by putting the old file back, running, and restoring:
  - Old `DocumentLibrary.tsx`: the columns test has 2/2 failing.
  - `SmartTodo.tsx` restored: the source scan has 2 failing (offenders found, file present).
  - Old `recordTemplates.ts`: 7 failing (the config shape, 4 placement cases in Access, Upload and New message in
    ChildDialogs).

## Decisions

- **The seam stays.** The list offers its action and the template renders it. The template, not the section, turns
  offers into `IHeaderToolbarSlot`s, so header and section actions share one visibility rule and one host per action.
- **Task 011's hold is not built.** `TasksEventsSection` has a single list now, so the per-list busy hold is gone.
  Task 011 must restore a hold per list when it adds the Task list (noted in the busy-hold test header).
- **The ProjectPage header icons are left as they are.** Its "Tasks" header icon and the dashboard's "Tasks" icon are
  existing no-op placeholders, not to-do UI. Removing them is outside this task.

## Needs an issue (not filed)

1. **BFF to-do routes are still live** (D-13 #2800 keeps them for now). No SPA caller is left. When they are retired,
   remove the routes and their contract tests.
   - Scenario: a stale SPA bundle in a browser cache can still POST `/api/v1/external/{root}/{id}/todos`.
2. **The shared `RECORD_ACTION_ICONS` still has `addTodo`** (`Spaarke.UI.Components`, not changed here). It has no
   `addTask` yet; task 011 needs one for the Task list's create action. The SPA cannot reference `addTodo` (the
   allow-list type and the source scan both stop it).
3. **No-op header icons.** The ProjectPage header (Mail, Tasks, Notifications) and the OutsideCounselDashboard "Tasks"
   button do nothing on click.
   - Scenario: a user clicks "Tasks" on the dashboard and nothing happens.
4. **The "Can contribute" label** in the record header predates the 2026-11-13 access-level directive. It was out of
   scope here; task 011 decides it.

## Review round 1

Fixed in the working tree. Nothing was committed, and only `src/client/external-spa/**` changed.

1. **F2.** The truncated document name now carries `title={item.sprk_name}` (`DocumentLibrary.tsx`, the name `Text`).
   `DocumentLibrary.columns.test.tsx` asserts it. It fails with the title removed.
2. **F4.** A new test in `RecordTemplateAccess.test.tsx` ("empty states point at the section action") opens a matter
   whose lists all may create and are empty. The Documents, Messages and Tasks & events empty states each must contain
   "above" and must not match `/toolbar|to-?do/i`. It fails with the old Messages text ("…in the toolbar…").
   - The Preview card is left out on purpose: Download is in the record header.
3. **K1.** `NoTodoSurface.test.ts` gains the `/ToDo/` pattern and a must-fire control (`getProjectToDos(id)`).
4. **K1.** A new `RecordTemplateModal.test.tsx` test checks every template: `recordActionIdsOf` lists each id once, so no
   action appears in both the header and a section, or in two sections. It has a control that sees a planted repeat.
5. **K4.** In `ProjectPage.perRecordWrite.test.tsx`, the `findByRole` that flaked under load waits up to 15 s, and the
   test is allowed 30 s.

**Results**
- `npm run typecheck`: clean.
- `npm run lint`: clean.
- `npx prettier --check src tests`: clean except the already-failing, untouched
  `tests/OutsideCounselDashboard.projectName.test.tsx`.
- `npx vitest run`: 1489/1490 passed (295 files).
  - The one failure is in `DocumentUploadPage.externalUpload.test.tsx` ("401 … Sign in again"), a file task 009 does
    not touch. Run alone it passes 25/25, so it is a load flake.

**Issue references**
- D-15 (the BFF to-do routes) is now #2810.
- The shared icon map's `addTask` and the access labels ("Can contribute") moved into task 011. Issues 2 and 4 under
  "Needs an issue" above therefore need no separate filing.