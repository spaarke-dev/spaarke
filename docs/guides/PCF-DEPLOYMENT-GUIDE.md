# PCF Deployment Guide

> **Last Updated**: February 2026
>
> **Related Skill**: `.claude/skills/pcf-deploy/SKILL.md`

---

## Critical Rules

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
- ❌ **NEVER** include web resources (JS/HTML) in a PCF-only solution — deploy web resources separately via Dataverse UI or their own solution
- ❌ **NEVER** use placeholder GUIDs (e.g., `{00000000-...}`) for web resource IDs — Dataverse rejects GUIDs that don't match existing records

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
| 1 | `control/ControlManifest.Input.xml` | `version="X.Y.Z"` + description |
| 2 | `control/{Component}.tsx` | UI version footer |
| 3 | `Solution/solution.xml` | `<Version>X.Y.Z</Version>` |
| 4 | `Solution/Controls/.../ControlManifest.xml` | `version="X.Y.Z"` + description |
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
mv /c/code_files/spaarke-wt-ai-node-playbook-builder/Directory.Packages.props{,.disabled}

# Pack (creates fresh ZIP with forward slashes and lowercase entry names)
cd Solution && powershell -ExecutionPolicy Bypass -File pack.ps1

# Import
pac solution import --path bin/{SolutionName}_vX.Y.Z.zip --publish-changes

# Restore CPM
mv /c/code_files/spaarke-wt-ai-node-playbook-builder/Directory.Packages.props{.disabled,}
```

### Step 5: Verify

```bash
pac solution list | grep -i "{SolutionName}"
```

Hard refresh browser (`Ctrl+Shift+R`) and verify version footer.

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
      <RootComponent type="66" schemaName="sprk_Spaarke.Controls.{ControlName}" behavior="0" />
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
      <Name>sprk_Spaarke.Controls.{ControlName}</Name>
      <FileName>/Controls/sprk_Spaarke.Controls.{ControlName}/ControlManifest.xml</FileName>
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

**CRITICAL: ZIP entry names MUST be lowercase** for `solution.xml` and `customizations.xml` — even if the disk files use different casing. The entry name (string parameter in `CreateEntryFromFile`) controls what appears in the ZIP.

```powershell
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

Set-Location $PSScriptRoot
$version = "X.Y.Z"
$zipPath = "bin\{SolutionName}_v$version.zip"

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

---

## Bundle Size Optimization

If bundle exceeds 500KB, add platform libraries.

### ControlManifest.Input.xml

```xml
<resources>
  <code path="index.ts" order="1" />
  <css path="styles.css" order="2" />
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

### featureconfig.json

```json
{
  "pcfReactPlatformLibraries": "on",
  "pcfAllowCustomWebpack": "on"
}
```

### webpack.config.js (for icon tree-shaking)

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

---

## Custom Page Deployment

If PCF is in a Custom Page, after solution import:

1. Open Custom Page in [make.powerapps.com](https://make.powerapps.com)
2. Save and Publish
3. Run `pac solution publish-all`
4. Hard refresh browser

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| `The solution file is invalid. The compressed file must contain solution.xml, customizations.xml, and [Content_Types].xml` | ZIP entry names are wrong casing (e.g., `Solution.xml` instead of `solution.xml`) | Ensure pack.ps1 uses **lowercase ZIP entry names**: `solution.xml`, `customizations.xml`. The disk file casing does not matter — only the ZIP entry name string matters. |
| `unexpected error` | 1) Missing `.js`/`.css` in `[Content_Types].xml`, 2) Wrong `customizations.xml` format, 3) Backslashes in ZIP | Check `[Content_Types].xml` has `.js`/`.css`, verify `customizations.xml` uses `<Name>` child element (not attribute), use `pack.ps1` |
| `Root component missing for custom control` | `solution.xml` has empty `<RootComponents />` | Add `<RootComponent type="66" schemaName="sprk_Spaarke.Controls.{ControlName}" behavior="0" />` |
| `component sprk_xyz.js of type 61 is not declared as root component` | Web resource added to `customizations.xml` but not in `<RootComponents>`, or GUID doesn't match Dataverse | **Remove web resources from PCF solution entirely.** PCF solutions should contain ONLY the PCF control (type 66). Deploy web resources separately via Dataverse UI. |
| `NU1008` CPM error | CPM not disabled | Disable `Directory.Packages.props` before PAC commands |
| Bundle is 8MB | Dev build or missing platform libraries | Use `npm run build:prod`, add platform libraries |
| `Source File does not exist` | Missing file in Controls folder | Copy ALL 3 files from `out/` |
| Version didn't update | Stale bundle copied | Rebuild fresh, copy fresh files |
| Old features still showing | Browser cache | Hard refresh `Ctrl+Shift+R` |

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

## Project Structure

```
src/client/pcf/{ControlName}/
├── control/                          # Source code
│   ├── ControlManifest.Input.xml
│   └── {Component}.tsx
├── Solution/                         # Solution package
│   ├── Controls/sprk_Spaarke.Controls.{ControlName}/
│   │   ├── bundle.js                 # ← Copy from out/
│   │   ├── ControlManifest.xml       # ← Copy from out/
│   │   └── styles.css                # ← Copy from out/
│   ├── solution.xml                  # Or Solution.xml (casing on disk doesn't matter)
│   ├── customizations.xml            # Or Customizations.xml (pack.ps1 handles casing)
│   ├── [Content_Types].xml
│   ├── pack.ps1
│   └── bin/                          # Output ZIPs
├── out/controls/control/             # Build output
├── featureconfig.json
└── webpack.config.js
```

---

## Related ADRs

- [ADR-006](.claude/adr/ADR-006-pcf-over-webresources.md) - PCF over legacy webresources
- [ADR-021](.claude/adr/ADR-021-fluent-design-system.md) - Fluent UI v9
- [ADR-022](.claude/adr/ADR-022-pcf-platform-libraries.md) - React 16 compatibility
