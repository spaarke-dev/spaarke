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

### Async Init in ReactControl — Auth Must Live in the Component (CRITICAL)

For `ComponentFramework.ReactControl` (virtual controls), `notifyOutputChanged()` does **NOT** reliably trigger `updateView()`. If the control has no two-way bound field, the framework may ignore the call entirely.

**Rule**: Any async initialization (auth, config fetching) that needs to trigger a re-render MUST use `useState` + `useEffect` inside the React component — NOT the PCF class `init()`.

```typescript
// ✅ CORRECT — auth in React component useEffect
const [isAuthInitialized, setIsAuthInitialized] = useState(false);
useEffect(() => {
  initializeAuth(...).then(() => setIsAuthInitialized(true));
}, []);

// ❌ WRONG — auth in PCF class, triggers notifyOutputChanged()
// this._authInitialized = true;
// this.notifyOutputChanged(); // ← updateView() is NOT called for read-only ReactControl
```

**Full pattern**: See `.claude/patterns/pcf/control-initialization.md` § "Async Initialization"
**Canonical implementations**: `SemanticSearchControl.tsx`, `RelatedDocumentCount.tsx`

---

### Shared Library Dependency (CRITICAL)

**If the PCF imports from `@spaarke/ui-components/dist/...`, you MUST compile the shared library BEFORE the PCF build.** The PCF webpack bundles pre-compiled JS from the shared lib's `dist/` folder — NOT the `.tsx` source files. If `dist/` is stale, the PCF bundle will contain OLD code regardless of how many times you rebuild.

**Step 0 — Compile shared lib `dist/`:**

```bash
cd src/client/shared/Spaarke.UI.Components

# Full build (if it succeeds):
npm run build

# If full build fails due to pre-existing errors in unrelated files,
# compile ONLY the changed files with relaxed checks:
npx tsc \
  --target ES2020 --module ESNext --jsx react \
  --declaration --declarationMap --sourceMap \
  --outDir ./dist --rootDir ./src \
  --esModuleInterop --skipLibCheck \
  --moduleResolution node --resolveJsonModule \
  --isolatedModules --allowSyntheticDefaultImports \
  --strict false \
  src/components/YourChangedComponent/YourChangedComponent.tsx \
  src/components/AnotherChanged/AnotherChanged.tsx
```

**Verify dist/ is fresh** (timestamps must be NEWER than source edits):
```bash
stat -c '%y' dist/components/YourChangedComponent/YourChangedComponent.js
stat -c '%y' src/components/YourChangedComponent/YourChangedComponent.tsx
```

**If you skip this step, changes to shared components will NOT appear in the deployed PCF.**

### Other MUST/NEVER Rules

- ✅ **MUST** compile shared lib `dist/` before PCF build if shared components were modified
- ✅ **MUST** rebuild fresh every deployment (`npm run build`)
- ✅ **MUST** copy build output files from `out/` to Solution: `bundle.js`, `ControlManifest.xml`, and `styles.css` (if produced)
- ✅ **MUST** use `pack.ps1` (not `Compress-Archive` — backslashes break import)
- ✅ **MUST** use unmanaged solution (ADR-022)
- ✅ **MUST** use publisher `Spaarke` with prefix `sprk_`
- ❌ **NEVER** use `npm run build:prod` — pcf-scripts does not have a separate production build script; use `npm run build`
- ❌ **NEVER** use `pac pcf push` — creates temp solutions, rebuilds in dev mode
- ❌ **NEVER** reuse old solution ZIPs
- ❌ **NEVER** skip shared lib compilation when shared components were modified — stale `dist/` causes silent failures

---

## Path Map — SemanticSearchControl

All paths relative to the repository root. **Claude Code MUST use these exact paths.**

| Purpose | Path |
|---------|------|
| **PCF project root** | `src/client/pcf/SemanticSearchControl/` |
| **Control source dir** | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/` |
| **Source manifest** | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/ControlManifest.Input.xml` |
| **Main component** | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/SemanticSearchControl.tsx` |
| **Build output dir** | `src/client/pcf/SemanticSearchControl/out/controls/SemanticSearchControl/` |
| **Build bundle.js** | `src/client/pcf/SemanticSearchControl/out/controls/SemanticSearchControl/bundle.js` |
| **Build manifest** | `src/client/pcf/SemanticSearchControl/out/controls/SemanticSearchControl/ControlManifest.xml` |
| **Solution dir** | `src/client/pcf/SemanticSearchControl/Solution/` |
| **Solution controls** | `src/client/pcf/SemanticSearchControl/Solution/Controls/sprk_Sprk.SemanticSearchControl/` |
| **Pack script** | `src/client/pcf/SemanticSearchControl/Solution/pack.ps1` |
| **Solution ZIP** | `src/client/pcf/SemanticSearchControl/Solution/bin/SpaarkeSemanticSearch_v{X.Y.Z}.zip` |

### Common Path Mistakes (NEVER)

- ❌ Build from inner `SemanticSearchControl/SemanticSearchControl/` — build from the **outer** project root
- ❌ Look for output at `SemanticSearchControl/SemanticSearchControl/out/` — output is at the **outer** level
- ❌ Copy from `out/controls/bundle.js` — the **control name** is in the path: `out/controls/SemanticSearchControl/bundle.js`
- ❌ Copy to `Solution/Controls/bundle.js` — the **full schema name** is in the path: `Solution/Controls/sprk_Sprk.SemanticSearchControl/`

---

## Deployment Steps

### Step 1: Version Bump (ALL 5 Locations)

Before building, update the version in all 5 files. The build propagates the version from `ControlManifest.Input.xml` into the build output.

### Step 1.5: Compile Shared Library (if modified)

If ANY files in `src/client/shared/Spaarke.UI.Components/src/` were modified, compile `dist/` BEFORE the PCF build. See "Shared Library Dependency" section above for commands.

### Step 2: Build Fresh

```bash
cd src/client/pcf/{ControlName}
rm -rf out/ bin/
npm run build

