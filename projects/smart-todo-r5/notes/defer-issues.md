# Smart To Do R5 — Deferrals & Discovered Issues

> Source-of-truth tracker (per project CLAUDE.md "Deferrals & Issues"). Each entry names a concrete failing behavior/contract, not "flexibility" (§11). **Before the wrap-up PR (task 090), each open entry needs a GitHub Issue URL** (`/project-defer-issue-tracking`); `push-to-github` blocks on entries lacking one.

| # | Source task | Concrete gap | Proposed fix | Status |
|---|---|---|---|---|
| D-1 | 012 | `ITodo` (`types/entities.ts`) lacks `sprk_priority`/`sprk_effort` fields, so the priority/effort card UI reads them via **2 `as unknown as` structural casts**. Casts silently break if the field logical names change. | Widen `ITodo` with optional `sprk_priority?: number` / `sprk_effort?: number`; delete the 2 casts in `components/KanbanCard/KanbanCard.tsx` + `components/SmartToDo/KanbanCard.tsx`. (Out of 012's `components/**`-only scope.) | OPEN — no GH issue yet |
| D-2 | 011 | `DataverseService` (Code Page) + `CreateTodoWizard` persist the **derived score** (`sprk_priorityscore`/`sprk_effortscore`) but do **not** persist the raw **choice** (`sprk_priority`/`sprk_effort`). A record created via wizard/quick-add has a score but no selectable label. | Decide product intent: persist the chosen `sprk_priority`/`sprk_effort` alongside the score on create. Likely folds into task 014 (deploy) or a wizard follow-up. | OPEN — no GH issue yet |
| D-3 | 002 | Two live `KanbanCard` implementations in `Spaarke.SmartTodo.Components` (widget flexbox card `components/KanbanCard/` + rich `components/SmartToDo/KanbanCard.tsx` aliased `SmartToDoKanbanCard`). Priority/effort UI (012) had to be added to BOTH. | §11 card unification — one card primitive both surfaces compose. Larger refactor; not in R5's "move 13 files" scope. | OPEN — no GH issue yet |
| D-4 | 002/003 | Hoisted package files carry pre-existing dead code (`dismissingIds`/`handleDismiss` in `components/SmartToDo/SmartToDo.tsx`, unused imports in `TodoAISummaryDialog.tsx`, a browser-`process` ref) — flagged TS6133/TS6196 under stricter consumers, kept verbatim to preserve parity. | Prune dead code + guard/remove the `process` ref. Safe cleanup pass (own small PR). | OPEN — no GH issue yet |
| D-5 | 022 | Query-string-level assertions for `buildTodoItemsQuery(includeCompleted)` not added (no Jest runner in `solutions/SmartTodo` yet). Logic verified via throwaway transpile only. | Add the assertion when task 040 wires Jest to `solutions/SmartTodo`. | OPEN — folds into task 040 |

## Notes
- D-1/D-2 are the natural fast-follows to the FR-02/03 scoring work (tasks 011/012) and should be resolved before FR-02/03 is called "done" at task 014.
- D-3/D-4 are hoist-hygiene follow-ups from FR-01 (tasks 002/003).
- All entries must be filed as GitHub Issues before the task-090 wrap-up PR.
