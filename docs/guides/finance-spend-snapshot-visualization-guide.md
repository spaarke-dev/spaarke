# Finance Spend Snapshot Visualization Guide

> **Created**: February 11, 2026
> **Last Updated**: February 15, 2026
> **Purpose**: Comprehensive technical guide for implementing Spend Snapshot visualizations using VisualHost PCF Field Pivot
> **Status**: Design Document - Implementation Pending
>
> **✅ APPROACH**: Finance uses **VisualHost Field Pivot** to display KPI cards on Matter/Project forms by reading denormalized fields. Trend charts and dashboards use **React Custom Page** querying `sprk_spendsnapshot` directly.

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

### Field Pivot: How It Works for Finance

**Field Pivot** is a VisualHost data mode that reads multiple fields from a single Dataverse record and presents each as a separate card in MetricCardMatrix.

**Perfect for Finance** because:
- ✅ SpendSnapshotGenerationJobHandler already writes summary metrics to **denormalized fields** on `sprk_matter` and `sprk_project`
- ✅ No BFF API needed - data is on the current form record
- ✅ No additional queries needed - reads fields from record already loaded by form
- ✅ Fast - no HTTP calls, no Redis cache, instant display

### Denormalized Fields on Matter/Project

After SpendSnapshotGenerationJobHandler runs, these fields are updated:

| Field | Purpose | Example Value |
|-------|---------|---------------|
| `sprk_budget` | Sum of all Budget records | $100,000 |
| `sprk_currentspend` | Lifetime invoiced amount | $45,250 |
| `sprk_budgetvariance` | Budget - Spend | $54,750 |
| `sprk_budgetutilizationpct` | (Spend / Budget) × 100 | 45.25% |
| `sprk_velocitypct` | MoM growth rate | 12.5% |
| `sprk_lastfinanceupdatedate` | Snapshot generation timestamp | 2026-02-11T14:30:00Z |

### Integration Architecture (Field Pivot)

```
┌─────────────────────────────────────────────────────────┐
│  Matter Form (Dataverse Model-Driven App)               │
│                                                          │
│  Current Record: sprk_matter                            │
│  Fields loaded: sprk_budget, sprk_currentspend, etc.    │
│                                                          │
│  ┌────────────────────────────────────────────────┐    │
│  │  VisualHost PCF Control                        │    │
│  │                                                 │    │
│  │  [Config: fieldPivot]                          │    │
│  │  Reads 5 fields from current Matter record     │    │
│  │  Displays each as a MetricCard                 │    │
│  └────────────────────────────────────────────────┘    │
│                                                          │
│  Result: 5 KPI cards (Budget, Spend, Variance, etc.)    │
└─────────────────────────────────────────────────────────┘
                        ↑
                        │ (No HTTP calls)
                        │ Reads fields via context.webAPI.retrieveRecord()
                        │
                Uses existing PCF Dataverse WebAPI
                Record is current form record (already loaded)
```

### VisualHost Configuration for Finance KPI Cards

#### Field Pivot Configuration (Matter Form)

**Chart Definition Record** (`sprk_chartdefinition`):

| Field | Value |
|-------|-------|
| `sprk_name` | "Matter Financial Summary" |
| `sprk_visualtype` | ReportCardMetric (100000010) |
| `sprk_entitylogicalname` | `sprk_matter` |
| `sprk_configurationjson` | See below |

**Configuration JSON**:
```json
{
  "fieldPivot": {
    "fields": [
      { "field": "sprk_budget", "label": "Total Budget", "sortOrder": 1 },
      { "field": "sprk_currentspend", "label": "Total Spend", "sortOrder": 2 },
      { "field": "sprk_budgetvariance", "label": "Remaining Budget", "sortOrder": 3 },
      { "field": "sprk_budgetutilizationpct", "label": "Utilization %", "sortOrder": 4 },
      { "field": "sprk_velocitypct", "label": "MoM Velocity", "sortOrder": 5 }
    ]
  },
  "columns": 5
}
```

