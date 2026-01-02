# Power App Setup Guide - Visual Host & DrillThroughWorkspace

> **Last Updated**: 2025-12-30
> **Environment**: https://spaarkedev1.crm.dynamics.com
> **Purpose**: Configure deployed PCF controls in Power Apps
> **Visual Host Version**: 1.1.0 (Hybrid binding + context filtering)

---

## Prerequisites

Completed:
- [x] Visual Host PCF deployed to Dataverse (v1.0.3) - `sprk_Spaarke.Visuals.VisualHost`
- [x] DrillThroughWorkspace PCF deployed to Dataverse (v1.1.0) - `sprk_Spaarke.Controls.DrillThroughWorkspace`
- [x] sprk_chartdefinition entity exists with 7 test records (one for each visual type)

---

## Architecture Overview

**Visual Host** uses a **lookup binding** architecture:

```
[Entity Form]                [sprk_chartdefinition]
     │                              │
     ▼                              ▼
┌─────────────────┐         ┌─────────────────────┐
│ Account         │         │ Chart Definition    │
│ ─────────────── │         │ ─────────────────── │
│ sprk_chartdef   │────────►│ Name                │
│ (lookup field)  │         │ Visual Type         │
│                 │         │ Entity Logical Name │
│ [Visual Host]   │         │ Base View ID        │
│ (PCF bound to   │         │ JSON Config         │
│  lookup field)  │         └─────────────────────┘
└─────────────────┘
```

The Visual Host control binds to a **lookup column** on the form's entity that points to `sprk_chartdefinition`. This allows different records to display different charts.

---

## Step 1: Create Lookup Column on Test Entity

Before adding Visual Host to a form, you need a lookup column that points to sprk_chartdefinition.

### 1.1 Open Power Apps Maker Portal

1. Go to https://make.powerapps.com
2. Select environment: **SPAARKE DEV 1**
3. Navigate to: **Solutions** → **PowerAppsToolsTemp_sprk** (or your target solution)

### 1.2 Create Lookup Column

1. Navigate to **Tables** → **Account** (or your test entity)
2. Click **Columns** → **+ New column**
3. Configure:
   - **Display name**: `Chart Definition`
   - **Name**: `sprk_chartdefinition` (auto-generated)
   - **Data type**: `Lookup`
   - **Related table**: `Chart Definition` (sprk_chartdefinition)
   - **Required**: `Optional`
4. Click **Save**

### 1.3 Verify Column Created

The new lookup column should appear in the Columns list as:
- **sprk_chartdefinition** (Lookup → Chart Definition)

---

## Step 2: Add Visual Host to Form

### 2.1 Open Form Designer

1. In the solution, navigate to **Tables** → **Account** → **Forms**
2. Open an existing Main form OR create new: **+ New form** → **Main form**
3. Name: "Test - Visual Host" (if creating new)

### 2.2 Add Lookup Field to Form

1. In the form designer, find **Table columns** panel on the left
2. Find **Chart Definition** (the lookup column you created)
3. Drag it onto the form in the desired section
4. This creates a standard lookup control

### 2.3 Add Visual Host PCF Control

1. Select the **Chart Definition** lookup field on the form
2. In the **Properties** panel (right side), click **+ Component**
3. Click **Get more components** at bottom
4. Switch to **Code** tab
5. Search for **VisualHost** (or `sprk_Spaarke.Visuals.VisualHost`)
6. Select it and click **Add**

### 2.4 Configure Visual Host Properties

After adding the component, configure its properties:

| Property | Binding | Description |
|----------|---------|-------------|
| **Chart Definition** | `sprk_chartdefinition` (bound) | Automatically bound to the lookup field |
| **Height** | Static: (leave empty or enter pixels) | Optional height, e.g., `400` |
| **Show Toolbar** | Static: `True` | Shows expand button in toolbar |
| **Enable Drill-Through** | Static: `True` | Enables click interactions |

### 2.5 Save and Publish

1. Click **Save**
2. Click **Publish**

---

## Step 3: Test Visual Host

### 3.1 Populate Chart Definition on Test Record

1. Open a Model-Driven App (e.g., Sales Hub)
2. Navigate to **Accounts**
3. Open any account record
4. Find the **Chart Definition** lookup field
5. Click lookup and select one of the test chart definitions:

