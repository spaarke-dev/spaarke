# Task 003 — LegalWorkspace SmartToDo → thin shim (FR-01)

> Status: **COMPLETE** · FULL rigor / sonnet / high · 2026-08-15
> Branch `work/smart-todo-r5`. NOTHING committed — all changes staged/unstaged in the worktree for review.

## Outcome

`src/solutions/LegalWorkspace/src/components/SmartToDo/` no longer contains any of the 13-file rich Kanban
implementation task 002 hoisted. It now holds exactly 2 thin-shim components (`SmartToDo.tsx`,
`SmartToDoDialog.tsx`) plus a 2-export barrel (`index.ts`) — zero duplicated component logic. All LW-specific
coupling (Dataverse data hooks, mutation callbacks, FeedTodoSyncContext, Xrm.Navigation) is centralized in one
new hook, `src/solutions/LegalWorkspace/src/hooks/useSmartToDoBridge.ts`, shared by both shim components.

## Consumers found (3, not 2 — a POML drift)

The POML's `<background>` named two consumers (`workspaceConfig.tsx`, `App.tsx`). A repo grep during
investigation found a **third**: `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx`'s
`LazySmartToDoDialog` (`React.lazy(() => import("../SmartToDo/SmartToDoDialog"))`), rendered as the "Open To Do
Dialog" 90vw×90vh Fluent Dialog. This is a genuine pre-existing consumer, not new scope — it was already
importing from `components/SmartToDo/SmartToDoDialog` before this task. Handled by keeping `SmartToDoDialog.tsx`'s
public prop surface (`open`, `onClose`, `webApi`, `userId`) unchanged, so **zero edits were needed to
`WorkspaceGrid.tsx`**.

## Files changed

