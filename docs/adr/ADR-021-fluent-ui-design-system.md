# ADR-021: Fluent UI v9 Design System Standard

| Field | Value |
|-------|-------|
| Status | **Accepted** (Revised) |
| Date | 2025-12-22 |
| Updated | 2026-02-23 (v1.3 — React 19 for Code Pages) |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-021 Concise](../../.claude/adr/ADR-021-fluent-design-system.md) - ~120 lines, decision + constraints
- [PCF V9 Packaging Guide](../guides/PCF-V9-PACKAGING.md) - Bundle optimization and platform libraries
- [ADR-012 Shared Components](./ADR-012-shared-component-library.md) - Component library implementation

**When to load this full ADR**: Icon standards, typography details, ribbon customization patterns, dark mode implementation

---

## Context

Spaarke builds UI across multiple surfaces:
- **PCF Controls** — field-bound controls embedded on Dataverse model-driven app forms
- **React Code Pages** — standalone HTML web resources opened as dialogs via `Xrm.Navigation.navigateTo` (React 19, bundled)
- **Ribbon/Command Bar** — thin JavaScript invocation scripts (no business logic)
- **Office Add-ins** — React 18 applications (bundled)
- **Shared Component Library** (`@spaarke/ui-components`) — consumed by all surfaces above

Without a unified design system standard, we risk:
- **Inconsistent user experience** across different surfaces
- **Dark mode incompatibility** when users or organizations enable dark themes
- **Accessibility failures** (WCAG non-compliance)
- **Bundle bloat** from mixed Fluent UI versions (v8 vs v9)
- **Runtime conflicts** from React version mismatches
- **Maintenance burden** from fragmented styling approaches

### Related ADRs

This ADR consolidates and extends UI/UX rules from:
- **ADR-006**: PCF over webresources (mentions Fluent briefly)
- **ADR-011**: Dataset PCF (requires Fluent v9 exclusively)
- **ADR-012**: Shared component library (detailed UI/UX standards)

This ADR serves as the **authoritative reference** for all UI/UX design decisions.

---

## Decision

**All Spaarke UI must follow the Microsoft Fluent UI v9.x design system.**

| Rule | Applies To | Description |
|------|-----------|-------------|
| **Fluent v9 Only** | All surfaces | Use `@fluentui/react-components` (v9.x) exclusively. No Fluent v8 (`@fluentui/react`). |
| **React 16/17 APIs** | PCF controls only | Use `ReactDOM.render()` / `unmountComponentAtNode()`. Runtime is 16.14.0 (canvas) or 17.0.2 (model-driven). See [ADR-022](./ADR-022-pcf-platform-libraries.md). |
| **React 19 APIs** | React Code Pages | Use `createRoot()` from `react-dom/client`. React 19 is bundled in the Code Page output. React 19 stable since Dec 2024. |
| **React 18 APIs** | Office Add-ins | Office add-ins currently bundle React 18 (upgrade to 19 separately). |
| **Semantic Tokens** | All surfaces | Use Fluent design tokens for all styling. No hard-coded colors or pixel values. |
| **Dark Mode Ready** | All surfaces | All components must render correctly in light, dark, and high-contrast modes. |
| **Accessibility First** | All surfaces | WCAG 2.1 AA compliance required for all interactive components. |
| **Platform Libraries** | PCF controls only | MUST use `platform-library` declarations in manifest to avoid bundling React/Fluent. |

### React Version by Surface

> **Critical**: The React version constraint depends on the UI surface.

| Surface | React Version | Entry Point | Where React Lives |
|---------|---------------|-------------|-------------------|
| **PCF controls** (form-bound) | 16.14.0 manifest / 17.0.2 runtime | `ReactDOM.render()` | Platform-provided — DO NOT bundle |
| **React Code Pages** (standalone dialogs) | **React 19** | `createRoot()` from `react-dom/client` | Bundled in Code Page output |
| **Office Add-ins** | **React 18** | `createRoot()` | Bundled (upgrade to React 19 separately) |
| **Shared component library** | `>=16.14.0` (peerDependency) | — | Provided by consumer — supports all surfaces |

---

## Constraints

### Typography

| Element | Fluent Token | Usage |
|---------|--------------|-------|
| Page title | `typographyStyles.title1` | Main headings |
| Section header | `typographyStyles.subtitle1` | Section titles |
| Body text | `typographyStyles.body1` | Standard content |
| Caption | `typographyStyles.caption1` | Secondary info, timestamps |
| Code/monospace | `typographyStyles.code` | Technical content |

