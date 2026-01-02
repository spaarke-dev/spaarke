# sprk_chartdefinition Entity Schema

> **Entity Logical Name**: `sprk_chartdefinition`
> **Display Name**: Chart Definition
> **Entity Type**: Organization-owned (Phase 1: Admin-only CRUD)
> **Created**: 2025-12-29
> **Project**: Visualization Module

---

## Entity Overview

The `sprk_chartdefinition` entity stores configuration for the Spaarke Visuals Framework. Each record defines a visual (chart, card, calendar) that can be rendered by the Visual Host PCF control.

---

## Fields

| Field Logical Name | Display Name | Type | Required | Description |
|-------------------|--------------|------|----------|-------------|
| `sprk_chartdefinitionid` | Chart Definition | Uniqueidentifier | Yes | Primary key (auto-generated GUID) |
| `sprk_name` | Name | String (200) | Yes | Display name for the chart definition |
| `sprk_visualtype` | Visual Type | OptionSet | Yes | Type of visual to render (see VisualType option set) |
| `sprk_entitylogicalname` | Entity Logical Name | String (100) | Yes | Dataverse entity to query (e.g., `sprk_project`) |
| `sprk_baseviewid` | Base View ID | String (50) | Yes | GUID of SavedQuery or UserQuery to use |
| `sprk_aggregationfield` | Aggregation Field | String (100) | No | Field to aggregate (null for count-only) |
| `sprk_aggregationtype` | Aggregation Type | OptionSet | No | Type of aggregation (default: count) |
| `sprk_groupbyfield` | Group By Field | String (100) | No | Field to group by (for charts with categories) |
| `sprk_optionsjson` | Options JSON | Multiline Text (100000) | No | JSON blob for per-visual-type advanced options |
| `sprk_reportingentity` | Reporting Entity | Lookup | No | Lookup to sprk_reportingentity for user-friendly entity selection (v1.1.0+) |
| `sprk_reportingview` | Reporting View | Lookup | No | Lookup to sprk_reportingview for user-friendly view selection (v1.1.0+) |

---

## Option Sets

### VisualType (sprk_visualtype)

Defines the type of visual to render.

| Value | Label | Description |
|-------|-------|-------------|
| `100000000` | Metric Card | Single aggregate value with optional trend indicator |
| `100000001` | Bar Chart | Vertical or horizontal bar chart |
| `100000002` | Line Chart | Line chart for trends over time |
| `100000003` | Area Chart | Filled area chart (cumulative) |
| `100000004` | Donut Chart | Donut/pie chart for proportions |
| `100000005` | Status Bar | Horizontal segmented status distribution bar |
| `100000006` | Calendar | Task/deadline calendar view |
| `100000007` | Mini Table | Top-N records table |

**TypeScript Enum Mapping**:

```typescript
export enum VisualType {
  MetricCard = 100000000,
  BarChart = 100000001,
  LineChart = 100000002,
  AreaChart = 100000003,
  DonutChart = 100000004,
  StatusBar = 100000005,
  Calendar = 100000006,
  MiniTable = 100000007,
}
```

### AggregationType (sprk_aggregationtype)

Defines how data is aggregated.

| Value | Label | Description |
|-------|-------|-------------|
| `100000000` | Count | Count of records |
| `100000001` | Sum | Sum of field values |
| `100000002` | Average | Average of field values |
| `100000003` | Min | Minimum value |
| `100000004` | Max | Maximum value |

**TypeScript Enum Mapping**:

```typescript
export enum AggregationType {
  Count = 100000000,
  Sum = 100000001,
  Average = 100000002,
  Min = 100000003,
  Max = 100000004,
}
```

---

## Field Details

### sprk_name (Primary Name Field)

- **Type**: Single Line of Text
- **Max Length**: 200 characters
- **Required**: Yes
- **Searchable**: Yes
- **Example**: "Active Projects by Status", "Invoice Totals by Month"

### sprk_entitylogicalname

- **Type**: Single Line of Text
- **Max Length**: 100 characters
- **Required**: Yes
- **Description**: The Dataverse entity logical name to query
- **Validation**: Must be a valid entity logical name
- **Examples**: `sprk_project`, `sprk_matter`, `sprk_document`, `sprk_invoice`, `sprk_event`, `email`

### sprk_baseviewid

- **Type**: Single Line of Text
- **Max Length**: 50 characters (GUID format)
- **Required**: Yes
- **Description**: GUID reference to a SavedQuery (system view) or UserQuery (personal view)
- **Format**: GUID without braces (e.g., `12345678-1234-1234-1234-123456789012`)
- **Note**: The view's FetchXML defines the base data set before aggregation

### sprk_aggregationfield

- **Type**: Single Line of Text
- **Max Length**: 100 characters
- **Required**: No (null for count-only visuals)
- **Description**: Logical name of the field to aggregate
- **Examples**: `sprk_amount`, `sprk_totalvalue`, `sprk_estimatedhours`

### sprk_groupbyfield

- **Type**: Single Line of Text
- **Max Length**: 100 characters
- **Required**: No (null for single-value metrics)
- **Description**: Logical name of the field to group by (creates chart categories)
- **Examples**: `statuscode`, `sprk_projecttype`, `createdon` (for time series)

### sprk_optionsjson

- **Type**: Multiline Text
- **Max Length**: 100,000 characters
- **Required**: No
- **Description**: JSON blob for advanced per-visual-type options
- **Default**: `{}`
- **Note**: Schema varies by visual type; see per-visual documentation

