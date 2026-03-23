/**
 * Code Page Theme Detection
 *
 * Theme resolution for standalone Code Page wrappers (HTML pages loaded via
 * Xrm.Navigation.navigateTo). Unlike PCF controls, Code Pages do NOT have a
 * ComponentFramework context, so theme detection relies on a 3-level cascade:
 *
 * 1. localStorage (`spaarke-theme` key) - user's explicit preference
 * 2. URL `flags` param with `themeOption=dark|light`
 * 3. Navbar DOM color detection (Dataverse model-driven app context)
 *
 * When no preference is found, light theme is used as the default.
 * OS-level `prefers-color-scheme` is intentionally NOT consulted — ADR-021
 * requires the Spaarke theme system (not the OS) to control all UI surfaces.
 *
 * @see ADR-021 - Fluent UI v9 Design System (dark mode required)
 * @see ADR-012 - Shared component library
 */

import { Theme, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import {
  getUserThemePreference,
  THEME_STORAGE_KEY,
  THEME_CHANGE_EVENT,
} from './themeStorage';

// ============================================================================
// URL-based Detection
// ============================================================================

/**
 * Detect dark mode from URL `flags` parameter.
 *
 * Code Pages opened via `Xrm.Navigation.navigateTo` can receive a `flags`
 * query parameter containing `themeOption=dark` or `themeOption=light`.
 *
 * @returns `true` if dark, `false` if light, `null` if not specified
 *
 * @example
 * ```typescript
 * // URL: ?flags=themeOption%3Ddark%26otherFlag%3Dtrue
 * const isDark = detectDarkModeFromUrl(); // true
 *
 * // URL: ?flags=themeOption%3Dlight
 * const isDark = detectDarkModeFromUrl(); // false
 *
 * // URL: ?someOtherParam=value (no flags or no themeOption)
 * const isDark = detectDarkModeFromUrl(); // null
 * ```
 */
export function detectDarkModeFromUrl(): boolean | null {
  try {
    const params = new URLSearchParams(window.location.search);

    // Check `flags` param (URL-encoded key=value pairs separated by &)
    const flags = params.get('flags');
    if (flags) {
      const decoded = decodeURIComponent(flags);
      const flagPairs = new URLSearchParams(decoded);
      const themeOption = flagPairs.get('themeOption');
      if (themeOption === 'dark') return true;
      if (themeOption === 'light') return false;
    }

    // Also check a direct `theme` param as a convenience fallback
    const directTheme = params.get('theme');
    if (directTheme === 'dark') return true;
    if (directTheme === 'light') return false;
  } catch {
    // URL parsing failed
  }
  return null;
}

// ============================================================================
// Navbar DOM Detection
// ============================================================================

/**
 * Detect dark mode from the Dataverse navbar background color.
 *
 * When a Code Page is embedded in a model-driven app, the parent frame
 * contains a navbar element whose computed background color indicates the
 * current Dataverse theme. This is a reliable fallback when no explicit
 * user preference or URL parameter is available.
 *
 * @returns `true` if dark, `false` if light, `null` if navbar not found or color unrecognized
 *
 * @example
 * ```typescript
 * // Inside a Dataverse dark-mode app:
 * const isDark = detectDarkModeFromNavbar(); // true
 *
 * // Outside Dataverse (no navbar element):
 * const isDark = detectDarkModeFromNavbar(); // null
 * ```
 */
export function detectDarkModeFromNavbar(): boolean | null {
  try {
    // Check current document first
    let navbar = document.querySelector('[data-id="navbar-container"]');

    // Try parent frame if in an iframe (Code Pages often run in iframes)
    if (!navbar && window.parent && window.parent !== window) {
      try {
        navbar = window.parent.document.querySelector('[data-id="navbar-container"]');
      } catch {
        // Cross-origin access denied — expected in some environments
      }
    }

    if (navbar) {
      const bgColor = window.getComputedStyle(navbar).backgroundColor;
      // Known Dataverse navbar colors
      if (bgColor === 'rgb(10, 10, 10)') return true; // Dark mode
      if (bgColor === 'rgb(240, 240, 240)') return false; // Light mode

      // Luminance-based fallback for custom navbar colors
      const match = bgColor.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
      if (match) {
        const r = parseInt(match[1], 10);
        const g = parseInt(match[2], 10);
        const b = parseInt(match[3], 10);
        // Relative luminance approximation (ITU-R BT.601)
        const luminance = 0.299 * r + 0.587 * g + 0.114 * b;
        return luminance < 128;
      }
    }
  } catch {
    // DOM access failed
  }
  return null;
}

// ============================================================================
// Theme Resolution
// ============================================================================

/**
 * Resolve the Fluent UI v9 theme for a Code Page using the 3-level cascade.
 *
 * Priority:
 * 1. **localStorage** (`spaarke-theme` key) - user's explicit dark/light choice
 * 2. **URL flags** (`flags` param with `themeOption=dark|light`)
 * 3. **Navbar DOM** - reads Dataverse navbar background-color luminance
 *
 * Falls back to **light theme** when no preference is found.
 * OS `prefers-color-scheme` is NOT consulted — ADR-021 requires the Spaarke
 * theme system to control all UI surfaces, not the operating system.
 *
 * This function does NOT require a PCF `ComponentFramework.Context` and is
 * designed exclusively for Code Page wrappers.
 *
 * @returns Fluent UI v9 Theme (`webDarkTheme` or `webLightTheme`)
 *
 * @example
 * ```typescript
 * import { resolveCodePageTheme } from '@spaarke/ui-components';
 * import { FluentProvider } from '@fluentui/react-components';
 *
 * const App: React.FC = () => {
 *   const [theme, setTheme] = React.useState(resolveCodePageTheme);
 *   return <FluentProvider theme={theme}>...</FluentProvider>;
 * };
 * ```
 */
export function resolveCodePageTheme(): Theme {
  try {
    // 1. localStorage user preference
    const preference = getUserThemePreference();
    if (preference === 'dark') return webDarkTheme;
    if (preference === 'light') return webLightTheme;

    // 2. URL flags parameter
    const urlDark = detectDarkModeFromUrl();
    if (urlDark !== null) return urlDark ? webDarkTheme : webLightTheme;

    // 3. Navbar DOM color detection
    const navbarDark = detectDarkModeFromNavbar();
    if (navbarDark !== null) return navbarDark ? webDarkTheme : webLightTheme;

    // Default: light theme (OS prefers-color-scheme is intentionally NOT consulted)
    return webLightTheme;
  } catch {
    // Fallback to light theme on any error
    return webLightTheme;
  }
}

// ============================================================================
// Theme Change Listener
// ============================================================================

/**
 * Callback signature for Code Page theme change events.
 *
 * @param theme - The newly resolved Fluent UI v9 Theme
 */
export type CodePageThemeChangeHandler = (theme: Theme) => void;

/**
 * Set up listeners for theme changes relevant to Code Pages.
 *
 * Listens for:
 * - **localStorage changes** from other tabs (`storage` event on `spaarke-theme` key)
 * - **Same-tab theme changes** (`spaarke-theme-change` custom event from theme menu)
 *
 * OS `prefers-color-scheme` changes are intentionally NOT listened to — ADR-021
 * requires the Spaarke theme system (not the OS) to control all UI surfaces.
 *
 * @param onChange - Callback invoked with the newly resolved theme
 * @returns Cleanup function to remove all listeners
 *
 * @example
 * ```typescript
 * import { resolveCodePageTheme, setupCodePageThemeListener } from '@spaarke/ui-components';
 *
 * const App: React.FC = () => {
 *   const [theme, setTheme] = React.useState(resolveCodePageTheme);
 *
 *   React.useEffect(() => {
 *     const cleanup = setupCodePageThemeListener(setTheme);
 *     return cleanup;
 *   }, []);
 *
 *   return <FluentProvider theme={theme}>...</FluentProvider>;
 * };
 * ```
 */
export function setupCodePageThemeListener(onChange: CodePageThemeChangeHandler): () => void {
  const resolveAndNotify = () => {
    onChange(resolveCodePageTheme());
  };

  // Listen for localStorage changes from other tabs
  const handleStorageChange = (event: StorageEvent) => {
    if (event.key === THEME_STORAGE_KEY) {
      resolveAndNotify();
    }
  };

  // Listen for same-tab custom theme change events
  const handleThemeEvent = () => {
    resolveAndNotify();
  };

  window.addEventListener('storage', handleStorageChange);
  window.addEventListener(THEME_CHANGE_EVENT, handleThemeEvent);

  return () => {
    window.removeEventListener('storage', handleStorageChange);
    window.removeEventListener(THEME_CHANGE_EVENT, handleThemeEvent);
  };
}
