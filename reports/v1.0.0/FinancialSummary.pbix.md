# FinancialSummary.pbix — Build Instructions

> **Report Name**: Financial Summary
> **File**: `reports/v1.0.0/FinancialSummary.pbix`
> **Category**: Financial (sprk_category = 1)
> **sprk_name value**: `Financial Summary`
> **Status**: PLACEHOLDER — requires Power BI Desktop to create
> **Created**: 2026-03-31

---

## Purpose

Provides financial KPIs and trend analysis for matters — including revenue, costs, budget vs. actual,
and forecasting. Primary audience is finance managers and senior partners reviewing portfolio economics.

---

## Data Source

**Connection Type**: Dataverse (Import mode)
**Environment URL**: Configured per deployment — do NOT hardcode

### Dataverse Tables to Import

| Table Logical Name | Display Name | Purpose |
|-------------------|--------------|---------|
| `sprk_matter` | Matter | Primary entity with financial fields |
| `sprk_timentry` or `sprk_timeentry` | Time Entry | Billable hours / costs (if exists) |
| `businessunit` | Business Unit | BU hierarchy for RLS filtering |
| `systemuser` | User | Owner / attorney lookup |

> Adapt table names to match the actual Spaarke schema. Some financial fields may be on
> `sprk_matter` directly (e.g., `sprk_budget`, `sprk_billedamount`) or in a related entity.

### Key Columns to Include

From `sprk_matter`:
- `sprk_name` — Matter name
- `sprk_budget` — Agreed budget amount (currency)
- `sprk_billedamount` — Amount billed to date
- `sprk_estimatedvalue` — Estimated total value
- `sprk_opendate` — Matter open date
- `ownerid` — Responsible attorney
- `businessunitid` — Business unit

---

## Relationships

- `sprk_matter[ownerid]` → `systemuser[systemuserid]`
- `sprk_matter[businessunitid]` → `businessunit[businessunitid]`
- `systemuser[businessunitid]` → `businessunit[businessunitid]`

---

## Calculated Measures (DAX)

Define these measures in the Power BI data model:

```dax
-- Budget Utilization %
Budget Utilization % =
    DIVIDE(SUM(sprk_matter[sprk_billedamount]), SUM(sprk_matter[sprk_budget]), 0) * 100

-- Budget Remaining
Budget Remaining =
    SUM(sprk_matter[sprk_budget]) - SUM(sprk_matter[sprk_billedamount])

-- Revenue (YTD)
Revenue YTD =
    CALCULATE(
        SUM(sprk_matter[sprk_billedamount]),
        DATESYTD(sprk_matter[sprk_opendate])
    )

-- Matters Over Budget
Matters Over Budget =
    COUNTROWS(
        FILTER(sprk_matter,
            sprk_matter[sprk_billedamount] > sprk_matter[sprk_budget]
                && NOT(ISBLANK(sprk_matter[sprk_budget]))
        )
    )
```

---

## BU RLS Role

| Role Name | Table | DAX Filter |
|-----------|-------|------------|
| `BusinessUnitFilter` | `systemuser` | `[domainname] = USERNAME()` |

Filters financial data to the current user's business unit hierarchy.

---

## Visualizations

### Page 1: Financial KPIs

| Visual | Type | Data |
|--------|------|------|
| Total Revenue (YTD) | KPI card | Revenue YTD vs prior year |
| Average Budget Utilization | Gauge | Budget Utilization % (0–100%) |
| Budget Remaining | Card | Budget Remaining (summed) |
| Matters Over Budget | Card | Count of over-budget matters |

### Page 2: Revenue Trends

| Visual | Type | Data |
|--------|------|------|
| Revenue Over Time | Area chart | Monthly billed amount trend |
| Budget vs Actual by Matter | Clustered bar | sprk_budget vs sprk_billedamount per matter |
| Top 10 Matters by Revenue | Bar chart | Top matters sorted by billed amount |

### Page 3: BU Breakdown

| Visual | Type | Data |
|--------|------|------|
| Revenue by Business Unit | Tree map | Total billed per BU |
| Budget Utilization by BU | Bar chart | Avg utilization % per BU |
| Slicers | Dropdown | Date range, business unit, matter type |

---

## Formatting

- **Canvas background**: Transparent (100% transparency)
- **Colors**:
  - Revenue / budget: Spaarke Blue (`#2563EB`)
  - Under budget (positive): Spaarke Teal (`#0D9488`)
  - Over budget (alert): Spaarke Red (`#DC2626`)
  - At-risk (approaching limit): Spaarke Amber (`#F59E0B`)
- **Report title**: "Financial Summary" — Spaarke Navy (`#1E3A5F`), 20pt Segoe UI Semibold
- **Currency format**: Use the environment's default currency symbol; format as `#,##0.00`

---

## Dataverse Record (After Publishing)

| Field | Value |
|-------|-------|
| `sprk_name` | `Financial Summary` |
| `sprk_category` | `1` (Financial) |
| `sprk_iscustom` | `false` |
| `sprk_pbi_reportid` | `<GUID from PBI Service URL>` |
| `sprk_workspaceid` | `<workspace GUID>` |
| `sprk_datasetid` | `<dataset GUID>` |
| `sprk_description` | `Financial KPIs, budget utilization, and revenue trends by matter and business unit.` |

---

*Spaarke Reporting Module R1 | Project: spaarke-powerbi-embedded-r1 | Created: 2026-03-31*
