# FR-15 (task 034) Verification — already-resolved, no code change

> **Date**: 2026-08-16
> **Task**: 034 — Migrate SmartTodo preview/browse consumer → BrowseModal, OR confirm already-resolved
> **Outcome**: **Already-resolved.** No live `RecordNavigationModalShell` consumer exists in
> SmartTodo/LegalWorkspace-SmartToDo scope post-hoist (task 003). No code change made.

---

## Verification performed (post-hoist state, gated on task 003)

### 1. Grep for `RecordNavigationModalShell` usage in scope

```
Grep "RecordNavigationModalShell" — path: src/solutions/SmartTodo
  → src\solutions\SmartTodo\src\components\Toolbar\ToolbarActions.ts   (comment only, see below)
  → src\solutions\SmartTodo\README.md                                  (stale doc line, see below)

Grep "RecordNavigationModalShell" — path: src/solutions/LegalWorkspace
  → No files found

Grep "RecordNavigationModalShell" — path: src/client/shared/Spaarke.SmartTodo.Components
  → No files found

Grep "RecordNavigationModalShell" — path: src/solutions/LegalWorkspace/src/components/SmartToDo
  → No files found

Grep "import.*RecordNavigationModalShell|from ['\"].*RecordNavigationModalShell" — path: src/solutions/SmartTodo
  → No matches found (zero actual imports)

Glob "src/solutions/SmartTodo/src/**/*.tsx" — cross-checked against the two textual hits above:
  none of the 15 .tsx files in SmartTodo import or render RecordNavigationModalShell.

Glob "src/solutions/SmartTodo/src/components/Modal/**" → No files found (the README's
  described `components/Modal/` directory does not exist).

Glob "**/SmartTodoModal.tsx" → No files found (confirms 2026-07-01 deletion by
  ai-spaarke-ai-workspace-UI-r2 task 022 — no resurrection).
```

**Repo-wide** `RecordNavigationModalShell` grep (`src/`) returns 27 files total; outside the
two SmartTodo textual hits above, all other matches are the component's own source
(`Spaarke.UI.Components/.../RecordNavigationModalShell/*`), `BrowseModal.tsx`/its test (the
canonical migration target), and the three previously-identified **unrelated** consumers —
`RichFilePreviewDialog.tsx` (Documents preview), `ReconciliationBrowseShell.tsx`
(Communication.Components), `QuickStartModal.tsx` (SpaarkeAi conversation) — plus unrelated
compiled PCF `bundle.js` artifacts and one unrelated PCF app file
(`CommunicationAttachmentsApp.tsx`). None are in FR-15's scope and none were modified by this
task.

### 2. Disposition of the two SmartTodo textual hits

Both hits are **stale comments describing a superseded plan**, not live code:

- `src/solutions/SmartTodo/src/components/Toolbar/ToolbarActions.ts` (lines ~7-11, ~114-118):
  docblock says the Open action's `OPEN_TODOS_EVENT` dispatch "Task 040 will subscribe to this
  event and route to `<RecordNavigationModalShell>` + To Do main form iframe." This references
  an **R4-era task 040** (the original hybrid-modal plan), not this project's task 040
  (`040-vitest-expansion-coverage.poml`, unrelated — vitest/coverage work per
  `tasks/TASK-INDEX.md` row 29).

- `src/solutions/SmartTodo/README.md` (lines 43, 50): describes `components/Modal/` — "Hybrid
  `<SmartTodoModal>` — `<RecordNavigationModalShell>` + OOB form iframe" — a directory that
  does not exist in the current tree (glob above returns zero files) and a component
  (`SmartTodoModal.tsx`) that was deleted 2026-07-01.

**Live implementation check** — `src/solutions/SmartTodo/src/SmartTodoApp.tsx` lines 244-284
contains the authoritative, current comment and code:

> "R2 FR-13 (2026-07-01) — The hybrid `<SmartTodoModal>` (R4 task 040) that used to overlay
> this component has been retired. The `OPEN_TODOS_EVENT` listener and the `useLaunchContext`
> openTodo effect (below) now call `openSprkTodoAsLayout1` directly, which uses
> `Xrm.Navigation.navigateTo` at 85% × 85% (Layout 1 standard per FR-20). No local modal state
> remains."

