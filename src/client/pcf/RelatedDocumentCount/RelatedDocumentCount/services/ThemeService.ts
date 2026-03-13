/**
 * Theme resolution service for RelatedDocumentCount.
 *
 * Follows the theme-management pattern from .claude/patterns/pcf/theme-management.md
 * Supports light, dark, and high-contrast modes per ADR-021.
 */

import {
  Theme,
  webLightTheme,
  webDarkTheme,
  teamsHighContrastTheme,
} from "@fluentui/react-components";
import { IInputs } from "../generated/ManifestTypes";

const THEME_STORAGE_KEY = "spaarke-theme";
const THEME_CHANGE_EVENT = "spaarke-theme-change";

export type ThemePreference = "auto" | "light" | "dark";

/**
 * Get user's explicit theme preference from localStorage.
 */
export function getUserThemePreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === "light" || stored === "dark" || stored === "auto") {
      return stored;
    }
  } catch {
    // localStorage not available (e.g., sandbox)
  }
  return "auto";
}

/**
 * Detect dark mode from URL flags (Power Apps parameter).
 */
function detectDarkModeFromUrl(): boolean | null {
  try {
    const params = new URLSearchParams(window.location.search);
    const flags = params.get("flags");
    if (flags?.includes("themeOption=dark")) return true;
    if (flags?.includes("themeOption=light")) return false;
  } catch {
    // URL parsing failed
  }
  return null;
}

/**
 * Detect dark mode from navbar background color (Custom Page fallback).
 */
function detectDarkModeFromNavbar(): boolean | null {
  try {
    const navbar = document.querySelector('[data-id="navbar-container"]');
    if (!navbar) return null;
    const bgColor = window.getComputedStyle(navbar).backgroundColor;
    const rgb = bgColor.match(/\d+/g)?.map(Number) ?? [];
    if (rgb.length < 3) return null;
    const luminance = (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255;
    return luminance < 0.5;
  } catch {
    // DOM access failed
  }
  return null;
}

/**
 * Detect dark mode from the Dataverse page background color.
 */
function detectDarkModeFromPageBackground(): boolean | null {
  try {
    const bodyBg = window.getComputedStyle(document.body).backgroundColor;
    const rgbMatch = bodyBg.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
    if (rgbMatch) {
      const luminance =
        0.299 * parseInt(rgbMatch[1]) +
        0.587 * parseInt(rgbMatch[2]) +
        0.114 * parseInt(rgbMatch[3]);
      return luminance < 128;
    }
    try {
      const parentBg = window.getComputedStyle(
        window.parent.document.body,
      ).backgroundColor;
      const parentMatch = parentBg.match(/rgb\((\d+),\s*(\d+),\s*(\d+)\)/);
      if (parentMatch) {
        const luminance =
          0.299 * parseInt(parentMatch[1]) +
          0.587 * parseInt(parentMatch[2]) +
          0.114 * parseInt(parentMatch[3]);
        return luminance < 128;
      }
    } catch {
      // Cross-origin parent access blocked
    }
  } catch {
    // DOM access failed
  }
  return null;
}

/**
 * Detect if system prefers dark mode.
 */
function getSystemPrefersDark(): boolean {
  try {
    return window.matchMedia("(prefers-color-scheme: dark)").matches;
  } catch {
    return false;
  }
}

/**
 * Get the effective dark mode state considering all sources.
 */
export function getEffectiveDarkMode(
  context?: ComponentFramework.Context<IInputs>,
): boolean {
  const preference = getUserThemePreference();

  if (preference === "dark") return true;
  if (preference === "light") return false;

  const urlDark = detectDarkModeFromUrl();
  if (urlDark !== null) return urlDark;

  if (context?.fluentDesignLanguage?.isDarkTheme !== undefined) {
    return context.fluentDesignLanguage.isDarkTheme;
  }

  const navbarDark = detectDarkModeFromNavbar();
  if (navbarDark !== null) return navbarDark;

  const pageBgDark = detectDarkModeFromPageBackground();
  if (pageBgDark !== null) return pageBgDark;

  return getSystemPrefersDark();
}

/**
 * Resolve the appropriate Fluent theme based on context.
 */
export function resolveTheme(
  context?: ComponentFramework.Context<IInputs>,
): Theme {
  if (context?.fluentDesignLanguage?.tokenTheme) {
    const tokenTheme = String(context.fluentDesignLanguage.tokenTheme);
    if (
      tokenTheme === "TeamsHighContrast" ||
      tokenTheme === "HighContrastWhite" ||
      tokenTheme === "HighContrastBlack"
    ) {
      return teamsHighContrastTheme;
    }
  }

  const isDark = getEffectiveDarkMode(context);
  return isDark ? webDarkTheme : webLightTheme;
}

/**
 * Set up listeners for theme changes.
 */
export function setupThemeListener(
  callback: (isDark: boolean) => void,
  context?: ComponentFramework.Context<IInputs>,
): () => void {
  const handleThemeChange = () => callback(getEffectiveDarkMode(context));
  window.addEventListener(THEME_CHANGE_EVENT, handleThemeChange);

  let mediaQuery: MediaQueryList | null = null;
  try {
    mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    mediaQuery.addEventListener("change", handleThemeChange);
  } catch {
    // matchMedia not available
  }

  return () => {
    window.removeEventListener(THEME_CHANGE_EVENT, handleThemeChange);
    if (mediaQuery) {
      mediaQuery.removeEventListener("change", handleThemeChange);
    }
  };
}

export default {
  resolveTheme,
  setupThemeListener,
  getUserThemePreference,
  getEffectiveDarkMode,
};
