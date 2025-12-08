# Dark Mode Toggle for Model-Driven App

> **Version**: 2.0
> **Date**: December 5, 2025
> **Status**: Draft Specification
> **Author**: AI-Assisted Design
> **Reviewed**: December 5, 2025 (Architecture Review)

---

## 1. Overview

### 1.1 Purpose

Add a **Theme** menu to the model-driven app command bar that allows users to select between Light, Dark, and Auto (system default) themes. The preference is persisted using `localStorage` and applied immediately to all PCF controls without page refresh.

### 1.2 Scope

| In Scope | Out of Scope |
|----------|--------------|
| Command bar menu with theme options | SharePoint Embedded preview theming (not supported by Microsoft) |
| localStorage persistence | Server-side user preference storage |
| PCF control theme awareness (all controls) | Model-driven app shell theming (controlled by Power Platform) |
| Immediate theme application | Theme customization (color palettes) - Phase 1 |
| Shared theme utilities in component library | Custom branded themes - Future enhancement |
| PCF architecture standards update | |

### 1.3 User Story

> As a **model-driven app user**, I want to **select my preferred theme from a menu** so that I can **work comfortably in different lighting conditions** and have my preference remembered across sessions.

---

## 2. Technical Architecture

### 2.1 Solution Components

```
┌─────────────────────────────────────────────────────────────────┐
│                    Model-Driven App Shell                        │
├─────────────────────────────────────────────────────────────────┤
│  ┌──────────────────┐    ┌─────────────────────────────────┐   │
│  │  Command Bar     │    │  PCF Controls                   │   │
│  │  ───────────────│    │  ─────────────────────────────  │   │
│  │  Theme >        │    │  • SpeFileViewer                │   │
│  │    ○ Auto       │───▶│  • UniversalDatasetGrid         │   │
│  │    ○ Light      │    │  • UniversalQuickCreate         │   │
│  │    ● Dark       │    │  (All use shared theme utils)   │   │
│  │  JavaScript WR  │    │  Apply theme via FluentProvider │   │
│  └──────────────────┘    └─────────────────────────────────┘   │
│                                      │                          │
│                                      ▼                          │
│           ┌─────────────────────────────────────────┐          │
│           │  Spaarke.UI.Components (Shared Library) │          │
│           │  ─────────────────────────────────────  │          │
│           │  src/utils/themeStorage.ts              │          │
│           │  - getUserThemePreference()             │          │
│           │  - setUserThemePreference()             │          │
│           │  - getEffectiveDarkMode()               │          │
│           │  - setupThemeListener()                 │          │
│           └─────────────────────────────────────────┘          │
│                                      │                          │
│                                      ▼                          │
│                          ┌─────────────────────┐               │
│                          │  localStorage       │               │
│                          │  ─────────────────  │               │
│                          │  spaarke-theme:     │               │
│                          │  "light"|"dark"|    │               │
│                          │  "auto"|<custom>    │               │
│                          └─────────────────────┘               │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Components

| Component | Type | Purpose |
|-----------|------|---------|
| `sprk_ThemeMenu.js` | JavaScript Web Resource | Command bar menu handler (minimal, invocation only) |
| `sprk_ThemeMenu.svg` | SVG Web Resource | Static menu icon |
| `themeStorage.ts` | Shared Library | Theme persistence and detection utilities |
| PCF Control Updates | TypeScript | All PCF controls updated to use shared theme utilities |
| Ribbon Customization | XML | Command bar menu definition |

### 2.3 ADR Compliance

| ADR | Requirement | Compliance |
|-----|-------------|------------|
| ADR-006 | Prefer PCF over web resources | ✅ Web resource minimal (invocation only); logic in shared library |
| ADR-012 | Shared component library | ✅ Theme utilities added to `Spaarke.UI.Components` |

---

## 3. Detailed Requirements

### 3.1 localStorage Key Specification

| Property | Value |
|----------|-------|
| **Key Name** | `spaarke-theme` |
| **Valid Values** | `"light"`, `"dark"`, `"auto"` (extensible for future custom themes) |
| **Default** | `"auto"` (if key not present) |
| **Persistence** | Indefinite (until cleared by user) |

```typescript
// Storage key constant (in shared library)
export const THEME_STORAGE_KEY = 'spaarke-theme';

// Valid theme values (extensible)
export type ThemePreference = 'light' | 'dark' | 'auto' | string;

// Built-in themes
export const BUILT_IN_THEMES = ['auto', 'light', 'dark'] as const;
```

### 3.2 Command Bar Menu Requirements

#### 3.2.1 Menu Structure

Following the MDA command bar pattern (see "Show As" example), the Theme option uses a **flyout submenu**:

```
Command Bar: [...] (More Commands)
└── Theme >
    ├── 🔄 Auto (follows system)
    ├── ☀️ Light
    └── 🌙 Dark
