/**
 * Theme Provider for Visual Host PCF
 * Detects and applies Fluent UI v9 themes based on Power Apps theme
 * Supports light, dark, and high-contrast modes per ADR-021
 */

import {
  webLightTheme,
  webDarkTheme,
  teamsLightTheme,
  teamsDarkTheme,
  teamsHighContrastTheme,
  Theme,
} from "@fluentui/react-components";
import { IInputs } from "../generated/ManifestTypes";
import { logger } from "../utils/logger";

/**
 * Theme mode enumeration
 */
export type ThemeMode = "light" | "dark" | "high-contrast";

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
 * Detect if dark mode is active in the host environment
 */
export function isDarkMode(
  context?: ComponentFramework.Context<IInputs>
): boolean {
  // Method 1: Check CSS custom properties
  const root = document.documentElement;
  const bgColor = getComputedStyle(root).getPropertyValue(
    "--colorNeutralBackground1"
  );
  if (bgColor) {
    // Parse RGB and check luminance
    const rgb = bgColor.match(/\d+/g);
    if (rgb && rgb.length >= 3) {
      const luminance =
        (parseInt(rgb[0]) * 299 +
          parseInt(rgb[1]) * 587 +
          parseInt(rgb[2]) * 114) /
        1000;
      return luminance < 128;
    }
  }

  // Method 2: Check body background color
  const bodyBg = getComputedStyle(document.body).backgroundColor;
  if (bodyBg && bodyBg !== "rgba(0, 0, 0, 0)") {
    const rgb = bodyBg.match(/\d+/g);
    if (rgb && rgb.length >= 3) {
      const luminance =
        (parseInt(rgb[0]) * 299 +
          parseInt(rgb[1]) * 587 +
          parseInt(rgb[2]) * 114) /
        1000;
      return luminance < 128;
    }
  }

  // Method 3: Check system preference
  if (window.matchMedia) {
    return window.matchMedia("(prefers-color-scheme: dark)").matches;
  }

  // Default to light mode
  return false;
}

/**
 * Resolve the appropriate Fluent theme based on environment
 * Supports light, dark, and high-contrast modes per ADR-021
 */
export function resolveTheme(
  context?: ComponentFramework.Context<IInputs>
): Theme {
  const themeMode = getThemeMode(context);

  // High-contrast mode takes precedence (accessibility requirement)
  if (themeMode === "high-contrast") {
    logger.debug("ThemeProvider", "Resolved high-contrast theme");
    return teamsHighContrastTheme;
  }

  const dark = themeMode === "dark";

  // Check if we're in Teams context
  const isTeams =
    window.location.hostname.includes("teams") ||
    document.body.classList.contains("teams-client");

  if (isTeams) {
    logger.debug("ThemeProvider", `Resolved Teams theme: ${dark ? "dark" : "light"}`);
    return dark ? teamsDarkTheme : teamsLightTheme;
  }

  logger.debug("ThemeProvider", `Resolved Web theme: ${dark ? "dark" : "light"}`);
  return dark ? webDarkTheme : webLightTheme;
}

/**
 * Set up a listener for theme changes
 * Listens for both dark mode and high-contrast mode changes
 * Returns a cleanup function
 */
export function setupThemeListener(
  onThemeChange: (themeMode: ThemeMode) => void,
  context?: ComponentFramework.Context<IInputs>
): () => void {
  // Listen for system preference changes (dark mode)
  const darkModeQuery = window.matchMedia("(prefers-color-scheme: dark)");

  // Listen for high-contrast mode changes
  const forcedColorsQuery = window.matchMedia("(forced-colors: active)");

  const handleChange = () => {
    const newMode = getThemeMode(context);
    logger.info("ThemeProvider", `Theme changed: ${newMode}`);
    onThemeChange(newMode);
  };

  darkModeQuery.addEventListener("change", handleChange);
  forcedColorsQuery.addEventListener("change", handleChange);

  // Return cleanup function
  return () => {
    darkModeQuery.removeEventListener("change", handleChange);
    forcedColorsQuery.removeEventListener("change", handleChange);
  };
}
