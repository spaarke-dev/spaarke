---
description: Deploy solutions, PCF controls, and web resources to Dataverse using PAC CLI
tags: [deploy, dataverse, power-platform, pac-cli, pcf, solutions]
techStack: [dataverse, pcf-framework, power-platform]
appliesTo: ["**/Solutions/**", "**/pcf/**", "deploy to dataverse", "pac pcf push"]
alwaysApply: false
---

# Dataverse Deploy

> **Category**: Operations
> **Last Updated**: January 16, 2026
> **Primary Guide**: [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md)

---

## Critical Rules

**These rules are MANDATORY. See [PCF-DEPLOYMENT-GUIDE.md](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) for full details.**

### MUST:
- ✅ **MUST** use unmanaged solution unless explicitly told to use managed (ADR-022)
- ✅ **MUST** use Dataverse publisher `Spaarke` with prefix `sprk_`
- ✅ **MUST** rebuild fresh every deployment (`npm run build:prod`)
- ✅ **MUST** copy ALL 3 files to Solution folder (bundle.js, ControlManifest.xml, styles.css)
- ✅ **MUST** update version in ALL 5 locations
- ✅ **MUST** include `.js` and `.css` entries in `[Content_Types].xml`
- ✅ **MUST** use `pack.ps1` script (creates forward slashes in ZIP)
- ✅ **MUST** disable/restore CPM around PAC commands

### NEVER:
- ❌ **NEVER** use managed solution unless explicitly told - unmanaged is the default
- ❌ **NEVER** use or create a new publisher - always use `Spaarke` (`sprk_`)
- ❌ **NEVER** reuse old solution ZIPs - always pack fresh
- ❌ **NEVER** use `pac pcf push` - creates temp solutions, rebuilds in dev mode
- ❌ **NEVER** use `Compress-Archive` - creates backslashes, breaks import
- ❌ **NEVER** skip copying files - stale bundles cause silent failures

---

## Deployment Workflow

**Follow [PCF-DEPLOYMENT-GUIDE.md](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) for complete details.**

### Step 1: Build Fresh

```bash
cd src/client/pcf/{ControlName}
rm -rf out/ bin/
npm run build:prod

# Verify size (~200-400KB, NOT 8MB)
ls -la out/controls/control/bundle.js
```

### Step 2: Update Version (5 Locations)

| # | File | Update |
|---|------|--------|
| 1 | `control/ControlManifest.Input.xml` | `version="X.Y.Z"` |
| 2 | `control/{Component}.tsx` | UI version footer |
| 3 | `Solution/solution.xml` | `<Version>X.Y.Z</Version>` |
| 4 | `Solution/Controls/.../ControlManifest.xml` | `version="X.Y.Z"` |
| 5 | `Solution/pack.ps1` | `$version = "X.Y.Z"` |

### Step 3: Copy Fresh Build to Solution

```bash
cp out/controls/control/bundle.js \
   out/controls/control/ControlManifest.xml \
   out/controls/control/styles.css \
   Solution/Controls/sprk_Spaarke.Controls.{ControlName}/
```

### Step 4: Pack and Import

```bash
# Disable CPM
mv /c/code_files/spaarke-wt-ai-rag-pipeline/Directory.Packages.props{,.disabled}

# Pack (creates fresh ZIP with forward slashes)
cd Solution && powershell -ExecutionPolicy Bypass -File pack.ps1

# Import
pac solution import --path bin/{SolutionName}_vX.Y.Z.zip --publish-changes

# Restore CPM
mv /c/code_files/spaarke-wt-ai-rag-pipeline/Directory.Packages.props{.disabled,}
```

### Step 5: Verify

```bash
pac solution list | grep -i "{SolutionName}"
```

Hard refresh browser (`Ctrl+Shift+R`) and verify version footer.

> **⚠️ Why NOT `pac pcf push`?** It ALWAYS rebuilds in development mode, ignoring production optimizations. Bundle size increases from ~300KB to 8MB. Tree-shaking is lost. Use the pack.ps1 workflow instead.

---

## Decision Tree: Which Workflow?

```
Is the PCF embedded in a Custom Page?
├── YES → See "Custom Page Deployment" section in guide
└── NO → Use "Deployment Workflow" above (build → copy → pack.ps1 → import)
```

**Primary Guide:** [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) - Complete workflow with version management and troubleshooting.

---

## Purpose

Deploy Dataverse components using PAC CLI following the [PCF-DEPLOYMENT-GUIDE.md](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md). This skill handles authentication, Central Package Management conflicts, and solution import issues.

---

## Best Practices

