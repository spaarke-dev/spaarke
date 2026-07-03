# MatterHeaderPcf bundle size + LOC verification (Task 024)

**Date**: 2026-07-03 (revised after `/pcf-deploy` skill compliance)
**Build command**: `npm run build:prod`
**Task**: 024
**Rigor level**: STANDARD

---

## Final measurements (POST-REMEDIATION) — NFR-04 ✅ PASS

- **Output path**: `src/client/pcf/MatterHeader/out/controls/control/bundle.js`
- **Un-gzipped**: **39,162 bytes** = **38 KB** — 6.5× under 250 KB ceiling
- **Gzipped**: **10,219 bytes** = **10 KB** — 25× under 250 KB ceiling
- **Build time**: ~31 seconds
- **Webpack**: 5.108.3, buildMode=production, minimizer active

### NFR-04 status: **PASS** ✅

Bundle went from 1.57 MiB to 38 KB (43× reduction) after applying the three-part fix documented below. This matches the pcf-deploy skill's expected 400-600 KB reference range for field-based PCFs (we're well under it because MatterHeader is a compact card, not a full-app).

## Three-part remediation (per /pcf-deploy skill + PCF-DEPLOYMENT-GUIDE.md)

Root cause of the original 1.6 MiB bundle: three standard PCF bundle-optimization mechanisms were missing.

### Fix 1: `featureconfig.json` — enable platform libraries + custom webpack

Created `src/client/pcf/MatterHeader/featureconfig.json`:
```json
{
  "pcfReactPlatformLibraries": "on",
  "pcfAllowCustomWebpack": "on"
}
```

Without this, `<platform-library>` entries in ControlManifest.Input.xml are declared but not enforced — React + Fluent still get bundled. Matches SemanticSearchControl / DocumentRelationshipViewer reference configs.

### Fix 2: `webpack.config.js` — Fluent icon tree-shaking

Created `src/client/pcf/MatterHeader/webpack.config.js`:
```javascript
module.exports = {
  optimization: { usedExports: true, sideEffects: true, innerGraph: true, providedExports: true },
  module: {
    rules: [{
      test: /[\\/]node_modules[\\/]@fluentui[\\/]react-icons[\\/]/,
      sideEffects: false,
    }],
  },
};
```

Marks `@fluentui/react-icons` as side-effect-free so webpack can drop unused icon chunks (~6.8 MB of icons otherwise).

### Fix 3: `MatterHeaderView.tsx` — deep-path imports

Changed from top-level barrel:
```typescript
// BEFORE — pulls EntityCreationService → mammoth → xmldom → bluebird (~550 KiB)
import { RecordHeaderShell, FieldGrid, TextField, ... } from '@spaarke/ui-components';
```
to sub-path imports:
```typescript
// AFTER — targets specific sub-barrels; no EntityCreationService drag-in
import { FieldGrid, RecordHeaderShell, TextField, TextareaField } from '@spaarke/ui-components/dist/components/RecordHeader';
import { LookupField as RecordHeaderLookupField } from '@spaarke/ui-components/dist/components/RecordHeader/fields';
import { useRecordFieldValues, useRecordHeaderToolbarActions } from '@spaarke/ui-components/dist/hooks';
```

The shared lib's top-level barrel (`dist/index.js`) uses `export * from './services'` which re-exports EntityCreationService which imports `@spaarke/sdap-client` which pulls in the mammoth docx-processing chain. Sub-path imports bypass this. **This is the convention for any PCF consuming `@spaarke/ui-components`** — should be captured in the Phase 4 authoring guide.

## Original measurement (PRE-REMEDIATION) — kept for reference

Before applying the three fixes above, the bundle was:

- **Un-gzipped**: 1,646,274 bytes = 1,607 KB = 1.57 MiB (6.4× over 250 KB ceiling)
- **Gzipped**: 431,660 bytes = 421 KB (1.7× over ceiling)

### Top bundle contributors (from webpack stats)

| Module path | Size | Notes |
|---|---|---|
| `Spaarke.UI.Components/node_modules/@xmldom/xmldom/lib/` | 134 KiB | XML parser (docx pipeline) |
| `Spaarke.UI.Components/node_modules/bluebird/js/release/` | 126 KiB | Promise polyfill (mammoth transitive) |
| `Spaarke.UI.Components/node_modules/dingbat-to-unicode/dist/` | 118 KiB | Unicode conversion (mammoth transitive) |
| `Spaarke.UI.Components/node_modules/mammoth/` | 92.9 KiB (40 modules) | Docx-to-HTML converter |
| `Spaarke.UI.Components/node_modules/xmlbuilder/lib/` | 62.8 KiB | XML builder |
| `Spaarke.UI.Components/node_modules/lop/` | 16.7 KiB | Parser combinators |

