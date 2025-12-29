---
description: Deploy solutions, PCF controls, and web resources to Dataverse using PAC CLI
tags: [deploy, dataverse, power-platform, pac-cli, pcf, solutions]
techStack: [dataverse, pcf-framework, power-platform]
appliesTo: ["**/Solutions/**", "**/pcf/**", "deploy to dataverse", "pac pcf push"]
alwaysApply: false
---

# Dataverse Deploy

> **Category**: Operations
> **Last Updated**: December 2025

---

## Quick Dev Deploy (90% of Use Cases)

**Use this workflow for iterative PCF development.** Takes ~60 seconds.

```bash
# 1. Navigate to control directory
cd /c/code_files/spaarke/src/client/pcf/{ControlName}

# 2. Build for production
npm run build:prod

# 3. Disable Central Package Management (REQUIRED)
mv /c/code_files/spaarke/Directory.Packages.props /c/code_files/spaarke/Directory.Packages.props.disabled

# 4. Deploy to Dataverse
pac pcf push --publisher-prefix sprk

# 5. If file lock error occurs (common), import the already-packed solution:
# pac solution import --path obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes

# 6. Restore Central Package Management (REQUIRED)
mv /c/code_files/spaarke/Directory.Packages.props.disabled /c/code_files/spaarke/Directory.Packages.props
```

### One-Liner (copy-paste ready)

```bash
cd /c/code_files/spaarke/src/client/pcf/{ControlName} && npm run build:prod && mv /c/code_files/spaarke/Directory.Packages.props{,.disabled} && pac pcf push --publisher-prefix sprk; mv /c/code_files/spaarke/Directory.Packages.props{.disabled,}
```

### File Lock Workaround

The `pac pcf push` command often fails with:
```
Unable to remove directory "obj\Debug\Metadata"
```

**This is harmless** - the solution IS already packed. Import it directly:

```bash
pac solution import --path obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes
```

---

## Decision Tree: Which Workflow?

```
Is this a production release with version tracking?
├── YES → Use "PCF Production Release" (Scenario 1d)
└── NO → Is the PCF embedded in a Custom Page?
    ├── YES → Use "PCF Custom Page Deploy" (Scenario 1c)
    └── NO → Use "Quick Dev Deploy" above
```

**Related Guides:**
- `docs/ai-knowledge/guides/PCF-QUICK-DEPLOY.md` - Streamlined dev workflow
- `docs/ai-knowledge/guides/PCF-PRODUCTION-RELEASE.md` - Version management
- `docs/ai-knowledge/guides/PCF-CUSTOM-PAGE-DEPLOY.md` - Custom Page complexity
- `docs/ai-knowledge/guides/PCF-TROUBLESHOOTING.md` - Error resolution

---

## Purpose

Automate deployment of Dataverse components using PAC CLI. This skill handles the common pitfalls and environment-specific quirks that cause deployment failures, including authentication, Central Package Management conflicts, and solution import issues.

---

## Best Practices

| Practice | Implementation |
|----------|----------------|
| **Always build fresh** | Never import from bin/ or obj/ without building first |
| **Delete old artifacts** | Run `npm run clean` or delete bin/obj before building |
| **Version footer** | Every PCF MUST display `vX.Y.Z • Built YYYY-MM-DD` in the UI |
| **Version bumping** | Increment version in 4 locations (see Scenario 1d) |
| **Verify deployment** | ALWAYS run `pac solution list` after import to confirm version |

### Key Guidance

- **Development Testing:** Use `pac pcf push` for quick iteration. Creates a **temporary solution** - does NOT update named solution version.
- **Production Releases:** Use full solution workflow (build → pack → import). This is the ONLY way to update named solution version.
- **Version Locations:** Update ALL four: (1) ControlManifest.Input.xml, (2) UI footer, (3) Solution.xml, (4) extracted ControlManifest.xml
- **Full Guide:** See `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md` Part B for detailed workflow

---

## Prerequisites Check

Before ANY deployment operation:

```bash
# 1. Check PAC CLI is installed
pac --version

# 2. Check authentication status
pac auth list

# 3. Verify active connection points to correct environment
# Look for: Active = *, Environment URL matches expected target
```

### Expected Output
```
Index Active Kind      Name         Cloud  Type Environment   Environment Url
[1]          UNIVERSAL      dev          Public User HIPC DEV 2    https://hipc-dev-2.crm.dynamics.com/
[2]   *      UNIVERSAL      prod         Public User SPAARKE DEV 1 https://spaarkedev1.crm.dynamics.com/
```

### If No Active Auth
```bash
# Create new authentication
pac auth create --environment "https://YOUR-ENV.crm.dynamics.com"

# Or select existing profile
pac auth select --index 1
```

---

## Deployment Scenarios

### Scenario 1: PCF Control Deployment (Standard)

