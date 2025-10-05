# Phase B: Theming & Design Tokens

**Sprint:** 5B - Universal Dataset Grid Compliance
**Phase:** B - Theming & Design Tokens
**Priority:** MEDIUM
**Estimated Effort:** 1-2 days
**Status:** 🔴 Not Started
**Depends On:** Phase A completion

---

## Objective

Implement proper theme support and replace all hard-coded styles with Fluent UI design tokens to ensure the control works correctly in light mode, dark mode, and high-contrast mode.

---

## Current Issues

**From Compliance Assessment (Section 2.1 & 2.2):**
> "The control ignores host theme changes (`context.mode` and `context.fluentDesignLanguage`). Hard-coded fonts (`Segoe UI`) and colors bypass theme tokens."

**Problems:**
1. Always uses `webLightTheme` - no dark mode support
2. Hard-coded colors: `#f3f2f1`, `#323130`
3. Hard-coded fonts: `Segoe UI, sans-serif`
4. No response to Power Apps theme changes
5. Breaks in high-contrast mode

---

## Tasks

### Task B.1: Implement Dynamic Theme Resolution

**File:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/providers/ThemeProvider.ts`

**Objective:** Detect Power Apps theme and provide correct Fluent theme

**AI Coding Instructions:**

```typescript
/**
 * Enhanced theme resolution with Power Apps context detection
 */

import {
    Theme,
    webLightTheme,
    webDarkTheme,
    teamsLightTheme,
    teamsDarkTheme,
    teamsHighContrastTheme,
    createLightTheme,
    createDarkTheme,
    BrandVariants
} from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';

/**
 * Resolve the appropriate Fluent UI theme based on Power Apps context.
 *
 * Detects:
 * - Light vs Dark mode
 * - High contrast mode
 * - Custom brand colors from Power Apps
 *
 * @param context - PCF context
 * @returns Fluent UI theme
 */
export function resolveTheme(context: ComponentFramework.Context<IInputs>): Theme {
    // Get theme information from Power Apps context
    const fluentDesign = context.fluentDesignLanguage;

    // Check for high contrast mode
    if (context.accessibility?.highContrastMode) {
        console.log('[ThemeProvider] Using high contrast theme');
        return teamsHighContrastTheme;
    }

    // Detect dark mode
    const isDarkMode =
        fluentDesign?.name?.toLowerCase().includes('dark') ||
        context.userSettings?.theme === 'dark';

    // Get brand colors from Power Apps (if available)
    const brandColors = getBrandColorsFromContext(context);

    // If custom brand colors provided, create custom theme
    if (brandColors) {
        console.log('[ThemeProvider] Creating custom branded theme');
        const createTheme = isDarkMode ? createDarkTheme : createLightTheme;
        return createTheme(brandColors);
    }

    // Use standard theme
    console.log(`[ThemeProvider] Using ${isDarkMode ? 'dark' : 'light'} theme`);
    return isDarkMode ? webDarkTheme : webLightTheme;
}

/**
 * Extract brand colors from Power Apps context.
 */
function getBrandColorsFromContext(
    context: ComponentFramework.Context<IInputs>
): BrandVariants | null {
    const palette = context.fluentDesignLanguage?.palette;

    if (!palette?.themePrimary) {
        return null;
    }

    // Map Power Apps colors to Fluent brand variants
    return {
        10: adjustColor(palette.themePrimary, 0.95),
        20: adjustColor(palette.themePrimary, 0.90),
        30: adjustColor(palette.themePrimary, 0.85),
        40: adjustColor(palette.themePrimary, 0.80),
        50: adjustColor(palette.themePrimary, 0.75),
        60: adjustColor(palette.themePrimary, 0.70),
        70: adjustColor(palette.themePrimary, 0.65),
        80: adjustColor(palette.themePrimary, 0.60),
        90: adjustColor(palette.themePrimary, 0.55),
        100: adjustColor(palette.themePrimary, 0.50),
        110: adjustColor(palette.themePrimary, 0.45),
        120: adjustColor(palette.themePrimary, 0.40),
        130: adjustColor(palette.themePrimary, 0.35),
        140: adjustColor(palette.themePrimary, 0.30),
        150: adjustColor(palette.themePrimary, 0.25),
        160: adjustColor(palette.themePrimary, 0.20)
    };
}

/**
 * Adjust color brightness (simple implementation).
 */
function adjustColor(color: string, factor: number): string {
    // TODO: Implement proper color adjustment
    // For now, return the base color
    return color;
}
```

---

### Task B.2: Replace Inline Styles with Tokens

**Files:**
- `UniversalDatasetGridRoot.tsx`
- `DatasetGrid.tsx`
- `CommandBar.tsx`

**Objective:** Remove all hard-coded colors, fonts, and spacing

**AI Coding Instructions:**

```typescript
/**
 * Replace hard-coded styles with Fluent design tokens
 *
 * BEFORE:
 * style={{ color: '#323130', fontFamily: 'Segoe UI', padding: '8px' }}
 *
 * AFTER:
 * style={{
 *   color: tokens.colorNeutralForeground1,
 *   fontFamily: tokens.fontFamilyBase,
 *   padding: tokens.spacingVerticalM
 * }}
 */