**MUST:**
- Use Fluent typography tokens for all text styling
- Maintain consistent type scale across surfaces
- Support dynamic font scaling for accessibility

**MUST NOT:**
- Hard-code font families, sizes, or weights
- Use CSS `!important` overrides on typography
- Mix typography systems (no Bootstrap, Tailwind, etc.)

### Icons

| Rule | Implementation |
|------|----------------|
| **Icon Library** | Use `@fluentui/react-icons` exclusively |
| **Size Variants** | Use standard sizes: 16px, 20px, 24px, 32px, 48px |
| **Naming Convention** | `{Name}{Size}{Style}` (e.g., `DocumentAdd20Regular`, `Settings24Filled`) |
| **Color Inheritance** | Icons must use `currentColor` to inherit text color |

**MUST:**
- Import icons from `@fluentui/react-icons`
- Use appropriate size variants (16 for inline, 20 for buttons, 24+ for emphasis)
- Ensure icons work in dark mode via `currentColor`
- Provide `aria-label` for icon-only buttons

**MUST NOT:**
- Use custom SVG icons without design review
- Hard-code icon colors (breaks dark mode)
- Use icon fonts (Font Awesome, Material Icons, etc.)
- Import entire icon library (tree-shake individual icons)

```typescript
// CORRECT: Import specific icons
import { DocumentAdd20Regular, Settings24Regular } from "@fluentui/react-icons";

// WRONG: Import all icons
import * as Icons from "@fluentui/react-icons";
```

### Components

| Component Type | Source | Notes |
|----------------|--------|-------|
| Buttons | `@fluentui/react-components` | Button, CompoundButton, ToggleButton |
| Inputs | `@fluentui/react-components` | Input, Textarea, Select, Combobox |
| Data Display | `@fluentui/react-components` | DataGrid, Table, Badge, Avatar |
| Feedback | `@fluentui/react-components` | Toast, MessageBar, Spinner, ProgressBar |
| Navigation | `@fluentui/react-components` | Menu, TabList, Breadcrumb |
| Overlays | `@fluentui/react-components` | Dialog, Popover, Tooltip, Drawer |

**MUST:**
- Use Fluent v9 components for all UI elements
- Wrap components in `FluentProvider` with appropriate theme
- Use Griffel (`makeStyles`) for custom styling
- Follow Fluent component patterns (controlled vs uncontrolled)

**MUST NOT:**
- Mix Fluent v8 and v9 components
- Use alternative component libraries (Ant Design, MUI, Chakra, etc.)
- Style Fluent internals with global CSS selectors
- Re-implement Fluent components from scratch

### Dark Mode Compatibility

All UI must support three theme modes:

| Mode | Theme Object | When Active |
|------|--------------|-------------|
| Light | `webLightTheme` / `spaarkeLight` | Default in most environments |
| Dark | `webDarkTheme` / `spaarkeDark` | User preference or org policy |
| High Contrast | `teamsHighContrastTheme` | Accessibility requirement |

**Implementation Pattern:**

```typescript
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
  tokens
} from "@fluentui/react-components";

// Detect theme from platform or user preference
const theme = usePlatformTheme(); // webLightTheme or webDarkTheme

return (
  <FluentProvider theme={theme}>
    <div style={{
      backgroundColor: tokens.colorNeutralBackground1,
      color: tokens.colorNeutralForeground1
    }}>
      {children}
    </div>
  </FluentProvider>
);
```

**MUST:**
- Use semantic color tokens (`colorNeutralBackground1`, not `#ffffff`)
- Test all components in light, dark, and high-contrast modes
- Support runtime theme switching without page reload
- Use `currentColor` for SVGs and icons

**MUST NOT:**
- Hard-code colors in CSS or inline styles
- Use opacity for disabled states (use semantic tokens)
- Assume light mode as default without fallback
- Use images with baked-in backgrounds (use transparent PNGs/SVGs)

### Responsive Design

| Breakpoint | Token | Width | Target |
|------------|-------|-------|--------|
| Small | `breakpointSmall` | <640px | Mobile, narrow panels |
| Medium | `breakpointMedium` | 640-1024px | Tablets, side panels |
| Large | `breakpointLarge` | 1024-1366px | Desktop, standard forms |
| Extra Large | `breakpointXLarge` | >1366px | Wide monitors |