### sprk_reportingentity (v1.1.0+)

- **Type**: Lookup
- **Related Entity**: sprk_reportingentity
- **Required**: No (optional for better UX)
- **Description**: Lookup for user-friendly entity selection
- **Note**: Form JavaScript syncs this to sprk_entitylogicalname backing field
- **Related Records Filtering**: None (shows all reporting entities)

### sprk_reportingview (v1.1.0+)

- **Type**: Lookup
- **Related Entity**: sprk_reportingview
- **Required**: No (optional for better UX)
- **Description**: Lookup for user-friendly view selection
- **Note**: Form JavaScript syncs View ID GUID to sprk_baseviewid backing field
- **Related Records Filtering**: Filtered by selected sprk_reportingentity

---

## Example Records

### Example 1: Metric Card - Total Active Projects

```json
{
  "sprk_name": "Active Projects Count",
  "sprk_visualtype": 100000000,
  "sprk_entitylogicalname": "sprk_project",
  "sprk_baseviewid": "11111111-1111-1111-1111-111111111111",
  "sprk_aggregationtype": 100000000,
  "sprk_optionsjson": "{\"showTrend\": true, \"trendPeriod\": \"month\"}"
}
```

### Example 2: Bar Chart - Projects by Status

```json
{
  "sprk_name": "Projects by Status",
  "sprk_visualtype": 100000001,
  "sprk_entitylogicalname": "sprk_project",
  "sprk_baseviewid": "22222222-2222-2222-2222-222222222222",
  "sprk_aggregationtype": 100000000,
  "sprk_groupbyfield": "statuscode",
  "sprk_optionsjson": "{\"orientation\": \"vertical\"}"
}
```

### Example 3: Donut Chart - Invoice Distribution

```json
{
  "sprk_name": "Invoice Amount by Type",
  "sprk_visualtype": 100000004,
  "sprk_entitylogicalname": "sprk_invoice",
  "sprk_baseviewid": "33333333-3333-3333-3333-333333333333",
  "sprk_aggregationfield": "sprk_totalamount",
  "sprk_aggregationtype": 100000001,
  "sprk_groupbyfield": "sprk_invoicetype",
  "sprk_optionsjson": "{}"
}
```

### Example 4: Line Chart - Monthly Trends

```json
{
  "sprk_name": "New Matters by Month",
  "sprk_visualtype": 100000002,
  "sprk_entitylogicalname": "sprk_matter",
  "sprk_baseviewid": "44444444-4444-4444-4444-444444444444",
  "sprk_aggregationtype": 100000000,
  "sprk_groupbyfield": "createdon",
  "sprk_optionsjson": "{\"dateGrouping\": \"month\", \"showDataPoints\": true}"
}
```

---

## Entity Relationships

### Future Relationships (Phase 2+)

| Relationship | Related Entity | Type | Description |
|--------------|---------------|------|-------------|
| Owner | SystemUser/Team | N:1 | Owner of the chart definition |
| Regarding | Various | Polymorphic | Optional entity context for placement |

---

## Security Model (Phase 1)

| Role | Create | Read | Update | Delete |
|------|--------|------|--------|--------|
| System Administrator | Yes | Yes | Yes | Yes |
| Standard Users | No | Yes | No | No |

**Note**: Phase 1 uses organization-owned records with admin-only CRUD. Phase 2 may introduce user-owned definitions with more granular security.

---

## TypeScript Interface

For PCF control development, use this interface:

```typescript
/**
 * Chart Definition entity record from Dataverse
 */
export interface IChartDefinition {
  /** Primary key (GUID) */
  sprk_chartdefinitionid: string;

  /** Display name */
  sprk_name: string;

  /** Visual type to render */
  sprk_visualtype: VisualType;

  /** Target entity logical name */
  sprk_entitylogicalname: string;

  /** Base view GUID (SavedQuery or UserQuery) */
  sprk_baseviewid: string;

  /** Field to aggregate (optional) */
  sprk_aggregationfield?: string;

  /** Aggregation type (optional, default: Count) */
  sprk_aggregationtype?: AggregationType;

  /** Group by field (optional) */
  sprk_groupbyfield?: string;

  /** Per-visual-type options JSON (optional) */
  sprk_optionsjson?: string;
}
```

---

## Deployment Notes

### Solution Placement

- **Solution**: Spaarke Core (or dedicated Visualization solution)
- **Publisher Prefix**: `sprk_`
- **Managed/Unmanaged**: Deploy as managed to target environments

### Power Platform CLI Commands

```powershell
# Export solution containing entity
pac solution export --path ./solutions --name SpaarkeVisualization --managed false

# Import solution to target environment
pac solution import --path ./solutions/SpaarkeVisualization.zip
```

---

## Validation Rules

1. **sprk_visualtype** - Required; must be valid option set value
2. **sprk_entitylogicalname** - Required; should be validated against known entities
3. **sprk_baseviewid** - Required; must be valid GUID format
4. **sprk_optionsjson** - If provided, must be valid JSON

---

## Change History

| Date | Version | Change |
|------|---------|--------|
| 2025-12-29 | 1.0 | Initial schema definition |
| 2025-12-30 | 1.1 | Added sprk_reportingentity and sprk_reportingview lookup fields (Phase 6) |

---

*Entity schema for Task 001 of the Visualization Module project. Updated for Phase 6 enhancements.*
