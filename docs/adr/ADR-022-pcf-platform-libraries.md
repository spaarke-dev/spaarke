# ADR-022: PCF Platform Libraries — Field-Bound Controls Only

| Field | Value |
|-------|-------|
| Status | **Accepted** (Revised) |
| Date | 2025-12-30 |
| Updated | 2026-02-23 |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-022 Concise](../../.claude/adr/ADR-022-pcf-platform-libraries.md) - ~110 lines, decision + constraints
- [PCF Constraints](../../.claude/constraints/pcf.md) - MUST/MUST NOT rules for PCF development

**When to load this full ADR**: Understanding platform library versioning, debugging React errors, planning React upgrades

---

## Scope

**This ADR applies only to field-bound PCF controls** — controls that are embedded on Dataverse entity forms and use the `ComponentFramework` lifecycle (`init`, `updateView`, `destroy`).

**This ADR does NOT apply to:**
- **React Code Pages** (HTML web resources opened as standalone dialogs via `Xrm.Navigation.navigateTo`) — these bundle their own React 18. See [ADR-006](./ADR-006-prefer-pcf-over-webresources.md).
- **Office Add-ins** — bundled independently.

If you are building a standalone dialog (not embedded on a form), stop and use a React Code Page instead of a PCF in a custom page wrapper.

---

## Context

PowerApps Component Framework (PCF) controls run within the Dataverse model-driven app runtime. Microsoft provides common libraries (React, Fluent UI) as **platform libraries** that are injected at runtime, rather than requiring each control to bundle its own copy.

As of December 2025, Dataverse provides:
- **React 16.14.0** in manifest (NOT React 18)
- **Fluent UI v9** (9.46.x)

### Runtime Version Discovery

Testing revealed that the **actual runtime version differs from the manifest version**:

| Context | Manifest Version | Actual Runtime Version |
|---------|------------------|------------------------|
| Model-driven apps | 16.14.0 | **17.0.2** |
| Canvas apps | 16.14.0 | 16.14.0 |

This was discovered by logging `React.version` from a deployed PCF control. Model-driven apps load React 17.0.2 despite declaring 16.14.0 in the manifest.

**React 18 is NOT available** as a platform library. Microsoft has not announced a timeline for React 18 support. The "Dependent Libraries" feature (Preview) could theoretically enable custom library versions, but is not GA and is not recommended for production use.

### The Problem

Modern React tutorials and tooling default to React 18 patterns:
```typescript
// React 18 pattern - DOES NOT WORK in Dataverse
import { createRoot } from 'react-dom/client';
const root = createRoot(container);
root.render(<App />);
```

When a PCF control uses React 18 APIs but the platform injects React 16.14.0 at runtime, cryptic errors occur:
```
TypeError: Cannot create property '_updatedFibers' on number '0'
    at requestUpdateLane (react-dom.development.js:...)
```

This ADR establishes the constraint that all PCF controls MUST use React 16-compatible APIs.

---

## Decision

| Rule | Description |
|------|-------------|
| **Use React 16 APIs** | All PCF controls must use `ReactDOM.render()` and `ReactDOM.unmountComponentAtNode()`, NOT `createRoot` |
| **Declare platform libraries** | Manifest must include `<platform-library>` elements for React and Fluent |
| **Enable ReactDOM externalization** | Create `featureconfig.json` with `{ "pcfReactPlatformLibraries": "on" }` |
| **Dev dependencies only** | React packages in `devDependencies` for type-checking, not bundled in output |
| **Test in Dataverse** | Local harness may mask issues; always test deployed control |

---

## Consequences

**Positive:**
- Dramatically smaller bundle sizes (300KB vs 5MB+)
- Consistent React version across all controls in the app
- Automatic updates when Microsoft upgrades platform libraries
- Better performance due to shared runtime

**Negative:**
- Cannot use React 18 features (concurrent rendering, automatic batching, transitions)
- Must wait for Microsoft to upgrade platform React version
- Dev tooling may suggest React 18 patterns that won't work

---

## React Code Pages: React 18 (Different Rules)

