# Solution Packaging Checklist — LegalWorkspace PCF

> **Task**: 040 — Solution Packaging for Dataverse
> **Solution**: SpaarkeLegalWorkspace (unmanaged — ADR-022)
> **Control**: sprk_Spaarke.Controls.LegalWorkspace v1.0.1
> **Script**: `scripts/Package-LegalWorkspace.ps1`

---

## Quick Start

```powershell
# Full automated pipeline (recommended):
scripts\Package-LegalWorkspace.ps1 -Deploy

# Manual step-by-step: follow the checklist below
```

---

## Pre-Build Checklist

Before running the build, verify these are complete:

- [ ] All task dependencies satisfied (033 Bundle Optimization ✅, 034 Performance ✅, 037 E2E Tests ✅)
- [ ] `ControlManifest.Input.xml` — `version` attribute is set to `1.0.1`
- [ ] `index.ts` — `CONTROL_VERSION` constant is `"1.0.1"` (drives UI footer)
- [ ] `Solution/solution.xml` — `<Version>1.0.1</Version>` in SolutionManifest
- [ ] `Solution/Controls/sprk_Spaarke.Controls.LegalWorkspace/ControlManifest.xml` — `version="1.0.1"`
- [ ] `featureconfig.json` — `"pcfReactPlatformLibraries": "on"` (required for React externalization)
- [ ] `ControlManifest.Input.xml` — both `<platform-library>` entries present (React 18.2.0, Fluent 9)
- [ ] `package.json` — React and Fluent UI in `devDependencies` (not `dependencies`)
- [ ] No uncommitted breaking changes in workspace source files

**Version bump rule**: Always increment patch (1.0.x) for fixes/refinements, minor (1.x.0) for new
blocks/features. MUST update all 4 locations simultaneously.

---

## Build Verification

- [ ] `npm install` — all dependencies installed (no missing packages)
- [ ] `npm run build` exits with code 0 (no TypeScript compile errors)
- [ ] `out/controls/control/bundle.js` exists after build
- [ ] `out/controls/control/ControlManifest.xml` exists after build
- [ ] `out/controls/control/styles.css` exists after build (may be empty/small)

### Bundle Size Check (NFR-02)

- [ ] `bundle.js` is **under 5MB** — verify:
  ```powershell
  (Get-Item "src\client\pcf\LegalWorkspace\out\controls\control\bundle.js").Length / 1MB
  ```
  Expected: ~0.25–0.5 MB (platform libraries excluded via `platform-library` declarations)

- [ ] If bundle exceeds 5MB:
  - Verify `featureconfig.json` has `"pcfReactPlatformLibraries": "on"`
  - Verify `<platform-library>` entries are in `ControlManifest.Input.xml`
  - Verify React and Fluent UI are in `devDependencies` (not `dependencies`)
  - Review `bundle-optimization.md` for additional optimizations

---

## Solution XML Integrity Checks

Run these checks before packing to avoid silent import failures:

### `[Content_Types].xml`
- [ ] Contains `<Default Extension="xml" .../>` entry
- [ ] Contains `<Default Extension="js" .../>` entry  ← CRITICAL: missing causes silent failure
- [ ] Contains `<Default Extension="css" .../>` entry  ← CRITICAL: missing causes silent failure

### `customizations.xml`
- [ ] Uses child element format: `<CustomControl><Name>...</Name>` (NOT attribute format)
  ```xml
  <!-- CORRECT -->
  <CustomControl>
    <Name>sprk_Spaarke.Controls.LegalWorkspace</Name>
    ...
  </CustomControl>

  <!-- WRONG — causes "unexpected error" with no useful message -->
  <CustomControl Name="sprk_Spaarke.Controls.LegalWorkspace">
  ```
- [ ] Contains `<EntityDataProviders />` element
- [ ] Control `<Name>` matches `schemaName` in `solution.xml` `RootComponents`
- [ ] `<FileName>` path format: `/Controls/sprk_Spaarke.Controls.LegalWorkspace/ControlManifest.xml`

### `solution.xml`
- [ ] `<UniqueName>SpaarkeLegalWorkspace</UniqueName>`
- [ ] `<Version>1.0.1</Version>` matches all other 3 version locations
- [ ] `<Managed>0</Managed>` — MUST be 0 (unmanaged) per ADR-022
- [ ] Publisher `<UniqueName>Spaarke</UniqueName>` with prefix `sprk`
- [ ] Publisher `<CustomizationOptionValuePrefix>65949</CustomizationOptionValuePrefix>`
- [ ] `<RootComponent type="66" schemaName="sprk_Spaarke.Controls.LegalWorkspace" behavior="0" />`
  (type="66" = Custom Control / PCF component)

