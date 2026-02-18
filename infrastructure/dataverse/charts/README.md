# Finance Intelligence VisualHost Chart Definitions

> **Created**: 2026-02-11
> **Task**: 042 - Configure VisualHost Chart Definitions for Finance Metrics
> **Project**: financial-intelligence-module-r1

## Overview

This directory contains chart definitions for displaying finance metrics on Matter and Project forms using VisualHost. Following the architectural pivot decision (2026-02-11), these charts consume **denormalized finance fields** added to `sprk_matter` and `sprk_project` entities rather than using custom PCF components.

## Chart Definitions

### 1. Budget Utilization Gauge

**Purpose**: Displays current budget status with color-coded visual indicator

**Data Source**: `sprk_matter` or `sprk_project` entity (denormalized fields)

**Fields Used**:
- `sprk_budget` (Money) — Total budget amount
- `sprk_currentspend` (Money) — Amount spent to date
- `sprk_budgetvariance` (Money) — Difference (budget - spend)
- `sprk_budgetutilizationpct` (Decimal) — Percentage of budget utilized

**Color Thresholds**:
- 🟢 **Green** (0-79%): Under budget
- 🟡 **Yellow** (80-100%): Warning - approaching budget limit
- 🔴 **Red** (101%+): Over budget

**Empty State**: Displays "No budget configured" when `sprk_budget` is null

**Files**:
- `budget-utilization-gauge.xml` — Standard Dataverse chart XML format
- `budget-utilization-gauge.json` — VisualHost JSON configuration (if applicable)

---

### 2. Monthly Spend Timeline

**Purpose**: Displays monthly spending trend over the last 12 months

**Data Source**: `sprk_financespendsnapshot` entity (related to matter/project)

**Fields Used**:
- `sprk_snapshotdate` (DateTime) — Month grouping for x-axis
- `sprk_totalspend` (Money) — Aggregated spend amount for y-axis
- `sprk_regardingid` (Lookup) — Relationship to parent matter/project

**Filter Logic**:
- Period type = Month (100000000)
- Last 12 months of data
- Ordered by snapshot date ascending

**Empty State**: Displays "No spend data available" when no snapshots exist

**Files**:
- `monthly-spend-timeline.xml` — Standard Dataverse chart XML format
- `monthly-spend-timeline.json` — VisualHost JSON configuration (if applicable)

---

## Deployment Instructions

### Prerequisites

1. **Denormalized fields deployed** (Task 002):
   - `sprk_budget`, `sprk_currentspend`, `sprk_budgetvariance` on `sprk_matter` and `sprk_project`
   - `sprk_budgetutilizationpct`, `sprk_velocitypct`, `sprk_lastfinanceupdatedate`

2. **Snapshot generation operational** (Task 019):
   - `SpendSnapshotGenerationJobHandler` running
   - `sprk_financespendsnapshot` entity created and populated
   - Parent entity field updates implemented

3. **VisualHost platform**:
   - VisualHost configured in Spaarke environment
   - Access to VisualHost administration/configuration

### Deployment Steps

#### Option A: Standard Dataverse Chart Import (Using XML)

1. **Import via Solution**:
   ```bash
   # Add chart definitions to solution
   pac solution add-chart \
     --path infrastructure/dataverse/charts/budget-utilization-gauge.xml \
     --solution-name SpaarkeFinanceIntelligence

   pac solution add-chart \
     --path infrastructure/dataverse/charts/monthly-spend-timeline.xml \
     --solution-name SpaarkeFinanceIntelligence

   # Export and import solution to target environment
   pac solution export --name SpaarkeFinanceIntelligence
   pac solution import --path SpaarkeFinanceIntelligence.zip
   ```

2. **Add Charts to Form**:
   - Open Matter form in Form Designer
   - Add a new section: "Finance Intelligence"
   - Insert chart controls for:
     - Budget Utilization Gauge (select from available charts)
     - Monthly Spend Timeline (select from available charts)
   - Save and publish form

3. **Configure Chart Filtering**:
   - Set chart to filter by current record context (matterId)
   - Ensure relationship path is correct for spend snapshots

#### Option B: VisualHost JSON Configuration (If Applicable)

1. **Upload JSON Definitions**:
   - Navigate to VisualHost configuration UI
   - Upload `budget-utilization-gauge.json`
   - Upload `monthly-spend-timeline.json`
   - Configure chart zones on Matter/Project forms

2. **Map Data Sources**:
   - Verify entity and field mappings
   - Test data retrieval with sample records
   - Confirm color thresholds render correctly

3. **Configure Form Layout**:
   - Add VisualHost zones to Matter form
   - Assign Budget Gauge to zone 1
   - Assign Spend Timeline to zone 2
   - Set responsive layout properties

#### Option C: Manual Configuration via Power Apps UI

1. **Create New Chart (Budget Gauge)**:
   - Navigate to Power Apps → Dataverse → Charts
   - Select `sprk_matter` entity
   - Create new chart: "Budget Utilization Gauge"
   - Chart type: Gauge or Funnel (radial gauge if available)
   - Add fields: `sprk_budget`, `sprk_currentspend`, `sprk_budgetutilizationpct`
   - Configure color rules based on utilization percentage
   - Save chart

2. **Create New Chart (Spend Timeline)**:
   - Select `sprk_financespendsnapshot` entity
   - Create new chart: "Monthly Spend Timeline"
   - Chart type: Line or Column
   - X-axis: `sprk_snapshotdate` (group by month)
   - Y-axis: `sprk_totalspend` (sum aggregation)
   - Filter: `sprk_periodtype` = Month, last 12 months
   - Save chart

