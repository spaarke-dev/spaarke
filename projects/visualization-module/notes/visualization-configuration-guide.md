# Visualization Configuration Guide

> **Last Updated**: 2026-01-02
> **Audience**: Administrators and Power Platform Makers
> **Purpose**: Complete guide to creating and configuring visuals using the Spaarke Visuals Framework

---

## Overview

The Spaarke Visuals Framework provides configuration-driven charts and visualizations for Model-Driven Apps. Instead of hard-coding charts, administrators configure visuals through Dataverse records and place them on forms using the **Visual Host** PCF control.

### Key Concepts

| Concept | Description |
|---------|-------------|
| **Reporting Entity** | Master list of Dataverse entities available for charting |
| **Reporting View** | Pre-configured views per entity that define the data set |
| **Chart Definition** | Configuration record that defines what chart to render |
| **Visual Host** | PCF control that renders charts on forms |

### Architecture Flow

```
Administrator Creates:            User Sees on Form:

[Reporting Entities]              [Account Form]
      ↓                                 ↓
[Reporting Views]                 ┌─────────────────┐
      ↓                           │ VisualHost PCF  │
[Chart Definitions] ────────────→ │ ┌─────────────┐ │
                                  │ │ Bar Chart   │ │
                                  │ │ ████ ██ █   │ │
                                  │ └─────────────┘ │
                                  └─────────────────┘
```

---

## Step 1: Configure Reporting Entities

Reporting Entities are a master list of Dataverse entities that can be used for visualizations. This provides a user-friendly dropdown when creating chart definitions.

### 1.1 Navigate to Reporting Entities

1. Open https://make.powerapps.com
2. Select your environment (e.g., **SPAARKE DEV 1**)
3. Navigate to **Apps** → Open your Model-Driven App
4. Find **Reporting Entities** in the sitemap (or use Advanced Find)

### 1.2 Create a Reporting Entity Record

For each entity you want available for charting, create a record:

| Field | Description | Example |
|-------|-------------|---------|
| **Display Name** | User-friendly name | "Documents" |
| **Logical Name** | Dataverse entity logical name | `sprk_document` |
| **Schema Name** | Entity schema name (optional) | `sprk_Document` |
| **Plural Name** | Plural display name (optional) | "Documents" |
| **Is Active** | Whether entity is available | Yes |

### 1.3 Standard Reporting Entities

Recommended entities to configure:

| Display Name | Logical Name | Use Case |
|--------------|--------------|----------|
| Documents | `sprk_document` | Document counts, status distribution |
| Matters | `sprk_matter` | Matter metrics, case tracking |
| Projects | `sprk_project` | Project status, timeline charts |
| Events | `sprk_event` | Calendar views, event counts |
| Invoices | `sprk_invoice` | Financial summaries, revenue charts |
| Accounts | `account` | Account metrics, industry breakdown |
| Contacts | `contact` | Contact counts, relationship charts |

### 1.4 Save and Verify

After creating reporting entities:
1. Save each record
2. Verify they appear in the Reporting Entities list
3. These will now be available when creating Chart Definitions

---

## Step 2: Configure Reporting Views

Reporting Views link Dataverse views (SavedQuery) to Reporting Entities. Each view defines the base data set for a chart.

### 2.1 Navigate to Reporting Views

1. In your Model-Driven App, find **Reporting Views**
2. Or navigate via **Tables** → **Reporting View** → **Data**

### 2.2 Create a Reporting View Record

For each view you want available for charting:

| Field | Description | Example |
|-------|-------------|---------|
| **View Name** | User-friendly name | "Active Documents" |
| **View ID** | GUID of the Dataverse view | `12345678-1234-1234-1234-123456789012` |
| **Reporting Entity** | Lookup to parent entity | Documents |
| **Is Default** | Default view for this entity | Yes/No |

### 2.3 Finding View GUIDs

To find the GUID of a Dataverse view:

**Method 1: Advanced Find**
1. Open the entity in Power Apps Maker
2. Navigate to **Views**
3. Open the desired view
4. The URL contains the view GUID: `...savedqueryid=12345678-1234-...`