**Use when**: Deploying a new or updated PCF control to Dataverse for development testing.

#### Step 1: Build the Control

```bash
# Navigate to control directory (contains .pcfproj file)
cd /c/code_files/spaarke/src/client/pcf/{ControlName}

# Install dependencies if needed
npm install

# Build for production
npm run build:prod
```

#### Step 2: Disable Central Package Management (CRITICAL)

The workspace uses Central Package Management (Directory.Packages.props). PAC CLI conflicts with this.

```bash
# Disable CPM
if [ -f "/c/code_files/spaarke/Directory.Packages.props" ]; then
    mv /c/code_files/spaarke/Directory.Packages.props /c/code_files/spaarke/Directory.Packages.props.disabled
    echo "✓ Disabled Directory.Packages.props"
fi
```

#### Step 3: Deploy Control

```bash
# Push to Dataverse with Spaarke publisher prefix
pac pcf push --publisher-prefix sprk
```

**If file lock error occurs** (common on Windows):
```bash
# The solution is already packed - import directly
pac solution import --path obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes
```

#### Step 4: Restore Central Package Management

```bash
# ALWAYS restore after deployment
if [ -f "/c/code_files/spaarke/Directory.Packages.props.disabled" ]; then
    mv /c/code_files/spaarke/Directory.Packages.props.disabled /c/code_files/spaarke/Directory.Packages.props
    echo "✓ Restored Directory.Packages.props"
fi
```

#### Step 5: Add to Solution (Manual Step - First Time Only)

PCF controls deploy to "Default Solution" first. To add to a specific solution:

1. Go to Power Apps maker portal (make.powerapps.com)
2. Open target solution
3. Click **Add existing** → **More** → **Developer** → **Custom control**
4. Search for the control name
5. Select and click **Add**

---

### Scenario 1b: PCF Control with Platform Libraries (Large Controls)

**Use when**: PCF bundle exceeds 5MB due to bundled React/Fluent UI.

If your bundle is > 1MB, you're likely bundling React and Fluent UI. Use **platform libraries** so Dataverse provides these at runtime.

#### Check Bundle Size

```bash
ls -la out/controls/*/bundle.js
# Should be 300-500KB with platform libraries, 5MB+ without
```

#### Fix ControlManifest.Input.xml

Add `<platform-library>` elements:

```xml
<resources>
  <code path="index.ts" order="1" />
  <!-- Host-provided: DO NOT bundle React/Fluent -->
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

#### Fix package.json

Move React/Fluent to `devDependencies` (type-checking only):

```json
{
  "devDependencies": {
    "@types/react": "^18.2.0",
    "@fluentui/react-components": "^9.46.0"
  }
}
```

**Remove** from `dependencies`: `react`, `react-dom`, any `@fluentui/react-*` packages.

> **Full details**: See `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md`

---

### Scenario 1c: PCF Control in Custom Page (COMPLEX)

**Use when**: PCF control is embedded in a Canvas App Custom Page.

> **⚠️ WARNING**: This is the most complex deployment scenario. When a PCF is used inside a Custom Page, THREE version locations must stay synchronized.

**See detailed guide**: `docs/ai-knowledge/guides/PCF-CUSTOM-PAGE-DEPLOY.md`

#### Quick Summary

| Location | Purpose | Updated By |
|----------|---------|------------|
| **Dataverse Registry** | Master version | `pac pcf push` or Solution Import |
| **Solution Controls Folder** | Exported artifact | Manual copy |
| **Canvas App Embedded** | Runtime bundle | Manual copy + `pac canvas pack` |

#### The Problem

When you open a Custom Page in Power Apps Studio, it may **downgrade** your PCF if the Dataverse Registry has an older version.

#### Key Rules

1. **ALWAYS update Dataverse Registry FIRST** before opening Power Apps Studio
2. **ALWAYS copy bundle to BOTH locations** (Controls folder AND Canvas App embedded)
3. **NEVER open Power Apps Studio** until all three locations are synchronized

---

### Scenario 1d: PCF Production Release (Full Solution Workflow)

**Use when**: Deploying a PCF update with proper version tracking for production.

> **⚠️ CRITICAL**: `pac pcf push` does NOT update your named solution's version. Use this workflow for production releases.

**See detailed guide**: `docs/ai-knowledge/guides/PCF-PRODUCTION-RELEASE.md`

#### Why This Workflow?

| Method | Updates Dataverse Control | Updates Named Solution Version | Best For |
|--------|--------------------------|-------------------------------|----------|
| `pac pcf push` | ✅ Yes | ❌ No (creates temp solution) | Dev testing |
| Solution Import | ✅ Yes | ✅ Yes | Production releases |

#### Quick Steps

```bash
# 1. Build
cd src/client/pcf/{ControlName}
npm run build:prod

