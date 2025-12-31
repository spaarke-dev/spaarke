# Task Index - Spaarke Visuals Framework

> **Last Updated**: 2025-12-30
> **Total Tasks**: 28
> **Status**: Phase 6 In Progress

---

## Quick Stats

| Phase | Tasks | Status |
|-------|-------|--------|
| Phase 1: Foundation | 5 | 5/5 complete |
| Phase 2: Chart Components | 8 | 8/8 complete |
| Phase 3: Visual Host PCF | 4 | 4/4 complete |
| Phase 4: Drill-Through | 5 | 5/5 complete |
| Phase 5: Testing & Docs | 2 | 2/2 complete |
| Phase 6: v1.1.0 Enhancements | 3 | 2/3 in-progress |
| Wrap-up | 1 | 0/1 not-started |

---

## Task Registry

### Phase 1: Foundation & Infrastructure

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 001 | [Define sprk_chartdefinition entity schema](001-define-chartdefinition-entity.poml) | ✅ completed | none | 3 |
| 002 | [Create shared TypeScript types](002-create-shared-types.poml) | ✅ completed | 001 | 2 |
| 003 | [Scaffold Visual Host PCF project](003-scaffold-visualhost-pcf.poml) | ✅ completed | 002 | 3 |
| 004 | [Configure Storybook for chart components](004-configure-storybook.poml) | ✅ completed | 003 | 2 |
| 005 | [Deploy entity to Dataverse](005-deploy-entity-dataverse.poml) | ✅ completed | 001 | 2 |

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
| 021 | [Implement configuration loader service](021-configuration-loader.poml) | ✅ completed | 005, 020 | 3 |
| 022 | [Implement data aggregation service](022-data-aggregation-service.poml) | ✅ completed | 021 | 4 |
| 023 | [Integrate theme management](023-theme-integration.poml) | ✅ completed | 020 | 3 |

### Phase 4: Drill-Through Workspace

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 030 | [Create drill-through Custom Page](030-drillthrough-custompage.poml) | ✅ completed | 023 | 4 |
| 031 | [Build two-panel layout component](031-twopanel-layout.poml) | ✅ completed | 030 | 3 |
| 032 | [Implement filter state context with Dataset PCF](032-filter-state-context.poml) | ✅ completed | 031 | 4 |
| 033 | [Integrate dataset grid with platform filtering](033-dataset-grid-filtering.poml) | ✅ completed | 032 | 3 |
| 034 | [Deploy PCF and Custom Page to Dataverse](034-deploy-pcf-custompage.poml) | ✅ completed | 033 | 3 |

### Phase 5: Testing & Documentation

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 040 | [Integration testing with Dataverse](040-integration-testing.poml) | ✅ completed | 034 | 4 |
| 041 | [Complete Storybook documentation](041-storybook-documentation.poml) | ✅ completed | 040 | 3 |

### Phase 6: Visual Host v1.1.0 Enhancements

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 050 | [Visual Host v1.1.0 PCF changes](050-visualhost-v110-pcf.poml) | ✅ completed | 040 | 4 |
| 051 | [Chart Definition form JavaScript](051-chartdefinition-form-js.poml) | ✅ completed | 050 | 2 |
| 052 | [Deploy v1.1.0 and integration testing](052-deploy-v110-test.poml) | 🔄 in-progress | 050, 051 | 3 |

### Project Wrap-up

| ID | Title | Status | Dependencies | Est. Hours |
|----|-------|--------|--------------|------------|
| 090 | [Project wrap-up and documentation](090-project-wrap-up.poml) | 🔲 not-started | 052 | 2 |

---

## Critical Path

