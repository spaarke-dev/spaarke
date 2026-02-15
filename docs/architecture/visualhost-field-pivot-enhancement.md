# VisualHost Field Pivot Enhancement

> **Version**: 1.0
> **Created**: 2026-02-15
> **Status**: Approved — Implementation Ready
> **Scope**: VisualHost PCF enhancement (generic, reusable)

---

## Summary

Add a **field pivot** data mode to VisualHost that reads multiple fields from a single Dataverse record and presents each as a separate card in MetricCardMatrix. This is a generic capability — not KPI-specific — for any scenario where multiple numeric fields on one record should display as a card row.

**Problem**: Three separate VisualHost PCFs in a 3-column Dataverse form section fail to fill their allocated column width due to platform CSS constraints. No amount of CSS from inside the PCF can override the `div.pa-cb.flexbox` responsive stacking behavior.

**Solution**: One VisualHost PCF that internally renders multiple cards from multiple fields on the current record, using MetricCardMatrix's own responsive CSS Grid.

---

## How It Works

### Current Flow (Grouped Records → Multiple Cards)

```
Fetch MANY records → Group by field → Aggregate per group → N data points → N cards
```

### New Flow (Single Record → Multiple Cards)

```
Fetch ONE record → Read N configured fields → Map each to a data point → N cards
```

The field pivot produces `IAggregatedDataPoint[]` — the same shape as the existing grouped aggregation. Everything downstream (MetricCardMatrix, cardConfigResolver, ReportCardMetric preset) is unchanged.

### Data Flow

```
Chart Definition loaded
  │
  ├─ configurationJson has "fieldPivot"?
  │    │
  │    YES → FieldPivotService.fetchAndPivot()
  │    │      1. Get current record ID from PCF context
  │    │      2. Retrieve record via context.webAPI.retrieveRecord()
  │    │      3. For each field in fieldPivot.fields[]:
  │    │           → Create IAggregatedDataPoint { label, value, fieldValue }
  │    │      4. Return IChartData
  │    │
  │    NO  → Existing fetchAndAggregate() (VIEW or BASIC mode, unchanged)
  │
  ├─ resolveCardConfig()  ← Unchanged
  │
  └─ MetricCardMatrix     ← Unchanged (receives same dataPoints[])
```

---

## Configuration

### configurationJson.fieldPivot

```typescript
interface IFieldPivotConfig {
  fields: IFieldPivotEntry[];
}

interface IFieldPivotEntry {
  field: string;         // Dataverse field logical name (e.g., "sprk_budgetcompliancegrade_current")
  label: string;         // Display label (e.g., "Budget")
  fieldValue?: unknown;  // Value for icon/color resolution (e.g., 2 for option set mapping)
  sortOrder?: number;    // Explicit sort order (default: array index)
}
```

### Example: KPI Performance Grades

```json
{
  "fieldPivot": {
    "fields": [
      { "field": "sprk_guidelinescompliancegrade_current", "label": "Guidelines", "fieldValue": 1, "sortOrder": 1 },
      { "field": "sprk_budgetcompliancegrade_current",     "label": "Budget",     "fieldValue": 2, "sortOrder": 2 },
      { "field": "sprk_outcomescompliancegrade_current",   "label": "Outcomes",   "fieldValue": 3, "sortOrder": 3 }
    ]
  },
  "columns": 3
}
```

### Example: Financial Summary (Future)

```json
{
  "fieldPivot": {
    "fields": [
      { "field": "sprk_totalbudget",        "label": "Total Budget",  "fieldValue": "budget" },
      { "field": "sprk_totalspend",         "label": "Total Spend",   "fieldValue": "spend" },
      { "field": "sprk_remainingbudget",    "label": "Remaining",     "fieldValue": "remaining" },
      { "field": "sprk_budgetutilization",  "label": "Utilization %", "fieldValue": "utilization" }
    ]
  },
  "columns": 4
}
```

### Example: Project Health Indicators

```json
{
  "fieldPivot": {
    "fields": [
      { "field": "sprk_schedulehealth",  "label": "Schedule",  "fieldValue": 1 },
      { "field": "sprk_budgethealth",    "label": "Budget",    "fieldValue": 2 },
      { "field": "sprk_qualityhealth",   "label": "Quality",   "fieldValue": 3 },
      { "field": "sprk_riskexposure",    "label": "Risk",      "fieldValue": 4 }
    ]
  }
}
```

