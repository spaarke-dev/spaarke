# PCF Build Scaffold — Virtual Control Gotchas

> **Category**: PCF / Build
> **Load when**: Scaffolding a NEW virtual PCF (`control-type="virtual"`), OR debugging an existing one that hits "build fails" / "renders transparent popover" / "silent navigateTo" symptoms.
> **Owner**: `record-header-and-notepad-r1` project surfaced 5 build-blocking + 5 runtime gotchas across 11 versions of live QA (2026-07-02 through 2026-07-04). This file captures them in order of severity so future PCF authors don't re-hit the same wall.

---

## The 5 build-blocking gotchas

### 1. `Sparkle20Regular`-style direct imports from `@fluentui/react-icons` in a virtual PCF

**Symptom**: Webpack build fails with:
```
Can't resolve 'react/jsx-runtime' in '.../@griffel/react/src'
```

**Root cause**: `@griffel/react`'s `package.json` `"module"` field points at `./src/index.js` (source with JSX + `jsx-runtime` imports). Webpack prefers `module` over `main` for tree-shaking, picks up the source, then fails to resolve `react/jsx-runtime` because React 16 (platform-library) doesn't ship it. `@fluentui/react-icons/lib/utils/useIconState.js` pulls in griffel; every icon transitively depends on it.

**Fix**: Never direct-import icons in the PCF layer. Route icons through the shared library's compiled `dist/`:

```tsx
// ❌ WRONG — direct import in PCF .tsx (breaks build)
import { Sparkle20Regular } from '@fluentui/react-icons';

// ✅ RIGHT — icon is used INSIDE the shared component; PCF passes a callback
// Shared lib (`HeaderToolbar.tsx`) — this direct import is OK because webpack
// resolves it against the shared lib's own node_modules at compile time,
// and the compiled `dist/HeaderToolbar.js` inlines the icon reference.
import { Sparkle20Regular } from '@fluentui/react-icons';
```

The direct import IS safe inside the shared library because webpack processes the shared lib against ITS OWN node_modules once (at shared-lib compile time), then the PCF consumes the pre-compiled `dist/*.js`. The PCF-layer webpack never sees the raw jsx-runtime problem.

**When VisualHost's direct icon imports "just work"**: `Sparkle20Regular` is an existing icon; webpack tree-shakes safely. `Link24Regular` doesn't exist in `@fluentui/react-icons@2.0.331`; the missing-export path is what triggers griffel/src resolution. Rule: if you must direct-import, verify the icon exists via `grep -l "Link24Regular" node_modules/@fluentui/react-icons/lib/` before compile.

---

### 2. Manifest layout: `MatterHeader/control/index.ts` vs sibling pattern

**Symptom**: `pcf-scripts build` errors "cannot find manifest"; ControlManifest.xml not emitted; task 023 pack.ps1 can't find the built manifest to copy.

**Root cause**: Two competing layouts exist in the repo:
- `SemanticSearchControl`, `DocumentRelationshipViewer` — manifest + `index.ts` are SIBLINGS at `PCFName/PCFName/`
- Documentation examples and some templates — manifest at PCF root, class at `PCFName/control/index.ts`

**Fix**: Pick one, document it in `plan.md`, keep the manifest's `<code path="…" order="1"/>` element consistent with the class location. This project used `MatterHeader/control/index.ts` throughout — see `src/client/pcf/MatterHeader/control/ControlManifest.Input.xml` for the `<code path="index.ts" order="1"/>` element (path relative to the manifest file).

Whichever pattern you pick, the manifest's `path` attribute is what pcf-scripts follows.

---

### 3. `context.mode.contextInfo` isn't in `@types/powerapps-component-framework`

**Symptom**: TypeScript build error:
```
Property 'contextInfo' does not exist on type 'Mode'.
```

**Root cause**: The runtime `context.mode.contextInfo` object (with `.entityId`, `.entityTypeName`, `.entityRecord`) is real but missing from the shipped types. Any PCF that needs the current record's GUID hits this.

**Fix**: Type-cast at the read site:

```typescript
// src/client/pcf/MatterHeader/control/index.ts
public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
  const contextInfo = (context.mode as unknown as { contextInfo?: { entityId?: string } }).contextInfo;
  const recordId = contextInfo?.entityId || '';
  return React.createElement(MatterHeaderHost, { recordId, ... });
}
```

Mirror the pattern from `ScopeConfigEditor` and `SearchIndexResolver`. Don't fight the types.

---

### 4. `tsconfig.json` must be copied from a working PCF reference

**Symptom**: `pcf-scripts build` errors on module resolution ("cannot find module '@fluentui/react-components'"); ambient types not picked up.

