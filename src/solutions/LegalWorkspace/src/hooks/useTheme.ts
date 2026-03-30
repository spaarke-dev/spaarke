import { useState, useEffect, useCallback } from "react";
import { Theme } from "@fluentui/react-components";
import {
  getUserThemePreference,
  setUserThemePreference,
  resolveCodePageTheme,
  setupCodePageThemeListener,
  ThemePreference,
} from "@spaarke/ui-components";

/**
 * ThemeMode exposed to consumers.
 * Maps to ThemePreference from shared library ('auto' treated as 'light' for display).
 */
export type ThemeMode = "light" | "dark";

function preferenceToMode(pref: ThemePreference): ThemeMode {
  return pref === "dark" ? "dark" : "light";
}

export interface IUseThemeResult {
  theme: Theme;
  themeMode: ThemeMode;
  setThemeMode: (mode: ThemeMode) => void;
}

/**
 * React hook for theme management in Code Pages.
 *
 * Delegates to shared @spaarke/ui-components theme utilities:
 * - getUserThemePreference() / setUserThemePreference() for localStorage (`spaarke-theme` key)
 * - resolveCodePageTheme() for Fluent UI v9 theme resolution
 * - setupCodePageThemeListener() for cross-tab and same-tab change events
 *
 * OS `prefers-color-scheme` is intentionally NOT consulted — ADR-021 requires
 * the Spaarke theme system (not the OS) to control all UI surfaces.
 */
export function useTheme(): IUseThemeResult {
  const [theme, setTheme] = useState<Theme>(resolveCodePageTheme);
  const [themeMode, setThemeModeState] = useState<ThemeMode>(() =>
    preferenceToMode(getUserThemePreference())
  );

  const setThemeMode = useCallback((mode: ThemeMode) => {
    setUserThemePreference(mode);
    setThemeModeState(mode);
    // resolveCodePageTheme will pick up the new localStorage value
    setTheme(resolveCodePageTheme());
  }, []);

  // Listen for theme changes from other tabs or same-tab theme menu
  useEffect(() => {
    const cleanup = setupCodePageThemeListener((newTheme: Theme) => {
      setTheme(newTheme);
      setThemeModeState(preferenceToMode(getUserThemePreference()));
    });
    return cleanup;
  }, []);

  return {
    theme,
    themeMode,
    setThemeMode,
  };
}
