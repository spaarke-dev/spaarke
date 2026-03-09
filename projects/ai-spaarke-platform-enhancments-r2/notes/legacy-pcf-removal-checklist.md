# Legacy AnalysisWorkspace PCF Removal Checklist

> **Created**: 2026-02-26
> **Task**: R2-149 (Remove Legacy AW PCF from Solution)
> **Status**: Ready for execution
> **Prerequisite**: Task 144 (Code Page deployment) must be validated

---

## Overview

The AnalysisWorkspace PCF control (`sprk_Spaarke.Controls.AnalysisWorkspace`) was deployed as a standalone Dataverse solution (`AnalysisWorkspaceSolution`). It has been replaced by the AnalysisWorkspace Code Page (`sprk_AnalysisWorkspace` web resource). This checklist documents all steps required to fully remove the legacy PCF control from the Dataverse environment and repository.

### Replacement Artifacts (Verified Present)

| Artifact | Path | Status |
|----------|------|--------|
| Code Page HTML bundle | `src/client/code-pages/AnalysisWorkspace/out/sprk_analysisworkspace.html` | Present |
| Code Page JS bundle | `src/client/code-pages/AnalysisWorkspace/out/bundle.js` | Present |
| Launcher script | `src/client/code-pages/AnalysisWorkspace/launcher/sprk_AnalysisWorkspaceLauncher.js` | Present |

---

## Part 1: Dataverse Environment Steps (Manual Operations)

These steps must be performed in the Dataverse maker portal or via PAC CLI against the dev environment (`https://spaarkedev1.crm.dynamics.com`).

### 1.1 Verify Code Page Replacement is Working

- [ ] Open the Analysis form in the dev environment
- [ ] Confirm the Code Page launcher opens the AnalysisWorkspace Code Page dialog
- [ ] Confirm full functionality: document loading, SprkChat, AI analysis
- [ ] Confirm the ribbon button invokes `Spaarke.AnalysisWorkspace.openAnalysisWorkspace` (new launcher), NOT `Spaarke_OpenAnalysisWorkspaceFromSubgrid` (old function)

### 1.2 Remove PCF Control Binding from Analysis Form (if not already done in Task 144)

- [ ] Open `make.powerapps.com` > Tables > Analysis (`sprk_analysis`) > Forms
- [ ] Open the Main form in the form designer
- [ ] Verify no field is bound to `Spaarke.Controls.AnalysisWorkspace` custom control
- [ ] If still bound: remove the custom control binding from the field
- [ ] Save and Publish the form

### 1.3 Delete the AnalysisWorkspaceSolution from Dataverse

The PCF was deployed as a separate unmanaged solution: `AnalysisWorkspaceSolution`.

**Option A: PAC CLI (preferred)**
```powershell
# Authenticate to dev environment
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Delete the solution
pac solution delete --solution-name AnalysisWorkspaceSolution

# Publish all customizations
pac solution publish
```

**Option B: Maker Portal**
1. Open `make.powerapps.com` > Solutions
2. Find `AnalysisWorkspaceSolution`
3. Delete the solution
4. Publish all customizations

**Note**: Deleting the unmanaged solution removes the solution wrapper but may leave the PCF control as an unmanaged component. If so, proceed to 1.4.

### 1.4 Remove Orphaned PCF Control Component (if still present after solution deletion)

```powershell
# Check if the control still exists
pac pcf list | findstr "AnalysisWorkspace"

# If still present, it may need manual removal via solution component removal
```

If the control remains after solution deletion:
1. Add it to a temporary unmanaged solution
2. Remove the component from that solution
3. Delete the temporary solution
4. Publish all customizations

### 1.5 Delete the Custom Page (if still present)

The old Custom Page `sprk_analysisworkspace_8bc0b` was the host for the PCF control.

- [ ] Open `make.powerapps.com` > Apps
- [ ] Search for `sprk_analysisworkspace_8bc0b` (Canvas/Custom Page app)
- [ ] If present: delete the app
- [ ] Remove from any solution that includes it
- [ ] Publish all customizations

---

## Part 2: Ribbon Command Updates

The ribbon command that opens the Analysis Workspace must be updated to use the new Code Page launcher.

### 2.1 Current State (Legacy)

| Setting | Legacy Value |
|---------|-------------|
| Command | `Spaarke.Analysis.OpenWorkspace.Command` |
| Library | `sprk_analysis_commands.js` |
| Function | `Spaarke_OpenAnalysisWorkspaceFromSubgrid` |
| Navigates to | Custom Page `sprk_analysisworkspace_8bc0b` |

