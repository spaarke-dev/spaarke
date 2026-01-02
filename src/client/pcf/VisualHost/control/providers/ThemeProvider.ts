/**
 * Theme Provider for Visual Host PCF
 *
 * Detects and applies Fluent UI v9 themes based on Power Apps theme.
 * Supports light, dark, and high-contrast modes per ADR-021.
 *
 * Theme priority (per established pattern in UniversalDatasetGrid):
 * 1. localStorage ('spaarke-theme') user preference
 * 2. Power Apps context (fluentDesignLanguage.isDarkTheme)
 * 3. DOM navbar color fallback
 * 4. System preference (prefers-color-scheme)
 */

import {
  webLightTheme,
  webDarkTheme,
  teamsHighContrastTheme,
  Theme,
} from "@fluentui/react-components";
import { IInputs } from "../generated/ManifestTypes";
import { logger } from "../utils/logger";

// ─────────────────────────────────────────────────────────────────────────────
// Theme Storage Constants
// Matches UniversalDatasetGrid and shared components pattern
// ─────────────────────────────────────────────────────────────────────────────

const STORAGE_KEY = "spaarke-theme";
const THEME_CHANGE_EVENT = "spaarke-theme-change";

type ThemePreference = "auto" | "light" | "dark";

/**
 * Theme mode enumeration
 */
export type ThemeMode = "light" | "dark" | "high-contrast";

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
 * Detect if high-contrast mode is active
 */
export function isHighContrast(): boolean {
  // Method 1: Check for forced-colors media query (modern browsers)
  if (window.matchMedia) {
    const forcedColors = window.matchMedia("(forced-colors: active)");
    if (forcedColors.matches) {
      return true;
    }

    // Method 2: Check for -ms-high-contrast (legacy Edge/IE)
    const msHighContrast = window.matchMedia("(-ms-high-contrast: active)");
    if (msHighContrast.matches) {
      return true;
    }
  }

  // Method 3: Check for specific high-contrast classes on body
  if (
    document.body.classList.contains("high-contrast") ||
    document.body.classList.contains("ms-highContrast")
  ) {
    return true;
  }

  return false;
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
        logger.debug("ThemeProvider", "Navbar detected: dark mode");
        return true;
      }
      if (bgColor === "rgb(240, 240, 240)") {
        logger.debug("ThemeProvider", "Navbar detected: light mode");
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
 * 2. Power Apps context (fluentDesignLanguage.isDarkTheme)
 * 3. DOM navbar color fallback
 * 4. System preference
 *
 * @param context - PCF context (optional)
 * @returns boolean - true if dark mode should be active
 */
export function getEffectiveDarkMode(
  context?: ComponentFramework.Context<IInputs>
): boolean {
  const preference = getUserThemePreference();

  // 1. User explicit choice overrides everything
  if (preference === "dark") {
    logger.debug("ThemeProvider", "Using localStorage preference: dark");
    return true;
  }
  if (preference === "light") {
    logger.debug("ThemeProvider", "Using localStorage preference: light");
    return false;
  }

  // 2. Check Power Apps context
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const contextAny = context as any;
  if (contextAny?.fluentDesignLanguage?.isDarkTheme !== undefined) {
    const isDark = contextAny.fluentDesignLanguage.isDarkTheme;
    logger.debug("ThemeProvider", `Using Power Apps context: ${isDark ? "dark" : "light"}`);
    return isDark;
  }

  // 3. Check DOM navbar color
  const navbarDark = detectDarkModeFromNavbar();
  if (navbarDark !== null) {
    return navbarDark;
  }

  // 4. Fall back to system preference
  const systemDark = getSystemThemePreference();
  logger.debug("ThemeProvider", `Using system preference: ${systemDark ? "dark" : "light"}`);
  return systemDark;
}

/**
 * Detect if dark mode is active (legacy function for compatibility)
 */
export function isDarkMode(
  context?: ComponentFramework.Context<IInputs>
): boolean {
  return getEffectiveDarkMode(context);
}

/**
 * Get current theme mode
 */
export function getThemeMode(
  context?: ComponentFramework.Context<IInputs>
): ThemeMode {
  if (isHighContrast()) {
    return "high-contrast";
  }
  if (isDarkMode(context)) {
    return "dark";
  }
  return "light";
}

/**
 * Resolve the appropriate Fluent theme based on environment
 * Supports light, dark, and high-contrast modes per ADR-021
 */
export function resolveTheme(
  context?: ComponentFramework.Context<IInputs>
): Theme {
  try {
    // High-contrast mode takes precedence (accessibility requirement)
    if (isHighContrast()) {
      logger.debug("ThemeProvider", "Resolved high-contrast theme");
      return teamsHighContrastTheme;
    }

    const isDark = getEffectiveDarkMode(context);
    logger.debug("ThemeProvider", `Theme resolved: ${isDark ? "dark" : "light"}`);
    return isDark ? webDarkTheme : webLightTheme;
  } catch (error) {
    logger.warn("ThemeProvider", "Error resolving theme, using light theme fallback", error);
    return webLightTheme;
  }
}

/**
 * Set up a listener for theme changes
 * Listens for localStorage changes, system preference, and high-contrast mode
 * Returns a cleanup function
 */
export function setupThemeListener(
  onThemeChange: (themeMode: ThemeMode) => void,
  context?: ComponentFramework.Context<IInputs>
): () => void {
  // Handle custom theme change event (from ribbon menu)
  const handleThemeChange = () => {
    const newMode = getThemeMode(context);
    logger.info("ThemeProvider", `Theme changed via event: ${newMode}`);
    onThemeChange(newMode);
  };

  // Handle system preference change
  const handleSystemChange = () => {
    // Only respond if user preference is 'auto'
    if (getUserThemePreference() === "auto") {
      const newMode = getThemeMode(context);
      logger.info("ThemeProvider", `Theme changed via system: ${newMode}`);
      onThemeChange(newMode);
    }
  };

  // Listen for custom event
  window.addEventListener(THEME_CHANGE_EVENT, handleThemeChange);

  // Listen for system preference changes
  const darkModeQuery = window.matchMedia("(prefers-color-scheme: dark)");
  darkModeQuery.addEventListener("change", handleSystemChange);

  // Listen for high-contrast mode changes
  const forcedColorsQuery = window.matchMedia("(forced-colors: active)");
  forcedColorsQuery.addEventListener("change", handleThemeChange);

  // Return cleanup function
  return () => {
    window.removeEventListener(THEME_CHANGE_EVENT, handleThemeChange);
    darkModeQuery.removeEventListener("change", handleSystemChange);
    forcedColorsQuery.removeEventListener("change", handleThemeChange);
  };
}
