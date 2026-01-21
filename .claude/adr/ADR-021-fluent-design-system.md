# ADR-021: Fluent UI v9 Design System (Concise)

> **Status**: Accepted
> **Domain**: UI/UX Design System
> **Last Updated**: 2026-01-20

---

## Decision

All Spaarke UI must follow the **Microsoft Fluent UI v9.x design system**. This is the authoritative standard for typography, icons, components, theming, and accessibility across all surfaces.

**Rationale**: Ensures consistent UX, dark mode compatibility, accessibility compliance, and alignment with Microsoft design language.

---

## Constraints

### Technology Stack

| Technology | Version | Package |
|------------|---------|---------|
| **Fluent UI** | v9.x | `@fluentui/react-components` |
| **React** | 16.14.0 (manifest) / 17.0.2 (model-driven runtime) | `react`, `react-dom` (devDependencies) |
| **Icons** | Latest | `@fluentui/react-icons` |

> **Important**: Dataverse provides React as a platform library. Use React 16 APIs (`ReactDOM.render`). See [ADR-022](ADR-022-pcf-platform-libraries.md) for details.

### MUST

- **MUST** use `@fluentui/react-components` (Fluent v9) exclusively
- **MUST** use React 16 APIs (`ReactDOM.render()`, `unmountComponentAtNode()`) - NOT React 18 `createRoot`
- **MUST** import icons from `@fluentui/react-icons`
- **MUST** wrap all UI in `FluentProvider` with theme
- **MUST** use Fluent design tokens for colors, spacing, typography
- **MUST** support light, dark, and high-contrast modes
- **MUST** ensure icons use `currentColor` for theme compatibility
- **MUST** meet WCAG 2.1 AA accessibility standards
- **MUST** use `makeStyles` (Griffel) for custom styling
- **MUST** declare `platform-library` in PCF manifests
- **MUST** keep PCF bundle under 5MB

### MUST NOT

- **MUST NOT** use Fluent v8 (`@fluentui/react`)
- **MUST NOT** hard-code colors (hex, rgb, named colors)
- **MUST NOT** use React 18.x or 19.x APIs (`createRoot`, `hydrateRoot`, concurrent features)
- **MUST NOT** import from `react-dom/client` (React 18 entry point)
- **MUST NOT** import from granular `@fluentui/react-*` packages
- **MUST NOT** bundle React/Fluent in PCF artifacts
- **MUST NOT** use alternative UI libraries (MUI, Ant Design, etc.)
- **MUST NOT** style Fluent internals with global CSS
- **MUST NOT** use custom icon fonts (Font Awesome, Material Icons)

---

## Implementation Patterns

### FluentProvider Wrapper

```typescript
import { FluentProvider, webLightTheme } from "@fluentui/react-components";
import { spaarkeLight, spaarkeDark } from "@spaarke/ui-components/theme";

// Always wrap UI in FluentProvider
<FluentProvider theme={spaarkeLight}>
  <App />
</FluentProvider>
```

### Component Imports

```typescript
// CORRECT: Converged entry point
import {
  Button,
  DataGrid,
  Dialog,
  tokens
} from "@fluentui/react-components";

// CORRECT: Individual icon imports
import { DocumentAdd20Regular, Settings24Regular } from "@fluentui/react-icons";

// WRONG: Fluent v8
import { Button } from "@fluentui/react";

// WRONG: Granular v9 packages
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
    borderRadius: tokens.borderRadiusMedium,
  },
});

// WRONG: Hard-coded values
const badStyles = {
  backgroundColor: "#ffffff",  // WRONG
  color: "black",              // WRONG
  padding: "16px",             // Use tokens.spacingHorizontalM
};
```

### Icon Usage

```typescript
import { DocumentAdd20Regular } from "@fluentui/react-icons";

// Icon button with accessibility
<Button icon={<DocumentAdd20Regular />} aria-label="Add document" />

// Icon inherits color from context (works in dark mode)
<span style={{ color: tokens.colorBrandForeground1 }}>
  <DocumentAdd20Regular />
</span>
```

### PCF Manifest

```xml
<resources>
  <code path="index.ts" order="1" />
  <!-- Platform-provided libraries (not bundled) -->
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
</resources>
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
    "react-dom": "^16.14.0",
    "@fluentui/react-components": "^9.46.0",
    "@fluentui/react-icons": "^2.0.0"
  }
}
```

---

## Dark Mode Support

| Mode | Theme | Token Example |
|------|-------|---------------|
| Light | `webLightTheme` / `spaarkeLight` | `colorNeutralBackground1` = white |
| Dark | `webDarkTheme` / `spaarkeDark` | `colorNeutralBackground1` = dark gray |
| High Contrast | `teamsHighContrastTheme` | High contrast colors |

```typescript
// Detect user preference
const isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
const theme = isDark ? spaarkeDark : spaarkeLight;
```

---

## Ribbon/Command Bar Icons

| Size | Use Case | Format |
|------|----------|--------|
| 16x16 | Ribbon button small | SVG with `currentColor` |
| 32x32 | Ribbon button large | SVG with `currentColor` |

```xml
<Button Id="Spaarke.Document.Upload.Button"
        Image16by16="$webresource:sprk_icon_upload_16.svg"
        Image32by32="$webresource:sprk_icon_upload_32.svg" />
```

**Ribbon JavaScript**: Keep minimal (invocation only, no business logic).

---

## Accessibility Checklist

- [ ] Keyboard navigation works (Tab, Enter, Escape)
- [ ] Focus indicators visible
- [ ] `aria-label` on icon-only buttons
- [ ] Color contrast 4.5:1 minimum
- [ ] Semantic HTML elements used

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | PCF technology choice |
| [ADR-011](ADR-011-dataset-pcf.md) | Dataset PCF uses Fluent v9 |
| [ADR-012](ADR-012-shared-components.md) | Shared component library |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | **React 16 APIs required** - this ADR takes precedence for React version |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-021-fluent-ui-design-system.md](../../docs/adr/ADR-021-fluent-ui-design-system.md)

For detailed context including:
- Complete typography token reference
- Spaarke brand color definitions
- Responsive breakpoint patterns
- Full compliance checklist

---

**Lines**: ~120

