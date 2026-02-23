# PCF Deployment Guide

> **Last Updated**: February 2026
>
> **Related Skill**: `.claude/skills/pcf-deploy/SKILL.md`

---

## Two Control Types

This guide covers **both** PCF control types. Identify which you have before proceeding:

| Aspect | Field-Based PCF | Custom Page PCF |
|--------|----------------|-----------------|
| **Attached to** | Entity field on a form | Standalone page in model-driven app |
| **Source structure** | `control/` subfolder for source files | Source files in control root directory |
| **Build output path** | `out/controls/control/` | `out/controls/` (no `control/` subfolder) |
| **ControlManifest.Input.xml location** | `control/ControlManifest.Input.xml` | `ControlManifest.Input.xml` (root) |
| **React version in manifest** | `16.14.0` | `16.14.0` (even if code uses React 18 createRoot) |
| **Required `<property>` node** | Yes (bound to field) | Yes (can be dummy `SingleLine.Text` with `required="false"`) |
| **`styles.css` produced** | Usually yes | Often no (Griffel CSS-in-JS compiles into bundle) |
| **Bundle size** | ~200-400 KB typical | 1-5 MB typical (full app with many components) |
| **Post-import** | Bind to field on form | Create Custom Page, add to app sitemap |
| **Example** | EventFormController | LegalWorkspace |

---

## Schema Name Convention (CRITICAL)

The control schema name used throughout the solution MUST match the ControlManifest.Input.xml exactly:

```
Schema name = {publisherPrefix}_{namespace}.{constructor}
```

**Read from ControlManifest.Input.xml**:
```xml
<control namespace="Spaarke" constructor="LegalWorkspace" ...>
```
**Publisher prefix**: `sprk`

**Correct schema name**: `sprk_Spaarke.LegalWorkspace`

This name MUST appear identically in ALL of:
- `solution.xml` RootComponent `schemaName` attribute
- `customizations.xml` CustomControl `<Name>` element
- `customizations.xml` CustomControl `<FileName>` path
- Solution `Controls/` folder name
- `pack.ps1` `$ControlFolderName` variable

**Common mistake**: Adding `.Controls.` to the name (e.g., `sprk_Spaarke.Controls.LegalWorkspace`) — this does NOT match the manifest and causes: `"Root component missing for custom control Spaarke.LegalWorkspace"`

**To derive the correct name**:
1. Open `ControlManifest.Input.xml`
2. Read `namespace` and `constructor` attributes from `<control>`
3. Combine: `sprk_{namespace}.{constructor}`

| Manifest | namespace | constructor | Correct Schema Name |
|----------|-----------|-------------|-------------------|
| EventFormController | Spaarke | EventFormController | `sprk_Spaarke.EventFormController` |
| LegalWorkspace | Spaarke | LegalWorkspace | `sprk_Spaarke.LegalWorkspace` |
| AiToolAgent | Spaarke | AiToolAgent | `sprk_Spaarke.AiToolAgent` |

---

## Critical Rules

### MUST:
- **MUST** use unmanaged solution unless explicitly told to use managed (ADR-022)
- **MUST** use Dataverse publisher `Spaarke` with prefix `sprk_`
- **MUST** rebuild fresh every deployment (`npm run build`)
- **MUST** copy build output files to Solution folder (bundle.js, ControlManifest.xml, and styles.css if produced)
- **MUST** update version in ALL version locations (see Version Locations below)
- **MUST** include `.js` and `.css` entries in `[Content_Types].xml`
- **MUST** use `pack.ps1` script (creates forward slashes in ZIP)
- **MUST** disable/restore CPM around PAC commands
- **MUST** verify build output path matches your control type (see table above)

