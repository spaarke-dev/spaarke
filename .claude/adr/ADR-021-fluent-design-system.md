# ADR-021: Fluent UI v9 Design System (Concise)

> **Status**: Accepted (Revised 2026-02-23, Updated 2026-02-23)
> **Domain**: UI/UX Design System
> **Last Updated**: 2026-02-23

---

## Decision

All Spaarke UI must follow the **Microsoft Fluent UI v9.x design system**. This is the authoritative standard for typography, icons, components, theming, and accessibility across all surfaces — both PCF controls and React Code Pages.

**Rationale**: Ensures consistent UX, dark mode compatibility, accessibility compliance, and alignment with Microsoft design language.

---

## React Version by Surface

> **Critical**: The React version constraint depends on the UI surface, not a blanket rule.

| Surface | React Version | Entry Point | Reason |
|---------|---------------|-------------|--------|
| **PCF controls** (form-bound) | 16.14.0 manifest / **17.0.2 runtime** | `ReactDOM.render()` | Dataverse platform provides React — cannot bundle |
| **React Code Pages** (dialogs, standalone) | **React 19** | `createRoot()` | Bundled independently — no Dataverse version constraint on HTML web resources |
| **Office add-ins** | React 18+ | `createRoot()` | Bundled independently |
| **Shared component library** | peerDep `>=16.14.0` | — | Consumed by all surfaces |

---

## Constraints

### MUST (All Surfaces)

- **MUST** use `@fluentui/react-components` (Fluent v9) exclusively
- **MUST** import icons from `@fluentui/react-icons`
- **MUST** wrap all UI in `FluentProvider` with theme
- **MUST** use Fluent design tokens for colors, spacing, typography
- **MUST** support light, dark, and high-contrast modes
- **MUST** ensure icons use `currentColor` for theme compatibility
- **MUST** meet WCAG 2.1 AA accessibility standards
- **MUST** use `makeStyles` (Griffel) for custom styling

### MUST (PCF Controls Only)

- **MUST** use React 16 APIs (`ReactDOM.render`, not `createRoot`)
- **MUST** declare `platform-library` in PCF manifests
- **MUST** keep PCF bundle under 5MB (React/Fluent not bundled)

### MUST (React Code Pages Only)

- **MUST** use React 19 `createRoot()` entry point
- **MUST** bundle React 19 + Fluent v9 in the Code Page output
- **MUST** read parameters from `URLSearchParams` (not PCF context)

### MUST NOT (All Surfaces)

- **MUST NOT** use Fluent v8 (`@fluentui/react`)
- **MUST NOT** hard-code colors (hex, rgb, named colors)
- **MUST NOT** import from granular `@fluentui/react-*` packages
- **MUST NOT** use alternative UI libraries (MUI, Ant Design, etc.)
- **MUST NOT** style Fluent internals with global CSS
- **MUST NOT** use custom icon fonts (Font Awesome, Material Icons)

### MUST NOT (PCF Controls Only)

- **MUST NOT** use React 18 APIs (`createRoot`, `hydrateRoot`, concurrent features)
- **MUST NOT** import from `react-dom/client` (React 18 entry point)
- **MUST NOT** bundle React/Fluent in PCF artifacts

---

## Implementation Patterns

### PCF Control — React 16/17 (Platform Library)

```typescript
// index.ts — PCF entry point
import * as ReactDOM from "react-dom";  // NOT react-dom/client
import { FluentProvider } from "@fluentui/react-components";

public updateView(context): void {
    ReactDOM.render(           // React 16 API
        React.createElement(FluentProvider, { theme: this._theme },
            React.createElement(MyComponent, { context })
        ),
        this.container
    );
}
```

### React Code Page — React 19 (Bundled)

```typescript
// index.tsx — Code Page entry point
import { createRoot } from "react-dom/client";  // React 19
import { FluentProvider, webLightTheme } from "@fluentui/react-components";

const params = new URLSearchParams(window.location.search);
createRoot(document.getElementById("root")!).render(
    <FluentProvider theme={webLightTheme}>
        <App documentId={params.get("documentId") ?? ""} />
    </FluentProvider>
);
```

### PCF Manifest

```xml
<resources>
  <code path="index.ts" order="1" />
  <!-- Platform-provided libraries (not bundled) — PCF only -->
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
```

### Code Page package.json (React 19)

```json
{
  "dependencies": {
    "react": "^19.0.0",
    "react-dom": "^19.0.0",
    "@fluentui/react-components": "^9.54.0",
    "@fluentui/react-icons": "^2.0.0",
    "@spaarke/ui-components": "workspace:*"
  }
}
```

### PCF package.json (React 16 devDependency only)

```json
{
  "dependencies": {
    "@spaarke/ui-components": "workspace:*"
  },
  "devDependencies": {
    "@types/react": "^16.14.0",
    "@types/react-dom": "^16.9.0",
    "react": "^16.14.0",
    "react-dom": "^16.14.0",
    "@fluentui/react-components": "^9.46.0",
    "@fluentui/react-icons": "^2.0.0"
  }
}
```

---

## Component Imports (All Surfaces)

```typescript
// ✅ CORRECT: Converged entry point
import { Button, DataGrid, Dialog, tokens } from "@fluentui/react-components";
import { DocumentAdd20Regular } from "@fluentui/react-icons";

// ❌ WRONG: Fluent v8
import { Button } from "@fluentui/react";

// ❌ WRONG: Granular v9 packages
import { Button } from "@fluentui/react-button";
```

### Styling with Tokens

```typescript
import { makeStyles, tokens } from "@fluentui/react-components";

const useStyles = makeStyles({
    container: {
        backgroundColor: tokens.colorNeutralBackground1,
        color: tokens.colorNeutralForeground1,
        padding: tokens.spacingHorizontalM,
    },
});
// WRONG: backgroundColor: "#ffffff"  — use tokens
```

---

## Dark Mode Support

| Mode | Theme | PCF | Code Page |
|------|-------|-----|-----------|
| Light | `webLightTheme` / `spaarkeLight` | ✅ | ✅ |
| Dark | `webDarkTheme` / `spaarkeDark` | ✅ | ✅ |
| High Contrast | `teamsHighContrastTheme` | ✅ | ✅ |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | Two-tier surface architecture |
| [ADR-011](ADR-011-dataset-pcf.md) | Dataset PCF uses Fluent v9 |
| [ADR-012](ADR-012-shared-components.md) | Shared component library |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF platform libraries (field-bound controls only) |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-021-fluent-ui-design-system.md](../../docs/adr/ADR-021-fluent-ui-design-system.md)

---

**Lines**: ~140
