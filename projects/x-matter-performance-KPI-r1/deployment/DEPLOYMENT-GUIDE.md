# Matter Performance KPI R1 - Deployment Guide

> **Generated**: 2026-02-12
> **Updated**: 2026-02-16 (corrected web resource approach — subgrid listener replaces Quick Create trigger)
> **Project**: matter-performance-KPI-r1

---

## Deployment Components

| # | Component | Package | Deploy To | Order |
|---|-----------|---------|-----------|-------|
| 1 | BFF API (ScorecardCalculatorService) | `publish.zip` | Azure App Service via Kudu | First |
| 2 | VisualHost PCF Control (v1.2.30) | `VisualHostSolution_v1.2.30.zip` | Dataverse via Solution Import | Second |
| 3 | Web Resources (JS) | Manual upload | Dataverse via Maker Portal | Third |
| 4 | Ribbon Customization | Export/Modify/Import | Dataverse via ribbon-edit workflow | Fourth (after web resources) |

---

## 1. BFF API Deployment (Kudu)

**Package**: `src/server/api/Sprk.Bff.Api/publish.zip` (~60 MB)

### Upload via Kudu

1. Navigate to: `https://spe-api-dev-67e2xz.scm.azurewebsites.net`
2. Go to **Tools** > **Zip Push Deploy** (or drag-and-drop)
3. Upload `publish.zip`
4. Verify health check: `GET https://spe-api-dev-67e2xz.azurewebsites.net/healthz`

### Endpoints Added

```
POST /api/matters/{matterId:guid}/recalculate-grades   (AllowAnonymous + RateLimited)
POST /api/projects/{projectId:guid}/recalculate-grades  (AllowAnonymous + RateLimited)
```

> **Note**: Endpoints use `.AllowAnonymous()` because Dataverse web resources cannot acquire Azure AD tokens. `RequireRateLimiting("dataverse-query")` provides abuse protection. TODO: Replace with API key or service-to-service auth for production.

**Files added to API**:
- `Services/ScorecardCalculatorService.cs` - Grade calculation logic
- `Api/ScorecardCalculatorEndpoints.cs` - Endpoint registration
- `Models/ScorecardModels.cs` - Response DTOs

---

## 2. VisualHost PCF Solution (Dataverse)

**Package**: `src/client/pcf/VisualHost/Solution/bin/VisualHostSolution_v1.2.30.zip`

### Import Solution

```powershell
pac solution import --path "VisualHostSolution_v1.2.30.zip" --publish-changes
```

Or import manually via Power Apps maker portal:
1. Go to `make.powerapps.com` > Solutions > Import
2. Select the zip file
3. Import as **unmanaged**

### Verify

```powershell
pac solution list | Select-String "VisualHost"
```

Should show version 1.2.30.

### New Components in v1.2.30

- **GradeMetricCard** (`VisualType.ReportCardMetric = 100000010`) - Grade display with color coding
- **TrendCard** (`VisualType.TrendCard = 100000011`) - Historical trend with sparkline
- **Configuration modules** - matterMainCards.ts, matterReportCardTrends.ts

---

## 3. Web Resources (Manual Upload)

JavaScript web resources must be uploaded to Dataverse BEFORE the ribbon customization.

> **IMPORTANT**: The original design used a Quick Create form trigger (`sprk_kpiassessment_quickcreate.js`). This does NOT work in UCI — Quick Create flyouts cannot refresh the parent form. The correct approach is a **subgrid listener on the parent main form**.

### 3a. Matter KPI Refresh (Main Form Subgrid Listener) — REQUIRED

| Field | Value |
|-------|-------|
| **Name** | `sprk_/scripts/matter_kpi_refresh.js` |
| **Display Name** | Matter KPI Refresh |
| **Type** | JavaScript (JScript) |
| **Source File** | `src/solutions/webresources/sprk_matter_kpi_refresh.js` |

**Registration on Matter main form**:
- Entity: `sprk_matter`
- Form: Main form
- Event: OnLoad
- Function: `Spaarke.MatterKpi.onLoad`
- Pass execution context: Yes
- Parameters: (none)

**How it works**: Attaches a listener to the `subgrid_kpiassessments` subgrid. When a Quick Create save adds/removes a row, the subgrid's `addOnLoad` fires. The listener detects the row count change, calls the calculator API, waits 1.5s for Dataverse to commit, then refreshes form data.

### 3b. KPI Subgrid Refresh (For Project Form) — OPTIONAL

| Field | Value |
|-------|-------|
| **Name** | `sprk_/scripts/kpi_subgrid_refresh.js` |
| **Display Name** | KPI Subgrid Refresh |
| **Type** | JavaScript (JScript) |
| **Source File** | `src/solutions/webresources/sprk_kpi_subgrid_refresh.js` |

