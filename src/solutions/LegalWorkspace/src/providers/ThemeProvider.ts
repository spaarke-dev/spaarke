/**
 * Theme Provider Utility for Legal Workspace Custom Page
 *
 * Resolves the appropriate Fluent UI theme based on context.
 * Detects light mode, dark mode, and high-contrast mode automatically.
 *
 * Theme priority (per ADR-021):
 * 1. localStorage ('spaarke-theme') user preference
 * 2. URL query parameter (?theme=dark)
 * 3. DOM navbar color fallback (when embedded in Dataverse)
 * 4. System preference (prefers-color-scheme)
 *
 * Based on src/solutions/EventDetailSidePane/src/providers/ThemeProvider.ts
 * Adapted for Events Custom Page context.
 */

import {
  Theme,
  webLightTheme,
  webDarkTheme,
} from "@fluentui/react-components";

// ─────────────────────────────────────────────────────────────────────────────
// Theme Storage Utilities
// ─────────────────────────────────────────────────────────────────────────────

const STORAGE_KEY = "spaarke-theme";
const THEME_CHANGE_EVENT = "spaarke-theme-change";

type ThemePreference = "auto" | "light" | "dark";

/**
 * Get the user's theme preference from localStorage
 */
export function getUserThemePreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === "light" || stored === "dark" || stored === "auto") {
      return stored;
    }
  } catch {
    // localStorage not available (SSR, private browsing, etc.)
  }
  return "auto";
}

/**
 * Get theme from URL query parameter
 */
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

/**
 * Detect dark mode from DOM navbar color (Power Apps fallback)
 */
function detectDarkModeFromNavbar(): boolean | null {
  try {
    const navbar = document.querySelector('[data-id="navbar-container"]');
    if (navbar) {
      const bgColor = window.getComputedStyle(navbar).backgroundColor;
      // rgb(10, 10, 10) = dark, rgb(240, 240, 240) = light
      if (bgColor === "rgb(10, 10, 10)") {
        return true;
      }
      if (bgColor === "rgb(240, 240, 240)") {
        return false;
      }
    }
  } catch {
    // DOM access failed
  }
  return null;
}

/**
 * Get system theme preference
 */
function getSystemThemePreference(): boolean {
  try {
    return window.matchMedia("(prefers-color-scheme: dark)").matches;
  } catch {
    return false;
  }
}

/**
 * Get effective dark mode state considering all sources
 *
 * Priority:
 * 1. localStorage user preference (if not 'auto')
 * 2. URL query parameter
 * 3. DOM navbar color fallback
 * 4. System preference
 *
 * @returns boolean - true if dark mode should be active
 */
export function getEffectiveDarkMode(): boolean {
  const preference = getUserThemePreference();

  // 1. User explicit choice overrides everything
  if (preference === "dark") {
    return true;
  }
  if (preference === "light") {
    return false;
  }

  // 2. Check URL parameter
  const urlTheme = getThemeFromUrl();
  if (urlTheme === "dark") {
    return true;
  }
  if (urlTheme === "light") {
    return false;
  }

  // 3. Check DOM navbar color (when embedded in Dataverse)
  const navbarDark = detectDarkModeFromNavbar();
  if (navbarDark !== null) {
    return navbarDark;
  }

  // 4. Fall back to system preference
  return getSystemThemePreference();
}

/**
 * Set up listener for theme changes (localStorage and system preference)
 *
 * @param callback - Called when theme changes
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(callback: () => void): () => void {
  // Handle custom theme change event (from ribbon menu or other controls)
  const handleThemeChange = () => callback();

  // Handle system preference change
  const handleSystemChange = () => {
    // Only respond if user preference is 'auto'
    if (getUserThemePreference() === "auto") {
      callback();
    }
  };

  // Listen for custom event
  window.addEventListener(THEME_CHANGE_EVENT, handleThemeChange);

  // Listen for system preference changes
  const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
  mediaQuery.addEventListener("change", handleSystemChange);

  // Return cleanup function
  return () => {
    window.removeEventListener(THEME_CHANGE_EVENT, handleThemeChange);
    mediaQuery.removeEventListener("change", handleSystemChange);
  };
}

/**
 * Resolve the appropriate Fluent UI theme based on user preference and context.
 *
 * Uses getEffectiveDarkMode() which implements the priority chain:
 * 1. localStorage user preference
 * 2. URL query parameter
 * 3. DOM navbar color
 * 4. System preference
 *
 * @returns Fluent UI theme object
 */
export function resolveTheme(): Theme {
  try {
    const isDark = getEffectiveDarkMode();
    return isDark ? webDarkTheme : webLightTheme;
  } catch {
    // Error resolving theme, use light theme fallback
    return webLightTheme;
  }
}
