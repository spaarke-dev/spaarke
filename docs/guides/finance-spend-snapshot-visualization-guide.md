# Finance Spend Snapshot Visualization Guide

> **Created**: February 11, 2026
> **Purpose**: Comprehensive technical guide for implementing Spend Snapshot visualizations using VisualHost PCF control
> **Status**: Design Document - Implementation Pending

---

## Table of Contents

1. [What is Spend Snapshot?](#what-is-spend-snapshot)
2. [Why Spend Snapshot Exists](#why-spend-snapshot-exists)
3. [Spend Snapshot Schema](#spend-snapshot-schema)
4. [How Snapshot Generation Works](#how-snapshot-generation-works)
5. [Visualization Scenarios](#visualization-scenarios)
6. [VisualHost Integration](#visualhost-integration)
7. [BFF API Endpoints Required](#bff-api-endpoints-required)
8. [VisualHost Enhancements Needed](#visualhost-enhancements-needed)
9. [Implementation Roadmap](#implementation-roadmap)
10. [Examples and Sample Data](#examples-and-sample-data)

---

## What is Spend Snapshot?

**Spend Snapshot (`sprk_spendsnapshot`)** is a **pre-aggregated financial metrics table** that captures point-in-time spending analytics for Matters and Projects.

### Key Concepts

- **Pre-aggregated metrics** - Compute once, query many times (instead of real-time aggregation)
- **Point-in-time snapshots** - Historical data preserved (can view February 2025 data even in 2026)
- **Idempotent upserts** - Re-running snapshot jobs updates existing snapshots via alternate key
- **Fast dashboards** - No real-time aggregation needed; query pre-computed data
- **Trend analysis** - Month-over-month velocity, cumulative totals, budget variance tracking

### Think of It As

> "A financial data warehouse that stores computed metrics instead of forcing you to recalculate them every time someone views a dashboard."

---

## Why Spend Snapshot Exists

### Problem: Real-Time Aggregation is Slow

```
User Opens Dashboard
     ↓
Query 10,000 BillingEvent records
     ↓
Group by month (12 months)
     ↓
Calculate MoM velocity for each month
     ↓
Query Budget records
     ↓
Calculate variance for each month
     ↓
[3-5 seconds later...]
     ↓
Render chart
```

**User Experience**: 😞 "Why is this dashboard so slow?"

### Solution: Pre-Compute and Cache

```
Invoice Processing Job Completes
     ↓
SpendSnapshotService.GenerateAsync(matterId)
     ↓
[Compute metrics once]
     ↓
Upsert 2 SpendSnapshot records (Month + ToDate)
     ↓
[Done - 500ms]

---

User Opens Dashboard
     ↓
Query 12 SpendSnapshot records (pre-aggregated)
     ↓
[<1 second]
     ↓
Render chart
```

**User Experience**: 😊 "Wow, that loaded fast!"

### Performance Benefits

| Metric | Without Snapshot | With Snapshot |
|--------|------------------|---------------|
| **Dashboard Load** | 3-5 seconds (aggregate 10K billing events) | <500ms (query 12 snapshot records) |
| **Trend Chart** | 8 seconds (complex date grouping + MoM calc) | <1 second (pre-computed) |
| **Budget Alerts** | Real-time query (expensive) | Query snapshot table (indexed) |
| **Database Load** | High (complex aggregations on every view) | Low (simple indexed queries) |

---

## Spend Snapshot Schema

### Entity: `sprk_spendsnapshot`

| Field | Type | Purpose | Example Value |
|-------|------|---------|---------------|
| `sprk_spendsnapshotid` | Guid | Primary key | (GUID) |
| `sprk_name` | Text | Display name | "Matter ABC-123 - 2026-02 (Month)" |
| `sprk_matter` | Lookup → sprk_matter | Parent matter | Matter ID (null for Project) |
| `sprk_project` | Lookup → sprk_project | Parent project | Project ID (null for Matter) |
| `sprk_periodtype` | Choice | Month / ToDate / Quarter / Year | **Month** (100000000) |
| `sprk_periodkey` | Text | Period identifier | "2026-02" or "TO_DATE" |
| `sprk_bucketkey` | Text | Budget category | "TOTAL" (MVP) |
| `sprk_visibilityfilter` | Text | Visibility scope | "ACTUAL_INVOICED" (MVP) |
| `sprk_invoicedamount` | Currency | Total invoiced for period | $45,250.00 |
| `sprk_budgetamount` | Currency | Budget allocation | $100,000.00 |
| `sprk_budgetvariance` | Currency | Budget - Invoiced | $54,750.00 (under budget) |
| `sprk_budgetvariancepct` | Decimal | (Variance / Budget) * 100 | 54.75% |
| `sprk_velocitypct` | Decimal | MoM growth rate | 12.5% (12.5% increase) |
| `sprk_priorperiodamount` | Currency | Previous period spend | $40,000.00 |
| `sprk_priorperiodkey` | Text | Previous period identifier | "2026-01" |
| `sprk_generatedat` | DateTime | Snapshot computation time | 2026-02-11T14:30:00Z |
| `sprk_correlationid` | Text | Job chain traceability | "inv-proc-job-abc123" |

### Alternate Key (5-Field Composite)

**Key Fields**:
```
sprk_matter + sprk_project + sprk_periodtype + sprk_periodkey + sprk_generatedat
```

**Purpose**: Enables idempotent upsert operations
- Re-running snapshot generation for same period updates existing record
- Matter and Project can both be null (but at least one must be populated)

### Period Types (MVP vs. Future)

| Period Type | Choice Value | Period Key Format | MVP Status |
|-------------|--------------|-------------------|------------|
| **Month** | 100000000 | "YYYY-MM" (e.g., "2026-02") | ✅ MVP |
| **ToDate** | 100000003 | "TO_DATE" (cumulative) | ✅ MVP |
| **Quarter** | 100000001 | "YYYY-Q1" (e.g., "2026-Q1") | ⏳ Future |
| **Year** | 100000002 | "YYYY" (e.g., "2026") | ⏳ Future |

### Bucket Keys (MVP vs. Future)

| Bucket Key | Purpose | MVP Status |
|------------|---------|------------|
| **"TOTAL"** | All spending (no category breakdown) | ✅ MVP |
| "LEGAL" | Legal fees only | ⏳ Future |
| "EXPERT_WITNESS" | Expert witness fees | ⏳ Future |
| "DISCOVERY" | Discovery costs | ⏳ Future |

---

## How Snapshot Generation Works

### Trigger Flow

```
┌──────────────────────────────────────────────┐
│ Invoice Extraction Job Completes             │
│ [New invoices inserted into sprk_invoice]    │
└──────────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────────┐
│ AttachmentClassificationJobHandler           │
│ [Enqueues follow-up jobs]                    │
└──────────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────────┐
│ SpendSnapshotGenerationJob (Enqueued)        │
│ Parameters: matterId, correlationId          │
└──────────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────────┐
│ SpendSnapshotService.GenerateAsync(matterId) │
│                                               │
│ 1. Query BillingEvents for matter            │
│ 2. Group by month → aggregate amounts         │
│ 3. Query Budget records → sum totals          │
│ 4. Calculate variance & velocity              │
│ 5. Upsert SpendSnapshot records               │
└──────────────────────────────────────────────┘
                    ↓
┌──────────────────────────────────────────────┐
│ 2 SpendSnapshot Records Created/Updated      │
│ - Month snapshot ("2026-02")                  │
│ - ToDate snapshot ("TO_DATE")                 │
└──────────────────────────────────────────────┘
```

### What Gets Computed (MVP)

For each Matter, generates **2 snapshots**:

#### 1. Month Snapshot

**Period Type**: Month (100000000)
**Period Key**: "2026-02" (current month)

**Computation**:
- **Invoiced Amount**: Sum all BillingEvents for Matter where `sprk_eventdate` in February 2026
- **Budget Amount**: Sum Budget records for Matter (or get BudgetBucket for "TOTAL" + Feb period)
- **Budget Variance**: `budgetAmount - invoicedAmount`
- **Prior Period Amount**: Invoiced amount from January 2026
- **Velocity %**: `((currentAmount - priorAmount) / priorAmount) * 100`

#### 2. ToDate Snapshot (Cumulative)

**Period Type**: ToDate (100000003)
**Period Key**: "TO_DATE"

**Computation**:
- **Invoiced Amount**: Sum **ALL** BillingEvents for Matter (lifetime cumulative)
- **Budget Amount**: Sum **ALL** Budget records for Matter (total allocated budget)
- **Budget Variance**: `totalBudget - totalSpend`
- **Prior Period Amount**: Previous ToDate snapshot's invoiced amount
- **Velocity %**: Growth from last ToDate calculation to current

### Example Snapshots for Matter "ABC-123"

| Snapshot | PeriodType | PeriodKey | Invoiced | Budget | Variance | Velocity | Generated At |
|----------|------------|-----------|----------|--------|----------|----------|--------------|
| Feb Month | Month | "2026-02" | $12,500 | $10,000 | -$2,500 (over) | +15% vs Jan | 2026-02-11 14:30 |
| Lifetime | ToDate | "TO_DATE" | $45,250 | $100,000 | +$54,750 (under) | +8% vs last calc | 2026-02-11 14:30 |

### When Snapshots are Regenerated

| Trigger | Frequency | Why |
|---------|-----------|-----|
| **Invoice Processing** | After each invoice batch | New spend data available |
| **Budget Update** | When Budget records change | Budget amounts changed |
| **Nightly Job** | Daily at 2am | Ensure consistency across all matters |
| **Manual Refresh** | On-demand via API | User requests latest data |

---

## Visualization Scenarios

### Scenario A: Matter Detail Page (Individual Matter)

**Location**: Matter form in Dataverse model-driven app
**User Goal**: View financial health of one specific matter

**Visuals Needed**:

1. **Financial Summary Cards** (4 KPI cards across top)
   ```
   ┌─────────────┬─────────────┬─────────────┬─────────────┐
   │ Total Budget│ Total Spend │ Remaining   │ Utilization │
   │  $100,000   │   $45,250   │   $54,750   │    45.3%    │
   └─────────────┴─────────────┴─────────────┴─────────────┘
   ```
   - **Data Source**: Latest ToDate snapshot
   - **Query**: `WHERE sprk_matter = @matterId AND sprk_periodtype = ToDate ORDER BY sprk_generatedat DESC TOP 1`
   - **Fields**: `sprk_budgetamount`, `sprk_invoicedamount`, `sprk_budgetvariance`, calculated utilization %

2. **Monthly Spend Trend Chart** (Line chart - last 12 months)
   ```
   Monthly Spend Trend
   ─────────────────────────────────────
   $15K ─┐                    ╭─ $12.5K
        │                   ╱
   $10K ─┼──────╱────╱────╱
        │   ╱
    $5K ─┼╱──────────────────────────────
        └────────────────────────────────
        Mar  Apr  May  ...  Jan  Feb
   ```
   - **Data Source**: Month snapshots for last 12 months
   - **Query**: `WHERE sprk_matter = @matterId AND sprk_periodtype = Month AND sprk_periodkey >= '2025-03' ORDER BY sprk_periodkey`
   - **X-Axis**: `sprk_periodkey` (month labels)
   - **Y-Axis**: `sprk_invoicedamount` (spend amounts)

3. **Budget Variance Indicator** (Gauge or progress bar)
   ```
   Budget Status: Under Budget ✓
   ████████████░░░░░░░░░░ 45% used ($45K of $100K)
   ```
   - **Data Source**: Latest ToDate snapshot
   - **Field**: `sprk_budgetvariancepct` or calculate from `sprk_invoicedamount / sprk_budgetamount`

4. **Velocity Alert Badge** (Small indicator)
   ```
   ↑ +12.5% vs last month
   ```
   - **Data Source**: Latest Month snapshot
   - **Field**: `sprk_velocitypct`
   - **Conditional**: Red if >20%, Yellow if >10%, Green if <10%

---

### Scenario B: Finance Dashboard (Organization-Wide Executive View)

**Location**: Custom page or dashboard in model-driven app
**User Goal**: See portfolio-wide financial health, identify issues

**Visuals Needed**:

1. **Top Spenders This Month** (Horizontal bar chart - top 10 matters)
   ```
   Top 10 Matters by Spend (February 2026)
   ──────────────────────────────────────
   Matter ABC-123 ████████████████  $45K
   Matter XYZ-456 ████████████      $32K
   Matter DEF-789 ██████████        $28K
   Matter GHI-012 ████████          $22K
   ...
   ```
   - **Data Source**: Month snapshots for current month across all matters
   - **Query**: `WHERE sprk_periodtype = Month AND sprk_periodkey = '2026-02' ORDER BY sprk_invoicedamount DESC TOP 10`
   - **X-Axis**: `sprk_invoicedamount`
   - **Y-Axis**: Matter names (from lookup)

2. **Budget Alerts** (Data grid with conditional formatting)
   ```
   Matters Over Budget (February 2026)
   ───────────────────────────────────────
   Matter      | Spend   | Budget  | Variance | Status
   ───────────────────────────────────────────────────
   ⚠ ABC-123  | $115K   | $100K   | -15%     | ⚠ Over
   ⚠ DEF-456  | $88K    | $80K    | -10%     | ⚠ Over
   ⚠ GHI-789  | $62K    | $50K    | -24%     | ⚠ Critical
   ```
   - **Data Source**: Month snapshots where budget variance is negative
   - **Query**: `WHERE sprk_periodtype = Month AND sprk_periodkey = '2026-02' AND sprk_budgetvariance < 0 ORDER BY sprk_budgetvariancepct ASC`
   - **Conditional Formatting**: Red row if variance < -20%, Yellow if < -10%

3. **Organization Spend Trend** (Line chart - aggregate across all matters)
   ```
   Total Organization Spend Over Time (Last 12 Months)
   ──────────────────────────────────────
   $500K ─┐              ╭──╮
         │            ╱    ╰╮
   $400K ─┤         ╱       ╰─
         │      ╱
   $300K ─┼───╱─────────────────────────
         └──────────────────────────────
         Mar  Apr  May  ...  Jan  Feb
   ```
   - **Data Source**: Sum of Month snapshots grouped by period
   - **Query**: `SELECT sprk_periodkey, SUM(sprk_invoicedamount) WHERE sprk_periodtype = Month AND sprk_periodkey >= '2025-03' GROUP BY sprk_periodkey ORDER BY sprk_periodkey`
   - **X-Axis**: Period keys (months)
   - **Y-Axis**: Sum of invoiced amounts

4. **Portfolio Budget Status** (Treemap or stacked bar chart)
   ```
   Budget Utilization by Matter
   ──────────────────────────────────────
   [Matter A: 85%] [Matter B: 45%] [Matter C: 120% ⚠]
   [Matter D: 60%] [Matter E: 30%] [Matter F: 95%]
   ```
   - **Data Source**: ToDate snapshots for all active matters
   - **Query**: `WHERE sprk_periodtype = ToDate ORDER BY sprk_budgetutilizationpercent DESC`
   - **Calculated Field**: `(sprk_invoicedamount / sprk_budgetamount) * 100` for utilization %

---

## VisualHost Integration

### Current VisualHost Capabilities

**VisualHost PCF Control** supports three data source modes:

| Mode | Purpose | Use Case |
|------|---------|----------|
| **Reporting View** | Query Dataverse view | Simple lists/grids of records |
| **Single Field Value** | Display one field | KPI cards, gauges (single value) |
| **Custom JSON Query** | Complex aggregations | Charts requiring BFF API queries |

### Integration Architecture

```
┌─────────────────────────────────────────────────────────┐
│  Matter Form (Dataverse Model-Driven App)               │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │  VisualHost PCF Control (Fluent UI v2 Charts)  │    │
│  │                                                 │    │
│  │  [Config: customJsonQuery]                     │    │
│  │  endpoint: /api/finance/matters/{id}/spend-trend │  │
│  └────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
                        │
                        │ HTTP GET
                        ↓
┌─────────────────────────────────────────────────────────┐
│  BFF API (.NET 8 Minimal API)                           │
│                                                          │
│  GET /api/finance/matters/{id}/spend-trend              │
│  ↓                                                       │
│  FinanceSnapshotQueryService                            │
│  ├─ Query SpendSnapshot WHERE sprk_matter = {id}       │
│  ├─ Filter by periodtype = Month                        │
│  ├─ Order by periodkey DESC                             │
│  └─ Transform to chart-ready JSON                       │
└─────────────────────────────────────────────────────────┘
                        │
                        │ Dataverse SDK
                        ↓
┌─────────────────────────────────────────────────────────┐
│  Dataverse (sprk_spendsnapshot table)                   │
│                                                          │
│  [Pre-aggregated spend snapshot records]                │
└─────────────────────────────────────────────────────────┘
```

### VisualHost Configuration Examples

#### Example 1: KPI Card (Single Field Value)

**Scenario**: Display "Total Budget" on Matter page

**VisualHost Config**:
```json
{
  "dataSource": {
    "type": "singleFieldValue",
    "entity": "sprk_spendsnapshot",
    "filter": "sprk_matter eq @CurrentRecord AND sprk_periodtype eq 100000003",
    "field": "sprk_budgetamount",
    "aggregation": "latest"
  },
  "chartType": "kpiCard",
  "options": {
    "title": "Total Budget",
    "format": "currency",
    "icon": "Money",
    "colors": {
      "primary": "#0078D4"
    }
  }
}
```

**Rendered Output**:
```
┌───────────────┐
│ 💰 Total Budget
│   $100,000    │
└───────────────┘
```

---

#### Example 2: Line Chart (Custom JSON Query)

**Scenario**: Monthly spend trend on Matter page

**VisualHost Config**:
```json
{
  "dataSource": {
    "type": "customJsonQuery",
    "endpoint": "/api/finance/matters/{{CurrentRecord.Id}}/spend-trend",
    "parameters": {
      "months": 12
    },
    "refreshInterval": 60000
  },
  "chartType": "lineChart",
  "options": {
    "title": "Monthly Spend Trend (Last 12 Months)",
    "xAxisLabel": "Month",
    "yAxisLabel": "Spend ($)",
    "colors": ["#0078D4"],
    "showDataLabels": true,
    "showLegend": false
  }
}
```

**BFF API Response** (`GET /api/finance/matters/{id}/spend-trend?months=12`):
```json
{
  "labels": ["2025-03", "2025-04", "2025-05", "2025-06", "2025-07", "2025-08",
             "2025-09", "2025-10", "2025-11", "2025-12", "2026-01", "2026-02"],
  "datasets": [
    {
      "label": "Monthly Spend",
      "data": [8500, 9200, 12000, 11500, 13000, 10800,
               9500, 11200, 10500, 12800, 11000, 12500]
    }
  ]
}
```

**Rendered Output**: Line chart showing spend trend over 12 months

---

#### Example 3: Bar Chart (Dashboard - Top Spenders)

**Scenario**: Finance dashboard showing top 10 matters by spend

**VisualHost Config**:
```json
{
  "dataSource": {
    "type": "customJsonQuery",
    "endpoint": "/api/finance/dashboard/top-spenders",
    "parameters": {
      "month": "2026-02",
      "limit": 10
    },
    "refreshInterval": 300000
  },
  "chartType": "barChart",
  "options": {
    "title": "Top 10 Matters by Spend (February 2026)",
    "orientation": "horizontal",
    "xAxisLabel": "Spend ($)",
    "yAxisLabel": "Matter",
    "colors": ["#107C10"],
    "showDataLabels": true
  }
}
```

**BFF API Response** (`GET /api/finance/dashboard/top-spenders?month=2026-02&limit=10`):
```json
{
  "labels": ["Matter ABC-123", "Matter XYZ-456", "Matter DEF-789",
             "Matter GHI-012", "Matter JKL-345", "Matter MNO-678",
             "Matter PQR-901", "Matter STU-234", "Matter VWX-567", "Matter YZA-890"],
  "datasets": [
    {
      "label": "Spend",
      "data": [45000, 32000, 28000, 22000, 19500, 18000,
               16500, 15000, 14200, 13800]
    }
  ]
}
```

---

#### Example 4: Data Grid with Conditional Formatting (Budget Alerts)

**Scenario**: Dashboard showing matters over budget

**VisualHost Config**:
```json
{
  "dataSource": {
    "type": "customJsonQuery",
    "endpoint": "/api/finance/dashboard/budget-alerts",
    "parameters": {
      "month": "2026-02"
    },
    "refreshInterval": 300000
  },
  "chartType": "dataGrid",
  "columns": [
    {
      "field": "matterName",
      "header": "Matter",
      "width": "200px"
    },
    {
      "field": "spend",
      "header": "Spend",
      "format": "currency",
      "width": "120px"
    },
    {
      "field": "budget",
      "header": "Budget",
      "format": "currency",
      "width": "120px"
    },
    {
      "field": "variance",
      "header": "Variance",
      "format": "currency",
      "width": "120px",
      "conditionalFormatting": {
        "field": "variancePct",
        "rules": [
          { "condition": "< -20", "backgroundColor": "#C50F1F", "color": "#FFFFFF" },
          { "condition": "< -10", "backgroundColor": "#FFB900", "color": "#000000" }
        ]
      }
    },
    {
      "field": "variancePct",
      "header": "Variance %",
      "format": "percent",
      "width": "100px"
    }
  ],
  "options": {
    "title": "Matters Over Budget (February 2026)",
    "sortable": true,
    "filterable": true
  }
}
```

**BFF API Response** (`GET /api/finance/dashboard/budget-alerts?month=2026-02`):
```json
{
  "rows": [
    {
      "matterId": "guid-1",
      "matterName": "Matter ABC-123",
      "spend": 115000,
      "budget": 100000,
      "variance": -15000,
      "variancePct": -15.0
    },
    {
      "matterId": "guid-2",
      "matterName": "Matter DEF-456",
      "spend": 88000,
      "budget": 80000,
      "variance": -8000,
      "variancePct": -10.0
    },
    {
      "matterId": "guid-3",
      "matterName": "Matter GHI-789",
      "spend": 62000,
      "budget": 50000,
      "variance": -12000,
      "variancePct": -24.0
    }
  ]
}
```

---

## BFF API Endpoints Required

### Endpoints for Matter-Level Visualizations

| Endpoint | Method | Purpose | Returns |
|----------|--------|---------|---------|
| `/api/finance/matters/{id}/summary` | GET | KPI cards (4 metrics) | JSON with budget, spend, variance, utilization |
| `/api/finance/matters/{id}/spend-trend` | GET | Monthly trend chart | Chart-ready JSON (labels + datasets) |
| `/api/finance/matters/{id}/velocity` | GET | MoM velocity indicator | Single value + direction |

#### Endpoint Details: `GET /api/finance/matters/{id}/summary`

**Query Logic**:
```csharp
// Query latest ToDate snapshot
var snapshot = await QueryLatestToDateSnapshot(matterId);

return new
{
    totalBudget = snapshot.BudgetAmount,
    totalSpend = snapshot.InvoicedAmount,
    remainingBudget = snapshot.BudgetVariance,
    budgetUtilization = (snapshot.InvoicedAmount / snapshot.BudgetAmount) * 100
};
```

**Response**:
```json
{
  "totalBudget": 100000,
  "totalSpend": 45250,
  "remainingBudget": 54750,
  "budgetUtilization": 45.25
}
```

---

#### Endpoint Details: `GET /api/finance/matters/{id}/spend-trend?months=12`

**Query Logic**:
```csharp
// Query Month snapshots for last N months
var snapshots = await QueryMonthSnapshots(matterId, months: 12);

return new
{
    labels = snapshots.Select(s => s.PeriodKey).ToArray(),
    datasets = new[]
    {
        new {
            label = "Monthly Spend",
            data = snapshots.Select(s => s.InvoicedAmount).ToArray()
        }
    }
};
```

**Response**: (see Example 2 above)

---

#### Endpoint Details: `GET /api/finance/matters/{id}/velocity`

**Query Logic**:
```csharp
// Query latest Month snapshot
var snapshot = await QueryLatestMonthSnapshot(matterId);

return new
{
    velocityPct = snapshot.VelocityPct,
    direction = snapshot.VelocityPct > 0 ? "up" : "down",
    priorAmount = snapshot.PriorPeriodAmount,
    currentAmount = snapshot.InvoicedAmount
};
```

**Response**:
```json
{
  "velocityPct": 12.5,
  "direction": "up",
  "priorAmount": 11000,
  "currentAmount": 12500
}
```

---

### Endpoints for Dashboard Visualizations

| Endpoint | Method | Purpose | Returns |
|----------|--------|---------|---------|
| `/api/finance/dashboard/top-spenders` | GET | Top N matters by spend | Array of matter + spend |
| `/api/finance/dashboard/budget-alerts` | GET | Matters over budget | Array of matter + variance |
| `/api/finance/dashboard/org-trend` | GET | Organization-wide trend | Aggregated chart JSON |
| `/api/finance/dashboard/portfolio-status` | GET | Budget utilization across portfolio | Array of matter + utilization % |

#### Endpoint Details: `GET /api/finance/dashboard/top-spenders?month=2026-02&limit=10`

**Query Logic**:
```csharp
// Query Month snapshots for specified month, order by spend desc
var snapshots = await QueryMonthSnapshotsForMonth(month: "2026-02", limit: 10);

return new
{
    labels = snapshots.Select(s => s.MatterName).ToArray(),
    datasets = new[]
    {
        new {
            label = "Spend",
            data = snapshots.Select(s => s.InvoicedAmount).ToArray()
        }
    }
};
```

**Response**: (see Example 3 above)

---

#### Endpoint Details: `GET /api/finance/dashboard/budget-alerts?month=2026-02`

**Query Logic**:
```csharp
// Query Month snapshots where variance < 0 (over budget)
var snapshots = await QueryOverBudgetSnapshots(month: "2026-02");

return new
{
    rows = snapshots.Select(s => new
    {
        matterId = s.MatterId,
        matterName = s.MatterName,
        spend = s.InvoicedAmount,
        budget = s.BudgetAmount,
        variance = s.BudgetVariance,
        variancePct = s.BudgetVariancePct
    }).ToArray()
};
```

**Response**: (see Example 4 above)

---

#### Endpoint Details: `GET /api/finance/dashboard/org-trend?months=12`

**Query Logic**:
```csharp
// Query ALL Month snapshots, group by period, sum amounts
var aggregated = await QueryAndAggregateByPeriod(months: 12);

return new
{
    labels = aggregated.Select(a => a.PeriodKey).ToArray(),
    datasets = new[]
    {
        new {
            label = "Total Organization Spend",
            data = aggregated.Select(a => a.TotalSpend).ToArray()
        }
    }
};
```

**Response**:
```json
{
  "labels": ["2025-03", "2025-04", "2025-05", ..., "2026-02"],
  "datasets": [
    {
      "label": "Total Organization Spend",
      "data": [285000, 312000, 298000, ..., 345000]
    }
  ]
}
```

---

### Project-Level Endpoints (Same Pattern)

All Matter endpoints have Project equivalents:

| Endpoint | Purpose |
|----------|---------|
| `/api/finance/projects/{id}/summary` | Project KPI summary |
| `/api/finance/projects/{id}/spend-trend` | Project monthly trend |
| `/api/finance/projects/{id}/velocity` | Project MoM velocity |

**Query Logic**: Identical to Matter endpoints, but query `WHERE sprk_project = @projectId`

---

## VisualHost Enhancements Needed

### Current Gap Analysis

| Feature | Current Support | Needed For | Priority |
|---------|----------------|------------|----------|
| **Custom JSON Query** | ❓ Unknown | All chart scenarios | 🔴 Critical |
| **Dynamic Parameters** ({{CurrentRecord.Id}}) | ❓ Unknown | Matter/Project page charts | 🔴 Critical |
| **Chart Types: Line, Bar, Pie** | ✅ Likely supported | Trend charts, top spenders | ✅ Assumed OK |
| **Chart Type: Gauge** | ❓ Unknown | Budget utilization indicator | 🟡 Nice to have |
| **Chart Type: Treemap** | ❌ Likely missing | Portfolio view | 🟢 Future |
| **Conditional Formatting (Grid)** | ❓ Unknown | Budget alerts (red/yellow rows) | 🟡 Important |
| **Data Refresh Interval** | ❓ Unknown | Auto-refresh dashboards | 🟢 Nice to have |

### Recommended Enhancements

#### Enhancement 1: Custom JSON Query Support (CRITICAL)

**Requirement**: VisualHost must support calling BFF API endpoints and consuming JSON responses

**Implementation**:
```typescript
// VisualHost PCF - Add CustomJsonQueryDataSource
interface CustomJsonQueryConfig {
  endpoint: string;           // "/api/finance/matters/{id}/spend-trend"
  parameters?: Record<string, any>;  // { months: 12 }
  refreshInterval?: number;   // Auto-refresh in ms
  headers?: Record<string, string>;  // Auth headers
}

async function fetchCustomJsonData(config: CustomJsonQueryConfig): Promise<any> {
  // Replace {{CurrentRecord.Id}} with actual value
  const url = interpolateParameters(config.endpoint, config.parameters);

  // Fetch from BFF API
  const response = await fetch(url, {
    headers: config.headers
  });

  return await response.json();
}
```

---

#### Enhancement 2: Dynamic Parameter Interpolation (CRITICAL)

**Requirement**: Support `{{CurrentRecord.Id}}`, `{{CurrentUser.Id}}`, `{{Today}}` in endpoint URLs

**Implementation**:
```typescript
function interpolateParameters(endpoint: string, params: Record<string, any>): string {
  let url = endpoint;

  // Replace CurrentRecord tokens
  url = url.replace(/\{\{CurrentRecord\.Id\}\}/g, getCurrentRecordId());
  url = url.replace(/\{\{CurrentUser\.Id\}\}/g, getCurrentUserId());
  url = url.replace(/\{\{Today\}\}/g, new Date().toISOString().split('T')[0]);

  // Replace explicit parameters
  for (const [key, value] of Object.entries(params)) {
    url = url.replace(new RegExp(`\\{${key}\\}`, 'g'), encodeURIComponent(String(value)));
  }

  return url;
}
```

---

#### Enhancement 3: Gauge Chart Type (IMPORTANT)

**Requirement**: Render gauge chart for budget utilization %

**Use Case**:
```json
{
  "chartType": "gauge",
  "options": {
    "minValue": 0,
    "maxValue": 100,
    "value": 45.25,
    "ranges": [
      { "min": 0, "max": 70, "color": "#107C10" },    // Green: Under budget
      { "min": 70, "max": 90, "color": "#FFB900" },   // Yellow: Approaching limit
      { "min": 90, "max": 100, "color": "#C50F1F" }   // Red: Over budget
    ]
  }
}
```

---

#### Enhancement 4: Conditional Formatting (Data Grid) (IMPORTANT)

**Requirement**: Apply background color to grid rows based on field values

**Use Case**:
```json
{
  "chartType": "dataGrid",
  "columns": [
    {
      "field": "variance",
      "header": "Variance",
      "conditionalFormatting": {
        "field": "variancePct",
        "rules": [
          { "condition": "< -20", "backgroundColor": "#C50F1F", "color": "#FFFFFF" },
          { "condition": "< -10", "backgroundColor": "#FFB900", "color": "#000000" },
          { "condition": ">= 0", "backgroundColor": "#DFF6DD", "color": "#000000" }
        ]
      }
    }
  ]
}
```

---

#### Enhancement 5: Auto-Refresh (Nice to Have)

**Requirement**: Automatically refresh chart data at specified interval

**Implementation**:
```typescript
if (config.refreshInterval) {
  setInterval(async () => {
    const data = await fetchCustomJsonData(config);
    updateChart(data);
  }, config.refreshInterval);
}
```

---

## Implementation Roadmap

### Phase 1: Foundation (Complete ✅)

- [x] Spend Snapshot schema defined
- [x] SpendSnapshotService implementation (Matter-level)
- [x] Snapshot generation integrated into invoice processing workflow
- [x] FinancialCalculationToolHandler (Matter + Project support)

### Phase 2: BFF API Query Endpoints (NEXT)

**Priority**: 🔴 Critical
**Estimated Effort**: 2-3 days

**Tasks**:
1. Create `FinanceSnapshotQueryService` (query Spend Snapshots from Dataverse)
2. Implement Matter endpoints:
   - `GET /api/finance/matters/{id}/summary`
   - `GET /api/finance/matters/{id}/spend-trend`
   - `GET /api/finance/matters/{id}/velocity`
3. Implement Dashboard endpoints:
   - `GET /api/finance/dashboard/top-spenders`
   - `GET /api/finance/dashboard/budget-alerts`
   - `GET /api/finance/dashboard/org-trend`
4. Add authorization filters (ensure user has access to matter)
5. Add unit tests for query service

**Acceptance Criteria**:
- All endpoints return chart-ready JSON
- Endpoints respond in < 500ms (querying pre-aggregated snapshots)
- Authorization enforced (user must have access to matter)
- Unit tests cover query logic and edge cases

---

### Phase 3: VisualHost Enhancements (PARALLEL with Phase 2)

**Priority**: 🔴 Critical
**Estimated Effort**: 3-5 days

**Tasks**:
1. Add Custom JSON Query data source mode
2. Implement dynamic parameter interpolation (`{{CurrentRecord.Id}}`)
3. Test with sample BFF API endpoints (can use mock data initially)
4. Add gauge chart type (if needed)
5. Add conditional formatting for data grids (if needed)
6. Update VisualHost documentation with examples

**Acceptance Criteria**:
- VisualHost can call BFF API endpoints
- Dynamic parameters work correctly
- Charts render with real Spend Snapshot data
- Performance is acceptable (<2 second initial load)

---

### Phase 4: Matter Page Visualizations (AFTER Phase 2 + 3)

**Priority**: 🟡 High
**Estimated Effort**: 2-3 days

**Tasks**:
1. Add VisualHost instances to Matter form
   - KPI summary cards (4 cards)
   - Monthly spend trend chart
   - Budget utilization gauge
   - Velocity indicator
2. Configure each VisualHost with appropriate endpoints
3. Test with real Matter data in DEV
4. Adjust styling/layout for optimal UX

**Acceptance Criteria**:
- All 4 visualizations render on Matter page
- Data loads in < 1 second
- Charts update when Matter data changes
- Mobile-responsive layout

---

### Phase 5: Finance Dashboard (AFTER Phase 4)

**Priority**: 🟡 High
**Estimated Effort**: 3-4 days

**Tasks**:
1. Create custom page for Finance Dashboard
2. Add VisualHost instances:
   - Top spenders bar chart
   - Budget alerts grid
   - Organization trend line chart
   - Portfolio status treemap (or defer to future)
3. Add filters (month selector, matter type filter)
4. Test with multi-matter data
5. User acceptance testing

**Acceptance Criteria**:
- Dashboard loads in < 2 seconds
- All charts interactive (click to drill down to Matter)
- Filters work correctly
- Dashboard accessible from navigation

---

### Phase 6: Project-Level Support (AFTER Phase 5)

**Priority**: 🟢 Medium
**Estimated Effort**: 2-3 days

**Tasks**:
1. Complete SpendSnapshotService.GenerateForProjectAsync implementation
2. Create Project equivalents of BFF API endpoints
3. Add Project page visualizations (same as Matter)
4. Update Finance Dashboard to include Projects

**Acceptance Criteria**:
- Project snapshots generate correctly
- Project visualizations work same as Matter
- Dashboard shows combined Matter + Project data

---

### Phase 7: Enhancements (FUTURE)

**Priority**: 🟢 Low
**Estimated Effort**: TBD

**Possible Enhancements**:
- Quarter and Year period types
- Budget category breakdown (not just "TOTAL")
- Forecasting/predictive analytics
- Export to Excel functionality
- Scheduled email reports
- Mobile app views

---

## Examples and Sample Data

### Sample Spend Snapshot Records

#### Matter ABC-123 - February 2026 Month Snapshot

```json
{
  "sprk_spendsnapshotid": "guid-1",
  "sprk_name": "Matter ABC-123 - 2026-02 (Month)",
  "sprk_matter": { "id": "matter-guid-1", "name": "Matter ABC-123" },
  "sprk_project": null,
  "sprk_periodtype": 100000000,
  "sprk_periodkey": "2026-02",
  "sprk_bucketkey": "TOTAL",
  "sprk_visibilityfilter": "ACTUAL_INVOICED",
  "sprk_invoicedamount": 12500.00,
  "sprk_budgetamount": 10000.00,
  "sprk_budgetvariance": -2500.00,
  "sprk_budgetvariancepct": -25.0,
  "sprk_velocitypct": 15.0,
  "sprk_priorperiodamount": 11000.00,
  "sprk_priorperiodkey": "2026-01",
  "sprk_generatedat": "2026-02-11T14:30:00Z",
  "sprk_correlationid": "inv-proc-job-abc123"
}
```

#### Matter ABC-123 - ToDate Snapshot

```json
{
  "sprk_spendsnapshotid": "guid-2",
  "sprk_name": "Matter ABC-123 - TO_DATE (Cumulative)",
  "sprk_matter": { "id": "matter-guid-1", "name": "Matter ABC-123" },
  "sprk_project": null,
  "sprk_periodtype": 100000003,
  "sprk_periodkey": "TO_DATE",
  "sprk_bucketkey": "TOTAL",
  "sprk_visibilityfilter": "ACTUAL_INVOICED",
  "sprk_invoicedamount": 45250.00,
  "sprk_budgetamount": 100000.00,
  "sprk_budgetvariance": 54750.00,
  "sprk_budgetvariancepct": 54.75,
  "sprk_velocitypct": 8.0,
  "sprk_priorperiodamount": 42000.00,
  "sprk_priorperiodkey": null,
  "sprk_generatedat": "2026-02-11T14:30:00Z",
  "sprk_correlationid": "inv-proc-job-abc123"
}
```

---

### Sample BFF API Responses

#### GET /api/finance/matters/{id}/summary

```json
{
  "totalBudget": 100000.00,
  "totalSpend": 45250.00,
  "remainingBudget": 54750.00,
  "budgetUtilization": 45.25,
  "lastUpdated": "2026-02-11T14:30:00Z"
}
```

#### GET /api/finance/matters/{id}/spend-trend?months=12

```json
{
  "labels": [
    "2025-03", "2025-04", "2025-05", "2025-06",
    "2025-07", "2025-08", "2025-09", "2025-10",
    "2025-11", "2025-12", "2026-01", "2026-02"
  ],
  "datasets": [
    {
      "label": "Monthly Spend",
      "data": [
        8500, 9200, 12000, 11500,
        13000, 10800, 9500, 11200,
        10500, 12800, 11000, 12500
      ]
    }
  ]
}
```

#### GET /api/finance/dashboard/top-spenders?month=2026-02&limit=5

```json
{
  "month": "2026-02",
  "labels": [
    "Matter ABC-123",
    "Matter XYZ-456",
    "Matter DEF-789",
    "Matter GHI-012",
    "Matter JKL-345"
  ],
  "datasets": [
    {
      "label": "Spend",
      "data": [45000, 32000, 28000, 22000, 19500]
    }
  ]
}
```

#### GET /api/finance/dashboard/budget-alerts?month=2026-02

```json
{
  "month": "2026-02",
  "rows": [
    {
      "matterId": "guid-1",
      "matterName": "Matter ABC-123",
      "matterNumber": "ABC-123",
      "spend": 115000,
      "budget": 100000,
      "variance": -15000,
      "variancePct": -15.0,
      "severity": "warning"
    },
    {
      "matterId": "guid-2",
      "matterName": "Matter DEF-456",
      "matterNumber": "DEF-456",
      "spend": 88000,
      "budget": 80000,
      "variance": -8000,
      "variancePct": -10.0,
      "severity": "warning"
    },
    {
      "matterId": "guid-3",
      "matterName": "Matter GHI-789",
      "matterNumber": "GHI-789",
      "spend": 62000,
      "budget": 50000,
      "variance": -12000,
      "variancePct": -24.0,
      "severity": "critical"
    }
  ]
}
```

---

## Summary

### What We Built

✅ **Spend Snapshot** - Pre-aggregated financial metrics table for fast dashboard queries
✅ **Snapshot Generation** - Automated via invoice processing workflow
✅ **Matter + Project Support** - Financial calculations work for both entity types

### What We Need to Build

🔴 **BFF API Query Endpoints** - REST endpoints to serve Spend Snapshot data to VisualHost
🔴 **VisualHost Enhancements** - Custom JSON Query support, dynamic parameters, conditional formatting
🟡 **Matter Page Visualizations** - Integrate VisualHost instances into Matter form
🟡 **Finance Dashboard** - Organization-wide executive dashboard with multiple charts
🟢 **Project-Level Visualizations** - Same as Matter but for Projects

### Key Decisions Made

1. **Use Custom JSON Query mode in VisualHost** for all chart scenarios (not Reporting Views)
2. **BFF API serves chart-ready JSON** - VisualHost just renders, minimal transformation
3. **Query Spend Snapshot table** - Fast (<500ms) vs. aggregating raw BillingEvents (3-5 seconds)
4. **MVP focuses on Month + ToDate periods** - Quarter and Year deferred to future
5. **1:1 Invoice→Matter/Project model for MVP** - Line-item-level tracking deferred to future

---

**Document Status**: ✅ Complete - Ready for implementation

**Next Steps**:
1. Implement Phase 2 (BFF API endpoints)
2. Enhance VisualHost (Phase 3 in parallel)
3. Deploy visualizations to Matter pages (Phase 4)

