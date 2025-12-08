---
description: Deploy solutions, PCF controls, and web resources to Dataverse using PAC CLI
alwaysApply: false
---

# Dataverse Deploy

> **Category**: Operations  
> **Last Updated**: December 2025

---

## Purpose

Automate deployment of Dataverse components using PAC CLI. This skill handles the common pitfalls and environment-specific quirks that cause deployment failures, including authentication, Central Package Management conflicts, and solution import issues.

---

## Applies When

- Deploying PCF controls to Dataverse
- Importing/exporting Dataverse solutions
- Deploying web resources (JS, CSS, images)
- Publishing customizations
- **Trigger phrases**: "deploy to dataverse", "pac pcf push", "solution import", "deploy control", "publish customizations"

---

## Prerequisites Check (ALWAYS RUN FIRST)

Before ANY deployment operation, run this verification:

```powershell
# 1. Check PAC CLI is installed
pac --version

# 2. Check authentication status
pac auth list

# 3. Verify active connection points to correct environment
# Look for: Status = Active, Environment URL matches expected target
```

### Expected Output
```
Index   Active   Kind           Name         Cloud     User           Environment URL
[1]     *        DATAVERSE      dev          Public    user@org.com   https://spaarkedev1.crm.dynamics.com/
```

### If No Active Auth
```powershell
# Create new authentication
pac auth create --environment "https://YOUR-ENV.crm.dynamics.com"

# Or select existing profile
pac auth select --index 1
```

---

## Deployment Scenarios

### Scenario 1: PCF Control Deployment

**Use when**: Deploying a new or updated PCF control to Dataverse.

#### Step 1: Build the Control

```powershell
# Navigate to control directory (contains .pcfproj file)
Set-Location "c:\code_files\spaarke\src\client\pcf\{ControlName}"

# Install dependencies if needed
npm install

# Build for production
npm run build
```

#### Step 2: Handle Central Package Management (CRITICAL)

The workspace uses Central Package Management (Directory.Packages.props). PAC CLI conflicts with this. **MUST disable before `pac pcf push`**:

```powershell
# Store original location
$repoRoot = "c:\code_files\spaarke"

# Disable CPM (move the file)
if (Test-Path "$repoRoot\Directory.Packages.props") {
    Move-Item "$repoRoot\Directory.Packages.props" "$repoRoot\Directory.Packages.props.disabled" -Force
    Write-Host "✓ Disabled Directory.Packages.props" -ForegroundColor Yellow
}
```

#### Step 3: Deploy Control

```powershell
# Push to Dataverse with Spaarke publisher prefix
pac pcf push --publisher-prefix sprk
```

**Expected output**: `Successfully pushed control to Dataverse`

#### Step 4: Restore Central Package Management

```powershell
# ALWAYS restore after deployment
if (Test-Path "$repoRoot\Directory.Packages.props.disabled") {
    Move-Item "$repoRoot\Directory.Packages.props.disabled" "$repoRoot\Directory.Packages.props" -Force
    Write-Host "✓ Restored Directory.Packages.props" -ForegroundColor Green
}
```

#### Step 5: Add to Solution (Manual Step Required)

PCF controls deploy to "Default Solution" first. To add to a specific solution:

1. Go to Power Apps maker portal (make.powerapps.com)
2. Open target solution
3. Click **Add existing** → **More** → **Developer** → **Custom control**
4. Search for the control name
5. Select and click **Add**

---

### Scenario 1b: PCF Control with Platform Libraries (Large Controls)

**Use when**: PCF control exceeds 5MB due to bundled React/Fluent UI.

If your PCF bundle is too large, you're likely bundling React and Fluent UI. The correct fix is to use **platform libraries** so Dataverse provides these at runtime.

#### Check Bundle Size

```powershell
# After building, check bundle size
Get-ChildItem "out\controls\**\bundle.js" | Select-Object Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB, 2)}}
```

If > 5MB, follow the platform library approach:

#### Fix ControlManifest.Input.xml

Add `<platform-library>` elements to use host-provided React/Fluent:

```xml
<resources>
  <code path="index.ts" order="1" />
  <!-- Host-provided: DO NOT bundle React/Fluent -->
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
  <css path="styles.css" order="2" />
</resources>
```

#### Fix package.json

Move React/Fluent to `devDependencies` (type-checking only):

```json
{
  "devDependencies": {
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.7",
    "@fluentui/react-components": "^9.46.0",
    "@fluentui/react-icons": "^2.0.0"
  }
}
```