**Method 2: Web API Query**
```
GET /api/data/v9.2/savedqueries?$filter=returnedtypecode eq 'sprk_document'&$select=name,savedqueryid
```

### 2.4 Recommended Views Per Entity

| Entity | View Name | Purpose |
|--------|-----------|---------|
| Documents | Active Documents | All non-archived documents |
| Documents | My Documents | Documents owned by current user |
| Documents | Documents This Week | Recently created documents |
| Matters | Active Matters | Open matters |
| Matters | My Matters | Matters assigned to current user |
| Projects | Active Projects | In-progress projects |
| Invoices | Pending Invoices | Unpaid invoices |

### 2.5 Related Records Filtering

When creating Chart Definitions, the Reporting View lookup automatically filters by the selected Reporting Entity. This is configured via:
- **Form**: Chart Definition Information form
- **Field**: Reporting View lookup
- **Filter**: Related records filtering on `sprk_reportingentity`

---

## Step 3: Understand Chart Types

The Visual Host supports 7 visual types. Choose based on your data and analysis goals.

### 3.1 Visual Type Reference

| Visual Type | Best For | Required Fields |
|-------------|----------|-----------------|
| **Metric Card** | Single KPI value | Entity, View, Aggregation Type |
| **Bar Chart** | Comparing categories | Entity, View, Group By Field |
| **Line Chart** | Trends over time | Entity, View, Group By (date field) |
| **Area Chart** | Cumulative trends | Entity, View, Group By (date field) |
| **Donut Chart** | Part-to-whole | Entity, View, Group By Field |
| **Status Bar** | Status distribution | Entity, View, Group By (status field) |
| **Mini Table** | Top N records | Entity, View |

### 3.2 Detailed Visual Type Descriptions

#### Metric Card (100000000)

Displays a single aggregate value with optional trend indicator.

**Use Cases:**
- Total active documents
- Sum of invoice amounts
- Average project duration

**Configuration:**
```
Visual Type: Metric Card
Aggregation Type: Count | Sum | Average | Min | Max
Aggregation Field: (required for Sum/Average/Min/Max)
Group By Field: (not used)
```

**Example:**
- Name: "Active Documents Count"
- Entity: Documents
- View: Active Documents
- Aggregation: Count

---

#### Bar Chart (100000001)

Vertical or horizontal bars comparing categories.

**Use Cases:**
- Documents by type
- Projects by status
- Invoices by client

**Configuration:**
```
Visual Type: Bar Chart
Aggregation Type: Count (or Sum for values)
Group By Field: Category field (e.g., sprk_documenttype)
Options JSON: {"orientation": "vertical"} (optional)
```

**Example:**
- Name: "Documents by Type"
- Entity: Documents
- View: Active Documents
- Group By: sprk_documenttype
- Aggregation: Count

---

#### Line Chart (100000002)

Lines showing trends over time.

**Use Cases:**
- New matters per month
- Document volume trends
- Revenue over quarters

**Configuration:**
```
Visual Type: Line Chart
Group By Field: Date field (e.g., createdon)
Options JSON: {"dateGrouping": "month", "showDataPoints": true}
```

**Example:**
- Name: "New Matters by Month"
- Entity: Matters
- View: All Matters
- Group By: createdon
- Aggregation: Count

---

#### Area Chart (100000003)

Filled area showing cumulative trends.

**Use Cases:**
- Cumulative project hours
- Running invoice totals
- Growth over time

**Configuration:**
```
Visual Type: Area Chart
Group By Field: Date field
Aggregation Field: Value field (for sum)
```

---

#### Donut Chart (100000004)

Circular chart showing proportions.

**Use Cases:**
- Document status breakdown
- Project allocation
- Invoice payment status

**Configuration:**
```
Visual Type: Donut Chart
Group By Field: Category field
Aggregation Type: Count or Sum
```

**Example:**
- Name: "Documents by Status"
- Entity: Documents
- View: Active Documents
- Group By: statuscode
- Aggregation: Count

---

#### Status Bar (100000005)

Horizontal segmented bar showing status distribution.

**Use Cases:**
- Project phase distribution
- Document approval status
- Task completion rates

**Configuration:**
```
Visual Type: Status Bar
Group By Field: Status/phase field
```