| Visual Type | Chart Definition Name |
|-------------|----------------------|
| MetricCard | Test - Active Accounts Count |
| BarChart | Test - Accounts by Industry |
| LineChart | Test - Accounts Created Over Time |
| AreaChart | Test - Revenue by Account Type |
| DonutChart | Test - Accounts by Status |
| StatusBar | Test - Account Status Bar |
| MiniTable | Test - Top 10 Accounts |

6. **Save** the record

### 3.2 Verify Visual Host Renders

After saving with a Chart Definition selected:

1. The Visual Host control should display below/replacing the lookup field
2. **Expected states**:
   - **Loading**: Spinner while fetching chart definition
   - **Rendered**: Chart matching the selected definition's visual type
   - **Error**: Message if chart definition loading fails

### 3.3 Test Different Visual Types

To test all visual types:
1. Edit the Account record
2. Change the Chart Definition lookup to a different test record
3. Save
4. Verify the new visual type renders

---

## Step 4: Test Toolbar and Drill-Through

### 4.1 Toolbar

If **Show Toolbar** is `True`:
- Expand button (↗) appears in top-right corner
- Clicking opens DrillThroughWorkspace dialog (when configured)

### 4.2 Drill-Through Interactions

If **Enable Drill-Through** is `True`:
- Chart elements are clickable (bars, segments, points)
- Click events are logged to browser console
- Full drill-through requires DrillThroughWorkspace Custom Page (Step 5)

---

## Step 5: Create Drill-Through Custom Page (Simplified Approach)

This enables drill-through from VisualHost charts using the **built-in Data Table control**.

> **Architecture Decision**: Use platform-native Data Table instead of custom PCF.
> - No custom code deployment required
> - Full platform features (sorting, paging, column customization)
> - One Custom Page per entity type (Documents, Matters, etc.)
> - All configuration done in Power Apps Maker Portal UI

### 5.1 Create Custom Page

1. Open https://make.powerapps.com
2. Select environment: **SPAARKE DEV 1**
3. Navigate to: **Solutions** → **PowerAppsToolsTemp_sprk** (or your target solution)
4. Click **+ New** → **More** → **Custom page**
5. Choose **Start from blank** → **Create**
6. In the Properties panel (right side), set:
   - **Name**: `Drill Through - Documents` (or entity name)
   - **Display name**: `Drill Through - Documents`

### 5.2 Configure App OnStart (Read URL Parameters)

The Custom Page receives filter parameters from VisualHost via URL.

1. In the **Tree View** (left panel), click **App**
2. In the **formula bar** at top, select **OnStart** from dropdown
3. Enter this Power Fx formula:
   ```
   Set(varFilterField, Param("filterField"));
   Set(varFilterValue, Param("filterValue"));
   Set(varChartTitle, Param("chartTitle"))
   ```

This reads three URL parameters:
- `filterField` - The column to filter (e.g., `sprk_documenttype`)
- `filterValue` - The value to filter by (e.g., `100000001` for Contract)
- `chartTitle` - Optional title to display

### 5.3 Add Header Label (Optional)

1. Click **+** (Insert) in left panel
2. Under **Display**, click **Text label**
3. Position at top of page
4. Set properties:
   - **Text**: `"Drill Through: " & If(IsBlank(varChartTitle), "Records", varChartTitle)`
   - **X**: `20`
   - **Y**: `10`
   - **Width**: `Parent.Width - 40`
   - **Height**: `40`
   - **Size**: `20` (font size)
   - **FontWeight**: `FontWeight.Semibold`

### 5.4 Add Data Table Control

1. Click **+** (Insert) in left panel
2. Under **Layout**, click **Data table**
3. The Data table control appears on canvas
4. Position below the header:
   - **X**: `0`
   - **Y**: `60`
   - **Width**: `Parent.Width`
   - **Height**: `Parent.Height - 60`

### 5.5 Bind Data Source with Filter

1. Select the **Data table** control
2. In the Properties panel, find **Data source**
3. Click **Select a data source** → **Dataverse** → **Documents** (or your entity)
4. In the **formula bar**, select **Items** property
5. Enter the filter formula:

**For Documents (sprk_document):**
```
If(
    IsBlank(varFilterField) || IsBlank(varFilterValue),
    Documents,
    Filter(Documents,
        Switch(varFilterField,
            "sprk_documenttype", sprk_documenttype = Value(varFilterValue),
            "sprk_status", sprk_status = Value(varFilterValue),
            "sprk_matterid", sprk_matterid = GUID(varFilterValue),
            true
        )
    )
)
```