# Verify size (~200-500KB field-based, 1-5MB custom page)
ls -la out/controls/{ControlName}/bundle.js
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

## Manual Quick Deploy (User-Performed)

When the user wants to deploy manually for fastest iteration:

1. **Build** (from repo root):
   ```bash
   cd src/client/pcf/SemanticSearchControl && npm run build
   ```
2. **Copy artifacts**:
   ```bash
   cp out/controls/SemanticSearchControl/bundle.js Solution/Controls/sprk_Sprk.SemanticSearchControl/
   cp out/controls/SemanticSearchControl/ControlManifest.xml Solution/Controls/sprk_Sprk.SemanticSearchControl/
   ```
3. **Pack**: `cd Solution && powershell -File pack.ps1`
4. **Import**: `pac solution import --path "Solution/bin/SpaarkeSemanticSearch_v{X.Y.Z}.zip" --publish-changes`
5. **Verify**: Hard refresh browser (`Ctrl+Shift+R`), check version footer

### When the User Says "I'll deploy manually"

- Ensure the build is complete and artifacts are copied to `Solution/Controls/`
- Tell the user: "Run `pack.ps1` in the Solution folder, then import the ZIP from `Solution/bin/`"
- Provide the exact ZIP filename with the current version number

---

## Troubleshooting

| Error | Cause | Fix |
|-------|-------|-----|
| `The solution file is invalid...must contain solution.xml, customizations.xml` | ZIP entry names are wrong casing | Ensure pack.ps1 maps to **lowercase** ZIP entry names |
| `Root component missing for custom control` | Empty `<RootComponents />` in solution.xml | Add `<RootComponent type="66" schemaName="..." behavior="0" />` |
| `component sprk_xyz.js of type 61 is not declared` | Web resource in customizations.xml but not in RootComponents, or GUID mismatch | **Remove all web resources from PCF solution.** PCF = PCF only. |
| `unexpected error` (no details) | Wrong customizations.xml format OR missing [Content_Types].xml entries | Check: 1) `<Name>` child elements (not attributes), 2) `.js`/`.css` in [Content_Types].xml |
| Bundle is 8MB | Missing platform libraries | Ensure `<platform-library>` entries in manifest, verify `featureconfig.json` has `"pcfReactPlatformLibraries": "on"` |
| Version didn't update in browser | ControlManifest.Input.xml version not incremented | Update #1 location FIRST, rebuild, redeploy |
| PCF shows old behavior after import | Dataverse control cache | `pac solution delete --solution-name X` then reimport |
| Shared component changes not appearing | Stale `dist/` in shared lib — `tsc` build failed, webpack bundled old compiled JS | **Compile shared lib `dist/` first** (see Step 1.5). Verify `dist/` timestamps are newer than source edits. |
| Multiple rebuilds with no visible change | Same as above — shared lib `dist/` not recompiled | Check `stat -c '%y'` on dist vs src files. If dist is older, shared lib was never recompiled. |

---

## Relationship to Other Skills

| Skill | Relationship |
|-------|-------------|
| **dataverse-deploy** | General Dataverse operations (plugins, web resources, solution export). This skill (`pcf-deploy`) is PCF-specific. |
| **task-execute** | May invoke `pcf-deploy` when task tags include `pcf` + `deploy` |
| **adr-aware** | ADR-006 (PCF over webresources), ADR-022 (React 16), ADR-021 (Fluent v9) |
| **code-page-deploy** | For Code Page web resources (HTML). PCF solutions do NOT include code pages. |
| **bff-deploy** | For BFF API deployment to Azure App Service |

---

## Related ADRs

| ADR | Relevance |
|-----|-----------|
| [ADR-006](../../adr/ADR-006-pcf-over-webresources.md) | PCF over legacy webresources |
| [ADR-021](../../adr/ADR-021-fluent-design-system.md) | Fluent UI v9 design system |
| [ADR-022](../../adr/ADR-022-pcf-platform-libraries.md) | React 16 compatibility, platform libraries |

---

*For Claude Code: This skill handles PCF control deployment ONLY. For web resources, plugins, and other Dataverse components, use `dataverse-deploy` or deploy via Dataverse UI.*