```

| Element | Description |
|---------|-------------|
| **Parent Menu Item** | "Theme" with chevron indicator (>) showing submenu |
| **Submenu** | Standard flyout with simple button items |
| **Menu Items** | Icon + label; clicking immediately applies theme |
| **Visual Feedback** | Theme change itself is the feedback (no checked states) |

> **Pattern Reference**: See `notes/main-menu-top-level.jpg` and `notes/menu-sub-level.jpg` for the standard Power Platform flyout pattern.

#### 3.2.2 Menu Options

| Option | Label | Icon | Description |
|--------|-------|------|-------------|
| Auto | "Auto (follows system)" | `WeatherSunnyLow` | Uses OS/browser preference |
| Light | "Light" | `WeatherSunny` | Always light mode |
| Dark | "Dark" | `WeatherMoon` | Always dark mode |

> **Future Extensibility**: Additional custom themes (e.g., "High Contrast", "Spaarke Brand") can be added as menu options without architectural changes.

#### 3.2.3 Menu Behavior

1. **Click Action**: Immediately sets the selected theme (no confirmation needed)
2. **Visual Feedback**: PCF controls update instantly; the theme change IS the feedback
3. **No Page Refresh**: Theme applies immediately via storage event
4. **No Alert Dialog**: No disruptive modals; visual change provides feedback
5. **Keyboard Accessible**: Menu navigable via arrow keys, Enter to select
6. **No Checked States**: Menu items don't show selected/unselected indicators (matches Power Platform pattern)

#### 3.2.4 Menu Placement

- **Location**: Main command bar overflow menu ("..." More Commands)
- **Position**: After "Show As" option (logical grouping with display options)
- **Visibility**: Always visible on all forms/views

### 3.3 JavaScript Web Resource

#### 3.3.1 File: `sprk_ThemeMenu.js`

> **ADR-006 Compliance**: This web resource contains **invocation logic only**. All theme detection and storage logic is in the shared component library.

```javascript
/**
 * Theme Menu Command Bar Handler
 *
 * MINIMAL web resource per ADR-006.
 * Only handles ribbon invocation; actual logic in shared library.
 */

var Spaarke = window.Spaarke || {};
Spaarke.Theme = Spaarke.Theme || {};

(function() {
    'use strict';

    const STORAGE_KEY = 'spaarke-theme';
    const VALID_THEMES = ['auto', 'light', 'dark'];

    /**
     * Set theme - called by ribbon menu items
     * @param {string} theme - Theme to set
     */
    Spaarke.Theme.setTheme = function(theme) {
        if (!VALID_THEMES.includes(theme)) {
            console.warn('[Spaarke.Theme] Invalid theme:', theme);
            return;
        }

        localStorage.setItem(STORAGE_KEY, theme);
        console.log('[Spaarke.Theme] Theme set to:', theme);

        // Dispatch custom event for same-tab listeners
        window.dispatchEvent(new CustomEvent('spaarke-theme-change', {
            detail: { theme: theme }
        }));
    };

    /**
     * Check if theme is currently selected (for menu checkmark)
     * @param {string} theme - Theme to check
     * @returns {boolean} True if this theme is selected
     */
    Spaarke.Theme.isSelected = function(theme) {
        const stored = localStorage.getItem(STORAGE_KEY) || 'auto';
        return stored === theme;
    };

    /**
     * Enable rule - always enabled
     */
    Spaarke.Theme.isEnabled = function() {
        return true;
    };

    // Listen for system preference changes (for auto mode)
    if (window.matchMedia) {
        window.matchMedia('(prefers-color-scheme: dark)')
            .addEventListener('change', function(e) {
                const stored = localStorage.getItem(STORAGE_KEY) || 'auto';
                if (stored === 'auto') {
                    window.dispatchEvent(new CustomEvent('spaarke-theme-change', {
                        detail: { theme: 'auto', effectiveDark: e.matches }
                    }));
                }
            });
    }

})();
```

### 3.4 Shared Library: Theme Storage Utilities

#### 3.4.1 File: `src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts`

> **ADR-012 Compliance**: Centralized theme utilities shared by all PCF controls.

```typescript
/**
 * Theme Storage Utilities
 *
 * Centralized theme persistence and detection for all PCF controls.
 * Extends existing themeDetection.ts with localStorage support.
 */

import { Theme, webLightTheme, webDarkTheme } from "@fluentui/react-components";

// ============================================================================
// Constants
// ============================================================================

export const THEME_STORAGE_KEY = 'spaarke-theme';
export const THEME_CHANGE_EVENT = 'spaarke-theme-change';

export type ThemePreference = 'light' | 'dark' | 'auto';

// ============================================================================
// Storage Functions
// ============================================================================

/**
 * Get user's theme preference from localStorage
 * @returns ThemePreference ('auto' if not set)
 */
export function getUserThemePreference(): ThemePreference {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'auto') {
        return stored;
    }
    return 'auto';
}

/**
 * Set user's theme preference in localStorage
 * Dispatches custom event for same-tab listeners
 */
export function setUserThemePreference(theme: ThemePreference): void {
    localStorage.setItem(THEME_STORAGE_KEY, theme);

    window.dispatchEvent(new CustomEvent(THEME_CHANGE_EVENT, {
        detail: { theme }
    }));
}

