# Task 234 notes: an open inquiry's outcome To Do closes only through the BFF, from SmartTodo, LegalWorkspace and the chat tool (#3279 DI-179, #1984; D-233)

Line numbers are approximate, at origin/master 6f4d7bc7e4 plus this change. Rigor FULL. Branch `fix/ontology-234-inquiry-todo-close-bff`.

## Decision applied

D-233 option A for the two client hosts. For the chat tool, the coordinator chose **B** in-session (2026-11-27): refuse in
`ExecuteChatAsync`, after the confirm dialog, failing closed; leave `IToolHandler`, `ToolHandlerToAIFunctionAdapter` and
`SideEffectGateAIFunction` untouched. Reason: the gate's pre-confirm validation (`ValidateChat`) is synchronous and does no I/O by
established design (`ChatPreSuspendServiceRefusalTests`: "a validation with no I/O"), and the inquiry check needs Dataverse reads as the
caller. Doing it before the dialog would be a shared hot-path contract change. **Known limit:** the person can confirm, then sees the
refusal; nothing is written. Filed as DI-181 (#3291); D-233 records it as the owner's decision.

## What changed

### Shared library (`Spaarke.SmartTodo.Components`)
- `src/services/TodoDataverseService.ts`: a third, required constructor argument `updateTodoRecord: UpdateTodoRecord` (the same injection
  pattern as `createTodoRecord`). `updateTodoStatus` (and so `dismissTodo`) writes through it, never `Xrm.WebApi.updateRecord`. The columns
  are unchanged: complete `{statecode:1, statuscode:2}`, dismiss `{statecode:1, statuscode:659490002}`, restore `{statecode:0, statuscode:1}`.
- A refusal carrying `reasonCode` `inquiry.disposition_required` (top level on the BFF's `ChildRecordWriteError`, or in the
  `problemDetails` of an `ApiError` that `@spaarke/auth`'s fetch throws) becomes error code `TODO_INQUIRY_DISPOSITION_REQUIRED` with
  `INQUIRY_TODO_CLOSE_MESSAGE` from `@spaarke/ui-components` ("Record the outcome on the inquiry first. ..."), the same constant TodoDetail
  shows (fix round 1, F2-2). Any other
  failure keeps its own message and `TODO_STATUS_UPDATE_ERROR`, as before (offline and failed calls are handled as the Xrm path handled
  them: a failed result, no throw). The reason code is imported from `@spaarke/ui-components` (`INQUIRY_DISPOSITION_REQUIRED_REASON`),
  not copied.
- Column (`sprk_todocolumn`) and pin writes are unchanged (still `Xrm.WebApi`): they never close a To Do.

### Hosts (every host that constructs the service)
| Host | How it reaches the BFF | Reaches the BFF? |
|---|---|---|
| SmartTodo code page (`src/solutions/SmartTodo`) | new `updateTodoThroughBff` in `services/childRecordWrites.ts`, beside `createTodoThroughBff`; same lazy `ensureBffBaseUrl()` / `initAuth` | Yes (creates already did) |
| LegalWorkspace (`src/solutions/LegalWorkspace`, `DataverseService extends TodoDataverseService`), also when embedded in the Console | default `updateTodoRecord` = `updateChildRecordViaBff(authenticatedFetch, getBffBaseUrl(), 'sprk_todo', ...)`, beside the create default | Yes (creates already did) |

No other code constructs `TodoDataverseService` (grep, 2026-11-27). `SmartTodoWidget` (the LegalWorkspace "To Do" section and the Console
widget) does not use the service and writes only column and pin. No host lacks the BFF, so the escalation trigger did not fire.

### Chat tool
- `DataverseUpdateRecordHandler.ExecuteChatAsync` (~:300-322): when the table is `sprk_todo` and `InquiryDispositionService.ClosesTodo(item)`,
  it reads the To Do's notes and matter as the caller, then calls `ChildRecordEndpoints.InquiryTodoCloseRefusalAsync` (the route's guard,
  reused, not copied). 409 -> tool error `inquiry.disposition_required` with the route's own detail; 503 -> `child_record.inquiry_check_unavailable`.
  Nothing written. A failed row read is the caller's own error (as every other read in the tool).
- `InquiryTodoCloseRefused` (~:820) words the guard's `ProblemHttpResult` for the model.
- No new service, interface, endpoint or DI registration.

## Which UI paths close a To Do today (for an honest #1984 close)

Checked at origin/master 6f4d7bc7e4:
1. **SmartTodo Kanban and the LegalWorkspace Smart To Do board** (`SmartToDo` from `@spaarke/smart-todo-components`): **no live complete or
   dismiss control.** `SmartToDo.handleDismiss` is defined (~:566) but never rendered or passed to a card; `updateTodoStatus(id, 'Completed')`
   has no caller. Restore (Open) is wired to the Dismissed section, but that section lists only items dismissed through `handleDismiss` in
   the same session, so it is unreachable too. This change still routes the service through the BFF, so the latent path is closed for any
   future control.
2. **LegalWorkspace "To Do" section / Console widget** (`SmartTodoWidget`): writes column and pin only; it never closes a To Do.
3. **SmartTodo code page toolbar** (`ToolbarActions.ts`): Open, Delete, Email, Pin. No complete or dismiss. (Delete: see out of scope.)
4. **TodoDetail** (`Spaarke.UI.Components`, Complete and Dismiss buttons, #1984's original path): **not mounted by any current source.** No
   solution, PCF or add-in imports it. Its former host `src/solutions/TodoDetailSidePane` was deleted in e328beafb1 (2026-06-09); the built
   web resource `sprk_tododetailsidepane` is still in the SpaarkeMaster solution, but no current code opens it. TodoDetail itself does not
   choose the write path: its host's `onSaveTodo` / `onDismissTodo` do. Through `withBffChildWrites` a `sprk_todo` update goes to the BFF
   PATCH, and task 228 made TodoDetail show the refusal. Since fix round 1 (F2-1) that refusal reaches TodoDetail with its `reasonCode`
   on a real host too: `updateChildRecordViaBff` turns the `ApiError` that `@spaarke/auth` throws into a `ChildRecordWriteError`, whose
   top-level `reasonCode` TodoDetail's own check (`TodoDetail.tsx` ~:80-90) reads. No TodoDetail change was needed.
5. **Opening a To Do** from SmartTodo or LegalWorkspace opens the model-driven `sprk_todo` form. Its **Deactivate** (and status-reason
   change on the form) is the **known limit**: no plugins (ADR-002), so it is not guarded.
6. **Chat tool** `dataverse.update_record`: refused after the confirm dialog (this change).
7. **Worklist decision "Mark complete"**: guarded by task 228 (`ChildRecordEndpoints.UpdateAsync` core).

So the only live client path that can complete an open inquiry's To Do without its disposition, apart from the chat tool (now refused)
and the decision action (task 228), is the model-driven form (known limit).

## Fix round 1 (PR #3293 review at e03af25a53)

- **F2-1.** On a real host the refusal never mapped to `TODO_INQUIRY_DISPOSITION_REQUIRED`: `@spaarke/auth`'s `authenticatedFetch` THROWS
  an `ApiError` on a 409 (`Spaarke.Auth/src/authenticatedFetch.ts` ~:79, code in `problemDetails.reasonCode`), so `updateChildRecordViaBff`
  never reached its `!response.ok` branch and the service saw no top-level `reasonCode`. Fix: `updateChildRecordViaBff`
  (`bffChildWriteAdapter.ts` ~:306-321) catches `isApiError(err)` and throws `new ChildRecordWriteError(err.message, err.status,
  <problemDetails reasonCode>)`; anything else (AuthError, network) is rethrown unchanged. `TodoDataverseService.reasonCodeOf` also reads
  `problemDetails.reasonCode`, so either shape is recognised. The refusal tests now throw an `ApiError`-shaped double (name `ApiError`,
  status 409, `problemDetails.reasonCode`).
- **F2-2.** One message: `INQUIRY_TODO_CLOSE_MESSAGE` moved from `TodoDetail.tsx` to `TodoDetail/types.ts` beside
  `INQUIRY_DISPOSITION_REQUIRED_REASON`, with D-233's wording ("Record the outcome on the inquiry first. ..."), still exported from the
  package root. TodoDetail and the Smart To Do service import it; `INQUIRY_TODO_CLOSE_REFUSED_MESSAGE` is removed.
- **K4.** `InquiryDispositionService.ClosesTodo` compares `property.Name.Trim()`, as `DataverseWriteItemMapper.MapCoreAsync` trims
  column names. Before, `{" statecode": 1, " statuscode": 2}` was written as a close but not recognised as one, on the chat tool and the
  route.
- **K3.** `useSmartTodoBridge.test.tsx`'s refusal test passed on master (it mocked the service), so it is deleted. The bridge test that
  remains checks the service is built with the BFF status-update helper.

## Known limits

1. **The chat refusal comes after the confirm dialog** (DI-181, #3291; owner decision D-233). See "Decision applied".
2. **`DataverseUpdateRecordHandler.InquiryTodoCloseRefused`** (~:825-836) reads the guard's result by casting it to `ProblemHttpResult`.
   If the route's guard ever returns another `IResult` type, the tool still refuses (it falls back to `inquiry.disposition_required` and
   the route's detail text), but a 503 lookup fault would then be reported as the 409 code. `ChatTool_ALookupFault_FailsClosed_...` pins
   today's mapping, so such a change fails that test.
3. **The model-driven `sprk_todo` form's Deactivate** is not guarded (no plugins, ADR-002).

## Tests (fail before, pass after)

- `Spaarke.SmartTodo.Components/__tests__/todoDataLayer.test.tsx`: 7 failed on the old service, all pass after. Refusal for dismiss and
  complete; must-not-fire for dismiss, complete, restore of an ordinary To Do (same columns, through the BFF, `Xrm.WebApi` never called);
  another failure keeps its message.
- `SmartTodo/src/services/__tests__/childRecordWrites.test.ts` (new) and `useSmartTodoBridge.test.tsx`: 3 failed before, pass after.
- Fix round 1, each run against e03af25a53's production code first:
  - `Spaarke.UI.Components` `bffChildWriteAdapter.test.ts`: a thrown `ApiError` 409 becomes a `ChildRecordWriteError` with its
    `reasonCode`: 1 failed / 48 passed before; passes after. Plus a must-not-fire: an `AuthError` passes through unchanged.
  - `todoDataLayer.test.tsx`: refusal for dismiss and complete, each with an `ApiError`-shaped and a `ChildRecordWriteError`-shaped
    failure: 2 failed (the `ApiError` cases) / 37 passed before; all pass after.
  - `childRecordWrites.test.ts`: the code page's update, through the real adapter, rejects a thrown `ApiError` 409 as a
    `ChildRecordWriteError` with its `reasonCode`: failed before (received name `ApiError`, no `reasonCode`); passes after.
  - `SecureChildOwnershipAiToolTests.InquiryTodoCloseChat.cs` `APaddedStatusKey_StillClosesTheToDo_AndIsRefused_...` (2 cases):
    2 failed before ("Expected chat.Success to be false, but found True"); pass after.
- `tests/integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.InquiryTodoCloseChat.cs` (new): 5 failed against
  master's handler, all 10 pass after. Refusal (hint, matter fallback, dismiss/close variants), fail-closed lookup fault, must-not-fire
  (no inquiry, closed inquiry, non-inquiry service request, non-closing update), and a pin that `ValidateChat` stays I/O-free (the known limit).
- LegalWorkspace has no test runner (see `FeedTodoSyncContext.test.tsx` header); its path is the inherited service (covered above) plus the
  default-argument wiring, checked by its type-check.

## Gates run

After fix round 1:
- `Spaarke.UI.Components` jest, `bffChildWriteAdapter.test.ts` and `TodoDetail`: 2 suites, 73 passed. `tsc` build clean.
- `Spaarke.SmartTodo.Components` jest: 13 suites, 174 passed. `tsc --noEmit` clean.
- SmartTodo code page jest: 9 suites, 94 passed (one fewer: K3). tsc: 21 errors, none in a changed file; they are the existing
  `jestSetup.test.ts` one and `Spaarke.UI.Components` source compiled without its ambient types.
- LegalWorkspace `npm run typecheck` (tsc-surface-gate): src clean, no new baselined errors.
- BFF `--filter "FullyQualifiedName~SecureChildOwnershipAiToolTests|FullyQualifiedName~DataverseUpdateRecordHandler|FullyQualifiedName~ChildRecord|FullyQualifiedName~InquiryDisposition"`:
  1370 passed, 0 failed, 0 skipped.
- `tests/Spaarke.ArchTests`: 2860 passed, 0 failed.

Before fix round 1 (e03af25a53): BFF `--filter "FullyQualifiedName~Ai|FullyQualifiedName~Signals|FullyQualifiedName~ChildRecord"` 8009
passed, 0 failed, 12 skipped; ArchTests 2860 passed.
- client-lint: `legal-workspace-app` and `ui-components` ok (see out of scope item 4).
- Publish size, against a fresh origin/master publish (same short paths, Python zipfile for both): +1,760 bytes uncompressed and
  +765 bytes zipped (about 42.66 MB total). 194 files in both. No package added.

## Manual dev check (main session, with the owner's go; no ids in public)

Prerequisites: this PR's BFF (the chat refusal) and the SmartTodo / LegalWorkspace bundles on dev. The BFF PATCH guard itself is
already on dev (task 228). Then:
1. Pick (or create through the existing inquiry flow) an OPEN outbound budget inquiry whose "Record the outcome" To Do is open.
2. Create an ordinary To Do with no inquiry (Smart To Do quick add).
3. Client path. The boards offer no status control a person can click (see the path list above), so check the exact request the service
   now sends, as the signed-in user, against dev's BFF (for example from the browser console of the SmartTodo page, which has
   `@spaarke/auth` initialised after a quick-add): `PATCH /api/v1/child-records/sprk_todo/{ordinary id}` with
   `{"statecode":1,"statuscode":659490002}` -> 204; read back: statecode 1, statuscode Dismissed. Then the same body for the inquiry's To Do
   -> 409 with `reasonCode` `inquiry.disposition_required`; read back: unchanged (Open).
4. Chat: ask the Assistant to mark the inquiry's To Do complete. Expected: after you confirm, the tool reports it was NOT written,
   "record the outcome on the inquiry first ..."; read the row back: still statecode 0, statuscode Open.
5. Chat must-not-fire: ask it to complete the ordinary To Do. Expected: written; read back statecode 1, statuscode 2.
6. Read the inquiry back: still open, disposition empty.

## Out of scope, found (not fixed here; D-106)

1. SmartTodo code-page toolbar **Delete** (`ToolbarActions.ts` ~:259) deletes `sprk_todo` rows through `Xrm.WebApi.deleteRecord`. Deleting an
   open inquiry's outcome To Do also leaves the inquiry with no prompt, and no check stops it.
2. **External access** `ExternalDataService.UpdateTodoAsync` (~:4112) completes a To Do app-only for an external user (status Completed
   moves the state). It does not run the inquiry check. Whether an external user can ever reach an inquiry's outcome To Do was not
   verified here.
3. `SmartToDo.handleDismiss` is dead code (never rendered), so the Dismissed section and Restore are unreachable; a failed dismiss or
   restore would be rolled back silently, with no message shown (the board would need a message surface if a control is added).
4. `client-lint check` reports FAILED for packages whose own dependencies are not installed in this worktree (ai-widgets etc.; type-aware
   rules read missing types as `any`/always-falsy). The covered packages this change touches pass (`legal-workspace-app`, `ui-components`);
   `Spaarke.SmartTodo.Components` and the SmartTodo code page are not covered by client-lint.
