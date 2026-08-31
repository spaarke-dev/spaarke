# Task 022 — Completed status: kanban render support + query-layer plumbing (FR-07 / F-2)

**Status**: complete
**Scope executed**: query-layer parameter (`queryHelpers.ts`) + kanban-render verification/documentation (`useKanbanColumns.ts`) + new test coverage (`useKanbanColumns.test.ts`).
**Deps**: 003 (complete).

## What changed

### 1. `src/solutions/SmartTodo/src/services/queryHelpers.ts` — `buildTodoItemsQuery`

Added a third, optional, backward-compatible parameter:

```ts
export function buildTodoItemsQuery(
  contactId: string,
  regardingFilter?: ITodoRegardingFilter,
  includeCompleted: boolean = false,
): string
```

- Default (`includeCompleted` omitted/false): filter is byte-identical in intent to before — `statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)`, wrapped in one extra pair of parens for safe OR-composition (does not change matched records).
- `includeCompleted = true`: filter becomes `(statecode eq 0 and (statuscode eq 1 or statuscode eq 659490001)) or statuscode eq 2` — Completed (statuscode=2) items are OR'd in. No `statecode eq 0` assertion is made for the Completed branch, mirroring the existing `buildDismissedTodoQuery` precedent (it doesn't assert statecode either) — this repo's `sprk_todo` schema was not available to inspect directly (Dataverse-hosted, no local form/entity XML), so rather than guess a statecode value for the Completed sub-status, the filter only asserts what's already proven safe (statuscode-only match for a terminal status).
- `buildDismissedTodoQuery` (the Dismissed/659490002 lane) is completely untouched — verified by reading `DismissedSection.tsx` + `DataverseService.getDismissedTodos`: Dismissed items are fetched via a wholly separate query/component, never routed through `buildTodoItemsQuery` or `useKanbanColumns`.
- **Existing caller unaffected**: `DataverseService.getActiveTodos(contactId, regardingFilter)` (line ~445) calls `buildTodoItemsQuery(contactId, regardingFilter)` with no 3rd arg — defaults to `includeCompleted=false`, so today's fetch behavior (Completed hidden) is unchanged with zero edits to `DataverseService.ts` (out of this task's scope; that file belongs to other tasks per the scope ban).

### 2. `src/client/shared/Spaarke.SmartTodo.Components/src/hooks/useKanbanColumns.ts`

**No behavioral change was needed.** Verified by reading (not assuming, per step 2 of the POML): `bucketTodoItems`/`resolveColumn`/`assignColumnByDate` bucket purely on `sprk_duedate` + pin state (`sprk_todocolumn`/`sprk_todopinned`). There is no statuscode/statecode reference anywhere in this file's bucketing logic, and `IKanbanTodoLike` (the type `bucketTodoItems` is generic over) doesn't even carry those fields — only the superset `IKanbanCardTodo` (consumed by `KanbanCard`) does. So a Completed item that the query layer starts returning flows through the exact same score/due-date bucketing as an Open/In-Progress item today, with no 4th "Completed" column — satisfying the acceptance criterion as-is.