// ============================================================================
// Theme Resolution
// ============================================================================

/**
 * Get effective dark mode considering all sources
 *
 * Priority:
 * 1. localStorage (user's explicit preference)
 * 2. Power Platform context (fluentDesignLanguage.isDarkTheme)
 * 3. DOM navbar detection (Custom Pages fallback)
 * 4. System preference (OS/browser)
 *
 * @param context - PCF context (optional)
 * @returns true if dark mode should be active
 */
export function getEffectiveDarkMode(context?: any): boolean {
    const preference = getUserThemePreference();

    // Explicit user choice
    if (preference === 'dark') return true;
    if (preference === 'light') return false;

    // Auto mode: check Power Platform context first
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme;
    }

    // Fallback for Custom Pages: check navbar background color
    const navbar = document.querySelector("[data-id='navbar-container']");
    if (navbar) {
        const bg = getComputedStyle(navbar).backgroundColor;
        if (bg === "rgb(10, 10, 10)") return true;   // Dark mode navbar
        if (bg === "rgb(240, 240, 240)") return false; // Light mode navbar
    }

    // Final fallback to system preference
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
}

/**
 * Resolve Fluent UI theme based on effective dark mode
 */
export function resolveThemeWithUserPreference(context?: any): Theme {
    return getEffectiveDarkMode(context) ? webDarkTheme : webLightTheme;
}

// ============================================================================
// Event Listeners
// ============================================================================

export interface ThemeChangeHandler {
    (isDark: boolean): void;
}