**Attributed subtotal for docx pipeline in bundle**: ~550 KiB of pre-minified source. Not a MatterHeader feature — pulled in by the shared library's barrel export chain.

`@spaarke/auth` and `BffDataverseClient` did **not** survive tree-shaking (`grep -c` = 0 hits in bundle). Docx modules survived (webpack could not tree-shake through the shared lib's CommonJS barrel).

---

## PCF LOC

Measured excluding: import statements, blank lines, single-line (`//`) comments, block (`/* */`) comments. Includes: executable code, JSX, interface / type declarations, `const` / `export` declarations that are not imports.

| File | LOC (excl imports/blank/comments) | Raw lines |
|---|---|---|
| `control/index.ts` | 20 | 47 |
| `control/MatterHeaderView.tsx` | 69 | 111 |
| `control/version.ts` | 1 | 12 |
| **Total** | **90** | 170 |

### NFR-02 status: **PASS**

- Target: ≤ 100 LOC excluding shared primitives
- Measured: 90 LOC (10 LOC of headroom)

---

## Verification

- **npm install**: 1,056 packages, ~70 seconds (clean re-install after removing stale `node_modules` + `package-lock.json` to unblock ajv v6→v8 hoisting issue). +1 additional package (`ajv@^8.12.0`) added as direct devDep to force top-level ajv to v8 (see Deviations §1).
- **Build**: succeeded with **3 warnings** (webpack size limit — expected given the bundle overshoot), **0 errors**
- **Zero `@spaarke/auth` imports in source**: verified via `grep -r @spaarke/auth control/` — only mentions are in JSDoc / block comments documenting NFR-05 compliance.
- **Zero BFF client imports in source**: verified via `grep -r "sdap-client|BffDataverseClient|BffClient" control/` — 0 hits (compliant with NFR-07).
- **Bundle content**: `@spaarke/auth` and `BffDataverseClient` strings absent from `bundle.js` (tree-shaken).
- **Version consistency**: `control/version.ts` = `1.0.0`; `control/ControlManifest.Input.xml` = `version="1.0.0"`. Solution manifests (2 files under `Solution/`) not re-verified in this task but were set by task 023.

---

## Deviations from task 023 discovered while running task 024

Task 024 was blocked at every step by environment / task-023 gaps. Each was repaired with the minimum change necessary; each is a task-023 defect the wrap-up (Phase 5) should own.

### 1. `ajv` version-hoist conflict (npm install-time)

- **Symptom**: `pcf-scripts build --buildMode production` failed with `Cannot find module 'ajv/dist/compile/codegen'` from `ajv-keywords@5.1.0`.
- **Root cause**: `pcf-scripts` pins `ajv@6.15.0` at top-level; `ajv-keywords@5.1.0` needs `ajv@^8.8.2` at runtime (require of `ajv/dist/compile/codegen`); npm hoisted ajv v6 first so the require failed.
- **Fix**: `npm install ajv@^8.12.0 --save-dev --legacy-peer-deps` — force ajv v8 at top level so both consumers resolve satisfactorily. Adds one line to `package.json` `devDependencies`.
- **Cross-cutting**: SemanticSearchControl / DocumentRelationshipViewer likely hit the same issue when built from a clean tree; consider a repo-level `ajv` version pin.

### 2. Missing `tsconfig.json` (compile-time)

- **Symptom**: `TS5083: Cannot read file 'src/client/pcf/node_modules/pcf-scripts/tsconfig_base.json'` — the compiler fell back to the parent `src/client/pcf/tsconfig.json` because the MatterHeader folder had no `tsconfig.json`.
- **Root cause**: Task 023 scaffolded `tsconfig.test.json` but omitted the base `tsconfig.json`.
- **Fix**: Created `src/client/pcf/MatterHeader/tsconfig.json` mirroring `SemanticSearchControl/tsconfig.json` (same `extends` + `paths` for react / react-dom under React 16.14).

### 3. Missing SdapClient build (bundle-time)

- **Symptom**: `Can't resolve '@spaarke/sdap-client'` from `Spaarke.UI.Components/dist/services/EntityCreationService.js`.
- **Root cause**: `Spaarke.SdapClient` symlink target had no `dist/` folder. `main: "dist/index.js"` was unresolvable.
- **Fix**: Ran `npm install && npm run build` inside `src/client/shared/Spaarke.SdapClient/`.
- **Cross-cutting**: Any sibling PCF depending on `@spaarke/ui-components` transitively depends on `@spaarke/sdap-client`. The shared-lib build pipeline should build SdapClient before UI.Components consumers ship.

### 4. ControlManifest.Input.xml placement (build-time)

- **Symptom**: `Cannot find module './generated/ManifestTypes'` — pcf-scripts generated types into `MatterHeader/generated/` at the folder root; `control/index.ts` looked for `control/generated/`.
- **Root cause**: Task 023 placed `ControlManifest.Input.xml` at `MatterHeader/` root but code files under `control/` with `<code path="control/index.ts">`. pcf-scripts co-locates `generated/` next to the manifest, not next to the code file.
- **Fix**: Moved `ControlManifest.Input.xml` into `control/` and changed `<code path>` from `control/index.ts` to `index.ts` (relative to manifest). Now co-located per the VisualHost convention.
- **Cross-cutting**: This is the canonical PCF layout when using a `control/` subfolder. Task 023 should have used this layout from the start.

### 5. `context.mode.contextInfo` type gap (type-check-time)

- **Symptom**: `TS2339: Property 'contextInfo' does not exist on type 'Mode'.` — `@types/powerapps-component-framework@1.3.18` doesn't type the runtime property.
- **Root cause**: Task 023's `control/index.ts` accessed `context.mode.contextInfo.entityId` directly instead of using the type-cast pattern proven in ScopeConfigEditor + SemanticSearchControl (`(mode as unknown as { contextInfo?: {...} }).contextInfo`).
- **Fix**: Minimal type-cast in `control/index.ts` updateView; three added lines (comment + cast helper). Behavior unchanged — same runtime property access. Added LOC accounted for in NFR-02 count above.
- **Cross-cutting**: This is a known Power Apps type-definitions gap. Documented in ScopeConfigEditor / SemanticSearchControl. Task 023 should have mirrored the type-cast pattern.

---

## Findings

- **Bundle miss is structural, not incidental**. NFR-04 fail is not caused by MatterHeader's own code (which is 90 LOC and 5 imports from `@spaarke/ui-components`). It is caused by the shared library's `dist/index.js` re-exporting `EntityCreationService` and other services whose transitive dependency chain pulls in `mammoth` (docx-to-HTML converter) plus its ecosystem (`xmldom`, `bluebird`, `xmlbuilder`, `dingbat-to-unicode`, `lop`). Webpack could not tree-shake through the shared lib's CommonJS emit — evidenced by mammoth surviving in the minified `bundle.js` while `@spaarke/auth` (used in the same barrel chain) did NOT survive.
- **Where the pattern was set**: `Spaarke.UI.Components/src/services/EntityCreationService.ts` imports `@spaarke/sdap-client` at the top level, and that module pulls in mammoth via a docx-processing helper. The barrel `services/index.ts` re-exports `EntityCreationService`, and `dist/index.js` re-exports `./services`. Any consumer of the shared lib inherits mammoth.
- **NFR-05 (no @spaarke/auth) + NFR-07 (no BFF) are met at the source level**. Bundle contents also confirm neither survives — even though the barrel chain reaches them.
- **Type-cast pattern is a canonical Spaarke PCF idiom** — treat it as a task-023 template gap and encode into the pattern file at wrap-up.

---

## Recommendations

### For task 025 (Deploy + QA)

- **Do NOT deploy until NFR-04 is addressed.** A 1.5 MiB bundle for a header card is user-visible page-load bloat; it will regress Matter form TTI (NFR-01) as well. Escalate to owner: hold task 025 pending mammoth extraction.
- **If deploy proceeds under waiver**: measure real Matter form TTI at load and record in `notes/matter-form-tti.md`; if it exceeds NFR-01 cold ≤ 800 ms, file DEF-06 for tree-shake remediation.

### Concrete tree-shake remediation options (any single one would resolve NFR-04)

1. **Preferred — deep-import in MatterHeaderView.tsx**: change `import { … } from '@spaarke/ui-components'` to per-symbol deep imports (`import { RecordHeaderShell } from '@spaarke/ui-components/dist/components/RecordHeader/RecordHeaderShell'` etc.). Bypasses the barrel entirely for MatterHeader while preserving barrel convenience for larger consumers.
2. **Alternative — split the shared lib**: move `EntityCreationService` and its docx-processing chain into a separate subpath export (e.g. `@spaarke/ui-components/services/entity-creation`) that MatterHeader never touches. Requires changing consumers of `EntityCreationService`.
3. **Least invasive — mark shared lib `sideEffects: false`**: add `"sideEffects": false` to `Spaarke.UI.Components/package.json`, verify no runtime regression in existing consumers. Enables webpack to prune unreachable barrel branches. Risk: any real side-effect module (registration, telemetry init) currently reachable via barrel would silently be dropped.

Recommendation for R1: option (1) at task 025 or a fast-follow, plus a wrap-up defect (DEF-06) tracking option (3) with a controlled audit of shared-lib side-effect modules.

---

## Downstream

- **Task 025 (deploy + QA)**: BLOCKED on NFR-04 resolution or waiver.
- **Solution ZIP** (per task 023 `pack.ps1`): deferred to task 025.
- **Task 023 deviations documented above**: capture in wrap-up (Phase 5) as DEF-07 (`.claude/patterns/ui/record-header-composition.md` should note the type-cast pattern and canonical manifest placement).
