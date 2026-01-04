# ADR-022: PCF Platform Libraries (Concise)

> **Status**: Accepted
> **Domain**: PCF/Frontend Architecture
> **Last Updated**: 2025-12-30

---

## Decision

Use **Dataverse platform-provided libraries** (React 16/17, Fluent UI v9) for PCF controls instead of bundling these dependencies. This ensures compatibility, reduces bundle size by ~20x, and aligns with Microsoft's recommended practices.

**Rationale**: Dataverse provides React as a platform library. Using React 18 APIs (e.g., `createRoot`) causes runtime errors because the platform injects React 16/17 at runtime.

**Runtime Versions** (as of December 2025):

| Context | Manifest Version | Actual Runtime Version |
|---------|------------------|------------------------|
| Model-driven apps | 16.14.0 | **17.0.2** |
| Canvas apps | 16.14.0 | 16.14.0 |

**React 18 is NOT available** as a platform library. Microsoft has not announced a timeline for React 18 support.

---

## Constraints

### MUST

- **MUST** use React 16 APIs in PCF controls (`ReactDOM.render`, not `createRoot`)
- **MUST** declare platform libraries in `ControlManifest.Input.xml`:
  ```xml
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
  ```
- **MUST** create `featureconfig.json` to enable ReactDOM externalization:
  ```json
  { "pcfReactPlatformLibraries": "on" }
  ```
- **MUST** use `devDependencies` for React types (not `dependencies`)
- **MUST** test PCF controls in Dataverse environment (not just local harness)

### MUST NOT

- **MUST NOT** use React 18+ APIs (`createRoot`, `hydrateRoot`, concurrent features)
- **MUST NOT** bundle React/ReactDOM into PCF output (bundle should be <1MB)
- **MUST NOT** import from `react-dom/client` (React 18 entry point)
- **MUST NOT** deploy managed solutions unless user explicitly requests (always use unmanaged)

---

## Implementation Patterns

### React 16 Render Pattern

```typescript
// index.ts - PCF control entry point
import * as React from "react";
import * as ReactDOM from "react-dom";  // NOT react-dom/client

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
      ReactDOM.unmountComponentAtNode(this.container);  // React 16 API
      this.container = null;
    }
  }

  private renderReactTree(context): void {
    if (!this.container) return;

    // React 16 API - NOT createRoot().render()
    ReactDOM.render(
      React.createElement(FluentProvider, { theme },
        React.createElement(MyComponent, { context })
      ),
      this.container
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

### Package.json Configuration

```json
{
  "dependencies": {
    // NO React here - platform provides it
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

## Common Errors

| Error | Cause | Fix |
|-------|-------|-----|
| `Cannot create property '_updatedFibers' on number '0'` | Using React 18 `createRoot` with React 16 runtime | Use `ReactDOM.render()` |
| `createRoot is not a function` | Importing from `react-dom/client` | Import from `react-dom` |
| Bundle > 5MB | React bundled in output | Add `platform-library` to manifest |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | PCF as preferred UI technology |
| [ADR-021](ADR-021-fluent-design-system.md) | Fluent UI v9 provided via platform library |
| [ADR-012](ADR-012-shared-components.md) | Shared components use same React 16 constraint |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-022-pcf-platform-libraries.md](../../docs/adr/ADR-022-pcf-platform-libraries.md)

---

**Lines**: ~110
