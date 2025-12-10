# PCF Controls — Packaging, Versioning, and Deployment Guide

> **Related Skill**: `.claude/skills/dataverse-deploy/SKILL.md` - See deployment scenarios
>
> **Last Updated**: December 2025

This document covers two critical aspects of PCF development:
1. **Part A**: Bundle size optimization (React/Fluent v9 platform libraries)
2. **Part B**: Deployment workflow and version management (lessons learned from production deployments)

---

## Part A: Bundle Size Optimization (React/Fluent v9)

Date: 2025-10-04
Scope: This section explains why your current PCF bundle grows past 5 MB, clarifies the correct runtime model for React and Fluent UI in Power Platform, and provides precise code changes and task instructions your AI coding agent (Claude Code) can apply immediately.

## 1. Context and Goal
You are building a dataset PCF that should render with Fluent UI v9 while staying under Dataverse’s single-file upload cap (commonly 5 MB). The correct approach on Power Platform is to externalize React and Fluent via platform libraries so they are not bundled into your PCF artifact. This keeps your bundle small, aligns with modern theming, and avoids version conflicts.

## 2. Diagnosis (what’s wrong today)
It is helpful to understand why the current setup triggers the size limit and other issues before changing code.


Your package.json includes runtime dependencies on react, react-dom, and granular @fluentui/react-* v9 packages. This forces the bundler to embed these libraries into bundle.js, making it large and duplicating libraries the host can provide.


Your ControlManifest.Input.xml uses an incompatible mix: it declares dataset semantics but either uses the wrong control-type or omits &lt;platform-library&gt; entries. Without these entries, the platform cannot supply React/Fluent at runtime, so the bundle must include them.


Your import style pulls multiple granular Fluent packages instead of the converged entrypoint @fluentui/react-components. This increases surface area and complicates tree-shaking.


The effect of these choices is a bulky artifact, potential runtime duplication of React/Fluent, and a theming experience that may diverge from the host.

## 3. What “platform-library” solves (and what it does not)
This section explains the model you are moving to so the code changes make sense.


Platform libraries allow you to declare React and Fluent as host-provided. Your PCF then compiles against those APIs but does not ship them.


Model-driven apps currently load a compatible React runtime; you declare a specific supported version in the manifest (e.g., 16.14.0 for build-time compatibility) and the platform wires the appropriate runtime.


Fluent UI v9 is supported as a platform library. You should not mix v8 and v9 within one control.


You still ship your control code, styles, and any lightweight utilities. You do not ship React/Fluent bytes.


This model is the reason you do not need to downgrade to React 16 + Fluent v8 or raise file size limits; you simply stop bundling the heavy libraries.

## 4. Required code changes
The following changes bring your project to the recommended, supported pattern. Apply them as-is unless your environment enforces different version pins.
### 4.1. Fix the manifest (control type + platform libraries)
Use a dataset control and declare platform libraries so the host supplies React and Fluent v9. Keep your own code and styles in resources; do not add packaged React/Fluent scripts.
&lt;?xml version="1.0" encoding="utf-8"?&gt;
&lt;manifest&gt;
  &lt;control
    namespace="Spaarke.UI.Components"
    constructor="UniversalDatasetGrid"
    version="2.0.1"
    display-name-key="Universal Dataset Grid"
    description-key="Document management grid with SDAP integration and Fluent UI v9"
    control-type="dataset"&gt;

    &lt;!-- Primary dataset binding --&gt;
    &lt;data-set name="dataset" display-name-key="Dataset"
              cds-data-set-options="DisplayCommandBar:false" /&gt;

    &lt;!-- Example configuration (keep what you actually use) --&gt;
    &lt;property name="viewMode" of-type="Enum" usage="input" default-value="Grid"&gt;
      &lt;value name="Grid"&gt;Grid&lt;/value&gt;
      &lt;value name="Card"&gt;Card&lt;/value&gt;
      &lt;value name="List"&gt;List&lt;/value&gt;
    &lt;/property&gt;
    &lt;property name="configJson" of-type="Multiple" usage="input" required="false" /&gt;

    &lt;resources&gt;
      &lt;!-- Your code entrypoint; this produces bundle.js --&gt;
      &lt;code path="index.ts" order="1" /&gt;
      &lt;!-- Host-provided libraries: DO NOT bundle React/Fluent --&gt;
      &lt;platform-library name="React"  version="16.14.0" /&gt;
      &lt;platform-library name="Fluent" version="9.46.2" /&gt;
      &lt;css path="styles.css" order="2" /&gt;
    &lt;/resources&gt;

    &lt;feature-usage&gt;
      &lt;uses-feature name="WebAPI"     required="true" /&gt;
      &lt;uses-feature name="Navigation" required="true" /&gt;
    &lt;/feature-usage&gt;
  &lt;/control&gt;