This formula:
- Shows all records if no filter parameters
- Filters by the specified field/value when parameters are passed
- Handles different field types (choice, lookup, etc.)

### 5.6 Configure Data Table Columns

1. Select the Data table control
2. Click **Edit fields** in Properties panel
3. Add columns you want to display:
   - **Name** (`sprk_name`)
   - **Document Type** (`sprk_documenttype`)
   - **Status** (`sprk_status`)
   - **Created On** (`createdon`)
   - etc.
4. Reorder columns by dragging
5. Click outside to close the fields panel

### 5.7 Add Close Button

1. Click **+** (Insert) → **Button**
2. Position in top-right corner:
   - **X**: `Parent.Width - 120`
   - **Y**: `10`
   - **Width**: `100`
   - **Height**: `40`
3. Set properties:
   - **Text**: `"Close"`
   - **OnSelect**: `Back()`

### 5.8 Save and Publish

1. Click **File** (top left)
2. Click **Save**
3. Enter name: `sprk_drillthrough_documents`
4. Click **Save**
5. Click **Publish**
6. Click **Publish this version**

### 5.9 Test the Custom Page

**Direct URL Test:**
```
https://spaarkedev1.crm.dynamics.com/main.aspx?appid=YOUR_APP_ID&pagetype=custom&name=sprk_drillthrough_documents&filterField=sprk_documenttype&filterValue=100000001&chartTitle=Contracts
```

Replace `YOUR_APP_ID` with your model-driven app GUID.

**Expected Behavior:**
1. Page loads with header showing "Drill Through: Contracts"
2. Data Table shows only documents where Document Type = Contract
3. Close button returns to previous page

---

## Step 5b: Create Additional Entity Custom Pages

Repeat Step 5 for each entity type you need drill-through for:

| Entity | Custom Page Name | Data Source |
|--------|------------------|-------------|
| Documents | `sprk_drillthrough_documents` | `Documents` |
| Matters | `sprk_drillthrough_matters` | `Matters` |
| Events | `sprk_drillthrough_events` | `Events` |
| Invoices | `sprk_drillthrough_invoices` | `Invoices` |

Each page uses the same pattern - just change the data source and filter fields.

---

## Step 5c: URL Parameters Reference

When VisualHost opens a drill-through page, it passes these parameters:

| Parameter | Description | Example |
|-----------|-------------|---------|
| `filterField` | Column logical name to filter | `sprk_documenttype` |
| `filterValue` | Value to filter by | `100000001` |
| `chartTitle` | Display title (optional) | `Contracts` |
| `chartDefinitionId` | Chart Definition GUID (optional) | `9464fc2d-...` |

**Full URL Pattern:**
```
https://{org}.crm.dynamics.com/main.aspx?
  appid={appId}&
  pagetype=custom&
  name={customPageName}&
  filterField={fieldLogicalName}&
  filterValue={value}&
  chartTitle={title}
```

---

## Step 5d: Future Enhancement - Full Grid Experience

The current Data Table approach provides **read-only** viewing with sorting and paging. For more advanced scenarios requiring editing, row selection, or custom rendering, consider these upgrade paths:

### Current Data Table Limitations

| Feature | Data Table | Advanced Grid |
|---------|------------|---------------|
| View records | ✅ | ✅ |
| Sort columns | ✅ | ✅ |
| Paging | ✅ | ✅ |
| Inline editing | ❌ | ✅ |
| Row selection (checkboxes) | ❌ | ✅ |
| Bulk actions | ❌ | ✅ |
| Custom column rendering | ❌ | ✅ |
| Conditional formatting | ❌ | ✅ |
| Export to Excel | ❌ | ✅ |

### Option 1: Gallery Control (Medium Effort)

Replace the Data Table with a **Gallery** control for more customization:

**Pros:**
- Full control over row layout and styling
- Can add edit icons, buttons per row
- Conditional formatting via Power Fx
- No code deployment

**Cons:**
- Must manually build column headers
- More complex Power Fx formulas
- Performance may degrade with large datasets

**Implementation:**
1. Replace Data Table with Vertical Gallery
2. Create header row manually
3. Add Edit/View buttons per row
4. Use `Patch()` for inline edits

### Option 2: Power Apps Editable Grid (Model-Driven Apps)

For Model-Driven App contexts, use the **Power Apps grid control**:

