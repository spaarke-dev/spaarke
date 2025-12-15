# PCF Custom Page Deployment

> **Use When**: Deploying a PCF control that is embedded in a Canvas App Custom Page
>
> **Complexity**: HIGH - Three version locations must stay synchronized
>
> **Critical Warning**: Opening Power Apps Studio can DOWNGRADE your PCF if not done correctly

---

## Why Custom Pages Are Complex

When a PCF control is used inside a Custom Page (Canvas App), there are **THREE** independent copies:

| Location | Purpose | Updated By |
|----------|---------|------------|
| **Dataverse Registry** | Master version reference | `pac pcf push` or Solution Import |
| **Solution Controls Folder** | Exported solution artifact | Manual copy of bundle.js |
| **Canvas App Embedded** | Runtime bundle in Custom Page | Republish Custom Page |

**The Problem**: Power Apps Studio can download an OLDER version from Dataverse Registry and embed it, overwriting your new code.

---

## Critical Rules

1. **ALWAYS update Dataverse Registry FIRST** before opening Power Apps Studio
2. **ALWAYS copy bundle to BOTH locations** (Controls folder AND Canvas App embedded)
3. **NEVER open Power Apps Studio** until Dataverse Registry has the new version
4. **ALWAYS hard refresh** after any Custom Page update

---

## Complete Workflow

### Phase 1: Update Dataverse Registry

First, ensure the Dataverse component registry has your new PCF version.

```bash
# Option A: Quick Dev Deploy
cd /c/code_files/spaarke/src/client/pcf/{ControlName}
npm run build:prod
mv /c/code_files/spaarke/Directory.Packages.props{,.disabled}
pac pcf push --publisher-prefix sprk
mv /c/code_files/spaarke/Directory.Packages.props{.disabled,}

# If file lock error, import directly:
pac solution import --path obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes
```

### Phase 2: Republish Custom Page

**This step is REQUIRED to update the embedded PCF copy.**

1. Navigate to [make.powerapps.com](https://make.powerapps.com)
2. Go to **Apps** (left nav)
3. Find the Custom Page containing the PCF control
4. Click the **...** menu → **Edit**
5. Wait for Power Apps Studio to load
6. Click **File** → **Save**
7. Click **File** → **Publish**
8. Wait for "Published successfully"
9. Close Power Apps Studio

### Phase 3: Publish All Customizations

```bash
pac solution publish-all
```

### Phase 4: Clear Browser Cache

Users MUST perform hard refresh to see the new version:
- **Windows**: `Ctrl+Shift+R`
- **Mac**: `Cmd+Shift+R`

---

## Production Release with Custom Page

For version-tracked production releases:

### Step 1: Build and Update Versions

```bash
cd /c/code_files/spaarke/src/client/pcf/{ControlName}
npm run build:prod
```

Update versions in all 4 locations (see [PCF-PRODUCTION-RELEASE.md](PCF-PRODUCTION-RELEASE.md#step-2-update-versions-4-locations)).

### Step 2: Update Solution Controls Folder

```bash
# Copy bundle to solution folder
cp out/controls/control/bundle.js \
   infrastructure/dataverse/solutions/{Solution}_extracted/Controls/{namespace}.{ControlName}/
```

### Step 3: Update Canvas App Embedded Copy

The Canvas App has its own copy of the PCF bundle in the solution:

```bash
# Location varies - find it:
find infrastructure/dataverse/solutions/{Solution}_extracted/CanvasApps -name "bundle.js"

# Copy bundle there too
cp out/controls/control/bundle.js \
   infrastructure/dataverse/solutions/{Solution}_extracted/CanvasApps/{AppName}/pkgs/{guid}/
```

### Step 4: Pack and Import

```bash
mv /c/code_files/spaarke/Directory.Packages.props{,.disabled}

cd infrastructure/dataverse/solutions
pac solution pack --zipfile {Solution}_vX.Y.Z.zip --folder {Solution}_extracted
pac solution import --path {Solution}_vX.Y.Z.zip --force-overwrite --publish-changes

mv /c/code_files/spaarke/Directory.Packages.props{.disabled,}
```

### Step 5: Republish Custom Page

Even after solution import, you MUST republish the Custom Page:

1. Open Custom Page in Power Apps Maker
2. Save
3. Publish
4. Run `pac solution publish-all`

### Step 6: Verify

```bash
pac solution list | grep -i "{SolutionName}"
```

Hard refresh browser and check footer version.

---

## Troubleshooting Custom Page Issues

### "PCF shows old version after import"

1. Did you republish the Custom Page? (Most common cause)
2. Did you hard refresh browser?
3. Check both locations have new bundle:
   - `Controls/{namespace}.{ControlName}/bundle.js`
   - `CanvasApps/{AppName}/pkgs/{guid}/bundle.js`

### "Power Apps Studio prompts to update component"

This means Dataverse Registry has a different version than the embedded copy.
- If Registry is NEWER: Click "Update" - this is correct
- If Registry is OLDER: **STOP** - update Registry first via `pac pcf push`

### "Changes appear then disappear"

Browser cache issue. Clear all site data:
1. Open DevTools (F12)
2. Application tab → Storage → Clear site data
3. Hard refresh

---

## Custom Page Locations (Spaarke)

| Custom Page | Canvas App Location |
|-------------|---------------------|
| Analysis Workspace | `CanvasApps/sprk_analysisworkspace/` |
| Quick Create | `CanvasApps/sprk_quickcreate/` |

---

## Related Guides

- [PCF-QUICK-DEPLOY.md](PCF-QUICK-DEPLOY.md) - Quick dev iteration
- [PCF-PRODUCTION-RELEASE.md](PCF-PRODUCTION-RELEASE.md) - Version management
- [PCF-TROUBLESHOOTING.md](PCF-TROUBLESHOOTING.md) - Error resolution
- [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) - Comprehensive guide