React Code Pages are **not PCF controls**. They are HTML web resources bundled with React 18 and opened as standalone dialogs. They are entirely separate from this ADR.

```typescript
// Code Page index.tsx — React 18 ✅
import { createRoot } from "react-dom/client";
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

createRoot(document.getElementById("root")!).render(
  <FluentProvider theme={webLightTheme}>
    <App />
  </FluentProvider>
);
```

```json
// Code Page package.json — React 18 as dependency ✅
{
  "dependencies": {
    "react": "^18.3.0",
    "react-dom": "^18.3.0",
    "@fluentui/react-components": "^9.46.0"
  }
}
```

These patterns are **correct for Code Pages** — they would be errors in PCF controls. See [ADR-006](./ADR-006-prefer-pcf-over-webresources.md) for the full Code Page architecture.

---

## Alternatives Considered

### Bundle React 18 Anyway

**Rejected** (for PCF): Would result in 5MB+ bundles and potential conflicts with platform-provided React.

### Use Preact

**Rejected**: Would require different component syntax and lose Fluent UI compatibility.

### Wait for Microsoft to Upgrade

**Not an option**: No timeline from Microsoft; must support current platform.

---

## Operationalization

### featureconfig.json (CRITICAL)

The pcf-scripts build tool requires a `featureconfig.json` file to enable ReactDOM externalization. **Without this file, ReactDOM will be bundled even with `platform-library` declared in the manifest.**

Create `featureconfig.json` in the PCF control root (same directory as `package.json`):

```json
{
  "pcfReactPlatformLibraries": "on"
}
```

This tells pcf-scripts to treat ReactDOM as an external dependency that will be provided by the platform at runtime.

### Control Manifest Configuration

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls" constructor="MyControl" version="1.0.0"
           display-name-key="MyControl" description-key="MyControl_Desc">
    <!-- ... properties ... -->
    <resources>
      <code path="index.ts" order="1" />
      <!-- Platform-provided libraries - DO NOT bundle these -->
      <platform-library name="React" version="16.14.0" />
      <platform-library name="Fluent" version="9.46.2" />
    </resources>
  </control>
</manifest>
```

### Package.json Configuration

```json
{
  "name": "my-pcf-control",
  "version": "1.0.0",
  "dependencies": {
    // NO React packages here
  },
  "devDependencies": {
    "@types/react": "^16.14.0",
    "@types/react-dom": "^16.9.0",
    "react": "^16.14.0",
    "react-dom": "^16.14.0",
    "@fluentui/react-components": "^9.46.0"
  }
}
```

### PCF Index.ts Pattern

```typescript
/**
 * PCF Control using React 16 APIs
 * IMPORTANT: Dataverse provides React 16.14.0 at runtime
 */
import * as React from "react";
import * as ReactDOM from "react-dom";  // NOT 'react-dom/client'
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