**Pros:**
- Full editing capabilities
- Bulk selection and actions
- Excel-like experience
- Platform-native, no deployment

**Cons:**
- Only works in Model-Driven App grids (not Custom Pages)
- Less control over appearance
- Requires form/view configuration

**Implementation:**
1. Create a Model-Driven App view for the entity
2. Enable "Power Apps grid control" in view settings
3. Configure editable columns
4. Navigate to view URL instead of Custom Page

### Option 3: DrillThroughWorkspace PCF (High Effort, Maximum Control)

Use the custom **DrillThroughWorkspace PCF** already built in this project:

**Pros:**
- Full custom React UI
- Chart + Grid side-by-side layout
- Click-to-filter interactions
- Complete control over UX

**Cons:**
- Requires PCF deployment
- More maintenance overhead
- Bundle size considerations

**Implementation:**
1. The PCF is already deployed (v1.1.1)
2. Create Custom Page with DrillThroughWorkspace control
3. Bind to dataset
4. See [DrillThroughWorkspace control](../../../src/client/pcf/DrillThroughWorkspace/)

### Option 4: UniversalDatasetGrid PCF (Future - Not Currently Working)

> **Status**: Broken - React 18 incompatible with Dataverse platform libraries
> **Fix Required**: See [universal-dataset-grid-r2 project](../../universal-dataset-grid-r2/README.md)

The **UniversalDatasetGrid** PCF (v2.1.4) provides document management features:
- File operations (download, delete, replace)
- SDAP/SharePoint Embedded integration
- Command bar with bulk actions

**Currently Not Deployable**: Uses React 18's `createRoot()` API which is incompatible with Dataverse's platform-provided React 16.14.0. Requires migration to `ReactDOM.render()` pattern per ADR-022.

**When Fixed** (future project):
1. Deploy UniversalDatasetGrid
2. Use in Custom Page instead of Data Table
3. Full document management capabilities

### Recommendation

| Use Case | Recommended Approach |
|----------|---------------------|
| Read-only drill-through | **Data Table** (current) |
| Simple inline edits | **Gallery** with Patch() |
| Full editing + bulk actions | **Power Apps grid control** (Model-Driven view) |
| Custom visualizations + interactions | **DrillThroughWorkspace PCF** |

**For most drill-through scenarios**, the Data Table is sufficient. Upgrade only when specific editing or customization requirements emerge.

---

## Verification Checklist

### Visual Host PCF
- [ ] Lookup column created on test entity (Account)
- [ ] Visual Host added to form bound to lookup column
- [ ] Form saved and published
- [ ] Test record has Chart Definition selected
- [ ] Control renders on form (not just the lookup)
- [ ] MetricCard renders with count
- [ ] BarChart renders with bars
- [ ] LineChart renders with line
- [ ] DonutChart renders with segments
- [ ] StatusBar renders with segments
- [ ] MiniTable renders with rows
- [ ] Expand button visible (if Show Toolbar = True)
- [ ] Click interactions logged (if Enable Drill-Through = True)

### DrillThroughWorkspace PCF (if configured)
- [ ] Custom Page created
- [ ] DrillThroughWorkspace control added
- [ ] Expand button opens dialog
- [ ] Two-panel layout renders
- [ ] Chart renders in left panel
- [ ] Grid renders in right panel

### Theme Integration
- [ ] Colors match Fluent UI theme
- [ ] Dark mode works (if enabled in environment)

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Visual Host not in component list | Not published | Run `pac solution publish-all` |
| Control shows "No chart configured" | Lookup field empty | Select a Chart Definition record |
| Control shows loading forever | Chart definition load failed | Check browser console for errors |
| Chart shows error message | Invalid chart definition config | Verify sprk_chartdefinition record has valid data |
| Expand button doesn't work | Custom Page not created | Create sprk_drillthroughworkspace Custom Page |

---

## Quick Reference

### Chart Definition Test Records

| ID | Visual Type | Name |
|----|-------------|------|
| `cf7e2453-2be5-f011-8406-7ced8d1dc988` | MetricCard | Test - Active Accounts Count |
| `d07e2453-2be5-f011-8406-7ced8d1dc988` | BarChart | Test - Accounts by Industry |
| `1b2cb856-2be5-f011-8406-7c1e520aa4df` | LineChart | Test - Accounts Created Over Time |
| `1c2cb856-2be5-f011-8406-7c1e520aa4df` | AreaChart | Test - Revenue by Account Type |
| `73e9c352-2be5-f011-8406-7c1e525abd8b` | DonutChart | Test - Accounts by Status |
| `1d2cb856-2be5-f011-8406-7c1e520aa4df` | StatusBar | Test - Account Status Bar |
| `1e2cb856-2be5-f011-8406-7c1e520aa4df` | MiniTable | Test - Top 10 Accounts |

