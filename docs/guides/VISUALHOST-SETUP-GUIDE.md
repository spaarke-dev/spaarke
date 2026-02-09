# VisualHost PCF Control - Setup & Configuration Guide

> **Version**: 1.2.12 | **Last Updated**: February 9, 2026
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
9. [Data Querying Options](#data-querying-options)
10. [Use Cases: Visual Type Setup Walkthroughs](#use-cases-visual-type-setup-walkthroughs)
11. [Troubleshooting](#troubleshooting)

---

## Overview

The **VisualHost** PCF control renders configuration-driven visualizations inside Dataverse model-driven app forms. Instead of building separate PCF controls for each chart type, a single VisualHost instance reads a `sprk_chartdefinition` record to determine:

- **What** to display (visual type: bar chart, donut, metric card, due date cards, etc.)
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

5. Save the record and note the **Chart Definition ID** (GUID) from the URL

---

## Step 2: Configure Visual Type Settings

### Chart Types (MetricCard, BarChart, LineChart, DonutChart, StatusBar, Calendar, MiniTable)

These visual types use the **DataAggregationService** to fetch and aggregate entity records client-side.

| Field | Description | When to Use |
|-------|-------------|-------------|
| **Group By Field** (`sprk_groupbyfield`) | Entity field to group data by (e.g., `statuscode`, `sprk_priority`) | Required for all charts except MetricCard |
| **Aggregation Type** (`sprk_aggregationtype`) | How to aggregate: Count, Sum, Average, Min, Max | Optional (defaults to Count) |
| **Aggregation Field** (`sprk_aggregationfield`) | Numeric field to aggregate (e.g., `sprk_amount`) | Required for Sum/Average/Min/Max |
| **Base View ID** (`sprk_baseviewid`) | Saved view to use as data source | Optional (uses all records if not set) |
| **Options JSON** (`sprk_optionsjson`) | JSON object for component-specific styling options | Optional |

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
| **contextFieldName** | Text | Lookup field name for context filtering (e.g., `_sprk_matterid_value`). Filters data to current record's related records. | - |
| **fetchXmlOverride** | Text | FetchXML query override. Highest query priority - overrides all other data sources. | - |
| **height** | Number | Chart height in pixels. | Auto |
| **showToolbar** | Yes/No | Show the expand button in the upper right corner. | Yes |
| **enableDrillThrough** | Yes/No | Enable click interactions and drill-through navigation. | Yes |

### Priority: chartDefinition Lookup vs chartDefinitionId

If both are provided:
- The **lookup binding** (`chartDefinition`) takes precedence
- The **static ID** (`chartDefinitionId`) is used as a fallback when the lookup is empty

This allows flexible configuration: bind a lookup for dynamic chart selection, or use a static ID for fixed dashboards.

---

## Visual Type Configuration Reference

### MetricCard (100000000)

Displays a single large number with label and optional trend indicator.

**Chart Definition Settings:**
| Field | Value | Notes |
|-------|-------|-------|
| Visual Type | MetricCard | |
| Entity Logical Name | Your entity (e.g., `sprk_document`) | |
| Group By Field | Optional | If set, shows first group's value |
| Aggregation Type | Count (default) or Sum/Avg | |

**Options JSON Example:**
```json
{
  "trend": "up",
  "trendValue": 12.5,
  "compact": true
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
| Context Field Name | `_sprk_matterid_value` | The lookup field on Event that points to the parent (Matter) |
| Max Display Items | `5` or `10` | How many cards to show |
| View List Tab Name | `tabEvents` | Optional: form tab name for "View All" link |

**PCF Property Settings:**
| Property | Value | Notes |
|----------|-------|-------|
| contextFieldName | `_sprk_matterid_value` | Must match the chart definition's Context Field Name |

**Important:** The Context Field Name must be the **lookup field on the child entity** (Event) that points back to the parent entity (Matter). Use the WebAPI format with `_` prefix and `_value` suffix for lookup fields.

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

If `sprk_contextfieldname` is set (e.g., `_sprk_matterid_value`), VisualHost injects a filter into the view's FetchXML at runtime to show **only records related to the current form record**.

#### How the Context Filter Flow Works

1. The view's FetchXML is retrieved from the saved view specified in `sprk_baseviewid` (e.g., a "Tasks Due Next 14 Days" view)
2. The `contextRecordId` is read from the current form (the GUID of the record the form is displaying)
3. If **both** `sprk_contextfieldname` is set AND `contextRecordId` is available:
   - `ViewDataService.injectContextFilter()` modifies the FetchXML to add:
     ```xml
     <condition attribute="sprk_matterid" operator="eq" value="{matterId}" />
     ```
   - The modified FetchXML is then executed against Dataverse
4. If either value is missing, the view runs unfiltered

#### Field Name Transformation

The context filter automatically transforms the WebAPI lookup field format to a FetchXML attribute name:

```
_sprk_matterid_value  →  sprk_matterid
 ↑                ↑
 strips _ prefix   strips _value suffix
```

This transformation happens in `DueDateCardList.tsx` (line 164). You always configure the field using the **WebAPI format** (`_sprk_matterid_value`), and the control handles the conversion internally.

#### Required Configuration for Context Filtering

To filter a DueDateCardList to show only events related to the current form record, you need **both** the chart definition and PCF property configured:

**Chart Definition fields:**

| Field | Value | Purpose |
|-------|-------|---------|
| Base View ID (`sprk_baseviewid`) | GUID of saved view (e.g., "Tasks Due Next 14 Days") | Controls which events and columns to query |
| Context Field Name (`sprk_contextfieldname`) | `_sprk_matterid_value` | Tells VisualHost which lookup field to filter on |

**PCF Control properties:**

| Property | Value | Purpose |
|----------|-------|---------|
| contextFieldName | `_sprk_matterid_value` | Provides the runtime context record ID from the current form |

**Important:** The `contextFieldName` value must be the **lookup field on the child entity** (e.g., the `sprk_matterid` lookup on `sprk_event`) expressed in WebAPI format with `_` prefix and `_value` suffix. The chart definition and PCF property values should match.

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
- Check `sprk_contextfieldname` uses the correct lookup field format (e.g., `_sprk_matterid_value`)
- Verify the saved view ID is correct and the view is accessible
- Check browser console for detailed error messages

### "Could not find a property named..."
- Navigation property names for `$expand` must use the **relationship schema name**, not the lookup attribute name
- Example: Use `sprk_event_EventType_n1` instead of `sprk_eventtype_ref`

### Version Badge Not Updating
- Verify the solution was imported successfully
- Clear browser cache (Ctrl+Shift+Delete)
- Check the version badge in the lower-left corner of the control (should show v1.2.12)

### Click Actions Not Working
- Verify `enableDrillThrough` is set to Yes on the PCF control
- Check that `sprk_onclickaction` is set on the chart definition
- Ensure `sprk_onclicktarget` has the correct entity name or Custom Page name
- Check browser console for Xrm API errors

---

## Quick Reference: Common Configurations

### Bar Chart on Matter Form (Documents by Status)

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Documents by Status |
| Visual Type | BarChart (100000001) |
| Entity Logical Name | `sprk_document` |
| Group By Field | `statuscode` |
| Aggregation Type | Count (100000000) |
| **PCF Properties** | |
| contextFieldName | `_sprk_matterid_value` |

### DueDateCardList on Matter Form (Upcoming Events)

| Setting | Value |
|---------|-------|
| **Chart Definition** | |
| Name | Upcoming Events |
| Visual Type | DueDateCardList (100000009) |
| Entity Logical Name | `sprk_event` |
| Base View ID | *(GUID of "Tasks 14 Days" view)* |
| Context Field Name | `_sprk_matterid_value` |
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

---

*For architecture details and component reusability, see [VISUALHOST-ARCHITECTURE.md](VISUALHOST-ARCHITECTURE.md)*