Added a documentation block above `bucketTodoItems` recording this verification (so a future reader doesn't have to re-derive it), and the interactive/draggable decision below.

### 3. Completed-item interactivity decision

Product intent was ambiguous on whether a Completed card should remain draggable/pinnable. Decision (documented inline in `useKanbanColumns.ts`): **keep it fully interactive** — no special-case guard was added to `moveItem`/`togglePin`/`recalculate`, so a Completed card behaves identically to any other card once visible. Rationale: adding a guard would be new, unrequested behavior (the spec only asks for correct *rendering*, not a new interaction restriction), and the existing `KanbanCard.tsx` `isCompleted` styling (dimmed/strikethrough) already signals its state visually without needing to also lock it.

### 4. New test file — `src/client/shared/Spaarke.SmartTodo.Components/__tests__/useKanbanColumns.test.ts`

Follows the exact Jest-less pure-value pattern already established by the sibling `SmartTodoWidget.test.tsx` in this package (no Jest config exists yet in `@spaarke/smart-todo-components` — task 040 wires it). Exercises the exported pure function `bucketTodoItems` directly (no React renderer needed):

1. `runDefaultHiddenRegressionTest` — non-Completed items still bucket into exactly 3 columns, nothing dropped (regression baseline).
2. `runCompletedItemBucketingTest` — Completed items (overdue, far-future, undated) bucket into Today/Future correctly, mixed alongside a non-completed item in the same column, no 4th column, no item dropped. This is the core FR-07 render-side guarantee.
3. `runCompletedPinnedItemTest` — a pinned Completed item honors its pinned-column override over its date-computed column, same as any other item.
4. `runDismissedShapeUnaffectedTest` — a Dismissed-shaped item (statuscode 659490002) still buckets with zero statuscode discrimination, proving this file's bucketing logic can't be the source of any Dismissed-lane regression (it never discriminated on statuscode to begin with).

**Verified executable, not just type-checked**: compiled the test file + its two dependencies (`useKanbanColumns.ts`, `types/kanban.ts`) to CommonJS via a throwaway `tsconfig` and ran with `node` (`USE_KANBAN_COLUMNS_SMOKE=1`) — all 4 tests printed `... passed.` with zero assertion failures. Temp build artifacts were deleted afterward; nothing left in the repo.

Query-layer behavior (`buildTodoItemsQuery`'s default-hidden / include-completed filter strings) is **not** covered by a new test file in this task — the POML's `<outputs>` and this task's file-scope list only name `useKanbanColumns.test.ts`, and `queryHelpers.ts` lives in a different package (`src/solutions/SmartTodo`) than this shared-lib test file; importing across that boundary into `@spaarke/smart-todo-components/__tests__` would violate ADR-012's "no `src/solutions/…` reach-in" rule. That query-string coverage is a natural fit for whoever executes task 021 (which already owns `buildTodoItemsQuery`'s UI-wiring / call-site changes) or for task 040 when Jest is wired into `solutions/SmartTodo`'s existing Jest suite.

## Verification run

- `cd src/client/shared/Spaarke.SmartTodo.Components && npx tsc --noEmit` → **exit 0**, no errors (unchanged from baseline).
- `__tests__` directory explicitly type-checked (temp `tsconfig` extending the base with `__tests__` added to `include`) → **zero new errors**; the 4 pre-existing errors in `SmartTodoWidget.test.tsx` (unrelated `userId` vs `contactId` param drift) are untouched pre-existing debt, not introduced by this task.
- `useKanbanColumns.test.ts` executed at runtime via a throwaway CommonJS transpile → **4/4 assertions passed**.
- `cd src/solutions/SmartTodo && npx tsc --noEmit` → pre-existing baseline errors only (unrelated `@spaarke/ui-components`/`ComponentFramework`/`@azure/msal-browser` resolution issues + 2 pre-existing `SmartToDo.tsx` errors); **zero errors reference `queryHelpers.ts` or `DataverseService.ts`** — confirms the new optional parameter doesn't break the existing call site.
- `grep` for hex/rgb literals across all 3 touched files → zero matches.

## Cross-task dependency surfaced (not reached into)

The Status filter's "Completed" checkbox UI and the wiring that threads its checked-state into `getActiveTodos`/`buildTodoItemsQuery`'s new `includeCompleted` parameter belongs to **task 021** (filter pane, not run in this wave). This task deliberately stopped at the query/render layer per its explicit scope boundary — `DataverseService.ts`, `SmartToDo.tsx`, and the filter-pane component were not touched. Task 021 (or whoever wires the checkbox) should call `getActiveTodos(contactId, regardingFilter, includeCompletedFromFilterState)` — the shape is ready.

## Files touched

- `src/solutions/SmartTodo/src/services/queryHelpers.ts` (code)
- `src/client/shared/Spaarke.SmartTodo.Components/src/hooks/useKanbanColumns.ts` (doc-only addition)
- `src/client/shared/Spaarke.SmartTodo.Components/__tests__/useKanbanColumns.test.ts` (new test file)
- `projects/smart-todo-r5/tasks/022-completed-status-toggle.poml` (status → completed)
- `projects/smart-todo-r5/notes/task-022-completed-toggle.md` (this file)

Nothing else was edited. Nothing was committed (per instruction — commits are handled by the orchestrating session).