/**
 * Set up theme change listeners for PCF controls
 *
 * Listens for:
 * - localStorage changes from other tabs
 * - Custom events from same-tab theme menu
 * - System preference changes (for auto mode)
 *
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(
    onChange: ThemeChangeHandler,
    context?: any
): () => void {
    const handleStorageChange = (event: StorageEvent) => {
        if (event.key === THEME_STORAGE_KEY) {
            onChange(getEffectiveDarkMode(context));
        }
    };

    const handleThemeEvent = () => {
        onChange(getEffectiveDarkMode(context));
    };

    const handleSystemChange = (event: MediaQueryListEvent) => {
        if (getUserThemePreference() === 'auto') {
            onChange(event.matches);
        }
    };

    // Add listeners
    window.addEventListener('storage', handleStorageChange);
    window.addEventListener(THEME_CHANGE_EVENT, handleThemeEvent);

    const mediaQuery = window.matchMedia?.('(prefers-color-scheme: dark)');
    mediaQuery?.addEventListener('change', handleSystemChange);

    // Return cleanup function
    return () => {
        window.removeEventListener('storage', handleStorageChange);
        window.removeEventListener(THEME_CHANGE_EVENT, handleThemeEvent);
        mediaQuery?.removeEventListener('change', handleSystemChange);
    };
}
```

### 3.5 PCF Control Updates

All PCF controls must be updated to use the shared theme utilities.

#### 3.5.1 Controls Requiring Updates

| Control | Location | Current Theme Implementation | Required Changes |
|---------|----------|------------------------------|------------------|
| **SpeFileViewer** | `src/client/pcf/SpeFileViewer/` | Internal `themeOverride` state | Replace with shared `themeStorage` utilities |
| **UniversalDatasetGrid** | `src/client/pcf/UniversalDatasetGrid/` | `resolveTheme()` in local ThemeProvider | Update to use shared library with localStorage |
| **UniversalQuickCreate** | `src/client/pcf/UniversalQuickCreate/` | No theme detection | Add theme support using shared library |

#### 3.5.2 Theme Detection Priority

PCF controls must check theme in this order (implemented in shared library):

1. **localStorage** (`spaarke-theme`) - User's explicit preference
2. **Power Platform Context** (`context.fluentDesignLanguage?.isDarkTheme`) - App setting
3. **DOM Navbar Detection** - Custom Pages fallback (checks navbar background color)
4. **System Preference** (`prefers-color-scheme`) - Browser/OS setting

#### 3.5.3 Implementation Pattern for PCF Controls

```typescript
// In index.ts - import shared utilities
import {
    setupThemeListener,
    resolveThemeWithUserPreference,
    getEffectiveDarkMode
} from '@spaarke/ui-components/utils/themeStorage';

export class MyPcfControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private cleanupThemeListener: (() => void) | null = null;

    public init(context, notifyOutputChanged, state, container): void {
        // Set up theme listener
        this.cleanupThemeListener = setupThemeListener(
            (isDark) => this.onThemeChange(isDark),
            context
        );

        // Initial render with correct theme
        this.renderWithTheme(context);
    }

    private onThemeChange(isDark: boolean): void {
        // Trigger re-render when theme changes
        this.renderWithTheme(this._context);
    }

    private renderWithTheme(context: any): void {
        const theme = resolveThemeWithUserPreference(context);

        this.root.render(
            React.createElement(FluentProvider, { theme },
                React.createElement(MyComponent, { /* props */ })
            )
        );
    }

    public destroy(): void {
        // Clean up theme listener
        if (this.cleanupThemeListener) {
            this.cleanupThemeListener();
            this.cleanupThemeListener = null;
        }
    }
}
```

#### 3.5.4 SpeFileViewer Specific Changes

The existing `FilePreview.tsx` has an internal theme toggle button and `themeOverride` state. This should be:

1. **Removed**: Internal theme toggle button in `FilePreview.tsx`
2. **Removed**: Component-local `themeOverride` state
3. **Added**: Use shared `getEffectiveDarkMode()` for theme detection
4. **Added**: Theme listener in `index.ts` to re-render on theme change

> **Breaking Change**: The per-control theme toggle is replaced by the global command bar menu. This is intentional - users should have ONE place to change theme, not multiple toggle buttons.

### 3.6 Ribbon/Command Bar Configuration

#### 3.6.1 Ribbon XML (via Ribbon Workbench)

> **Note**: This uses the standard Power Platform flyout submenu pattern (like "Show As").
> Menu items are simple buttons - selection is immediate on click; the theme change itself provides visual feedback.
> See reference: `notes/main-menu-top-level.jpg` and `notes/menu-sub-level.jpg`

```xml
<RibbonDiffXml>
  <CustomActions>
    <!-- Theme Flyout Menu in More Commands -->
    <CustomAction Id="sprk.ThemeMenu.FlyoutAnchor.CustomAction"
                  Location="Mscrm.GlobalTab.MainTab.More._children"
                  Sequence="1000">
      <CommandUIDefinition>
        <FlyoutAnchor Id="sprk.ThemeMenu.FlyoutAnchor"
                      Command="sprk.ThemeMenu.FlyoutCommand"
                      LabelText="$LocLabels:sprk.ThemeMenu.Label"
                      ToolTipTitle="$LocLabels:sprk.ThemeMenu.TooltipTitle"
                      ToolTipDescription="$LocLabels:sprk.ThemeMenu.TooltipDesc"
                      Image16by16="$webresource:sprk_ThemeMenu16.svg"
                      Image32by32="$webresource:sprk_ThemeMenu32.svg"
                      PopulateDynamically="false"
                      PopulateQueryCommand=""
                      TemplateAlias="o1">
          <Menu Id="sprk.ThemeMenu.Menu">
            <MenuSection Id="sprk.ThemeMenu.MenuSection"
                         Title="$LocLabels:sprk.ThemeMenu.SectionTitle"
                         Sequence="10">
              <Controls Id="sprk.ThemeMenu.Controls">
                <!-- Auto Option -->
                <Button Id="sprk.ThemeMenu.Auto"
                        Command="sprk.ThemeMenu.SetAuto"
                        LabelText="$LocLabels:sprk.ThemeMenu.Auto"
                        Image16by16="$webresource:sprk_ThemeAuto16.svg"
                        ToolTipTitle="$LocLabels:sprk.ThemeMenu.Auto"
                        ToolTipDescription="$LocLabels:sprk.ThemeMenu.AutoDesc"
                        Sequence="10" />
                <!-- Light Option -->
                <Button Id="sprk.ThemeMenu.Light"
                        Command="sprk.ThemeMenu.SetLight"
                        LabelText="$LocLabels:sprk.ThemeMenu.Light"
                        Image16by16="$webresource:sprk_ThemeLight16.svg"
                        ToolTipTitle="$LocLabels:sprk.ThemeMenu.Light"
                        ToolTipDescription="$LocLabels:sprk.ThemeMenu.LightDesc"
                        Sequence="20" />
                <!-- Dark Option -->
                <Button Id="sprk.ThemeMenu.Dark"
                        Command="sprk.ThemeMenu.SetDark"
                        LabelText="$LocLabels:sprk.ThemeMenu.Dark"
                        Image16by16="$webresource:sprk_ThemeDark16.svg"
                        ToolTipTitle="$LocLabels:sprk.ThemeMenu.Dark"
                        ToolTipDescription="$LocLabels:sprk.ThemeMenu.DarkDesc"
                        Sequence="30" />
              </Controls>
            </MenuSection>
          </Menu>
        </FlyoutAnchor>
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <!-- Flyout parent command -->
    <CommandDefinition Id="sprk.ThemeMenu.FlyoutCommand">
      <EnableRules>
        <EnableRule Id="sprk.AlwaysEnabled" />
      </EnableRules>
      <DisplayRules />
      <Actions />
    </CommandDefinition>

    <!-- Set Auto Theme -->
    <CommandDefinition Id="sprk.ThemeMenu.SetAuto">
      <EnableRules>
        <EnableRule Id="sprk.AlwaysEnabled" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js"
                            FunctionName="Spaarke.Theme.setTheme">
          <StringParameter Value="auto" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

    <!-- Set Light Theme -->
    <CommandDefinition Id="sprk.ThemeMenu.SetLight">
      <EnableRules>
        <EnableRule Id="sprk.AlwaysEnabled" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js"
                            FunctionName="Spaarke.Theme.setTheme">
          <StringParameter Value="light" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

    <!-- Set Dark Theme -->
    <CommandDefinition Id="sprk.ThemeMenu.SetDark">
      <EnableRules>
        <EnableRule Id="sprk.AlwaysEnabled" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_ThemeMenu.js"
                            FunctionName="Spaarke.Theme.setTheme">
          <StringParameter Value="dark" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <EnableRules>
      <EnableRule Id="sprk.AlwaysEnabled">
        <CustomRule Library="$webresource:sprk_ThemeMenu.js"
                    FunctionName="Spaarke.Theme.isEnabled"
                    Default="true" />
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>

  <LocLabels>
    <LocLabel Id="sprk.ThemeMenu.Label">
      <Titles><Title languagecode="1033" description="Theme" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.TooltipTitle">
      <Titles><Title languagecode="1033" description="Select Theme" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.TooltipDesc">
      <Titles><Title languagecode="1033" description="Choose your preferred color theme" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.SectionTitle">
      <Titles><Title languagecode="1033" description="Color Theme" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.Auto">
      <Titles><Title languagecode="1033" description="Auto (follows system)" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.AutoDesc">
      <Titles><Title languagecode="1033" description="Uses your operating system's theme preference" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.Light">
      <Titles><Title languagecode="1033" description="Light" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.LightDesc">
      <Titles><Title languagecode="1033" description="Light background with dark text" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.Dark">
      <Titles><Title languagecode="1033" description="Dark" /></Titles>
    </LocLabel>
    <LocLabel Id="sprk.ThemeMenu.DarkDesc">
      <Titles><Title languagecode="1033" description="Dark background with light text" /></Titles>
    </LocLabel>
  </LocLabels>