# 2. Update version in 4 locations (manual)

# 3. Copy bundle to solution folder
cp out/controls/control/bundle.js \
   infrastructure/dataverse/solutions/{Solution}/Controls/{namespace}.{ControlName}/

# 4. Pack and import
pac solution pack --zipfile Solution_vX.Y.Z.zip --folder {Solution}
pac solution import --path Solution_vX.Y.Z.zip --publish-changes

# 5. Verify
pac solution list | grep -i "{SolutionName}"
```

---

### Scenario 2: Solution Export

**Use when**: Backing up or extracting a solution for modification.

```bash
# List available solutions
pac solution list

# Export unmanaged solution (for editing)
pac solution export --name "{SolutionName}" --path "./{SolutionName}.zip" --managed false

# Export managed solution (for production)
pac solution export --name "{SolutionName}" --path "./{SolutionName}_managed.zip" --managed true
```

---

### Scenario 3: Solution Import

**Use when**: Deploying a solution package to an environment.

```bash
# Import and publish in one step
pac solution import --path "./{SolutionName}.zip" --publish-changes

# Force import (overwrites conflicts)
pac solution import --path "./{SolutionName}.zip" --force-overwrite --publish-changes
```

#### Post-Import Verification

```bash
# Check solution was imported
pac solution list | grep -i "{SolutionName}"

# If not auto-published, publish manually
pac solution publish
```

---

### Scenario 4: Web Resource Deployment

**Use when**: Deploying JavaScript, CSS, or image files.

Include web resources in a solution and use Scenario 3.

```bash
# Export solution containing web resources
pac solution export --name "SpaarkeCore" --path "./SpaarkeCore.zip" --managed false

# Extract, modify, repack, import
unzip SpaarkeCore.zip -d SpaarkeCore_extracted
# ... modify files in WebResources folder ...
pac solution pack --zipfile SpaarkeCore_modified.zip --folder SpaarkeCore_extracted
pac solution import --path SpaarkeCore_modified.zip --publish-changes
```

---

### Scenario 5: Publish Customizations

**Use when**: Making customizations visible to users.

```bash
# Publish all customizations
pac solution publish

# Or use publish-all for everything
pac solution publish-all
```

---

## Conventions

### Publisher Prefix

**Always use `sprk`** for Spaarke components:
- `pac pcf push --publisher-prefix sprk`
- Web resources: `sprk_FileName.js`
- Entities: `sprk_entityname`
- Fields: `sprk_fieldname`

### Working Directories

| Component Type | Directory |
|----------------|-----------|
| PCF Controls | `src/client/pcf/{ControlName}` |
| Web Resources JS | `src/client/webresources/js/` |
| Ribbon XML | `infrastructure/dataverse/ribbon/` |
| Solutions | `infrastructure/dataverse/solutions/` |

---

## Error Handling

| Error | Cause | Solution |
|-------|-------|----------|
| `No active authentication profile` | Not logged in | Run `pac auth create --environment "URL"` |
| `NU1008: Projects that use central package version management` | Directory.Packages.props conflict | Disable CPM before `pac pcf push` |
| `Unable to remove directory "obj\Debug\Metadata"` | File lock during cleanup | **Harmless** - import the packed solution directly (see Quick Dev Deploy) |
| `Solution not found` | Wrong solution name | Run `pac solution list` to find exact name |
| `Publisher not found` | Wrong publisher prefix | Use `--publisher-prefix sprk` |
| `Import failed: Dependency not found` | Missing dependent solution | Import dependencies first |
| `File exceeds 5MB limit` | React/Fluent bundled | Use platform libraries (Scenario 1b) |
| PCF version regresses in Custom Page | Registry has older version | Update registry FIRST (Scenario 1c) |
| `PowerAppsToolsTemp` solutions appear | Created by `pac pcf push` | Delete after deployment if needed |

---

## Quick Reference Commands

```bash
# Authentication
pac auth list                           # Show all auth profiles
pac auth create --environment "URL"     # Create new profile
pac auth select --index N               # Switch profile

# Solutions
pac solution list                       # List all solutions
pac solution export --name X --path Y   # Export solution
pac solution import --path Y            # Import solution
pac solution publish                    # Publish customizations

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
|----------|---------|
| `docs/ai-knowledge/guides/PCF-QUICK-DEPLOY.md` | Streamlined dev workflow |
| `docs/ai-knowledge/guides/PCF-PRODUCTION-RELEASE.md` | Version management |
| `docs/ai-knowledge/guides/PCF-CUSTOM-PAGE-DEPLOY.md` | Custom Page complexity |
| `docs/ai-knowledge/guides/PCF-TROUBLESHOOTING.md` | Error resolution |
| `docs/ai-knowledge/guides/PCF-V9-PACKAGING.md` | Platform library setup |
