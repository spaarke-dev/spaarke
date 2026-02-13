# VisualHost PCF Control - Setup & Configuration Guide

> **Version**: 1.2.33 | **Last Updated**: February 13, 2026
>
> **Audience**: Dataverse administrators, form designers, solution configurators
>
> **Purpose**: Step-by-step guide for configuring VisualHost chart definitions and placing visual controls on Dataverse forms

---

## Table of Contents

1. [Overview](#overview)
2. [Prerequisites](#prerequisites)
3. [Step 1: Create a Chart Definition Record](#step-1-create-a-chart-definition-record)
4. [Step 2: Configure Visual Type Settings](#step-2-configure-visual-type-settings)
5. [Step 3: Add VisualHost Control to a Form](#step-3-add-visualhost-control-to-a-form)
6. [Step 4: Configure PCF Properties](#step-4-configure-pcf-properties)
7. [Visual Type Configuration Reference](#visual-type-configuration-reference)
8. [Click Actions Configuration](#click-actions-configuration)
9. [Drill-Through Target Configuration](#drill-through-target-configuration)
10. [Data Querying Options](#data-querying-options)
11. [Use Cases: Visual Type Setup Walkthroughs](#use-cases-visual-type-setup-walkthroughs)
12. [Troubleshooting](#troubleshooting)
13. [Adding the ReportCardMetric Visual Type](#adding-the-reportcardmetric-visual-type)

---

## Overview

The **VisualHost** PCF control renders configuration-driven visualizations inside Dataverse model-driven app forms. Instead of building separate PCF controls for each chart type, a single VisualHost instance reads a `sprk_chartdefinition` record to determine:

- **What** to display (visual type: bar chart, donut, metric card, grade card, due date cards, etc.)
- **Where** to get data (entity, view, custom FetchXML, or PCF override)
- **How** to aggregate (count, sum, average, min, max)
- **What happens on click** (open record, open side pane, navigate to page)

One chart definition record = one visual instance on a form.

---

## Prerequisites

- **VisualHost Solution** imported into your Dataverse environment (unmanaged)
- **sprk_chartdefinition** entity available (included in the solution)
- Access to **model-driven app form editor** (classic or modern)
- Familiarity with Dataverse views and entity schemas

---

## Step 1: Create a Chart Definition Record

1. Navigate to your model-driven app
2. Open the **Chart Definitions** area (or navigate to `sprk_chartdefinitions` via advanced find)
3. Click **+ New** to create a new chart definition
4. Fill in the required fields:

| Field | Description | Required |
|-------|-------------|----------|
| **Name** (`sprk_name`) | Display name for the visual (shown in chart headers) | Yes |
| **Visual Type** (`sprk_visualtype`) | The type of visualization to render | Yes |
| **Entity Logical Name** (`sprk_entitylogicalname`) | The Dataverse entity to query data from (e.g., `sprk_event`, `sprk_document`) | Yes (for charts) |
| **Drill-Through Target** (`sprk_drillthroughtarget`) | Web resource name to open when the expand button is clicked (e.g., `sprk_eventspage.html`). If not set, falls back to entity list dialog. | No |
| **Value Format** (`sprk_valueformat`) | How to format metric card values (Short Number, Letter Grade, Percentage, Whole Number, Decimal, Currency) | No (defaults to Short Number) |
| **Color Source** (`sprk_colorsource`) | How per-card colors are determined in matrix mode (None, Option Set Color, Value Threshold) | No (defaults to None) |

5. Save the record and note the **Chart Definition ID** (GUID) from the URL

---

## Step 2: Configure Visual Type Settings

### Chart Types (MetricCard, BarChart, LineChart, DonutChart, StatusBar, Calendar, MiniTable, ReportCardMetric)

> **v1.2.33 Note:** MetricCard now supports **matrix mode** (renders multiple cards in a responsive grid when Group By Field is set). ReportCardMetric is now a **preset for MetricCard** — it auto-applies grade styling via `cardConfigResolver` rather than using a separate component.

These visual types use the **DataAggregationService** to fetch and aggregate entity records client-side.

| Field | Description | When to Use |
|-------|-------------|-------------|
| **Group By Field** (`sprk_groupbyfield`) | Entity field to group data by. See [Group By Field Format](#group-by-field-format) for correct naming per field type. | Required for all charts except MetricCard |
| **Aggregation Type** (`sprk_aggregationtype`) | How to aggregate: Count, Sum, Average, Min, Max | Optional (defaults to Count) |
| **Aggregation Field** (`sprk_aggregationfield`) | Numeric field to aggregate (e.g., `sprk_amount`) | Required for Sum/Average/Min/Max |
| **Base View ID** (`sprk_baseviewid`) | Saved view to use as data source | Optional (uses all records if not set) |
| **Options JSON** (`sprk_optionsjson`) | JSON object for component-specific styling options | Optional |

#### Group By Field Format

The Group By Field value must use the correct **WebAPI property name** format, which varies by field type:

| Field Type | Format | Example | Labels |
|------------|--------|---------|--------|
| **Choice / Optionset** | `fieldname` (plain logical name) | `sprk_documenttype` | Auto-resolved via formatted value annotation (e.g., "Contract", "Invoice") |
| **Lookup** | `_fieldname_value` (with `_` prefix and `_value` suffix) | `_sprk_eventtype_ref_value` | Auto-resolved via formatted value annotation (e.g., "Task", "Reminder") |
| **Status / StatusReason** | `statuscode` or `statecode` | `statuscode` | Auto-resolved (e.g., "Active", "Inactive") |
| **Text** | `fieldname` | `sprk_category` | Uses raw text value |
| **Boolean / TwoOptions** | `fieldname` | `sprk_isactive` | "Yes" / "No" |

**Common mistake:** Using `_sprk_documenttype_value` for a choice field. The `_..._value` format is **only for lookup fields**. Choice fields use the plain logical name.

**Formatted value labels:** VisualHost automatically uses the `@OData.Community.Display.V1.FormattedValue` annotation when available. This means lookup and choice fields display human-readable labels (e.g., "Task" instead of a GUID or numeric code) without any extra configuration.

**Auto-attribute injection (v1.2.14+):** If the Group By Field column is not included in the saved view's column set, VisualHost automatically injects it into the view's FetchXML at runtime. You do **not** need to add the column to your saved view manually.

**Sort order:** Grouped data points are sorted **alphabetically by label** (A→Z, left-to-right on bar charts).

### Card Types (DueDateCard, DueDateCardList)

Card visual types fetch their own data directly. They do NOT use the DataAggregationService.

| Field | Description | When to Use |
|-------|-------------|-------------|
| **Entity Logical Name** | The entity to query (typically `sprk_event`) | Required |
| **Base View ID** (`sprk_baseviewid`) | Saved view for the card list query | Required for DueDateCardList |
| **Context Field Name** (`sprk_contextfieldname`) | Lookup field on the entity that points to the parent record | Required for context filtering |
| **Max Display Items** (`sprk_maxdisplayitems`) | Maximum number of cards to show (default: 10) | Optional |
| **View List Tab Name** (`sprk_viewlisttabname`) | Form tab name for "View All" link navigation | Optional |

---

## Step 3: Add VisualHost Control to a Form

### Method A: Bind to a Lookup Field (Recommended)

1. Open the form in the **form editor**
2. Add a **lookup field** that points to `sprk_chartdefinition` on your entity, OR use an existing field
3. Select the field on the form
4. Click **Change Control** (or Properties → Controls tab)
5. Add **VisualHost** control
6. Set the control for Web, Phone, and Tablet as needed

### Method B: Use Static Chart Definition ID

1. Add any **text field** or **subgrid placeholder** to the form section
2. Replace it with the **VisualHost** control
3. In the PCF properties, set **chartDefinitionId** to the GUID of your chart definition record

---

## Step 4: Configure PCF Properties

After adding the VisualHost control, configure its properties:

| Property | Type | Description | Default |
|----------|------|-------------|---------|
| **chartDefinition** | Lookup | Bound lookup to `sprk_chartdefinition`. Takes precedence over static ID. | - |
| **chartDefinitionId** | Text | Static GUID of chart definition record. Use when not binding a lookup. | - |
| **contextFieldName** | Text | Field name for context filtering (e.g., `sprk_regardingrecordid`). Filters data to current record's related records. | - |
| **fetchXmlOverride** | Text | FetchXML query override. Highest query priority - overrides all other data sources. | - |
| **height** | Number | Chart height in pixels. | Auto |
| **showToolbar** | Yes/No | Show the expand button in the upper right corner. | Yes |
| **enableDrillThrough** | Yes/No | Enable click interactions and drill-through navigation. | Yes |
| **valueFormatOverride** | Text | Override chart definition value format for this placement. Values: shortNumber, letterGrade, percentage, wholeNumber, decimal, currency. | - |
| **width** | Number | Width in pixels for card sizing. | Auto |
| **columns** | Number | Number of cards per row in MetricCard matrix layout. | Auto |
| **justification** | Text | Content alignment: left, center, right. | left |

### Priority: chartDefinition Lookup vs chartDefinitionId

If both are provided:
- The **lookup binding** (`chartDefinition`) takes precedence
- The **static ID** (`chartDefinitionId`) is used as a fallback when the lookup is empty

This allows flexible configuration: bind a lookup for dynamic chart selection, or use a static ID for fixed dashboards.

---

## Visual Type Configuration Reference

### MetricCard (100000000)

Displays a single metric value or a matrix of metric cards in a responsive CSS Grid layout.

#### Single Card Mode (Default)

When **Group By Field** is not set, MetricCard displays a single large number with label and optional trend indicator. This is the existing behavior.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | MetricCard | |
| Entity Logical Name | Your entity (e.g., `sprk_document`) | |
| Group By Field | *(leave empty)* | Single card mode |
| Aggregation Type | Count (default) or Sum/Avg | |

**Options JSON Example (single card):**
```json
{
  "trend": "up",
  "trendValue": 12.5,
  "compact": true
}
```

#### Matrix Mode (v1.2.33+)

When **Group By Field** is set, the DataAggregationService produces multiple data points (one per group). MetricCard renders these as a **MetricCardMatrix** -- a responsive CSS Grid of individual metric cards.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | MetricCard | |
| Entity Logical Name | Your entity | |
| Group By Field | Field to segment cards (e.g., `sprk_performancearea`) | Required for matrix mode |
| Aggregation Type | Count, Sum, Average, etc. | |
| Aggregation Field | Numeric field (e.g., `sprk_amount`) | Required for Sum/Average/Min/Max |
| Value Format | How to format card values | Optional (defaults to Short Number) |
| Color Source | How per-card colors are determined | Optional (defaults to None) |

#### New Dataverse Fields (v1.2.33)

**Value Format** (`sprk_valueformat`) -- choice field on `sprk_chartdefinition`:

| Label | Value | Description |
|-------|-------|-------------|
| Short Number | 100000000 | Abbreviates large numbers (e.g., 1.2K, 3.5M) |
| Letter Grade | 100000001 | Converts decimal 0.00-1.00 to letter grade (A+, B, C, etc.) |
| Percentage | 100000002 | Displays as percentage (e.g., 85%) |
| Whole Number | 100000003 | Displays as integer with no decimals |
| Decimal | 100000004 | Displays with 2 decimal places |
| Currency | 100000005 | Displays with currency symbol and formatting |

**Color Source** (`sprk_colorsource`) -- choice field on `sprk_chartdefinition`:

| Label | Value | Description |
|-------|-------|-------------|
| None | 100000000 | No per-card coloring (default neutral theme) |
| Option Set Color | 100000001 | Uses the Dataverse option set hex color as the card accent color |
| Value Threshold | 100000002 | Applies Fluent token sets (brand, warning, danger) based on value ranges defined in `colorThresholds` |

#### Configuration JSON Options (Matrix Mode)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `cardSize` | string | `"medium"` | Card size: `"small"`, `"medium"`, `"large"` |
| `sortBy` | string | `"label"` | Sort order: `"label"` (A-Z), `"value"` (desc), `"valueAsc"`, `"optionSetOrder"` (Dataverse option order) |
| `maxCards` | number | *(all)* | Maximum number of cards to display |
| `compact` | boolean | `false` | Use compact card layout with reduced padding |
| `showTitle` | boolean | `true` | Show the chart title above the matrix |
| `cardDescription` | string | -- | Template for card description text. Placeholders: `{value}` (raw), `{formatted}` (formatted value), `{label}` (group label), `{count}` (record count) |
| `nullDisplay` | string | `"--"` | Text to show when a card has no data |
| `nullDescription` | string | -- | Description text for null/no-data cards |
| `iconMap` | object | -- | Maps group labels to Fluent icon names. Supported icons: `gavel`, `money`, `target`, `calendar`, `alert`, `checkmark`, `document`, `people`, `star`, `clipboard` |
| `colorThresholds` | array | -- | Array of `{ "range": [min, max], "tokenSet": "brand"|"warning"|"danger" }` objects. Used when Color Source = Value Threshold |

**Example: Basic matrix configuration:**
```json
{
  "cardSize": "medium",
  "sortBy": "value",
  "showTitle": true,
  "compact": false
}
```

**Example: Grade-style cards with icons and color thresholds:**
```json
{
  "cardSize": "medium",
  "sortBy": "optionSetOrder",
  "iconMap": {
    "Guidelines": "gavel",
    "Budget": "money",
    "Outcomes": "target"
  },
  "cardDescription": "{formatted} compliance",
  "nullDisplay": "N/A",
  "nullDescription": "No data available",
  "colorThresholds": [
    { "range": [0.85, 1.00], "tokenSet": "brand" },
    { "range": [0.70, 0.84], "tokenSet": "warning" },
    { "range": [0.00, 0.69], "tokenSet": "danger" }
  ]
}
```

---

### BarChart (100000001)

Vertical or horizontal bar chart with grouped/aggregated data.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | BarChart | |
| Entity Logical Name | Your entity | |
| Group By Field | Field to segment bars (e.g., `statuscode`) | Required |
| Aggregation Type | Count, Sum, etc. | |

**Options JSON:**
```json
{
  "orientation": "horizontal",
  "showLabels": true,
  "showLegend": true,
  "showTitle": true
}
```

---

### LineChart (100000002) / AreaChart (100000003)

Line or area chart for trend visualization.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | LineChart or AreaChart | AreaChart fills area below line |
| Entity Logical Name | Your entity | |
| Group By Field | Field for X-axis (e.g., date field, status) | Required |

**Options JSON:**
```json
{
  "showLegend": true,
  "showTitle": true,
  "lineColor": "#0078d4"
}
```

---

### DonutChart (100000004)

Donut/pie chart for distribution visualization.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | DonutChart | |
| Entity Logical Name | Your entity | |
| Group By Field | Field to segment slices (e.g., `sprk_priority`) | Required |

**Options JSON:**
```json
{
  "innerRadius": 60,
  "showCenterValue": true,
  "centerLabel": "Total",
  "showLegend": true,
  "showTitle": true
}
```

---

### StatusBar (100000005)

Horizontal colored bar showing distribution of statuses.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | StatusBar | |
| Entity Logical Name | Your entity | |
| Group By Field | Status or category field | Required |

**Options JSON:**
```json
{
  "showLabels": true,
  "showCounts": true,
  "showTitle": true,
  "barHeight": 24
}
```

---

### Calendar (100000006)

Calendar heat map showing event counts per date.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | Calendar | |
| Entity Logical Name | Your entity | |
| Group By Field | Date field (e.g., `sprk_duedate`) | Required |

**Options JSON:**
```json
{
  "showTitle": true,
  "showNavigation": true
}
```

---

### MiniTable (100000007)

Compact ranked table with label/value pairs.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | MiniTable | |
| Entity Logical Name | Your entity | |
| Group By Field | Field for row labels | Required |

**Options JSON:**
```json
{
  "topN": 5,
  "showRank": true,
  "showTitle": true,
  "columns": [
    { "key": "label", "header": "Category", "width": "60%" },
    { "key": "value", "header": "Count", "width": "40%", "isValue": true }
  ]
}
```

---

### DueDateCard (100000008)

Single event due date card showing overdue/upcoming status for the current record.

**Use Case:** Place on an **Event form** to show that event's due date information.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | DueDateCard | |
| Entity Logical Name | `sprk_event` | The entity of the current record |

**PCF Property Settings:**
| Property | Value | Notes |
|----------|-------|-------|
| contextFieldName | *(leave empty)* | Not needed - uses current record ID directly |

The DueDateCard fetches the current form record directly using `contextRecordId` and displays a single card.

---

### DueDateCardList (100000009)

List of event due date cards for records related to the current parent form.

**Use Case:** Place on a **Matter form** (or other parent entity) to show all related upcoming events.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | DueDateCardList | |
| Entity Logical Name | `sprk_event` | The entity to query events from |
| Base View ID | GUID of a saved view (e.g., "Tasks 14 Days") | Recommended: use a view to control which events appear |
| Context Field Name | `sprk_regardingrecordid` | The field on Event that stores the parent record's ID |
| Max Display Items | `5` or `10` | How many cards to show |
| View List Tab Name | `tabEvents` | Optional: form tab name for "View All" link |

**PCF Property Settings:**
| Property | Value | Notes |
|----------|-------|-------|
| contextFieldName | `sprk_regardingrecordid` | Must match the chart definition's Context Field Name |

**Important:** The Context Field Name must be the **field on the child entity** (Event) that points back to the parent entity. For Events, this is `sprk_regardingrecordid` (a text field containing the parent record's GUID). For direct lookup fields on other entities, use the WebAPI format with `_` prefix and `_value` suffix (e.g., `_sprk_matterid_value`).

---

### ReportCardMetric (100000010)

> **v1.2.33:** ReportCardMetric is now a **preset for MetricCard**. The `GradeMetricCard` component is deprecated. When `sprk_visualtype = ReportCardMetric (100000010)`, the system auto-applies grade defaults via `cardConfigResolver` and renders using the MetricCard pipeline (including matrix mode support).

Letter grade card with color-coded styling for KPI performance areas (Guidelines, Budget, Outcomes). Displays a grade derived from a decimal value (0.00-1.00) with automatic color coding and contextual text.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | ReportCardMetric | Preset for MetricCard -- auto-applies grade defaults |
| Entity Logical Name | `sprk_matter` or `sprk_project` | The entity that has grade fields |
| Aggregation Field | Grade field (e.g., `sprk_guidelinecompliancegrade_current`) | Must return a decimal 0.00-1.00 |
| Aggregation Type | Average (100000002) | How to aggregate the grade field |
| Configuration JSON | See below | Icon, template, color rules (now feeds into ICardConfig system) |

**Auto-Applied Defaults via `cardConfigResolver`:**

When Visual Type = ReportCardMetric, the following defaults are automatically applied. All can be overridden via Configuration JSON or the new Dataverse fields (`sprk_valueformat`, `sprk_colorsource`):

| Setting | Default Value |
|---------|---------------|
| Value Format | `letterGrade` |
| Color Source | `valueThreshold` |
| Color Thresholds | 0.85-1.00 = brand/blue, 0.70-0.84 = warning/yellow, 0.00-0.69 = danger/red |
| Icons | Guidelines = Gavel, Budget = Money, Outcomes = Target |
| Null Display | `"N/A"` |
| Null Description | `"No grade data available for {areaName}"` |
| Sort | `optionSetOrder` |

**New Approach (v1.2.33): Single Definition with Matrix Mode**

Instead of creating 3 separate chart definitions (one per grade area), you can now use a **single chart definition** with Group By Field set to the performance area field. This renders all 3 grades as a matrix of cards in a responsive grid. See [Use Case 5](#use-case-5-kpi-grade-cards-on-a-matter-form-report-card) for details.

**Grade Value to Letter Mapping:**

| Decimal Range | Letter | Color |
|---------------|--------|-------|
| 1.00 | A+ | Blue |
| 0.95-0.99 | A | Blue |
| 0.90-0.94 | B+ | Blue |
| 0.85-0.89 | B | Blue |
| 0.80-0.84 | C+ | Yellow |
| 0.75-0.79 | C | Yellow |
| 0.70-0.74 | D+ | Yellow |
| 0.65-0.69 | D | Red |
| 0.00-0.64 | F | Red |
| null | N/A | Grey |

**Color Rules:**

| Range | Color | Meaning |
|-------|-------|---------|
| 0.85-1.00 | Blue (brand) | Good performance |
| 0.70-0.84 | Yellow (warning) | Caution |
| 0.00-0.69 | Red (danger) | Poor performance |
| null | Grey (neutral) | No data available |

**Configuration JSON (Reference):**

The Configuration JSON format is still supported and now feeds into the `ICardConfig` system used by the MetricCard pipeline:

```json
{
  "icon": "guidelines",
  "contextTemplate": "You have a {grade}% in {area} compliance",
  "colorRules": [
    { "range": [0.85, 1.00], "color": "blue" },
    { "range": [0.70, 0.84], "color": "yellow" },
    { "range": [0.00, 0.69], "color": "red" }
  ]
}
```

**Configuration JSON Fields:**

| Field | Type | Required | Default | Description |
|-------|------|----------|---------|-------------|
| `icon` | string | Yes | -- | Icon key: `"guidelines"`, `"budget"`, `"outcomes"` |
| `contextTemplate` | string | No | `"You have a {grade}% in {area} compliance"` | Template with `{grade}` (percentage) and `{area}` (area name) placeholders |
| `colorRules` | array | No | Default (blue/yellow/red) | Custom color range mappings |

**Available Icon Keys:**

| Key | Icon | Use For |
|-----|------|---------|
| `guidelines` | Gavel | Rules/legal compliance areas |
| `budget` | Money | Financial/budget areas |
| `outcomes` | Target | Goals/outcomes areas |

**Null/No-Data State:** When no grade data exists (null), the card shows "N/A" with grey styling and contextual text: "No grade data available for {areaName}".

---

## Click Actions Configuration

Configure what happens when a user clicks on a chart element or card.

### Available Click Actions

| Action | Value | Behavior |
|--------|-------|----------|
| **None** | 100000000 | No click action (default) |
| **Open Record Form** | 100000001 | Opens a record form in a modal dialog |
| **Open Side Pane** | 100000002 | Opens a Custom Page in the side pane |
| **Navigate to Page** | 100000003 | Navigates to a Custom Page |
| **Open Dataset Grid** | 100000004 | Triggers the expand/drill-through workspace |

### Click Action Fields

| Field | Description | Used By |
|-------|-------------|---------|
| **On Click Action** (`sprk_onclickaction`) | Which action to perform | All click actions |
| **On Click Target** (`sprk_onclicktarget`) | Entity logical name (for forms) or Custom Page name | OpenRecordForm, OpenSidePane, NavigateToPage |
| **On Click Record Field** (`sprk_onclickrecordfield`) | Field on the clicked record to use as the record ID | OpenRecordForm |

### Example: Open Event Form on Card Click

```
On Click Action: Open Record Form (100000001)
On Click Target: sprk_event
On Click Record Field: sprk_eventid
```

When a user clicks a DueDateCard, it opens the Event record form in a modal dialog.

---

## Drill-Through Target Configuration

### Overview

The **Drill-Through Target** field (`sprk_drillthroughtarget`) controls what opens when a user clicks the **expand button** (upper-right icon) on a VisualHost chart. This enables charts to open a context-filtered dataset grid in a dialog, showing the underlying data behind the visualization.

### How It Works

| `sprk_drillthroughtarget` | Expand Button Behavior |
|---------------------------|------------------------|
| **Set** (e.g., `sprk_eventspage.html`) | Opens the named web resource in a Dataverse dialog (90% width, 85% height) with context parameters |
| **Not set** (empty) | Falls back to a standard entity list dialog showing all records (no context filtering) |

### Configuration Fields

| Field | Description | Example |
|-------|-------------|---------|
| **Drill-Through Target** (`sprk_drillthroughtarget`) | Web resource name (including extension) | `sprk_eventspage.html` |
| **Context Field Name** (`sprk_contextfieldname`) | Field on the entity that references the parent record — used to filter the drill-through grid | `sprk_regardingrecordid` |
| **Base View ID** (`sprk_baseviewid`) | Saved view GUID — passed to the web resource for query source | GUID of "Active Tasks" view |

The PCF control property `contextFieldName` must also be set on the form to provide the current record's GUID.

### Parameters Passed to the Web Resource

When the expand button is clicked, VisualHost passes these parameters to the web resource via a URL-encoded `data` query string:

| Parameter | Value Source | Purpose |
|-----------|-------------|---------|
| `entityName` | `sprk_entitylogicalname` | Entity the chart queries |
| `filterField` | Context field name (cleaned) | Field to filter the grid on |
| `filterValue` | Current form record GUID | Value to filter by |
| `viewId` | `sprk_baseviewid` (cleaned) | Saved view for the grid to use |
| `mode` | Always `"dialog"` | Tells the web resource it's running in a dialog |

### Setting Up Drill-Through to the Events Page

**Goal:** When a user clicks the expand button on an Events bar chart on a Matter form, open the Events Page in a dialog showing only events for that Matter.

1. **Chart Definition:**

   | Field | Value |
   |-------|-------|
   | Name | Events by Type |
   | Visual Type | BarChart (100000001) |
   | Entity Logical Name | `sprk_event` |
   | Group By Field | `_sprk_eventtype_ref_value` |
   | Base View ID | *(GUID of "Active Tasks" view)* |
   | Context Field Name | `sprk_regardingrecordid` |
   | **Drill-Through Target** | `sprk_eventspage.html` |

2. **PCF Properties on Form:**

   | Property | Value |
   |----------|-------|
   | contextFieldName | `sprk_regardingrecordid` |
   | enableDrillThrough | Yes |
   | showToolbar | Yes |

3. **Result:** Clicking the expand icon opens the Events Page in a dialog, filtered to show only events where `sprk_regardingrecordid` matches the current Matter's GUID. The Calendar side pane is suppressed in the dialog.

### Setting Up Drill-Through for Other Web Resources

Any HTML web resource that implements the parameter contract can be used as a drill-through target:

1. **Parse parameters:** Read `window.location.search`, extract the `data` param, parse as `URLSearchParams`
2. **Detect dialog mode:** Check if `mode === "dialog"` and adjust UI accordingly
3. **Apply context filter:** Use `filterField` and `filterValue` to filter data queries
4. **Set `sprk_drillthroughtarget`:** Enter the web resource name (e.g., `sprk_mydatasetgrid.html`)

### Without Drill-Through Target (Fallback)

If `sprk_drillthroughtarget` is not set, the expand button opens a standard `entitylist` dialog. This shows all records from the entity (filtered by `viewId` if configured) but does **not** support context filtering — the `entitylist` page type in `navigateTo` does not accept `filterXml`.

---

## Data Querying Options

VisualHost supports four data sources with descending priority:

### Priority 1: PCF FetchXML Override (Highest)

Set the `fetchXmlOverride` PCF property to provide per-deployment FetchXML. This overrides all other data sources.

**Use Case:** Different FetchXML per form placement without changing the chart definition.

### Priority 2: Custom FetchXML on Chart Definition

Set `sprk_fetchxmlquery` on the chart definition record with custom FetchXML.

**Use Case:** Complex queries that can't be expressed as a saved view.

**Supports parameter substitution:**
- `{contextRecordId}` - Current form record ID
- `{currentUserId}` - Current Dataverse user ID
- `{currentDate}` - Today's date (YYYY-MM-DD)
- `{currentDateTime}` - Current date/time (ISO format)

Additional parameters can be defined in `sprk_fetchxmlparams` as JSON:
```json
{
  "customParam1": "value1",
  "customParam2": "value2"
}
```

### Priority 3: Saved View

Set `sprk_baseviewid` to the GUID of a Dataverse saved view (system view).

**Use Case:** Reuse existing views for data filtering and column selection.

The view's FetchXML is retrieved at runtime and can be augmented with context filters.

### Priority 4: Direct Entity Query (Lowest)

If no FetchXML source is configured, VisualHost falls back to querying all records from `sprk_entitylogicalname` with OData.

**Use Case:** Simple scenarios where you want all records aggregated.

### Context Filtering (Filtering by Related Records)

Whether a list view filters by related records depends on whether you have the **Context Field Name** (`sprk_contextfieldname`) configured on the chart definition.

#### Without Context Field Name

If `sprk_contextfieldname` is **not set**, the control shows **all records** returned by the view — no filtering by the current form record. For example, a DueDateCardList on a Matter form would show every event from the view, not just events related to that Matter.

#### With Context Field Name

If `sprk_contextfieldname` is set (e.g., `sprk_regardingrecordid`), VisualHost injects a filter into the view's FetchXML at runtime to show **only records related to the current form record**.

#### How the Context Filter Flow Works

1. The view's FetchXML is retrieved from the saved view specified in `sprk_baseviewid` (e.g., a "Tasks Due Next 14 Days" view)
2. The `contextRecordId` is read from the current form (the GUID of the record the form is displaying)
3. If **both** `sprk_contextfieldname` is set AND `contextRecordId` is available:
   - `ViewDataService.injectContextFilter()` modifies the FetchXML to add:
     ```xml
     <condition attribute="sprk_regardingrecordid" operator="eq" value="{recordId}" />
     ```
   - The modified FetchXML is then executed against Dataverse
4. If either value is missing, the view runs unfiltered

#### Field Name Transformation

The context filter automatically transforms WebAPI lookup field format to a FetchXML attribute name when needed:

```
_sprk_matterid_value  →  sprk_matterid   (lookup fields: strips _ prefix and _value suffix)
sprk_regardingrecordid → sprk_regardingrecordid  (text fields: no transformation needed)
```

The control handles this conversion internally. For Events, use `sprk_regardingrecordid` (a text field containing the parent GUID). For direct lookup fields on other entities, use the WebAPI format (`_fieldname_value`).

#### Required Configuration for Context Filtering

To filter a DueDateCardList to show only events related to the current form record, you need **both** the chart definition and PCF property configured:

**Chart Definition fields:**

| Field | Value | Purpose |
|-------|-------|---------|
| Base View ID (`sprk_baseviewid`) | GUID of saved view (e.g., "Tasks Due Next 14 Days") | Controls which events and columns to query |
| Context Field Name (`sprk_contextfieldname`) | `sprk_regardingrecordid` | Tells VisualHost which field to filter on |

**PCF Control properties:**

| Property | Value | Purpose |
|----------|-------|---------|
| contextFieldName | `sprk_regardingrecordid` | Provides the runtime context record ID from the current form |

**Important:** The `contextFieldName` value must be the **field on the child entity** that references the parent record. For Events, use `sprk_regardingrecordid` (text field). For direct lookups on other entities, use WebAPI format with `_` prefix and `_value` suffix. The chart definition and PCF property values should match.

#### Context Filtering with Different Query Sources

Context filtering works with all query priorities:

| Query Source | How Context Filter is Applied |
|-------------|-------------------------------|
| PCF FetchXML Override | Filter injected into the override FetchXML |
| Custom FetchXML | Filter injected (or use `{contextRecordId}` parameter) |
| Saved View | Filter injected into the view's FetchXML |
| Direct Entity Query | Filter appended as a FetchXML condition in the fallback query |

---

## Use Cases: Visual Type Setup Walkthroughs

### Use Case 1: DueDateCard on an Event Form

**Goal:** Show a single due date card for the event being viewed.

The DueDateCard visual type fetches **one event record** using the current form's record ID as the event primary key. This means it is designed to be placed **on an Event form** (where the form's `contextRecordId` IS the event ID).

**Step-by-step:**

1. **Create Chart Definition:**

   | Field | Value |
   |-------|-------|
   | Name | Event Due Date |
   | Visual Type | DueDateCard (100000008) |
   | Entity Logical Name | `sprk_event` |

   Leave all other fields empty — no view, no context field, no aggregation.

2. **Add VisualHost control to your Event form:**
   - Set `chartDefinitionId` to the chart definition GUID (or bind a lookup)
   - Leave `contextFieldName` empty

3. **How it works at runtime:**
   - The control reads `contextRecordId` from the form (the Event's GUID)
   - Executes a FetchXML query: `<condition attribute="sprk_eventid" operator="eq" value="{eventGuid}" />`
   - Displays one EventDueDateCard with overdue/upcoming status, event type color, and assigned-to

**Important:** Do NOT place DueDateCard on a parent form (Matter, Project, etc.) — it would try to look up an Event by the parent's GUID, which would fail.

---

### Use Case 2: DueDateCardList on a Matter Form (Related Events)

**Goal:** Show a list of upcoming events related to the current Matter.

The DueDateCardList fetches **multiple event records** filtered to the current form record using context filtering.

**Step-by-step:**

1. **Create a Saved View** (e.g., "Open Tasks 7 Days"):
   - Entity: `sprk_event`
   - Filters: Due Date within next 7 days, Status = Open
   - Note the view's GUID from the URL

2. **Create Chart Definition:**

   | Field | Value |
   |-------|-------|
   | Name | Upcoming Events |
   | Visual Type | DueDateCardList (100000009) |
   | Entity Logical Name | `sprk_event` |
   | Base View ID | *(GUID of your saved view)* |
   | Context Field Name | `sprk_regardingrecordid` |
   | Max Display Items | `5` |
   | View List Tab Name | `tabEvents` *(optional — form tab for "View All" link)* |

3. **Add VisualHost control to your Matter form:**
   - Set `chartDefinitionId` to the chart definition GUID
   - Set `contextFieldName` to `sprk_regardingrecordid`

4. **How it works at runtime:**
   - Retrieves the saved view's FetchXML from Dataverse
   - Injects a context filter: `<condition attribute="sprk_regardingrecordid" operator="eq" value="{currentMatterGuid}" />`
   - Executes the modified FetchXML and renders up to 5 EventDueDateCards
   - If `sprk_viewlisttabname` is set, shows a "View All" link that focuses that tab on the form

---

### Use Case 3: MetricCard Showing Event Count from a View

**Goal:** Display a single number showing how many events match a saved view's criteria (e.g., "5 tasks due in 7 days").

The MetricCard uses the **DataAggregationService** which fetches records from the configured data source and aggregates them client-side.

**Step-by-step:**

1. **Create a Saved View** (e.g., "Open Tasks 7 Days"):
   - Entity: `sprk_event`
   - Filters: Due Date within next 7 days, Status = Active
   - Note the view's GUID

2. **Create Chart Definition:**

   | Field | Value |
   |-------|-------|
   | Name | Tasks Due (7 Days) |
   | Visual Type | MetricCard (100000000) |
   | Entity Logical Name | `sprk_event` |
   | Base View ID | *(GUID of your saved view)* |
   | Aggregation Type | Count (100000000) |
   | Context Field Name | `sprk_regardingrecordid` *(if you want to filter to the current record)* |

   Leave **Group By Field** empty for a simple total count.

3. **Add VisualHost control to your form:**
   - Set `chartDefinitionId` to the chart definition GUID
   - Set `contextFieldName` to `sprk_regardingrecordid` (if filtering by parent record)

4. **How it works at runtime:**
   - DataAggregationService detects the `sprk_baseviewid` and retrieves the saved view's FetchXML
   - If context filtering is configured, injects the filter condition
   - Executes the FetchXML query and counts the returned records
   - MetricCard displays the count as a large number

**Troubleshooting MetricCard showing "0":**
- Verify the **Base View ID** is correct and the view actually returns records when accessed directly
- Confirm the view's filters are valid (date ranges, status values)
- If using context filtering, ensure the current record has related events
- Check the browser console (F12) for FetchXML execution errors

---

## Troubleshooting

### "Chart definition not found"
- Verify the chart definition record exists and is active
- Check that the GUID in `chartDefinitionId` matches the record
- If using a lookup binding, ensure the lookup field has a value on the current record

### "No data available"
- Verify `sprk_entitylogicalname` is correct
- Check that the saved view (if configured) returns records
- Verify context filtering isn't excluding all records

### "Failed to load events" (DueDateCardList)
- Check `sprk_contextfieldname` uses the correct field name (e.g., `sprk_regardingrecordid` for Events)
- Verify the saved view ID is correct and the view is accessible
- Check browser console for detailed error messages

### ReportCardMetric shows "N/A" instead of a grade
- As of v1.2.33, ReportCardMetric uses the MetricCard pipeline with `resolveCardConfig` for rendering
- This is normal when no KPI assessments have been submitted for that area
- Submit at least one KPI assessment via the Quick Create form, which triggers the calculator API
- Refresh the form after saving the assessment — grades update asynchronously
- Verify the grade fields on the entity record have non-null values (e.g., `sprk_guidelinecompliancegrade_current`)

### ReportCardMetric not rendering (blank space)
- Verify `sprk_visualtype` is set to `100000010` on the chart definition record
- If the Visual Type choice field doesn't have a "ReportCardMetric" option, you need to add it — see [Adding the ReportCardMetric Visual Type](#adding-the-reportcardmetric-visual-type) below
- Ensure VisualHost PCF solution v1.2.33 or later is imported
- Check browser console (F12) for rendering errors

### Bar chart shows a single "(Blank)" bar
- **Wrong field format for Group By Field:** Choice/optionset fields use `fieldname` (e.g., `sprk_documenttype`). Lookup fields use `_fieldname_value` (e.g., `_sprk_eventtype_ref_value`). See [Group By Field Format](#group-by-field-format).
- **Column missing from view (pre-v1.2.14):** If using a version before v1.2.14, the Group By Field column must be included in the saved view's column list. v1.2.14+ auto-injects missing columns.
- Check browser console for `[DIAG] groupByField=` — if raw value shows `(undefined)`, the field name format is wrong.

### "Could not find a property named..."
- Navigation property names for `$expand` must use the **relationship schema name**, not the lookup attribute name
- Example: Use `sprk_event_EventType_n1` instead of `sprk_eventtype_ref`

### Version Badge Not Updating
- Verify the solution was imported successfully
- Clear browser cache (Ctrl+Shift+Delete)
- Check the version badge in the lower-left corner of the control (should show v1.2.33)

### Click Actions Not Working
- Verify `enableDrillThrough` is set to Yes on the PCF control
- Check that `sprk_onclickaction` is set on the chart definition
- Ensure `sprk_onclicktarget` has the correct entity name or Custom Page name
- Check browser console for Xrm API errors

### Drill-Through Dialog Not Opening
- Verify `sprk_drillthroughtarget` is set on the chart definition and contains the correct web resource name (e.g., `sprk_eventspage.html`)
- Verify the web resource exists in Dataverse — navigate to Settings → Web Resources and search for the name
- Check browser console (F12) for `navigateTo` errors
- Ensure `showToolbar` and `enableDrillThrough` are both set to Yes on the PCF control
- If using `pageType: "custom"` by mistake: the Events Page is a **web resource**, not a Custom Page — `sprk_drillthroughtarget` must be a web resource name

### Drill-Through Opens but Data Not Filtered
- Verify `sprk_contextfieldname` is set on the chart definition (e.g., `sprk_regardingrecordid`)
- Verify the PCF property `contextFieldName` is also set on the form control
- Check browser console for `[GridSection] Applying context filter:` log — if missing, the params were not passed
- Check browser console for `[EventsPage] Drill-through params:` log — verify `filterField` and `filterValue` are present
- Ensure the field name is correct for the entity (e.g., `sprk_regardingrecordid` for Events regarding records)

### Calendar Pane Still Appears in Drill-Through Dialog
- Verify the Events Page is at version 2.16.0 or later — check the version footer
- Check browser console for `[EventsPage] Dialog mode — skipping Calendar side pane registration` log
- If the log is missing, the `mode=dialog` parameter is not being received — check that VisualHost is passing the `data` params correctly

---

### Use Case 4: Drill-Through from Bar Chart to Events Page (Context-Filtered)

**Goal:** Place a bar chart on a Matter form showing events by type. When the user clicks the expand button, open the Events Page in a dialog showing only events for that Matter.

**Step-by-step:**

1. **Ensure the Events Page web resource exists:**
   - The web resource `sprk_eventspage.html` must be deployed to your Dataverse environment
   - Deploy using `scripts/Deploy-EventsPage.ps1` or import manually
   - Requires Events Page version 2.16.0+ for dialog mode support

2. **Create Chart Definition:**

   | Field | Value |
   |-------|-------|
   | Name | Events by Type |
   | Visual Type | BarChart (100000001) |
   | Entity Logical Name | `sprk_event` |
   | Group By Field | `_sprk_eventtype_ref_value` |
   | Aggregation Type | Count (100000000) |
   | Base View ID | *(GUID of "Active Tasks" view)* |
   | Context Field Name | `sprk_regardingrecordid` |
   | Drill-Through Target | `sprk_eventspage.html` |

3. **Add VisualHost control to your Matter form:**
   - Set `chartDefinitionId` to the chart definition GUID
   - Set `contextFieldName` to `sprk_regardingrecordid`
   - Set `showToolbar` to Yes
   - Set `enableDrillThrough` to Yes

4. **How it works at runtime:**
   - The bar chart renders events grouped by type, filtered to the current Matter
   - The expand icon appears in the upper-right corner
   - Clicking expand calls `Xrm.Navigation.navigateTo` with:
     - `pageType: "webresource"`, `webresourceName: "sprk_eventspage.html"`
     - `data: "entityName=sprk_event&filterField=sprk_regardingrecordid&filterValue={matterGuid}&mode=dialog"`
   - The Events Page opens in a 90% × 85% dialog
   - The Events Page detects dialog mode, skips the Calendar side pane
   - `GridSection` injects a FetchXML condition: `<condition attribute="sprk_regardingrecordid" operator="eq" value="{matterGuid}" />`
   - Only events related to the current Matter are displayed

---

### Use Case 5: KPI Grade Cards on a Matter Form (Report Card)

**Goal:** Display three grade cards (Guidelines, Budget, Outcomes) on a Matter form's Report Card tab, each showing the current performance grade as a letter with color coding.

#### Option A: Single Chart Definition with Matrix Mode (New - Recommended)

Use one chart definition with Group By Field to render all 3 grades as a responsive matrix. This approach requires fewer chart definitions and provides responsive layout automatically.

**Step-by-step:**

1. **Create a single Chart Definition record:**

   | Field | Value |
   |-------|-------|
   | Name | Performance Grades |
   | Visual Type | ReportCardMetric (100000010) |
   | Entity Logical Name | `sprk_matter` |
   | Group By Field | Performance area grouping field (e.g., field that segments into Guidelines/Budget/Outcomes) |
   | Aggregation Type | Average (100000002) |
   | Aggregation Field | Grade field (e.g., `sprk_compliancegrade_current`) |
   | Value Format | Letter Grade (100000001) |
   | Color Source | Value Threshold (100000002) |
   | Configuration JSON | See below |

   **Configuration JSON:**
   ```json
   {
     "cardSize": "medium",
     "sortBy": "optionSetOrder",
     "iconMap": {
       "Guidelines": "gavel",
       "Budget": "money",
       "Outcomes": "target"
     },
     "cardDescription": "{formatted} compliance",
     "nullDisplay": "N/A",
     "nullDescription": "No data available",
     "colorThresholds": [
       { "range": [0.85, 1.00], "tokenSet": "brand" },
       { "range": [0.70, 0.84], "tokenSet": "warning" },
       { "range": [0.00, 0.69], "tokenSet": "danger" }
     ]
   }
   ```

2. **Add one VisualHost control to the Matter form:**
   - Place it in the Report Card tab section
   - Set `chartDefinitionId` to the chart definition GUID
   - No `contextFieldName` needed -- the card reads grade fields directly from the current record
   - One VisualHost instance renders all 3 cards in a responsive CSS Grid

3. **How it works at runtime:**
   - VisualHost loads the chart definition
   - ChartRenderer detects `VisualType = ReportCardMetric`
   - `resolveCardConfig` applies the ReportCardMetric preset defaults (letter grade format, value threshold colors, icons)
   - DataAggregationService groups data by the performance area field, producing 3 data points
   - MetricCardMatrix renders 3 cards in a responsive grid with grade letters, color coding, and icons
   - If no KPI assessments have been entered, cards show "N/A" in grey

#### Option B: Three Separate Chart Definitions (Legacy)

This approach still works -- ReportCardMetric preset auto-applies grade styling via `cardConfigResolver`. However, matrix mode (Option A) is preferred for fewer chart definitions and responsive layout.

**Step-by-step:**

1. **Create three Chart Definition records** (one per performance area):

   **Guidelines Card:**

   | Field | Value |
   |-------|-------|
   | Name | Guidelines |
   | Visual Type | ReportCardMetric (100000010) |
   | Entity Logical Name | `sprk_matter` |
   | Aggregation Field | `sprk_guidelinecompliancegrade_current` |
   | Aggregation Type | Average (100000002) |
   | Configuration JSON | `{"icon": "guidelines", "contextTemplate": "You have a {grade}% in {area} compliance"}` |

   **Budget Card:**

   | Field | Value |
   |-------|-------|
   | Name | Budget |
   | Visual Type | ReportCardMetric (100000010) |
   | Entity Logical Name | `sprk_matter` |
   | Aggregation Field | `sprk_budgetcompliancegrade_current` |
   | Aggregation Type | Average (100000002) |
   | Configuration JSON | `{"icon": "budget", "contextTemplate": "You have a {grade}% in {area} compliance"}` |

   **Outcomes Card:**

   | Field | Value |
   |-------|-------|
   | Name | Outcomes |
   | Visual Type | ReportCardMetric (100000010) |
   | Entity Logical Name | `sprk_matter` |
   | Aggregation Field | `sprk_outcomecompliancegrade_current` |
   | Aggregation Type | Average (100000002) |
   | Configuration JSON | `{"icon": "outcomes", "contextTemplate": "You have a {grade}% in {area} compliance"}` |

2. **Note the Chart Definition IDs** (GUIDs) from each record's URL after saving.

3. **Add three VisualHost controls to the Matter form:**
   - Place them in the Report Card tab section (side by side or in a 3-column section)
   - For each control, set `chartDefinitionId` to the corresponding chart definition GUID
   - No `contextFieldName` needed -- the card reads the current record's grade fields directly

4. **How it works at runtime:**
   - Each VisualHost instance loads its chart definition
   - ChartRenderer detects `VisualType = ReportCardMetric`
   - `resolveCardConfig` applies the ReportCardMetric preset defaults (letter grade format, value threshold colors, icons)
   - Reads the grade field value from the current matter record
   - Color coding is applied automatically (blue/yellow/red/grey)
   - If no KPI assessments have been entered, cards show "N/A" in grey

#### Updating Grades After Adding KPI Assessments (Both Options)

- KPI Assessment Quick Create form triggers the BFF API calculator endpoint on post-save
- The calculator recalculates all three area grades and updates the matter record
- Refresh the form to see updated grade cards

### Use Case 6: KPI Grade Cards on a Project Form

Same as Use Case 5, but for the Project entity. Change the entity and field names:

| Field | Matter Value | Project Value |
|-------|-------------|---------------|
| Entity Logical Name | `sprk_matter` | `sprk_project` |
| Guidelines Aggregation Field | `sprk_guidelinecompliancegrade_current` | `sprk_guidelinecompliancegrade_current` |
| Budget Aggregation Field | `sprk_budgetcompliancegrade_current` | `sprk_budgetcompliancegrade_current` |
| Outcomes Aggregation Field | `sprk_outcomecompliancegrade_current` | `sprk_outcomecompliancegrade_current` |

The grade field names are the same on both entities. Only the Entity Logical Name changes.

---

## Quick Reference: Common Configurations

### Bar Chart on Matter Form (Documents by Document Type — Choice Field)

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Documents by Document Type |
| Visual Type | BarChart (100000001) |
| Entity Logical Name | `sprk_document` |
| Base View ID | *(GUID of "All Documents" view)* |
| Group By Field | `sprk_documenttype` |
| Aggregation Field | `sprk_documentid` |
| Aggregation Type | Count (100000000) |
| Context Field Name | `sprk_regardingrecordid` |
| **PCF Properties** | |
| contextFieldName | `sprk_regardingrecordid` |

> **Note:** `sprk_documenttype` is a **choice field** — use the plain logical name (no `_` prefix or `_value` suffix). Labels like "Contract", "Invoice" are resolved automatically.

### Bar Chart on Matter Form (Events by Event Type — Lookup Field)

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Events by Event Type |
| Visual Type | BarChart (100000001) |
| Entity Logical Name | `sprk_event` |
| Base View ID | *(GUID of "Active Tasks" view)* |
| Group By Field | `_sprk_eventtype_ref_value` |
| Aggregation Field | `sprk_eventid` |
| Aggregation Type | Count (100000000) |
| Context Field Name | `sprk_regardingrecordid` |
| **PCF Properties** | |
| contextFieldName | `sprk_regardingrecordid` |

> **Note:** `sprk_eventtype_ref` is a **lookup field** — use the `_fieldname_value` format. Labels like "Task", "Reminder" are resolved automatically.

### DueDateCardList on Matter Form (Upcoming Events)

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Upcoming Events |
| Visual Type | DueDateCardList (100000009) |
| Entity Logical Name | `sprk_event` |
| Base View ID | *(GUID of "Tasks 14 Days" view)* |
| Context Field Name | `sprk_regardingrecordid` |
| Max Display Items | `5` |
| View List Tab Name | `tabEvents` |
| On Click Action | Open Record Form (100000001) |
| On Click Target | `sprk_event` |
| **PCF Properties** | |
| contextFieldName | `sprk_regardingrecordid` |
| enableDrillThrough | Yes |

### Metric Card with Saved View (Count from View)

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Tasks Due (7 Days) |
| Visual Type | MetricCard (100000000) |
| Entity Logical Name | `sprk_event` |
| Base View ID | *(GUID of "Open Tasks 7 Days" view)* |
| Aggregation Type | Count (100000000) |
| Context Field Name | `sprk_regardingrecordid` |
| **PCF Properties** | |
| contextFieldName | `sprk_regardingrecordid` |

### DueDateCard on Event Form

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Event Due Date |
| Visual Type | DueDateCard (100000008) |
| Entity Logical Name | `sprk_event` |
| **PCF Properties** | |
| contextFieldName | *(leave empty)* |

### ReportCardMetric — Guidelines Grade on Matter Form

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Guidelines |
| Visual Type | ReportCardMetric (100000010) |
| Entity Logical Name | `sprk_matter` |
| Aggregation Field | `sprk_guidelinecompliancegrade_current` |
| Aggregation Type | Average (100000002) |
| Configuration JSON | `{"icon": "guidelines", "contextTemplate": "You have a {grade}% in {area} compliance"}` |
| **PCF Properties** | |
| chartDefinitionId | *(GUID of the chart definition)* |

> **Note:** Leave all other chart definition fields empty (no Group By, no Context Field, no Base View ID). The ReportCardMetric reads the grade value directly from the aggregation field on the current record.
>
> **v1.2.33 Alternative:** Instead of three separate chart definitions (Guidelines, Budget, Outcomes), you can use a single chart definition with Group By Field set to the performance area field to render all three grades as a matrix. See [Use Case 5 Option A](#use-case-5-kpi-grade-cards-on-a-matter-form-report-card) for details.

### ReportCardMetric — Budget Grade on Matter Form

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Budget |
| Visual Type | ReportCardMetric (100000010) |
| Entity Logical Name | `sprk_matter` |
| Aggregation Field | `sprk_budgetcompliancegrade_current` |
| Aggregation Type | Average (100000002) |
| Configuration JSON | `{"icon": "budget", "contextTemplate": "You have a {grade}% in {area} compliance"}` |
| **PCF Properties** | |
| chartDefinitionId | *(GUID of the chart definition)* |

> **v1.2.33 Alternative:** Consider using a single chart definition with matrix mode instead of three separate definitions. See [Use Case 5 Option A](#use-case-5-kpi-grade-cards-on-a-matter-form-report-card).

### ReportCardMetric — Outcomes Grade on Matter Form

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Outcomes |
| Visual Type | ReportCardMetric (100000010) |
| Entity Logical Name | `sprk_matter` |
| Aggregation Field | `sprk_outcomecompliancegrade_current` |
| Aggregation Type | Average (100000002) |
| Configuration JSON | `{"icon": "outcomes", "contextTemplate": "You have a {grade}% in {area} compliance"}` |
| **PCF Properties** | |
| chartDefinitionId | *(GUID of the chart definition)* |

> **v1.2.33 Alternative:** Consider using a single chart definition with matrix mode instead of three separate definitions. See [Use Case 5 Option A](#use-case-5-kpi-grade-cards-on-a-matter-form-report-card).

### Bar Chart with Drill-Through to Events Page

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Events by Type |
| Visual Type | BarChart (100000001) |
| Entity Logical Name | `sprk_event` |
| Base View ID | *(GUID of "Active Tasks" view)* |
| Group By Field | `_sprk_eventtype_ref_value` |
| Aggregation Type | Count (100000000) |
| Context Field Name | `sprk_regardingrecordid` |
| **Drill-Through Target** | `sprk_eventspage.html` |
| **PCF Properties** | |
| contextFieldName | `sprk_regardingrecordid` |
| enableDrillThrough | Yes |
| showToolbar | Yes |

> **Note:** The Drill-Through Target must be the exact web resource name including extension. The Events Page web resource must be version 2.16.0+ for dialog mode support (calendar suppression and context filtering).

---

---

## Adding the ReportCardMetric Visual Type

If the `sprk_visualtype` choice field on `sprk_chartdefinition` does not yet include the **ReportCardMetric** option, you need to add it before creating KPI grade card definitions.

### Option Set Value

| Label | Value |
|-------|-------|
| ReportCardMetric | 100000010 |

### How to Add (Manual)

1. Navigate to **Settings** > **Customizations** > **Customize the System**
2. Expand **Entities** > **Chart Definition** (`sprk_chartdefinition`) > **Fields**
3. Open the **Visual Type** (`sprk_visualtype`) field
4. In the **Options** section, click **Add** to add a new option:
   - **Label**: `ReportCardMetric`
   - **Value**: `100000010`
5. Save and **Publish All Customizations**

### How to Add (Solution Import)

If using the SpaarkeCore solution or project deployment solution, the option set value should be included in the `customizations.xml`. Verify the solution XML includes the option in the `sprk_visualtype` field definition.

### Verification

After adding the option:
1. Open any Chart Definition record
2. Click the **Visual Type** dropdown
3. Confirm **ReportCardMetric** appears in the list
4. Create a test record with Visual Type = ReportCardMetric to verify it saves correctly

---

### Adding the New Chart Definition Choice Fields (v1.2.33)

Two new choice columns must be added to the `sprk_chartdefinition` entity to support MetricCard matrix mode value formatting and color configuration.

#### Value Format (`sprk_valueformat`)

| Label | Value |
|-------|-------|
| Short Number | 100000000 |
| Letter Grade | 100000001 |
| Percentage | 100000002 |
| Whole Number | 100000003 |
| Decimal | 100000004 |
| Currency | 100000005 |

#### Color Source (`sprk_colorsource`)

| Label | Value |
|-------|-------|
| None | 100000000 |
| Option Set Color | 100000001 |
| Value Threshold | 100000002 |

#### How to Add (Manual)

Follow the same process used for the Visual Type field:

1. Navigate to **Settings** > **Customizations** > **Customize the System**
2. Expand **Entities** > **Chart Definition** (`sprk_chartdefinition`) > **Fields**
3. Click **New** to create a new field:
   - **Display Name**: `Value Format`
   - **Name**: `sprk_valueformat`
   - **Data Type**: Choice (Option Set)
   - **Type**: Local Option Set (new)
   - Add all options from the **Value Format** table above with the specified values
   - **Default Value**: Short Number (100000000)
4. Save the field
5. Create a second new field:
   - **Display Name**: `Color Source`
   - **Name**: `sprk_colorsource`
   - **Data Type**: Choice (Option Set)
   - **Type**: Local Option Set (new)
   - Add all options from the **Color Source** table above with the specified values
   - **Default Value**: None (100000000)
6. Save the field
7. **Publish All Customizations**

#### How to Add (Solution Import)

If using the SpaarkeCore solution or project deployment solution, the new choice fields should be included in the `customizations.xml`. Verify the solution XML includes both `sprk_valueformat` and `sprk_colorsource` field definitions with their option set values on the `sprk_chartdefinition` entity.

#### Verification

After adding the fields:
1. Open any Chart Definition record
2. Confirm **Value Format** and **Color Source** fields appear on the form (you may need to add them to the form layout)
3. Verify each dropdown contains the expected options
4. Create a test record with Value Format = Letter Grade and Color Source = Value Threshold to verify they save correctly

---

*For architecture details and component reusability, see [VISUALHOST-ARCHITECTURE.md](VISUALHOST-ARCHITECTURE.md)*