</RibbonDiffXml>
```

---

## 4. User Experience

### 4.1 First-Time User Flow

```
1. User opens model-driven app
2. Theme defaults to "Auto" (follows system preference)
3. PCF controls render based on system dark/light mode
4. User sees "Theme" button in command bar with sun/moon icon
```

### 4.2 Theme Toggle Flow

```
1. User clicks "Theme" button in command bar
2. Theme cycles: Auto → Dark → Light → Auto
3. localStorage updated immediately
4. Custom event dispatched to same-tab listeners
5. PCF controls receive event and re-render with new theme
6. Optional: Brief toast notification shows new theme
7. No page refresh required
```

### 4.3 Cross-Tab Behavior

```
1. User has app open in two browser tabs
2. User changes theme in Tab 1
3. localStorage fires 'storage' event in Tab 2
4. Tab 2 PCF controls receive event and update theme
5. Both tabs now show consistent theme
```

### 4.4 Session Persistence

```
1. User sets theme to "Dark"
2. User closes browser
3. User reopens app next day
4. localStorage still contains 'spaarke-theme: dark'
5. App immediately renders in dark mode
```

---

## 5. Implementation Tasks

### 5.1 Task Breakdown

| # | Task | Effort | Dependencies |
|---|------|--------|--------------|
| 1 | Create shared `themeStorage.ts` in `Spaarke.UI.Components` | 2h | None |
| 2 | Create `sprk_ThemeMenu.js` web resource (minimal) | 1h | Task 1 |
| 3 | Create SVG icons (menu, auto, light, dark - 16x16 each) | 1h | None |
| 4 | Configure command bar flyout menu via Ribbon Workbench | 3h | Task 2, 3 |
| 5 | Update **SpeFileViewer** PCF to use shared theme utilities | 3h | Task 1 |
| 6 | Update **UniversalDatasetGrid** PCF to use shared theme utilities | 2h | Task 1 |
| 7 | Update **UniversalQuickCreate** PCF to add theme support | 2h | Task 1 |
| 8 | Update PCF architecture documentation (`sdap-pcf-patterns.md`) | 1h | Task 1 |
| 9 | Integration testing (all controls) | 3h | All above |
| 10 | Deploy to DEV environment | 1h | All above |

**Total Estimated Effort**: ~19 hours

### 5.2 Files to Create

```
src/
├── client/
│   ├── shared/
│   │   └── Spaarke.UI.Components/
│   │       └── src/
│   │           └── utils/
│   │               ├── themeStorage.ts       # NEW: Theme persistence utilities
│   │               └── __tests__/
│   │                   └── themeStorage.test.ts  # NEW: Unit tests
│   ├── webresources/
│   │   └── js/
│   │       └── sprk_ThemeMenu.js             # NEW: Minimal ribbon handler
│   └── assets/
│       └── icons/
│           ├── sprk_ThemeMenu16.svg          # NEW: Menu icon
│           ├── sprk_ThemeAuto16.svg          # NEW: Auto option icon
│           ├── sprk_ThemeLight16.svg         # NEW: Light option icon
│           └── sprk_ThemeDark16.svg          # NEW: Dark option icon
└── solutions/
    └── SpaarkeCore/
        └── Ribbon/
            └── ApplicationRibbon.xml          # MODIFY: Add theme menu
```

### 5.3 Files to Modify

```
src/client/pcf/SpeFileViewer/control/
├── index.ts          # Add theme listener, use shared utilities
├── FilePreview.tsx   # REMOVE internal theme toggle button and themeOverride state
└── package.json      # Add dependency on @spaarke/ui-components

