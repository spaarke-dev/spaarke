import { Theme } from "@fluentui/react-components";
/**
 * DarkLightMode exposed to consumers.
 * Maps to ThemePreference ('auto' treated as 'light' for display).
 */
export type DarkLightMode = "light" | "dark";
export interface IUseThemeResult {
    theme: Theme;
    themeMode: DarkLightMode;
    setDarkLightMode: (mode: DarkLightMode) => void;
}
/**
 * React hook for theme management in Code Pages and workspace SPAs.
 *
 * Delegates to shared theme utilities:
 * - getUserThemePreference() / setUserThemePreference() for localStorage
 * - resolveCodePageTheme() for Fluent UI v9 theme resolution
 * - setupCodePageThemeListener() for cross-tab and same-tab change events
 *
 * OS `prefers-color-scheme` is intentionally NOT consulted (ADR-021).
 */
export declare function useTheme(): IUseThemeResult;
//# sourceMappingURL=useTheme.d.ts.map