# Task Index — Smart To Do Kanban Board

> **Project**: smart-todo-kanban
> **Total Tasks**: 15
> **Last Updated**: 2026-02-26

---

## Status Legend

| Icon | Status |
|------|--------|
| 🔲 | Pending |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏭️ | Skipped |

---

## Phase 1: Foundation (Tasks 001-005)

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|--------------|----------|
| 001 | Install @hello-pangea/dnd dependency | 🔲 | — | 15min |
| 002 | Extend data model interfaces (IEvent, IUserPreference, enums, SELECT) | 🔲 | — | 30min |
| 003 | Add DataverseService methods (preferences, column, pin) | 🔲 | 002 | 45min |
| 004 | Create useUserPreferences hook | 🔲 | 003 | 30min |
| 005 | Create useKanbanColumns hook | 🔲 | 003, 004 | 1hr |

## Phase 2: Reusable DnD Components (Tasks 010-012)

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|--------------|----------|
| 010 | Create generic KanbanBoard component | 🔲 | 001 | 1hr |
| 011 | Create generic KanbanColumn component | 🔲 | 010 | 45min |
| 012 | Create shared barrel export (index.ts) | 🔲 | 011 | 10min |

## Phase 3: Smart To Do Kanban Components (Tasks 020-026)

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|--------------|----------|
| 020 | Create KanbanCard component | 🔲 | 002, 012 | 1hr |
| 021 | Create KanbanHeader component | 🔲 | 002 | 30min |
| 022 | Create ThresholdSettingsPopover | 🔲 | 004 | 45min |
| 023 | Create TodoDetailPane (side pane) | 🔲 | 002, 003 | 1hr |
| 024 | Rewire SmartToDo container with Kanban board | 🔲 | 005, 012, 020-023 | 2hr |
| 025 | Update SmartToDo barrel exports | 🔲 | 024 | 10min |
| 026 | Build verification + bundle size check | 🔲 | 025 | 15min |

## Phase 4: Polish & Wrap-up (Tasks 030-032)

| # | Task | Status | Dependencies | Estimate |
|---|------|--------|--------------|----------|
| 030 | Dark mode + high-contrast verification | 🔲 | 026 | 30min |
| 031 | Accessibility review (keyboard DnD, screen reader) | 🔲 | 026 | 30min |
| 032 | Project wrap-up | 🔲 | 030, 031 | 15min |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002 | — | Dependency install and data model are independent |
| B | 004, 005 | 003 | Two independent hooks after service methods |
| C | 010-012 | 001 | DnD components only need the package installed |
| D | 020, 021, 022, 023 | Phase 1 + Phase 2 | Four independent leaf components |
| E | 030, 031 | 026 | Visual and accessibility checks in parallel |

## Critical Path

```
001 → 010 → 011 → 012 ──┐
                          ├→ 020 ──┐
002 → 003 → 005 ─────────┤        │
              └→ 004 → 022─┤      ├→ 024 → 025 → 026 → 030/031 → 032
                            ├→ 023 ┘
                            └→ 021 ┘
```

Longest path: 001 → 010 → 011 → 012 → 020 → 024 → 025 → 026 → 032

## Estimated Total Effort

~10 hours of implementation work across 15 tasks.
