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

## Step 5: Create DrillThroughWorkspace Custom Page (Optional)

This enables the full drill-through experience when clicking the expand button.

### 5.1 Create Custom Page

1. In Power Apps Maker, go to **Apps** → **+ New app** → **Page**
2. Name: `sprk_drillthroughworkspace`
3. This creates a Canvas App-style page

### 5.2 Add DrillThroughWorkspace PCF

1. In the Custom Page editor, click **+** (Insert)
2. Click **Get more components** at bottom of panel
3. Switch to **Code** tab
4. Search for **DrillThroughWorkspace** (or **Spaarke.Controls.DrillThroughWorkspace**)
5. Add to the page

### 5.3 Configure Dataset Binding

The DrillThroughWorkspace is a **Dataset PCF** - it requires dataset binding:

1. Select the DrillThroughWorkspace control
2. In Properties panel, configure:
   - **Items**: Bind to a data source (dynamically set based on chart definition)
   - **chartDefinitionId**: Pass via URL parameter

### 5.4 Save and Publish Custom Page

1. **File** → **Save**
2. **File** → **Publish**

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