src/client/pcf/UniversalDatasetGrid/control/
├── index.ts          # Add theme listener, use shared utilities
├── providers/ThemeProvider.ts   # Update to use shared themeStorage
└── package.json      # Verify/add dependency on @spaarke/ui-components

src/client/pcf/UniversalQuickCreate/control/
├── index.ts          # Add FluentProvider with theme, add listener
└── package.json      # Add dependency on @spaarke/ui-components

src/client/shared/Spaarke.UI.Components/
├── src/utils/index.ts    # Export new themeStorage utilities
└── package.json          # Version bump

docs/ai-knowledge/architecture/
└── sdap-pcf-patterns.md  # Add theming section
```

### 5.4 Documentation Updates Required

| Document | Changes |
|----------|---------|
| `sdap-pcf-patterns.md` | Add "Theme Support" section with implementation pattern |
| `Spaarke.UI.Components/README.md` | Document themeStorage utilities |
| PCF control READMEs | Note theme support and shared library dependency |

---

## 6. Testing Requirements

### 6.1 Unit Tests (themeStorage.ts)

| Test Case | Expected Result |
|-----------|-----------------|
| `getUserThemePreference()` with no localStorage | Returns `'auto'` |
| `getUserThemePreference()` with valid value | Returns stored value |
| `getUserThemePreference()` with invalid value | Returns `'auto'` |
| `setUserThemePreference('dark')` | localStorage contains `'dark'`, event dispatched |
| `getEffectiveDarkMode()` when `'dark'` | Returns `true` |
| `getEffectiveDarkMode()` when `'light'` | Returns `false` |
| `getEffectiveDarkMode()` when `'auto'` + system dark | Returns `true` |
| `getEffectiveDarkMode()` when `'auto'` + system light | Returns `false` |
| `getEffectiveDarkMode()` when `'auto'` + Platform context dark | Returns `true` |
| `setupThemeListener()` returns cleanup function | Function cleans up all listeners |

### 6.2 Integration Tests

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| Theme menu in command bar | Open (...) menu, click Theme > Dark | All PCF controls update to dark theme without refresh |
| Cross-tab sync | Change theme in tab 1 via menu | Tab 2 updates automatically |
| Persistence | Set dark, close browser, reopen | Dark mode persists |
| System preference change | Set auto, change OS to dark | PCF controls update to dark |
| SpeFileViewer theme | Toggle theme | FilePreview renders with correct FluentProvider theme |
| UniversalDatasetGrid theme | Toggle theme | Grid renders with correct theme |
| UniversalQuickCreate theme | Open upload dialog after theme change | Dialog uses correct theme |

### 6.3 Per-Control Tests

| Control | Test Cases |
|---------|------------|
| **SpeFileViewer** | Initial load respects localStorage; theme change re-renders; internal toggle removed |
| **UniversalDatasetGrid** | Grid background/text colors match theme; column headers themed |
| **UniversalQuickCreate** | Upload form uses correct theme; file list styled correctly |

### 6.4 Accessibility Tests

| Test Case | Expected Result |
|-----------|-----------------|
| Keyboard navigation | Theme menu navigable via Tab, Arrow keys, Enter to select |
| Screen reader | Announces "Theme submenu" and option names |
| Color contrast | All themes meet WCAG 2.1 AA contrast requirements |
| Focus indicator | Visible focus ring on menu items |

---

## 7. Constraints and Limitations

### 7.1 Known Limitations

| Limitation | Reason | Mitigation |
|------------|--------|------------|
| Cannot theme SharePoint preview iframe | Cross-origin security, Microsoft API limitation | Display disclaimer; wait for Microsoft support |
| Model-driven app shell stays unchanged | Power Platform controls app chrome | Theme only affects PCF control content |
| No checked state in menu | By design per Fluent V9 pattern (like "Show As" menu) | Theme change itself provides visual feedback; icons differentiate options |
| localStorage is per-browser | No server sync | Document that preference is browser-specific |

### 7.2 Browser Support

| Browser | Supported | Notes |
|---------|-----------|-------|
| Microsoft Edge (Chromium) | Yes | Primary target |
| Google Chrome | Yes | Full support |
| Mozilla Firefox | Yes | Full support |
| Safari | Yes | Full support |
| Internet Explorer | No | Not supported by Power Platform |

---

## 8. Security Considerations

### 8.1 localStorage Security

- **No sensitive data**: Only stores `'light'`, `'dark'`, or `'auto'` string
- **Same-origin policy**: localStorage is isolated to the app domain
- **No PII**: Theme preference contains no personal information
- **XSS mitigation**: Values are validated before use (whitelist approach)

### 8.2 Web Resource Security

- **No external calls**: JavaScript runs locally, no network requests
- **Input validation**: All inputs validated against allowed values
- **Error handling**: Graceful fallback to `'auto'` on any error

---

## 9. Deployment

### 9.1 Solution Components

| Component | Type | Display Name |
|-----------|------|--------------|
| `sprk_ThemeMenu.js` | Web Resource (JS) | Spaarke Theme Menu Script |
| `sprk_ThemeMenu16.svg` | Web Resource (SVG) | Spaarke Theme Menu Icon |
| `sprk_ThemeAuto16.svg` | Web Resource (SVG) | Spaarke Theme Auto Icon |
| `sprk_ThemeLight16.svg` | Web Resource (SVG) | Spaarke Theme Light Icon |
| `sprk_ThemeDark16.svg` | Web Resource (SVG) | Spaarke Theme Dark Icon |
| Application Ribbon | Ribbon Customization | (embedded in solution) |
| SpeFileViewer | PCF Control | Updated with theme support |
| UniversalDatasetGrid | PCF Control | Updated with theme support |
| UniversalQuickCreate | PCF Control | Updated with theme support |

### 9.2 Deployment Steps

1. Update shared component library (`Spaarke.UI.Components`)
2. Build and test updated PCF controls locally
3. Export unmanaged solution from DEV
4. Add web resources to solution
5. Configure ribbon flyout menu via Ribbon Workbench
6. Deploy updated PCF controls
7. Import solution to TEST environment
8. Validate all PCF controls respond to theme changes
9. Promote to PROD

---

## 10. Future Enhancements

| Enhancement | Description | Priority |
|-------------|-------------|----------|
| Custom branded themes | Add "Spaarke" or client-specific themes to menu | Medium |
| High contrast theme | Accessibility-focused high contrast option | Medium |
| Server-side preference sync | Store preference in Dataverse user settings | Low |
| SharePoint preview dark mode | When Microsoft adds API support | Waiting on Microsoft |
| Per-form theme override | Different themes for different forms | Low |

---

## 11. PCF Architecture Standards Update

### 11.1 New PCF Theming Requirement

All PCF controls MUST implement theme support following this pattern:

```typescript
// Required imports
import {
    setupThemeListener,
    resolveThemeWithUserPreference
} from '@spaarke/ui-components/utils/themeStorage';