### NEVER:
- ❌ **NEVER** use managed solution unless explicitly told - unmanaged is the default
- ❌ **NEVER** use or create a new publisher - always use `Spaarke` (`sprk_`)
- ❌ **NEVER** reuse old solution ZIPs - always pack fresh
- ❌ **NEVER** use `pac pcf push` - creates temp solutions, rebuilds in dev mode
- ❌ **NEVER** use `Compress-Archive` - creates backslashes, breaks import
- ❌ **NEVER** skip copying files - stale bundles cause silent failures
- ❌ **NEVER** include web resources (JS/HTML) in a PCF-only solution — deploy web resources separately via Dataverse UI or their own solution
- ❌ **NEVER** use placeholder GUIDs (e.g., `{00000000-...}`) for web resource IDs — Dataverse rejects GUIDs that don't match existing records
- ❌ **NEVER** hand-edit `Solution/Controls/.../ControlManifest.xml` - always copy from build output
- ❌ **NEVER** use `npm run build:prod` - pcf-scripts only has `build` (no separate prod script)
- ❌ **NEVER** use special characters (em dashes, smart quotes) in pack.ps1 - breaks PowerShell parsing

---

## 🚨 PCF Solution = PCF Control ONLY

**A PCF deployment solution MUST contain only the PCF control.** Do NOT add:
- ❌ Web resources (`sprk_*.js`, `sprk_*.html`)
- ❌ Entity definitions
- ❌ Ribbon customizations
- ❌ Any other component types

**Why**: Web resources require real Dataverse GUIDs in `<WebResourceId>`. Using placeholder GUIDs causes import failures: `"component sprk_xyz.js of type 61 is not declared in the solution file as a root component"`. Web resources should be managed separately via the Dataverse UI or a dedicated web resource solution.

**The solution.xml `<RootComponents>` section MUST only contain:**
```xml
<RootComponents>
  <RootComponent type="66" schemaName="sprk_Spaarke.Controls.{ControlName}" behavior="0" />
</RootComponents>
```
- Type `66` = Custom Control (PCF). This is the ONLY type that belongs in a PCF solution.
- Do NOT add type `61` (WebResource) entries.

**The customizations.xml MUST only contain `<CustomControls>` — no `<WebResources>` section.**

---

## 🚨 File Name Casing in ZIP

**Dataverse requires specific lowercase file names inside the solution ZIP.** The XML files on disk can use any casing (e.g., `Solution.xml`, `Customizations.xml`), but the **ZIP entry names MUST be lowercase**:

| ZIP Entry Name (MUST be exact) | Disk File Name (any casing) |
|--------------------------------|---------------------------|
| `solution.xml` | `Solution.xml`, `solution.xml` |
| `customizations.xml` | `Customizations.xml`, `customizations.xml` |
| `[Content_Types].xml` | `[Content_Types].xml` |

**The `pack.ps1` script handles this mapping.** When creating ZIP entries, the entry name (second parameter) controls what appears in the ZIP — the disk file name is irrelevant.

**If you get:** `"The solution file is invalid. The compressed file must contain solution.xml, customizations.xml, and [Content_Types].xml"`
→ Check that the ZIP entry names are lowercase (not the disk file names).

---

## Deployment Workflow

### Step 1: Identify Control Type and Build Output Path

```bash
cd src/client/pcf/{ControlName}

# Check: Is ControlManifest.Input.xml in a control/ subfolder or in the root?
# Field-based:  control/ControlManifest.Input.xml  -> output at out/controls/control/
# Custom Page:  ControlManifest.Input.xml           -> output at out/controls/
```

### Step 2: Build Fresh

```bash
cd src/client/pcf/{ControlName}
rm -rf out/

npm run build

# Verify build output exists at the CORRECT path for your control type:
#   Field-based:  ls -la out/controls/control/bundle.js
#   Custom Page:  ls -la out/controls/bundle.js
```

**Important**: The build command is `npm run build` (not `build:prod`). pcf-scripts does not have a separate production build script.

### Step 3: Verify Build Output Files

After build, check what files were produced:

```bash
# Field-based PCF:
ls out/controls/control/
# Expected: bundle.js  ControlManifest.xml  styles.css

# Custom Page PCF:
ls out/controls/
# Expected: bundle.js  ControlManifest.xml  (styles.css may NOT exist)
```

**Why styles.css may be missing**: Custom Page controls using Fluent UI v9 with Griffel (makeStyles) compile all CSS into JavaScript at runtime. No separate CSS file is produced. This is normal and expected.

### Step 4: Update Version (All Locations)

#### Field-Based PCF (5 locations):

