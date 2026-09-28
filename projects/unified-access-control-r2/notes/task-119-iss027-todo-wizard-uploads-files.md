# Task 119 — ISS-027: Create To Do wizard uploads and links attached files

GitHub #1007. Owner decision 2026-09-18: "Fix; To Do needs files."

## Step 0 — premise re-verification (against code at head, 2026-09-21)

- `TodoWizardDialog.tsx:178` still captions the files step "Upload documents to associate
  with this to do, or click Next to skip." and (pre-fix) `onFinish` (:236-305) never read
  `context.uploadedFiles` — confirmed by reading the file before editing.
- `CreateRecordWizard.tsx` still only owns the file-state reducer and hands the array to
  `onFinish` via `context.uploadedFiles` (types.ts:219) — it imports no upload service. **Not
  modified by this task.**
- `Models.cs` `DocumentLinkFields.All` (line 214) confirms `sprk_relatedtodo` /
  `sprk_RelatedToDo` / `sprk_todo` is a real, registered link field — in the `sprk_related*`
  family, not a bare `sprk_todo` column (which does not exist). `TodoLookup` (line 392-402)
  corroborates: "This column ALWAYS existed" — three prior records wrongly concluded a
  document was unmappable to a to-do by checking for the bare column.
- `sprk_relatedtodo` has zero TypeScript call sites anywhere in `src/` prior to this task
  (confirmed by grep) — the nav-prop is resolved at runtime via `discoverNavProps('sprk_document')`
  with a literal fallback `'sprk_RelatedToDo'`, not copied from an existing string.

**Nav-prop resolution**: `todoService.ts` calls `discoverNavProps('sprk_document')`, maps it
via `toNavPropMap`, and reads `map['sprk_relatedtodo']`, falling back to the literal
`'sprk_RelatedToDo'` when discovery returns no entry for that column (mirrors
`matterService.ts:371-372`'s pattern for `sprk_matter`). Both the discovery path and the
fallback path are covered by dedicated tests
(`withFiles_createsOneDocumentPerUploadedFile_...` and
`withFiles_fallsBackToLiteralNavProp_...`).

## Step 1 — where the sequence lives

**Decision: inside `TodoService.createTodo`**, not `TodoWizardDialog.onFinish` — mirrors
`MatterService.createMatter`, which keeps the wizard thin (the wizard only threads
`context.uploadedFiles` through and merges `result.warnings` into the panel). `TodoService`
already owns record creation; adding the upload+link sequence to it (rather than a new
service) keeps `EntityCreationService` as the only place that owns the upload/link primitives
themselves, per CLAUDE.md §11 (no new service/wrapper).

`TodoService` builds its own `EntityCreationService` lazily (`_getEntityCreationService()`),
gated on `_authenticatedFetch`/`_bffBaseUrl` being present — both are optional constructor
params (one existing call site, `AddTodoFollowOnStep.tsx`'s `createTodoRegardingChild`,
constructs `TodoService` with neither and never attaches files). When files are supplied to a
`TodoService` instance built without those deps, the code pushes a warning
("upload is not configured for this to do creation path") instead of throwing or silently
dropping the files — covered by
`uploadNotConfigured_warnsAndSkips_ratherThanThrowing_whenFilesSuppliedWithoutDeps`.

## Step 2 — implementation

`todoService.ts`: the create-record `try/catch` was narrowed to cover only
`this._dataService.createRecord('sprk_todo', entity)`. The upload → discover nav-prop →
`createDocumentRecords` sequence runs **after** that try/catch, deliberately outside it —
the to do already exists by that point, so an unexpected upload/link exception must never be
mis-reported as "failed to create to do". This matches
`matterService.ts:349-418`'s structure exactly (its upload block is likewise outside the
`matterId`-creation try/catch).

- Zero files (`uploadedFiles.length === 0`, the default): the whole block is skipped — no
  `uploadFilesToSpe` call, no `createDocumentRecords` call, no warnings pushed. Byte-identical
  to pre-task behaviour.
- Upload call: `entityService.uploadFilesToSpe('sprk_todo', todoId, uploadedFiles)` —
  create-first, using the real `todoId` (never before creation, never empty).
- Full upload failure (`!uploadResult.success`): pushes
  `"File upload failed (N of M). Files can be added from the to do record."` — no throw, todo
  survives. This is also the path a live 409 "no container configured" takes (per
  `EntityCreationService.uploadFilesToSpe`'s own contract, verified in
  `Spaarke.SdapClient`'s `uploadFailureShapes.test.ts`: every non-2xx from the upload route,
  a 409 included, is caught inside `_uploadEach` and turned into a per-file error, never a
  thrown exception that reaches `TodoService`).