&lt;/manifest&gt;

Why this matters: the &lt;platform-library&gt; elements are the switch that tells the platform to load React/Fluent. Without them, the bundler tries to include those libraries into your artifact.

### 4.2. Slim and correct package.json (do not bundle React/Fluent)
Move React/Fluent to devDependencies for type-checking and compilation only, and use converged imports (@fluentui/react-components). Keep icons separate. Remove granular @fluentui/react-* packages and any direct react/react-dom runtime deps.
 {
   "name": "spaarke-universal-dataset",
   "version": "2.0.1",
   "scripts": {
     "build": "pcf-scripts build",
     "build:prod": "pcf-scripts build --production",
     "start": "pcf-scripts start watch",
     "test": "jest",
     "test:coverage": "jest --coverage",
     "lint": "eslint src/**/*.{ts,tsx}",
     "type-check": "tsc --noEmit"
   },
-  "dependencies": {
-    "@fluentui/react-button": "^9.x",
-    "@fluentui/react-dialog": "^9.x",
-    "@fluentui/react-provider": "^9.x",
-    "@fluentui/react-utilities": "^9.x",
-    "react": "^18.2.0",
-    "react-dom": "^18.2.0"
-  },
+  "dependencies": {
+    "@spaarke/ui-components": "file:../../shared/Spaarke.UI.Components/spaarke-ui-components-2.0.0.tgz"
+  },
   "devDependencies": {
     "@types/node": "^18.19.86",
-    "@types/react": "^18.2.0",
-    "@types/react-dom": "^18.2.7",
-    "@fluentui/react-components": "^9.46.0",
-    "@fluentui/react-icons": "^2.0.0",
+    "@types/react": "^18.2.0",
+    "@types/react-dom": "^18.2.7",
+    "@fluentui/react-components": "^9.46.0",
+    "@fluentui/react-icons": "^2.0.0",
     "pcf-scripts": "^1.31.0",
     "pcf-start": "^1.31.0",
     "typescript": "^5.8.3",
     "jest": "^29.7.0",
     "@testing-library/react": "^14.2.1",
     "eslint": "^9.x",
     "@microsoft/eslint-plugin-power-apps": "^0.2.51"
   }
 }


If you depend on a shared package (e.g., @spaarke/ui-components), ensure it does not re-bundle React/Fluent. In that package’s package.json, React/Fluent must be peerDependencies, not dependencies:
{
  "peerDependencies": {
    "react": "&gt;=16.14.0",
    "react-dom": "&gt;=16.14.0",
    "@fluentui/react-components": "&gt;=9.4.0"
  }
}





          
            
          
        
  
        
    

4.3. Fix source imports (converged entrypoint)
Avoid granular v9 packages; import from the converged entrypoint so types and tree-shaking remain stable.
// BEFORE
import { Button } from "@fluentui/react-button";
import { Dialog } from "@fluentui/react-dialog";
import { Tooltip } from "@fluentui/react-tooltip";

// AFTER
import {
  Button,
  Dialog,
  Tooltip,
  Toolbar,
  DataGrid,
  DataGridBody,
  DataGridRow,
  DataGridCell,
  TableColumnDefinition,
  createTableColumn,
  FluentProvider,
  tokens
} from "@fluentui/react-components";
import { ArrowUpload20Regular } from "@fluentui/react-icons";

Use Griffel for styling; do not write global CSS that targets Fluent internals.
import { makeStyles, shorthands } from "@fluentui/react-components";

const useStyles = makeStyles({
  root: {
    display: "grid",
    gridTemplateRows: "auto 1fr",
    ...shorthands.padding("8px", "16px")
  }
});


## 5. Build, validate, and size-check
Once you have edited the manifest and dependencies, run a clean build and verify the artifact size.
# start clean
rm -rf node_modules
npm ci

# build the PCF
npm run build

# verify the artifact
ls -lh out/controls/**/bundle.js
# expect: comfortably under 5 MB since React/Fluent are no longer bundled

