/**
 * Theme Storage Utilities
 *
 * Centralized theme persistence, detection, and change listening for ALL
 * Spaarke UI surfaces — PCF controls AND Code Pages.
 *
 * PCF controls use:
 *   getEffectiveDarkMode(context), resolveThemeWithUserPreference(context),
 *   setupThemeListener(onChange, context)
 *
 * Code Pages use:
 *   resolveCodePageTheme(), setupCodePageThemeListener(onChange)
 *
 * Both share:
 *   getUserThemePreference(), setUserThemePreference(),
 *   detectDarkModeFromUrl(), detectDarkModeFromNavbar()
 *
 * @see ADR-021 - Fluent UI v9 Design System (dark mode, no OS fallback)
 * @see ADR-012 - Shared component library
 */

import { Theme, webLightTheme, webDarkTheme } from '@fluentui/react-components';

// ============================================================================
// Constants
// ============================================================================

export const THEME_STORAGE_KEY = 'spaarke-theme';
export const THEME_CHANGE_EVENT = 'spaarke-theme-change';

/**
 * Dataverse choice value for the ThemePreference preference type.
 * Must match the sprk_preferencetype option set value configured in Dataverse.
 * Used by syncThemeFromDataverse() and persistThemeToDataverse().
 */
export const PREFERENCE_TYPE_THEME = 100000001;

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

  window.dispatchEvent(
    new CustomEvent(THEME_CHANGE_EVENT, {
      detail: { theme },
    })
  );
}

// ============================================================================
// Detection Utilities
// ============================================================================

/**
 * Detect dark mode from URL `flags` parameter.
 *
 * Code Pages opened via `Xrm.Navigation.navigateTo` can receive a `flags`
 * query parameter containing `themeOption=dark` or `themeOption=light`.
 *
 * @returns `true` if dark, `false` if light, `null` if not specified
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

/**
 * Detect dark mode from the Dataverse navbar background color.
 *
 * When a UI surface is embedded in a model-driven app, the parent frame
 * contains a navbar element whose computed background color indicates the
 * current Dataverse theme. Checks current document first, then parent frame.
 *
 * @returns `true` if dark, `false` if light, `null` if navbar not found or color unrecognized
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
// PCF Theme Resolution
// ============================================================================

/**
 * Get effective dark mode considering all sources (PCF controls).
 *
 * Priority:
 * 1. localStorage (user's explicit preference)
 * 2. Power Platform context (fluentDesignLanguage.isDarkTheme)
 * 3. DOM navbar detection
 *
 * Defaults to false (light mode) when no preference is found.
 * OS `prefers-color-scheme` is intentionally NOT consulted — ADR-021 requires
 * the Spaarke theme system (not the OS) to control all UI surfaces.
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

  // Navbar DOM detection
  const navbarDark = detectDarkModeFromNavbar();
  if (navbarDark !== null) return navbarDark;

  // Default: light mode (OS prefers-color-scheme is intentionally NOT consulted)
  return false;
}

/**
 * Resolve Fluent UI theme based on effective dark mode (PCF controls)
 * @param context - PCF context (optional)
 * @returns Fluent UI v9 Theme (webDarkTheme or webLightTheme)
 */
export function resolveThemeWithUserPreference(context?: any): Theme {
  return getEffectiveDarkMode(context) ? webDarkTheme : webLightTheme;
}

// ============================================================================
// Code Page Theme Resolution
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
// PCF Event Listeners
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
 *
 * OS `prefers-color-scheme` changes are intentionally NOT listened to — ADR-021
 * requires the Spaarke theme system (not the OS) to control all UI surfaces.
 *
 * @param onChange - Callback when theme changes
 * @param context - PCF context (optional, for re-evaluating effective theme)
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(onChange: ThemeChangeHandler, context?: any): () => void {
  const handleStorageChange = (event: StorageEvent) => {
    if (event.key === THEME_STORAGE_KEY) {
      onChange(getEffectiveDarkMode(context));
    }
  };

  const handleThemeEvent = () => {
    onChange(getEffectiveDarkMode(context));
  };

  // Add listeners
  window.addEventListener('storage', handleStorageChange);
  window.addEventListener(THEME_CHANGE_EVENT, handleThemeEvent);

  // Return cleanup function
  return () => {
    window.removeEventListener('storage', handleStorageChange);
    window.removeEventListener(THEME_CHANGE_EVENT, handleThemeEvent);
  };
}