| Practice | Implementation |
|----------|----------------|
| **Always use unmanaged** | Never export/pack as managed unless user explicitly requests |
| **Always use Spaarke publisher** | Never create a new publisher - use `Spaarke` (`sprk_`) |
| **Always build fresh** | Run `npm run build:prod`, never reuse old artifacts |
| **Always use pack.ps1** | Never use `Compress-Archive` (creates backslashes) |
| **Version footer** | Every PCF MUST display `vX.Y.Z • Built YYYY-MM-DD` in the UI |
| **Version bumping** | Increment version in ALL 5 locations |
| **Verify deployment** | ALWAYS run `pac solution list` after import to confirm version |
| **Use React 16 APIs** | Dataverse provides React 16.14.0 - see ADR-022 |

### Key Guidance

- **Always use the pack.ps1 workflow** - build → copy files → pack.ps1 → import
- **Never use `pac pcf push`** - it rebuilds in dev mode, ignoring production optimizations
- **Version Locations:** Update ALL 5: (1) ControlManifest.Input.xml, (2) UI footer, (3) Solution.xml, (4) extracted ControlManifest.xml, (5) pack.ps1
- **Full Guide:** See [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) for complete workflow

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

## Additional Scenarios

### Scenario 1: PCF Control Deployment

**Use the "Deployment Workflow" section above.** Follow [PCF-DEPLOYMENT-GUIDE.md](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) for the complete workflow:

1. Build fresh (`npm run build:prod`)
2. Update version in ALL 5 locations
3. Copy ALL 3 files to Solution folder
4. Pack with `pack.ps1` (NOT `Compress-Archive`)
5. Import with `pac solution import`
6. Verify with `pac solution list`

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

#### Create featureconfig.json (CRITICAL)

pcf-scripts requires a feature flag to externalize ReactDOM:

```json
{
  "pcfReactPlatformLibraries": "on"
}
```

Without this file, React is externalized but ReactDOM is still bundled, causing React version mismatch errors at runtime.

#### Enable Custom Webpack for Icon Tree-Shaking (CRITICAL for large bundles)

If your bundle is still large (>500KB) after adding platform libraries, `@fluentui/react-icons` is likely not tree-shaking. The full icon library is ~6.8MB.

**Solution**: Enable custom webpack with `sideEffects: false` for icons:

1. **Update featureconfig.json** (add `pcfAllowCustomWebpack`):
```json
{
  "pcfReactPlatformLibraries": "on",
  "pcfAllowCustomWebpack": "on"
}
```

2. **Create webpack.config.js** in control root:
```javascript
// Custom webpack configuration for PCF
// Enables tree-shaking for @fluentui/react-icons
module.exports = {
  optimization: {
    usedExports: true,
    sideEffects: true,
    innerGraph: true,
    providedExports: true
  },
  module: {
    rules: [
      {
        // Mark @fluentui/react-icons as side-effect-free
        test: /[\\/]node_modules[\\/]@fluentui[\\/]react-icons[\\/]/,
        sideEffects: false
      }
    ]
  }
};
```

**Result**: Bundle size typically drops from 5-9MB to 200-400KB.

> **⚠️ NOTE**: `pac pcf push` rebuilds in development mode, ignoring these optimizations. Use the pack.ps1 workflow instead to preserve production build.

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

> **Full details**: See [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md)

---

### Scenario 1c: PCF Control in Custom Page (COMPLEX)

**Use when**: PCF control is embedded in a Canvas App Custom Page.

> **⚠️ WARNING**: This is the most complex deployment scenario. When a PCF is used inside a Custom Page, THREE version locations must stay synchronized.

**See detailed guide**: [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) (Custom Page section)

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

**See detailed guide**: [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md)

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

# Export unmanaged solution (ALWAYS use unmanaged)
pac solution export --name "{SolutionName}" --path "./{SolutionName}.zip" --managed false
```

> **⚠️ NEVER export as managed** unless the user explicitly requests it. Managed solutions have caused issues in past projects.

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

### Publisher

**Always use `Spaarke` publisher with prefix `sprk_`** - NEVER create a new publisher.

| Setting | Value |
|---------|-------|
| Unique Name | `Spaarke` |
| Prefix | `sprk` |
| Option Value Prefix | `65949` |

**Naming examples:**
- PCF controls: `sprk_Spaarke.Controls.{ControlName}`
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
| `NU1008: Projects that use central package version management` | Directory.Packages.props conflict | Disable CPM before PAC commands |
| `Unable to remove directory "obj\Debug\Metadata"` | File lock during cleanup | **Harmless** - import the packed solution directly |
| `Solution not found` | Wrong solution name | Run `pac solution list` to find exact name |
| `Publisher not found` | Wrong publisher prefix | Use `--publisher-prefix sprk` |
| `Import failed: Dependency not found` | Missing dependent solution | Import dependencies first |
| `File exceeds 5MB limit` | React/Fluent bundled | Use platform libraries (Scenario 1b) |
| PCF version regresses in Custom Page | Registry has older version | Update registry FIRST (Scenario 1c) |
| `PowerAppsToolsTemp` solutions appear | Created by `pac pcf push` | Delete after deployment if needed |
| `Cannot create property '_updatedFibers'` | Using React 18 APIs with React 16 runtime | Use `ReactDOM.render()`, not `createRoot()` - see ADR-022 |
| `createRoot is not a function` | Importing from `react-dom/client` | Import from `react-dom` instead |
| Solution zip not created | pack.ps1 failed | Check pack.ps1 script exists and run manually |
| `Orphaned component blocking deployment` | Namespace changed or old controls exist | Delete orphaned controls via Web API (see below) |
| `CustomControls Source File styles.css does not exist` | styles.css not copied to solution folder | Copy ALL 3 files to Solution folder |

### Orphaned Control Cleanup

When namespace changes (e.g., `Spaarke.PCF` → `Spaarke.Controls`) or old deployments exist, orphaned controls can block new deployments.

**Symptoms:**
- Deployment fails with duplicate component errors
- Multiple versions of same control in solution list
- "Component with same name already exists" errors

**Solution - Delete via Web API:**

```bash
# 1. Find the orphaned control's ID
# Use Dataverse Web API or Advanced Find