If your environment still enforces a 5 MB cap and your bundle remains oversized, something is still bundling React/Fluent (check nested deps, dynamic imports, or incorrect manifest).

## 6. Optional environment setting (only if you truly must)
As a last resort, admins can raise the environment’s maximum web resource size (up to higher limits). This is not the preferred solution; rely on platform libraries first, then adjust limits only for rare, justified cases.

## 7. AI Coding Task Card (for Claude Code)
Paste the following into your AI coding agent to apply the changes safely.
Task: Convert the PCF to a Dataset control using platform libraries for React + Fluent v9, and remove bundled React/Fluent to keep bundle &lt; 5 MB.

Inputs:
- ControlManifest.Input.xml
- package.json
- src/** imports

Steps:
1) Manifest:
   - Ensure control-type="dataset".
   - Keep &lt;data-set name="dataset" .../&gt;.
   - Add platform libraries:
     &lt;platform-library name="React"  version="16.14.0"/&gt;
     &lt;platform-library name="Fluent" version="9.46.2"/&gt;
   - Keep only index.ts and styles.css in &lt;resources&gt;.

2) package.json:
   - Remove runtime deps: react, react-dom, and all granular @fluentui/react-* packages.
   - Add devDeps only: @fluentui/react-components, @fluentui/react-icons, @types/react, @types/react-dom.
   - If @spaarke/ui-components is used, require it to list peerDependencies for React/Fluent; otherwise temporarily remove it.

3) Source imports:
   - Replace any "@fluentui/react-&lt;pkg&gt;" imports with "@fluentui/react-components".
   - Icons from "@fluentui/react-icons".
   - Style via makeStyles/shorthands; do not add global CSS selectors against Fluent internals.

4) Build &amp; verify:
   - npm ci &amp;&amp; npm run build
   - Verify out/controls/**/bundle.js &lt; 5 MB.
   - If larger, search for bundled copies of react or @fluentui/react-components in the bundle.

Definition of Done:
- PCF compiles and builds.
- bundle.js &lt; 5 MB (React/Fluent not bundled).
- Dataset semantics work (paging, selection, navigation).
- UI renders with Fluent v9 components supplied by the platform library.


## 8. Validation checklist (what to test after the change)
It is important to validate both functionality and runtime wiring.


Confirm the control renders in a model-driven app and adopts the app’s theme (modern theming).


Verify dataset behaviors: sorting reflects server state, selection syncs to the host, and paging uses dataset.paging.


Ensure no runtime conflicts: the console should not show multiple React instances or hook mismatches.


Inspect bundle size: it should be notably smaller than the previous build.


Confirm a11y basics: tab order, focus outlines, icon buttons with aria-label, and live regions for background progress.



## 9. Troubleshooting guide
If you still hit the size limit or run into runtime errors, the following items are common culprits.


Nested dependency re-bundling: a private package (e.g., @spaarke/ui-components) lists React/Fluent as dependencies instead of peerDependencies. Fix its package.json.


Wrong manifest type: the control is marked standard while you use dataset APIs. Ensure control-type="dataset".


Mixed Fluent versions: do not import v8 (@fluentui/react) anywhere; use only @fluentui/react-components for v9.


Global CSS override: removing Griffel and styling internals via global styles can break layout across themes. Keep styles local.


Host differences: model-driven vs canvas can load different React run times. Avoid React 18-specific APIs and stick to patterns compatible with React 16/17.



## 10. Example minimal render with Fluent v9 (converged import)
This snippet shows a dataset-backed grid rendered with Fluent v9 using converged imports and no bundled libraries.
import * as React from "react";
import {
  FluentProvider,
  DataGrid, DataGridBody, DataGridRow, DataGridCell,
  TableColumnDefinition, createTableColumn, tokens
} from "@fluentui/react-components";

type Row = { id: string; name: string; status: string };

const columns: TableColumnDefinition&lt;Row&gt;[] = [
  createTableColumn&lt;Row&gt;({
    columnId: "name",
    renderHeaderCell: () =&gt; "Name",
    renderCell: item =&gt; item.name
  }),
  createTableColumn&lt;Row&gt;({
    columnId: "status",
    renderHeaderCell: () =&gt; "Status",
    renderCell: item =&gt; item.status
  })
];