### 2.2 Target State (Code Page)

| Setting | New Value |
|---------|-----------|
| Command | `Spaarke.Analysis.OpenWorkspace.Command` (unchanged) |
| Library | `sprk_/scripts/analysisWorkspaceLauncher.js` |
| Function | `Spaarke.AnalysisWorkspace.openAnalysisWorkspace` |
| Navigates to | Web resource `sprk_AnalysisWorkspace` via `Xrm.Navigation.navigateTo()` |

### 2.3 Ribbon Update Steps

- [ ] Export the solution containing the `sprk_analysis` entity ribbon definitions
- [ ] Update the `RibbonDiffXml` for `sprk_analysis`:
  - Change the library reference from `sprk_analysis_commands.js` to `sprk_/scripts/analysisWorkspaceLauncher.js`
  - Change the function name from `Spaarke_OpenAnalysisWorkspaceFromSubgrid` to `Spaarke.AnalysisWorkspace.openAnalysisWorkspace`
- [ ] Import the updated solution
- [ ] Publish all customizations
- [ ] Test the ribbon button opens the Code Page dialog

### 2.4 Legacy sprk_analysis_commands.js Cleanup

The `sprk_analysis_commands.js` file contains both:
- **Legacy workspace functions** (to be removed): `Spaarke_OpenAnalysisWorkspace()`, `Spaarke_OpenAnalysisWorkspaceFromSubgrid()`
- **Other analysis functions** (to be kept): `NewAnalysis`, `NewAnalysisFromSubgrid`, Analysis Builder dialog functions

**Action**:
- [ ] Remove `Spaarke_OpenAnalysisWorkspace()` function (lines 145-204 in current file)
- [ ] Remove `Spaarke_OpenAnalysisWorkspaceFromSubgrid()` function (lines 213-236 in current file)
- [ ] Keep all other functions intact (they handle Analysis Builder, not the workspace)
- [ ] Update the web resource in Dataverse after modifying

---

## Part 3: Solution XML File Updates (Repository)

These are the repository-side changes to remove PCF control references from solution project files.

### 3.1 PCF Source Files (Already Deprecated)

The PCF source code at `src/client/pcf/AnalysisWorkspace/` is already marked with `DEPRECATED.md` (created in Task 068). The source files are preserved for reference and rollback capability.

**No immediate deletion required** -- the source code stays as reference. The Dataverse-side removal (Parts 1-2) is the critical action.

### 3.2 PCF Solution Files (Reference Only)

These files are part of the legacy PCF solution and describe what was deployed:

| File | Contains | Action |
|------|----------|--------|
| `src/client/pcf/AnalysisWorkspace/Solution/solution.xml` | Solution manifest with `AnalysisWorkspaceSolution` name and PCF root component | No repo change needed (deprecated) |
| `src/client/pcf/AnalysisWorkspace/Solution/customizations.xml` | `CustomControls` section with `sprk_Spaarke.Controls.AnalysisWorkspace` | No repo change needed (deprecated) |
| `src/client/pcf/AnalysisWorkspace/Solution/Content_Types.xml` | Solution package content types | No repo change needed (deprecated) |
| `src/client/pcf/AnalysisWorkspace/Solution/pack.ps1` | Build/pack script | No repo change needed (deprecated) |

### 3.3 SpaarkeCore Solution (No References Found)

Verified: The SpaarkeCore solution at `src/solutions/SpaarkeCore/` contains **no references** to the AnalysisWorkspace PCF control. The PCF was deployed as a separate solution, so no SpaarkeCore XML files need updating.

### 3.4 Other Solutions (No References Found)

Verified: No references to `AnalysisWorkspace` PCF control, `Spaarke.Controls.AnalysisWorkspace`, or `AnalysisWorkspaceSolution` exist in:
- `src/solutions/EventCommands/` -- no references
- `src/solutions/SpaarkeCore/` -- no references
- Only a benign code comment in `src/solutions/LegalWorkspace/src/config/msalConfig.ts` (line 6, just a documentation comment listing apps)

---

## Part 4: Verification After Removal

### 4.1 Dataverse Verification

- [ ] Solution list no longer shows `AnalysisWorkspaceSolution`
- [ ] Custom controls list no longer shows `sprk_Spaarke.Controls.AnalysisWorkspace`
- [ ] Custom Page `sprk_analysisworkspace_8bc0b` no longer exists in Apps
- [ ] Analysis form loads without errors
- [ ] Ribbon button opens Code Page workspace (not Custom Page)
- [ ] Full workspace functionality works (document loading, SprkChat, AI analysis)