**Remove** from `dependencies`: `react`, `react-dom`, any `@fluentui/react-*` packages.

#### Fix Source Imports

Use converged imports, not granular packages:

```typescript
// ✅ CORRECT: Converged import
import { Button, Dialog, FluentProvider } from "@fluentui/react-components";
import { ArrowUpload20Regular } from "@fluentui/react-icons";

// ❌ WRONG: Granular imports (will be bundled)
import { Button } from "@fluentui/react-button";
```

> **Full details**: See `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md`

---

### Scenario 1c: PCF Control in Custom Page (CRITICAL)

**Use when**: PCF control is embedded in a Canvas App Custom Page.

> **⚠️ WARNING**: When a PCF control is used inside a Canvas App Custom Page, there are THREE version locations that must be synchronized. Failure to keep these in sync causes version regression when opening Power Apps Studio.

#### The Three PCF Version Locations

| Location | Purpose | Updated By |
|----------|---------|------------|
| **1. Dataverse Registry** | Master version Power Apps Studio checks | `pac pcf push` or Solution Import |
| **2. Solution Controls Folder** | Exported solution artifact | Manual copy to `Controls/` folder |
| **3. Canvas App Embedded** | Runtime bundle inside Custom Page | Manual copy to `CanvasApps/src/.../Other/Resources/Controls/` |

#### The Problem

When you open a Custom Page in Power Apps Studio:
1. Studio reads the **embedded** PCF version from the Canvas App
2. Studio checks the **Dataverse Registry** version
3. If registry version is NEWER, Studio shows "Update component" and replaces embedded bundle
4. **If registry version is OLDER, Studio STILL shows "Update component" and DOWNGRADES the embedded bundle!**

#### Correct Deployment Workflow

```bash
# Step 1: Build PCF control
cd src/client/pcf/{ControlName}
npm run build

# Step 2: Update Dataverse Registry FIRST
# Option A: Via pac pcf push (creates temp solution)
pac pcf push --publisher-prefix sprk

# Option B: Via solution import (preferred - avoids temp solutions)
# Build the solution with updated PCF, then import it

# Step 3: Copy bundle to BOTH solution locations
# Location 1: Solution Controls folder
cp out/controls/control/bundle.js \
   infrastructure/dataverse/ribbon/temp/{Solution}_unpacked/Controls/sprk_Spaarke.Controls.{ControlName}/

# Location 2: Canvas App embedded (path varies by Canvas App structure)
cp out/controls/control/bundle.js \
   infrastructure/dataverse/ribbon/temp/{Solution}_unpacked/CanvasApps/src/{canvasapp_guid}/Other/Resources/Controls/Spaarke.Controls.{ControlName}.bundle.js

# Step 4: Pack Canvas App first
pac canvas pack \
   --sources CanvasApps/src/{canvasapp_guid} \
   --msapp CanvasApps/{canvasapp_guid}.msapp

# Step 5: Pack solution
pac solution pack \
   --zipfile {Solution}_v{X.Y.Z}.zip \
   --folder {Solution}_unpacked \
   --packagetype Unmanaged

# Step 6: Import solution (this also updates registry if not done in Step 2)
pac solution import --path {Solution}_v{X.Y.Z}.zip --publish-changes

# Step 7: Clean up temp solutions created by pac pcf push
pac solution list | grep -i "temp\|PowerAppsTools\|PCFUpdate"
pac solution delete --solution-name "PowerAppsToolsTemp_sprk"  # if exists
pac solution delete --solution-name "PCFUpdateTemp"           # if exists
```

#### Key Rules

1. **ALWAYS update Dataverse Registry to match or exceed embedded version before opening Power Apps Studio**
2. **ALWAYS copy bundle to BOTH locations** (Controls folder AND Canvas App embedded)
3. **ALWAYS clean up temp solutions** after `pac pcf push` - they can cause version conflicts
4. **NEVER open Power Apps Studio** until all three locations are synchronized
5. **Consider skipping `pac pcf push`** and using solution import instead to avoid temp solution clutter

#### Finding the Canvas App Embedded Path

Canvas App embedded PCF location varies. Find it with:

```bash
find infrastructure/dataverse/ribbon/temp/{Solution}_unpacked -name "*{ControlName}*bundle*"
```

Typical paths:
- `CanvasApps/src/{guid}/Other/Resources/Controls/{Namespace}.{ControlName}.bundle.js`
- `CanvasApps/src/{guid}/Resources/Controls/{Namespace}.{ControlName}.bundle.js`