export function UniversalDatasetGrid(props: { items: Row[]; theme: any }) {
  return (
    &lt;FluentProvider theme={props.theme}&gt;
      &lt;div style={{ padding: tokens.spacingHorizontalM }}&gt;
        &lt;DataGrid items={props.items} columns={columns} getRowId={r =&gt; r.id}&gt;
          &lt;DataGridBody&gt;
            {({ item }) =&gt; (
              &lt;DataGridRow&gt;
                {({ renderCell }) =&gt; &lt;DataGridCell&gt;{renderCell(item)}&lt;/DataGridCell&gt;}
              &lt;/DataGridRow&gt;
            )}
          &lt;/DataGridBody&gt;
        &lt;/DataGrid&gt;
      &lt;/div&gt;
    &lt;/FluentProvider&gt;
  );
}

This code compiles against @fluentui/react-components but at runtime the platform supplies Fluent and React, keeping your bundle small.

## 11. Conclusion (Part A)
You do not need to downgrade to React 16 + Fluent v8 to meet the size limit. The correct fix is to stop bundling React and Fluent UI v9 and rely on platform libraries in your manifest. The changes in this document align your control with Microsoft's modern theming and runtime model, reduce your artifact size well under 5 MB, and keep your component compatible across model-driven and custom page deployments.

---

## Part B: PCF Deployment Workflow and Version Management

> **Last Updated**: December 2025 (based on production deployment lessons learned)

This section documents the correct PCF deployment workflow, common pitfalls, and their resolutions. Following this process ensures reliable deployments and proper version tracking.

---

## 12. PCF Version Requirements

### 12.1 Mandatory Version Footer

**Every PCF control MUST display a version footer** in the UI with:
- Version number (semantic versioning: MAJOR.MINOR.PATCH)
- Build date (ISO format: YYYY-MM-DD)

```typescript
// Footer component pattern
<span className={styles.versionText}>
    v3.2.4 • Built 2025-12-09
</span>
```

This allows users and developers to immediately verify which version is running without accessing developer tools.

### 12.2 Version Location Synchronization

PCF versions must be synchronized in **FOUR locations**:

| Location | File | Example |
|----------|------|---------|
| **1. Control Manifest** | `ControlManifest.Input.xml` | `version="3.2.4"` |
| **2. UI Footer** | Component `.tsx` file | `v3.2.4 • Built 2025-12-09` |
| **3. Solution Manifest** | `solution.xml` or `Solution.xml` | `<Version>3.2.4</Version>` |
| **4. Dataverse Registry** | Verified via `pac solution list` | Displayed after import |

**CRITICAL**: All four must match. Mismatches cause confusion and deployment failures.

---

## 13. Two Deployment Methods Compared

### 13.1 Method A: `pac pcf push` (Quick Development)

**Best for**: Rapid iteration during development

```bash
# From control directory (contains .pcfproj)
pac pcf push --publisher-prefix sprk
```

**Pros**:
- Fast and simple
- Builds and deploys in one step

**Cons**:
- Creates a **temporary solution** (e.g., `PowerAppsToolsTemp_sprk`)
- Does NOT update named solution version (e.g., `UniversalQuickCreate` stays at old version)
- Can cause version confusion when checking `pac solution list`

**When to use**: Development testing only. Not for production releases.

### 13.2 Method B: Solution Import (Production Releases)

**Best for**: Production deployments, version-controlled releases

```bash
# Full workflow - see Section 14 for detailed steps
npm run build
# Update versions in manifest and solution.xml
# Copy bundle to solution folder
pac solution pack --zipfile Solution_vX.Y.Z.zip --folder Solution_extracted
pac solution import --path Solution_vX.Y.Z.zip --force-overwrite --publish-changes
```

**Pros**:
- Updates named solution version visible in `pac solution list`
- Proper audit trail for version tracking
- Solution can be exported and moved between environments

**Cons**:
- More steps required
- Must manage solution folder structure

**When to use**: All production releases and version updates.

---

## 14. Complete Production Deployment Workflow

### Step 1: Build the PCF Control

```bash
cd src/client/pcf/{ControlName}
npm run build
```

Verify the bundle was created:
```bash
ls -la out/controls/control/bundle.js
# Expected: bundle.js should exist (typically 500KB-1MB with platform libraries)
```

### Step 2: Update Version Numbers

Update ALL four locations:

**A. ControlManifest.Input.xml** (source):
```xml
<control ... version="3.2.4" description-key="Feature description (v3.2.4)">
```

**B. UI Footer** (source component):
```typescript
<span>v3.2.4 • Built 2025-12-09</span>
```

