---
description: Build, pack, and deploy PCF controls to Dataverse via solution ZIP import
tags: [deploy, pcf, dataverse, power-platform, solution]
techStack: [pcf-framework, typescript, react, dataverse]
appliesTo: ["**/pcf/**", "deploy pcf", "build and deploy pcf", "pcf solution import"]
alwaysApply: false
---

# PCF Deploy

> **Category**: Operations (Tier 3)
> **Last Updated**: February 22, 2026
> **Primary Guide**: [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md)

---

## Purpose

Build, version-bump, pack, and deploy a PCF control to Dataverse via solution ZIP import. This skill is **PCF-specific** — for other Dataverse operations (web resources, plugins, solution export), see `dataverse-deploy`.

**When to Use**:
- "deploy pcf", "build and deploy pcf control"
- "pack pcf solution", "pcf solution import"
- Task tags include `pcf` + `deploy`
- After making changes to PCF source code that need to be deployed

**When NOT to Use**:
- Deploying web resources → Use Dataverse UI directly or `dataverse-deploy`
- Deploying plugins → Use `dataverse-deploy`
- Deploying Azure infrastructure → Use `azure-deploy`

---

## 🚨 Critical Rules (MEMORIZE THESE)

### PCF Solution = PCF Control ONLY

A PCF deployment solution contains **ONLY the PCF control**. Nothing else.

- ❌ **NEVER** add web resources (`.js`, `.html`) to a PCF solution
- ❌ **NEVER** add `<WebResources>` to `customizations.xml`
- ❌ **NEVER** add type `61` (WebResource) to `<RootComponents>`
- ❌ **NEVER** use placeholder GUIDs for web resource IDs
- ✅ **ONLY** type `66` (CustomControl) belongs in `<RootComponents>`

**Why**: Web resources require real Dataverse GUIDs. Placeholder GUIDs cause: `"component sprk_xyz.js of type 61 is not declared in the solution file as a root component"`.

### ZIP Entry Names MUST Be Lowercase

The XML files on disk can use any casing (`Solution.xml`, `Customizations.xml`), but **ZIP entry names MUST be lowercase**:

| ZIP Entry Name (REQUIRED) | Disk File Name (any casing OK) |
|---------------------------|-------------------------------|
| `solution.xml` | `Solution.xml` or `solution.xml` |
| `customizations.xml` | `Customizations.xml` or `customizations.xml` |
| `[Content_Types].xml` | `[Content_Types].xml` |

**The `pack.ps1` script handles this mapping.** The entry name string parameter in `CreateEntryFromFile` controls what appears in the ZIP.

### Version Bump ALL 5 Locations

Every deployment MUST increment the version in ALL 5 files:

| # | File | What to Update |
|---|------|----------------|
| 1 | `control/ControlManifest.Input.xml` | `version="X.Y.Z"` attribute + description `(vX.Y.Z)` |
| 2 | `control/components/{Component}.tsx` | UI version footer string |
| 3 | `Solution/solution.xml` (or `src/Other/Solution.xml`) | `<Version>X.Y.Z</Version>` |
| 4 | `Solution/Controls/.../ControlManifest.xml` (or `src/WebResources/.../ControlManifest.xml`) | `version="X.Y.Z"` + description |
| 5 | `Solution/pack.ps1` | `$version = "X.Y.Z"` |

**If you forget #1 (ControlManifest.Input.xml), Dataverse silently keeps the old control.**

### Other MUST/NEVER Rules

- ✅ **MUST** rebuild fresh every deployment (`npm run build:prod`)
- ✅ **MUST** copy ALL 3 files from `out/` to Solution: `bundle.js`, `ControlManifest.xml`, `styles.css`
- ✅ **MUST** use `pack.ps1` (not `Compress-Archive` — backslashes break import)
- ✅ **MUST** use unmanaged solution (ADR-022)
- ✅ **MUST** use publisher `Spaarke` with prefix `sprk_`
- ❌ **NEVER** use `pac pcf push` — creates temp solutions, rebuilds in dev mode
- ❌ **NEVER** reuse old solution ZIPs

---

## Deployment Steps

### Step 1: Version Bump (ALL 5 Locations)

Before building, update the version in all 5 files. The build propagates the version from `ControlManifest.Input.xml` into the build output.

### Step 2: Build Fresh

```bash
cd src/client/pcf/{ControlName}/control
rm -rf ../out/ ../bin/
npm run build:prod

# Verify size (~200-500KB, NOT 8MB)
ls -la ../out/controls/control/bundle.js
```

### Step 3: Copy Build Output to Solution

```bash
cp ../out/controls/control/bundle.js \
   ../out/controls/control/ControlManifest.xml \
   ../out/controls/control/styles.css \
   ../Solution/Controls/sprk_Spaarke.Controls.{ControlName}/
```

**Note**: Some projects use `src/WebResources/` instead of `Controls/` in the Solution folder. Copy to wherever the `pack.ps1` reads from.

### Step 4: Pack Solution ZIP

```bash
cd ../Solution
powershell -ExecutionPolicy Bypass -File pack.ps1
```

