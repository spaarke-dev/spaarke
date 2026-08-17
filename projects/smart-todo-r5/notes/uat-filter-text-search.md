# UAT decision — replace structured Filter pane with expanding text search

**Date**: 2026-08-17
**Trigger**: Operator UAT decision (2026-08-17), delivered as a direct implementation
brief (not a `task-XXX.poml`).
**Scope**: `src/solutions/SmartTodo/**` only.

## Decision

REPLACE the SmartTodo Code Page's structured Filter pane (task 021, FR-06 / F-3 —
Priority / Status / Due-date / Assigned-To categories, built via a Fluent
`Accordion` + `TagFilter` + Xrm.WebApi contact typeahead) with a single expanding
free-text search. Clicking the Header's "Filter" pill (task 020, unchanged —
`isFilterPaneOpen` / `onToggleFilterPane`) now expands a `SearchBox` instead of the
structured pane. Typing filters the Kanban client-side by case-insensitive substring
match against the To Do's **name**, **description**, and **assigned-to** display
name (plus the pre-existing regarding-record name/number match from DEF-11 Part 3,
preserved unchanged).

The operator explicitly chose "replace" over "add search alongside the structured
pane" or "keep Completed toggle, drop the rest."

## What changed

| Area | Before (task 021) | After (this change) |
|---|---|---|
| UI component | `components/FilterPane/` (Accordion: Priority/Status/Due/Assigned-To) | `components/SearchFilter/` (single `SearchBox`) |
| Predicate location | Server-side (`buildTodoItemsQuery` filter clauses) | Client-side (`SmartToDo.tsx` `displayItems` memo, via `utils/todoSearchUtils.ts::matchesTodoSearchQuery`) |
| Match fields | Priority (choice), Status (multi-select), Due-date (bucket), Assigned-To (contact typeahead) | name, description, regarding-record name/number, assigned-to display name (free text) |
| State shape | `ITodoFilterState` (4 fields) lifted to `SmartTodoApp.tsx` | `searchQuery: string` lifted to `SmartTodoApp.tsx` (was already declared as a dead `const ""` since task 020 removed its old producer) |
| Toggle wiring | `isFilterPaneOpen` / `onToggleFilterPane` (task 020, Header) | UNCHANGED — same props, same Header, same pill |

## FR-07 regression (accepted, not a bug)

Removing the structured Status filter also removes the **"Show Completed" toggle**
(FR-07 / task 022), which lived exclusively in the Filter pane's Status checkboxes
(`Completed` option). There is no other UI surface offering this toggle in the
SmartTodo Code Page after this change — completed to-dos are simply not shown in
the Kanban (matches the pre-task-021 default: `statuscode` in {Open, In Progress}).

The operator was told this explicitly and chose "replace" anyway. This is flagged
here for operator sign-off, not fixed or worked around. `buildTodoItemsQuery`'s
`includeCompleted` parameter is left in place (unused by any live caller, same as
before task 021's `filterState` was added on top of it) — a future task can wire a
new producer for it without any query-layer change.

## Assigned-to search — gap check (closed, no query change needed)

The task brief asked me to verify whether the assigned-to display name was already
fetched for the Kanban card data, since the search needs to match it. It was:
`DataverseService.mapTodoFormattedValues` (in `services/DataverseService.ts`) already
maps `_sprk_assignedto_value@OData.Community.Display.V1.FormattedValue` onto
`ITodo.assignedToName` for every `getActiveTodos` call — this predates this task
(added for the KanbanCard's assignee display) and required no `$select`/`$expand`
change. The only change needed was extending the client-side match predicate to
also test `assignedToName`.

While verifying this, found and fixed a **pre-existing type gap**: `ITodo` (in
`types/entities.ts`) was missing `sprk_regardingrecordname` / `sprk_regardingrecordnumber`
even though `SmartToDo.tsx`'s search predicate already read them off `ITodo` items at
runtime (DEF-11 Part 3, 2026-07-04) — a latent `tsc` error (`TS2339`) that happened to
not block anything because nobody had run a clean `tsc --noEmit` on just this
package since. Added both fields to `ITodo` (they ARE always selected via
`queryHelpers.ts TODO_SELECT_FIELDS`). This fixed 2 pre-existing errors and avoided
introducing 2 new ones in the extracted `todoSearchUtils.ts`.

## Files changed

- **Removed**: `components/FilterPane/` (all 4 files — component, styles, barrel, test)
- **Added**: `components/SearchFilter/` (component, styles, barrel, test)
- **Added**: `utils/todoSearchUtils.ts` (+ test) — extracted the search predicate out
  of `SmartToDo.tsx` into a pure, directly-unit-testable function, since
  `SmartToDo.tsx` itself has no existing render-test harness to exercise the
  predicate through.
- **Modified**: `SmartTodoApp.tsx` — `searchQuery` is now real `useState` (was a
  dead `const ""`); `filterState`/`ITodoFilterState`/`DEFAULT_TODO_FILTER` removed;
  `<FilterPane>` → `<SearchFilter>`.
- **Modified**: `components/SmartToDo.tsx` — dropped the `filter` prop; extended
  `displayItems`'s search predicate via `matchesTodoSearchQuery`.
- **Modified**: `hooks/useTodoItems.ts` — dropped the `filter` option and its
  primitive-key destructuring/`filterRef` plumbing.
- **Modified**: `services/DataverseService.ts` — `getActiveTodos` dropped its
  `filter` parameter.
- **Modified**: `services/queryHelpers.ts` — removed `ITodoFilterState`,
  `DEFAULT_TODO_FILTER`, `TodoStatusFilterValue`, `TODO_STATUS_FILTER_STATUSCODE`,
  `TodoDueDateCategory`, `TODO_PRIORITY_CHOICE_VALUES`, `buildDueDateRangeClause`;
  `buildTodoItemsQuery` reverted to its pre-task-021 3-parameter shape
  (`contactId`, `regardingFilter?`, `includeCompleted?`).
- **Modified**: `types/entities.ts` — added the two `ITodo` fields (see above).
- **Replaced**: `services/__tests__/queryHelpers.test.ts` — the task-021 version
  tested ONLY the removed `filterState` branch; rewritten to cover
  `buildTodoItemsQuery`'s reverted default/regardingFilter/includeCompleted shape.
- **Untouched** (verified, out of scope): `components/Header/Header.tsx` (the Filter
  pill + `isFilterPaneOpen`/`onToggleFilterPane` contract is unchanged — this task
  only swapped what's mounted underneath it) and the LegalWorkspace solution's
  parallel `DataverseService.ts`/`useTodoItems.ts` (separate duplicate files, not
  imported from SmartTodo — confirmed via repo-wide grep before editing).

## Verification

- `npx tsc --noEmit` (in `src/solutions/SmartTodo`): 40 errors on baseline (git
  stash) → 38 after this change. Zero NEW errors; 2 pre-existing errors fixed
  (the `ITodo` regarding-record type gap above). All remaining 38 are pre-existing
  and unrelated (cross-package `@azure/msal-browser` / `ComponentFramework` /
  `DOMPurify` type-resolution gaps in `Spaarke.Auth`/`Spaarke.UI.Components`, plus
  one pre-existing `IWebApi` structural mismatch at `SmartToDo.tsx:424`).
- `npx jest` (in `src/solutions/SmartTodo`): 9 suites / 114 tests, all passing.
- hex/rgb/`'1px'` grep across every changed file: zero matches introduced by this
  diff (one pre-existing `shorthands.borderWidth("1px")` remains in `SmartToDo.tsx`
  at an untouched line, unrelated to this change).