# 2. Delete using PAC CLI or Web API
pac org fetch --filter "customcontrolid eq 'GUID-HERE'"

# 3. Or use Power Platform Admin Center:
# - Go to Environments → Your Environment → Settings → Solutions
# - Find and delete orphaned components
```

**Prevention:**
- Always use consistent namespace (e.g., `Spaarke.Controls`)
- Delete old solutions before changing namespace
- Use `pac solution delete` to cleanly remove old solutions

---

## Quick Reference Commands

```bash
# Authentication
pac auth list                           # Show all auth profiles
pac auth create --environment "URL"     # Create new profile
pac auth select --index N               # Switch profile

# Solutions
pac solution list                       # List all solutions
pac solution export --name X --path Y   # Export solution (add --managed false)
pac solution import --path Y            # Import solution
pac solution publish                    # Publish customizations

# PCF Controls - Use pack.ps1 workflow (see Deployment Workflow above)
npm run build:prod                      # Build control
powershell -File Solution/pack.ps1      # Pack solution (NOT Compress-Archive)
pac solution import --path bin/X.zip    # Import solution

# Troubleshooting
pac org who                             # Show current org info
pac solution check --path Y             # Validate solution before import
```

---

## Related Skills

- `ribbon-edit` - Ribbon customizations use solution export/import workflow
- `spaarke-conventions` - Naming conventions for all Dataverse components
- `adr-aware` - ADR-006 governs PCF control patterns, ADR-022 governs React version
- `ci-cd` - CI/CD pipeline status and automated deployment workflows

---

## CI/CD Integration

### Automated Plugin Deployment via GitHub Actions

Plugin deployments can be automated via the `deploy-staging.yml` workflow:

| Workflow | Trigger | What It Deploys |
|----------|---------|-----------------|
| `deploy-staging.yml` | Auto (after CI passes on master) or Manual | Dataverse plugins via PAC CLI |

### Workflow Plugin Deployment

The `deploy-plugins` job in `deploy-staging.yml`:

1. Downloads build artifacts from CI
2. Authenticates with Power Platform using service principal
3. Deploys plugin assembly via PAC CLI

```yaml
pac auth create --url $POWER_PLATFORM_URL --applicationId $CLIENT_ID --clientSecret $SECRET
pac plugin push --path ./artifacts/publish/plugins/Spaarke.Plugins.dll
```

### When to Use Manual vs Automated

| Scenario | Use |
|----------|-----|
| Plugin code changes merged to master | Automated (`deploy-staging.yml`) |
| PCF control iterative development | Manual (this skill - Quick Dev Deploy) |
| Production solution release | Manual (this skill - Scenario 1d) |
| Custom Page updates | Manual (this skill - Scenario 1c) |
| Emergency hotfix | Manual (this skill) |

### Required Secrets for Automated Deployment

| Secret | Purpose |
|--------|---------|
| `POWER_PLATFORM_URL` | Dataverse environment URL |
| `POWER_PLATFORM_CLIENT_ID` | Service principal app ID |
| `POWER_PLATFORM_CLIENT_SECRET` | Service principal secret |

### Monitor Automated Deployments

```powershell
# View staging deployment status
gh run list --workflow=deploy-staging.yml

# View specific deployment run
gh run view {run-id}

# Check deploy-plugins job
gh run view {run-id} --log --job=deploy-plugins
```

### Manual Trigger of Plugin Deployment

```powershell
# Trigger staging deployment with plugins
gh workflow run deploy-staging.yml -f deploy_plugins=true
```

## Related ADRs

| ADR | Relevance |
|-----|-----------|
| [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) | PCF over legacy webresources |
| [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) | React 16 compatibility (CRITICAL) |
| [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) | Fluent UI v9 design system |

---

## Resources

| Resource | Purpose |
|----------|---------|
| [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) | **Primary guide** - Consolidated PCF deployment workflow |
