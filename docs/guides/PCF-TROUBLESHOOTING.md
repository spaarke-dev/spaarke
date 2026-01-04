# PCF Troubleshooting Guide

> **Use When**: Encountering errors during PCF build, deploy, or runtime
>
> **Organization**: Errors grouped by phase (Build, Deploy, Runtime)

---

## Build Errors

### NU1008: Central Package Management Conflict

**Error:**
```
error NU1008: Projects that use central package version management should not define Version on PackageReference items.
```

**Cause:** PAC CLI conflicts with Directory.Packages.props

**Solution:**
```bash
# Disable before pac commands
mv /c/code_files/spaarke/Directory.Packages.props /c/code_files/spaarke/Directory.Packages.props.disabled

# Run your pac command
pac pcf push --publisher-prefix sprk

# Restore after
mv /c/code_files/spaarke/Directory.Packages.props.disabled /c/code_files/spaarke/Directory.Packages.props
```

### Bundle Too Large (>5MB)

**Error:** Import fails or build warning about bundle size

**Cause:** React/Fluent UI bundled into control instead of using platform libraries

**Solution:**
1. Add platform libraries to `ControlManifest.Input.xml`:
```xml
<platform-library name="React" version="16.14.0" />
<platform-library name="Fluent" version="9.46.2" />
```

2. Move to devDependencies in `package.json`:
```json
{
  "devDependencies": {
    "@fluentui/react-components": "^9.46.0",
    "@types/react": "^18.2.0"
  }
}
```

See [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) Part A for full details.

### TypeScript Compilation Errors

**Error:** Various TS errors

**Solution:**
```bash
# Clean and rebuild
npm run clean
rm -rf node_modules
npm install
npm run build:prod
```

---

## Deployment Errors

### File Lock Error

**Error:**
```
Unable to remove directory "obj\Debug\Metadata"
Access to the path is denied.
```

**Cause:** Windows file lock during cleanup phase

**Status:** **Harmless** - the solution IS already packed

**Solution:** Import the already-packed solution directly:
```bash
pac solution import --path obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes
```

### No Active Authentication Profile

**Error:**
```
No active authentication profile
```

**Solution:**
```bash
# Check existing profiles
pac auth list

# Select existing profile
pac auth select --index 1

# Or create new profile
pac auth create --environment "https://your-env.crm.dynamics.com"
```

### Publisher Not Found

**Error:**
```
Publisher 'sprk' not found
```

**Solution:** Use the correct publisher prefix for your environment:
```bash
# List publishers
pac admin list-publishers

# Use correct prefix
pac pcf push --publisher-prefix sprk
```

### Solution Import Failed - Dependency Missing

**Error:**
```
Import failed: Dependency not found
```

**Solution:** Import dependent solutions first. Check solution.xml for dependencies.

### pac solution pack - Missing Files

**Error:**
```
Error: Customizations.xml not found
Error: Solution.xml not found
```

**Cause:** Files in wrong location (root instead of Other/)

**Solution:**
```bash
cd {Solution}_extracted
mkdir -p Other
mv solution.xml Other/Solution.xml
mv customizations.xml Other/Customizations.xml
```

---

## Version/Update Errors

### Version Didn't Update After pac pcf push

**Symptom:** `pac solution list` shows old version

**Cause:** `pac pcf push` creates temporary solution, doesn't update named solution

**Solution:** Use [PCF-PRODUCTION-RELEASE.md](PCF-PRODUCTION-RELEASE.md) workflow for version-tracked releases.

### Old Bundle Running After Import

**Symptom:** Footer shows old version, new features missing

**Causes & Solutions:**

| Cause | Solution |
|-------|----------|
| Browser cache | Hard refresh: `Ctrl+Shift+R` |
| Bundle not copied | Copy fresh bundle to solution folder before packing |
| Custom Page not republished | Republish Custom Page in Power Apps Maker |
| Publish pending | Run `pac solution publish-all` |

### Version Correct But Features Missing

**Symptom:** Footer shows new version but features don't work

**Cause:** Version updated in code but bundle not rebuilt

**Solution:**
```bash
npm run build:prod
# Then copy fresh bundle and redeploy
```

---

## Custom Page Errors

### PCF Downgraded When Opening Power Apps Studio

**Symptom:** After editing Custom Page, PCF shows old version

**Cause:** Dataverse Registry had older version, Power Apps Studio embedded it