| # | File | Update |
|---|------|--------|
| 1 | `control/ControlManifest.Input.xml` | `version="X.Y.Z"` + description |
| 2 | `control/{Component}.tsx` | UI version footer |
| 3 | `Solution/solution.xml` | `<Version>X.Y.Z</Version>` |
| 4 | `Solution/Controls/.../ControlManifest.xml` | Copied from build output (auto-updated) |
| 5 | `Solution/pack.ps1` | `$version = "X.Y.Z"` (or auto-detect from solution.xml) |

#### Custom Page PCF (4 locations + auto):

| # | File | Update |
|---|------|--------|
| 1 | `ControlManifest.Input.xml` (root) | `version="X.Y.Z"` |
| 2 | `index.ts` (`CONTROL_VERSION` constant) | UI version footer |
| 3 | `Solution/solution.xml` | `<Version>X.Y.Z</Version>` |
| 4 | `Solution/Controls/.../ControlManifest.xml` | **Always copy from build output** - never edit manually |

**Note**: pack.ps1 should auto-detect version from solution.xml. If it uses a hardcoded `$version` variable, update that too.

### Step 5: Copy Fresh Build to Solution

```bash
# FIELD-BASED PCF (3 files):
cp out/controls/control/bundle.js \
   out/controls/control/ControlManifest.xml \
   out/controls/control/styles.css \
   Solution/Controls/sprk_{Namespace}.{Constructor}/

# CUSTOM PAGE PCF (2 files - styles.css typically not produced):
cp out/controls/bundle.js \
   out/controls/ControlManifest.xml \
   Solution/Controls/sprk_{Namespace}.{Constructor}/

# If styles.css WAS produced, copy it too:
cp out/controls/styles.css \
   Solution/Controls/sprk_{Namespace}.{Constructor}/ 2>/dev/null || true
```

**Critical**: The `ControlManifest.xml` in the Solution folder must ALWAYS be the one from build output. It contains the `api-version` and `built-by` attributes that pcf-scripts generates. Never hand-edit this file.

### Step 6: Pack Solution

```bash
cd Solution
powershell -ExecutionPolicy Bypass -File pack.ps1
```

This creates: `bin/{SolutionName}_v{X.Y.Z}.zip`

### Step 7: Disable CPM and Import

```bash
# Disable CPM (if applicable - only needed if Directory.Packages.props exists in parent)
mv /path/to/Directory.Packages.props{,.disabled}

# Import
pac solution import --path bin/{SolutionName}_vX.Y.Z.zip --publish-changes

# Restore CPM
mv /path/to/Directory.Packages.props{.disabled,}
```

### Step 8: Verify Import

```bash
pac solution list | grep -i "{SolutionName}"
```

### Step 9: Post-Import (differs by control type)

#### Field-Based PCF:
1. Open the entity form in the form designer
2. Add or verify the control is bound to the target field
3. Save and Publish the form
4. Hard refresh browser (`Ctrl+Shift+R`)
5. Verify version footer