The pattern works for any entity with multiple numeric fields that should display as a card row.

---

## Chart Definition Record Setup

For KPI Performance Grades, create one `sprk_chartdefinition` record:

| Field | Value |
|-------|-------|
| `sprk_name` | "Matter Performance Scorecard" |
| `sprk_visualtype` | ReportCardMetric (100000010) |
| `sprk_entitylogicalname` | `sprk_matter` |
| `sprk_configurationjson` | See example above |

The `ReportCardMetric` preset auto-applies:
- `valueFormat: "letterGrade"` (0.85 → "B+")
- `colorSource: "valueThreshold"` (blue ≥0.85, yellow ≥0.70, red <0.70)
- `iconMap`: Guidelines→Gavel, Budget→Money, Outcomes→Target
- `showAccentBar: true`

**Form layout**: Single VisualHost PCF in a full-width (1-column) section. MetricCardMatrix handles the 3-column grid internally.

---

## Components

### Unchanged

| Component | File | Why Unchanged |
|-----------|------|---------------|
| MetricCardMatrix | `components/MetricCardMatrix.tsx` | Receives same `IAggregatedDataPoint[]` |
| MetricCard | `components/MetricCard.tsx` | No change to card rendering |
| ChartRenderer | `components/ChartRenderer.tsx` | Routes to MetricCardMatrix as before |
| cardConfigResolver | `utils/cardConfigResolver.ts` | 3-tier resolution + ReportCardMetric preset |
| DataAggregationService | `services/DataAggregationService.ts` | VIEW/BASIC modes untouched |
| ConfigurationLoader | `services/ConfigurationLoader.ts` | Loads chart definition as before |
| index.ts | `control/index.ts` | PCF lifecycle unchanged |

### New

| Component | File | Purpose | Est. Lines |
|-----------|------|---------|------------|
| **FieldPivotService** | `services/FieldPivotService.ts` | Generic: fetch one record, pivot N fields → N data points | ~80 |
| **IFieldPivotConfig** | `types/index.ts` | Type definitions for fieldPivot configuration | ~15 |

### Modified

| Component | File | Change | Est. Lines Changed |
|-----------|------|--------|-------------------|
| **VisualHostRoot** | `components/VisualHostRoot.tsx` | Add fieldPivot check before fetchAndAggregate | ~10 |

---

## Implementation Tasks

| # | Task | Est. |
|---|------|------|
| 1 | Add `IFieldPivotConfig` and `IFieldPivotEntry` types to `types/index.ts` | 15 min |
| 2 | Create `services/FieldPivotService.ts` — generic record fetch + field-to-dataPoint mapping | 1.5 hr |
| 3 | Wire into `VisualHostRoot.tsx` — detect `fieldPivot` in configurationJson, call FieldPivotService | 30 min |
| 4 | Version bump to 1.2.41 (5 locations) | 15 min |
| 5 | Build and package | 15 min |
| 6 | Update VISUALHOST-ARCHITECTURE.md and VISUALHOST-SETUP-GUIDE.md | 30 min |
| **Total** | | **~3 hours** |

---

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Where does pivot logic live? | New `FieldPivotService.ts` | Clean separation from existing DataAggregationService |
| How to fetch the record? | `context.webAPI.retrieveRecord()` | Uses existing PCF Dataverse WebAPI — no HTTP client, no auth tokens, no CORS |
| How to identify the record? | `context.page.entityId` (current form record) | PCF is on the entity form — the record is the one being viewed |
| How to identify the entity? | `sprk_entitylogicalname` from chart definition | Already exists on IChartDefinition |
| KPI-specific logic in pivot service? | None — fully configuration-driven | Labels, fields, fieldValues all come from configurationJson |
| What triggers field pivot mode? | `configurationJson.fieldPivot` exists | Simple presence check — no new entity fields or PCF properties needed |

---

## Superseded Document

This document supersedes [visualhost-custom-json-query-enhancement.md](visualhost-custom-json-query-enhancement.md) which proposed a Web API HTTP client approach. That approach was rejected as over-engineering — VisualHost should remain focused on Dataverse data presentation.

---

**Last Updated**: 2026-02-15