**What This Does**:
1. VisualHost reads the current Matter record via `context.webAPI.retrieveRecord()`
2. Extracts values from 5 denormalized fields
3. Creates 5 `IAggregatedDataPoint` objects (one per field)
4. Renders 5 MetricCards in a responsive grid

**Rendered Output** (on Matter form):
```
┌─────────────┬─────────────┬─────────────┬─────────────┬─────────────┐
│ Total Budget│ Total Spend │ Remaining   │ Utilization │ MoM Velocity│
│  $100,000   │   $45,250   │   $54,750   │    45.3%    │    +12.5%   │
└─────────────┴─────────────┴─────────────┴─────────────┴─────────────┘
```

**No BFF API needed** - all data is on the current record.

---

#### Project Form Configuration (Identical Pattern)

Create second `sprk_chartdefinition` for Project entity:

| Field | Value |
|-------|-------|
| `sprk_name` | "Project Financial Summary" |
| `sprk_entitylogicalname` | `sprk_project` |
| `sprk_configurationjson` | Same fieldPivot config as Matter |

---

### Trend Charts and Dashboards: React Custom Page

**For visualizations requiring historical data** (monthly trends, organization-wide aggregations), use **React Custom Page** instead of VisualHost:

**Why React Custom Page?**
- Queries `sprk_spendsnapshot` directly (no BFF API needed)
- More flexible than VisualHost for complex dashboards
- Can use modern charting libraries (Recharts, Chart.js, Fluent UI Charts)
- No VisualHost enhancement needed

**Example Queries in React Custom Page**:

```typescript
// Monthly Spend Trend (last 12 months)
const snapshots = await dataverse.retrieveMultipleRecords(
  "sprk_spendsnapshot",
  "?$filter=_sprk_matter_value eq {matterId} and sprk_periodtype eq 100000000&$orderby=sprk_periodkey asc&$top=12"
);

// Map to chart data
const chartData = snapshots.entities.map(s => ({
  month: s.sprk_periodkey,
  spend: s.sprk_invoicedamount
}));
```

---

## Data Access Strategy

### No BFF API Endpoints Needed for KPI Cards

**Field Pivot eliminates the need for BFF API endpoints** because:
- KPI metrics are **denormalized** on `sprk_matter` and `sprk_project` by SpendSnapshotGenerationJobHandler
- VisualHost reads these fields directly from the current form record via Dataverse WebAPI
- No HTTP calls, no caching, no API authorization logic needed

**Denormalized fields updated by SpendSnapshotGenerationJobHandler**:
- `sprk_budget` (sum of all Budget records)
- `sprk_currentspend` (lifetime invoiced amount)
- `sprk_budgetvariance` (budget - spend)
- `sprk_budgetutilizationpct` ((spend / budget) × 100)
- `sprk_velocitypct` (MoM growth rate)
- `sprk_lastfinanceupdatedate` (snapshot generation timestamp)

### React Custom Page for Trend Charts and Dashboards

**For historical trends and organization-wide dashboards**, Finance will use **React Custom Page** that queries `sprk_spendsnapshot` directly:

| Visualization | Data Source | Query Method |
|---------------|-------------|--------------|
| **KPI Cards** (Matter/Project form) | Denormalized fields on current record | VisualHost Field Pivot |
| **Monthly Trend Chart** | `sprk_spendsnapshot` (Month snapshots) | React Custom Page → Dataverse WebAPI |
| **Top Spenders** (Dashboard) | `sprk_spendsnapshot` (aggregate by Matter) | React Custom Page → Dataverse WebAPI |
| **Budget Alerts** (Dashboard) | `sprk_spendsnapshot` (filter variance < 0) | React Custom Page → Dataverse WebAPI |