#### Custom Page PCF:
1. Go to [make.powerapps.com](https://make.powerapps.com) > select environment
2. Navigate to **Solutions** > find your solution
3. **Create Custom Page** (first time only):
   - Click **New** > **Page** > **Custom Page**
   - Name the page (e.g., "Legal Workspace Home")
   - Add the PCF control to the page canvas
   - Bind the control's `boundField` property to a text field
   - Save and Publish the Custom Page
4. **Add to Model-Driven App** (first time only):
   - Open the target app in the app designer
   - Add the Custom Page to the sitemap navigation
   - Save and Publish the app
5. Run `pac solution publish-all`
6. Hard refresh browser (`Ctrl+Shift+R`)
7. Verify the workspace loads correctly

---

## ControlManifest.Input.xml Requirements

### Field-Based PCF

```xml
<manifest>
  <control namespace="Spaarke" constructor="{ControlName}" version="X.Y.Z"
           display-name-key="{Display Name}" description-key="{Description}"
           control-type="standard">
    <external-service-usage enabled="false" />
    <property name="{fieldName}" display-name-key="{Label}"
              description-key="{Desc}" of-type="SingleLine.Text"
              usage="bound" required="true" />
    <resources>
      <code path="index.ts" order="1" />
      <css path="styles.css" order="2" />
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9.46.2" />
    </resources>
  </control>
</manifest>
```

### Custom Page PCF

```xml
<manifest>
  <control namespace="Spaarke" constructor="{ControlName}" version="X.Y.Z"
           display-name-key="{Display Name}" description-key="{Description}"
           control-type="standard">
    <external-service-usage enabled="false" />
    <!-- Custom Pages still require at least one property node -->
    <property name="boundField" display-name-key="Bound Field"
              description-key="Text field to attach this control to"
              of-type="SingleLine.Text" usage="bound" required="false" />
    <resources>
      <code path="index.ts" order="1" />
      <!-- NO css path if using Griffel (makeStyles) exclusively -->
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9.46.2" />
    </resources>
    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
      <uses-feature name="Utility" required="true" />
    </feature-usage>
  </control>
</manifest>
```

**Key differences for Custom Page manifest**:
- `<property>` node is required by pcf-scripts validation (pcf-1042 error if missing), even though Custom Pages don't bind to entity fields. Use `required="false"`.
- `<feature-usage>` section needed if the control uses WebAPI or Utility features.
- No `<css path="styles.css">` if all styles are Griffel/makeStyles (CSS-in-JS compiles into bundle.js).
- React version MUST be `16.14.0` in the manifest (pcf-scripts requirement), even if code uses React 18 `createRoot` API. The Dataverse runtime provides the actual React version.
- Fluent version MUST be a specific release like `9.46.2` (not just `"9"` -- causes pcf-1063 error).

---

## Solution File Requirements

### [Content_Types].xml

**MUST include `.js` and `.css` entries.** Missing entries cause "unexpected error" with no useful message.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="xml" ContentType="application/octet-stream" />
  <Default Extension="js" ContentType="application/octet-stream" />
  <Default Extension="css" ContentType="application/octet-stream" />
</Types>
```

**Note**: Keep the `.css` entry even if no styles.css is produced. It does no harm and prevents errors if styles.css is added later.

### solution.xml

**CRITICAL: `<RootComponents>` MUST list the PCF control (type 66) ONLY. Do NOT add web resources (type 61).**

```xml
<ImportExportXml version="9.2.25124.178" SolutionPackageVersion="9.2" languagecode="1033"
    generatedBy="CrmLive" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    OrganizationVersion="9.2.25124.178" OrganizationSchemaType="Standard"
    CRMServerServiceabilityVersion="9.2.25124.00180">
  <SolutionManifest>
    <UniqueName>Spaarke{ControlName}</UniqueName>
    <Version>X.Y.Z</Version>
    <Managed>0</Managed>
    <Publisher>
      <UniqueName>Spaarke</UniqueName>
      <CustomizationPrefix>sprk</CustomizationPrefix>
      <CustomizationOptionValuePrefix>65949</CustomizationOptionValuePrefix>
      <!-- Full publisher block required - copy from working solution -->
    </Publisher>
    <RootComponents>
      <RootComponent type="66" schemaName="sprk_{Namespace}.{Constructor}" behavior="0" />
    </RootComponents>
    <MissingDependencies />
  </SolutionManifest>
</ImportExportXml>
```

### customizations.xml

**CRITICAL FORMAT RULES:**
- ❌ **WRONG:** `<CustomControl Name="...">` (attribute format)
- ✅ **CORRECT:** `<CustomControl><Name>...</Name>` (child element format)
- ✅ **MUST include:** `<EntityDataProviders />` element
- ❌ **MUST NOT include:** `<WebResources>` section (PCF solutions are PCF-only)

**Wrong format causes "unexpected error" with no useful message.**

```xml
<ImportExportXml xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    OrganizationVersion="9.2.25124.178" OrganizationSchemaType="Standard"
    CRMServerServiceabilityVersion="9.2.25124.00180">
  <Entities />
  <Roles />
  <Workflows />
  <FieldSecurityProfiles />
  <Templates />
  <EntityMaps />
  <EntityRelationships />
  <OrganizationSettings />
  <optionsets />
  <CustomControls>
    <CustomControl>
      <Name>sprk_{Namespace}.{Constructor}</Name>
      <FileName>/Controls/sprk_{Namespace}.{Constructor}/ControlManifest.xml</FileName>
    </CustomControl>
  </CustomControls>
  <EntityDataProviders />
  <Languages>
    <Language>1033</Language>
  </Languages>
</ImportExportXml>
```

### pack.ps1

**MUST use `System.IO.Compression` for forward slashes.** `Compress-Archive` breaks import.

**MUST use only ASCII characters.** Em dashes, smart quotes, and other non-ASCII characters cause PowerShell parsing errors.

**CRITICAL: ZIP entry names MUST be lowercase** for `solution.xml` and `customizations.xml` — even if the disk files use different casing. The entry name (string parameter in `CreateEntryFromFile`) controls what appears in the ZIP.

The pack.ps1 should:
1. Auto-detect version from solution.xml (not hardcoded)
2. Validate required files exist (bundle.js, ControlManifest.xml)
3. Treat styles.css as optional (may not exist for Griffel-based controls)
4. Exclude non-deployment files (README, .txt files) from the ZIP
5. Verify bundle size against NFR limits
6. Use `System.IO.Compression.ZipFile` with forward-slash entry names

```powershell
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path "bin")) { New-Item -ItemType Directory -Path "bin" | Out-Null }
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    # Root XML files - ZIP entry names MUST be lowercase
    # Disk files may use any casing (e.g., Solution.xml, Customizations.xml)
    # but the ZIP entry name parameter controls what Dataverse sees
    @(
        @{ Disk = 'Solution.xml';       Entry = 'solution.xml' },
        @{ Disk = 'Customizations.xml'; Entry = 'customizations.xml' }
    ) | ForEach-Object {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, (Join-Path $PSScriptRoot $_.Disk), $_.Entry,
            [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }

    # [Content_Types].xml - create in-memory (exact casing required)
    $contentTypes = @'
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="xml" ContentType="application/octet-stream" />
  <Default Extension="js" ContentType="application/octet-stream" />
  <Default Extension="css" ContentType="application/octet-stream" />
</Types>
'@
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($contentTypes)
    $entry = $zip.CreateEntry("[Content_Types].xml", [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Close()

    # PCF Control files ONLY (no web resources)
    $controlsDir = Join-Path $PSScriptRoot 'Controls\sprk_Spaarke.Controls.{ControlName}'
    Get-ChildItem -Path $controlsDir -File | ForEach-Object {
        $entryName = 'Controls/sprk_Spaarke.Controls.{ControlName}/' + $_.Name
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
    # Include CSS subfolder if it exists
    $cssDir = Join-Path $controlsDir 'css'
    if (Test-Path $cssDir) {
        Get-ChildItem -Path $cssDir -File | ForEach-Object {
            $entryName = 'Controls/sprk_Spaarke.Controls.{ControlName}/css/' + $_.Name
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $_.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal
            ) | Out-Null
        }
    }
} finally { $zip.Dispose() }
Write-Host "Created: $zipPath"
```

See the [LegalWorkspace pack.ps1](../../src/client/pcf/LegalWorkspace/Solution/pack.ps1) for a working reference implementation.

---

## Project Structure

### Field-Based PCF

```
src/client/pcf/{ControlName}/
+-- control/                              # Source code
|   +-- ControlManifest.Input.xml
|   +-- index.ts
|   +-- {Component}.tsx
|   +-- styles.css                        # Source CSS (if any)
+-- Solution/                             # Solution package
|   +-- Controls/sprk_{Namespace}.{Constructor}/
|   |   +-- bundle.js                     # <- Copy from out/controls/control/
|   |   +-- ControlManifest.xml           # <- Copy from out/controls/control/
|   |   +-- styles.css                    # <- Copy from out/controls/control/
|   +-- solution.xml
|   +-- customizations.xml
|   +-- [Content_Types].xml
|   +-- pack.ps1
|   +-- bin/                              # Output ZIPs
+-- out/controls/control/                 # Build output (note: control/ subfolder)
+-- featureconfig.json
+-- webpack.config.js                     # Optional (for icon tree-shaking)
```

### Custom Page PCF

```
src/client/pcf/{ControlName}/
+-- ControlManifest.Input.xml             # Source manifest (in root, NOT control/)
+-- index.ts                              # PCF entry point
+-- LegalWorkspaceApp.tsx                  # Root React component
+-- components/                           # React components
+-- hooks/                                # React hooks
+-- services/                             # API service layer
+-- types/                                # TypeScript types
+-- utils/                                # Utility functions
+-- Solution/                             # Solution package
|   +-- Controls/sprk_{Namespace}.{Constructor}/
|   |   +-- bundle.js                     # <- Copy from out/controls/ (no control/ subfolder!)
|   |   +-- ControlManifest.xml           # <- Copy from out/controls/
|   |   +-- styles.css                    # <- Copy if produced (may not exist with Griffel)
|   +-- solution.xml
|   +-- customizations.xml
|   +-- [Content_Types].xml
|   +-- pack.ps1
|   +-- bin/                              # Output ZIPs
+-- out/controls/                         # Build output (note: NO control/ subfolder)
+-- package.json
+-- tsconfig.json
+-- featureconfig.json
+-- webpack.config.js                     # Optional
```

**Key structural difference**: Custom Page controls have source files in the root directory (not in a `control/` subfolder), and the build output goes to `out/controls/` (not `out/controls/control/`).

---

## Bundle Size

### Field-Based PCF
- Target: ~200-400 KB
- If 8MB: Missing platform libraries, building in dev mode

### Custom Page PCF
- Target: 1-5 MB (full application with many components)
- Large bundles are expected for complex workspaces
- React, ReactDOM, and FluentUI are externalized via platform libraries
- Compressed ZIP is typically 5-10x smaller than raw bundle
- If over 5MB: Consider icon tree-shaking (Fluent icon chunks are very large)

### Icon Tree-Shaking

Fluent UI icons are the largest contributor to bundle size. Add a webpack.config.js:

```javascript
module.exports = {
  optimization: { usedExports: true, sideEffects: true },
  module: {
    rules: [{
      test: /[\\/]node_modules[\\/]@fluentui[\\/]react-icons[\\/]/,
      sideEffects: false
    }]
  }
};
```

And enable in featureconfig.json:
```json
{
  "pcfReactPlatformLibraries": "on",
  "pcfAllowCustomWebpack": "on"
}
```

---

## Common Build Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `The solution file is invalid. The compressed file must contain solution.xml, customizations.xml, and [Content_Types].xml` | ZIP entry names are wrong casing (e.g., `Solution.xml` instead of `solution.xml`) | Ensure pack.ps1 uses **lowercase ZIP entry names**: `solution.xml`, `customizations.xml`. The disk file casing does not matter — only the ZIP entry name string matters. |
| `pcf-1042` "At least one data-set or property should be present" | Missing `<property>` node in manifest | Add a `<property>` node (Custom Pages: use `required="false"`) |
| `pcf-1063` "Unsupported Fluent version" | Version is `"9"` instead of specific release | Change to `version="9.46.2"` (or latest installed version) |
| `TS2322` makeStyles token type mismatches | Griffel types expect narrower types than `string` | Cast token values with `as string` |
| `TS2322` invalid SkeletonItem size | Size value not in allowed set | Use only: 8, 12, 16, 20, 24, 28, 32, 36, 40, 48, 56, 64, 72, 96, 120, 128 |
| `TS2724` wrong icon names | Icon does not exist in @fluentui/react-icons | Verify exact name: use `AlertRegular` not `BellRegular`, `TaskListSquareAddRegular` not `TaskListSquareRegular` |
| Jest/test type errors | Test files included in build | Add `"exclude": ["__tests__/**", "*.test.ts", "*.test.tsx"]` to tsconfig.json |

---

## Troubleshooting (Import/Runtime)

| Error | Cause | Fix |
|-------|-------|-----|
| `Root component missing for custom control {Name}` | Schema name in solution.xml does not match `{namespace}.{constructor}` from ControlManifest | Derive correct name: `sprk_{namespace}.{constructor}` from manifest. Fix in solution.xml, customizations.xml, Controls folder name, and pack.ps1 |
| `unexpected error` | 1) Missing `.js`/`.css` in `[Content_Types].xml`, 2) Wrong `customizations.xml` format, 3) Backslashes in ZIP | Check `[Content_Types].xml` has `.js`/`.css`, verify `customizations.xml` uses `<Name>` child element (not attribute), use `pack.ps1` |
| `Root component missing for custom control` | `solution.xml` has empty `<RootComponents />` | Add `<RootComponent type="66" schemaName="sprk_Spaarke.Controls.{ControlName}" behavior="0" />` |
| `component sprk_xyz.js of type 61 is not declared as root component` | Web resource added to `customizations.xml` but not in `<RootComponents>`, or GUID doesn't match Dataverse | **Remove web resources from PCF solution entirely.** PCF solutions should contain ONLY the PCF control (type 66). Deploy web resources separately via Dataverse UI. |
| `NU1008` CPM error | CPM not disabled | Disable `Directory.Packages.props` before PAC commands |
| Bundle is 8MB (field-based) | Dev build or missing platform libraries | Ensure platform libraries declared, run `npm run build` |
| `Source File does not exist` | Missing file in Controls folder | Copy ALL files from build output |
| Version didn't update | Stale bundle copied | Rebuild fresh, copy fresh files |
| Old features still showing | Browser cache | Hard refresh `Ctrl+Shift+R` |
| pack.ps1 parsing error | Non-ASCII characters in script | Remove em dashes, smart quotes; use only ASCII |

### Orphaned Controls

If import fails with "component already exists":

```bash
# Query for orphaned controls
GET https://{org}.crm.dynamics.com/api/data/v9.2/customcontrols?$filter=contains(name,'{ControlName}')

# Delete orphaned control
DELETE https://{org}.crm.dynamics.com/api/data/v9.2/customcontrols({guid})
```

---

## Publisher Configuration

| Setting | Value |
|---------|-------|
| Unique Name | `Spaarke` |
| Prefix | `sprk` |
| Option Value Prefix | `65949` |

---

## Checklist: Solution Package Completeness

```
src/client/pcf/{ControlName}/
├── control/                          # Source code
│   ├── ControlManifest.Input.xml
│   └── {Component}.tsx
├── Solution/                         # Solution package
│   ├── Controls/sprk_Spaarke.Controls.{ControlName}/
│   │   ├── bundle.js                 # <- Copy from out/
│   │   ├── ControlManifest.xml       # <- Copy from out/
│   │   └── styles.css                # <- Copy from out/
│   ├── solution.xml                  # Or Solution.xml (casing on disk doesn't matter)
│   ├── customizations.xml            # Or Customizations.xml (pack.ps1 handles casing)
│   ├── [Content_Types].xml
│   ├── pack.ps1
│   └── bin/                          # Output ZIPs
├── out/controls/control/             # Build output
├── featureconfig.json
└── webpack.config.js
```

Before running pack.ps1, verify ALL required files exist:

### Solution Root (3 files - always required)
- [ ] `solution.xml` - Correct version, unmanaged, Spaarke publisher
- [ ] `customizations.xml` - Uses `<Name>` child element format, has `<EntityDataProviders />`
- [ ] `[Content_Types].xml` - Has `.xml`, `.js`, `.css` extensions

### Controls Folder (2-3 files)
- [ ] `Controls/sprk_Spaarke.Controls.{Name}/bundle.js` - Fresh from build output
- [ ] `Controls/sprk_Spaarke.Controls.{Name}/ControlManifest.xml` - Fresh from build output (never hand-edited)
- [ ] `Controls/sprk_Spaarke.Controls.{Name}/styles.css` - From build output (optional for Griffel-based controls)

### Verification
- [ ] No `.txt` or `README` files in the Controls folder (excluded from ZIP)
- [ ] Bundle size under NFR limit (typically 5MB)
- [ ] Version matches across solution.xml and ControlManifest.xml
- [ ] pack.ps1 uses `System.IO.Compression` (not `Compress-Archive`)

---

## Related ADRs

- [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) - PCF over legacy webresources
- [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) - Fluent UI v9
- [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) - React 16 compatibility
