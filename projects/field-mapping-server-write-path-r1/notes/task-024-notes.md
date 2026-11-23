# Task 024: Create To Do wizard on the server write path (FR-16) — code part

> Rigor FULL. Branch `task/fm-024`, from `2400607d98`. **Code part only.** Not done here (main session, owner OK needed): the deploy of `sprk_createtodowizard` (and the SpaarkeAi bundle for the in-app host) and the live checks. Nothing in this branch has been run against dev, Dataverse or Azure.

## What changed

| File (under `src/client/shared/Spaarke.UI.Components/src/components/`) | Change |
|---|---|
| `CreateTodoWizard/todoService.ts` | Deleted the `applyFieldMappings` import, call and its comment block. The create now goes through `createRecordViaBffWithWarnings(authenticatedFetch, bffBaseUrl, 'sprk_todo', entity, { mappingSource })` (the shape 023 used), not `withBffChildWrites(...).createRecord`. `mappingSource` = the regarding parent (`entityType`, `recordId`), only when the to do has one. Server warnings are pushed into `warnings`. A service built without `authenticatedFetch` / `bffBaseUrl` returns a failed result (fails closed, as before). The not-found wording is the root-create sentence when there is no regarding parent. |
| `CreateTodoWizard/TodoWizardDialog.tsx` | Calls `broadcastTodoCreated(todoId)` after a successful create (see "Broadcast" below); comment only otherwise. |
| `CreateTodoWizard/todoWizardHostSupport.ts` | New `broadcastTodoCreated(todoId)` (the post, extracted from `withTodoCreatedBroadcast`, which now calls it). `withTodoCreatedBroadcast` behaves as before. |
| `CreateTodoWizard/__tests__/todoService.fieldMapping.test.ts` | New (below). |
| `CreateTodoWizard/__tests__/TodoWizardDialog.create.test.tsx` | New: the dialog's real `onFinish` posts to the route, never `dataService.createRecord`, and broadcasts. |
| `CreateTodoWizard/__tests__/todoService.test.ts` | One assertion updated: the route path now carries the mapping-source query when a parent is given. |
| `CreateTodoWizard/__tests__/todoWizardHostSupport.test.ts` | Two tests for `broadcastTodoCreated`. |
| `WizardFollowOns/__tests__/AddTodoFollowOnStep.test.tsx` | **Outside the wizard folder** (deviation 1): one assertion updated. |

Hosts: **no change** to `src/solutions/CreateTodoWizard/src/main.tsx` or `InAppWizardHost.tsx`. Both pass `authenticatedFetch` and `bffBaseUrl` through to `TodoWizardDialog` (main.tsx:146-148; InAppWizardHost `sprk_createtodowizard` case), which is what the route needs. `bffChildWriteAdapter.ts` is not edited.

## Key inventory (POML step 1)

Every key `createTodo` put on `entity` before this task (To Do row: `RecordTableRules.cs` ~:281; `sprk_todo` is in `OwnedChildWrite.RegardingPairTables`):

| Key | Class | Registry row (`sprk_todo`) | Now |
|---|---|---|---|
| `sprk_name` | user-entered (Title) | — | sent |
| `sprk_notes` | user-entered, only when non-blank | — | sent |
| `sprk_duedate` | user-entered, only when set | — | sent |
| `sprk_priorityscore`, `sprk_effortscore` | user-entered: the 0-100 value the user's Priority / Effort pick resolves to (`todoScoreMappings.ts`); always sent, the form defaults to Normal / 50 and shows it selected | none (not owned, not mapping-protected) | sent |
| `<assignedto navprop>@odata.bind` | user-picked (Assigned To; the host may pre-fill the current user's contact in the form, so it is the user's pick when submitted) | `ForPerson` + `ServerDefaults`: server writers and the chat caller only; the browser route adds no default | sent when picked; blank writes nothing |
| `sprk_Regarding<Parent>@odata.bind` + `sprk_RegardingRecordType@odata.bind` + `sprk_regardingrecordid` / `name` / `number` / `url` (`applyResolverFields`) | user-picked filing keys (ADR-024) | `RegardingPairTables` includes `sprk_todo`; the 045f record-URL rule applies | sent (see "Regarding pair") |
| `statecode` = 0, `statuscode` = 1 | **client default, kept** (initial state) | **not in the registry**; no server rule sets or protects them | sent. Reason: the Open / Active initial state is what the Kanban and every To Do reader filter on, nothing on the server owns it, and I cannot read dev metadata to prove the platform default is Open (the status values were customized, `Customize-SprkTodoStatuscode.ps1`). They are not mapped columns, so keeping them cannot interfere with the server mapping. The old wizard sent them on this same route in production. Dropping them is a one-line change if the owner wants the platform default instead. |
| every mapped field (`applyFieldMappings`) | **client default** | server mapping step (FR-02, fill-only) | **dropped** (server) |
| owner / owning team / impersonation | never sent | `ResolveOwnerAsync` (FR-09) | not sent (asserted) |
| container / search-index keys | never sent (the To Do row has neither `SearchIndexed` nor a container) | — | not sent (asserted) |