// Required in init()
this.cleanupThemeListener = setupThemeListener(
    (isDark) => this.onThemeChange(isDark),
    context
);

// Required in destroy()
if (this.cleanupThemeListener) {
    this.cleanupThemeListener();
}
```

### 11.2 Documentation Update: `sdap-pcf-patterns.md`

Add new section:

```markdown
## Theme Support (Required)

All PCF controls must support user theme preferences via the shared theme utilities.

### Implementation Checklist

- [ ] Import `setupThemeListener` and `resolveThemeWithUserPreference` from shared library
- [ ] Call `setupThemeListener()` in `init()` to listen for theme changes
- [ ] Use `resolveThemeWithUserPreference()` to get the correct Fluent UI theme
- [ ] Clean up listener in `destroy()`
- [ ] Wrap component tree in `FluentProvider` with resolved theme

### Theme Priority Order

1. localStorage (`spaarke-theme`) - User's explicit choice
2. Power Platform context (`fluentDesignLanguage.isDarkTheme`) - App setting
3. DOM navbar detection - Custom Pages fallback
4. System preference (`prefers-color-scheme`) - OS/browser setting

### Example

See `SpeFileViewer/control/index.ts` for reference implementation.
```

---

## 12. Icon Compliance for Dark Mode

### 12.1 Design Requirement

All icons used in Spaarke solutions **MUST** support both light and dark mode themes. This is achieved by using `currentColor` in SVG definitions, which inherits the text color from the parent context.

### 12.2 Icon Guidelines

| Requirement | Implementation |
|-------------|----------------|
| **Use `currentColor`** | All fill and stroke attributes must use `currentColor` instead of hardcoded colors |
| **No hardcoded colors** | Avoid `fill="#000000"` or `stroke="white"` - these break in opposite themes |
| **Test both themes** | Verify icon visibility in both light and dark mode before deployment |
| **Prefer Fluent UI icons** | Use `@fluentui/react-icons` when possible - they are theme-aware by default |

### 12.3 SVG Pattern for Theme-Aware Icons

```svg
<!-- ✅ CORRECT: Uses currentColor - works in both themes -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16">
  <circle cx="8" cy="8" r="6" fill="currentColor"/>
  <path d="M8 4v8" stroke="currentColor" stroke-width="1.5"/>
</svg>

<!-- ❌ WRONG: Hardcoded colors - breaks in dark mode -->
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16">
  <circle cx="8" cy="8" r="6" fill="#333333"/>
  <path d="M8 4v8" stroke="black" stroke-width="1.5"/>
</svg>
```

### 12.4 Power Platform Command Bar Icons

Power Platform command bar icons are SVG web resources. Ensure all Spaarke ribbon icons follow these rules:

1. **16x16 and 32x32 sizes** - Both sizes required for command bar
2. **`currentColor` throughout** - No hardcoded fill/stroke colors
3. **Simple paths** - Avoid gradients or complex effects
4. **Monochrome design** - Single color that adapts to theme

### 12.5 Fluent UI Icon Usage in PCF Controls

For icons within PCF React components, prefer Fluent UI icons:

```typescript
// ✅ CORRECT: Fluent UI icons are theme-aware
import { WeatherMoon24Regular, WeatherSunny24Regular } from "@fluentui/react-icons";

