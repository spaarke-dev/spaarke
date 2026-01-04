# PCF React 16 Remediation - Technical Specification

> **Version**: 1.0
> **Date**: 2025-12-30
> **Author**: AI-assisted with human review

---

## 1. Problem Description

### 1.1 The Issue

Spaarke PCF controls have inconsistent React version handling:

1. **Some controls bundle React 18** (~10 MB bundles)
   - Full React 18 features available
   - Each control loads its own copy of React
   - Severe performance impact on form load

2. **Some controls use platform library** (~500 KB bundles)
   - Microsoft provides React at runtime
   - Shared across all controls on the form
   - But some still use React 18 APIs, causing runtime errors

3. **Mixed configurations cause runtime errors**
   - `TypeError: Cannot create property '_updatedFibers' on number '0'`
   - `createRoot is not a function`
   - These occur when React 18 APIs are called against React 16 runtime

### 1.2 Root Cause Analysis

**Microsoft Platform Library Versions** (as of December 2025):

| Library | Manifest Version | Runtime Version (Model-Driven) | Runtime Version (Canvas) |
|---------|------------------|--------------------------------|--------------------------|
| React | 16.14.0 | **17.0.2** | 16.14.0 |
| Fluent | 9.46.2 | 9.x | 9.x |

**Key Finding**: Microsoft does NOT provide React 18 as a platform library. The highest available is React 17.0.2 (in model-driven apps).

### 1.3 Impact

| Scenario | JavaScript Loaded | Load Time Impact |
|----------|-------------------|------------------|
| 5 controls, all platform React | ~2.5 MB | Baseline |
| 5 controls, all bundle React 18 | ~50 MB | 20x slower |
| Mixed (current state) | ~25 MB + errors | Broken + slow |

---

## 2. Solution

### 2.1 Strategy

Standardize ALL PCF controls on:
1. **React 16/17 APIs** - Use `ReactDOM.render()`, not `createRoot()`
2. **Platform library** - Let Microsoft provide React at runtime
3. **Proper configuration** - `featureconfig.json` + manifest declarations

### 2.2 React 16 vs React 18 API Differences

| Operation | React 18 (DON'T USE) | React 16/17 (USE THIS) |
|-----------|---------------------|------------------------|
| Import | `import * as ReactDOM from 'react-dom/client'` | `import * as ReactDOM from 'react-dom'` |
| Create root | `const root = ReactDOM.createRoot(container)` | N/A |
| Render | `root.render(<App />)` | `ReactDOM.render(<App />, container)` |
| Unmount | `root.unmount()` | `ReactDOM.unmountComponentAtNode(container)` |

### 2.3 Required Files for Platform Library

Each PCF control needs these files configured:

**1. ControlManifest.Input.xml** - Add platform-library declarations:
```xml
<resources>
  <code path="index.ts" order="1" />
  <css path="css/styles.css" order="1" />
  <!-- Platform-provided: DO NOT bundle -->
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

**2. featureconfig.json** - Enable ReactDOM externalization:
```json
{
  "pcfReactPlatformLibraries": "on"
}
```

**3. package.json** - React in devDependencies only:
```json
{
  "dependencies": {
    // NO React here
  },
  "devDependencies": {
    "react": "^18.2.0",
    "react-dom": "^18.2.0",
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.0"
  }
}
```

**4. index.ts** - React 16 render pattern:
```typescript
import * as React from "react";
import * as ReactDOM from "react-dom";  // NOT 'react-dom/client'

export class MyControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement | null = null;

  public init(context, notifyOutputChanged, state, container: HTMLDivElement): void {
    this.container = container;
    this.renderReactTree(context);
  }

  public updateView(context): void {
    this.renderReactTree(context);
  }

  public destroy(): void {
    if (this.container) {
      ReactDOM.unmountComponentAtNode(this.container);
      this.container = null;
    }
  }

  private renderReactTree(context): void {
    if (!this.container) return;
    ReactDOM.render(
      React.createElement(FluentProvider, { theme },
        React.createElement(MyComponent, { context })
      ),
      this.container
    );
  }
}
```

---

## 3. Migration Checklist

For each PCF control, complete:

### 3.1 Manifest Updates
- [ ] Add `<platform-library name="React" version="16.14.0" />`
- [ ] Add `<platform-library name="Fluent" version="9.46.2" />`

### 3.2 Configuration Files
- [ ] Create/update `featureconfig.json` with `pcfReactPlatformLibraries: on`
- [ ] Move react/react-dom from dependencies to devDependencies

### 3.3 Code Changes
- [ ] Change import from `react-dom/client` to `react-dom`
- [ ] Replace `createRoot()` with container reference
- [ ] Replace `root.render()` with `ReactDOM.render(element, container)`
- [ ] Replace `root.unmount()` with `ReactDOM.unmountComponentAtNode(container)`

### 3.4 Verification
- [ ] Run `npm run build:prod`
- [ ] Verify bundle size < 1 MB
- [ ] Deploy to Dataverse
- [ ] Test in model-driven app form
- [ ] Verify no console errors

---

## 4. Common Errors and Fixes

| Error | Cause | Fix |
|-------|-------|-----|
| `Cannot create property '_updatedFibers' on number '0'` | React 18 `createRoot` called with React 16 runtime | Use `ReactDOM.render()` |
| `createRoot is not a function` | Importing from `react-dom/client` | Import from `react-dom` |
| Bundle > 5MB | React not externalized | Add `featureconfig.json` |
| Bundle > 1MB with platform-library | ReactDOM not externalized | Add `pcfReactPlatformLibraries: on` |

---

## 5. Shared Component Library

The `@spaarke/ui-components` library must also use React 16-compatible APIs.

### 5.1 Audit Required

Check for React 18-specific usage:
- `useId` hook
- `useSyncExternalStore` hook
- `useTransition` hook
- `useDeferredValue` hook
- `startTransition` function

### 5.2 Testing Strategy

- Test with React 16.14.0 in Jest configuration
- Verify Storybook still works (can use React 18 for dev)
- Ensure all components render in Dataverse

---

## 6. References

- [ADR-022: PCF Platform Libraries](../../.claude/adr/ADR-022-pcf-platform-libraries.md)
- [Microsoft Docs: React controls & platform libraries](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/react-controls-platform-libraries)
- [dataverse-deploy skill](../../.claude/skills/dataverse-deploy/SKILL.md)

---

## 7. Success Metrics

| Metric | Target | Current |
|--------|--------|---------|
| Max bundle size | < 1 MB | 10+ MB (some controls) |
| Form load time (5 controls) | < 3s | 10+ s (with bundled React) |
| Console errors | 0 | Multiple (mixed configs) |
| Controls on platform library | 100% | ~50% |