### 4.2 Repository Verification

Run a final grep to confirm only Code Page references remain:

```bash
# Should find only: DEPRECATED.md, launcher comments, LegalWorkspace comment
grep -r "AnalysisWorkspace" src/ --include="*.ts" --include="*.tsx" --include="*.js" --include="*.xml" --include="*.cs" | grep -v DEPRECATED.md | grep -v node_modules
```

Expected results:
- `sprk_AnalysisWorkspaceLauncher.js` -- Code Page launcher (correct, keep)
- `src/client/code-pages/AnalysisWorkspace/` -- Code Page source (correct, keep)
- `src/solutions/LegalWorkspace/src/config/msalConfig.ts` -- Documentation comment (benign, keep)

### 4.3 No Orphaned References Check

- [ ] No form XML references to `Spaarke.Controls.AnalysisWorkspace` control
- [ ] No sitemap references to the PCF control
- [ ] No ribbon commands pointing to `sprk_analysis_commands.js` for workspace functions
- [ ] No environment variables or settings referencing the old Custom Page ID

---

## Part 5: Rollback Plan

If issues are discovered after PCF removal, the legacy control can be restored.

### 5.1 Quick Rollback (Restore PCF Solution)

```powershell
# Navigate to the preserved PCF source
cd src/client/pcf/AnalysisWorkspace

# Install dependencies and build
npm install
npm run build

# Pack the solution
cd Solution
./pack.ps1
# Produces: Solution/bin/AnalysisWorkspaceSolution_v1.3.5.zip

# Import to Dataverse
pac auth create --url https://spaarkedev1.crm.dynamics.com
pac solution import --path Solution/bin/AnalysisWorkspaceSolution_v1.3.5.zip --force-overwrite --publish-changes
```

### 5.2 Restore Form Binding

1. Open `sprk_analysis` Main form
2. Add `sprk_analysisid` field to the workspace section
3. Configure it to use `Spaarke.Controls.AnalysisWorkspace` custom control
4. Configure input properties (tenantId, clientAppId, bffAppId, apiBaseUrl)
5. Save and Publish

### 5.3 Restore Ribbon Commands

1. Revert ribbon command library to `sprk_analysis_commands.js`
2. Revert function name to `Spaarke_OpenAnalysisWorkspaceFromSubgrid`
3. Publish all customizations

### 5.4 Restore Custom Page (if deleted)

The Custom Page `sprk_analysisworkspace_8bc0b` would need to be recreated from scratch if deleted. Consider whether direct form binding (without Custom Page wrapper) is sufficient for rollback.

---

## Part 6: Testing Steps After Removal

### 6.1 Smoke Tests

- [ ] Open Analysis entity grid -- loads without errors
- [ ] Click ribbon "Open Workspace" button -- Code Page dialog opens
- [ ] Analysis form loads -- no errors related to missing PCF control
- [ ] Code Page loads document content correctly
- [ ] SprkChat panel is functional in Code Page
- [ ] AI analysis features work end-to-end

### 6.2 Regression Tests

- [ ] Other analysis commands still work (`NewAnalysis`, `NewAnalysisFromSubgrid`)
- [ ] Other PCF controls on the Analysis form (if any) still function
- [ ] Solution import/export cycle completes cleanly
- [ ] No console errors in browser dev tools related to missing controls
- [ ] Other entities/forms unaffected by the removal

### 6.3 Solution Health Check

```powershell
# Verify solution list is clean
pac solution list | findstr -i "AnalysisWorkspace"
# Expected: no results

# Verify solution import health
pac solution check --path <exported-solution.zip>
```

---

## Execution Order Summary

1. **Verify** Code Page replacement is fully working (Part 1.1)
2. **Update** ribbon commands to use new launcher (Part 2)
3. **Remove** PCF control binding from form if still present (Part 1.2)
4. **Delete** AnalysisWorkspaceSolution from Dataverse (Part 1.3)
5. **Delete** orphaned PCF component if remaining (Part 1.4)
6. **Delete** Custom Page app if present (Part 1.5)
7. **Clean up** legacy functions from sprk_analysis_commands.js (Part 2.4)
8. **Verify** everything works (Part 4)
9. **Run** regression tests (Part 6)

---

*Created as part of Task R2-149 -- Remove Legacy AnalysisWorkspace PCF from Dataverse Solution*