**Example: React Custom Page Query for Monthly Trend**:
```typescript
// Query Month snapshots for last 12 months
const result = await Xrm.WebApi.retrieveMultipleRecords(
  "sprk_spendsnapshot",
  "?$filter=_sprk_matter_value eq " + matterId +
  " and sprk_periodtype eq 100000000" +
  "&$orderby=sprk_periodkey asc&$top=12"
);

const trendData = result.entities.map(snapshot => ({
  month: snapshot.sprk_periodkey,
  spend: snapshot.sprk_invoicedamount,
  budget: snapshot.sprk_budgetamount
}));

// Render with Recharts, Chart.js, or Fluent UI Charts
```

---

## ~~BFF API Endpoints Required~~ (Obsolete - Field Pivot Used Instead)

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

## VisualHost Field Pivot Implementation

### How Field Pivot Works

**Field Pivot** reads multiple fields from a single Dataverse record and creates a card for each field. This is exactly what Finance needs for KPI cards on Matter/Project forms.

**Architecture**:
```
Current Matter Record (already loaded by form)
  ├─ sprk_budget: $100,000
  ├─ sprk_currentspend: $45,250
  ├─ sprk_budgetvariance: $54,750
  ├─ sprk_budgetutilizationpct: 45.25
  └─ sprk_velocitypct: 12.5

          ↓ (Field Pivot reads these fields)

VisualHost FieldPivotService.fetchAndPivot()
  ├─ context.webAPI.retrieveRecord("sprk_matter", matterId, "sprk_budget,sprk_currentspend,...")
  ├─ For each field in fieldPivot.fields[]:
  │    → Create IAggregatedDataPoint { label, value, fieldValue }
  └─ Return IChartData with 5 dataPoints[]

          ↓

MetricCardMatrix renders 5 cards
  [Total Budget] [Total Spend] [Remaining] [Utilization %] [MoM Velocity]
     $100,000      $45,250      $54,750        45.3%           +12.5%
```

### Implementation Status

**Matter Performance KPI Project** owns the Field Pivot enhancement (~3 hours development):
- Add `IFieldPivotConfig` interface to VisualHost
- Create `FieldPivotService.ts` (fetch record + map fields to data points)
- Wire into `VisualHostRoot.tsx` (detect `fieldPivot` in config)

**Finance Benefits** from this enhancement without additional work:
- Once deployed, Finance configures one `sprk_chartdefinition` record
- Add VisualHost PCF to Matter/Project forms pointing to chart definition
- KPI cards render automatically

### Configuration for Finance

**Chart Definition** (`sprk_chartdefinition` record):
```json
{
  "fieldPivot": {
    "fields": [
      { "field": "sprk_budget", "label": "Total Budget", "sortOrder": 1 },
      { "field": "sprk_currentspend", "label": "Total Spend", "sortOrder": 2 },
      { "field": "sprk_budgetvariance", "label": "Remaining Budget", "sortOrder": 3 },
      { "field": "sprk_budgetutilizationpct", "label": "Utilization %", "sortOrder": 4 },
      { "field": "sprk_velocitypct", "label": "MoM Velocity", "sortOrder": 5 }
    ]
  },
  "columns": 5
}
```

**Form Setup**:
1. Add VisualHost PCF to Matter form (full-width section)
2. Configure control property: `chartDefinitionId` = (GUID of chart definition)
3. VisualHost renders 5 cards in responsive grid

### No Additional Development Needed

| Component | Status | Owner |
|-----------|--------|-------|
| ✅ Denormalized fields on Matter/Project | Complete | Finance (SpendSnapshotGenerationJobHandler) |
| 🚧 Field Pivot enhancement to VisualHost | ~3 hours dev | Matter Performance KPI |
| ⏳ Chart Definition configuration | 30 min | Finance (after Field Pivot deployed) |
| ⏳ Add PCF to Matter/Project forms | 30 min | Finance (after Field Pivot deployed) |

**Total Finance Effort**: ~1 hour (configuration only, after Matter Performance deploys Field Pivot)

---

## Implementation Roadmap

### Phase 1: Foundation (Complete ✅)