| File | Change |
|---|---|
| `src/solutions/LegalWorkspace/src/hooks/useSmartToDoBridge.ts` | **NEW.** The one place that fulfils the task-002 injected-props contract. |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDo.tsx` | **Rewritten** — thin shim (85 lines, was 805). Preserves the pre-hoist `ISmartToDoProps` surface (`webApi`, `userId`, `mockItems?`, `embedded?`, `onCountChange?`, `onRefetchReady?`, `onShowMore?`, `disableSidePane?`, `scope?`, `businessUnitId?`) so `workspaceConfig.tsx`'s call site needed no edits. Calls `useSmartToDoBridge` then renders `@spaarke/smart-todo-components`'s `SmartToDo`. |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/SmartToDoDialog.tsx` | **Rewritten** — thin shim (48 lines, was 155). Preserves `{open, onClose, webApi, userId}`. Calls `useSmartToDoBridge` then renders `@spaarke/smart-todo-components`'s `SmartToDoDialog` with `smartTodoProps={bridge}`. |
| `src/solutions/LegalWorkspace/src/components/SmartToDo/index.ts` | **Rewritten** — 2-export barrel (`SmartToDo`, `SmartToDoDialog` + their prop types). |
| `src/solutions/LegalWorkspace/src/App.tsx` | **Edited** — dropped a dead `useDialogForDetail` JSX prop on `<SmartToDo webApi={webApi} userId={userId} useDialogForDetail />` (line 126). This prop was never declared on `ISmartToDoProps` in either the pre-hoist or shim version — it was silently broken (confirmed: `tsc` baseline shows `TS2322`/property-does-not-exist on this exact line before this task's edit). Removing it is a pure bug fix, not a behavior change (the prop never did anything). |
| `src/solutions/LegalWorkspace/src/workspaceConfig.tsx` | **Untouched.** Its `<SmartToDo embedded webApi={p.webApi} userId={p.userId} disableSidePane onCountChange={p.onTodoCountChange} onRefetchReady={p.onTodoRefetchReady} />` call resolves through the new shim with zero changes needed. |
| `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx` | **Untouched.** Its `LazySmartToDoDialog` (`open`, `onClose`, `webApi`, `userId`) call resolves through the new shim with zero changes needed. |

## Deleted (git rm) — 10 files, confirmed via `git status`

```
D  src/solutions/LegalWorkspace/src/components/SmartToDo/AddTodoBar.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/DismissedSection.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/EffortScoreCard.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanCard.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/KanbanHeader.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/PriorityScoreCard.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/ThresholdSettings.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/TodoAISummaryDialog.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/TodoDetailPane.tsx
D  src/solutions/LegalWorkspace/src/components/SmartToDo/todoScoringTypes.ts
```

(These 10 have zero external consumers outside `components/SmartToDo/` — verified by grep before deletion. All
13 files from task-002's hoist table are now accounted for: 10 deleted outright, 3 replaced in-place with thin
shims per the POML's "if the shim itself lives in that folder, keep/replace it appropriately" allowance.)

## How the 6-part injected contract is wired (`useSmartToDoBridge.ts`)

| Contract prop | Source |
|---|---|
| `items` / `isLoading` / `error` / `onRefetch` | LW `useTodoItems({ webApi, userId, mockItems, scope, businessUnitId })` — unchanged hook, called by the shim instead of by the old rich `SmartToDo.tsx`. |
| `preferences` / `onUpdatePreferences` / `prefsLoading` | LW `useUserPreferences({ webApi, userId })`; `onUpdatePreferences` wraps `updatePreferences` (`Promise<void>` → fire-and-forget `void`, matching the pre-hoist `handleSettingsSave`). |
| `onCreateTodo` / `onDismissTodo` / `onRestoreTodo` → `Promise<ITodoMutationResult>` | Adapters around `DataverseService.createTodo` / `.dismissTodo` / `.updateTodoStatus(id, 'Open')`, converting LW's `IResult<T>` (`{success, data?, error?}`) to the package's `ITodoMutationResult` (`{success, id?, error?: {message?}}`). |
| `dataverseService: IKanbanDataverseService` | The LW `DataverseService` instance (`serviceRef.current`) passed **directly, no adapter class** — its `updateEventColumn`/`updateEventPinned`/`batchUpdateEventColumns` methods already have matching parameter shapes and `IResult<T>`-typed (structurally `{success:boolean,...}`) return types, so it satisfies the interface structurally. Verified by reading both signatures side by side before wiring (Component Justification note is in the file header). |
| `feedSync: IFeedSyncBridge` | LW `useFeedTodoSync()` → `{ notifyChange: notifyTodoChange, subscribe }`, the same bridge shape `todo.registration.ts`'s `FeedSyncBridgeHost` already uses for `SmartTodoWidget`. |
| `onOpenTodo` | Ported **verbatim** from the pre-hoist `SmartToDo.tsx#handleOpenSmartTodo` — `Xrm.Navigation.navigateTo({pageType:"webresource", webresourceName:"sprk_smarttodo", data:"eventId=<id>"}, {target:2, width/height: OOB_MODAL_SIZES.record})`. This is intentionally **different** from the Pattern-D `SmartTodoWidget` shim's `onOpenTodo` in `sections/todo.registration.ts` (which opens the OOB `sprk_todo` entity FORM via `pageType:"entityrecord"`) — the two "My To Do List" surfaces (classic 5-section dashboard + dialog vs. Pattern-D dynamic layout) have always used different open mechanisms; both preserved unchanged, no behavior drift. |

## Build / verify results

- **`npx tsc --noEmit`** (LegalWorkspace, full monorepo path-mapped check): the touched/new files
  (`hooks/useSmartToDoBridge.ts`, `components/SmartToDo/{SmartToDo,SmartToDoDialog,index}.ts(x)`, `App.tsx`,
  `workspaceConfig.tsx`, `components/Shell/WorkspaceGrid.tsx`) produce **zero errors**.
  A/B baseline comparison (git-stash the task-003 diff, re-run `tsc`, `git stash pop`): baseline had **259**
  error lines, post-change has **251** — a net **8-line reduction**, all of them the `App.tsx` `useDialogForDetail`
  TS2322 (2 lines) and 6 lines of dead-code lint noise from the deleted local files (`DismissedSection.tsx`
  unused import, `SmartToDo.tsx` unused `dismissingIds`/`handleDismiss`, `TodoAISummaryDialog.tsx` unused
  types + missing `@types/node`). **Diff contains zero additions** — nothing new introduced. The remaining
  ~251 lines of noise are pre-existing, unrelated to SmartToDo (missing `@types/node`/`@types/jest` across
  several sibling packages, `ComponentFramework` namespace gaps in `Spaarke.UI.Components`, a `dompurify` type
  mismatch, ESLint-style `noUnusedLocals` hits in unrelated files, and 2 pre-existing carried-over dead-code
  hits inside the *package's* hoisted `SmartTodo.tsx`/`TodoAISummaryDialog.tsx`/`useKanbanColumns.ts` — flagged
  by task 002's own code-review as a known suggestion, not this task's to fix).
- **`npm run build`** (`vite build`): **fails**, but on a confirmed **pre-existing, unrelated** error:
  `Rollup failed to resolve import "@tiptap/core" from ".../Spaarke.Compose.Components/src/widgets/hooks/useComposeDocumentStyles.ts"`.
  Verified via the same git-stash A/B test — identical failure on the pre-task-003 baseline (module count
  2965 vs. 2939, difference exactly accounted for by the 10 deleted files; error text and location identical).
  This is a missing `@tiptap/core` devDependency in a completely unrelated shared package
  (`Spaarke.Compose.Components`, part of the Compose feature), unrelated to SmartToDo/LegalWorkspace and out of
  this task's scope to fix.
- **Reach-in grep** `../../../../Spaarke.UI.Components/src/`: zero matches in `Spaarke.SmartTodo.Components` or
  `LegalWorkspace` (the actual scope of tasks 001–003). Two **pre-existing, out-of-scope** matches remain
  repo-wide in unrelated packages (`Spaarke.Events.Components/.../CalendarWorkspaceWidget.tsx`,
  `Spaarke.AI.Widgets/.../StructuredOutputStreamWidget.integration.dispatchSummarizeOnly.test.tsx`) — neither
  touches SmartToDo/LegalWorkspace and neither was in scope for task 001's PR #508 absorption. Noted here rather
  than silently claimed clean.
- **Reach-in grep** `Spaarke.SmartTodo.Components/src/` from outside the package: zero real reach-ins (only
  `vite.config.ts` HMR glob patterns — sanctioned infra — and doc-comment provenance references).
- **Dangling-import grep** for the 10 deleted local paths: zero matches anywhere in `src/` (only doc-comment
  provenance references inside the package's own hoisted files, e.g. "hoisted from
  `src/solutions/LegalWorkspace/.../TodoDetailPane.tsx`").
- **`@spaarke/smart-todo-components` usage confirmed**: `useSmartToDoBridge.ts` and both shim components import
  `SmartToDo`, `SmartToDoDialog`, and the `IFeedSyncBridge`/`ISmartToDoProps`/`ITodoMutationResult` types from
  the package specifier only.

## Quality gates (Step 9.5)

- **code-review**: PASS — 0 critical, 0 warnings. `useSmartToDoBridge.ts` fulfils the full 6-part injected
  contract with no dropped persistence/navigation; `IResult<T>`→`ITodoMutationResult` adapters correct;
  `DataverseService`→`IKanbanDataverseService` structural pass-through verified correct (no wrapper class
  needed — sound Component Justification); shim files contain zero duplicated implementation; the `App.tsx`
  dead-prop removal confirmed safe (zero other references to `useDialogForDetail` anywhere in the repo).
  Minor pre-existing pattern carried over verbatim from the original (constructing a throwaway
  `DataverseService` on every render inside `useRef`'s initializer before React discards it on re-render) —
  not a new smell, matches the pre-hoist code exactly.
- **adr-check**: CLEAN — ADR-012 ✅ (shim consumes `@spaarke/smart-todo-components` via package specifier only,
  zero reach-in; injected-props pattern matches the established `SmartTodoWidget`/`FeedSyncBridgeHost`
  precedent); ADR-021 ✅ (shim files contain no styling/JSX color literals — trivially compliant); scoring
  formula (`todoScoring.ts` / `todoScoreUtils.ts` / `dueLabelUtils.ts`) confirmed **untouched** (zero git status
  changes) — no drift.

## Deviations / escalations

None required — the escalation trigger (a needed prop/behavior missing from the hoisted package) never fired;
task 002's contract was complete. One drift from the POML's stated scope was found and resolved without
escalation (a third consumer, `WorkspaceGrid.tsx`'s `LazySmartToDoDialog`, not named in the POML background) —
resolved by preserving the pre-hoist prop surface so it required zero code changes, and documented above.

## PR #508 status

Per CLAUDE.md "Decisions Made" 2026-08-15 and the POML notes: PR #508 is ready-to-close as superseded, but
formal closure happens at project wrap-up (task 090), not here.