- Partial/full success: nav-prop discovery + `createDocumentRecords('sprk_todos', todoId,
  docTodoNavProp, uploadResult.uploadedFiles, { parentRecordName })`; its own `warnings` are
  appended. A partial upload failure (some files succeed, some fail) additionally appends a
  `"N file(s) failed to upload: <names>"` warning, matching `matterService.ts`'s convention.

`TodoWizardDialog.tsx`: `onFinish` now passes `context.uploadedFiles` as the third argument to
`createTodo`, and seeds the panel's `warnings` array from `result.warnings` instead of starting
it empty. **Pre-existing gap fixed as a necessary part of this task**: `result.warnings` (used
since task 021 for field-mapping warnings) was never read by `onFinish` before this change —
any field-mapping warning, and now any upload/link warning, would have been silently dropped
without this one-line fix. Without it, none of the upload NEGATIVE acceptance criteria could
reach the success panel.

## Step 3 — tests

New file: `src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/__tests__/todoService.upload.test.ts`
(10 tests, all against `TodoService.createTodo` directly — the layer that owns the logic).
Existing `todoService.test.ts` (16 tests) and `initialRegarding.test.tsx` (0 changes needed)
stay green unmodified.

Perturbation verified empirically (not just asserted in prose) by temporarily short-circuiting
each guarded call and re-running the suite, then reverting:
- Disabling the `uploadFilesToSpe` call (`if (false && uploadedFiles.length > 0)`) failed
  **8 of 10** new tests.
- Disabling the `createDocumentRecords` branch (`else if (false && uploadResult.uploadedFiles.length > 0)`)
  failed **4 of 10** new tests.
Both reverted immediately after confirming the failure; final state is the real implementation.

**Scope note** (per the "Test scope" acceptance criterion): tests stop at `TodoService`'s
boundary with `EntityCreationService`/`SdapApiClient` — they inject a fake `authenticatedFetch`
(plain Response-like objects: `{ ok, status, statusText, json }`, not real `Response`
instances, because this package's jsdom test environment does not provide a global `Response`)
and assert on `dataService.createRecord` call payloads. The HTTP-status→typed-error translation
(409 → `UploadNameConflictError`, etc.) is already covered at the `SdapApiClient` layer by
`Spaarke.SdapClient/src/__tests__/uploadFailureShapes.test.ts`; re-testing that translation here
would duplicate coverage rather than add it, since `TodoService`'s warning path is agnostic to
*which* typed error `_uploadEach` catches.

## Step 4 — verification

- `npx tsc --noEmit` (package-scoped): clean for all task-119 files. 8 pre-existing errors in
  `AccessGrantModal.tsx` belong to a concurrently-running sibling agent's in-progress edit in
  the same shared worktree (confirmed via `git status`/`git diff --stat` — that file is not
  touched by this task).
- `npx jest src/components/CreateTodoWizard`: **26/26 passed** (16 existing + 10 new), 3 test
  suites.
- `npx eslint src/components/CreateTodoWizard/todoService.ts
  src/components/CreateTodoWizard/TodoWizardDialog.tsx
  src/components/CreateTodoWizard/__tests__/todoService.upload.test.ts`: clean (no errors; one
  benign Node module-type warning unrelated to these files).
- **Did not run** `npm run build` / `npm run clean` in this package — forbidden per dispatch
  instructions (shared `dist/`, concurrent agents in the same package). The main session runs
  one build centrally after the wave.

## Escalation triggers — neither fired

1. `sprk_relatedtodo` writable from this path: confirmed via `Models.cs` (not a live-metadata
   check, since this is a client-side task with no Dataverse connection available in this
   environment, but the server-side model is the source of truth the task's own background
   cites). Not escalated.
2. Container-not-resolvable (409): this is not a "cannot fix here" condition requiring
   escalation — the code already surfaces it as a warning via the generic upload-failure path
   (verified by `noContainerResolvable_surfacesAsWarning_notAnUnhandledRejection_todoSurvives`).
   Per the escalation trigger's own text ("Ship the warning path, report the finding") — this
   note is that report; no further escalation needed.

## Shared-shell verdict

**The shared wizard shell (`CreateRecordWizard.tsx`) was NOT modified and did not need to be.**
It already exposes `context.uploadedFiles` to every `onFinish` implementation (line 219 /
`CreateRecordWizard.tsx:612`) — the gap was entirely in `TodoWizardDialog.onFinish` not reading
that field and `TodoService` not acting on it, both inside this task's declared lane. This task
remains `parallel-safe: true`.