**Registration on Project main form** (when needed):
- Entity: `sprk_project`
- Form: Main form
- Event: OnLoad
- Function: `Spaarke.KpiSubgrid.onLoad`
- Pass execution context: Yes
- Parameters: (none)

> Auto-detects entity type (Matter vs Project) and routes to the correct API endpoint.

### 3c. KPI Ribbon Actions

| Field | Value |
|-------|-------|
| **Name** | `sprk_/scripts/kpi_ribbon_actions.js` |
| **Display Name** | KPI Ribbon Actions |
| **Type** | JavaScript (JScript) |
| **Source File** | `src/solutions/webresources/sprk_kpi_ribbon_actions.js` |

### Upload Steps

1. Go to `make.powerapps.com` > Solutions > Default Solution (or SpaarkeCore)
2. **Add existing** > **More** > **Developer** > **Web Resource**
3. Click **+ New web resource**
4. Fill in Name, Display Name, Type
5. Upload the JS file
6. Save and Publish

---

## 4. Ribbon Customization (ribbon-edit workflow)

The "+ Add KPI" button on the KPI Assessments subgrid requires the ribbon-edit export/modify/import workflow.

**Prerequisite**: Web resources from Step 3 must be deployed first.

### Step 4.1: Create Dedicated Ribbon Solution (one-time)

1. In Power Apps maker portal, create a new **unmanaged** solution:
   - Name: `MatterKpiRibbon`
   - Publisher: **Spaarke** (`sprk_`)
   - Version: `1.0.0.0`
2. Add existing > **Table** > Select `sprk_matter`
   - Include table metadata only (no forms, views, subcomponents)
3. Publish the solution

### Step 4.2: Export the Solution

```powershell
pac solution export --name "MatterKpiRibbon" --path "./MatterKpiRibbon.zip" --managed false
```

### Step 4.3: Extract

```powershell
Expand-Archive -Path "MatterKpiRibbon.zip" -DestinationPath "MatterKpiRibbon_extracted" -Force
```

### Step 4.4: Merge RibbonDiffXml

Open `MatterKpiRibbon_extracted/customizations.xml`.

Find the `<RibbonDiffXml>` section inside the `<Entity>` block for `sprk_matter`.

**Replace** the empty `<RibbonDiffXml>` with the content from:
`src/solutions/SpaarkeCore/entities/sprk_matter/RibbonDiff/add-kpi-ribbon.xml`

The RibbonDiffXml to merge:

```xml
<RibbonDiffXml>
  <CustomActions>
    <CustomAction Id="sprk.matter.subgrid.kpi.AddKpiButton.CustomAction"
                  Location="Mscrm.SubGrid.sprk_kpiassessment.MainTab.Management.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="sprk.matter.subgrid.kpi.AddKpiButton"
                Alt="$LocLabels:sprk.matter.subgrid.kpi.AddKpiButton.Alt"
                Command="sprk.matter.subgrid.kpi.AddKpiButton.Command"
                LabelText="$LocLabels:sprk.matter.subgrid.kpi.AddKpiButton.LabelText"
                ToolTipTitle="$LocLabels:sprk.matter.subgrid.kpi.AddKpiButton.ToolTipTitle"
                ToolTipDescription="$LocLabels:sprk.matter.subgrid.kpi.AddKpiButton.ToolTipDescription"
                TemplateAlias="o1"
                ModernImage="Add" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>
  <Templates>
    <RibbonTemplates Id="Mscrm.Templates" />
  </Templates>
  <CommandDefinitions>
    <CommandDefinition Id="sprk.matter.subgrid.kpi.AddKpiButton.Command">
      <EnableRules />
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_/scripts/kpi_ribbon_actions.js"
                            FunctionName="Spaarke.KpiRibbon.openQuickCreate">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>
  <RuleDefinitions>
    <TabDisplayRules />
    <DisplayRules />
    <EnableRules />
  </RuleDefinitions>
  <LocLabels>
    <LocLabel Id="sprk.matter.subgrid.kpi.AddKpiButton.LabelText">
      <Titles>
        <Title description="+ Add KPI" languagecode="1033" />
      </Titles>
    </LocLabel>
    <LocLabel Id="sprk.matter.subgrid.kpi.AddKpiButton.Alt">
      <Titles>
        <Title description="Add KPI Assessment" languagecode="1033" />
      </Titles>
    </LocLabel>
    <LocLabel Id="sprk.matter.subgrid.kpi.AddKpiButton.ToolTipTitle">
      <Titles>
        <Title description="Add KPI Assessment" languagecode="1033" />
      </Titles>
    </LocLabel>
    <LocLabel Id="sprk.matter.subgrid.kpi.AddKpiButton.ToolTipDescription">
      <Titles>
        <Title description="Open Quick Create form to add a new KPI assessment for this matter" languagecode="1033" />
      </Titles>
    </LocLabel>
  </LocLabels>
</RibbonDiffXml>
```

