# Theme Consistency Constraints

> **Domain**: UI / Theming
> **Source ADRs**: ADR-021, ADR-012
> **Last Updated**: 2026-03-30

---

## Purpose

Mandatory rules for theme resolution, storage, and change detection across ALL Spaarke UI surfaces. Ensures consistent dark mode behavior after the R2 consolidation.

---

## MUST

- **MUST** use `@spaarke/ui-components` theme utilities — never implement local theme detection
- **MUST** use localStorage key `spaarke-theme` exclusively (exported as `THEME_STORAGE_KEY`)
- **MUST** use the unified priority chain for theme resolution (see below)
- **MUST** default to **light theme** when no preference is found
- **MUST** listen for theme changes via `setupThemeListener` (PCF) or `setupCodePageThemeListener` (Code Pages)
- **MUST** clean up theme listeners in `destroy()` (PCF) or `useEffect` cleanup (React)
- **MUST** use Fluent UI v9 `FluentProvider` with resolved theme
- **MUST** use semantic design tokens — never hard-code colors

## MUST NOT

- **MUST NOT** consult OS `prefers-color-scheme` for theme resolution
- **MUST NOT** add `window.matchMedia('(prefers-color-scheme: dark)')` listeners
- **MUST NOT** inline theme detection logic — import from shared library
- **MUST NOT** use localStorage keys other than `spaarke-theme` (except Office Add-ins)
- **MUST NOT** define local `getUserThemePreference` or `getEffectiveDarkMode` functions
- **MUST NOT** block rendering for Dataverse theme sync (always async)

---

## Unified Priority Chain

### PCF Controls (have ComponentFramework context)

```
1. localStorage `spaarke-theme` (user's explicit preference)
2. PCF context.fluentDesignLanguage.isDarkTheme
3. Navbar DOM background color detection
4. Default: light
```

**Function**: `getEffectiveDarkMode(context)` or `resolveThemeWithUserPreference(context)`

### Code Pages (no PCF context)

```
1. localStorage `spaarke-theme` (user's explicit preference)
2. URL `flags` parameter (themeOption=dark|light)
3. Navbar DOM background color detection (current + parent frame)
4. Default: light
```

**Function**: `resolveCodePageTheme()`

### Office Add-ins (EXCEPTION)

Office Add-ins use `sessionStorage` (not localStorage) and `Office.context.officeTheme`. This is an intentional isolation — Office controls its own theming.

---

## Code Patterns

### PCF Control (Deep Import Required)

```typescript
// ✅ CORRECT: Deep import for PCF (avoids Lexical/jsx-runtime)
import {
  getEffectiveDarkMode,
  setupThemeListener,
  ThemeChangeHandler,
} from "@spaarke/ui-components/dist/utils/themeStorage";
import { webLightTheme, webDarkTheme } from "@fluentui/react-components";

export class MyControl implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private _cleanupThemeListener?: () => void;
  private _isDark = false;

  public init(context: ComponentFramework.Context<IInputs>): void {
    this._isDark = getEffectiveDarkMode(context);
    this._cleanupThemeListener = setupThemeListener((isDark) => {
      this._isDark = isDark;
      this.renderComponent();
    }, context);
  }

  public destroy(): void {
    this._cleanupThemeListener?.();
  }
}
```

### Code Page (Barrel Import OK)

```typescript
// ✅ CORRECT: Barrel import for Code Pages (React 19, jsx-runtime available)
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components";
import { FluentProvider } from "@fluentui/react-components";

const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  React.useEffect(() => {
    const cleanup = setupCodePageThemeListener(setTheme);
    return cleanup;
  }, []);

  return <FluentProvider theme={theme}>...</FluentProvider>;
};
```

### Dataverse Theme Sync (Optional, Async)

```typescript
// Call on page load (after render) for cross-device sync
import {
  syncThemeFromDataverse,
  persistThemeToDataverse,
} from "@spaarke/ui-components";

// Sync on load (non-blocking)
React.useEffect(() => {
  if (webApi && userId) {
    syncThemeFromDataverse(webApi, userId);
  }
}, []);

// Persist on change (non-blocking)
function handleThemeChange(theme: ThemePreference) {
  setUserThemePreference(theme); // localStorage first (instant)
  persistThemeToDataverse(webApi, userId, theme); // Dataverse async
}
```

---

## Available Exports from `themeStorage.ts`

| Export | Surface | Purpose |
|--------|---------|---------|
| `getUserThemePreference()` | All | Read localStorage preference |
| `setUserThemePreference(theme)` | All | Write localStorage + dispatch event |
| `getEffectiveDarkMode(context?)` | PCF | Resolve dark mode boolean |
| `resolveThemeWithUserPreference(context?)` | PCF | Resolve Fluent Theme object |
| `resolveCodePageTheme()` | Code Pages | Resolve Fluent Theme (no context) |
| `setupThemeListener(onChange, context?)` | PCF | Listen for theme changes |
| `setupCodePageThemeListener(onChange)` | Code Pages | Listen for theme changes |
| `detectDarkModeFromUrl()` | Code Pages | URL flag detection |
| `detectDarkModeFromNavbar()` | All | Navbar DOM color detection |
| `syncThemeFromDataverse(webApi, userId)` | All (Xrm) | Async load from Dataverse |
| `persistThemeToDataverse(webApi, userId, theme)` | All (Xrm) | Async save to Dataverse |
| `THEME_STORAGE_KEY` | All | localStorage key constant |
| `THEME_CHANGE_EVENT` | All | Custom event name constant |
| `PREFERENCE_TYPE_THEME` | All (Xrm) | Dataverse option set value |

---

## Related

- [ADR-021](../adr/ADR-021-fluent-design-system.md) — Fluent v9, dark mode
- [ADR-012](../adr/ADR-012-shared-components.md) — Shared component library
- [PCF Theme Management Pattern](../patterns/pcf/theme-management.md) — PCF-specific pattern

---

**Lines**: ~130
