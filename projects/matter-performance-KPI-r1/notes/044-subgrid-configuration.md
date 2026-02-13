# KPI Assessments Subgrid Configuration

> **Task**: 044
> **Project**: matter-performance-KPI-r1
> **Date**: 2026-02-12

## Subgrid Details

| Setting | Value |
|---------|-------|
| **Control ID** | subgrid_kpiassessments |
| **Target Entity** | sprk_kpiassessment |
| **Relationship** | sprk_matter_kpiassessments (N:1 via sprk_matter lookup) |
| **Records Per Page** | 10 |
| **Quick Find** | Enabled |
| **View Picker** | Disabled (uses default view) |

## Required Columns (Default View)

These columns must be configured in the default view for sprk_kpiassessment:

| Column | Logical Name | Type | Width |
|--------|-------------|------|-------|
| Performance Area | sprk_performancearea | Choice | 150px |
| KPI Name | sprk_kpiname | Text | 200px |
| Grade | sprk_grade | Choice | 100px |
| Created On | createdon | DateTime | 150px |
| Assessment Notes | sprk_assessmentnotes | Multiline Text | 250px (preview) |

## Sort Order

- Primary: **Created On** (createdon) -- **Descending** (most recent first)

## Deployment Steps

1. Add Report Card tab to matter main form in Dataverse
2. Configure the subgrid control on the tab
3. Create/update the default view for sprk_kpiassessment with the columns above
4. Set sort order on the view
5. Publish customizations

## Tab Layout

```
Report Card Tab
+-- Performance Trends (3-column section)
|   +-- Guidelines Trend [VisualHost] (Task 043)
|   +-- Budget Trend [VisualHost] (Task 043)
|   +-- Outcomes Trend [VisualHost] (Task 043)
|
+-- KPI Assessments (1-column section)
    +-- subgrid_kpiassessments
        +-- Performance Area
        +-- KPI Name
        +-- Grade
        +-- Created On
        +-- Assessment Notes (preview)
```

## Notes

- The subgrid uses the standard Dataverse subgrid control (classid E7A81278-8635-4D9E-8D4D-59480B391C5B)
- ViewId is set to all zeros -- Dataverse will use the default active view
- The relationship name `sprk_matter_kpiassessments` must match the actual 1:N relationship created in Task 001
- Trend cards section is a placeholder -- actual VisualHost controls will be configured in Task 043
