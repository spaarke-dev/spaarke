# Widget parity UAT — audit findings & remediation plan

> **Date**: 2026-06-18
> **Trigger**: UAT screenshot 2026-06-18 of deployed SmartTodo widget in SpaarkeAi workspace (post-2026-06-18 master deploy from PR #384). Six functional/architectural issues surfaced.
> **Decision**: Three new tasks (R4-099 / 100 / 101) — Wave D follow-up to PR #384.

## Six issues (raw user feedback)

1. To Do items created via the widget's `+` button do not appear in the widget after the wizard closes.
2. Widget "does not function like the app" — should follow Pattern D dual-use per `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §4 (same component reused in both shapes via shared-lib + Dashboard wrapper AND Direct widget wrapper).
3. Search should be a toolbar item, not a separate section.
4. Clicking Open on a To Do opens the Kanban Code Page, not the To Do record itself.
5. Duplicate "title" + duplicate toolbars in the widget — should be ONE title "Smart To Do", ONE toolbar with `[+, Open, refresh]`. The pane chrome `+` should be removed (the widget owns `+`).
6. Items should be organized by Today / Tomorrow / Future sections (mirroring the full app's Kanban columns).

## Diagnostic findings (cross-file evidence)

The widget code itself (`src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx`) is largely Pattern D compliant — host-agnostic, `webApi` injected via prop, `feedSync` bridged (no direct `FeedTodoSyncContext` coupling). The architectural drift is **in the LegalWorkspace shim** (`src/solutions/LegalWorkspace/src/sections/todo.registration.ts`), which adds a section-level chrome (title + toolbar) on top of the widget's own PaneHeader chrome.

**Comparison with Calendar (canonical Pattern D)**: `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` lines 51-58 declare a section config with **no title**, **no section toolbar** — the `CalendarWorkspaceWidget` owns 100% of the chrome. SmartTodo's shim breaks this rule.

### Per-issue root causes

| # | Root cause | File:line |
|---|---|---|
| 1 | Shim's `onAddTodo` → `ctx.onOpenWizard('sprk_createtodowizard')` navigates away; no post-wizard-close listener fires `refetchRef.current?.()` so widget shows stale list when user returns. | `todo.registration.ts:105-107` |
| 2 | Architecture compliance is broken at the shim layer, not the widget. Shim adds chrome the widget already owns. Calendar pattern: shim is purely structural. | `todo.registration.ts:188-222, 232` vs `calendar.registration.ts:51-58` |
| 3 | Widget has **no search input at all**. Code Page has search in Header.tsx Row 2 (lines 268-273). Widget should expose `searchQuery` prop and render a SearchBox in PaneHeader. | `SmartTodoWidget.tsx:183-213, 372-394` |
| 4 | Shim's `onOpenTodo` → `ctx.onOpenWizard('sprk_smarttodo', data, …)` opens the Code Page bare, which defaults to Kanban. Needs a launch-action discriminator (e.g. `?action=openTodo&todoId=<guid>`) that the Code Page's `useLaunchContext` parses to auto-mount `<SmartTodoModal>`. | `todo.registration.ts:94-103` + `src/solutions/SmartTodo/src/hooks/useLaunchContext.ts` (extend) + `src/solutions/SmartTodo/src/SmartTodoApp.tsx` (auto-mount branch) |
| 5 | Two titles + two toolbars: shim declares `title: "My To Do List"` (line 232) → rendered as section chrome by host; widget renders its own `<PaneHeader title=…>` (`SmartTodoWidget.tsx:372-373`). Same for buttons — shim toolbar (lines 188-222) + widget PaneHeader right slot (lines 376-390). | `todo.registration.ts:188-222, 232` + `SmartTodoWidget.tsx:372-394` |
| 6 | Widget renders flat list via `renderItem` (`SmartTodoWidget.tsx:339-364`). Code Page uses `useKanbanColumns` (`src/solutions/SmartTodo/src/hooks/useKanbanColumns.ts`) to bucket items by due date + threshold prefs into Today / Tomorrow / Future. Hoist was **explicitly deferred** from R4-020 ("13-file rich-feature subtree" follow-up). | `src/solutions/SmartTodo/src/hooks/useKanbanColumns.ts` (source of grouping) + `SmartTodoWidget.tsx:339-364, 425` (flat render to replace) |

## Structural observation

The widget's host-agnostic structure is correct. The LW shim's chrome layering is the misalignment. Calendar's shim has zero section-level chrome — the widget owns everything. SmartTodo's shim must collapse to the same shape for issues 2 + 5 to be solved.

## Three-task remediation plan

| Task | Closes issues | Effort | Files | Parallel-safe with |
|---|---|---|---|---|
| **R4-099 (W-1)** Widget chrome consolidation + Pattern D alignment | 2, 3, 5 | ~0.5-1 day | `todo.registration.ts`, `SmartTodoWidget.tsx` (+ styles) | R4-101 |
| **R4-100 (W-2)** Open-to-form launch protocol + refetch wiring | 1, 4 | ~1 day | `useLaunchContext.ts`, `SmartTodoApp.tsx`, `todo.registration.ts` | None — serializes after R4-099 (shared file: `todo.registration.ts`) |
| **R4-101 (W-3)** Today/Tomorrow/Future grouping (`useKanbanColumns` hoist) | 6 | ~1 day | `useKanbanColumns.ts` hoist into peer package; `SmartTodoWidget.tsx` rendering | R4-099 |

**Dispatch order**: Wave D-1 = R4-099 + R4-101 parallel; Wave D-2 = R4-100 alone after R4-099 lands. All three land in ONE PR.

**Out of scope for Wave D**:
- Full Kanban interactivity in the widget (drag, score thresholds UI) — widget shows grouped lists only, NOT a full drag-drop Kanban
- BFF / API changes — purely client-side
- New PCF controls
- Spec-defined NFR test sweep (that's R4-093)

## References

- `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` §4 — dual-use Pattern D contract
- `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` — canonical Pattern D shim shape
- `projects/smart-todo-r4/notes/widget-surface-audit.md` — R4-001 audit that originally chose Pattern D
- PR #384 (`work/smart-todo-r4-wave2` → master, merged 2026-06-11) — current deployed state