### Step 4.5: Repack and Import

```powershell
# Repack
Compress-Archive -Path "MatterKpiRibbon_extracted/*" -DestinationPath "MatterKpiRibbon_modified.zip" -Force

# Import
pac solution import --path "MatterKpiRibbon_modified.zip" --publish-changes
```

### Step 4.6: Verify

- Open a Matter record
- Scroll to the KPI Assessments subgrid
- Verify the "+ Add KPI" button appears in the subgrid command bar
- Click the button — Quick Create form should open

---

## 5. Dataverse Entity Configuration (Manual)

These entity changes need to be configured manually in Dataverse if not already present:

### 5a. KPI Assessment Entity (`sprk_kpiassessment`)

| Field | Type | Description |
|-------|------|-------------|
| `sprk_matter` | Lookup (sprk_matter) | Parent matter reference |
| `sprk_performancearea` | Choice | Guidelines, Budget, Outcomes |
| `sprk_kpiname` | Text | KPI name |
| `sprk_assessmentcriteria` | Text | Assessment criteria |
| `sprk_grade` | Decimal | Grade value (0-100) |
| `sprk_notes` | Multi-line Text | Assessment notes |

### 5b. Matter Entity Grade Fields

Add these 6 fields to `sprk_matter`:

| Field | Type |
|-------|------|
| `sprk_guidelinecompliancegrade_current` | Decimal |
| `sprk_guidelinecompliancegrade_average` | Decimal |
| `sprk_budgetcompliancegrade_current` | Decimal |
| `sprk_budgetcompliancegrade_average` | Decimal |
| `sprk_outcomecompliancegrade_current` | Decimal |
| `sprk_outcomecompliancegrade_average` | Decimal |

### 5c. Quick Create Form

Configure the Quick Create form for `sprk_kpiassessment` with the following fields:
- Performance Area (required)
- KPI Name (required)
- Assessment Criteria
- Grade (required)
- Notes

> **No OnLoad event handler needed on the Quick Create form.** The API call and form refresh are handled by the Matter main form's subgrid listener (Step 3a). The original Quick Create trigger approach (`sprk_kpiassessment_quickcreate.js`) was abandoned because UCI Quick Create flyouts cannot refresh the parent form.

### 5d. Report Card Tab on Matter Form

Add a new tab to the Matter main form with:
- 3 VisualHost PCF instances for trend cards (configured with `chartDefinitionId`)
- 1 subgrid for KPI Assessments (`subgrid_kpiassessments`)
  - Relationship: `sprk_matter_kpiassessments`
  - Records per page: 10

### 5e. Main Tab Grade Cards

Add 3 VisualHost PCF instances to the Matter main tab:
- Guidelines Compliance Grade (current)
- Budget Compliance Grade (current)
- Outcomes Compliance Grade (current)

Each configured with the appropriate `chartDefinitionId` pointing to a `sprk_chartdefinition` record.

---

## Deployment Verification Checklist

- [x] API health check passes (`/healthz`) — verified 2026-02-16
- [x] `POST /api/matters/{id}/recalculate-grades` endpoint responds (AllowAnonymous) — verified 2026-02-16
- [x] VisualHost solution imported to Dataverse
- [x] Web resource `sprk_/scripts/matter_kpi_refresh.js` published and registered on Matter main form OnLoad — verified 2026-02-16
- [x] Web resource `sprk_/scripts/kpi_ribbon_actions.js` published
- [x] "+ Add KPI" button appears on KPI Assessments subgrid
- [x] Quick Create form opens with pre-populated Matter lookup
- [x] After saving KPI assessment, grades auto-recalculate on Matter form — **verified working 2026-02-16**
- [ ] Grade metric cards display on Matter main tab (VisualHost configuration)
- [ ] Trend cards display on Report Card tab (VisualHost configuration)
- [ ] Dark mode works (no hard-coded colors) — code audited, PCF tested
- [ ] Project form: `sprk_/scripts/kpi_subgrid_refresh.js` deployed (when needed)

### Console Output (Expected on Matter Form)

When adding a KPI assessment via Quick Create:
```
[Matter KPI] v1.0.0 loaded. API: https://spe-api-dev-67e2xz.azurewebsites.net
[Matter KPI] Subgrid listener attached. Initial rows: N
[Matter KPI] Subgrid row count changed: N → N+1
[Matter KPI] Calling calculator API: POST https://spe-api-dev-67e2xz.azurewebsites.net/api/matters/{id}/recalculate-grades
[Matter KPI] Calculator API succeeded. Refreshing form in 1500ms...
[Matter KPI] Form data refreshed successfully.
```

---

*Generated by Claude Code for matter-performance-KPI-r1 project. Updated: 2026-02-16.*