**Result:** the only client defaults were the mapping (dropped) and the initial state (kept, reason above). The To Do row has no required column and no autonumber, and the wizard never defaulted the assignee on the client, so removing the client mapping leaves nothing empty that a To Do rule needs. No BFF edit is needed or made.

## Regarding pair

Same finding as task 023 (`ChildRecordEndpoints.CreateAsync` → `RecordCreationService.CreateAsync`): the route keeps a wizard-shaped pair as sent (type and id together), name and number are stored as sent, and the 045f rule needs a sent `sprk_regardingrecordurl` to name the pair's record. The wizard's `buildRecordUrl` does (`etn=` and `id=`; asserted). Decision: keep sending the pair.

## mappingSource answer

`ChildRecordEndpoints.InferMappingSourceTables` holds only `sprk_matter` and `sprk_project`, so for `sprk_todo` a create with no `mappingSource` asks for **no mapping at all**. Dropping `applyFieldMappings` without naming a source would silently stop mapping to dos. The to do names its source explicitly: the regarding parent (`{entityType, recordId}`), in the query string only (`?mappingSourceEntity=&mappingSourceId=`, lower-case, braces stripped). No parent: no query string, no mapping. This is the same record the old client mapping read (`catalogEntry.entityType` / `regarding.recordId`). Adding `sprk_todo` to the inference set would be a BFF edit and would also change every other browser create of a to do that names no source; not done.

Every caller of `TodoService.createTodo` files the to do under the record it wants mapped from, so the explicit source is right for all of them:

| Caller | Regarding | mappingSource sent |
|---|---|---|
| `TodoWizardDialog.tsx` ~:266 (code page `sprk_createtodowizard`; in-app `InAppWizardHost`) | `context.association` | the association; none when the step is skipped |
| `createTodoRegardingChild` (`AddTodoFollowOnStep.tsx` :137) from `CreateReportCardWizard.tsx` ~:527 and `CreateAnalysisWizardWidget.tsx` ~:919 | the record the wizard just created (report card, analysis, invoice, event ...) | that record |

## Warnings

`withBffChildWrites(...).createRecord` dropped the route's `warnings` (it returns the id only); the service now reads them with `createRecordViaBffWithWarnings` and pushes them into `result.warnings`, which `TodoWizardDialog` already seeds into the success dialog (`'To Do created with warnings'`). No adapter edit.

## Broadcast (a regression this migration would otherwise cause)