### PAC CLI Commands

```bash
# Publish all customizations
pac solution publish-all

# Check control registration
pac org who
```

---

## v1.1.0 Enhancements: Chart Definition Form JavaScript

### New in v1.1.0

Visual Host v1.1.0 adds:
- **Hybrid binding**: Lookup OR static Chart Definition ID
- **Context filtering**: Filter data to related records (e.g., "Documents for this Matter")
- **User-friendly lookups**: sprk_reportingentity and sprk_reportingview on Chart Definition form

### Step 6: Configure Chart Definition Form JavaScript

The Chart Definition form needs minimal JavaScript to sync lookup selections to backing text fields.

#### 6.1 Upload Web Resource

1. In Power Apps Maker, go to **Solutions** → your solution
2. Click **+ New** → **More** → **Web resource**
3. Configure:
   - **Display name**: `Chart Definition Form Script`
   - **Name**: `sprk_/scripts/chartdefinition_form.js`
   - **Type**: `JavaScript (JS)`
4. Click **Choose file** and upload: `src/solutions/webresources/sprk_chartdefinition_form.js`
5. Click **Save** → **Publish**

#### 6.2 Register Form Event Handlers

1. Navigate to **Tables** → **Chart Definition** → **Forms**
2. Open the **Information** main form
3. Click **Form Properties** (top toolbar)
4. Under **Events**, click **+ Add library**
5. Select `sprk_/scripts/chartdefinition_form.js`
6. Click **Add**

**Register OnLoad Handler:**
1. Under **Event Handlers** → **On Load**, click **+ Event Handler**
2. Configure:
   - **Library**: `sprk_/scripts/chartdefinition_form.js`
   - **Function**: `Spaarke.ChartDefinition.onLoad`
   - **Pass execution context**: ✅ Checked
3. Click **Done**

#### 6.3 Configure Related Records Filtering (Native)

The Reporting View lookup should filter by selected Reporting Entity:

1. Select the **Reporting View** lookup field on the form
2. In Properties, find **Related records filtering** section
3. Configure:
   - **Filter by**: `Reporting Entity (sprk_reportingentity)`
   - **Relationship**: `sprk_reportingentity_sprk_reportingview`
4. Save and Publish the form

### JavaScript Functions Reference

| Function | Trigger | Behavior |
|----------|---------|----------|
| `onLoad` | Form OnLoad | Registers onChange handlers |
| `onReportingEntityChange` | Reporting Entity OnChange | Syncs to `sprk_entitylogicalname`, clears view |
| `onReportingViewChange` | Reporting View OnChange | Syncs to `sprk_baseviewid` |

### v1.1.0 Control Properties

| Property | Type | Description |
|----------|------|-------------|
| `chartDefinition` | Lookup (optional) | Bound to lookup column - takes precedence |
| `chartDefinitionId` | Text (input) | Static GUID for form-level config |
| `contextFieldName` | Text (input) | Field to filter by (e.g., `_sprk_matterid_value`) |

---

## Phase 6 Status

| Task | Status | Description |
|------|--------|-------------|
| 050 | ✅ Complete | Visual Host v1.1.0 PCF changes |
| 051 | ✅ Complete | Chart Definition form JavaScript |
| 052 | 🔄 In Progress | Deploy v1.1.0 and integration testing |

### Deployment Complete (2025-12-30)

Visual Host v1.1.0 deployed to Dataverse:
- PCF control: `sprk_Spaarke.Visuals.VisualHost` v1.1.0
- Solution: `PowerAppsToolsTemp_sprk`

**Remaining Manual Steps**:
1. Upload web resource: `sprk_/scripts/chartdefinition_form.js`
2. Register form OnLoad handler on Chart Definition form
3. Configure related records filtering on Reporting View lookup
4. Test all scenarios per [power-app-setup-guide.md](power-app-setup-guide.md)

See [TASK-INDEX.md](../tasks/TASK-INDEX.md) for full details.

---

*Guide updated for Phase 6 v1.1.0 enhancements (2025-12-30).*