export class MyControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private container: HTMLDivElement | null = null;
  private _context: ComponentFramework.Context<IInputs> | null = null;

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this._context = context;
    this.container = container;
    this.renderReactTree(context);
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this._context = context;
    this.renderReactTree(context);
  }

  public destroy(): void {
    if (this.container) {
      // React 16 unmount API
      ReactDOM.unmountComponentAtNode(this.container);
      this.container = null;
    }
    this._context = null;
  }

  public getOutputs(): IOutputs {
    return {};
  }

  private renderReactTree(context: ComponentFramework.Context<IInputs>): void {
    if (!this.container) return;

    // React 16 render API - NOT createRoot().render()
    ReactDOM.render(
      React.createElement(
        FluentProvider,
        { theme: webLightTheme },
        React.createElement(MyComponent, { context })
      ),
      this.container
    );
  }
}
```

---

## Common Errors and Solutions

| Error Message | Cause | Solution |
|---------------|-------|----------|
| `Cannot create property '_updatedFibers' on number '0'` | Using `createRoot` (React 18) with React 16 runtime | Change to `ReactDOM.render()` |
| `createRoot is not a function` | Importing from `react-dom/client` | Import from `react-dom` |
| `ReactDOM.render is not a function` | React 18 only in project, no React 16 | Add React 16 as devDependency |
| Bundle size > 5MB | React bundled in output | Add `platform-library` to manifest |
| Bundle still > 1MB after adding platform-library | Missing `featureconfig.json` | Create `featureconfig.json` with `pcfReactPlatformLibraries: on` |
| Control works locally but fails in Dataverse | Local harness uses bundled React | Test deployed control |

---

## Solution Deployment

**ALWAYS use unmanaged solutions for all deployments.** Managed solutions have caused issues in past projects.

| Solution Type | When to Use |
|---------------|-------------|
| **Unmanaged** | All development, testing, and production - ALWAYS |
| **Managed** | NEVER - unless user explicitly requests |

**Why unmanaged:**
- Allows components to be modified/removed freely
- No solution layering complexity
- Easier troubleshooting and rollback
- Consistent behavior across environments

See `dataverse-deploy` skill for detailed deployment procedures.

---

## Version Tracking

| Platform Library | Current Version | Notes |
|-----------------|-----------------|-------|
| React | 16.14.0 | Last LTS before React 17 |
| Fluent UI | 9.46.2 | Check release notes for updates |

Microsoft may update these versions. When they do:
1. Update this ADR with new versions
2. Test all existing PCF controls
3. Update `platform-library` versions in manifests

---

## Compliance

**Code review checklist:**
- [ ] Import from `react-dom`, NOT `react-dom/client`
- [ ] Using `ReactDOM.render()`, NOT `createRoot()`
- [ ] Using `ReactDOM.unmountComponentAtNode()`, NOT `root.unmount()`
- [ ] `platform-library` elements in manifest
- [ ] `featureconfig.json` with `pcfReactPlatformLibraries: on`
- [ ] React in `devDependencies`, not `dependencies`
- [ ] Bundle size < 1MB (indicates platform libraries used)
- [ ] Tested in deployed Dataverse environment

---

## AI-Directed Coding Guidance

**IMPORTANT**: Before applying the rules below, confirm you are building a **field-bound PCF control**, not a standalone dialog. If it's a standalone dialog, use a React Code Page instead (see ADR-006). PCF rules do NOT apply to Code Pages.

When creating or modifying **PCF controls** (field-bound only):
1. **Always** import from `react-dom`, never `react-dom/client`
2. **Always** use `ReactDOM.render(element, container)` pattern (or ReactControl `updateView` returning element)
3. **Always** use `ReactDOM.unmountComponentAtNode(container)` in `destroy()`
4. **Check** manifest has `platform-library` for React and Fluent
5. **Create** `featureconfig.json` with `{ "pcfReactPlatformLibraries": "on" }`
6. **Verify** bundle size after build — if > 1MB, likely bundling React
7. **Test** in actual Dataverse environment, not just local harness

When creating **React Code Pages** (standalone dialogs):
1. **Use** `createRoot()` from `react-dom/client` — React 18 is correct here
2. **Bundle** React 18 in `dependencies` (not devDependencies)
3. **Read parameters** via `new URLSearchParams(window.location.search)`
4. **Open** via `Xrm.Navigation.navigateTo({ pageType: "webresource", ... }, { target: 2 })`
5. **Do NOT** add `platform-library` to manifest — Code Pages are plain HTML bundles

---

## References

- [Microsoft PCF Platform Libraries Documentation](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/reference/feature-usage#platform-libraries)
- [React 16 Legacy Documentation](https://legacy.reactjs.org/)
- [Fluent UI v9 Documentation](https://react.fluentui.dev/)

---

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-30 | 1.0 | Initial ADR — React 16 compatibility issue discovered during Visual Host v1.1.0 deployment | Spaarke Engineering |
| 2026-02-23 | 1.1 | Revised for two-tier architecture (ADR-006 revision): added Scope section clarifying this ADR applies to field-bound PCF controls only — NOT React Code Pages. Added React Code Page section with React 18 patterns. Updated AI-Directed Coding Guidance to distinguish PCF vs Code Page rules. | Spaarke Engineering |