**Root cause**: `pcf-scripts` scaffolding doesn't produce a working `tsconfig.json` out of the box for virtual PCFs on React 16.14 with the platform-library boundary. The right shape includes `"jsx": "react"`, `"moduleResolution": "node"`, `"types": ["node", "pcf-scripts", "powerapps-component-framework"]`, and specific `paths` for `@spaarke/*` workspace deps.

**Fix**: Copy `tsconfig.json` from `SemanticSearchControl` (verified working reference) and only change the `include` paths + the `paths` map. Do NOT hand-author.

---

### 5. `ajv` v8 must be a devDep on the PCF (webpack build needs it)

**Symptom**: `pcf-scripts build` errors at bundle emit stage:
```
Cannot find module 'ajv/dist/compile/codegen'
```

**Root cause**: Webpack 5 → `ajv-keywords` → wants `ajv` v8 in the resolution graph. If it's not in the PCF's own `node_modules` (or hoisted to a workspace root), the build fails after webpack starts emitting.

**Fix**: Add `"ajv": "^8.12.0"` to the PCF's `devDependencies`. This is a workaround for a webpack-5 packaging quirk; keep the version in sync with the reference PCFs.

---

## The 5 runtime gotchas (post-build, PCF-loads-but-misbehaves)

### 6. Portal-rendered popovers render transparent (no background, no shadow, no rounded corners)

**Symptom**: `<Popover>` / `<PopoverSurface>` / `<Tooltip>` / `<Menu>` / `<Dialog>` renders with the correct STRUCTURE but the "chrome" (background, border, shadow, border-radius) is missing. Content appears floating on the parent page's background.

**Root cause**: Fluent v9 portal-rendered surfaces mount to `document.body` — OUTSIDE the PCF's own DOM subtree. Platform-library Fluent theming is applied to the PCF's root element, so its CSS variables (`--colorNeutralBackground1`, `--shadow16`, `--borderRadiusMedium`) don't cascade to portal children.

**Fix**: Wrap the PCF's view in your OWN `<FluentProvider>`. Fluent v9's `applyStylesToPortals` (default `true`) explicitly injects theme variables into portal subtrees.

```typescript
// src/client/pcf/MatterHeader/control/MatterHeaderHost.tsx
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

export const MatterHeaderHost: React.FC<Props> = props => (
  <FluentProvider theme={webLightTheme} style={{ width: '100%' }}>
    <MatterHeaderView {...props} />
  </FluentProvider>
);
```

VisualHost's `VisualHostHost.tsx` is the canonical reference. For dark-mode support, port `VisualHost/control/providers/ThemeProvider.ts` (152 LOC — reads `context.userSettings.colorScheme` + listens on `localStorage` `spaarke-theme-change` events).

**Do NOT rely on**: platform-library auto-theming to reach portals. It doesn't.

---

### 7. `Xrm.Navigation.navigateTo` silent no-op — `this`-binding stripped

**Symptom**: Console log shows the click handler fires; navigate call returns without error; NO modal opens. `.catch()` never fires.

**Root cause**: `Xrm.Navigation.navigateTo` is a method that internally references `this` to reach its Navigation state. Aliasing it (e.g., `const navigate = xrm.Navigation.navigateTo; navigate(...)`) strips the `this` binding and produces a silently resolved promise with no side effect.

**Fix**: Call directly on `xrm.Navigation` OR use `.call()`:

```typescript
// ❌ WRONG — strips `this`, silent no-op
const navigate = xrm.Navigation.navigateTo;
navigate(pageInput, options);

// ✅ RIGHT — direct call
xrm.Navigation.navigateTo(pageInput, options);

// ✅ ALSO RIGHT — explicit `this` via .call()
(xrm.Navigation.navigateTo as XrmNavigateToTwoArg).call(
  xrm.Navigation, pageInput, options
);
```

Every working `navigateTo` in this repo (LegalWorkspace `WorkspaceGrid`, SpaarkeAi `launch-resolver`, VisualHost `VisualHostRoot`) uses the direct-call form.

---

### 8. `Xrm.Navigation.navigateTo` `pageInput` shape for `pageType: 'webresource'`

**Symptom**: Modal fails to open; no console error.

**Root cause**: The pageInput for webresources uses `webresourceName` (NOT `name`) and `data` MUST be a URL query string (NOT an object).

**Fix**:
```typescript
// ✅ RIGHT
xrm.Navigation.navigateTo({
  pageType: 'webresource',
  webresourceName: 'sprk_notepad',                                  // NOT `name`
  data: `regardingEntity=${encodeURIComponent(entity)}&regardingId=${encodeURIComponent(recordId)}`,  // string, NOT object
}, { target: 2, position: 1, width: {value:70,unit:'%'}, height: {value:80,unit:'%'} });
```

The webresource itself reads the string via `URLSearchParams(window.location.search).get('data')` and then unwraps it as inner params.

