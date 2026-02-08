# Task Index - Visualization Framework R2

> **Generated**: 2026-02-08
> **Total Tasks**: 12 core tasks (expandable to 24)
> **Estimated Effort**: 28-40 hours

---

## Task Status Legend

| Status | Meaning |
|--------|---------|
| 🔲 | Pending |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Blocked |
| ⏭️ | Skipped |

---

## Phase 1: Schema Changes (4-6 hrs)

| Task | Description | Status | Dependencies |
|------|-------------|--------|--------------|
| [001](001-create-click-action-fields.poml) | Create click action fields in sprk_chartdefinition | 🔲 | None |
| [002](002-create-visual-config-fields.poml) | Create visual configuration fields | 🔲 | None |
| [003](003-add-visual-type-options.poml) | Add DueDateCard and DueDateCardList option set values | 🔲 | None |
| [004](004-export-schema-solution.poml) | Export and commit solution with schema changes | 🔲 | 001, 002, 003 |

---

## Phase 2: Click Action Handler (6-8 hrs)

| Task | Description | Status | Dependencies |
|------|-------------|--------|--------------|
| [010](010-extend-chart-definition-interface.poml) | Extend IChartDefinition interface with new fields | 🔲 | 004 |
| [011](011-update-configuration-loader.poml) | Update ConfigurationLoader to fetch new fields | 🔲 | 010 |
| [012](012-create-click-action-handler.poml) | Create ClickActionHandler service | 🔲 | 011 |
| [013](013-wire-click-handler-to-renderer.poml) | Wire click handler to ChartRenderer components | 🔲 | 012 |
| [014](014-click-handler-tests.poml) | Unit tests for ClickActionHandler | 🔲 | 012 |

---

## Phase 3: EventDueDateCard Shared Component (4-6 hrs)

| Task | Description | Status | Dependencies |
|------|-------------|--------|--------------|
| [020](020-create-event-due-date-card.poml) | Create EventDueDateCard component | 🔲 | None |
| [021](021-card-styling-dark-mode.poml) | Implement styling with Fluent tokens and dark mode | 🔲 | 020 |
| [022](022-card-tests-stories.poml) | Unit tests and Storybook stories | 🔲 | 021 |
| [023](023-export-shared-component.poml) | Export component from @spaarke/ui-components | 🔲 | 022 |

---

## Phase 4: Due Date Card Visual Types (6-8 hrs)

| Task | Description | Status | Dependencies |
|------|-------------|--------|--------------|
| [030](030-due-date-card-visual.poml) | Create DueDateCard visual component | 🔲 | 013, 023 |
| [031](031-due-date-card-list-visual.poml) | Create DueDateCardList visual component | 🔲 | 030 |
| [032](032-update-chart-renderer.poml) | Update ChartRenderer with new visual type routing | 🔲 | 031 |
| [033](033-view-list-navigation.poml) | Implement "View List" navigation link | 🔲 | 032 |

---

## Phase 5: Advanced Query Support (4-6 hrs)

| Task | Description | Status | Dependencies |
|------|-------------|--------|--------------|
| [040](040-create-view-data-service.poml) | Create ViewDataService for view-driven fetching | 🔲 | 033 |
| [041](041-parameter-substitution.poml) | Implement parameter substitution engine | 🔲 | 040 |
| [042](042-query-priority-resolution.poml) | Implement query priority resolution | 🔲 | 041 |
| [043](043-add-fetchxml-override-property.poml) | Add fetchXmlOverride PCF property | 🔲 | 042 |
| [044](044-query-support-tests.poml) | Unit tests for query support features | 🔲 | 043 |

---

## Phase 6: Testing & Deployment (4-6 hrs)

| Task | Description | Status | Dependencies |
|------|-------------|--------|--------------|
| [050](050-end-to-end-testing.poml) | End-to-end testing in Dataverse | 🔲 | 044 |
| [051](051-deploy-visualhost.poml) | Deploy VisualHost v1.2.0 | 🔲 | 050 |
| [052](052-migrate-duedate-widget.poml) | Migrate DueDateWidget to use VisualHost | 🔲 | 051 |
| [090](090-project-wrap-up.poml) | Project wrap-up and documentation | 🔲 | 052 |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 001, 002, 003 | None | Independent schema changes |
| B | 020, 021, 022 | None | Shared component work (parallel to Phase 1-2) |

---

## Critical Path

```
001, 002, 003 ─► 004 ─► 010 ─► 011 ─► 012 ─► 013 ─► 030 ─► 031 ─► 032 ─► 033 ─► 040 ─► ... ─► 090
                                                    ▲
020 ─► 021 ─► 022 ─► 023 ────────────────────────────┘
```

---

## High Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 012 | Xrm.Navigation API availability | Test in Dataverse early |
| 040 | View FetchXML complexity | Start with simple views |
| 050 | Integration test coverage | Thorough test plan |

---

## Notes

**Core Tasks Created**: 12 task files currently exist. Additional tasks (013-014, 021-023, 031-033,
041-044, 051-052) can be created during implementation as needed. Each core task may encompass
work from multiple planned tasks.

**Task Files Status**:
- ✅ 001, 002, 003, 004 - Phase 1 (Schema)
- ✅ 010, 011, 012 - Phase 2 (Click Handler)
- ✅ 020 - Phase 3 (Shared Component)
- ✅ 030 - Phase 4 (Visual Types)
- ✅ 040 - Phase 5 (Query Support)
- ✅ 050, 090 - Phase 6 (Testing/Wrap-up)

---

*Last Updated: 2026-02-08*