#### Version Synchronization Checklist

Before releasing a PCF update:

- [ ] Version bumped in `ControlManifest.Input.xml`
- [ ] Version updated in component code (footer, logs)
- [ ] PCF rebuilt (`npm run build`)
- [ ] Bundle copied to solution `Controls/` folder
- [ ] Bundle copied to Canvas App embedded location
- [ ] Canvas App packed (`pac canvas pack`)
- [ ] Solution packed (`pac solution pack`)
- [ ] Solution imported with `--publish-changes`
- [ ] Temp solutions cleaned up

---

### Scenario 2: Solution Export

**Use when**: Backing up or extracting a solution for modification.

```powershell
# List available solutions
pac solution list

# Export unmanaged solution (for editing)
pac solution export --name "{SolutionName}" --path ".\{SolutionName}.zip" --managed false

# Export managed solution (for production)
pac solution export --name "{SolutionName}" --path ".\{SolutionName}_managed.zip" --managed true
```

**Common solution names in Spaarke**:
- `SpaarkeCore` - Main platform solution
- `ApplicationRibbon` - Global ribbon customizations
- `UniversalQuickCreate` - Quick create functionality
- `ThemeMenuRibbons` - Theme menu ribbon

---

### Scenario 3: Solution Import

**Use when**: Deploying a solution package to an environment.

```powershell
# Import and publish in one step
pac solution import --path ".\{SolutionName}.zip" --publish-changes

# Import without auto-publish (faster, publish manually later)
pac solution import --path ".\{SolutionName}.zip"

# Force import (overwrites conflicts)
pac solution import --path ".\{SolutionName}.zip" --force-overwrite --publish-changes
```

#### Post-Import Verification

```powershell
# Check solution was imported
pac solution list | Select-String -Pattern "{SolutionName}"

# If not auto-published, publish manually
pac solution publish
```

---

### Scenario 4: Web Resource Deployment

**Use when**: Deploying JavaScript, CSS, or image files.

#### Option A: Via Solution (Recommended)

Include web resources in a solution and use Scenario 3.

#### Option B: Direct API Upload

Use the existing script for direct deployment:

```powershell
# Run existing web resource deployment script
& "c:\code_files\spaarke\scripts\Deploy-PCFWebResources.ps1"
```

Or use PAC CLI with a solution:

```powershell
# Export solution containing web resources
pac solution export --name "SpaarkeCore" --path ".\SpaarkeCore.zip" --managed false

# Extract, modify web resources, repackage, import
Expand-Archive -Path ".\SpaarkeCore.zip" -DestinationPath ".\SpaarkeCore_extracted" -Force

# ... modify files in WebResources folder ...

Compress-Archive -Path ".\SpaarkeCore_extracted\*" -DestinationPath ".\SpaarkeCore_modified.zip" -Force
pac solution import --path ".\SpaarkeCore_modified.zip" --publish-changes
```

---

### Scenario 5: Publish Customizations

**Use when**: Making customizations visible to users.

```powershell
# Publish all customizations
pac solution publish

# Or publish specific solution
pac solution publish --solution-name "{SolutionName}"
```

---

## Conventions

### Publisher Prefix

**Always use `sprk`** for Spaarke components:
- `pac pcf push --publisher-prefix sprk`
- Web resources: `sprk_FileName.js`
- Entities: `sprk_entityname`
- Fields: `sprk_fieldname`

### Solution Naming

| Type | Naming Pattern | Example |
|------|----------------|---------|
| Main solution | `SpaarkeCore` | `SpaarkeCore` |
| Feature solution | `{FeatureName}` | `UniversalQuickCreate` |
| Ribbon solution | `{Scope}Ribbons` | `ApplicationRibbon`, `DocumentRibbons` |
| PCF solution | `{ControlName}` | `SpeFileViewer` |

### Working Directories