<Button icon={isDark ? <WeatherMoon24Regular /> : <WeatherSunny24Regular />} />

// ❌ AVOID: Custom inline SVGs unless necessary
<Button icon={<svg>...</svg>} />
```

### 12.6 Validation Checklist

Before deploying any icon:

- [ ] Icon uses `currentColor` for all fill/stroke attributes
- [ ] Icon is visible on white background (light mode)
- [ ] Icon is visible on dark background (dark mode)
- [ ] Icon maintains semantic meaning in both themes
- [ ] For ribbon icons: Both 16x16 and 32x32 versions exist

---

## Appendix A: Icon Design

> **Note**: All icons below use `currentColor` for theme compliance per Section 12.

### Theme Menu Icon (Static)

A combined sun/moon icon representing theme options:

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" width="16" height="16">
  <!-- Half sun, half moon design -->
  <defs>
    <clipPath id="half">
      <rect x="0" y="0" width="8" height="16"/>
    </clipPath>
  </defs>
  <!-- Sun half (left) -->
  <g clip-path="url(#half)">
    <circle cx="8" cy="8" r="3" fill="currentColor"/>
    <g stroke="currentColor" stroke-width="1.5" stroke-linecap="round">
      <line x1="8" y1="1" x2="8" y2="3"/>
      <line x1="3" y1="3" x2="4.5" y2="4.5"/>
      <line x1="1" y1="8" x2="3" y2="8"/>
      <line x1="3" y1="13" x2="4.5" y2="11.5"/>
      <line x1="8" y1="15" x2="8" y2="13"/>
    </g>
  </g>
  <!-- Moon half (right) -->
  <path d="M8 2a6 6 0 1 0 0 12 5 5 0 0 1 0-12z" fill="currentColor"/>
</svg>
```

### Auto Option Icon

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" width="16" height="16">
  <circle cx="8" cy="8" r="6" stroke="currentColor" stroke-width="1.5" fill="none"/>
  <path d="M8 2v12" stroke="currentColor" stroke-width="1.5"/>
  <path d="M8 2a6 6 0 0 1 0 12" fill="currentColor"/>
</svg>
```

### Light Option Icon (Sun)

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" width="16" height="16">
  <circle cx="8" cy="8" r="3" fill="currentColor"/>
  <g stroke="currentColor" stroke-width="1.5" stroke-linecap="round">
    <line x1="8" y1="1" x2="8" y2="3"/>
    <line x1="8" y1="13" x2="8" y2="15"/>
    <line x1="3" y1="3" x2="4.5" y2="4.5"/>
    <line x1="11.5" y1="11.5" x2="13" y2="13"/>
    <line x1="1" y1="8" x2="3" y2="8"/>
    <line x1="13" y1="8" x2="15" y2="8"/>
    <line x1="3" y1="13" x2="4.5" y2="11.5"/>
    <line x1="11.5" y1="4.5" x2="13" y2="3"/>
  </g>
</svg>
```

### Dark Option Icon (Moon)

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" width="16" height="16">
  <path fill="currentColor" d="M14 10.5A6.5 6.5 0 1 1 5.5 2 5.5 5.5 0 0 0 14 10.5z"/>
</svg>
```

---

## Appendix B: Related Documents

- [ADR-006: PCF over Web Resources](../docs/reference/adr/ADR-006-prefer-pcf-over-webresources.md)
- [ADR-012: Shared Component Library](../docs/reference/adr/ADR-012-shared-component-library.md)
- [SDAP PCF Patterns](../docs/ai-knowledge/architecture/sdap-pcf-patterns.md)
- [Existing themeDetection.ts](../src/client/shared/Spaarke.UI.Components/src/utils/themeDetection.ts)

---

## Appendix C: Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2025-12-05 | Use flyout menu instead of toggle button | Matches MDA command bar pattern (Show As); better UX for future theme additions |
| 2025-12-05 | No alert dialog on theme change | Visual change is sufficient feedback; dialogs are disruptive |
| 2025-12-05 | Shared library for theme utilities | ADR-012 compliance; avoids code duplication across PCF controls |
| 2025-12-05 | Remove SpeFileViewer internal toggle | One global theme control, not per-control toggles |
| 2025-12-05 | localStorage over server storage | Immediate application; no API latency; user privacy |
| 2025-12-05 | Extensible theme values | Future custom themes can be added without spec changes |

---

## Appendix D: Open Questions

| Question | Status | Resolution |
|----------|--------|------------|
| Should we include ToggleButton checked state for menu items? | **Resolved** | Using standard Button elements per Power Platform flyout pattern (like "Show As"). No checked states needed - theme change itself provides visual feedback. |
| Integration with Power Platform tenant-level theming? | **Deferred** | If Microsoft releases tenant theming, we may need to adjust priority order. |

---

*End of Specification*
