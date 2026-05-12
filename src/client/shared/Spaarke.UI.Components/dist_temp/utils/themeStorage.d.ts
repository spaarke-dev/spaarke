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
import { Theme } from '@fluentui/react-components';
export declare const THEME_STORAGE_KEY = "spaarke-theme";
export declare const THEME_CHANGE_EVENT = "spaarke-theme-change";
/**
 * Dataverse choice value for the ThemePreference preference type.
 * Must match the sprk_preferencetype option set value configured in Dataverse.
 * Used by syncThemeFromDataverse() and persistThemeToDataverse().
 */
export declare const PREFERENCE_TYPE_THEME = 100000003;
export type ThemePreference = 'light' | 'dark' | 'auto';
/**
 * Get user's theme preference from localStorage
 * @returns ThemePreference ('auto' if not set)
 */
export declare function getUserThemePreference(): ThemePreference;
/**
 * Set user's theme preference in localStorage
 * Dispatches custom event for same-tab listeners
 */
export declare function setUserThemePreference(theme: ThemePreference): void;
/**
 * Detect dark mode from URL `flags` parameter.
 *
 * Code Pages opened via `Xrm.Navigation.navigateTo` can receive a `flags`
 * query parameter containing `themeOption=dark` or `themeOption=light`.
 *
 * @returns `true` if dark, `false` if light, `null` if not specified
 */
export declare function detectDarkModeFromUrl(): boolean | null;
/**
 * Detect dark mode from the Dataverse navbar background color.
 *
 * When a UI surface is embedded in a model-driven app, the parent frame
 * contains a navbar element whose computed background color indicates the
 * current Dataverse theme. Checks current document first, then parent frame.
 *
 * @returns `true` if dark, `false` if light, `null` if navbar not found or color unrecognized
 */
export declare function detectDarkModeFromNavbar(): boolean | null;
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
export declare function getEffectiveDarkMode(context?: any): boolean;
/**
 * Resolve Fluent UI theme based on effective dark mode (PCF controls)
 * @param context - PCF context (optional)
 * @returns Fluent UI v9 Theme (webDarkTheme or webLightTheme)
 */
export declare function resolveThemeWithUserPreference(context?: any): Theme;
/**
 * Apply theme to the full MDA application by manipulating the dark mode URL flag
 * and reloading the page.
 *
 * This triggers a full page navigation so the MDA shell (header, nav, form chrome)
 * switches between light and dark mode. The reload also causes all embedded surfaces
 * (PCF controls, Code Pages) to re-initialize with the new theme from localStorage.
 *
 * Call this AFTER setUserThemePreference() has updated localStorage.
 *
 * @param theme - The theme preference that was just set
 */
export declare function applyMdaTheme(theme: ThemePreference): void;
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
export declare function resolveCodePageTheme(): Theme;
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
export declare function setupThemeListener(onChange: ThemeChangeHandler, context?: any): () => void;
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
export declare function setupCodePageThemeListener(onChange: CodePageThemeChangeHandler): () => void;
/**
 * Minimal WebApi interface for theme persistence.
 * Compatible with both Xrm.WebApi and BFF API adapters (ADR-012).
 */
export interface IThemeWebApi {
    retrieveMultipleRecords(entityType: string, options: string, maxPageSize?: number): Promise<{
        entities: any[];
    }>;
    createRecord(entityType: string, data: Record<string, any>): Promise<{
        id: string;
    }>;
    updateRecord(entityType: string, id: string, data: Record<string, any>): Promise<void>;
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
export declare function syncThemeFromDataverse(webApi: IThemeWebApi, userId: string): Promise<void>;
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
export declare function persistThemeToDataverse(webApi: IThemeWebApi, userId: string, theme: ThemePreference): Promise<void>;
//# sourceMappingURL=themeStorage.d.ts.map