// ============================================================================
// Code Page Event Listeners
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

// ============================================================================
// Dataverse Theme Persistence (Cross-Device Sync)
// ============================================================================

/**
 * Minimal WebApi interface for theme persistence.
 * Compatible with both Xrm.WebApi and BFF API adapters (ADR-012).
 */
export interface IThemeWebApi {
  retrieveMultipleRecords(
    entityType: string,
    options: string,
    maxPageSize?: number
  ): Promise<{ entities: any[] }>;
  createRecord(
    entityType: string,
    data: Record<string, any>
  ): Promise<{ id: string }>;
  updateRecord(
    entityType: string,
    id: string,
    data: Record<string, any>
  ): Promise<void>;
}

/**
 * Sync theme preference from Dataverse to localStorage.
 *
 * Reads the user's ThemePreference from sprk_userpreference. If it differs
 * from the current localStorage value, updates localStorage and dispatches
 * a theme change event so all listening surfaces re-render.
 *
 * Designed to run async on page load — never blocks rendering.
 * Silently no-ops if Dataverse is unavailable.
 *
 * @param webApi - WebApi interface (Xrm.WebApi or adapter)
 * @param userId - Current user's systemuser GUID
 */
export async function syncThemeFromDataverse(
  webApi: IThemeWebApi,
  userId: string
): Promise<void> {
  try {
    const select = 'sprk_userpreferenceid,sprk_preferencevalue';
    const filter = `_sprk_user_value eq ${userId} and sprk_preferencetype eq ${PREFERENCE_TYPE_THEME}`;
    const query = `?$select=${select}&$filter=${filter}&$top=1`;

    const result = await webApi.retrieveMultipleRecords('sprk_userpreference', query, 1);

    if (result.entities.length > 0) {
      const dvTheme = result.entities[0].sprk_preferencevalue as string;
      if (dvTheme === 'dark' || dvTheme === 'light' || dvTheme === 'auto') {
        const current = getUserThemePreference();
        if (dvTheme !== current) {
          // Dataverse wins — update localStorage and notify listeners
          setUserThemePreference(dvTheme);
        }
      }
    }
  } catch {
    // Dataverse unavailable — silently fall back to localStorage
  }
}

/**
 * Persist theme preference to Dataverse for cross-device sync.
 *
 * Creates or updates the user's ThemePreference record in sprk_userpreference.
 * Runs async after user changes theme — never blocks the UI.
 * Silently no-ops if Dataverse is unavailable.
 *
 * @param webApi - WebApi interface (Xrm.WebApi or adapter)
 * @param userId - Current user's systemuser GUID
 * @param theme - The theme preference to persist
 */
export async function persistThemeToDataverse(
  webApi: IThemeWebApi,
  userId: string,
  theme: ThemePreference
): Promise<void> {
  try {
    // Check if a preference record already exists
    const select = 'sprk_userpreferenceid';
    const filter = `_sprk_user_value eq ${userId} and sprk_preferencetype eq ${PREFERENCE_TYPE_THEME}`;
    const query = `?$select=${select}&$filter=${filter}&$top=1`;

    const result = await webApi.retrieveMultipleRecords('sprk_userpreference', query, 1);

    if (result.entities.length > 0) {
      // Update existing record
      const id = result.entities[0].sprk_userpreferenceid as string;
      await webApi.updateRecord('sprk_userpreference', id, {
        sprk_preferencevalue: theme,
      });
    } else {
      // Create new preference record
      await webApi.createRecord('sprk_userpreference', {
        sprk_preferencetype: PREFERENCE_TYPE_THEME,
        sprk_preferencevalue: theme,
        'sprk_User@odata.bind': `/systemusers(${userId})`,
      });
    }
  } catch {
    // Dataverse unavailable — localStorage still has the preference
  }
}