**Example:**
- Name: "Document Status Distribution"
- Entity: Documents
- View: Active Documents
- Group By: statuscode

---

#### Mini Table (100000007)

Displays top N records in a compact table.

**Use Cases:**
- Recent documents
- Top invoices by amount
- Upcoming deadlines

**Configuration:**
```
Visual Type: Mini Table
Options JSON: {"maxRows": 10, "columns": ["sprk_name", "createdon"]}
```

---

## Step 4: Create Chart Definitions

Chart Definitions are the core configuration records that tell Visual Host what to render.

### 4.1 Navigate to Chart Definitions

1. Open your Model-Driven App
2. Find **Chart Definitions** in the sitemap
3. Click **+ New** to create a new definition

### 4.2 Chart Definition Form Fields

#### Basic Information Section

| Field | Required | Description |
|-------|----------|-------------|
| **Name** | Yes | Display name for the chart (e.g., "Active Projects by Status") |
| **Visual Type** | Yes | Select from dropdown (Metric Card, Bar Chart, etc.) |

#### Data Source Section

| Field | Required | Description |
|-------|----------|-------------|
| **Reporting Entity** | Yes* | Select the entity (user-friendly lookup) |
| **Reporting View** | Yes* | Select the view (filters by entity) |
| **Entity Logical Name** | Auto | Auto-populated from Reporting Entity selection |
| **Base View ID** | Auto | Auto-populated from Reporting View selection |

*Using the lookups is recommended. The backing text fields are auto-populated.

#### Aggregation Section

| Field | Required | Description |
|-------|----------|-------------|
| **Aggregation Type** | No | Count (default), Sum, Average, Min, Max |
| **Aggregation Field** | Conditional | Required for Sum/Average/Min/Max |
| **Group By Field** | Conditional | Required for charts with categories |

#### Advanced Section

| Field | Required | Description |
|-------|----------|-------------|
| **Options JSON** | No | Additional configuration in JSON format |

### 4.3 Step-by-Step: Create a Bar Chart

Let's create a "Documents by Type" bar chart:

1. **Click** + New Chart Definition
2. **Name**: "Documents by Type"
3. **Visual Type**: Bar Chart
4. **Reporting Entity**: Select "Documents"
5. **Reporting View**: Select "Active Documents"
6. **Group By Field**: Enter `sprk_documenttype`
7. **Aggregation Type**: Count
8. **Click** Save

### 4.4 Step-by-Step: Create a Metric Card

Let's create an "Active Documents Count" metric:

1. **Click** + New Chart Definition
2. **Name**: "Active Documents Count"
3. **Visual Type**: Metric Card
4. **Reporting Entity**: Select "Documents"
5. **Reporting View**: Select "Active Documents"
6. **Aggregation Type**: Count
7. **Click** Save

### 4.5 Options JSON Examples

For advanced customization, use the Options JSON field:

**Bar Chart - Horizontal Orientation:**
```json
{
  "orientation": "horizontal"
}
```

**Line Chart - Monthly Grouping:**
```json
{
  "dateGrouping": "month",
  "showDataPoints": true
}
```

**Mini Table - Custom Columns:**
```json
{
  "maxRows": 5,
  "columns": ["sprk_name", "statuscode", "createdon"]
}
```

**Metric Card - Show Trend:**
```json
{
  "showTrend": true,
  "trendPeriod": "month"
}
```

---

## Step 5: Add Visual Host to Forms

Once you have Chart Definitions, add the Visual Host PCF control to display them on forms.

### 5.1 Two Binding Methods

Visual Host supports two ways to connect to Chart Definitions:

| Method | Use Case | Configuration |
|--------|----------|---------------|
| **Lookup Binding** | Different records show different charts | Requires lookup column on entity |
| **Static ID Binding** | Same chart for all records | Set Chart Definition ID property |

### 5.2 Method A: Lookup Binding (Recommended)

Use this when different records should display different charts.

#### A.1 Create Lookup Column

1. Go to **Tables** → your entity (e.g., Account)
2. Click **Columns** → **+ New column**
3. Configure:
   - **Display name**: Chart Definition
   - **Data type**: Lookup
   - **Related table**: Chart Definition (sprk_chartdefinition)
