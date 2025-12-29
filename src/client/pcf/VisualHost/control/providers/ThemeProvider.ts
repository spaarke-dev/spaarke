/**
 * Theme Provider for Visual Host PCF
 * Detects and applies Fluent UI v9 themes based on Power Apps theme
 */

import {
  webLightTheme,
  webDarkTheme,
  teamsLightTheme,
  teamsDarkTheme,
  Theme,
} from "@fluentui/react-components";
import { IInputs } from "../generated/ManifestTypes";
import { logger } from "../utils/logger";

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
 */
export function resolveTheme(
  context?: ComponentFramework.Context<IInputs>
): Theme {
  const dark = isDarkMode(context);

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
 * Returns a cleanup function
 */
export function setupThemeListener(
  onThemeChange: (isDark: boolean) => void,
  context?: ComponentFramework.Context<IInputs>
): () => void {
  // Listen for system preference changes
  const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");

  const handleChange = (e: MediaQueryListEvent) => {
    logger.info("ThemeProvider", `System theme changed: ${e.matches ? "dark" : "light"}`);
    onThemeChange(e.matches);
  };

  mediaQuery.addEventListener("change", handleChange);

  // Return cleanup function
  return () => {
    mediaQuery.removeEventListener("change", handleChange);
  };
}