**C. Solution Manifest** (solution folder):
- Location: `{Solution}_extracted/Other/Solution.xml` OR `{Solution}_extracted/solution.xml`
```xml
<Version>3.2.4</Version>
```

**D. Extracted ControlManifest.xml** (solution folder):
- Location: `{Solution}_extracted/Controls/{namespace}.{ControlName}/ControlManifest.xml`
```xml
<control ... version="3.2.4">
```

### Step 3: Copy Bundle to Solution Folder

```bash
# Copy fresh bundle to solution Controls folder
cp out/controls/control/bundle.js \
   infrastructure/dataverse/ribbon/temp/{Solution}_extracted/Controls/{namespace}.{ControlName}/

# Verify size (should be similar to source)
ls -la infrastructure/dataverse/ribbon/temp/{Solution}_extracted/Controls/{namespace}.{ControlName}/bundle.js
```

### Step 4: Ensure Correct Solution Structure

The `pac solution pack` command requires a specific folder structure:

```
{Solution}_extracted/
├── Other/
│   ├── Solution.xml          # ← REQUIRED here for pac solution pack
│   └── Customizations.xml    # ← REQUIRED here for pac solution pack
├── Controls/
│   └── {namespace}.{ControlName}/
│       ├── ControlManifest.xml
│       └── bundle.js
├── WebResources/             # Optional
├── CanvasApps/               # If using Custom Pages
└── [other folders as needed]
```

**CRITICAL**: If `Solution.xml` and `Customizations.xml` are in the root folder (not `Other/`), move them:

```bash
cd infrastructure/dataverse/ribbon/temp/{Solution}_extracted

# Create Other folder if needed
mkdir -p Other

# Move files if they're in root
mv solution.xml Other/Solution.xml
mv customizations.xml Other/Customizations.xml
```

### Step 5: Pack the Solution

```bash
cd infrastructure/dataverse/ribbon/temp

pac solution pack \
    --zipfile {SolutionName}_v{X.Y.Z}.zip \
    --folder {Solution}_extracted \
    --packagetype Unmanaged
```

Expected output: `Solution successfully packed to {SolutionName}_v{X.Y.Z}.zip`

### Step 6: Import to Dataverse

```bash
pac solution import \
    --path {SolutionName}_v{X.Y.Z}.zip \
    --force-overwrite \
    --publish-changes
```

Expected output: `Solution import succeeded`

### Step 7: Verify Deployment

```bash
# Check solution version in Dataverse
pac solution list | grep -i "{SolutionName}"
```

**Expected output** shows new version:
```
UniversalQuickCreate    Universal Quick Create    3.2.4    False
```

### Step 8: Verify in Browser

1. **Hard refresh** the Power Apps page (`Ctrl+Shift+R`)
2. Open the control/form using the PCF
3. Check the **footer shows the new version** (e.g., `v3.2.4 • Built 2025-12-09`)

---

## 15. Common Pitfalls and Resolutions

### Pitfall 1: "pac pcf push worked but solution version didn't change"

**Symptom**: `pac solution list` shows old version (e.g., 2.2.0) even after `pac pcf push`

**Cause**: `pac pcf push` updates the control in Dataverse but creates a temporary solution. It does NOT update your named solution's version.

**Resolution**: Use Method B (Solution Import) for production releases. The named solution version will only update when you import a solution package with an updated `Solution.xml`.

### Pitfall 2: "pac solution pack fails - missing customizations.xml"

**Symptom**: Error about missing `Other/Customizations.xml`

**Cause**: Solution folder has files in wrong location (root instead of `Other/`)

**Resolution**:
```bash
mkdir -p Other
mv solution.xml Other/Solution.xml
mv customizations.xml Other/Customizations.xml
```

### Pitfall 3: "Imported solution but old bundle is still running"

**Symptom**: UI shows old version footer, or features missing

**Cause**:
- Browser cache
- Bundle.js wasn't copied to solution folder
- Old bundle.js was in solution folder

**Resolution**:
1. Verify bundle was copied: `ls -la {Solution}_extracted/Controls/{...}/bundle.js`
2. Verify file size matches source: compare with `out/controls/control/bundle.js`
3. Hard refresh browser: `Ctrl+Shift+R`
4. Check footer version matches expected version

### Pitfall 4: "Version shows correct but features missing"

**Symptom**: Footer shows v3.2.4 but new features don't work

**Cause**: Version was updated in code but bundle wasn't rebuilt