Both hosts wrap the data service in `withTodoCreatedBroadcast`, which posts `sprk_todo:created` on `sprk_todo:lifecycle` when `dataService.createRecord('sprk_todo', ...)` succeeds; the LegalWorkspace To Do widget refetches on it (R4 task 100, W-2). The service now posts to the route itself (it has to name the mapping source, which `IDataService.createRecord(entity, data)` cannot carry), so the wrapper would never see the create and the widget would stop refreshing. Fix without touching the adapter: `broadcastTodoCreated` (extracted, same message) is called by `TodoWizardDialog` after a successful create. It is the dialog, not the service, so the follow-on to dos (`createTodoRegardingChild`) do not start broadcasting; they did not before either. The hosts keep wrapping the data service (inert for the wizard's own to-do create; any other path that creates a `sprk_todo` through it still broadcasts, and the two cannot both fire for one create). Follow-up, not done: drop the wrapper from the two hosts.

## SmartTodo quick-add finding (POML escalation trigger)

**Not mapped on the client.** Nothing under `Spaarke.SmartTodo.Components/src` or `src/solutions/SmartTodo/src` imports `applyFieldMappings` or `FieldMappingService` or reads `/field-mappings/` (grep: no match).
- Quick-add and the three-field quick-add create through the BFF with a title / due date / assignee payload and no parent: `src/solutions/SmartTodo/src/components/SmartToDo.tsx:735-768` (`createTodoThroughBff(payload)`), `src/solutions/SmartTodo/src/services/childRecordWrites.ts:41`, `DataverseService.ts:188`. `AddTodoBar.tsx` (`Spaarke.SmartTodo.Components/.../SmartToDo/AddTodoBar.tsx:85-126`) only calls `onAdd(title)`.
- The "New Task" launcher opens the OOB main form pre-seeded with regarding and the current user's contact: `src/solutions/SmartTodo/src/services/newTaskLauncher.ts:276-290`; its own comment (:104-107) says it is "NOT the Field Mapping Framework". Rows made on that form are created by the form's own Dataverse save, not by this wizard path, so they get no mapping at all (neither client nor server). That is how it was before this task; nothing to widen here.
- Not investigated: whether the OOB-form create path should get server mapping; flagged for the owner.

## Tests

- **RED**, the HEAD `todoService.ts` and `TodoWizardDialog.tsx` put back with the new / converted tests in place (CreateTodoWizard folder + `AddTodoFollowOnStep`, 10 suites): **11 failed, 51 passed** (the failing suites: `todoService.fieldMapping`, `todoService`, `TodoWizardDialog.create`, `AddTodoFollowOnStep`). The files restored afterwards are the ones committed.
- **GREEN:** CreateTodoWizard folder + WizardFollowOns (16 suites): **119 passed.** Full `Spaarke.UI.Components` jest run: **444 of 446 suites pass, 7050 of 7053 tests**; the 2 failing suites (`OntologyAdmin/Rules/Rules.test.tsx`, `ConversationWorkspace/ConversationWorkspace.messages.test.tsx`) pass when run alone (48 tests) and import nothing this task touched: load-related timeouts in the full parallel run.

`todoService.fieldMapping.test.ts` (15 tests): (1) the body holds exactly the user's keys, the initial state and the six regarding keys; (2) a minimal to do sends name, two scores and initial state only (exact object); (3) no owner / owning / impersonation / container / search-index key; (4) `dataService.createRecord` is never called, the POST goes to `/api/v1/child-records/sprk_todo` with no query without a parent; (5) an empty base URL posts to the relative route; (6) the parent is the explicit source in the query string (lower-case, braces stripped) and no body key mentions a mapping source; (7) skipped / null regarding: no query string and no regarding key; (8) `applyFieldMappings` (mocked module) never called, no `/field-mappings/` request; (9) a source scan: `todoService.ts` has neither `applyFieldMappings`, `FieldMappingService` nor `withBffChildWrites`; (10) route warnings reach the result and the to do stays created; (11) a refusal is a hard error; (12, 13) the not-found wording with and without a parent; (14) a service with no BFF connection creates nothing; (15) a BFF-routed data service without a fetch of its own creates nothing.

### Deviations

1. **One test outside the wizard folder changed:** `WizardFollowOns/__tests__/AddTodoFollowOnStep.test.tsx` asserted the follow-on to do's route as exactly `/api/v1/child-records/sprk_todo`. The follow-on is filed under the record the wizard just created, so the route now carries `?mappingSourceEntity=sprk_invoice&mappingSourceId=abc-123-def`. That assertion pins the old behaviour (no source); updated, nothing else in the file changed. No other suite outside the wizard needed a change (full run above).
2. **`mappingSource` is sent explicitly** (the POML's step 3 says "only when the filed-under parent differs from the intended source"): the server does not infer for `sprk_todo`, so the filed-under parent is always named when there is one; it is the same record, never a different one.
3. **`statecode` / `statuscode` kept** (inventory above); the POML says drop client defaults the server owns, and the server owns neither.
4. **`broadcastTodoCreated` added** to `todoWizardHostSupport.ts` and called from the dialog (above): needed to keep the To Do widget refetch.
5. `eslint-suppressions.json` unchanged: `client-lint check --package ui-components` passes with no new violation and no unpruned suppression.

## Verification

- `npx tsc --noEmit -p .` in `Spaarke.UI.Components`: 0 errors. (`Spaarke.Auth` and `Spaarke.SdapClient` built first, fresh worktree.)
- `node scripts/quality/client-lint/client-lint.mjs check --package ui-components`: ok.
- `npm run build` in `src/solutions/CreateTodoWizard` (vite): built, `dist/index.html` 1,513.33 kB (420 kB gzip). `dist/` is gitignored; **not deployed**. No `package-lock.json` change to revert.
- `grep -n applyFieldMappings` on `todoService.ts`: none.

## Left for the live part (main session, needs owner OK)

1. **Deploy** `sprk_createtodowizard` via `code-page-deploy`; note the previous bundle for rollback. The route already supports `sprk_todo`. Mapped fields appear on first load only if the BFF build on dev carries the project's mapping step (tasks 013-020).
2. **Live checks:** a to do created under a parent with an active `{parent → sprk_todo}` profile, opened at once (mapped fields present); a user value kept over a mapped one (fill-only, e.g. Notes or Due Date if a rule maps it); owner = the parent's owning team (FR-09); a to do with no parent (no mapping, no warning); the Assigned To pick; files attached (upload and link); the LegalWorkspace To Do widget refetches after a create; dark mode.
3. **Other bundles change only when rebuilt and deployed** (the shared library is compiled into each host): the Spaarke Console's in-app To Do wizard (`InAppWizardHost`, a SpaarkeAi rebuild), and the Report Card and Analysis wizards' follow-on to dos (`sprk_createreportcardwizard`, the analysis widget's host). Until then they keep the old client mapping.

## Follow-ups (not done; not in scope)

- Stale comments in other files that still say `TodoService.createTodo` calls `applyFieldMappings`: `Spaarke.AI.Widgets/src/widgets/workspace/CreateAnalysisWizardWidget.tsx` (:37-38 and the "Field-Mapping-driven internally" comment ~:915) and `CreateReportCardWizard.tsx`. Other wizards' files.
- `withTodoCreatedBroadcast` is inert for the wizard's own create; remove it from `main.tsx` and `InAppWizardHost.tsx` once nothing else creates a `sprk_todo` through the host data service.
- `main.tsx` passes `resolvedBffBaseUrl` as `''` when auth init failed; the wizard now posts to the relative route and the fetch fails visibly, where before `withBffChildWrites(..., '' || undefined)` refused with "not connected". Both create nothing; only the message differs.
