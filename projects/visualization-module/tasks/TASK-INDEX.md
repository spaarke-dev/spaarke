# Task Index - Spaarke Visuals Framework

> **Last Updated**: 2025-12-29
> **Total Tasks**: 25
> **Status**: Ready for Execution

---

## Quick Stats

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1: Foundation | 5 | 4/5 complete |
| Phase 2: Chart Components | 8 | 8/8 complete |
| Phase 3: Visual Host PCF | 4 | 1/4 complete |
| Phase 4: Drill-Through | 5 | 0/5 complete |
| Phase 5: Testing & Docs | 2 | 0/2 complete |
| Wrap-up | 1 | 0/1 complete |

---

## Task Registry

### Phase 1: Foundation & Infrastructure

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 001 | [Define sprk_chartdefinition entity schema](001-define-chartdefinition-entity.poml) | ✅ completed | none | 3 |
| 002 | [Create shared TypeScript types](002-create-shared-types.poml) | ✅ completed | 001 | 2 |
| 003 | [Scaffold Visual Host PCF project](003-scaffold-visualhost-pcf.poml) | ✅ completed | 002 | 3 |
| 004 | [Configure Storybook for chart components](004-configure-storybook.poml) | ✅ completed | 003 | 2 |
| 005 | [Deploy entity to Dataverse](005-deploy-entity-dataverse.poml) | 🔲 not-started | 001 | 2 |

### Phase 2: Core Chart Components

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 010 | [Implement MetricCard component](010-implement-metriccard.poml) | ✅ completed | 004 | 3 |
| 011 | [Implement BarChart component](011-implement-barchart.poml) | ✅ completed | 004 | 4 |
| 012 | [Implement LineChart component](012-implement-linechart.poml) | ✅ completed | 004 | 3 |
| 013 | [Implement DonutChart component](013-implement-donutchart.poml) | ✅ completed | 004 | 3 |
| 014 | [Implement StatusDistributionBar component](014-implement-statusbar.poml) | ✅ completed | 004 | 3 |
| 015 | [Implement CalendarVisual component](015-implement-calendar.poml) | ✅ completed | 004 | 4 |
| 016 | [Implement MiniTable component](016-implement-minitable.poml) | ✅ completed | 004 | 2 |
| 017 | [Create chart component unit tests](017-chart-component-tests.poml) | ✅ completed | 010-016 | 4 |

### Phase 3: Visual Host PCF Control

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 020 | [Build Visual Host PCF core](020-visualhost-pcf-core.poml) | ✅ completed | 017 | 4 |
| 021 | [Implement configuration loader service](021-configuration-loader.poml) | 🔲 not-started | 005, 020 | 3 |
| 022 | [Implement data aggregation service](022-data-aggregation-service.poml) | 🔲 not-started | 021 | 4 |
| 023 | [Integrate theme management](023-theme-integration.poml) | 🔲 not-started | 020 | 3 |

### Phase 4: Drill-Through Workspace

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 030 | [Create drill-through Custom Page](030-drillthrough-custompage.poml) | 🔲 not-started | 023 | 4 |
| 031 | [Build two-panel layout component](031-twopanel-layout.poml) | 🔲 not-started | 030 | 3 |
| 032 | [Implement filter state context](032-filter-state-context.poml) | 🔲 not-started | 031 | 3 |
| 033 | [Integrate dataset grid with filtering](033-dataset-grid-filtering.poml) | 🔲 not-started | 032 | 4 |
| 034 | [Deploy PCF and Custom Page to Dataverse](034-deploy-pcf-custompage.poml) | 🔲 not-started | 033 | 3 |

### Phase 5: Testing & Documentation

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 040 | [Integration testing with Dataverse](040-integration-testing.poml) | 🔲 not-started | 034 | 4 |
| 041 | [Complete Storybook documentation](041-storybook-documentation.poml) | 🔲 not-started | 040 | 3 |

### Project Wrap-up

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 090 | [Project wrap-up and documentation](090-project-wrap-up.poml) | 🔲 not-started | 041 | 2 |

---

## Critical Path

```
001 → 002 → 003 → 004 → 010-016 → 017 → 020 → 021 → 022 → 023 → 030 → 031 → 032 → 033 → 034 → 040 → 041 → 090
                   ↓
                  005 (parallel with 004)
```

---

## Status Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | not-started |
| 🔄 | in-progress |
| ⏸️ | blocked |
| ✅ | completed |
| ⏭️ | deferred |

---

## Execution Notes

- **Next tasks**: Task 021 (requires 005), Task 023 (020 complete)
- **Critical dependency**: Task 005 (entity deployment) must complete before 021
- **Parallel work possible**: Tasks 021 and 023 can be worked in parallel once 005 is done
- **End with**: Task 090 (project wrap-up)

---

*Updated by task-execute skill as tasks progress*
