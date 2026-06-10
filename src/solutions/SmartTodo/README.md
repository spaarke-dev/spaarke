# SmartTodo Code Page

Unified Kanban board with inline resizable detail panel for managing `sprk_todo`
records in Dataverse.

## Overview

SmartTodo is a standalone React 19 Code Page (`sprk_smarttodo`) that combines
the Kanban board and TodoDetailPanel into a single HTML web resource. Clicking
a Kanban card opens an inline detail panel on the right, connected via shared
React context — no iframes, no BroadcastChannel, no side panes.

Per R3 FR-09 / FR-11, the kanban path queries the first-class `sprk_todo`
entity (not `sprk_event` with `sprk_todoflag=true`). The legacy two-entity
detail model (`sprk_event` + `sprk_eventtodo`) was retired in Phase 2/3
per OS-1 (no compat shims).

## Architecture

```
App (FluentProvider + theme detection)
  SmartTodoApp
    TodoProvider (shared state: items, selectedEventId — sprk_todoid; optimistic updates)
      SmartTodoLayout
        Left panel  — SmartToDo (Kanban board: Today/Tomorrow/Future columns)
        PanelSplitter (draggable, keyboard-accessible, from @spaarke/ui-components)
        Right panel — TodoDetailPanel (collapsible; loads a single sprk_todo record)
```

### Key Components

| Component | Purpose |
|-----------|---------|
| `App.tsx` | Root shell with FluentProvider and theme detection |
| `SmartTodoApp.tsx` | Two-panel layout using useTwoPanelLayout hook |
| `TodoContext.tsx` | Shared state: items, selectedEventId, updateItem, handleRemove |
| `TodoDetailPanel.tsx` | Detail panel wrapper — loads Dataverse data, calls updateItem for optimistic updates |
| `SmartToDo.tsx` | Kanban board with drag-drop, scoring, and card click selection |
| `KanbanCard.tsx` | Individual card in Kanban columns |

### Shared Library Dependencies

From `@spaarke/ui-components`:
- `PanelSplitter` — draggable resize handle with ARIA role="separator"
- `useTwoPanelLayout` — two-panel layout hook with localStorage persistence
- `TodoDetail` — reusable detail form (rendered inline by `TodoDetailPanel`)
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
