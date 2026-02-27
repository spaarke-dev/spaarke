# Smart To Do Kanban Board

> **Status**: In Progress
> **Branch**: `work/bff-matter-endpoints`
> **Module**: LegalWorkspace (Code Page — React 18 SPA)
> **Created**: 2026-02-26

---

## Overview

Replace the flat Smart To Do list in the Corporate Workspace with a three-column Kanban board (Today / Tomorrow / Future). Items are automatically assigned to columns based on their To Do Score (0-100) thresholds, with drag-and-drop between columns, pin/lock for manual override, and expandable cards via Xrm side pane.

## Key Features

- **Three-column Kanban**: Today (score ≥ 60), Tomorrow (30-59), Future (< 30)
- **Drag-and-drop**: `@hello-pangea/dnd` for reorder within and between columns
- **Pin/Lock**: Prevent auto-reassignment on recalculate
- **Recalculate**: Re-run To Do Score on unpinned items
- **Expandable cards**: Click opens side pane with editable description
- **User preferences**: Configurable thresholds persisted in `sprk_userpreference`
- **Reusable DnD**: Generic `KanbanBoard` component for cross-app use

## Architecture

```
SmartToDo (existing container — modified)
├── KanbanHeader (title, recalculate, settings gear)
├── KanbanBoard (generic DnD wrapper — @hello-pangea/dnd)
│   ├── KanbanColumn[Today]
│   │   └── KanbanCard[] (draggable)
│   ├── KanbanColumn[Tomorrow]
│   │   └── KanbanCard[]
│   └── KanbanColumn[Future]
│       └── KanbanCard[]
├── ThresholdSettingsPopover
└── TodoDetailPane (side pane content)
```

## Dataverse Entities

| Entity | Field | Type | Purpose |
|--------|-------|------|---------|
| `sprk_event` | `sprk_todocolumn` | Choice (0/1/2) | Column assignment |
| `sprk_event` | `sprk_todopinned` | Boolean | Lock item in column |
| `sprk_userpreference` | `sprk_preferencetype` | Choice | "TodoKanbanThresholds" |
| `sprk_userpreference` | `sprk_preferencevalue` | Text (JSON) | Threshold values |

## Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| ADR-006 | Code Pages for standalone dialogs; Kanban is within the existing Code Page |
| ADR-012 | Reusable components in shared library |
| ADR-021 | Fluent v9 only, semantic tokens, dark mode required |
| ADR-022 | Code Page bundles own React 18 — no platform restrictions |

## Graduation Criteria

- [ ] Three-column Kanban renders with score-based column assignment
- [ ] Drag-drop moves items with optimistic UI + Dataverse persistence
- [ ] Pinned items survive Recalculate
- [ ] Card click opens side pane with editable description
- [ ] Thresholds persist across sessions
- [ ] KanbanBoard is generic (no domain logic)
- [ ] Dark mode + high-contrast render correctly
- [ ] `npm run build` zero errors
