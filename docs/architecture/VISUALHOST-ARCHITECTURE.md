# VisualHost - Architecture Documentation

> **Version**: 1.3.0 | **Last Updated**: March 10, 2026
>
> **Audience**: Developers, solution architects, AI coding agents
>
> **Purpose**: Architecture decisions, data source modes, and design principles for the VisualHost visualization framework

> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified (accurate)
>
> Verified against `src/client/pcf/VisualHost/control/`: services (`ConfigurationLoader`, `DataAggregationService`, `FieldPivotService`, `ViewDataService`, `ClickActionHandler`) all present; components (`VisualHostRoot`, `ChartRenderer`, `MetricCard`, `MetricCardMatrix`, `GaugeVisual`, `HorizontalStackedBar`, `BarChart`, `LineChart`, `DonutChart`, `StatusDistributionBar`, `CalendarVisual`, `MiniTable`, `TrendCard`, `DueDateCard`, `DueDateCardList`, `EventDueDateCard`) all present; `utils/cardConfigResolver.ts` present. Finance services referenced by drill-through (`src/server/api/Sprk.Bff.Api/Services/Finance/`) verified restored (FinanceRollupService, FinanceSummaryService, InvoiceAnalysisService, InvoiceReviewService, InvoiceSearchService, SignalEvaluationService, SpendSnapshotService). `GradeMetricCard.tsx` still exists in components folder but is correctly marked DEPRECATED in this document.

---

## Architecture Overview

VisualHost is a **configuration-driven visualization framework** for Dataverse model-driven apps. A single PCF control renders 13 different visual types based on a `sprk_chartdefinition` entity record (12 base types + ReportCardMetric as a MetricCard preset). It supports three data source modes: **view/basic aggregation** (grouped records), **field pivot** (multiple fields from a single record), and **self-managed** (DueDateCard types). It also provides **drill-through navigation** that opens web resource-based dataset grids in Dataverse dialogs with full context filtering.

```
                    ┌──────────────────────────────┐
                    │       Dataverse Form          │
                    │  ┌────────────────────────┐   │
                    │  │   VisualHost PCF        │   │
                    │  └────────┬───────────────┘   │
                    └───────────┼───────────────────┘
                    ┌───────────▼───────────────────┐
                    │     VisualHostRoot.tsx         │
                    │  (Orchestration Component)     │
                    └───┬───────┬───────┬──────────┘
                        │       │       │
            Config Loader    Data Layer    Click Action Handler
                                  │
              Aggregation Service / FieldPivotService / ViewDataService
                                  │
                    ChartRenderer (visual type router)
                    → 13 visual types + drill-through dialog
```

---

## Design Principles

1. **Configuration over Code** — No code changes needed to add a new visual instance; create a `sprk_chartdefinition` record
2. **Single Control, Multiple Visuals** — One PCF control handles all visual types, reducing solution complexity
3. **Layered Data Fetching** — Three data modes with 4-tier query priority resolution and caching at each layer
4. **Shared Components** — Visual components in `@spaarke/ui-components` are reusable across PCF and Custom Pages
5. **Drill-Through to Dataset Grids** — Expand button opens web resource-based grids in Dataverse dialogs with context filtering

---

## Data Source Modes

### Mode 1: View/Basic Aggregation

Used for all chart types (MetricCard, BarChart, LineChart, DonutChart, etc.). DataAggregationService fetches entity records via FetchXML and aggregates them:

- **With viewId**: retrieves saved view's FetchXML, injects required chart attributes (groupByField, aggregationField) if missing, injects context filter, executes query
- **Without viewId**: builds basic FetchXML with `<attribute>` elements and context filter
- Uses `@OData.Community.Display.V1.FormattedValue` annotations for human-readable labels on lookup and choice fields
- Captures option set hex colors from `@OData.Community.Display.V1.Color` annotation
- Supported aggregation types: Count (default), Sum, Average, Min, Max

### Mode 2: Field Pivot

Reads multiple fields from a **single Dataverse record** and transforms each field into a data point. Configured via `"fieldPivot"` in `sprk_optionsjson`:

```json
{
  "fieldPivot": {
    "fields": [
      { "field": "sprk_some_field", "label": "Display Label", "sortOrder": 1, "valueFormat": "currency" },
      { "field": "sprk_score", "label": "Score", "totalField": "sprk_totalpossible", "valueFormat": "percentage" }
    ]
  }
}
```