**Prevention:**
1. ALWAYS update Dataverse Registry FIRST
2. Run `pac pcf push` before opening Power Apps Studio

**Recovery:**
1. Close Power Apps Studio (don't save)
2. Update Dataverse Registry with `pac pcf push`
3. Re-open Custom Page and republish

### Changes Appear Then Disappear

**Cause:** Browser cache serving old version

**Solution:**
1. Open DevTools (F12)
2. Application → Storage → Clear site data
3. Hard refresh (`Ctrl+Shift+R`)

---

## Runtime Errors

### PCF Goes Blank on Toolbar Button Hover

**Symptom:** Hovering over toolbar buttons causes the entire PCF control to go blank/disappear, then reappear when mouse moves away.

**Cause:** Fluent UI v9 `Tooltip` component uses React portals to render tooltips to `document.body`. In PCF controls running within Dataverse Model-Driven Apps, this portal rendering conflicts with the Dataverse form DOM, causing the PCF container to momentarily unmount.

**Affected Components:**
- All `<Tooltip>` components from `@fluentui/react-components`
- This affects PCF controls in:
  - Entity record forms
  - Custom Pages (Canvas Apps)
  - Dashboards

**Solution:** Replace Fluent UI `Tooltip` with native HTML `title` attribute:

```tsx
// ❌ BEFORE - causes blank screen on hover in PCF
import { Tooltip, Button } from "@fluentui/react-components";

<Tooltip content="Refresh" relationship="label">
    <Button icon={<ArrowClockwiseRegular />} onClick={handleRefresh} />
</Tooltip>

// ✅ AFTER - works correctly in PCF
import { Button } from "@fluentui/react-components";

<Button
    icon={<ArrowClockwiseRegular />}
    onClick={handleRefresh}
    title="Refresh"
    aria-label="Refresh"
/>
```

**Key Points:**
1. Remove `Tooltip` import from `@fluentui/react-components`
2. Add `title` attribute directly to the button/element
3. Add `aria-label` for accessibility (screen readers)
4. Native `title` renders via browser, doesn't use portals

**Files Fixed in Spaarke (January 2026):**
- `AnalysisWorkspaceApp.tsx` - Panel header buttons
- `ToolbarPlugin.tsx` - Rich Text Editor formatting toolbar
- `SourceDocumentViewer.tsx` - Document preview toolbar
- `FilePreview.tsx` (SpeFileViewer) - File viewer toolbar and status badges

**Why This Happens:**
Fluent UI v9 uses `@fluentui/react-portal` for floating UI elements (Tooltip, Popover, Menu, Dialog). These portals render outside the PCF container, directly to `document.body`. In Dataverse, this interacts poorly with the form's React/DOM management, causing the PCF to re-render or unmount when portal elements appear.

**Note:** `Menu` and `Popover` components may also exhibit similar issues, but are less problematic because they require explicit clicks rather than hover events. Monitor for issues with these components as well.

---

### Multiple React Instances

**Error in console:**
```
Warning: Invalid hook call. Hooks can only be called inside of the body of a function component.
```

**Cause:** React bundled in control AND provided by platform

**Solution:** Remove React from dependencies, use platform libraries. See [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md).

### Control Not Rendering

**Possible Causes:**
1. JavaScript error in console - check DevTools
2. Missing platform libraries
3. Wrong control-type in manifest

**Debug Steps:**
```bash
# Check browser console for errors
# Verify manifest has correct control-type
# Ensure platform libraries declared
```

### API/Network Errors

**Error:** 401, 403, CORS errors

**Solution:**
1. Check BFF API is running
2. Verify OAuth tokens not expired
3. Check API base URL configuration

---

## Quick Diagnostic Commands

```bash
# Check auth status
pac auth list

# Check solution versions
pac solution list

# Check current environment
pac org who

# Validate solution before import
pac solution check --path Solution.zip
```

---

## Related Guides

- [PCF-QUICK-DEPLOY.md](PCF-QUICK-DEPLOY.md) - Quick dev iteration
- [PCF-PRODUCTION-RELEASE.md](PCF-PRODUCTION-RELEASE.md) - Version management
- [PCF-CUSTOM-PAGE-DEPLOY.md](PCF-CUSTOM-PAGE-DEPLOY.md) - Custom Page complexity
- [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) - Bundle optimization
