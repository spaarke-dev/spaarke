# PCF Production Release

> **Use When**: Deploying a version-tracked PCF release to production
>
> **Time**: ~5-10 minutes
>
> **Result**: Named solution updated with proper version tracking

---

## When to Use This Workflow

| Scenario | Use This? |
|----------|-----------|
| Production release | Yes |
| Need version tracking in `pac solution list` | Yes |
| Deploying to multiple environments | Yes |
| Just testing during development | No - use [PCF-QUICK-DEPLOY.md](PCF-QUICK-DEPLOY.md) |

---

## Pre-Flight Checklist

- [ ] All code changes complete
- [ ] Tests pass
- [ ] Current PAC auth points to correct environment: `pac auth list`

---

## Workflow Steps

### Step 1: Build the Control

```bash
cd /c/code_files/spaarke/src/client/pcf/{ControlName}
npm run build:prod

# Verify bundle exists
ls -la out/controls/control/bundle.js
```

### Step 2: Update Versions (4 Locations)

**All four must match!**

| Location | File | Update To |
|----------|------|-----------|
| 1. Source Manifest | `control/ControlManifest.Input.xml` | `version="X.Y.Z"` |
| 2. UI Footer | Component `.tsx` (footer section) | `vX.Y.Z • Built YYYY-MM-DD` |
| 3. Solution Manifest | `Other/Solution.xml` | `<Version>X.Y.Z</Version>` |
| 4. Solution Control | `Controls/{...}/ControlManifest.xml` | `version="X.Y.Z"` |

### Step 3: Copy Bundle to Solution Folder

```bash
# Locate your solution extracted folder
SOLUTION_DIR="/c/code_files/spaarke/infrastructure/dataverse/solutions/{SolutionName}_extracted"

# Copy the fresh bundle
cp out/controls/control/bundle.js \
   $SOLUTION_DIR/Controls/{namespace}.{ControlName}/

# Verify file size matches source
ls -la $SOLUTION_DIR/Controls/{namespace}.{ControlName}/bundle.js
```

### Step 4: Disable Central Package Management

```bash
mv /c/code_files/spaarke/Directory.Packages.props /c/code_files/spaarke/Directory.Packages.props.disabled
```

### Step 5: Pack and Import Solution

```bash
cd /c/code_files/spaarke/infrastructure/dataverse/solutions

# Pack
pac solution pack \
    --zipfile {SolutionName}_vX.Y.Z.zip \
    --folder {SolutionName}_extracted \
    --packagetype Unmanaged

# Import
pac solution import \
    --path {SolutionName}_vX.Y.Z.zip \
    --force-overwrite \
    --publish-changes
```

### Step 6: Restore Central Package Management

```bash
mv /c/code_files/spaarke/Directory.Packages.props.disabled /c/code_files/spaarke/Directory.Packages.props
```

### Step 7: Verify Deployment

```bash
# Check version in Dataverse
pac solution list | grep -i "{SolutionName}"

# Expected output shows new version:
# SolutionName    Display Name    X.Y.Z    False
```

### Step 8: Browser Verification

1. Hard refresh the application (`Ctrl+Shift+R`)
2. Open the form/page using the PCF
3. Verify footer shows: `vX.Y.Z • Built YYYY-MM-DD`

---

## Solution Folder Locations (Spaarke)

| PCF Control | Solution Folder |
|-------------|-----------------|
| UniversalQuickCreate | `infrastructure/dataverse/solutions/UniversalQuickCreate_extracted/` |
| AnalysisWorkspace | `infrastructure/dataverse/solutions/AnalysisWorkspace_extracted/` |
| SpeFileViewer | `infrastructure/dataverse/solutions/SpeFileViewer_extracted/` |

---

## Folder Structure Requirements

```
{SolutionName}_extracted/
├── Other/
│   ├── Solution.xml          # Required
│   └── Customizations.xml    # Required
├── Controls/
│   └── {namespace}.{ControlName}/
│       ├── ControlManifest.xml
│       └── bundle.js         # Your built control
├── WebResources/             # Optional
└── CanvasApps/               # If using Custom Pages
```

**If files are in wrong location:**
```bash
mkdir -p Other
mv solution.xml Other/Solution.xml
mv customizations.xml Other/Customizations.xml
```

---

## Custom Page Additional Steps

If your PCF is embedded in a Custom Page, you MUST also republish the Custom Page. See [PCF-CUSTOM-PAGE-DEPLOY.md](PCF-CUSTOM-PAGE-DEPLOY.md).

---

## Related Guides

- [PCF-QUICK-DEPLOY.md](PCF-QUICK-DEPLOY.md) - Quick dev iteration
- [PCF-CUSTOM-PAGE-DEPLOY.md](PCF-CUSTOM-PAGE-DEPLOY.md) - Custom Page complexity
- [PCF-TROUBLESHOOTING.md](PCF-TROUBLESHOOTING.md) - Error resolution
- [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) - Comprehensive guide