VisualHostRoot checks for `configurationJson.fieldPivot` before the aggregation path. If present with `entityLogicalName` and `contextRecordId` available, field pivot mode is used. Output shape is identical to aggregation mode — same IChartData flows to ChartRenderer.

**`totalField`** (v1.3.0): Paired denominator field for ratio-mode gauges. When present, the gauge displays `field / totalField` as a ratio.

### Mode 3: Self-Managed (DueDateCard types)

DueDateCard and DueDateCardList build their own FetchXML with a link-entity for event type, filter by contextRecordId, and manage their own data fetching. They skip the aggregation pipeline entirely.

---

## Visual Types

13 visual types rendered by ChartRenderer via `sprk_visualtype` enum:

| Type | Notes |
|------|-------|
| MetricCard | Single value display with configurable format, icon, color |
| MetricCardMatrix | Responsive CSS Grid of MetricCards with container queries |
| ReportCardMetric | **Fallthrough case** — same code path as MetricCard; auto-applies grade preset (letterGrade format, valueThreshold color, grade icons) |
| BarChart | Via @fluentui/react-charting |
| LineChart / area | Via @fluentui/react-charting |
| DonutChart | Via @fluentui/react-charting |
| StatusDistributionBar | Colored status bar |
| CalendarVisual | Calendar heat map |
| MiniTable | Compact ranked table |
| TrendCard | Trend sparkline |
| DueDateCard | Single event card (self-managed data) |
| DueDateCardList | Event card list (self-managed data) |
| GaugeVisual (v1.3.0) | SVG semicircular arc with color thresholds. Two modes: `"single"` (0–1 field value) and `"ratio"` (field / totalField) |
| HorizontalStackedBar (v1.3.0) | Financial progress bar: `dataPoints[0]`=spent, `dataPoints[1]`=budget, remaining computed automatically |

**GradeMetricCard** — DEPRECATED. Use MetricCard with `sprk_visualtype = ReportCardMetric`.

---

## Key Design Decisions

### Why ReportCardMetric as a Fallthrough Case?

Rather than a separate component, ReportCardMetric falls through to MetricCard and auto-applies grade preset defaults via `cardConfigResolver`. This pattern allows domain-specific card configurations without component proliferation. New domain presets can follow the same pattern — add a fallthrough case and preset logic in the resolver.

### Why Three Data Source Modes?

- **Aggregation** covers the common case: group entity records by a field and aggregate values
- **Field pivot** covers KPI dashboards where multiple fields from one record each become a card — e.g., 6 financial KPIs from a single Matter record
- **Self-managed** covers DueDateCard which has fundamentally different query semantics (linked entity, specific record context)

All three modes produce the same `IChartData` output, so ChartRenderer is unchanged.

### Why Direct Path Imports from @spaarke/ui-components?

```typescript
// CORRECT: ~483 KiB bundle
import { EventDueDateCard } from "@spaarke/ui-components/dist/components/EventDueDateCard";

// INCORRECT: ~13.8 MiB bundle (pulls ALL components including unused deps like lexical)
import { EventDueDateCard } from "@spaarke/ui-components";
```

### Why Web Resource (Not Custom Page) for Drill-Through?

The `pageType: "entitylist"` does not support `filterXml` in `navigateTo`, so context filtering is impossible with standard entity lists. Web resource dialogs (`pageType: "webresource"`) allow full control over filtering. Custom Pages (`pageType: "custom"`) use `recordId` for data passing — not the URL-encoded parameter contract the drill-through system uses.

### Drill-Through Parameter Contract

Parameters are passed as a URL-encoded string in the `data` property of `navigateTo`. The receiving web resource reads them from `?data=key1=val1&key2=val2...`. Parameters: `entityName`, `filterField`, `filterValue`, `viewId`, `mode` (always `"dialog"`).

**Contract rule**: Changes to parameter names on the VisualHost side must be matched by `parseDrillThroughParams()` on the Events Page side. Both are deployed independently and must be updated together for breaking changes.

### AI Summary: Pre-Populated Field (Not Live AI Call)

The AI Summary sparkle icon (v1.3.0) reads a pre-populated Dataverse text field via `context.webAPI.retrieveRecord()` — it does NOT call Azure OpenAI at render time. The field is populated upstream (background service or plugin). Configured via `"aiSummaryField": "sprk_aisummary"` in `sprk_optionsjson`.