| Component Type | Directory |
|----------------|-----------|
| PCF Controls | `src\client\pcf\{ControlName}` |
| Web Resources JS | `src\client\webresources\js\` |
| Web Resources CSS | `src\client\webresources\css\` |
| Icons/Images | `src\client\assets\icons\` |
| Ribbon XML | `infrastructure\dataverse\ribbon\` |
| Deployment Scripts | `scripts\` |

---

## Error Handling

| Error | Cause | Solution |
|-------|-------|----------|
| `No active authentication profile` | Not logged in | Run `pac auth create --environment "https://ENV.crm.dynamics.com"` |
| `NU1008: Projects that use central package version management` | Directory.Packages.props conflict | Disable CPM before `pac pcf push` (see Scenario 1 Step 2) |
| `Solution not found` | Wrong solution name | Run `pac solution list` to find exact name |
| `Publisher not found` | Wrong publisher prefix | Use `--publisher-prefix sprk` for Spaarke |
| `Import failed: Dependency not found` | Missing dependent solution | Import dependencies first, or use `--skip-dependency-check` |
| `Access denied` | Insufficient permissions | Verify user has System Administrator or System Customizer role |
| `Timeout during import` | Large solution or slow connection | Add `--async` flag for background import |
| `Control not found in solution` | PCF in Default Solution only | Manually add control to target solution (see Scenario 1 Step 5) |
| `Web resource name conflict` | Duplicate resource name | Use unique `sprk_` prefixed names |
| `File exceeds 5MB limit` | React/Fluent bundled in PCF | Use platform libraries (see Scenario 1b) |
| `"Update component" prompt keeps appearing` | Dataverse Registry has older/different version than embedded | Update registry FIRST, then sync all three locations (see Scenario 1c) |
| PCF version regresses after opening Power Apps Studio | Studio "updated" to older registry version | Always sync Dataverse Registry before opening Studio |
| `Unable to remove directory "obj\Debug\Metadata"` | File lock during `pac pcf push` | Close VS Code terminals, delete obj folder, or skip `pac pcf push` and use solution import |
| `PowerAppsToolsTemp` or `PCFUpdateTemp` solutions appear | Created by `pac pcf push` | Delete these temp solutions after deployment to avoid version conflicts |

---

## Quick Reference Commands

```powershell
# Authentication
pac auth list                           # Show all auth profiles
pac auth create --environment "URL"     # Create new profile
pac auth select --index N               # Switch profile

# Solutions
pac solution list                       # List all solutions
pac solution export --name X --path Y   # Export solution
pac solution import --path Y            # Import solution
pac solution publish                    # Publish all customizations

# PCF Controls
pac pcf push --publisher-prefix sprk    # Deploy control (from control folder)

# Troubleshooting
pac org who                             # Show current org info
pac solution check --path Y             # Validate solution before import
```

---

## Related Skills

- `ribbon-edit` - Ribbon customizations use solution export/import workflow
- `spaarke-conventions` - Naming conventions for all Dataverse components
- `adr-aware` - ADR-006 governs PCF control patterns

---

## Resources

| Resource | Purpose |
|----------|----------|
| `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md` | **Critical**: Platform library setup for large PCF controls |
| `scripts\Deploy-PCFWebResources.ps1` | Direct web resource deployment |
| `scripts\Deploy-CustomPage.ps1` | Custom page deployment |
| `src\client\pcf\{Control}\scripts\` | Control-specific deployment scripts |

---

## Tips for AI

### Authentication & Setup
- **ALWAYS** check `pac auth list` before any deployment command
- **ALWAYS** disable `Directory.Packages.props` before `pac pcf push`
- **ALWAYS** restore `Directory.Packages.props` after `pac pcf push`

### Solution Import
- Use `--publish-changes` on import to avoid manual publish step
- If import fails with dependency errors, try `--force-overwrite`
- Solution names are case-sensitive in some PAC CLI versions
- Prefer `pac solution export --managed false` when you need to edit the solution
- Keep backups of solutions before importing (especially in production)

### PCF Controls
- PCF controls deploy to Default Solution first - manual step to add to target solution
- **CRITICAL**: If PCF is used in a Custom Page, follow Scenario 1c workflow
- The three PCF version locations (Registry, Controls folder, Canvas App embedded) MUST be synchronized
- **Prefer solution import over `pac pcf push`** when PCF is in a Custom Page - avoids temp solutions

### Custom Page Deployment
- **NEVER open Power Apps Studio** until Dataverse Registry matches or exceeds embedded version
- After `pac pcf push`, ALWAYS clean up temp solutions (`PowerAppsToolsTemp_sprk`, `PCFUpdateTemp`)
- When deploying PCF updates: build → update registry → copy to BOTH locations → pack canvas → pack solution → import
- If file lock errors occur with `pac pcf push`, skip it and use solution import (which also updates registry)

### Version Management
- Update version in THREE places: `ControlManifest.Input.xml`, source code, and component UI
- Use semantic versioning (MAJOR.MINOR.PATCH) for PCF controls
- Include build date in UI footer for easy verification
