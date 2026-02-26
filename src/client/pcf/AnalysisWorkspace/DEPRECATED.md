# DEPRECATED: AnalysisWorkspace PCF Control

> **Status**: DEPRECATED as of 2026-02-26
> **Replaced by**: `src/client/code-pages/AnalysisWorkspace/` (React 18 Code Page)
> **Migration tasks**: Tasks 060-072 in `projects/ai-spaarke-platform-enhancents-r2/`
> **ADR reference**: ADR-006 -- PCF for field-bound form controls only; standalone pages use Code Pages

---

## Summary

The AnalysisWorkspace PCF control (`Spaarke.Controls.AnalysisWorkspace`) has been replaced by the AnalysisWorkspace Code Page. The Code Page provides the same AI document analysis workspace functionality but as a standalone web resource opened via `Xrm.Navigation.navigateTo()`, following the ADR-006 pattern for non-field-bound UI.

**This source code is preserved for reference only. Do NOT build, deploy, or import the PCF solution.**

---

## Why Deprecated

1. **ADR-006 compliance**: The AnalysisWorkspace is a standalone workspace dialog, not a field-bound form control. ADR-006 mandates Code Pages for standalone dialogs and PCF for field-bound controls only.
2. **React version constraints**: PCF controls are limited to React 16/17 (platform-provided). The Code Page bundles React 18+ and has no such constraints (ADR-022).
3. **Architecture simplification**: The Code Page eliminates the Custom Page wrapper (`sprk_analysisworkspace_8bc0b`) and the associated PCF-inside-Custom-Page complexity.
4. **Independent authentication**: The Code Page acquires its own Bearer tokens via `Xrm.Utility.getGlobalContext()`, removing the need for MSAL configuration passed via PCF input properties.

---

## What Replaced It

| Legacy (PCF) | Replacement (Code Page) |
|--------------|------------------------|
| `src/client/pcf/AnalysisWorkspace/` | `src/client/code-pages/AnalysisWorkspace/` |
| PCF control: `Spaarke.Controls.AnalysisWorkspace` | Web resource: `sprk_AnalysisWorkspace` (HTML) |
| Custom Page: `sprk_analysisworkspace_8bc0b` | `Xrm.Navigation.navigateTo({ pageType: "webresource" })` |
| Dataverse solution: `AnalysisWorkspaceSolution` | Standard web resource in main Spaarke solution |
| `sprk_analysis_commands.js` (`Spaarke_OpenAnalysisWorkspace`) | `sprk_AnalysisWorkspaceLauncher.js` (`Spaarke.AnalysisWorkspace.openAnalysisWorkspace`) |

---

## Dataverse Components to Remove

The following Dataverse components were deployed as part of the legacy PCF control and should be removed from the environment. These are **manual operations** that must be performed in the Dataverse maker portal or via PAC CLI.

### 1. Remove PCF Control from Analysis Form

The AnalysisWorkspace PCF was placed on the `sprk_analysis` entity form (either the main form or via a Custom Page). If the PCF control is still bound to any form:

```
Entity:     sprk_analysis
Control:    Spaarke.Controls.AnalysisWorkspace
Bound to:   sprk_analysisid (SingleLine.Text field)
```

**Steps (Dataverse Form Designer)**:
1. Open `make.powerapps.com` > Tables > Analysis (sprk_analysis) > Forms
2. Open the Main form in the form designer
3. Locate the section containing the AnalysisWorkspace control
4. Select the field bound to the control and remove the custom control binding
5. Alternatively, remove the entire section/tab if it was solely for the PCF control
6. Save and Publish the form

### 2. Remove PCF Web Resource / Solution

The PCF was deployed as a separate Dataverse solution: `AnalysisWorkspaceSolution`.

**Option A: Delete the managed solution** (if imported as managed):
```powershell
pac solution delete --solution-name AnalysisWorkspaceSolution
pac solution publish
```

**Option B: Remove from unmanaged solution** (if imported as unmanaged):
1. Open `make.powerapps.com` > Solutions
2. Find `AnalysisWorkspaceSolution` (or the solution containing the PCF)
3. Remove the custom control component: `sprk_Spaarke.Controls.AnalysisWorkspace`
4. Publish all customizations

### 3. Remove the Custom Page (if still present)

The old Custom Page `sprk_analysisworkspace_8bc0b` was the host for the PCF control. It is no longer needed.

**Steps**:
1. Open `make.powerapps.com` > Apps
2. Find `sprk_analysisworkspace_8bc0b` (Canvas/Custom Page app)
3. Delete the app
4. Remove from any solution that includes it