- [x] Spend Snapshot schema defined
- [x] SpendSnapshotService implementation (Matter + Project support)
- [x] Snapshot generation integrated into invoice processing workflow
- [x] Denormalized fields on Matter/Project updated by SpendSnapshotGenerationJobHandler
- [x] Budget variance, MoM velocity, utilization % calculated and stored

**Status**: ✅ Complete - All backend work done

---

### Phase 2: VisualHost Field Pivot Enhancement (Matter Performance KPI Owns)

**Priority**: 🔴 Critical (blocks Finance KPI cards)
**Estimated Effort**: ~3 hours
**Owner**: Matter Performance KPI project

**Tasks**:
1. Add `IFieldPivotConfig` interface to VisualHost types
2. Create `FieldPivotService.ts` (fetch record, map fields to data points)
3. Wire into `VisualHostRoot.tsx` (detect `fieldPivot` config)
4. Version bump to 1.2.41
5. Build, package, deploy to Dataverse

**Acceptance Criteria**:
- Field Pivot mode works for any entity with multiple numeric fields
- Reads fields via `context.webAPI.retrieveRecord()`
- Returns `IAggregatedDataPoint[]` (same interface as existing modes)
- MetricCardMatrix renders cards correctly

**Finance Dependency**: Finance cannot configure KPI cards until this deploys.

**Reference**: See [`C:\code_files\spaarke-wt-matter-performance-KPI-r1\docs\architecture\visualhost-field-pivot-enhancement.md`](../../../spaarke-wt-matter-performance-KPI-r1/docs/architecture/visualhost-field-pivot-enhancement.md)

---

### Phase 3: Finance KPI Cards Configuration (AFTER Phase 2)

**Priority**: 🟡 High
**Estimated Effort**: ~1 hour
**Owner**: Finance

**Tasks**:
1. Create `sprk_chartdefinition` record for "Matter Financial Summary"
   - Set `fieldPivot` configuration (5 fields)
   - Set `columns: 5` for responsive grid
2. Add VisualHost PCF to Matter form
   - Full-width section
   - Point to chart definition GUID
3. Create `sprk_chartdefinition` record for "Project Financial Summary"
4. Add VisualHost PCF to Project form

**Acceptance Criteria**:
- 5 KPI cards render on Matter form (Budget, Spend, Variance, Utilization %, Velocity)
- 5 KPI cards render on Project form (same metrics)
- Cards load instantly (<100ms - data is on current record)
- Values update after SpendSnapshotGenerationJobHandler runs

---

### Phase 4: React Custom Page for Dashboards and Trends (AFTER Phase 3)

**Priority**: 🟡 High
**Estimated Effort**: 3-4 days
**Owner**: Finance

**Tasks**:
1. Create React Custom Page for Finance Dashboard
2. Query `sprk_spendsnapshot` directly via Dataverse WebAPI
3. Implement visualizations:
   - Monthly spend trend (LineChart - last 12 months)
   - Top 10 spenders (BarChart - current month)
   - Budget alerts grid (DataGrid - matters over budget)
   - Organization-wide trend (LineChart - aggregated)
4. Add filters (month selector, matter type)
5. Style with Fluent UI v9 components
6. Test with multi-matter data

**Acceptance Criteria**:
- Dashboard loads in < 2 seconds
- Charts render correctly with Spend Snapshot data
- Filters work (month, matter type)
- Mobile-responsive layout
- Dark mode support (ADR-021 compliance)

**Data Queries**:
```typescript
// Example: Monthly trend for specific Matter
const result = await Xrm.WebApi.retrieveMultipleRecords(
  "sprk_spendsnapshot",
  "?$filter=_sprk_matter_value eq " + matterId +
  " and sprk_periodtype eq 100000000&$orderby=sprk_periodkey asc&$top=12"
);
```

---

### Phase 5: Project-Level Support (AFTER Phase 4)

**Priority**: 🟢 Medium
**Estimated Effort**: ~30 minutes
**Owner**: Finance

**Tasks**:
1. Configure Project form KPI cards (already done in Phase 3)
2. Update React Custom Page to support Project filtering
3. Test with Project data

