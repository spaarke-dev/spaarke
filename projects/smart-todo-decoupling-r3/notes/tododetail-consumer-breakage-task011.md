# TodoDetail Consumer Breakage — task 011 follow-up

> **Source task**: `011-simplify-tododetail-single-entity.poml` (Phase 2 hoist)
> **Date**: 2026-06-07
> **Author**: smart-todo-decoupling-r3 / task-execute
> **Purpose**: Inventory the consumer-side type errors introduced by simplifying
> `TodoDetail` to a single-entity (`sprk_todo`) load/save shape, so the downstream
> repoint tasks (020 / 022) can pick them up cleanly.

## What changed in the shared lib (task 011)

`@spaarke/ui-components` (`TodoDetail` component) was simplified from a two-entity
(`sprk_event` + `sprk_eventtodo`) shape to a single-entity (`sprk_todo`) shape per
FR-09 + OS-1. Per the task instructions, consumer projects were **NOT** auto-fixed;
those fixes belong to their own Phase 3+ tasks.

### Breaking changes to `ITodoDetailProps`

| Removed | Replacement |
|---|---|
| `todoExtension: ITodoExtension \| null` (prop) | (gone — single record now covers all fields) |
| `onSaveEventFields(eventId, IEventFieldUpdates)` | `onSaveTodo(todoId, ITodoFieldUpdates)` |
| `onSaveTodoExtFields(todoId, ITodoExtensionUpdates)` | merged into `onSaveTodo` |
| `onDeactivateTodoExt(todoId)` | merged into `onSaveTodo` (set `statecode=1`, `statuscode=2`) |
| `onRemoveTodo(eventId)` (sprk_event.sprk_todoflag=false) | `onDismissTodo(todoId)` (sets `statuscode=659490002` Dismissed — semantics chosen by host: deactivate-via-statuscode OR delete) |

### Breaking changes to exported types

| Removed | Replacement |
|---|---|
| `ITodoExtension` | gone |
| `IEventFieldUpdates` | `ITodoFieldUpdates` |
| `ITodoExtensionUpdates` | gone (merged into `ITodoFieldUpdates`) |
| `TODO_EXTENSION_SELECT` | gone |
| `ITodoRecord` (old `sprk_event` shape) | `ITodoRecord` (new `sprk_todo` shape — sprk_todoid PK, sprk_name primary name, sprk_notes native, 11 sprk_regarding lookups, 4 resolvers, 5 sync state fields) |

### What stayed

- `TODO_DETAIL_SELECT` (same name; now lists `sprk_todo` fields)
- `IContactOption` (id/name; reusable for `systemuser` picker — note that
  `sprk_assignedto` is now a `systemuser` lookup, not contact)
- `onSearchContacts`, `onOpenRegardingRecord`, `onClose`, `isLoading`, `error` props
- Layout, score formula, Fluent v9 styling — unchanged

## Consumer-side type errors introduced (NOT auto-fixed)

`src/solutions/SmartTodo/` (scope: Phase 3 task 020):

| File | Error | Resolution path |
|---|---|---|
| `src/components/TodoDetailPanel.tsx:28,34` | Imports `loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension`, `ITodoExtension`, `IEventFieldUpdates`, `ITodoExtensionUpdates` from `@spaarke/ui-components/TodoDetail` — these no longer exist | Repoint at single-entity API: replace with `loadTodoRecord(todoId)` (single Web API call), `onSaveTodo` callback, `onDismissTodo` for the Remove button |
| `src/components/TodoDetailPanel.tsx:187+` | Implicit-`any` on `prev` and `Property 'error' does not exist on type '{ success: boolean }'` | Cascading from the type changes above; resolves when types are updated |
| `src/services/todoDetailService.ts:20-21` | Imports same removed types | Same as above |

`src/solutions/LegalWorkspace/`:

- LegalWorkspace's `SmartToDo/TodoDetailPane.tsx` is a self-contained component
  that does **NOT** import the shared `TodoDetail` — no direct breakage from
  this task. Only the shared-grid + service hoist tasks affect it.

## Pre-existing errors (NOT introduced by task 011)

Both `src/solutions/SmartTodo/` and `src/solutions/LegalWorkspace/` already have
unrelated TypeScript errors that pre-date this task:

- `Cannot find module '@spaarke/ui-components/<subpath>'` errors for sibling
  subpath exports (`/utils`, `/PanelSplitter`, etc.) — independent shared-lib
  packaging issue, not a task-011 regression
- `Cannot find namespace 'ComponentFramework'` errors in shared-lib `hooks/` and
  `services/` — pre-existing namespace-resolution gap when type-checking
  consumers; the shared lib's own `npm run build` succeeds cleanly

## Decision: "Remove from To Do" semantics (binding for downstream)

The legacy `sprk_event.sprk_todoflag = false` "Remove from To Do" path is removed
per OS-1. The R3 first-class `sprk_todo` model has TWO valid semantic equivalents:

1. **Dismiss** (chosen as default in TodoDetail): `statuscode = 659490002` (Dismissed),
   `statecode = 1` (Inactive). The record is preserved (visible in a DismissedSection;
   restorable). This matches the design.md §5.3 SmartTodoApp tree which shows a
   `DismissedSection` after the Kanban board.

2. **Delete**: hard delete the `sprk_todo` row.

The shared `TodoDetail` component exposes a single `onDismissTodo` callback prop;
the host implementation chooses the semantic. The button label is "Dismiss" (not
"Remove") to match the new model. Hosts that want hard delete can implement
`onDismissTodo` as `webApi.deleteRecord("sprk_todo", id)`. Hosts that want a
statuscode flip can implement it as `webApi.updateRecord("sprk_todo", id, { statecode: 1, statuscode: 659490002 })`.

The recommended default for SmartTodo + LegalWorkspace is **Dismiss-via-statuscode**
(option 1) so users can recover dismissed to-dos from the DismissedSection.

## Quality gates (task 011)

- ✅ Shared-lib `npm run build` clean (tsc 5.3.3, zero errors)
- ✅ `npx jest src/components/TodoDetail` → 12/12 pass
- ✅ Full shared-lib jest run → 1189/1192 pass (3 failures pre-existing in
  `XrmDataverseClient.test.ts` + `RichFilePreview.test.tsx`, verified by
  `git stash` baseline)
- ✅ ESLint scoped to `TodoDetail/` → 0 errors, 0 warnings
- ✅ `grep loadTodoExtension|saveTodoExtensionFields|deactivateTodoExtension|sprk_eventtodoid` in shared lib → 0 hits
- ✅ Package version bumped: `@spaarke/ui-components` 2.1.0 → 2.1.1 (PATCH)
- ✅ Added: `ResizeObserver` polyfill in `jest.setup.js` (jsdom gap that affected
  Fluent v9 `MessageBar` — benefits any future test using MessageBar/Drawer/etc.)
