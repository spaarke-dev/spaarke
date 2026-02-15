# VisualHost Custom JSON Query Enhancement

> **Version**: 1.0
> **Created**: 2026-02-15
> **Status**: Design Specification — For Review
> **Projects**: Matter Performance KPI R1, Finance Intelligence Module R1
> **Scope**: Cross-project VisualHost PCF enhancement

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Problem Statement](#problem-statement)
3. [Current Architecture](#current-architecture)
4. [Proposed Architecture](#proposed-architecture)
5. [Component Inventory](#component-inventory)
6. [API Response Contract](#api-response-contract)
7. [Configuration Schema](#configuration-schema)
8. [Use Cases](#use-cases)
9. [Implementation Plan](#implementation-plan)
10. [Task List](#task-list)
11. [Open Questions](#open-questions)
12. [References](#references)

---

## Executive Summary

**What**: Add a `customJsonQuery` data source mode to VisualHost PCF, enabling it to fetch chart-ready JSON from BFF API endpoints instead of querying Dataverse directly.

**Why**: Two active projects need this capability:

| Project | Need | Current Blocker |
|---------|------|-----------------|
| **Matter Performance KPI** | Display 3 performance grade cards (Guidelines, Budget, Outcomes) from pre-computed Matter fields in a single PCF | Multi-PCF layout fails due to Dataverse form responsive CSS; need a single PCF that renders 3 cards from API data |
| **Finance Intelligence** | Display budget KPIs, spend trends, and alerts from server-side aggregations with Redis caching | Complex multi-entity calculations not feasible with FetchXML; need BFF API as data source |

**Strategic benefit**: Build once in VisualHost, both projects (and all future projects) benefit from a generic Web API data source mode.

---

## Problem Statement

### Matter Performance KPI — Layout Failure

The original approach placed **3 separate VisualHost PCFs** in a 3-column Dataverse form section, each showing one performance grade. After 6 iterations (v1.2.35–v1.2.40), the cards could not reliably fill their allocated column width due to the Dataverse platform wrapper (`div.pa-cb.flexbox`) controlling responsive stacking behavior — CSS from inside the PCF cannot override it.

**Solution**: Use a **single VisualHost PCF** that fetches all 3 grades from a BFF API endpoint and renders them internally via MetricCardMatrix (which handles its own responsive CSS Grid layout).

### Finance Intelligence — FetchXML Limitations

Finance visualizations require:
- Multi-entity joins (spend snapshots + budgets + signals)
- Server-side calculations (budget utilization %, MoM velocity)
- Redis-cached aggregations (5-min TTL, <100ms response)
- Dynamic date ranges (last 12 months relative to today)

None of these are feasible with Dataverse FetchXML queries. The BFF API already implements these calculations — VisualHost just needs to consume the JSON output.

### Shared Solution

Both projects need the same capability: **VisualHost calls a BFF API endpoint and renders the JSON response**. Building `customJsonQuery` as a generic data source mode solves both.

---

## Current Architecture

### Data Source Modes (Today)

VisualHost currently supports **2 data source modes** in `DataAggregationService.fetchAndAggregate()`:

```
┌─────────────────────────────────────────────────────────┐
│            DataAggregationService                        │
│                                                          │
│  sprk_baseviewid populated?                              │
│    ├─ YES → VIEW MODE                                    │
│    │        fetchRecordsFromView()                        │
│    │        • Fetches saved view FetchXML                 │
│    │        • Injects required columns                    │
│    │        • Injects context filter                      │
│    │        • Executes via Dataverse WebAPI                │
│    │                                                      │
│    └─ NO  → BASIC MODE                                    │
│             fetchRecordsBasic()                            │
│             • Builds FetchXML from entity + columns       │
│             • Adds context filter                         │
│             • Executes via Dataverse WebAPI                │
│                                                          │
│  Both paths → aggregateRecords()                         │
│    • Groups by sprk_groupbyfield                         │
│    • Calculates aggregation per group                    │
│    • Returns IAggregatedDataPoint[]                      │
└─────────────────────────────────────────────────────────┘
```

### Unused Fields (Exist But Not Wired)

| Field | Status | Notes |
|-------|--------|-------|
| `sprk_fetchxmlquery` | Defined in IChartDefinition, NOT used in fetch logic | Was planned for manual FetchXML override |
| `sprk_fetchxmlparams` | Defined in IChartDefinition, NOT used | Parameter injection for FetchXML |
| PCF `fetchXmlOverride` property | Read from parameters, NOT passed to DataAggregationService | Would be a third Dataverse mode |

### Data Flow (Current)

```
VisualHostRoot.tsx
  │
  ├─ 1. loadChartDefinition()     ← Fetches sprk_chartdefinition record
  │     ConfigurationLoader
  │
  ├─ 2. fetchAndAggregate()        ← VIEW or BASIC mode (Dataverse only)
  │     DataAggregationService
  │     Returns: IChartData { dataPoints: IAggregatedDataPoint[] }
  │
  ├─ 3. resolveCardConfig()        ← 3-tier config resolution
  │     cardConfigResolver
  │     Returns: ICardConfig
  │
  └─ 4. <ChartRenderer>           ← Routes to visual component
        └─ <MetricCardMatrix>      ← Renders card grid from dataPoints[]
```

### Key Existing Interfaces

```typescript
interface IAggregatedDataPoint {
  label: string;           // Display text (e.g., "Guidelines")
  value: number;           // Numeric value (e.g., 0.85)
  color?: string;          // Optional hex color
  fieldValue: unknown;     // Raw value (e.g., 1 for option set)
  sortOrder?: number;      // Sort key for optionSetOrder mode
}

interface IChartData {
  dataPoints: IAggregatedDataPoint[];
  totalRecords: number;
  aggregationType: AggregationType;
  aggregationField?: string;
  groupByField?: string;
}
```

---

## Proposed Architecture

### New Data Source Mode: `customJsonQuery`

Add a third data source mode that **bypasses Dataverse entirely** and fetches data from a BFF API endpoint.

```
┌──────────────────────────────────────────────────────────────────┐
│            DataAggregationService (Enhanced)                      │
│                                                                   │
│  configurationJson.dataSource.type === "customJsonQuery"?         │
│    ├─ YES → CUSTOM JSON QUERY MODE (NEW)                          │
│    │        fetchFromCustomJsonQuery()                             │
│    │        • Interpolate URL tokens ({{CurrentRecord.Id}}, etc.) │
│    │        • HTTP GET to BFF API with auth token                 │
│    │        • Map JSON response → IAggregatedDataPoint[]          │
│    │        • Return IChartData                                   │
│    │                                                              │
│    └─ NO  → EXISTING FLOW (unchanged)                             │
│             sprk_baseviewid populated?                             │
│               ├─ YES → VIEW MODE                                  │
│               └─ NO  → BASIC MODE                                 │
└──────────────────────────────────────────────────────────────────┘
```

### Enhanced Data Flow

```
VisualHostRoot.tsx
  │
  ├─ 1. loadChartDefinition()           ← Unchanged
  │
  ├─ 2. Parse configurationJson          ← NEW: Check for dataSource config
  │     IF dataSource.type === "customJsonQuery":
  │       └─ fetchFromCustomJsonQuery()  ← NEW: HTTP fetch from BFF API
  │     ELSE:
  │       └─ fetchAndAggregate()         ← Existing Dataverse flow
  │
  ├─ 3. resolveCardConfig()             ← Unchanged
  │
  └─ 4. <ChartRenderer>                 ← Unchanged
        └─ <MetricCardMatrix>            ← Unchanged (receives same dataPoints[])
```

The key architectural decision: **customJsonQuery is detected early and returns the same `IChartData` shape**. Everything downstream (card config resolution, MetricCardMatrix rendering, ReportCardMetric preset, drill-through) works unchanged.

---

## Component Inventory

### Existing Components (Unchanged)

| Component | File | Role | Changes |
|-----------|------|------|---------|
| **VisualHostRoot** | `control/components/VisualHostRoot.tsx` | Orchestration, data loading, render dispatch | Minor: add customJsonQuery branch before fetchAndAggregate |
| **ChartRenderer** | `control/components/ChartRenderer.tsx` | Visual type router | None |
| **MetricCardMatrix** | `control/components/MetricCardMatrix.tsx` | Responsive CSS Grid of cards | None |
| **MetricCard** | `control/components/MetricCard.tsx` | Single card component | None |
| **cardConfigResolver** | `control/utils/cardConfigResolver.ts` | 3-tier config resolution, ReportCardMetric preset | None |
| **ConfigurationLoader** | `control/services/ConfigurationLoader.ts` | Loads sprk_chartdefinition from Dataverse | None |
| **DataAggregationService** | `control/services/DataAggregationService.ts` | VIEW and BASIC mode data fetching | None (new mode added alongside) |
| **ClickActionHandler** | `control/services/ClickActionHandler.ts` | Drill-through and click actions | None |
| **ErrorBoundary** | `control/components/ErrorBoundary.tsx` | React error boundary | None |
| **ThemeProvider** | `control/providers/ThemeProvider.ts` | Dark/light mode resolution | None |
| **index.ts** | `control/index.ts` | PCF lifecycle (init, updateView, destroy) | None |

### Existing BFF API Components (Unchanged)

| Component | File | Role | Changes |
|-----------|------|------|---------|
| **ScorecardCalculatorService** | `Services/ScorecardCalculatorService.cs` | Grade calculation logic | None |
| **ScorecardCalculatorEndpoints** | `Api/ScorecardCalculatorEndpoints.cs` | POST recalculate-grades endpoints | None (new GET endpoints added separately) |
| **ScorecardModels** | `Models/ScorecardModels.cs` | RecalculateGradesResponse | None (new response model added separately) |
| **IDataverseService** | `Spaarke.Dataverse/IDataverseService.cs` | Dataverse query interface | None |
| **DataverseWebApiService** | `Spaarke.Dataverse/DataverseWebApiService.cs` | Dataverse OData implementation | None |

### New Components

| Component | File | Role | Est. Lines |
|-----------|------|------|------------|
| **CustomJsonQueryService** | `control/services/CustomJsonQueryService.ts` | HTTP fetch, token interpolation, response mapping, error handling | ~150 |
| **ICustomJsonQueryConfig** | `control/types/index.ts` | Type definitions for customJsonQuery configuration | ~30 |
| **ScorecardGradesEndpoints** | `Api/ScorecardGradesEndpoints.cs` | GET /api/scorecard/matters/{id}/grades | ~80 |
| **ScorecardGradesResponse** | `Models/ScorecardModels.cs` | Response DTO for grades endpoint | ~20 |

### New Components — Detail

#### CustomJsonQueryService (PCF)

```typescript
// New file: control/services/CustomJsonQueryService.ts

/**
 * Fetches chart data from a BFF API endpoint.
 * Handles token interpolation, authentication, response mapping, and errors.
 */
export async function fetchFromCustomJsonQuery(
  context: ComponentFramework.Context<IInputs>,
  dataSource: ICustomJsonQueryDataSource,
  options?: { contextRecordId?: string }
): Promise<IChartData> {
  // 1. Interpolate URL tokens: {{CurrentRecord.Id}} → actual GUID
  // 2. Build full URL with query parameters
  // 3. Fetch with auth token from PCF context
  // 4. Handle HTTP errors (401/403/404/500 → user-friendly messages)
  // 5. Map response to IChartData
  //    - If response has "dataPoints" array → use directly
  //    - If response is flat object + cards config → map fields to dataPoints
  // 6. Return IChartData (same shape as Dataverse modes)
}
```

#### ICustomJsonQueryConfig Types (PCF)

```typescript
// Added to: control/types/index.ts

export interface ICustomJsonQueryDataSource {
  type: "customJsonQuery";
  endpoint: string;                      // "/api/scorecard/matters/{{CurrentRecord.Id}}/grades"
  method?: "GET" | "POST";              // Default: GET
  parameters?: Record<string, unknown>; // Query string params
  headers?: Record<string, string>;     // Custom headers
  refreshInterval?: number;             // Auto-refresh ms (P2)
}

export interface IDataSourceConfig {
  dataSource?: ICustomJsonQueryDataSource;
  responseMapping?: IResponseMapping;    // For flat JSON → dataPoints mapping
}

export interface IResponseMapping {
  dataPointsPath?: string;              // JSON path to dataPoints array (default: "dataPoints")
  cards?: ICardMapping[];               // Map flat object fields to data points
}

export interface ICardMapping {
  field: string;                        // Response object field name
  label: string;                        // Display label
  fieldValue?: unknown;                 // For icon/color resolution
}
```

#### ScorecardGradesEndpoints (BFF API)

```csharp
// New file: Api/ScorecardGradesEndpoints.cs

public static class ScorecardGradesEndpoints
{
    public static void MapScorecardGradesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/scorecard")
            .RequireAuthorization()
            .RequireRateLimiting("dataverse-query");

        // GET /api/scorecard/matters/{matterId}/grades
        group.MapGet("/matters/{matterId:guid}/grades", GetMatterGrades);

        // GET /api/scorecard/projects/{projectId}/grades
        group.MapGet("/projects/{projectId:guid}/grades", GetProjectGrades);
    }
}
```

Response model:

```csharp
// Added to: Models/ScorecardModels.cs

public sealed record ScorecardGradesResponse
{
    public required DataPointDto[] DataPoints { get; init; }
}

public sealed record DataPointDto
{
    public required string Label { get; init; }
    public required decimal Value { get; init; }
    public int? FieldValue { get; init; }  // Performance area enum for icon/color mapping
    public int? SortOrder { get; init; }
}
```

---

## API Response Contract

### Standard Response Format: `dataPoints[]`

All BFF API endpoints that serve VisualHost SHOULD return data in this format:

```json
{
  "dataPoints": [
    {
      "label": "string",
      "value": 0.00,
      "fieldValue": null,
      "color": null,
      "sortOrder": null
    }
  ]
}
```

This maps 1:1 to the existing `IAggregatedDataPoint` interface. No client-side transformation required.

### KPI Grades Endpoint

**`GET /api/scorecard/matters/{matterId}/grades`**

Response:
```json
{
  "dataPoints": [
    { "label": "Guidelines", "value": 0.85, "fieldValue": 1, "sortOrder": 1 },
    { "label": "Budget",     "value": 0.60, "fieldValue": 2, "sortOrder": 2 },
    { "label": "Outcomes",   "value": 0.92, "fieldValue": 3, "sortOrder": 3 }
  ]
}
```

Data source: Reads `sprk_guidelinescompliancegrade_current`, `sprk_budgetcompliancegrade_current`, `sprk_outcomescompliancegrade_current` from the Matter record. These are the authoritative pre-computed values maintained by `ScorecardCalculatorService`.

### Finance Summary Endpoint (Future)

**`GET /api/finance/matters/{matterId}/summary`**

Response:
```json
{
  "dataPoints": [
    { "label": "Total Budget",   "value": 100000,  "fieldValue": "totalBudget" },
    { "label": "Total Spend",    "value": 45250,   "fieldValue": "totalSpend" },
    { "label": "Remaining",      "value": 54750,   "fieldValue": "remaining" },
    { "label": "Utilization %",  "value": 45.25,   "fieldValue": "utilization" }
  ]
}
```

### Flat Object Response (Alternative)

For APIs that return domain-specific shapes, the `cards[]` mapping in configurationJson transforms them:

**API returns:**
```json
{
  "totalBudget": 100000,
  "totalSpend": 45250,
  "remainingBudget": 54750,
  "budgetUtilization": 45.25
}
```

**configurationJson maps it:**
```json
{
  "dataSource": { "type": "customJsonQuery", "endpoint": "..." },
  "responseMapping": {
    "cards": [
      { "field": "totalBudget", "label": "Total Budget", "fieldValue": "budget" },
      { "field": "totalSpend", "label": "Total Spend", "fieldValue": "spend" }
    ]
  }
}
```

**Priority**: The `dataPoints[]` format is P0 (MVP). The flat object + `cards[]` mapping is P1 (enhancement).

### Error Response Format

All BFF API errors follow ProblemDetails (ADR-019):

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "Matter a1b2c3d4... not found",
  "traceId": "00-abc123-def456-00"
}
```

VisualHost maps HTTP status codes to user-friendly messages:

| Status | VisualHost Displays |
|--------|---------------------|
| 401 | "Authentication failed. Please sign in again." |
| 403 | "You don't have permission to view this data." |
| 404 | Empty state (no data) |
| 500 | "Server error. Please try again later." |
| Network error | "Unable to connect. Check your network connection." |

---

## Configuration Schema

### Chart Definition Record

For the KPI Performance Scorecard, create one `sprk_chartdefinition` record:

| Field | Value |
|-------|-------|
| `sprk_name` | "Matter Performance Scorecard" |
| `sprk_visualtype` | ReportCardMetric (100000010) |
| `sprk_entitylogicalname` | `sprk_matter` (informational — not used for query) |
| `sprk_configurationjson` | See below |

```json
{
  "dataSource": {
    "type": "customJsonQuery",
    "endpoint": "/api/scorecard/matters/{{CurrentRecord.Id}}/grades"
  },
  "columns": 3,
  "cardSize": "medium",
  "sortBy": "optionSetOrder"
}
```

The `ReportCardMetric` preset automatically applies:
- `valueFormat: "letterGrade"` (converts 0.85 → "B+")
- `colorSource: "valueThreshold"` with grade thresholds (blue ≥0.85, yellow ≥0.70, red <0.70)
- `iconMap`: Guidelines→Gavel, Budget→Money, Outcomes→Target
- `showAccentBar: true`

### PCF Configuration on Form

| PCF Property | Value |
|-------------|-------|
| `chartDefinition` | Bound to lookup (or use `chartDefinitionId` with static GUID) |
| `contextFieldName` | (not needed — endpoint receives record ID via URL token) |
| `showToolbar` | true |
| `enableDrillThrough` | true |

**Form layout**: Single PCF in a full-width (1-column) section. MetricCardMatrix handles the internal 3-column grid via CSS Grid.

---

## Use Cases

### Use Case 1: Matter Performance Grades (This Project)

**Visual**: 3 metric cards showing current performance grades

```
┌─────────────────────────────────────────────────┐
│  ⚖ Guidelines    💰 Budget       🎯 Outcomes   │
│     B+ (85%)       D (60%)         A (92%)      │
│  ▐ blue accent   ▐ red accent   ▐ blue accent  │
└─────────────────────────────────────────────────┘
```

**Endpoint**: `GET /api/scorecard/matters/{matterId}/grades`
**Data source**: Reads 3 pre-computed decimal fields from Matter entity
**Visual type**: ReportCardMetric (auto letter grade, color thresholds, icons)

### Use Case 2: Project Performance Grades (This Project)

Same pattern, different parent entity:

**Endpoint**: `GET /api/scorecard/projects/{projectId}/grades`
**Data source**: Reads 3 pre-computed decimal fields from Project entity

### Use Case 3: Finance Summary Cards (Finance Intelligence Project)

**Visual**: 4 metric cards showing budget KPIs

```
┌──────────────────────────────────────────────────────────────┐
│  Total Budget     Total Spend     Remaining     Utilization  │
│   $100,000         $45,250        $54,750         45.25%     │
└──────────────────────────────────────────────────────────────┘
```

**Endpoint**: `GET /api/finance/matters/{matterId}/summary`
**Data source**: Multi-entity aggregation (spend snapshots + budgets), Redis-cached
**Visual type**: MetricCardMatrix with currency/percentage formatting

### Use Case 4: Monthly Spend Trend (Finance Intelligence Project)

**Visual**: Line chart showing last 12 months of spend

**Endpoint**: `GET /api/finance/matters/{matterId}/spend-trend?months=12`
**Response**: `ChartDataResponse` format (labels + datasets) — requires chart-type response mapping (P1)

### Use Case 5: Budget Alerts Dashboard (Finance Intelligence Project)

**Visual**: Data grid with conditional formatting

**Endpoint**: `GET /api/finance/dashboard/budget-alerts?month=2026-02`
**Response**: `DataGridResponse` format (rows + columns) — requires grid response mapping (P2)

---

## Implementation Plan

### Phase 1: VisualHost customJsonQuery Core (PCF)

**Goal**: VisualHost can fetch JSON from a BFF API endpoint and render MetricCardMatrix cards.

**Scope**:
- `customJsonQuery` data source handler in new `CustomJsonQueryService.ts`
- Token interpolation (`{{CurrentRecord.Id}}`, `{{CurrentUser.Id}}`, `{{Today}}`)
- HTTP fetch with Dataverse auth token (from PCF context)
- `dataPoints[]` response format → `IAggregatedDataPoint[]` mapping
- Error handling (HTTP errors → inline MessageBar)
- Type definitions (`ICustomJsonQueryDataSource`, etc.)
- Version bump, build, package

**Dependencies**: None (PCF-only changes)

### Phase 2: KPI Grades Endpoint (BFF API)

**Goal**: BFF API serves pre-computed grades from Matter/Project entities.

**Scope**:
- `GET /api/scorecard/matters/{matterId}/grades` endpoint
- `GET /api/scorecard/projects/{projectId}/grades` endpoint
- `ScorecardGradesResponse` / `DataPointDto` models
- Read 3 grade fields from entity via `IDataverseService`
- Authorization, rate limiting, error handling
- Unit tests

**Dependencies**: Phase 1 (VisualHost must consume the response)

### Phase 3: Integration & Deployment (Dataverse)

**Goal**: End-to-end working: chart definition → PCF → API → grades displayed.

**Scope**:
- Create "Matter Performance Scorecard" chart definition record
- Configure Matter form: single VisualHost PCF in full-width section
- Configure Project form: same pattern
- End-to-end testing (dark mode, empty state, error states)
- Remove old 3-PCF layout from form

**Dependencies**: Phase 1 + Phase 2

### Phase 4: Flat Object Response Mapping (Enhancement — P1)

**Goal**: Support APIs that return domain-specific JSON shapes (not pre-shaped `dataPoints[]`).

**Scope**:
- `responseMapping.cards[]` configuration support
- Map flat JSON object fields → `IAggregatedDataPoint[]`
- Enables Finance Intelligence Use Cases 1–3 without changing API response format

**Dependencies**: Phase 1

### Phase 5: Chart/Grid Response Formats (Future — P2)

**Goal**: Support line charts, bar charts, and data grids from API responses.

**Scope**:
- `ChartDataResponse` format (labels + datasets) → chart renderers
- `DataGridResponse` format (rows + pagination) → grid renderer
- Auto-refresh interval (`refreshInterval` configuration)
- Client-side response caching

**Dependencies**: Phase 4

---

## Task List

### Phase 1: VisualHost customJsonQuery Core

| # | Task | Description | Est. |
|---|------|-------------|------|
| 1.1 | **Add type definitions** | `ICustomJsonQueryDataSource`, `IDataSourceConfig`, `IResponseMapping`, `ICardMapping` in `types/index.ts` | 30 min |
| 1.2 | **Create CustomJsonQueryService** | New file `services/CustomJsonQueryService.ts` with `fetchFromCustomJsonQuery()` | 2 hr |
| 1.3 | **Implement token interpolation** | `interpolateEndpointTokens()` — replace `{{CurrentRecord.Id}}`, `{{CurrentUser.Id}}`, `{{Today}}`, `{{Now}}` | 30 min |
| 1.4 | **Implement HTTP fetch with auth** | Use PCF context to get auth token, make HTTP GET, handle response | 1 hr |
| 1.5 | **Implement dataPoints response mapping** | Map `{ dataPoints: [...] }` → `IChartData` | 30 min |
| 1.6 | **Implement error handling** | Map HTTP status codes → user-friendly error messages, return to VisualHostRoot | 30 min |
| 1.7 | **Wire into VisualHostRoot** | Add `customJsonQuery` check before `fetchAndAggregate()`, call `fetchFromCustomJsonQuery()` when detected | 30 min |
| 1.8 | **Version bump to 1.2.41** | Update 5 version locations per PCF-DEPLOYMENT-GUIDE | 15 min |
| 1.9 | **Build and package** | Clean build, copy to Solution, pack.ps1 | 15 min |
| 1.10 | **Update architecture docs** | Update VISUALHOST-ARCHITECTURE.md and VISUALHOST-SETUP-GUIDE.md with new data source mode | 30 min |

**Phase 1 Total**: ~6.5 hours

### Phase 2: KPI Grades Endpoint

| # | Task | Description | Est. |
|---|------|-------------|------|
| 2.1 | **Add response models** | `ScorecardGradesResponse`, `DataPointDto` in `Models/ScorecardModels.cs` | 15 min |
| 2.2 | **Add IDataverseService method** | `GetMatterGradeFieldsAsync(Guid matterId)` — reads 3 grade fields from Matter | 30 min |
| 2.3 | **Implement grades endpoint** | `ScorecardGradesEndpoints.cs` — GET /api/scorecard/matters/{id}/grades | 1 hr |
| 2.4 | **Add project grades endpoint** | GET /api/scorecard/projects/{id}/grades (same pattern) | 30 min |
| 2.5 | **Register endpoints** | Wire into `Program.cs` | 10 min |
| 2.6 | **Unit tests** | Test grade field reading, response mapping, error cases | 1.5 hr |
| 2.7 | **Build and verify** | `dotnet build`, `dotnet test` | 15 min |

**Phase 2 Total**: ~4 hours

### Phase 3: Integration & Deployment

| # | Task | Description | Est. |
|---|------|-------------|------|
| 3.1 | **Create chart definition record** | "Matter Performance Scorecard" with customJsonQuery config | 30 min |
| 3.2 | **Configure Matter form** | Single VisualHost PCF, full-width section, bound to chart definition | 30 min |
| 3.3 | **Deploy and test** | Import PCF solution + deploy API, verify 3 cards render | 1 hr |
| 3.4 | **Configure Project form** | Same pattern for Project entity | 30 min |
| 3.5 | **Test edge cases** | Empty grades, dark mode, error states, permissions | 1 hr |
| 3.6 | **Remove old 3-PCF layout** | Clean up previous multi-PCF form configuration | 15 min |

**Phase 3 Total**: ~3.75 hours

### Phase 4: Flat Object Response Mapping (P1)

| # | Task | Description | Est. |
|---|------|-------------|------|
| 4.1 | **Implement cards[] mapping** | In CustomJsonQueryService: map flat JSON + cards config → dataPoints | 1.5 hr |
| 4.2 | **Add response format detection** | Auto-detect `dataPoints[]` vs flat object; apply correct mapping | 30 min |
| 4.3 | **Unit tests** | Test flat object → dataPoints mapping with various configs | 1 hr |
| 4.4 | **Update docs** | Add flat object mapping examples to setup guide | 30 min |

**Phase 4 Total**: ~3.5 hours

### Phase 5: Chart/Grid Response Formats (P2 — Future)

| # | Task | Description | Est. |
|---|------|-------------|------|
| 5.1 | **ChartDataResponse mapping** | Map labels + datasets → chart renderer props | 3 hr |
| 5.2 | **DataGridResponse mapping** | Map rows → grid renderer props | 3 hr |
| 5.3 | **Auto-refresh interval** | `setInterval` with configurable TTL, cleanup on destroy | 1.5 hr |
| 5.4 | **Client-side response caching** | Cache responses by endpoint URL with configurable TTL | 1.5 hr |

**Phase 5 Total**: ~9 hours

### Summary

| Phase | Scope | Priority | Estimated Effort |
|-------|-------|----------|-----------------|
| **Phase 1** | customJsonQuery in VisualHost PCF | P0 — MVP | ~6.5 hr |
| **Phase 2** | KPI Grades BFF API endpoint | P0 — MVP | ~4 hr |
| **Phase 3** | Integration, forms, testing | P0 — MVP | ~3.75 hr |
| **Phase 4** | Flat object response mapping | P1 | ~3.5 hr |
| **Phase 5** | Chart/grid formats, auto-refresh, caching | P2 | ~9 hr |
| **Total MVP (P0)** | | | **~14.25 hr** |
| **Total with P1** | | | **~17.75 hr** |

---

## Open Questions

### Authentication

1. **Can VisualHost PCF access a Dataverse auth token for BFF API calls?**
   - The PCF `context.webAPI` uses internal Dataverse authentication
   - For external HTTP calls to the BFF API, we need a bearer token
   - **Options**: (a) Use `Xrm.Utility.getGlobalContext().getCurrentAppUrl()` to derive token, (b) Use fetch with `credentials: "include"` if same-origin, (c) Use a dedicated token endpoint
   - **Recommendation**: Investigate whether `context.factory.getPopup()` or `context.webAPI` exposes a reusable token

2. **CORS Configuration**: Does the BFF API need CORS headers for PCF → API calls?
   - If PCF runs in an iframe on `*.dynamics.com`, and BFF is on `*.azurewebsites.net`, CORS is required
   - Need to configure `AllowedOrigins` in BFF API

### Response Format

3. **Should we mandate `dataPoints[]` format for all endpoints, or support multiple formats?**
   - Mandating `dataPoints[]` is simplest (API does all shaping)
   - But Finance endpoints may want to return domain-specific shapes
   - **Recommendation**: P0 = `dataPoints[]` only; P1 = add flat object mapping

### Caching

4. **Client-side caching in VisualHost or rely on server-side only?**
   - Server: Redis 5-min TTL (Finance already plans this)
   - Client: Would reduce API calls during form navigation
   - **Recommendation**: P0 = server-side only; P2 = add client-side caching

### Scope Boundaries

5. **Should this project (KPI R1) implement Phase 1 + 2 only, or also Phase 4?**
   - Phase 4 (flat object mapping) benefits Finance more than KPI
   - KPI grades endpoint can return `dataPoints[]` directly
   - **Recommendation**: KPI project implements Phases 1–3; Finance project implements Phase 4–5

---

## References

| Document | Location | Content |
|----------|----------|---------|
| VisualHost Architecture | `docs/guides/VISUALHOST-ARCHITECTURE.md` | Current component hierarchy, data flow, visual types |
| VisualHost Setup Guide | `docs/guides/VISUALHOST-SETUP-GUIDE.md` | Chart definition configuration, PCF properties |
| Finance Web API Requirements | `C:\code_files\spaarke-wt-financial-intelligence-module-r1\docs\architecture\visualhost-web-api-query-requirements.md` | Full Finance requirements, endpoint specs, response formats |
| Matter Performance KPI Spec | `projects/matter-performance-KPI-r1/spec-r1.md` | KPI entity schema, grade calculation, visualization requirements |
| Matter Performance KPI Plan | `projects/matter-performance-KPI-r1/plan-r1.md` | Implementation phases, technical decisions |
| PCF Deployment Guide | `docs/guides/PCF-DEPLOYMENT-GUIDE.md` | Build, package, version bump, solution import procedures |
| ADR-001 | `.claude/adr/ADR-001-minimal-api-and-workers.md` | Minimal API patterns (BFF endpoint structure) |
| ADR-008 | `.claude/adr/ADR-008-endpoint-filters-for-auth.md` | Endpoint filter authorization (API security) |
| ADR-019 | `.claude/adr/ADR-019-problem-details-for-errors.md` | ProblemDetails error format |
| ADR-021 | `.claude/adr/ADR-021-fluent-ui-v9-design-system.md` | Fluent UI v9, dark mode, no hard-coded colors |
| ADR-022 | `.claude/adr/ADR-022-pcf-platform-libraries.md` | React 16 APIs, unmanaged solutions |

---

**Document Version**: 1.0
**Last Updated**: 2026-02-15
**Next Steps**: Review and approve, then begin Phase 1 implementation
**Maintained By**: Matter Performance KPI + Finance Intelligence teams (shared ownership)