**Implementation Pattern:**

```typescript
import { makeStyles, tokens } from "@fluentui/react-components";

const useStyles = makeStyles({
  container: {
    display: "grid",
    gridTemplateColumns: "1fr",
    gap: tokens.spacingHorizontalM,

    // Responsive breakpoints
    "@media (min-width: 640px)": {
      gridTemplateColumns: "1fr 1fr",
    },
    "@media (min-width: 1024px)": {
      gridTemplateColumns: "1fr 1fr 1fr",
    },
  },
});
```

**MUST:**
- Design mobile-first, enhance for larger screens
- Use CSS Grid or Flexbox for layouts (not fixed widths)
- Test PCF controls at different form factor widths
- Support Dataverse form sections (1-column, 2-column, 3-column)

**MUST NOT:**
- Use fixed pixel widths for containers
- Hide critical functionality on mobile
- Rely on hover states for essential interactions (touch devices)

---

## PCF Control Packaging (Field-Bound Controls Only)

> **Scope**: This section applies only to field-bound PCF controls. React Code Pages use a completely different packaging model — see [React Code Page Packaging](#react-code-page-packaging) below.

### Platform Library Configuration

PCF controls must externalize React and Fluent to reduce bundle size and ensure compatibility with the platform-provided runtime:

**ControlManifest.Input.xml:**
```xml
<resources>
  <code path="index.ts" order="1" />
  <!-- Host-provided libraries -->
  <platform-library name="React" version="16.14.0" />
  <platform-library name="Fluent" version="9.46.2" />
  <css path="styles.css" order="2" />
</resources>
```

**package.json:**
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

**MUST:**
- Declare `platform-library` for React and Fluent in manifest
- Move React/Fluent to `devDependencies` (compile-time only)
- Import from `@fluentui/react-components` (converged entry point)
- Keep bundle size under 5MB

**MUST NOT:**
- Bundle React/Fluent in PCF artifact
- Mix platform-library with bundled React
- Import from granular `@fluentui/react-*` packages
- Use React 18.x APIs (`createRoot`, `hydrateRoot`, concurrent features) — platform provides React 16/17 only
- Import from `react-dom/client` (React 18 entry point)

See [PCF V9 Packaging Guide](../guides/PCF-V9-PACKAGING.md) for detailed instructions.

---

## React Code Page Packaging

React Code Pages are standalone HTML web resources (not form-bound PCF controls). They **bundle their own React 19** — they are NOT subject to platform library constraints.

**package.json for a React Code Page:**
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

**index.tsx entry point:**
```typescript
import { createRoot } from "react-dom/client";  // React 19 ✅
import { FluentProvider, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import { App } from "./App";

const params = new URLSearchParams(window.location.search);
const isDark = window.matchMedia("(prefers-color-scheme: dark)").matches;

createRoot(document.getElementById("root")!).render(
  <FluentProvider theme={isDark ? webDarkTheme : webLightTheme}>
    <App params={params} />
  </FluentProvider>
);
```

**Parameters are passed via URL** (from `navigateTo` `data` field) and read with `URLSearchParams`. See [ADR-006](./ADR-006-prefer-pcf-over-webresources.md) for the Code Page architecture and opening pattern.

---

## Ribbon/Command Bar Customizations

Ribbon buttons in Dataverse model-driven apps have specific constraints:

### Icon Requirements

| Location | Icon Source | Size |
|----------|-------------|------|
| Ribbon button (16x16) | `/_imgs/ribbon/*.png` or SVG web resource | 16x16 px |
| Ribbon button (32x32) | `/_imgs/ribbon/*.png` or SVG web resource | 32x32 px |
| Command bar (modern) | Fluent icons via JavaScript | Dynamic |

**For Classic Ribbon:**
```xml
<Button Id="Spaarke.Document.Upload.Button"
        Command="Spaarke.Document.Upload.Command"
        LabelText="Upload Document"
        Image16by16="$webresource:sprk_icon_upload_16.svg"
        Image32by32="$webresource:sprk_icon_upload_32.svg" />
```

**For Modern Command Bar (JavaScript):**
```javascript
// Use Fluent icon names in modern commanding
{
  "Icon": "DocumentAdd",  // Fluent icon reference
  "Label": "Upload Document"
}
```

**MUST:**
- Use SVG web resources for ribbon icons (scale better than PNG)
- Ensure SVG icons use `currentColor` for theme compatibility
- Provide both 16x16 and 32x32 variants
- Follow Fluent icon visual style

**MUST NOT:**
- Use complex multi-color icons in ribbons
- Hard-code colors in SVG icons
- Use raster images (PNG/JPG) for new icons

### JavaScript Invocation Pattern

Ribbon/command bar JavaScript should be minimal (invocation only):

```javascript
// CORRECT: Thin invoker
function Spaarke_OpenDocumentDialog(primaryControl) {
    const formContext = primaryControl;
    const recordId = formContext.data.entity.getId();

    // Open PCF-based dialog - no business logic here
    Xrm.Navigation.navigateTo({
        pageType: "custom",
        name: "sprk_documentupload",
        entityName: formContext.data.entity.getEntityName(),
        recordId: recordId
    });
}

// WRONG: Business logic in ribbon script
function Spaarke_ProcessDocument(primaryControl) {
    // DON'T: Make API calls here
    fetch("/api/process", { ... }); // WRONG

    // DON'T: Implement validation here
    if (!validateDocument()) { ... } // WRONG
}
```

See [Ribbon Workbench Guide](../guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md) for detailed procedures.

---

## Spaarke Theme Definitions

### Custom Theme Objects

```typescript
// src/client/shared/Spaarke.UI.Components/src/theme/spaarkeLight.ts
import { createLightTheme, BrandVariants } from "@fluentui/react-components";

const spaarkeBrand: BrandVariants = {
  10: "#020305",
  20: "#111723",
  30: "#16253D",
  40: "#1B3250",
  50: "#1F4064",
  60: "#234E79",
  70: "#265D8F",
  80: "#2A6CA5",  // Primary brand color
  90: "#3A7DB5",
  100: "#4F8EC4",
  110: "#659FD2",
  120: "#7DB0DF",
  130: "#96C1EA",
  140: "#B1D2F3",
  150: "#CDE3FA",
  160: "#E9F4FF",
};

export const spaarkeLight = createLightTheme(spaarkeBrand);
```

```typescript
// src/client/shared/Spaarke.UI.Components/src/theme/spaarkeDark.ts
import { createDarkTheme } from "@fluentui/react-components";
import { spaarkeBrand } from "./brand";

export const spaarkeDark = createDarkTheme(spaarkeBrand);
```

### Theme Usage

```typescript
import { FluentProvider } from "@fluentui/react-components";
import { spaarkeLight, spaarkeDark } from "@spaarke/ui-components/theme";

// Detect platform theme preference
const isDarkMode = window.matchMedia("(prefers-color-scheme: dark)").matches;
const theme = isDarkMode ? spaarkeDark : spaarkeLight;

<FluentProvider theme={theme}>
  <App />
</FluentProvider>
```

---

## Accessibility Requirements

### WCAG 2.1 AA Compliance

| Requirement | Implementation |
|-------------|----------------|
| **Color Contrast** | 4.5:1 for normal text, 3:1 for large text |
| **Keyboard Navigation** | All interactive elements focusable via Tab |
| **Focus Indicators** | Visible focus ring on all focusable elements |
| **Screen Reader** | Semantic HTML, ARIA labels where needed |
| **Motion** | Respect `prefers-reduced-motion` |

**MUST:**
- Use semantic HTML elements (`<button>`, `<nav>`, `<main>`)
- Provide `aria-label` for icon-only buttons
- Maintain logical tab order
- Support keyboard shortcuts for common actions
- Test with screen readers (NVDA, VoiceOver)

**MUST NOT:**
- Remove focus outlines without replacement
- Use color alone to convey information
- Create keyboard traps
- Auto-play animations without user control

---

## Compliance Checklist

### Code Review Gate

Use this checklist for PRs that add or modify UI components:

**Design System:**
- [ ] Uses Fluent UI v9 components only (`@fluentui/react-components`)
- [ ] No Fluent v8 imports (`@fluentui/react`)
- [ ] Icons from `@fluentui/react-icons` with appropriate sizes
- [ ] Typography uses Fluent tokens

**Theming:**
- [ ] No hard-coded colors (uses semantic tokens)
- [ ] Works in light mode
- [ ] Works in dark mode
- [ ] Works in high-contrast mode
- [ ] Icons use `currentColor`

**Accessibility:**
- [ ] Keyboard navigation works
- [ ] Focus indicators visible
- [ ] ARIA labels on icon-only buttons
- [ ] Color contrast meets 4.5:1 ratio

**PCF Packaging (if PCF control):**
- [ ] `platform-library` declared for React and Fluent in manifest
- [ ] React/Fluent in `devDependencies` only (not `dependencies`)
- [ ] Bundle size under 5MB
- [ ] Uses `ReactDOM.render()` pattern (React 16/17) — NOT `createRoot`
- [ ] No imports from `react-dom/client`

**React Code Page Packaging (if Code Page):**
- [ ] React 18 in `dependencies` (bundled in output) — correct for Code Pages
- [ ] Uses `createRoot()` from `react-dom/client`
- [ ] Parameters read via `new URLSearchParams(window.location.search)`
- [ ] Opened via `Xrm.Navigation.navigateTo({ pageType: "webresource", ... }, { target: 2 })`

**Ribbon/Command Bar (if applicable):**
- [ ] SVG icons with `currentColor`
- [ ] Both 16x16 and 32x32 variants provided
- [ ] JavaScript is invocation-only (no business logic)

---

## AI-Directed Coding Guidance

When creating or modifying UI code:

1. **Determine the surface first**: PCF (form-bound) vs React Code Page (standalone dialog) — React version rules differ.
2. **Always wrap in FluentProvider** with appropriate theme.
3. **Import from converged entry points**:
   - Components: `@fluentui/react-components`
   - Icons: `@fluentui/react-icons`
4. **Use Griffel for custom styles**: `makeStyles` and `tokens`.
5. **Test dark mode**: Toggle theme and verify rendering.
6. **Check accessibility**: Tab through all interactive elements.
7. **For PCF**: Declare `platform-library` in manifest; use `ReactDOM.render()` (React 16/17 API).
8. **For Code Pages**: Use `createRoot()` (React 18); pass params via URL; open via `navigateTo`.
9. **For ribbons**: Keep JavaScript minimal (invocation only).

**Red flags to catch in code review:**

| Flag | Context | Why |
|------|---------|-----|
| `@fluentui/react` imports | Any | Fluent v8 — must use v9 |
| Hard-coded hex colors | Any | Use semantic tokens |
| `react@^18.x` in `dependencies` of a **PCF** project | PCF only | PCF must use devDependencies with `^16.14.0` |
| `react@^16.x` in `dependencies` of a **Code Page** | Code Page only | Code Pages SHOULD use React 18 |
| `@types/react@^18` in a **PCF** project | PCF only | Should be `@types/react@^16.14.0` |
| `createRoot` or `import from 'react-dom/client'` in **PCF** code | PCF only | Use `ReactDOM.render()` instead |
| `ReactDOM.render()` in **Code Page** code | Code Page only | Use `createRoot()` instead |
| Missing `FluentProvider` wrapper | Any | Required for theming |
| Icon buttons without `aria-label` | Any | Accessibility requirement |
| `font-family`, `font-size` in CSS | Any | Use Fluent tokens |

---

## References

- [Fluent UI React v9 Documentation](https://react.fluentui.dev/)
- [Fluent Design System](https://fluent2.microsoft.design/)
- [Fluent Icons Gallery](https://react.fluentui.dev/?path=/docs/icons-catalog--page)
- [PCF Platform Libraries](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/code-components-best-practices#use-platform-libraries)
- [WCAG 2.1 Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)

---

## Revision History

| Date | Version | Changes | Author |
|------|---------|---------|--------|
| 2025-12-22 | 1.0 | Initial ADR creation - consolidated UI/UX standards | Spaarke Engineering |
| 2026-01-20 | 1.1 | Aligned React version with ADR-022: React 16.14.0 APIs required (not React 18). Updated package.json examples, compliance checklist, and red flags. | Spaarke Engineering |
| 2026-02-23 | 1.2 | Revised for two-tier architecture (ADR-006 revision): split React version rules by surface. PCF: React 16/17 platform-provided. React Code Pages: React 18 bundled. Added Code Page packaging section. Updated decision table and code review checklist to distinguish PCF vs Code Page rules. Red flags table updated — `react@^18.x in dependencies` is a red flag for PCF but correct for Code Pages. | Spaarke Engineering |