---

### 9. Verify webresource NAME via Dataverse MCP before hard-coding

**Symptom**: Same silent-no-op as #7/#8 — but this time the shape is correct.

**Root cause**: The webresource with the assumed name doesn't exist. Task 001 of this project verified `sprk_smarttodo` (not `sprk_smarttodo_page` which the design assumed). Task 065 (Phase 6, in-flight) needs `sprk_todospage` but should verify it exists post-deploy.

**Fix**: Before hard-coding a webresource name in `toolbarLaunchDefaults.ts` (or equivalent), query Dataverse:

```sql
SELECT webresourceid, name, displayname, webresourcetype
FROM webresource
WHERE name LIKE 'sprk_notepad%' OR name LIKE 'sprk_smart%'
```

Fold the discovery into a Phase 1 or wrap-up verification task with an explicit ⛔-block on downstream navigation tasks.

---

### 10. Form-buffer save (`Xrm.Page.getAttribute().setValue()`) vs direct `Xrm.WebApi.updateRecord`

**Symptom** (if using `updateRecord`): Every field save triggers a full retrieveRecord → shell shows the loading skeleton → PCF flashes. Users describe it as "the whole PCF refreshes on every edit."

**Root cause**: `Xrm.WebApi.updateRecord` writes straight to Dataverse. If the PCF calls `refresh()` after (to pull the new value back), the loading toggle flashes the shell.

**Fix**: Use the form buffer instead. Field goes "dirty"; user commits via the form's Save button — matches OOB Dataverse UX:

```typescript
function getXrmPage(): any {
  return (window as any).Xrm?.Page || (window.parent as any)?.Xrm?.Page || null;
}

async function saveField(fieldName: string, newValue: string): Promise<void> {
  const attr = getXrmPage()?.getAttribute(fieldName);
  if (!attr) throw new Error(`Field '${fieldName}' not on form`);
  attr.setValue(newValue);                    // stages in form buffer
  // Track locally so the controlled TextField reflects the pending value:
  setPendingText(prev => ({ ...prev, [fieldName]: newValue }));
}
```

For lookups the setValue payload is `[{ id, name, entityType }]`. `null` clears. Reference: `AssociationResolver/handlers/FieldMappingHandler.ts` line 440.

---

## Bundle-size triad (NFR-compliant)

For any virtual PCF that consumes `@spaarke/ui-components`:

1. **`featureconfig.json`** at PCF root:
```json
{ "pcfReactPlatformLibraries": "on", "pcfAllowCustomWebpack": "on" }
```

2. **`webpack.config.js`** at PCF root — tree-shake `@fluentui/react-icons`:
```js
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

3. **Deep-path imports** in `MatterHeaderView.tsx`-equivalents — bypass the top-level `@spaarke/ui-components` barrel which drags `EntityCreationService` → `mammoth` (~550 KiB of docx deps):
```typescript
// ✅ RIGHT
import { FieldGrid, TextField } from '@spaarke/ui-components/dist/components/RecordHeader';
import { LookupField } from '@spaarke/ui-components/dist/components/LookupField/LookupField';

// ❌ WRONG — drags mammoth transitively
import { FieldGrid, TextField, LookupField } from '@spaarke/ui-components';
```

R1's MatterHeader dropped from **1.57 MiB → 38 KB** (43× reduction) via this triad. See `projects/record-header-and-notepad-r1/notes/bundle-size.md` for the full accounting.

**DEF-06 (in-flight Phase 6)** will replace deep-path imports with clean subpath imports once `@spaarke/ui-components/package.json` gets an `exports` map. Until then, deep-path is required.

---

## Applies to (verified references)

| PCF | Version at reference | Notes |
|---|---|---|
| `SemanticSearchControl` | 3.x | tsconfig.json reference, siblings layout, virtual + platform-library |
| `DocumentRelationshipViewer` | 2.x | pack.ps1 System.IO.Compression reference, siblings layout |
| `VisualHost` | 1.4.x | FluentProvider wrap for portal-vars (§6), ThemeProvider.ts (152 LOC) for dark mode |
| `MatterHeader` | 1.0.11 | ALL 10 gotchas (surfaced this pattern doc) |

---

## Related

- [`.claude/patterns/pcf/fluent-v9-modern-theming.md`](fluent-v9-modern-theming.md) — theme sources (approach 1 = platform-library auto; approach 4 = FluentProvider wrap for portal-vars, needed here)
- [`.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md`](fluent-v9-canvas-vs-mda-disabled.md) — Canvas host caveats
- [`.claude/patterns/ui/record-header-composition.md`](../ui/record-header-composition.md) — shared-lib primitives for record headers
- [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md) — deployment steps
- [`docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`](../../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md) — R1 authoring guide (assumes this pattern's build steps)