### 4. Update Ribbon Commands

The `sprk_analysis_commands.js` web resource contains the legacy `Spaarke_OpenAnalysisWorkspace()` function that navigates to the old Custom Page. This function should be updated or removed:

- **Current ribbon**: `Spaarke.Analysis.OpenWorkspace.Command` in `RibbonDiff.xml` calls `Spaarke_OpenAnalysisWorkspaceFromSubgrid` from `sprk_analysis_commands.js`
- **Replacement**: Update the ribbon command to call `Spaarke.AnalysisWorkspace.openAnalysisWorkspace` from `sprk_/scripts/analysisWorkspaceLauncher.js` (the new Code Page launcher)

See `src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js` for deployment notes and ribbon configuration details.

---

## Rollback Instructions

If the Code Page migration needs to be reverted and the legacy PCF restored:

### Step 1: Rebuild the PCF Control

```bash
cd src/client/pcf/AnalysisWorkspace
npm install
npm run build
```

### Step 2: Pack the Solution

```powershell
cd src/client/pcf/AnalysisWorkspace/Solution
./pack.ps1
# Produces: Solution/bin/AnalysisWorkspaceSolution_v1.3.5.zip
```

### Step 3: Import the Solution

```powershell
pac auth create --url https://spaarkedev1.crm.dynamics.com
pac solution import --path Solution/bin/AnalysisWorkspaceSolution_v1.3.5.zip --force-overwrite --publish-changes
```

### Step 4: Re-Add PCF to the Analysis Form

1. Open `make.powerapps.com` > Tables > Analysis (sprk_analysis) > Forms
2. Open the Main form
3. Add a section/tab for the workspace
4. Add the `sprk_analysisid` field (or appropriate text field) to the section
5. Configure the field to use the `Spaarke.Controls.AnalysisWorkspace` custom control
6. Configure the control input properties:
   - `analysisId` (bound to field)
   - `tenantId`: Azure AD Tenant ID
   - `clientAppId`: PCF Client App registration ID
   - `bffAppId`: BFF API App registration ID
   - `apiBaseUrl`: (optional, loaded from environment variable)
7. Save and Publish the form

### Step 5: Restore Ribbon Commands

Update the ribbon command `Spaarke.Analysis.OpenWorkspace.Command` to call the old `Spaarke_OpenAnalysisWorkspaceFromSubgrid` function from `sprk_analysis_commands.js`.

### Step 6: Restore Custom Page (if deleted)

If the Custom Page `sprk_analysisworkspace_8bc0b` was deleted, it would need to be recreated from scratch. Consider whether this step is necessary -- the PCF can also be used directly on the form without a Custom Page wrapper.

---

## PCF Control Details (Reference)

| Property | Value |
|----------|-------|
| **Namespace** | `Spaarke.Controls` |
| **Constructor** | `AnalysisWorkspace` |
| **Full Name** | `sprk_Spaarke.Controls.AnalysisWorkspace` |
| **Version** | 1.3.5 |
| **Solution** | `AnalysisWorkspaceSolution` |
| **Control Type** | `standard` (StandardControl) |
| **React** | React 18 (bundled, not platform-provided) |
| **Key Properties** | `analysisId` (bound), `tenantId`, `clientAppId`, `bffAppId`, `apiBaseUrl` |
| **Outputs** | `workingDocumentContent`, `chatHistory`, `analysisStatus` |
| **Features Used** | WebAPI, Utility |

---

## Files in This Directory (Preserved for Reference)

```
AnalysisWorkspace/
  AnalysisWorkspace.pcfproj     # PCF project file
  package.json                   # NPM dependencies
  tsconfig.json                  # TypeScript configuration
  pcfconfig.json                 # PCF configuration
  DEPRECATED.md                  # This file
  control/
    ControlManifest.Input.xml    # PCF manifest with property definitions
    index.ts                     # PCF entry point class
    components/                  # React components
    services/                    # MSAL auth, API clients
    hooks/                       # SSE streaming hooks
    types/                       # TypeScript interfaces
    utils/                       # Logger, environment variables, markdown
    css/                         # Styles
  Solution/
    solution.xml                 # Dataverse solution manifest
    customizations.xml           # Solution customizations
    Content_Types.xml            # Solution package content types
    pack.ps1                     # Solution packaging script
```

---

*Deprecated on 2026-02-26 as part of the SprkChat Interactive Collaboration (R2) project.*
*Source code preserved for reference and potential rollback. Do not deploy.*
