# Task 021 — Filter pane (FR-06)

> **Date**: 2026-08-16 · Sonnet/high · FULL rigor. Completed via subagent; bookkeeping finalized by orchestrator (agent completed code + tests + gates but did not write this note / flip its own POML status — done here).

## What was built
`src/solutions/SmartTodo/src/components/FilterPane/` (new: `FilterPane.tsx` 378 LOC, `FilterPane.styles.ts`, `index.ts`, `__tests__/FilterPane.test.tsx`) — a side-pane filter with 4 categories + clear-all:
- **Priority** — reuses the existing shared `TagFilter` (no new multi-select built; §11 compliance).
- **Status** — includes the **Completed** option, threaded into `buildTodoItemsQuery`'s `includeCompleted` param (the hand-off task 022 deferred here).
- **Due** — date-range categories via new `buildDueDateRangeClause`.
- **Assigned-To** — typeahead backed by **`Xrm.WebApi`** (injected `IWebApi`/`getWebApi()`), never `fetch`/BFF (DATA-ACCESS-DECISION-CRITERIA; a test spies on `globalThis.fetch` and asserts it is never called).

Wiring: mounts against the `isFilterPaneOpen`/`onToggleFilterPane` state task 020 lifted into `SmartTodoApp.tsx` (the header Filter pill drives it). `queryHelpers.ts` extended (+~180 LOC: filter types + `buildDueDateRangeClause` + extended `buildTodoItemsQuery`); `useTodoItems.ts` stabilizes the new `filter` dependency via the established primitive-destructuring pattern.

## Documented deviations (all benign, verified)
- **`SmartToDo.tsx` touched (+2 lines)** — not in the original scope list, but necessary: `useTodoItems` is invoked inside `SmartToDo.tsx`, so the filter predicate must thread through there (mirrors the existing `searchQuery`/`orientation` lifting). No other Wave-C agent touched `SmartToDo.tsx` → no collision. (Task 022's notes had flagged this as 021's job.)
- **New test file `services/__tests__/queryHelpers.test.ts`** — added to verify the back-compat query string against the pure function (task 022 recommended it land here).

## Verification (orchestrator-confirmed)
- `solutions/SmartTodo` **jest: 114/114 pass (6 suites)** — up from 76/4 pre-task.
- Back-compat path of `buildTodoItemsQuery` byte-identical, regression-tested.
- 38 new tests. ADR-021 zero color literals (grep). ADR-028/DATA-ACCESS: typeahead via `Xrm.WebApi` (fetch-never-called test). ADR-012: `TagFilter` reused.
- code-review + adr-check: Clean, 0 Critical/blocking; Warnings were process/doc items (this note addresses them).

## Follow-ups (minor, non-blocking)
- Assigned-To results use `role=listbox`/`option` without full `combobox`+`aria-activedescendant` — keyboard-operable, exceeds the removed Header code; future a11y enhancement.
- `getActiveTodos` passes explicit `undefined` for `includeCompleted` positional — correct but a named-options refactor would read cleaner (out of scope; touches all call sites).
