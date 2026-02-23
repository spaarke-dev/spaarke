import { useState, useEffect, useCallback } from "react";
import {
  webLightTheme,
  webDarkTheme,
  teamsHighContrastTheme,
  Theme,
} from "@fluentui/react-components";

export type ThemeMode = "light" | "dark" | "high-contrast";

const STORAGE_KEY = "spaarke-workspace-theme";

function getInitialThemeMode(): ThemeMode {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "light" || stored === "dark" || stored === "high-contrast") {
      return stored;
    }
  } catch {
    // localStorage may be unavailable in some iframe contexts
  }

  // No stored preference — detect from system
  try {
    const prefersDark = window.matchMedia("(prefers-color-scheme: dark)");
    return prefersDark.matches ? "dark" : "light";
  } catch {
    return "light";
  }
}

function resolveTheme(mode: ThemeMode): Theme {
  switch (mode) {
    case "dark":
      return webDarkTheme;
    case "high-contrast":
      return teamsHighContrastTheme;
    case "light":
    default:
      return webLightTheme;
  }
}

export interface IUseThemeResult {
  theme: Theme;
  themeMode: ThemeMode;
  setThemeMode: (mode: ThemeMode) => void;
}

export function useTheme(): IUseThemeResult {
  const [themeMode, setThemeModeState] = useState<ThemeMode>(
    getInitialThemeMode
  );

  const setThemeMode = useCallback((mode: ThemeMode) => {
    setThemeModeState(mode);
    try {
      localStorage.setItem(STORAGE_KEY, mode);
    } catch {
      // Ignore storage errors
    }
  }, []);

  // Listen for OS-level preference changes and update only when user has not
  // explicitly overridden (i.e. nothing stored in localStorage)
  useEffect(() => {
    let mediaQuery: MediaQueryList | null = null;

    try {
      mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    } catch {
      return;
    }

    const handler = (e: MediaQueryListEvent) => {
      try {
        const hasStoredPreference = localStorage.getItem(STORAGE_KEY) !== null;
        if (!hasStoredPreference) {
          setThemeModeState(e.matches ? "dark" : "light");
        }
      } catch {
        // Ignore errors
      }
    };

    if (mediaQuery.addEventListener) {
      mediaQuery.addEventListener("change", handler);
    } else {
      // Fallback for older browsers
      mediaQuery.addListener(handler);
    }

    return () => {
      if (mediaQuery) {
        if (mediaQuery.removeEventListener) {
          mediaQuery.removeEventListener("change", handler);
        } else {
          mediaQuery.removeListener(handler);
        }
      }
    };
  }, []);

  return {
    theme: resolveTheme(themeMode),
    themeMode,
    setThemeMode,
  };
}