**Verify ZIP contents** (optional but recommended on first deploy):
```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead("bin/{SolutionName}_vX.Y.Z.zip")
$z.Entries | ForEach-Object { "$($_.FullName) - $($_.Length) bytes" }
$z.Dispose()
```

**Expected ZIP structure** (no `WebResources/` folder):
```
solution.xml                                           ← lowercase
customizations.xml                                     ← lowercase
[Content_Types].xml
Controls/sprk_Spaarke.Controls.{ControlName}/bundle.js
Controls/sprk_Spaarke.Controls.{ControlName}/ControlManifest.xml
Controls/sprk_Spaarke.Controls.{ControlName}/styles.css
Controls/sprk_Spaarke.Controls.{ControlName}/css/*.css  (if applicable)
```

### Step 5: Import to Dataverse

```bash
# Disable CPM if needed
mv /c/code_files/{worktree}/Directory.Packages.props{,.disabled}

pac solution import --path bin/{SolutionName}_vX.Y.Z.zip --publish-changes

# Restore CPM
mv /c/code_files/{worktree}/Directory.Packages.props{.disabled,}
```

### Step 6: Verify

```bash
pac solution list | grep -i "{SolutionName}"
```

Hard refresh browser (`Ctrl+Shift+R`) and verify version footer in the PCF control.

---

## Solution XML Templates

### solution.xml

```xml
<ImportExportXml version="9.2.25124.178" SolutionPackageVersion="9.2" languagecode="1033"
    generatedBy="CrmLive" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
    OrganizationVersion="9.2.25124.178" OrganizationSchemaType="Standard"
    CRMServerServiceabilityVersion="9.2.25124.00180">
  <SolutionManifest>
    <UniqueName>{SolutionName}</UniqueName>
    <Version>X.Y.Z</Version>
    <Managed>0</Managed>
    <Publisher>
      <UniqueName>Spaarke</UniqueName>
      <CustomizationPrefix>sprk</CustomizationPrefix>
      <CustomizationOptionValuePrefix>65949</CustomizationOptionValuePrefix>
      <!-- Full publisher block required - copy from working solution -->
    </Publisher>
    <RootComponents>
      <!-- ONLY type 66 (PCF control). NEVER add type 61 (WebResource). -->
      <RootComponent type="66" schemaName="sprk_Spaarke.Controls.{ControlName}" behavior="0" />
    </RootComponents>
    <MissingDependencies />
  </SolutionManifest>
</ImportExportXml>
```

### customizations.xml

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
    <!-- MUST use child element format, NOT attribute format -->
    <!-- ❌ WRONG: <CustomControl Name="..."> -->
    <!-- ✅ CORRECT: -->
    <CustomControl>
      <Name>sprk_Spaarke.Controls.{ControlName}</Name>
      <FileName>/Controls/sprk_Spaarke.Controls.{ControlName}/ControlManifest.xml</FileName>
    </CustomControl>
  </CustomControls>
  <!-- NO <WebResources> section in PCF solutions -->
  <EntityDataProviders />
  <Languages>
    <Language>1033</Language>
  </Languages>
</ImportExportXml>
```

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| `The solution file is invalid...must contain solution.xml, customizations.xml` | ZIP entry names are wrong casing | Ensure pack.ps1 maps to **lowercase** ZIP entry names |
| `Root component missing for custom control` | Empty `<RootComponents />` in solution.xml | Add `<RootComponent type="66" schemaName="..." behavior="0" />` |
| `component sprk_xyz.js of type 61 is not declared` | Web resource in customizations.xml but not in RootComponents, or GUID mismatch | **Remove all web resources from PCF solution.** PCF = PCF only. |
| `unexpected error` (no details) | Wrong customizations.xml format OR missing [Content_Types].xml entries | Check: 1) `<Name>` child elements (not attributes), 2) `.js`/`.css` in [Content_Types].xml |
| Bundle is 8MB | Dev build | Use `npm run build:prod`, add platform libraries |
| Version didn't update in browser | ControlManifest.Input.xml version not incremented | Update #1 location FIRST, rebuild, redeploy |
| PCF shows old behavior after import | Dataverse control cache | `pac solution delete --solution-name X` then reimport |

---

## Relationship to Other Skills

| Skill | Relationship |
|-------|-------------|
| **dataverse-deploy** | General Dataverse operations (plugins, web resources, solution export). This skill (`pcf-deploy`) is PCF-specific. |
| **task-execute** | May invoke `pcf-deploy` when task tags include `pcf` + `deploy` |
| **adr-aware** | ADR-006 (PCF over webresources), ADR-022 (React 16), ADR-021 (Fluent v9) |

---

## Related ADRs

| ADR | Relevance |
|-----|-----------|
| [ADR-006](../../adr/ADR-006-pcf-over-webresources.md) | PCF over legacy webresources |
| [ADR-021](../../adr/ADR-021-fluent-design-system.md) | Fluent UI v9 design system |
| [ADR-022](../../adr/ADR-022-pcf-platform-libraries.md) | React 16 compatibility, platform libraries |

---

*For Claude Code: This skill handles PCF control deployment ONLY. For web resources, plugins, and other Dataverse components, use `dataverse-deploy` or deploy via Dataverse UI.*
