# ADR-022: PCF Platform Libraries (Field-Bound Controls Only) (Concise)

> **Status**: Accepted (Revised 2026-05-13)
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2026-05-13
> **Last Verified Against**: [Microsoft Learn — React controls & platform libraries](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/react-controls-platform-libraries) (Microsoft doc updated 2025-10-10)

---

## Decision

**Scope: This ADR applies only to field-bound PCF controls** (form-embedded controls that use Dataverse bound properties and the `ComponentFramework` lifecycle). It does NOT apply to React Code Pages, which bundle their own React 18.

Use **Dataverse platform-provided libraries** (React 16/17, Fluent UI v9) for PCF controls instead of bundling these dependencies. This ensures compatibility, reduces bundle size by ~20x, and aligns with Microsoft's recommended practices.

**Rationale**: Dataverse provides React as a platform library at runtime. Using React 18 APIs (e.g., `createRoot`) causes runtime errors because the platform injects React 16/17 at runtime, not React 18.

---

## Runtime Versions (PCF Controls)

| Library | Manifest declared | Actual Runtime Version |
|---------|------------------|------------------------|
| React (Model-driven apps) | `16.14.0` | **17.0.2** |
| React (Canvas apps) | `16.14.0` | 16.14.0 |
| `@fluentui/react-components` (v9) | `9.46.2` (top of allowed range `>=9.4.0 <=9.46.2`) | **9.68.0** |
| `@fluentui/react` (v8) | `8.29.0` or `8.121.1` | matches declaration |

**React 18 is NOT available as a PCF platform library** as of May 2026. The Microsoft GA note confirms future React-version upgrades are coming — at that point our manifests will need updating, but `createRoot` migration is opt-in (no automatic break). For React 18 today, use a React Code Page instead (see ADR-006).

**Fluent v9 runtime is newer than declared.** The platform loads `9.68.0` even though manifests pin to `9.46.2`. This is intentional per Microsoft's docs ("the application might load a higher compatible version"). Code that uses APIs newer than `9.46.2` will work at runtime but won't typecheck against our local Fluent install — keep local devDependencies aligned with the declared manifest version, not the runtime version.

---

## PCF Controls — Constraints

### MUST

- **MUST** use React 16 APIs in PCF controls (`ReactDOM.render`, not `createRoot`)
- **MUST** declare platform libraries in `ControlManifest.Input.xml`
- **MUST** create `featureconfig.json` to enable ReactDOM externalization
- **MUST** use `devDependencies` for React types (not `dependencies`)
- **MUST** test PCF controls in Dataverse environment (not just local harness)

### MUST NOT

- **MUST NOT** use React 18+ APIs in PCF controls (`createRoot`, `hydrateRoot`, concurrent features)
- **MUST NOT** bundle React/ReactDOM into PCF output (bundle should be <5MB)
- **MUST NOT** import from `react-dom/client` (React 18 entry point) in PCF controls

---

## React Code Pages — React 18 (Different Rules)

React Code Pages bundle their own React 18 and are NOT subject to this ADR.

```typescript
// Code Page entry point — React 18 (bundled, not platform-provided)
import { createRoot } from "react-dom/client";   // ✅ React 18 OK here
createRoot(document.getElementById("root")!).render(<App />);
```

Code Page `package.json`:
```json
{
  "dependencies": {
    "react": "^18.3.0",          // bundled in output
    "react-dom": "^18.3.0",      // bundled in output
    "@fluentui/react-components": "^9.46.0"
  }
}
```

See ADR-006 for complete Code Page architecture.

---

## PCF Implementation Patterns

### React 16 Render Pattern (ReactControl)

```typescript
// index.ts — PCF ReactControl entry point
import * as React from "react";

export class MyControl implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
        // ReactControl returns element directly — framework handles rendering
        return React.createElement(FluentProvider, { theme: this._theme },
            React.createElement(MyComponent, { context })
        );
    }
}
```

### Manifest Configuration

```xml
<control namespace="Spaarke.Controls" constructor="MyControl" version="1.0.0">
  <resources>
    <code path="index.ts" order="1" />
    <!-- Platform-provided: DO NOT bundle -->
    <platform-library name="React" version="16.14.0" />
    <platform-library name="Fluent" version="9.46.2" />
  </resources>
</control>
```

### featureconfig.json

```json
{ "pcfReactPlatformLibraries": "on" }
```

### PCF package.json

```json
{
  "dependencies": {
    "@spaarke/ui-components": "workspace:*"
  },
  "devDependencies": {
    "@types/react": "^16.14.0",
    "@types/react-dom": "^16.9.0",
    "react": "^16.14.0",
    "react-dom": "^16.14.0"
  }
}
```

---

## Common PCF Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `Cannot create property '_updatedFibers' on number '0'` | Using React 18 `createRoot` with React 16 runtime | Use `ReactDOM.render()` or ReactControl pattern |
| `createRoot is not a function` | Importing from `react-dom/client` | Import from `react-dom` |
| Bundle > 5MB | React bundled in output | Add `platform-library` to manifest |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | Defines when to use PCF vs Code Page |
| [ADR-021](ADR-021-fluent-design-system.md) | Fluent v9 for all surfaces; React version differs by surface |
| [ADR-012](ADR-012-shared-components.md) | Shared components must work with both React 16 and 18 |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-022-pcf-platform-libraries.md](../../docs/adr/ADR-022-pcf-platform-libraries.md)

---

**Lines**: ~110