```
Phase 1-5 (Complete):
001 → 002 → 003 → 004 → 010-016 → 017 → 020 → 021 → 022 → 023 → 030 → 031 → 032 → 033 → 034 → 040 → 041
                   ↓
                  005 (parallel with 004)

Phase 6 (Current):
040 → 050 → 051 → 052 → 090
           ↘     ↗
            (parallel)
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

- **Current Phase**: Phase 6 - Visual Host v1.1.0 Enhancements
- **Next tasks**: Task 050, 051, 052 (v1.1.0 implementation)
- **Phase 1-5 complete**: All core development and testing done
- **Phase 6 scope**: Hybrid chart selection, context filtering, Chart Definition UX
- **End with**: Task 090 (project wrap-up)

### Architecture Clarification (2025-12-29)

**DrillThroughWorkspace must be a Dataset PCF** (per ADR-011 and spec FR-03):
- Grid bound to view from `sprk_baseviewid` via platform dataset
- Filter via `dataset.filtering.setFilter()` API
- Platform handles paging, security, column schema
- See `UniversalDatasetGrid` for reference pattern

**Impact on tasks:**
- Task 032 updated: Includes manifest refactor to add `<data-set>` element
- Task 033 updated: Grid renders from `context.parameters.dataset` (no WebAPI)
- Services (ConfigurationLoader, DataAggregation) remain for chart side

### Task 033 Implementation (2025-12-29)

**DrillThroughGrid**: Purpose-built grid for chart drill-through (independent from UniversalDatasetGrid)
- Fluent UI v9 DataGrid with FilterStateContext integration
- Displays filtered dataset records based on chart interactions
- Filter column highlighting and active filter badge
- Multi-select with platform dataset sync
- 10 unit tests covering loading/empty/data/selection/accessibility

**Files created/modified:**
- `DrillThroughGrid.tsx` - New grid component
- `DrillThroughWorkspaceApp.tsx` - Integrated DrillThroughGrid
- `DrillThroughGrid.test.tsx` - Unit tests

### Task 034 Deployment (2025-12-29) - UPDATED

**PCF controls deployed to Dataverse dev environment:**
- DrillThroughWorkspace (v1.1.0) - 163 KB bundle - `sprk_Spaarke.Controls.DrillThroughWorkspace`
- VisualHost (v1.0.0) - 684 KB bundle - `sprk_Spaarke.Visuals.VisualHost`

**Deployment details:**
- Target: https://spaarkedev1.crm.dynamics.com
- Solution: PowerAppsToolsTemp_sprk (dev deployment)
- Both using platform libraries (React 16.14.0, Fluent UI 9.46.2)
- CPM temporarily disabled during `pac pcf push`
- Workaround required: Manual copy to `obj/PowerAppsToolsTemp_sprk/bin/net462` + dotnet build

### Task 040 Integration Testing (2025-12-29) - IN PROGRESS

**PCF controls verified in Dataverse:**
- `sprk_Spaarke.Visuals.VisualHost` (v1.0.3, Lookup.Simple binding)
- `sprk_Spaarke.Controls.DrillThroughWorkspace` (v1.1.0, Grid/Dataset)

**Architecture Update (v1.0.3):**
Visual Host now uses **lookup binding** instead of static GUID:
- Form entity needs a lookup column → sprk_chartdefinition
- Visual Host PCF binds to that lookup column
- Different records can display different charts
- See `power-app-setup-guide.md` for detailed instructions

**Test data created:**
- 7 chart definition records in sprk_chartdefinition entity
- All 7 visual types represented (MetricCard through MiniTable)

**Test Chart Definition IDs:**
| Visual Type | Name | ID |
|-------------|------|-----|
| MetricCard | Test - Active Accounts Count | `cf7e2453-2be5-f011-8406-7ced8d1dc988` |
| BarChart | Test - Accounts by Industry | `d07e2453-2be5-f011-8406-7ced8d1dc988` |
| LineChart | Test - Accounts Created Over Time | `1b2cb856-2be5-f011-8406-7c1e520aa4df` |
| AreaChart | Test - Revenue by Account Type | `1c2cb856-2be5-f011-8406-7c1e520aa4df` |
| DonutChart | Test - Accounts by Status | `73e9c352-2be5-f011-8406-7c1e525abd8b` |
| StatusBar | Test - Account Status Bar | `1d2cb856-2be5-f011-8406-7c1e520aa4df` |
| MiniTable | Test - Top 10 Accounts | `1e2cb856-2be5-f011-8406-7c1e520aa4df` |

**Scripts created:**
- `scripts/query-chartdefinitions.ps1` - Query chart definition records
- `scripts/create-test-chartdefinitions.ps1` - Create test records

**Setup guide created:**
- `projects/visualization-module/notes/power-app-setup-guide.md`
- Updated for v1.0.3 lookup binding architecture
- Step-by-step: Create lookup column → Add to form → Bind PCF
- Testing checklist

**Completed (automated):**
- [x] Deploy VisualHost PCF v1.0.3 (lookup binding)
- [x] Deploy DrillThroughWorkspace PCF v1.1.0
- [x] Verify controls registered in Dataverse
- [x] Create 7 test chart definition records
- [x] Update power-app-setup-guide.md for lookup binding

**Pending - Manual configuration required (Power Apps maker portal):**
- [ ] Create lookup column on Account → sprk_chartdefinition
- [ ] Add Visual Host PCF to form bound to lookup column
- [ ] Create DrillThroughWorkspace Custom Page (optional)
- [ ] Test all 7 visual types render correctly
- [ ] Test drill-through navigation and filtering
- [ ] Test theme integration

### Phase 6: Visual Host v1.1.0 Enhancements (2025-12-30)

**Enhancement Requirements** (identified during Task 040 integration testing):

1. **Multiple Charts Per Form**
   - Current: Only lookup binding (one chart per lookup field)
   - Enhancement: Add static ID binding for form-level chart configuration
   - Benefit: Place multiple Visual Hosts with different charts on same form

2. **Context Filtering (Show Related Only)**
   - Current: Charts show all data from view
   - Enhancement: Add `contextFieldName` property to filter by current record
   - Benefit: "Documents for this Matter" instead of "All Documents"

3. **Chart Definition UX Improvement**
   - Current: Manual entry of Entity Logical Name and View GUID
   - Enhancement: Lookup to sprk_reportingentity and sprk_reportingview
   - Benefit: User-friendly selection with cascading filter

**Schema Changes (Completed):**
- `sprk_reportingentity` lookup added to `sprk_chartdefinition`
- `sprk_reportingview` lookup added to `sprk_chartdefinition`
- Backing fields retained for compatibility: `sprk_entitylogicalname`, `sprk_baseviewid`

**Existing Infrastructure:**
- `sprk_reportingentity` table (Display Name, Logical Name, Schema Name, etc.)
- `sprk_reportingview` table (View Name, View ID GUID, Reporting Entity lookup, Is Default)

**Implementation Approach:**
- Task 050: PCF manifest + TypeScript changes for hybrid binding and context filtering
- Task 051: Minimal form JavaScript (~30 lines) to sync lookups to backing fields
- Task 052: Deploy v1.1.0 and validate all scenarios

**Constraint:** Hard restriction on plugins; minimal JavaScript is acceptable for form sync.

---

*Updated by task-execute skill as tasks progress*