4. **Save**

#### A.2 Add to Form

1. Open the entity's **Main Form** in designer
2. Find "Chart Definition" in **Table columns** panel
3. **Drag** it onto the form
4. **Select** the lookup field
5. Click **+ Component** in Properties panel
6. Click **Get more components** → **Code** tab
7. Search for "VisualHost"
8. **Select** and click **Add**

#### A.3 Configure Properties

| Property | Value | Description |
|----------|-------|-------------|
| **Chart Definition** | (bound automatically) | Lookup binding |
| **Chart Definition ID** | (leave empty) | Not needed for lookup binding |
| **Context Field Name** | (optional) | For related record filtering |
| **Height** | e.g., 350 | Chart height in pixels |
| **Show Toolbar** | True | Shows expand button |
| **Enable Drill-Through** | True | Enables click interactions |

#### A.4 Save and Publish

1. **Save** the form
2. **Publish** the form

### 5.3 Method B: Static ID Binding

Use this when all records should show the same chart.

#### B.1 Get Chart Definition ID

1. Open the Chart Definition record you want to use
2. Copy the **Chart Definition ID** (GUID) from the URL or form
3. Example: `cf7e2453-2be5-f011-8406-7ced8d1dc988`

#### B.2 Add to Form (Without Lookup Column)

1. Open the entity's **Main Form** in designer
2. Click **+ Component** on a section (or create a new section)
3. Click **Get more components** → **Code** tab
4. Search for "VisualHost"
5. **Add** the component

#### B.3 Configure Properties

| Property | Value | Description |
|----------|-------|-------------|
| **Chart Definition** | (not bound) | Leave unbound |
| **Chart Definition ID** | `cf7e2453-2be5-f011-...` | Paste the GUID |
| **Context Field Name** | (optional) | For related record filtering |
| **Height** | e.g., 350 | Chart height in pixels |
| **Show Toolbar** | True | Shows expand button |
| **Enable Drill-Through** | True | Enables click interactions |

### 5.4 Context Filtering (Show Related Records)

Use Context Field Name to filter chart data to records related to the current record.

**Example:** On a Matter form, show only documents linked to this matter.

| Setting | Value |
|---------|-------|
| **Context Field Name** | `_sprk_matterid_value` |

This adds a filter condition: `sprk_matterid = [current record ID]`

**Field Name Format:**
- For lookups: `_fieldname_value` (e.g., `_sprk_matterid_value`)
- For text fields: `fieldname` (e.g., `sprk_status`)

### 5.5 Multiple Charts Per Form

To display multiple charts on the same form:

1. Use **Static ID Binding** (Method B) for each chart
2. Add multiple Visual Host components
3. Each with a different **Chart Definition ID**

**Example Layout:**
```
┌─────────────────────────────────────────────┐
│ Account Information                          │
├─────────────────────────────────────────────┤
│ ┌───────────────┐  ┌───────────────┐        │
│ │ Doc Count     │  │ Docs by Type  │        │
│ │ (MetricCard)  │  │ (BarChart)    │        │
│ │     127       │  │ ████ ██ █     │        │
│ └───────────────┘  └───────────────┘        │
│ ┌───────────────────────────────────┐       │
│ │ Documents by Status (DonutChart)  │       │
│ │        ●●●                        │       │
│ └───────────────────────────────────┘       │
└─────────────────────────────────────────────┘
```

---

## Step 6: Test Your Configuration

### 6.1 Testing Checklist

After configuration, verify:

- [ ] Chart Definition record saved successfully
- [ ] Visual Host appears on form
- [ ] Chart renders with data (not "No chart configured")
- [ ] Correct visual type displays (bar, line, donut, etc.)
- [ ] Data matches expected records from view
- [ ] Toolbar expand button visible (if enabled)
- [ ] Different chart definitions show different charts (lookup binding)

### 6.2 Common Issues

| Symptom | Cause | Solution |
|---------|-------|----------|
| "No chart configured" | No Chart Definition linked | Select a Chart Definition in lookup or set static ID |
| "Chart definition not found" | Invalid GUID | Verify Chart Definition ID exists |
| Loading spinner forever | WebAPI error | Check browser console for errors |
| No data in chart | View returns no records | Verify view has data in Dataverse |
| Wrong chart type | Incorrect Visual Type | Edit Chart Definition, change Visual Type |