**Acceptance Criteria**:
- Project KPI cards work same as Matter
- Dashboard supports filtering by Matter OR Project
- Combined portfolio view shows both

**Note**: Most work already done - Phase 3 creates Project chart definition, Phase 4 dashboard just needs filter logic.

---

### Phase 6: Enhancements (FUTURE)

**Priority**: 🟢 Low
**Estimated Effort**: TBD

**Possible Enhancements**:
- Quarter and Year period types (expand PeriodType choice set)
- Budget category breakdown (expand BucketKey beyond "TOTAL")
- Forecasting/predictive analytics (ML models)
- Export to Excel functionality (Power Automate integration)
- Scheduled email reports (Power Automate scheduled flows)
- Mobile app native views (Power Apps mobile optimization)

---

## Summary of Simplified Approach

| Component | Solution | Effort | Status |
|-----------|----------|--------|--------|
| **Backend (Spend Snapshots)** | SpendSnapshotService + denormalized fields | Complete | ✅ Done |
| **KPI Cards on Forms** | VisualHost Field Pivot | ~3 hours (Matter Perf) + 1 hour (Finance config) | ⏳ Pending Field Pivot |
| **Trend Charts / Dashboards** | React Custom Page | ~3-4 days | ⏳ After Field Pivot |

**Total Finance Development**: ~4-5 days (after Field Pivot deploys)

**Key Decision**: No BFF API endpoints needed - Field Pivot reads denormalized fields, React Custom Page queries `sprk_spendsnapshot` directly.

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

### What We Built (Complete ✅)

✅ **Spend Snapshot** - Pre-aggregated financial metrics table for fast queries
✅ **Snapshot Generation** - Automated via invoice processing workflow (SpendSnapshotGenerationJobHandler)
✅ **Matter + Project Support** - Financial calculations work for both entity types
✅ **Denormalized Fields** - Budget, Spend, Variance, Utilization %, Velocity written to Matter/Project records

### What We Need to Build

🔴 **VisualHost Field Pivot** - Enhancement to VisualHost PCF (~3 hours, Matter Performance KPI owns)
🟡 **Finance KPI Cards** - Configure chart definitions and add to forms (~1 hour, Finance owns)
🟡 **React Custom Page** - Dashboard for trends and org-wide analytics (~3-4 days, Finance owns)

### Key Architecture Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **KPI Cards Data Source** | VisualHost Field Pivot (reads denormalized fields) | Data already on current record, no API needed, instant display |
| **Trend Charts / Dashboards** | React Custom Page (queries `sprk_spendsnapshot`) | More flexible than VisualHost, no API orchestration layer needed |
| **BFF API Endpoints** | None required | Field Pivot reads fields directly, React queries Dataverse directly |
| **Caching Strategy** | Denormalized fields on Matter/Project | SpendSnapshotGenerationJobHandler updates fields, no Redis cache needed |
| **Period Types (MVP)** | Month + ToDate | Quarter and Year deferred to future |
| **Bucket Keys (MVP)** | "TOTAL" only | Category breakdowns deferred to future |

### Why This Approach is Simple

**Field Pivot eliminates complexity**:
- ❌ No BFF API development needed (was estimated 2-3 days)
- ❌ No Redis caching infrastructure needed
- ❌ No API authorization logic needed
- ❌ No HTTP call overhead
- ❌ No cache invalidation complexity
- ✅ Finance already solved the hard problem (pre-aggregation in SpendSnapshotService)
- ✅ Data is where it needs to be (denormalized on Matter/Project)
- ✅ Total Finance effort: ~4-5 days (vs. 14+ days with BFF API orchestration)

---

**Document Status**: ✅ Complete - Reflects Field Pivot approach

**Next Steps**:
1. Wait for Matter Performance to deploy Field Pivot enhancement (~3 hours dev)
2. Configure Finance chart definitions (1 hour)
3. Build React Custom Page for dashboards (3-4 days)

