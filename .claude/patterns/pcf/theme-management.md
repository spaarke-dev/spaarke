# Theme Management Pattern

> **Domain**: PCF / Theming & Dark Mode
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-012

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/client/pcf/UniversalDatasetGrid/control/providers/ThemeProvider.ts` | Extracted theme utilities |
| `src/client/pcf/UniversalQuickCreate/control/index.ts` | Inline theme resolution |
| `src/client/pcf/AnalysisWorkspace/control/index.ts` | Theme with listener |

---

## Theme Resolution Priority

```typescript
import { webLightTheme, webDarkTheme, Theme } from "@fluentui/react-components";

function resolveTheme(context?: ComponentFramework.Context<IInputs>): Theme {
    const preference = getUserThemePreference(); // localStorage

    // 1. User explicit choice
    if (preference === 'dark') return webDarkTheme;
    if (preference === 'light') return webLightTheme;

    // 2. URL flag (Power Apps dark mode parameter)
    const urlDark = detectDarkModeFromUrl();
    if (urlDark !== null) return urlDark ? webDarkTheme : webLightTheme;

    // 3. PCF context (fluentDesignLanguage)
    if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
        return context.fluentDesignLanguage.isDarkTheme ? webDarkTheme : webLightTheme;
    }

    // 4. Navbar DOM detection (Custom Page fallback)
    const navbarDark = detectDarkModeFromNavbar();
    if (navbarDark !== null) return navbarDark ? webDarkTheme : webLightTheme;

    // 5. System preference
    return window.matchMedia('(prefers-color-scheme: dark)').matches
        ? webDarkTheme
        : webLightTheme;
}
```

---

## User Preference Storage

```typescript
const THEME_STORAGE_KEY = 'spaarke-theme';
type ThemePreference = 'auto' | 'light' | 'dark';

function getUserThemePreference(): ThemePreference {
    return (localStorage.getItem(THEME_STORAGE_KEY) as ThemePreference) || 'auto';
}

function setUserThemePreference(preference: ThemePreference): void {
    localStorage.setItem(THEME_STORAGE_KEY, preference);
    window.dispatchEvent(new CustomEvent('spaarke-theme-change'));
}
```

---

## Dynamic Theme Listener

```typescript
function setupThemeListener(
    callback: (isDark: boolean) => void,
    context?: ComponentFramework.Context<IInputs>
): () => void {
    // Custom event from theme toggle
    const handleThemeChange = () => callback(getEffectiveDarkMode(context));
    window.addEventListener('spaarke-theme-change', handleThemeChange);

    // System preference changes
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    mediaQuery.addEventListener('change', handleThemeChange);

    // Return cleanup function
    return () => {
        window.removeEventListener('spaarke-theme-change', handleThemeChange);
        mediaQuery.removeEventListener('change', handleThemeChange);
    };
}
```

---

## Detection Utilities

### URL Parameter Detection
```typescript
function detectDarkModeFromUrl(): boolean | null {
    const params = new URLSearchParams(window.location.search);
    const flags = params.get('flags');
    if (flags?.includes('themeOption=dark')) return true;
    if (flags?.includes('themeOption=light')) return false;
    return null;
}
```

### Navbar Color Detection
```typescript
function detectDarkModeFromNavbar(): boolean | null {
    const navbar = document.querySelector('[data-id="navbar-container"]');
    if (!navbar) return null;
    const bgColor = window.getComputedStyle(navbar).backgroundColor;
    const rgb = bgColor.match(/\d+/g)?.map(Number) || [];
    if (rgb.length < 3) return null;
    const luminance = (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255;
    return luminance < 0.5; // Dark if low luminance
}
```

---

## Usage in PCF Control

```typescript
export class MyControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
    private _theme: Theme = webLightTheme;
    private _cleanupThemeListener?: () => void;

    public init(context, notifyOutputChanged, state, container): void {
        this._theme = resolveTheme(context);
        this._cleanupThemeListener = setupThemeListener((isDark) => {
            this._theme = isDark ? webDarkTheme : webLightTheme;
            this.renderComponent();
        }, context);
        // ...
    }

    public destroy(): void {
        this._cleanupThemeListener?.();
        // ...
    }

    private renderComponent(): void {
        this._root?.render(
            React.createElement(FluentProvider, { theme: this._theme }, /* ... */)
        );
    }
}
```

---

## Key Constants

```typescript
const THEME_STORAGE_KEY = 'spaarke-theme';
const THEME_CHANGE_EVENT = 'spaarke-theme-change';
```

---

## Related Patterns

- [Control Initialization](control-initialization.md) - FluentProvider wrapper
- [PCF Constraints](../../constraints/pcf.md) - Theme requirements

---

**Lines**: ~120
