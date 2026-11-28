# SmartTodo Code Page

Kanban board (and List view) for managing `sprk_todo` records in Dataverse,
with a hybrid modal (iframe-embedded OOB main form) for to-do detail editing.

## Overview

SmartTodo is a standalone React 19 Code Page (`sprk_smarttodo`) rendering a
4-row header (R4 task 030 / FR-06) over a single primary surface — Kanban
(default) or List view (R4 task 033 / FR-09) — backed by a shared React
context. Clicking a card (or selecting + the toolbar Open) opens the hybrid
`<SmartTodoModal>` (R4 task 040), which embeds the OOB MDA `sprk_todo` main
form in an iframe so save, BPF, business rules, and statuscode stay native.

Per R3 FR-09 / FR-11, the kanban path queries the first-class `sprk_todo`
entity (not `sprk_event` with `sprk_todoflag=true`). The legacy two-entity
detail model (`sprk_event` + `sprk_eventtodo`) was retired in Phase 2/3
per OS-1 (no compat shims).

Per R4 FR-18 / task 042, the R3 `TodoDetailPanel` side-pane was retired —
the hybrid modal replaces it. UAT OD-4 (no save + Completed broken) was
inherent to the side-pane pattern, not a fixable bug.

## Architecture

```
App (FluentProvider + theme detection)
  SmartTodoApp
    SmartTodoLayout (data: hooks/useSmartTodoBridge)
      Header (@spaarke/smart-todo-components — search / Filter / New To Do / overflow, selection-aware toolbar)
      SmartToDo (@spaarke/smart-todo-components — Kanban: Today/Tomorrow/Future, compact cards, hideHeader)
    LaunchCreateTodoWizardHost (Outlook / parent-ribbon createTodo launch)
```

Since #2907 / #2972 the page keeps no copy of the board or its data layer: the board, `TodoDataverseService`,
`useTodoItems` and `useTodoKanbanPreferences` come from `@spaarke/smart-todo-components` (LegalWorkspace uses the
same ones). The page supplies its list scope (to-dos assigned to the user's contact, narrowed by an `openTodos`
launch), the BFF create helper (`services/childRecordWrites.ts`) and its chrome. To-dos open in the OOB `sprk_todo`
form (`services/openTodoLauncher.ts`).

### Key Components

| Component | Purpose |
|-----------|---------|
| `App.tsx` | Root shell with FluentProvider and theme detection |
| `SmartTodoApp.tsx` | Single-surface layout — Header + shared Kanban; open / new-task / toolbar actions |
| `hooks/useSmartTodoBridge.ts` | The board's data: list scope, BFF create, one preferences instance |
| `hooks/useLaunchContext.ts` | URL launch parameters (`createTodo`, `openTodos`, `openTodo`) |
| `components/Toolbar/` | Selection-aware Open / Delete / Email / Pin actions |

### Shared Library Dependencies

From `@spaarke/ui-components`:
- `RecordNavigationModalShell` — modal shell with `<` / `>` record navigation (used by `<SmartTodoModal>`)
- `CreateTodoWizard` — modal wizard used by Outlook ribbon `createTodo` launch
- `resolveCodePageTheme` / `setupCodePageThemeListener` — theme detection

## Build

```bash
cd src/solutions/SmartTodo
npm install
npm run build
```

Produces a single HTML file via Vite + `vite-plugin-singlefile`.

## Deploy

```powershell
./scripts/Deploy-SmartTodo.ps1
```

Uploads the built HTML as the `sprk_smarttodo` web resource to Dataverse.

## Web Resource

| Property | Value |
|----------|-------|
| Name | `sprk_smarttodo` |
| Type | HTML (Single-file) |
| Framework | React 19 + Fluent UI v9 |
| Theme Support | Light, Dark, High Contrast |