3. **Add to Matter Form**:
   - Open Matter form editor
   - Insert new section: "Finance Intelligence"
   - Add chart components
   - Configure data binding to current record
   - Publish customizations

---

## Validation

After deployment, verify:

### Budget Utilization Gauge

- [ ] Chart renders on Matter form
- [ ] Displays correct budget total from `sprk_budget`
- [ ] Shows current spend from `sprk_currentspend`
- [ ] Variance and percentage calculated correctly
- [ ] Color coding works:
  - Green when utilization < 80%
  - Yellow when utilization 80-100%
  - Red when utilization > 100%
- [ ] Empty state displays when budget is null

### Monthly Spend Timeline

- [ ] Chart renders on Matter form
- [ ] Shows last 12 months of data
- [ ] X-axis displays month labels (MMM yyyy format)
- [ ] Y-axis displays currency amounts
- [ ] Line/bars render correctly with data points
- [ ] Empty state displays when no snapshots exist
- [ ] Updates when new snapshots are generated

### Integration

- [ ] Charts update when `SpendSnapshotGenerationJobHandler` runs
- [ ] Finance summary endpoint data matches chart display
- [ ] Charts respect security roles (users see only authorized matters)
- [ ] Performance acceptable (charts load in < 2 seconds)

---

## Data Flow

```
SpendSnapshotGenerationJobHandler (Task 019)
  ↓
Updates denormalized fields on sprk_matter/sprk_project:
  - sprk_budget (from active BudgetPlan)
  - sprk_currentspend (aggregated from BillingEvents)
  - sprk_budgetvariance (budget - currentspend)
  - sprk_budgetutilizationpct ((currentspend / budget) * 100)
  - sprk_velocitypct (month-over-month change)
  - sprk_lastfinanceupdatedate (timestamp)
  ↓
Creates sprk_financespendsnapshot records:
  - sprk_snapshotdate (period date)
  - sprk_totalspend (aggregated amount)
  - sprk_regardingid (lookup to matter/project)
  - sprk_periodtype (Month = 100000000)
  ↓
VisualHost Charts Query Data:
  - Budget Gauge: reads denormalized fields from parent entity
  - Spend Timeline: reads related snapshot records via lookup
  ↓
Charts Render on Form:
  - Real-time query on form load
  - Refresh when form is reloaded
  - Color thresholds applied client-side
```

---

## Troubleshooting

### Chart Not Showing Data

1. **Check denormalized fields populated**:
   ```sql
   -- Query matter to verify fields have values
   SELECT sprk_budget, sprk_currentspend, sprk_budgetutilizationpct, sprk_lastfinanceupdatedate
   FROM sprk_matter
   WHERE sprk_matterid = '{matterId}'
   ```

2. **Verify snapshots exist**:
   ```sql
   -- Query snapshots for matter
   SELECT sprk_snapshotdate, sprk_totalspend, sprk_periodtype
   FROM sprk_financespendsnapshot
   WHERE sprk_regardingid = '{matterId}'
   ORDER BY sprk_snapshotdate DESC
   ```

3. **Check SpendSnapshotGenerationJobHandler**:
   - Verify job ran successfully
   - Check job logs for errors
   - Ensure UpdateAsync calls succeeded (Task 049)

### Color Coding Not Working

1. Verify `sprk_budgetutilizationpct` field populated correctly
2. Check chart color rule thresholds in configuration
3. Ensure Fluent UI semantic tokens available (if using VisualHost)

### Performance Issues

1. Add indexes on:
   - `sprk_regardingid` (on sprk_financespendsnapshot)
   - `sprk_snapshotdate` (on sprk_financespendsnapshot)
   - `sprk_periodtype` (on sprk_financespendsnapshot)

2. Limit date range to last 12 months (already configured in filter)

3. Use aggregated views if available

---

## Future Enhancements

Post-MVP improvements for chart definitions:

1. **Quarter/Year Timeline**:
   - Add chart for quarterly/yearly trends
   - Implement QoQ/YoY velocity visualization

2. **Budget Bucket Breakdown**:
   - Pie chart showing spend by budget bucket
   - Stacked bar chart for multi-bucket comparison

3. **Vendor Spend Distribution**:
   - Chart showing top vendors by spend amount
   - Filter timeline by selected vendor

4. **Signal Overlays**:
   - Annotate timeline with signal events (budget warnings, velocity spikes)
   - Visual markers for anomaly detection

5. **Forecast Projection**:
   - Linear regression trend line
   - Projected month-end spend estimate

---

## Related Tasks

- **Task 002**: Dataverse schema fields (denormalized finance fields)
- **Task 019**: SpendSnapshotGenerationJobHandler (populates data)
- **Task 040**: Finance summary endpoint (Redis-cached API)
- **Task 049**: Extend IDataverseService (enables parent entity updates)

---

## References

- **Spec**: `projects/financial-intelligence-module-r1/spec.md`
- **Architectural Pivot Decision**: `current-task.md` (2026-02-11 session notes)
- **Dataverse Chart Documentation**: [Power Apps Chart XML Schema](https://docs.microsoft.com/en-us/powerapps/developer/data-platform/customize-visualizations-dashboards)
- **VisualHost Documentation**: (Internal Spaarke platform docs)

---

*Last Updated: 2026-02-11*