### 6.3 Browser Console Debugging

Visual Host logs to browser console:
- Open browser Developer Tools (F12)
- Go to Console tab
- Look for logs starting with `[VisualHost]`

Example logs:
```
[VisualHost] Loading chart definition: cf7e2453-... (source: lookup)
[VisualHost] Loaded: Documents by Type
[VisualHost] Data loaded: 5 data points from 127 records
```

---

## Quick Reference

### Visual Type Values

| Visual Type | Value | Label |
|-------------|-------|-------|
| Metric Card | `100000000` | MetricCard |
| Bar Chart | `100000001` | BarChart |
| Line Chart | `100000002` | LineChart |
| Area Chart | `100000003` | AreaChart |
| Donut Chart | `100000004` | DonutChart |
| Status Bar | `100000005` | StatusBar |
| Calendar | `100000006` | Calendar |
| Mini Table | `100000007` | MiniTable |

### Aggregation Type Values

| Aggregation | Value | Description |
|-------------|-------|-------------|
| Count | `100000000` | Count of records |
| Sum | `100000001` | Sum of field values |
| Average | `100000002` | Average of field values |
| Min | `100000003` | Minimum value |
| Max | `100000004` | Maximum value |

### Visual Host Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `chartDefinition` | Lookup | No* | Bound to lookup column |
| `chartDefinitionId` | Text | No* | Static GUID for form-level config |
| `contextFieldName` | Text | No | Field to filter by current record |
| `height` | Number | No | Height in pixels (default: auto) |
| `showToolbar` | Boolean | No | Show expand button (default: true) |
| `enableDrillThrough` | Boolean | No | Enable click interactions (default: true) |

*One of `chartDefinition` or `chartDefinitionId` should be configured.

---

## Example Configurations

### Example 1: Dashboard with Multiple Charts

**Scenario:** Account form with documents metrics

| Chart | Visual Type | Binding | Context Filter |
|-------|-------------|---------|----------------|
| Document Count | Metric Card | Static ID | `_accountid_value` |
| Docs by Type | Bar Chart | Static ID | `_accountid_value` |
| Docs by Status | Donut Chart | Static ID | `_accountid_value` |

### Example 2: User-Selectable Chart

**Scenario:** User picks which chart to display

| Setup | Configuration |
|-------|---------------|
| Lookup Column | `sprk_selectedchart` on Account |
| Visual Host | Bound to `sprk_selectedchart` |
| User Action | Select different Chart Definition |
| Result | Chart updates to show selected visualization |

### Example 3: Matter-Specific Documents

**Scenario:** Matter form shows only related documents

| Chart | Configuration |
|-------|---------------|
| Name | "Matter Documents by Type" |
| Entity | Documents |
| View | Active Documents |
| Context Field | `_sprk_matterid_value` |

---

## Appendix: Form JavaScript Setup

If the Chart Definition form doesn't auto-sync lookups to backing fields, configure form JavaScript:

### Upload Web Resource

1. Go to **Solutions** → your solution
2. **+ New** → **More** → **Web resource**
3. Configure:
   - **Name**: `sprk_/scripts/chartdefinition_form.js`
   - **Type**: JavaScript (JS)
4. Upload the file from `src/solutions/webresources/sprk_chartdefinition_form.js`
5. **Save** and **Publish**

### Register Form Events

1. Open **Chart Definition** → **Forms** → **Information**
2. Click **Form Properties**
3. Under **Events**, click **+ Add library**
4. Select `sprk_/scripts/chartdefinition_form.js`
5. Under **Event Handlers** → **On Load**, click **+ Event Handler**
6. Configure:
   - **Library**: `sprk_/scripts/chartdefinition_form.js`
   - **Function**: `Spaarke.ChartDefinition.onLoad`
   - **Pass execution context**: Checked
7. **Done** → **Save** → **Publish**

---

*Guide version 1.0 | Created 2026-01-02 | Spaarke Visuals Framework*
