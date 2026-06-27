---
description: Build, pack, and deploy PCF controls to Dataverse via solution ZIP import — uses npm run build:prod for production mode (AP-1 fix verified 2026-05-14)
tags: [deploy, pcf, dataverse, power-platform, solution]
techStack: [pcf-framework, typescript, react, dataverse]
appliesTo: ["**/pcf/**", "deploy pcf", "build and deploy pcf", "pcf solution import"]
alwaysApply: false
exemplar: src/client/pcf/SemanticSearchControl/
last-reviewed: 2026-05-17
---

# PCF Deploy

> **Category**: Operations (Tier 3)
> **Last Reviewed**: 2026-05-17
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2d — fixed 2 broken ADR file paths; added FAILURE-MODES.md#AP-1 cross-reference; `leave-alone-justified` on body length per dereferencing-reliability concern)
> **Exemplar rationale**: `src/client/pcf/SemanticSearchControl/` is the canonical PCF control — named throughout this skill's body. Live, verifiable reference.
> **AP-1 fix verified in skill** (2026-05-14): Wrong "NEVER use npm run build:prod" instruction was removed. The Bundle Size & Production Mode section now mandates the correct `pcf-scripts build --buildMode production`. See [FAILURE-MODES.md#AP-1](../../FAILURE-MODES.md#ap-1-skill-prescribes-x-but-x-is-wrong) for incident history.
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
- **Authoring/modifying the PCF's React component code** → Use `fluent-v9-component` FIRST. This skill is for build+pack+deploy mechanics, not component design.

**Related companion patterns (read BEFORE editing the manifest or component code)**:
- [`.claude/patterns/pcf/fluent-v9-modern-theming.md`](../../patterns/pcf/fluent-v9-modern-theming.md) — `<platform-library>` manifest declarations, the 4 theming approaches, when to use `virtual` vs bundled.
- [`.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md`](../../patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md) — required if the PCF ships to both Canvas + MDA.
- [`.claude/patterns/pcf/theme-management.md`](../../patterns/pcf/theme-management.md) — existing Spaarke dark-mode wiring.

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

### Async Init in ReactControl — Auth Must Live in the Component (CRITICAL — per [ADR-028](../../adr/ADR-028-spaarke-auth-architecture.md))

For `ComponentFramework.ReactControl` (virtual controls), `notifyOutputChanged()` does **NOT** reliably trigger `updateView()`. If the control has no two-way bound field, the framework may ignore the call entirely.

**Rule**: Any async initialization (auth, config fetching) that needs to trigger a re-render MUST use `useState` + `useEffect` inside the React component — NOT the PCF class `init()`. Use the v2 contract from `@spaarke/auth`.

```typescript
// ✅ CORRECT — Spaarke Auth v2 contract in React component useEffect
import { initAuth, useAuth, authenticatedFetch } from '@spaarke/auth';

const [isAuthInitialized, setIsAuthInitialized] = useState(false);
useEffect(() => {
  initAuth({ clientId, tenantId, bffBaseUrl, bffApiScope })
    .then(() => setIsAuthInitialized(true));
}, []);
// Inside components: const { getAccessToken } = useAuth();
// For BFF calls: await authenticatedFetch('/api/...', { method: 'POST', body: ... })

// ❌ WRONG — instantiating PublicClientApplication directly
// new PublicClientApplication({...})  // bypasses @spaarke/auth singleton (INV-7)

// ❌ WRONG — auth in PCF class, triggers notifyOutputChanged()
// this._authInitialized = true;
// this.notifyOutputChanged(); // ← updateView() is NOT called for read-only ReactControl

// ❌ WRONG — passing accessToken as a typed prop (function-based contract retired this)
// <MyComponent accessToken={token} />  // use authenticatedFetch instead
```

**Full pattern**: See `.claude/patterns/pcf/control-initialization.md` § "Async Initialization" + `.claude/patterns/auth/spaarke-sso-binding.md` (INV-1..INV-8) + `.claude/adr/ADR-028-spaarke-auth-architecture.md`
**Canonical implementations**: `SemanticSearchControl.tsx`, `DocumentRelationshipViewer.tsx`, `RelatedDocumentCount.tsx`
**Pre-v2 holdout (V3 cleanup target)**: `UniversalQuickCreate` still uses its own local `MsalAuthProvider.ts` — do NOT copy this pattern for new PCFs.

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
- ✅ **MUST** use `npm run build:prod` for production deploys — runs `pcf-scripts build --buildMode production` which enables tree-shaking + minification. Default `npm run build` runs **development mode** and produces a bundle 10–15× larger because tree-shaking is disabled. See [Bundle Size & Production Mode](#bundle-size--production-mode) below.
- ❌ **NEVER** use `pac pcf push` — creates temp solutions, rebuilds in dev mode
- ❌ **NEVER** reuse old solution ZIPs
- ❌ **NEVER** skip shared lib compilation when shared components were modified — stale `dist/` causes silent failures

### Bundle Size & Production Mode (CRITICAL — verified 2026-05-14)

`pcf-scripts build` defaults to **development mode**. Webpack `mode: 'development'` disables:
- Tree-shaking — every imported barrel pulls in everything reachable, including `@fluentui/react-icons` chunk files (~500 KB each).
- Minification — variable names, whitespace, dead code all preserved.

Result: a control that should be 400–600 KB ships as 6–10 MB.

**Always invoke `npm run build:prod`** for any build that will be packed + imported to Dataverse. The script must be:
```json
"build:prod": "pcf-scripts build --buildMode production"
```

**Common malformed variants seen in this repo (DO NOT USE)**:
- `"pcf-scripts build --production"` — silently ignored, falls through to dev mode
- `"pcf-scripts build -- --mode production"` — passes `--mode production` as a webpack arg AFTER `--`, but pcf-scripts already configures webpack's mode internally; ignored

**Expected bundle sizes after `build:prod`** (committed reference, May 2026):

| PCF | bundle.js | ZIP |
|---|---|---|
| SpeDocumentViewer | 440 KB | 111 KB |
| SemanticSearchControl | 539 KB | ~140 KB |
| RelatedDocumentCount | 433 KB | ~110 KB |

If a fresh build comes out >1 MB, the `build:prod` script is almost certainly misconfigured or `npm run build` was used. Verify with:
```bash
stat -c '%s bytes' out/controls/{ControlName}/bundle.js
```

The committed bundle.js sizes in `solution/Controls/.../bundle.js` are authoritative for "known good." If your fresh build dramatically exceeds them, fix the build invocation before packing.

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
npm run build:prod         # MUST be build:prod — see Bundle Size & Production Mode

# Verify size (~400-600 KB field-based after build:prod; if >1 MB, build:prod is misconfigured)
stat -c '%s bytes' out/controls/{ControlName}/bundle.js
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
| [ADR-006](../../../docs/adr/ADR-006-prefer-pcf-over-webresources.md) | PCF over legacy webresources |
| [ADR-021](../../../docs/adr/ADR-021-fluent-ui-design-system.md) | Fluent UI v9 design system |
| [ADR-022](../../../docs/adr/ADR-022-pcf-platform-libraries.md) | React 16 compatibility, platform libraries |

> **Path fix 2026-05-17**: ADR-006 and ADR-021 filenames updated to actual repo filenames (`ADR-006-prefer-pcf-over-webresources.md` not `ADR-006-pcf-over-webresources.md`; `ADR-021-fluent-ui-design-system.md` not `ADR-021-fluent-design-system.md`). All 3 ADR links verified to resolve.

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| PCF bundle size jumps 5-10× after rebuild (e.g., 440KB → 6.7MB) | `npm run build` (dev mode, no tree-shaking) used instead of `npm run build:prod` | **This is the AP-1 origin.** See [FAILURE-MODES.md#AP-1](../../FAILURE-MODES.md#ap-1-skill-prescribes-x-but-x-is-wrong). ALWAYS use `npm run build:prod` for production deploys. Verify bundle size post-build matches expected ranges (skill's Bundle Size & Production Mode section). |
| `npm run build:prod` script silently runs dev mode | Wrong flag in `package.json` (`--production` or `-- --mode production` instead of `--buildMode production`) | Verify each PCF's `package.json` `build:prod` script invokes `pcf-scripts build --buildMode production`. Phase 0 inventory caught 3 PCFs with wrong flags; fix at the package.json level. |
| Solution import succeeds but PCF doesn't appear in form | Schema-name convention drift: `sprk_Sprk.<ControlName>` vs `sprk_Spaarke.Controls.<ControlName>` | Verify the schema name in `ControlManifest.xml` matches what's expected in the Form designer. Both conventions exist in the wild — must match exactly. |
| Bundle deployed but React not initializing | `ControlManifest.Input.xml` `<platform-library>` for React 16 not declared correctly | Per ADR-022, PCF declares React 16 via `<platform-library name="React" version="16.x.x" />`. Without this, ReactDOM doesn't bootstrap. |
| Multiple PCFs in one solution but only one deploys | Solution XML missing entries for second PCF | Each PCF needs its own `<RootComponent>` in `solution.xml` + matching directory structure. Don't try to deploy 2 controls from 1 solution unless explicitly set up. |
| `pac solution import` fails with cryptic XML errors | XML templates in this skill body need updating OR solution.xml structure drifted from PAC CLI expectations | The XML templates in this skill body are kept inline (not extracted to references/) per Phase 2b Wave 2d dereferencing-reliability concern. If templates drift from current PAC CLI behavior, update them here in-place. |
| `pac solution import` fails with `The 'description-key' attribute is invalid - The value '...' is invalid according to its datatype 'noAposStringType' - The Pattern constraint failed.` | Apostrophe (`'`) in any `description-key` attribute value in `ControlManifest.Input.xml`. Dataverse PCF XSD validation rejects literal single quotes in description-key attributes — `noAposStringType` is a regex pattern that excludes them. The XML attribute itself can be single-quoted (parses fine), but the *value content* must not contain apostrophes. | **Remove all apostrophes from description-key values** in `ControlManifest.Input.xml`. Common cases: possessives (`entity's` → `host entity`), inline examples (`'sprk_todo'` → `sprk_todo`). Apostrophes in XML *comments* are fine (XSD skips comments). Discovered 2026-06-25 during RegardingResolver v1.2.0 deploy. |

---

*For Claude Code: This skill handles PCF control deployment ONLY. For web resources, plugins, and other Dataverse components, use `dataverse-deploy` or deploy via Dataverse UI.*