### Caching Strategy

Three independent cache layers:

| Cache | TTL | Rationale |
|-------|-----|-----------|
| Configuration (chart definitions) | 5 min | Definitions rarely change during a session |
| Aggregation data | 2 min | Data changes more frequently |
| View FetchXML | 10 min | System views change very infrequently |

### Card Height: Content-Driven (v1.2.48)

MetricCard and MetricCardMatrix have no default minimum height. The PCF `height` property is `undefined` unless explicitly set. Chart types (BarChart, LineChart, DonutChart) retain a 300px default canvas height. This eliminates whitespace below card grids. The change required removing `height: "100%"` from the PCF container and `flex: 1` from chartContainer.

---

## Configuration System

Configuration is resolved via a **3-tier + defaults** system in `cardConfigResolver.ts`:

| Tier | Source | Priority |
|------|--------|----------|
| 1 | PCF property overrides (`valueFormatOverride`, `columns`, `showTitle`, `titleFontSize`) | Highest |
| 2 | Chart Definition Dataverse fields (`sprk_valueformat`, `sprk_colorsource`, `sprk_metriccardshape`) | Second |
| 3 | Configuration JSON (`sprk_optionsjson`) — full `ICardConfig` object | Third |
| 4 | Built-in defaults | Lowest |

---

## Drill-Through Navigation

When the user clicks the expand button, VisualHostRoot calls `handleExpandClick()`:

- **With `sprk_drillthroughtarget`**: opens web resource in a 90%×85% dialog via `Xrm.Navigation.navigateTo` with `pageType: "webresource"` and URL-encoded context params. Falls back to inline navigation if dialog not supported.
- **Without `sprk_drillthroughtarget`**: falls back to `pageType: "entitylist"` (no context filter support).

The Events Page (`sprk_eventspage.html`) is the primary drill-through target. In dialog mode (`mode=dialog`), it suppresses the Calendar side pane and injects the context filter into FetchXML.

---

## Component Reusability Tiers

| Tier | Location | Reusable In |
|------|----------|-------------|
| **Tier 1: Shared visual components** | `@spaarke/ui-components` (EventDueDateCard, AiSummaryPopover) | PCF, Custom Pages, standalone apps |
| **Tier 2: Service layer** | `VisualHost/control/services/` — depends on `IConfigWebApi` interface | PCF (via `context.webAPI`), Custom Pages (via `Xrm.WebApi`), tests (mock) |
| **Tier 3: PCF integration** | `VisualHost/control/` (index.ts, VisualHostRoot.tsx) — PCF lifecycle, property binding | PCF only |

Adding new visual types to reuse in Custom Pages: extract chart components to `@spaarke/ui-components`, define clean prop interfaces with `IAggregatedDataPoint[]` as data input, use Fluent UI design tokens for automatic dark mode.

---

## Version Management

Version must be updated in **5 locations** for each release: `ControlManifest.Input.xml`, `Solution/solution.xml`, `Solution/Controls/.../ControlManifest.xml`, `Solution/pack.ps1`, and the version badge string in `VisualHostRoot.tsx`.

---

## Technology Stack

| Layer | Technology | Version | Notes |
|-------|-----------|---------|-------|
| **Runtime** | React | 16.14.0 | Platform-provided (ADR-022: React 16 APIs only) |
| **UI Framework** | Fluent UI v9 | 9.46.2 | Platform-provided (ADR-021) |
| **Charts** | @fluentui/react-charting | 5.23.0 | Bar, line, pie, donut |
| **Language** | TypeScript | 4.x | Strict mode |
| **Platform** | PCF | 1.3.18 | API version |
| **Data** | Dataverse WebAPI | v9.2 | OData + FetchXML |

---

## Future Enhancement Opportunities

| Area | Current | Path |
|------|---------|------|
| Entity support in Events Page | `sprk_event` only | Parameterize entity from `entityName` drill-through param |
| Context filter | Single field | Multi-field context filters |
| Data services | Client-side aggregation | Server-side aggregation via BFF API for large datasets |
| Visual type presets | ReportCardMetric as fallthrough | Additional domain presets via same pattern |
| Chart components | Internal to VisualHost | Extract to `@spaarke/ui-components` for Custom Page reuse |
