# ADR-021: Fluent UI v9 Design System Standard

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-12-22 |
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
- **PCF Controls** embedded in Dataverse model-driven apps
- **Custom Pages** (Canvas Apps) within model-driven apps
- **Ribbon/Command Bar** customizations
- **Office Add-ins** (future)
- **Power Pages** customizations (future)

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

| Rule | Description |
|------|-------------|
| **Fluent v9 Only** | Use `@fluentui/react-components` (v9.x) exclusively. No Fluent v8 (`@fluentui/react`). |
| **React 18.2.x** | Standardize on React ^18.2.0 for all PCF controls |
| **Semantic Tokens** | Use Fluent design tokens for all styling. No hard-coded colors or pixel values. |
| **Dark Mode Ready** | All components must render correctly in light, dark, and high-contrast modes |
| **Accessibility First** | WCAG 2.1 AA compliance required for all interactive components |
| **Platform Libraries** | PCF controls should use platform-library declarations to avoid bundling React/Fluent |

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

## PCF Control Packaging

### Platform Library Configuration

PCF controls should externalize React and Fluent to reduce bundle size and ensure consistency:

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
    "@types/react": "^18.2.0",
    "@types/react-dom": "^18.2.0",
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
- Use React 19.x (not yet stable for PCF runtime)

See [PCF V9 Packaging Guide](../guides/PCF-V9-PACKAGING.md) for detailed instructions.

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

**PCF Packaging (if applicable):**
- [ ] `platform-library` declared for React and Fluent
- [ ] React/Fluent in `devDependencies` only
- [ ] Bundle size under 5MB
- [ ] Uses `createRoot` pattern (React 18)

**Ribbon/Command Bar (if applicable):**
- [ ] SVG icons with `currentColor`
- [ ] Both 16x16 and 32x32 variants provided
- [ ] JavaScript is invocation-only (no business logic)

---

## AI-Directed Coding Guidance

When creating or modifying UI code:

1. **Always wrap in FluentProvider** with appropriate theme
2. **Import from converged entry points**:
   - Components: `@fluentui/react-components`
   - Icons: `@fluentui/react-icons`
3. **Use Griffel for custom styles**: `makeStyles` and `tokens`
4. **Test dark mode**: Toggle theme and verify rendering
5. **Check accessibility**: Tab through all interactive elements
6. **For PCF**: Declare `platform-library` in manifest
7. **For ribbons**: Keep JavaScript minimal (invocation only)

**Red flags to catch in code review:**
- `@fluentui/react` imports (v8 - should be v9)
- Hard-coded hex colors (`#ffffff`, `rgb(0,0,0)`)
- `react@^19.x` in package.json
- Missing `FluentProvider` wrapper
- Icon buttons without `aria-label`
- `font-family`, `font-size` in CSS (use tokens)

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