// Common replacements:
// - Colors:
//   '#f3f2f1' -> tokens.colorNeutralBackground2
//   '#323130' -> tokens.colorNeutralForeground1
//   '#ffffff' -> tokens.colorNeutralBackground1
//   '#edebe9' -> tokens.colorNeutralBackground3
//
// - Spacing:
//   '4px' -> tokens.spacingVerticalXS / tokens.spacingHorizontalXS
//   '8px' -> tokens.spacingVerticalS / tokens.spacingHorizontalS
//   '12px' -> tokens.spacingVerticalM / tokens.spacingHorizontalM
//   '16px' -> tokens.spacingVerticalL / tokens.spacingHorizontalL
//
// - Typography:
//   'Segoe UI' -> tokens.fontFamilyBase
//   '14px' -> tokens.fontSizeBase300
//   '12px' -> tokens.fontSizeBase200
//   '16px' -> tokens.fontSizeBase400
//
// - Borders:
//   '1px solid #edebe9' -> `1px solid ${tokens.colorNeutralStroke1}`

// Example transformation for DatasetGrid.tsx:
return (
    <div
        style={{
            height: '100%',
            overflow: 'auto',
            background: tokens.colorNeutralBackground1, // WAS: '#ffffff'
            fontFamily: tokens.fontFamilyBase,          // WAS: 'Segoe UI'
            fontSize: tokens.fontSizeBase300            // WAS: '14px'
        }}
    >
```

---

### Task B.3: Create Component Styles with makeStyles

**Objective:** Use Fluent's `makeStyles` for better performance and theme integration

**AI Coding Instructions:**

```typescript
/**
 * Optional: Use makeStyles for complex styling
 *
 * This is a best practice for Fluent UI but not required for basic compliance.
 */

import { makeStyles, tokens } from '@fluentui/react-components';

// Define styles using makeStyles hook
const useDatasetGridStyles = makeStyles({
    root: {
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        backgroundColor: tokens.colorNeutralBackground1
    },
    gridContainer: {
        flex: 1,
        overflow: 'auto',
        padding: tokens.spacingVerticalM
    },
    emptyState: {
        textAlign: 'center',
        padding: tokens.spacingVerticalXXL,
        color: tokens.colorNeutralForeground3
    }
});

// Use in component
export const DatasetGrid: React.FC<DatasetGridProps> = (props) => {
    const styles = useDatasetGridStyles();

    return (
        <div className={styles.root}>
            <div className={styles.gridContainer}>
                {/* Grid content */}
            </div>
        </div>
    );
};
```

---

## Testing Checklist

### Theme Testing
- [ ] Control renders correctly in light mode
- [ ] Control renders correctly in dark mode
- [ ] Control renders correctly in high-contrast mode
- [ ] Theme changes when Power Apps theme changes
- [ ] Brand colors respected (if provided by Power Apps)

### Token Validation
- [ ] No hard-coded hex colors in code (`#ffffff`, `#323130`, etc.)
- [ ] No hard-coded fonts (`Segoe UI`)
- [ ] No hard-coded pixel values for spacing (`8px`, `12px`)
- [ ] All tokens imported from `@fluentui/react-components`

### Visual Testing
- [ ] Control looks correct in all themes
- [ ] Contrast ratios meet WCAG 2.1 AA standards
- [ ] Text is readable in all themes
- [ ] Borders and dividers visible in all themes

---

## Validation Criteria

### Success Criteria:
1. ✅ Dynamic theme resolution based on Power Apps context
2. ✅ No hard-coded colors in codebase
3. ✅ All spacing uses Fluent tokens
4. ✅ All typography uses Fluent tokens
5. ✅ Theme updates when Power Apps theme changes
6. ✅ High-contrast mode works properly

### Code Quality:
- ✅ Theme resolution has proper logging
- ✅ Graceful fallback if context unavailable
- ✅ No console errors in any theme mode

---

## References

- **Compliance Assessment:** Section 2.2 "Fluent UI v9 ADR Violations"
- **Compliance Assessment:** Section 4.3 "Theme Resolution"
- **Fluent Theming Docs:** https://react.fluentui.dev/?path=/docs/concepts-developer-theming--page
- **Design Tokens:** https://react.fluentui.dev/?path=/docs/concepts-developer-design-tokens--page

---

## Completion Criteria

Phase B is complete when:
1. All tasks completed (B.1, B.2, B.3)
2. Theme switches work in Power Apps
3. No hard-coded styles remain
4. All validation criteria met
5. Visual testing passed in all themes

---

_Document Version: 1.0_
_Created: 2025-10-05_
_Status: Ready for Implementation_
_Requires: Phase A completion_
