# @spaarke/smart-todo-components

> **Status**: 0.1.0 (R4 task 020 / Pattern D rebuild — 2026-06-10)
> **Mirrors**: `@spaarke/events-components` published-only structure
> **Audit reference**: `projects/smart-todo-r4/notes/widget-surface-audit.md`

## Purpose

Host-agnostic React components for Smart To Do, sized to be consumed by:
1. **LegalWorkspace embedded section shim** (`src/solutions/LegalWorkspace/src/sections/todo.registration.ts`) — the current Dashboard-wrapper mount path.
2. **(Future) SpaarkeAi Direct widget registration** (`@spaarke/ai-widgets/workspace/register-workspace-widgets.ts`) — adds the optional `smart-todo` Direct widget type per BUILD-A-NEW-WORKSPACE-WIDGET §4.2 Step 6.

The peer package is the **canonical** SmartTodoWidget surface going forward. Modeled on the proven `@spaarke/events-components` CalendarWorkspaceWidget pattern (R3 task 115).

## What's host-agnostic

The widget queries `sprk_todo` via `Xrm.WebApi` filtered by:
- Regarding context (`_sprk_regarding<X>_value eq <recordId>`) — passed in by the host
- `statecode eq 0 AND (statuscode eq 1 or statuscode eq 659490001)` — Open + In Progress per R3 task 009 / spec.md FR-02

It does **not** subscribe to any host-specific context (e.g., FeedTodoSyncContext). Cross-block sync is the host shim's responsibility — the shim subscribes and triggers `refetch` via the prop callback passed in (`onRefetchReady`).

## What's deferred (R4 wrap-up follow-up)

The 13-file LW SmartToDo subtree (KanbanCard, KanbanHeader, ThresholdSettings, DismissedSection, EffortScoreCard, PriorityScoreCard, TodoAISummaryDialog, etc.) is NOT hoisted in this initial 0.1.0 release. It remains in `src/solutions/LegalWorkspace/src/components/SmartToDo/` and is composed by the shim. A follow-up task should hoist these to `src/components/` inside this package as Pattern D fully matures.

## Consumption

```ts
// LW shim — todo.registration.ts
import { SmartTodoWidget } from '@spaarke/smart-todo-components';
import { WidgetErrorBoundary } from '@spaarke/ui-components';

return React.createElement(
  WidgetErrorBoundary,
  { widgetType: 'smart-todo', displayName: 'Smart To Do', surface: 'LegalWorkspace' },
  React.createElement(SmartTodoWidget, {
    webApi: ctx.webApi,
    userId: ctx.userId,
    scope: ctx.scope,
    businessUnitId: ctx.businessUnitId,
    feedSync: { notifyChange, subscribe },  // wired in from FeedTodoSyncContext
    onRefetchReady: ctx.onRefetchReady,
    onBadgeCountChange: ctx.onBadgeCountChange,
  })
);
```

## ADRs

- ADR-012 — Shared component library (peer package mirrors `@spaarke/events-components`).
- ADR-021 — Fluent UI v9 + Griffel + semantic tokens.
- ADR-030 — PaneEventBus contract (this widget does NOT dispatch lifecycle events; no bus wiring needed).