Both the `useLaunchContext` `openTodo` effect (line 257-264) and the `OPEN_TODOS_EVENT`
listener (line 273-284) call `openSprkTodoAsLayout1(...)` — the OOB `navigateTo` main-form
path (ADR-050 Path A exception, per `projects/smart-todo-r5/CLAUDE.md` "ADR Tension in
Effect") — not `RecordNavigationModalShell`. The two textual hits are therefore
**doc/comment drift left over from a since-retired design**, not evidence of a live consumer.
No `.tsx` file in `src/solutions/SmartTodo/src/**` imports or renders
`RecordNavigationModalShell` (confirmed by the targeted import-grep and the exhaustive `.tsx`
glob cross-check above).

### 3. Equivalent duplicate-title-bar pattern check (non-literal)

Checked the two SmartTodo-family dialogs that DO wrap content in a Fluent v9 `Dialog` for an
equivalent "nested chrome" problem:

- `src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartToDo/TodoAISummaryDialog.tsx`
  — single Fluent v9 `Dialog`/`DialogSurface`/`DialogTitle` wrapping a scoring grid. One title
  source, no nested modal shell, no "N of M" browse navigation. Not a browse consumer —
  out of FR-15's scope.
- `src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartToDo/SmartToDoDialog.tsx`
  — single Fluent v9 `Dialog`/`DialogSurface`/`DialogTitle` wrapping the Kanban board (90%×90%,
  "sized to match the retired navigateTo dialog dimensions" per its own docblock). One title
  source, no nested shell, no browse nav. Not a browse consumer — out of FR-15's scope.

Neither renders `RecordNavigationModalShell` nor any other nested-chrome/duplicate-title
pattern. No equivalent problem found.

### 4. `chromeMode` MUST-NOT check

```
Grep "chromeMode" — path: src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell
  → No matches found

Grep "chromeMode" — path: src (repo-wide, src/ subtree)
  → No files found
```

Confirms the MUST-NOT rule (ADR-050 / `projects/smart-todo-r5/CLAUDE.md` "MUST NOT Rules") is
intact: no `chromeMode` prop exists anywhere in `RecordNavigationModalShell`'s
props/types (`RecordNavigationModalShell/types.ts` reviewed directly — no such member).

---

## Conclusion

FR-15 (FU-2) is **already resolved by prior work**: the SmartTodo `SmartTodoModal.tsx` hybrid
consumer of `RecordNavigationModalShell` was deleted 2026-07-01 by
`ai-spaarke-ai-workspace-UI-r2` task 022 (R2 FR-13), and the live open path
(`openSprkTodoAsLayout1` via `Xrm.Navigation.navigateTo`) does not use
`RecordNavigationModalShell` at all — it is the ADR-050 Path A OOB-`navigateTo` exception
already documented in `projects/smart-todo-r5/CLAUDE.md`. The two remaining textual
`RecordNavigationModalShell` references in `src/solutions/SmartTodo/` (`ToolbarActions.ts`
docblock, `README.md`) are stale comments describing the retired plan; they do not affect
runtime behavior. This task made **no `src/**` edits** — no migration was needed, and no
unrelated `RecordNavigationModalShell` consumer (`RichFilePreviewDialog.tsx`,
`ReconciliationBrowseShell.tsx`, `QuickStartModal.tsx`) was touched.

**Recommendation for a future cleanup task (optional, not part of this task's scope)**: the
stale `ToolbarActions.ts` docblock and `README.md` "Hybrid `<SmartTodoModal>`" line could be
updated to reflect the current `openSprkTodoAsLayout1` architecture, to prevent future
confusion. Not filed as a defer/issue since it is a comment-only drift with no functional
impact — noted here for the record.

## Acceptance criteria verification

| Criterion | Result |
|---|---|
| If a live consumer found → migrated to BrowseModal, no chromeMode added | N/A — no live consumer found |
| If no live consumer found → negative finding documented with grep evidence | ✅ this file |
| No `chromeMode` API exists in `RecordNavigationModalShell` props/types | ✅ verified (§4) |
| No unrelated `RecordNavigationModalShell` consumer modified | ✅ none touched (read-only grep only) |
| All unit tests pass; build is green | N/A — no code changed this task; nothing to build/test |