### `Solution/Controls/sprk_Spaarke.Controls.LegalWorkspace/`
- [ ] `bundle.js` — freshly copied from `out/controls/control/` (not stale)
- [ ] `ControlManifest.xml` — freshly copied from `out/controls/control/`
- [ ] `ControlManifest.xml` version attribute = `1.0.1`
- [ ] `styles.css` — present (can be empty for controls without custom CSS)

---

## Packing Checklist

- [ ] Using `System.IO.Compression` (via `pack.ps1` or `Package-LegalWorkspace.ps1`) — NOT `Compress-Archive`
- [ ] ZIP entry paths use **forward slashes** (verified by pack.ps1 using `ZipFileExtensions`)
- [ ] ZIP contains all 6 entries:
  - `solution.xml`
  - `customizations.xml`
  - `[Content_Types].xml`
  - `Controls/sprk_Spaarke.Controls.LegalWorkspace/bundle.js`
  - `Controls/sprk_Spaarke.Controls.LegalWorkspace/ControlManifest.xml`
  - `Controls/sprk_Spaarke.Controls.LegalWorkspace/styles.css`
- [ ] ZIP is in `Solution/bin/SpaarkeLegalWorkspace_v1.0.1.zip`
- [ ] Previous ZIP removed before repacking (pack.ps1 handles this automatically)

---

## Import Verification Steps

After running `pac solution import`:

### Immediate Checks
- [ ] Import exits with code 0 (no errors)
- [ ] No warnings about managed/unmanaged conflicts
- [ ] `pac solution list | Select-String SpaarkeLegalWorkspace` shows the solution
- [ ] Solution version shown = `1.0.1`

### Dataverse API Check
```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/customcontrols?$filter=name eq 'sprk_Spaarke.Controls.LegalWorkspace'
```
Expected: Response contains the control with `version="1.0.1"`

### Custom Page Republish (CRITICAL for updates)
- [ ] Open Custom Page in [make.powerapps.com](https://make.powerapps.com)
  → Solutions → SpaarkeLegalWorkspace → Pages → `sprk_LegalOperationsWorkspace`
  → Edit → File → Save → File → Publish
- [ ] Run `pac solution publish-all`

### Browser Verification
- [ ] Hard refresh (Ctrl+Shift+R) the MDA containing the workspace
- [ ] Navigate to the Legal Operations Workspace SubArea
- [ ] Check UI footer — must show `v1.0.1`
- [ ] Workspace renders without console errors
- [ ] All 7 blocks visible (or placeholder for blocks pending BFF wiring):
  - Block 1: Get Started (action cards + Quick Summary)
  - Block 2: Portfolio Health Strip
  - Block 3: Activity Feed / Updates Feed
  - Block 4: Smart To Do List
  - Block 5: My Portfolio Widget (placeholder column)
  - Block 6: Create Matter wizard (launched from Block 1)
  - Block 7: Notification Panel (launched from Page Header)

---

## Rollback Procedure

If the import causes issues:

1. Identify previous working ZIP in `Solution/bin/`
2. `pac solution import --path bin\SpaarkeLegalWorkspace_vX.Y.Z.zip --force-overwrite --publish-changes`
3. Open Custom Page → Edit → Save → Publish
4. Hard refresh browser

---

## Known Issues and Workarounds

| Issue | Cause | Workaround |
|-------|-------|------------|
| `"unexpected error"` on import | Missing `.js`/`.css` in `[Content_Types].xml` | Verify both entries exist |
| `"unexpected error"` on import | Attribute format in `customizations.xml` | Use child element `<Name>` format |
| `"unexpected error"` on import | Backslashes in ZIP entry paths | Use `pack.ps1`, not `Compress-Archive` |
| `NU1008` CPM conflict | Directory.Packages.props active during pac | Script auto-disables/re-enables CPM |
| Bundle is 8MB+ | Dev build without platform libraries | Verify `featureconfig.json` and `platform-library` entries |
| Old version still showing | Browser cache | Hard refresh Ctrl+Shift+R |
| Custom Page blank after deploy | Stale Custom Page not republished | Edit + Save + Publish in Power Apps Maker |
| `Source File does not exist` | Missing file in Controls folder | Copy ALL 3 files from `out/controls/control/` |

---

## Automation Script Reference

```powershell
# Full pipeline (recommended for production deploys):
scripts\Package-LegalWorkspace.ps1 -Deploy

# Build + pack only (verify ZIP before importing):
scripts\Package-LegalWorkspace.ps1

# Use existing build, just repack and deploy:
scripts\Package-LegalWorkspace.ps1 -SkipBuild -Deploy

# Just repack from artifacts already in Solution/Controls/:
scripts\Package-LegalWorkspace.ps1 -PackOnly

# Pack only with existing artifacts (no deploy):
cd src\client\pcf\LegalWorkspace\Solution
.\pack.ps1
```

---

*Created by Task 040 — Solution Packaging for Dataverse*
*See also: custom-page-definition.md, PCF-DEPLOYMENT-GUIDE.md, scripts/Package-LegalWorkspace.ps1*