**Resolution**: Always rebuild before deployment:
```bash
npm run build
# Then copy fresh bundle to solution folder
```

### Pitfall 5: "Canvas App Custom Page shows wrong PCF version"

**Symptom**: Opening Custom Page in Power Apps Studio shows old version or prompts to "Update component"

**Cause**: Canvas Apps have an **embedded copy** of the PCF bundle that must also be updated

**Resolution**: See "Scenario 1c: PCF Control in Custom Page" in `.claude/skills/dataverse-deploy/SKILL.md`

---

## 16. Pre-Deployment Checklist

Before every PCF deployment, verify:

- [ ] **Code changes complete** - All features/fixes implemented
- [ ] **Version updated in 4 locations**:
  - [ ] `ControlManifest.Input.xml` (source)
  - [ ] UI footer in component code
  - [ ] `Solution.xml` (solution folder)
  - [ ] `ControlManifest.xml` (solution Controls folder)
- [ ] **Bundle rebuilt** - `npm run build` completed successfully
- [ ] **Bundle copied** - Fresh `bundle.js` copied to solution Controls folder
- [ ] **Bundle size verified** - Similar size to source bundle
- [ ] **Solution structure correct** - `Other/Solution.xml` and `Other/Customizations.xml` exist
- [ ] **Solution packed** - `pac solution pack` succeeded
- [ ] **Solution imported** - `pac solution import` succeeded
- [ ] **Version verified** - `pac solution list` shows new version
- [ ] **Browser verified** - Hard refresh shows new footer version

---

## 17. Quick Reference Commands

```bash
# Build PCF
cd src/client/pcf/{ControlName}
npm run build

# Check bundle size
ls -la out/controls/control/bundle.js

# Copy bundle to solution
cp out/controls/control/bundle.js \
   infrastructure/dataverse/ribbon/temp/{Solution}_extracted/Controls/{ns}.{Control}/

# Pack solution
pac solution pack \
    --zipfile {Solution}_v{X.Y.Z}.zip \
    --folder {Solution}_extracted \
    --packagetype Unmanaged

# Import solution
pac solution import \
    --path {Solution}_v{X.Y.Z}.zip \
    --force-overwrite \
    --publish-changes

# Verify deployment
pac solution list | grep -i "{SolutionName}"
```

---

## 18. Solution Folder Locations (Spaarke)

| Solution | Extracted Folder Location |
|----------|---------------------------|
| UniversalQuickCreate | `infrastructure/dataverse/ribbon/temp/UniversalQuickCreate_extracted/` |
| ApplicationRibbon | `infrastructure/dataverse/ribbon/temp/ApplicationRibbon_extracted/` |
| SpaarkeCore | `infrastructure/dataverse/ribbon/temp/SpaarkeCore_extracted/` |

---

## 19. Example: Full UniversalQuickCreate Deployment

```bash
# 1. Build
cd /c/code_files/spaarke/src/client/pcf/UniversalQuickCreate
npm run build

# 2. Update versions (manual - edit these files):
#    - control/ControlManifest.Input.xml -> version="3.2.4"
#    - control/components/DocumentUploadForm.tsx -> "v3.2.4 • Built 2025-12-09"

# 3. Navigate to solution folder
cd /c/code_files/spaarke/infrastructure/dataverse/ribbon/temp/UniversalQuickCreate_extracted

# 4. Update solution version
#    - Edit Other/Solution.xml -> <Version>3.2.4</Version>
#    - Edit Controls/sprk_Spaarke.Controls.UniversalDocumentUpload/ControlManifest.xml -> version="3.2.4"

# 5. Copy fresh bundle
cp /c/code_files/spaarke/src/client/pcf/UniversalQuickCreate/out/controls/control/bundle.js \
   Controls/sprk_Spaarke.Controls.UniversalDocumentUpload/

# 6. Pack solution
cd ..
pac solution pack \
    --zipfile UniversalQuickCreate_v3.2.4.zip \
    --folder UniversalQuickCreate_extracted \
    --packagetype Unmanaged

# 7. Import
pac solution import \
    --path UniversalQuickCreate_v3.2.4.zip \
    --force-overwrite \
    --publish-changes

# 8. Verify
pac solution list | grep -i UniversalQuickCreate
# Expected: UniversalQuickCreate    Universal Quick Create    3.2.4    False
```

---

*End of Part B - Deployment Workflow and Version Management*
        