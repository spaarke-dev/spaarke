/**
 * Theme Provider Utility for Analysis Builder Code Page
 *
 * Resolves the appropriate Fluent UI theme based on context.
 * Detects light mode, dark mode, and high-contrast mode automatically.
 *
 * Theme priority (per ADR-021):
 * 1. localStorage ('spaarke-theme') user preference
 * 2. URL query parameter (?theme=dark)
 * 3. DOM navbar color fallback (when embedded in Dataverse)
 * 4. System preference (prefers-color-scheme)
 */

import {
  Theme,
  webLightTheme,
  webDarkTheme,
} from "@fluentui/react-components";

const STORAGE_KEY = "spaarke-theme";
const THEME_CHANGE_EVENT = "spaarke-theme-change";

type ThemePreference = "auto" | "light" | "dark";

export function getUserThemePreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "light" || stored === "dark" || stored === "auto") {
      return stored;
    }
  } catch {
    // localStorage not available
  }
  return "auto";
}

function getThemeFromUrl(): ThemePreference | null {
  try {
    const params = new URLSearchParams(window.location.search);
    const theme = params.get("theme");
    if (theme === "light" || theme === "dark") {
      return theme;
    }
  } catch {
    // URL parsing failed
  }
  return null;
}

function detectDarkModeFromNavbar(): boolean | null {
  try {
    const navbar = document.querySelector('[data-id="navbar-container"]');
    if (navbar) {
      const bgColor = window.getComputedStyle(navbar).backgroundColor;
      if (bgColor === "rgb(10, 10, 10)") return true;
      if (bgColor === "rgb(240, 240, 240)") return false;
    }
  } catch {
    // DOM access failed
  }
  return null;
}

function getSystemThemePreference(): boolean {
  try {
    return window.matchMedia("(prefers-color-scheme: dark)").matches;
  } catch {
    return false;
  }
}

export function getEffectiveDarkMode(): boolean {
  const preference = getUserThemePreference();
  if (preference === "dark") return true;
  if (preference === "light") return false;

  const urlTheme = getThemeFromUrl();
  if (urlTheme === "dark") return true;
  if (urlTheme === "light") return false;

  const navbarDark = detectDarkModeFromNavbar();
  if (navbarDark !== null) return navbarDark;

  return getSystemThemePreference();
}

export function setupThemeListener(callback: () => void): () => void {
  const handleThemeChange = () => callback();
  const handleSystemChange = () => {
    if (getUserThemePreference() === "auto") callback();
  };

  window.addEventListener(THEME_CHANGE_EVENT, handleThemeChange);
  const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
  mediaQuery.addEventListener("change", handleSystemChange);

  return () => {
    window.removeEventListener(THEME_CHANGE_EVENT, handleThemeChange);
    mediaQuery.removeEventListener("change", handleSystemChange);
  };
}

export function resolveTheme(): Theme {
  try {
    const isDark = getEffectiveDarkMode();
    return isDark ? webDarkTheme : webLightTheme;
  } catch {
    return webLightTheme;
  }
}
