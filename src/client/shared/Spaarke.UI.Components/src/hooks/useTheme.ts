import { useState, useEffect, useCallback } from "react";
import { Theme } from "@fluentui/react-components";
import {
  getUserThemePreference,
  setUserThemePreference,
  resolveCodePageTheme,
  setupCodePageThemeListener,
  applyMdaTheme,
  ThemePreference,
} from "../utils/themeStorage";

/**
 * DarkLightMode exposed to consumers.
 * Maps to ThemePreference ('auto' treated as 'light' for display).
 */
export type DarkLightMode = "light" | "dark";

function preferenceToMode(pref: ThemePreference): DarkLightMode {
  return pref === "dark" ? "dark" : "light";
}

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
export function useTheme(): IUseThemeResult {
  const [theme, setTheme] = useState<Theme>(resolveCodePageTheme);
  const [themeMode, setDarkLightModeState] = useState<DarkLightMode>(() =>
    preferenceToMode(getUserThemePreference())
  );

  const setDarkLightMode = useCallback((mode: DarkLightMode) => {
    setUserThemePreference(mode);
    // Apply to MDA shell — triggers full page reload with dark mode URL flag.
    // After reload, all surfaces re-initialize from localStorage.
    applyMdaTheme(mode);
    // If applyMdaTheme didn't reload (flag already matched), update React state
    setDarkLightModeState(mode);
    setTheme(resolveCodePageTheme());
  }, []);

  useEffect(() => {
    const cleanup = setupCodePageThemeListener((newTheme: Theme) => {
      setTheme(newTheme);
      setDarkLightModeState(preferenceToMode(getUserThemePreference()));
    });
    return cleanup;
  }, []);

  return { theme, themeMode, setDarkLightMode };
}
