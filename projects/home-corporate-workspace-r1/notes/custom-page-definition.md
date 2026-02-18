# Custom Page Definition — Legal Operations Workspace

> **Task**: 040 — Solution Packaging for Dataverse
> **Purpose**: Specification for registering the LegalWorkspace PCF as a Power Apps Custom Page
> **Environment**: https://spaarkedev1.crm.dynamics.com

---

## Overview

The LegalWorkspace PCF control is hosted as a **Power Apps Custom Page** within a Dataverse
Model-Driven App (MDA). Custom Pages run in their own iframe and have access to the full
React 18 runtime, which is the architectural reason for using this hosting model
(see CLAUDE.md — "Custom Page (React 18)" section for the ADR exception rationale).

---

## Custom Page Metadata

| Field | Value |
|-------|-------|
| **Name** | `sprk_LegalOperationsWorkspace` |
| **Display Name** | `Legal Operations Workspace` |
| **Description** | `Home Corporate Legal Operations dashboard — matters, events, to-do items, and AI briefings.` |
| **Type** | Custom Page (Power Apps) |
| **PCF Control** | `sprk_Spaarke.Controls.LegalWorkspace` |
| **Solution** | `SpaarkeLegalWorkspace` (unmanaged) |

---

## Page Dimensions

| Setting | Value | Reason |
|---------|-------|--------|
| **Width** | Responsive fill (`Flexible width`) | Workspace adapts to MDA viewport |
| **Height** | Responsive fill (`Flexible height`) | Full-page layout with no scrollbars |
| **Orientation** | Landscape | Required for 2-column grid layout |
| **Scale to fit** | Off | PCF controls its own scaling via `allocatedWidth` |

> Custom Page responsive fill passes the full iframe dimensions to the PCF via
> `context.mode.allocatedWidth` and `context.mode.allocatedHeight`, which
> `WorkspaceGrid.tsx` uses for its breakpoint-based 1-column/2-column layout switch.

---

## PCF Control Reference

The Custom Page contains a single PCF control that fills the entire page:

| Property | Value |
|----------|-------|
| **Control Type** | Code Component (PCF) |
| **Control Name** | `Spaarke.LegalWorkspace` |
| **Solution Reference** | `SpaarkeLegalWorkspace` |
| **Namespace** | `Spaarke` |
| **Constructor** | `LegalWorkspace` |
| **Version** | `1.0.1` (must match after version bump) |

### PCF Input Properties

The LegalWorkspace control (as a standard control with no manifest inputs) receives
all data through the PCF framework context object:

| Data | Source in PCF | Notes |
|------|--------------|-------|
| Xrm.WebApi | `context.webAPI` | Direct entity queries (matters, events, to-dos) |
| Current User ID | `context.userSettings.userId` | For filtering user-specific items |
| Allocated Width | `context.mode.allocatedWidth` | Drives responsive grid breakpoint |
| Allocated Height | `context.mode.allocatedHeight` | Available but not used for height-locking |

---

## Sitemap SubArea XML

Add to the Model-Driven App sitemap to expose the Custom Page as a navigation item:

```xml
<SubArea Id="sprk_legal_workspace"
         Title="Legal Operations Workspace"
         Icon="$webresource:sprk_workspace_icon_16"
         Type="PageType"
         PageType="custom"
         CustomPage="sprk_LegalOperationsWorkspace"
         CheckSecurity="false">
  <Titles>
    <Title LCID="1033" Title="Legal Workspace" />
  </Titles>
  <Descriptions>
    <Description LCID="1033" Description="Home Corporate Legal Operations dashboard" />
  </Descriptions>
</SubArea>
```

> **Icon**: Use an existing `sprk_` prefixed icon webresource or omit the `Icon` attribute.
> The workspace uses a briefcase icon semantically — find the `sprk_briefcase_16` webresource
> or use the generic `sprk_workspace_icon_16` if available.

---

## Navigation Area

| Field | Value |
|-------|-------|
| **Area** | Legal Operations (existing `sprk_legal_operations` area) |
| **Group** | Workspace (new `sprk_workspace_group` group, or add to existing "Home" group) |
| **Order** | 1000 (first item in the Workspace group) |

### Full Sitemap Area Context

```xml
<Area Id="sprk_legal_operations" Title="Legal Operations" Icon="$webresource:sprk_legal_icon">
  <Group Id="sprk_workspace_group" Title="Workspace">
    <SubArea Id="sprk_legal_workspace" ... />  <!-- The Custom Page -->
  </Group>
  <!-- existing SubAreas ... -->
</Area>
```

---

## Power Apps Studio Creation Steps

Custom Pages **cannot be created via PAC CLI or Web API directly** — they must be
initially created through [make.powerapps.com](https://make.powerapps.com). Use
the solution-based approach for subsequent updates.

### One-Time Setup (Power Apps Maker Portal)

1. Navigate to [make.powerapps.com](https://make.powerapps.com)
2. Select the **Spaarke Dev** environment (`spaarkedev1.crm.dynamics.com`)
3. Go to **Solutions** → Open `SpaarkeLegalWorkspace`
4. Click **+ New** → **App** → **Page** → **Custom page**
5. In the Custom Page editor:
   - Set **Name**: `sprk_LegalOperationsWorkspace`
   - Set **Display Name**: `Legal Operations Workspace`
   - Set width/height to **Flexible** (fills viewport)
6. Insert the PCF control:
   - Click **+ Insert** → **Code components** → `Spaarke.LegalWorkspace`
   - Set the control's position and size to fill the entire page canvas
7. **File** → **Save** → **File** → **Publish**
8. Verify: Run `pac solution publish-all` after publish

### Solution-Based Updates (Post Initial Setup)

After the Custom Page is created once, subsequent PCF updates are deployed via solution import:

```powershell
# Full pipeline: build → pack → import
scripts\Package-LegalWorkspace.ps1 -Deploy

# After import, republish the Custom Page:
# 1. Open Custom Page in make.powerapps.com → Edit → Save → Publish
# 2. pac solution publish-all
```

---

## Verification Steps

After import and publish:

| Check | Command / Action |
|-------|-----------------|
| Solution visible in environment | `pac solution list | Select-String SpaarkeLegalWorkspace` |
| PCF control registered | Query `GET /api/data/v9.2/customcontrols?$filter=name eq 'sprk_Spaarke.Controls.LegalWorkspace'` |
| Custom Page accessible | Navigate to the SubArea in the MDA — workspace loads |
| Version footer correct | Hard refresh (Ctrl+Shift+R) — footer shows `v1.0.1` |
| Dark mode works | Toggle MDA theme — workspace responds with matching theme |
| Responsive layout | Resize browser — grid switches between 1-column and 2-column |

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| Control shows old version | Browser cache | Ctrl+Shift+R hard refresh |
| Custom Page blank | PCF bundle error | Check browser console for TypeScript errors |
| Custom Page not in sitemap | Sitemap not updated | Update sitemap XML and republish app |
| PCF loads but data missing | BFF not deployed | Deploy BFF via `Deploy-WorkspaceBff.ps1` |
| "unexpected error" on import | ZIP format issue | Use `pack.ps1` (forward slashes), not Compress-Archive |
| Control not listed | Solution not imported | Run `pac solution import` and verify |

---

*Created by Task 040 — Solution Packaging for Dataverse*
*See also: solution-packaging-checklist.md, PCF-DEPLOYMENT-GUIDE.md*
