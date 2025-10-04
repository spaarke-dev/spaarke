/**
 * Theme detection and bridging utilities
 * Detects Power Platform theme and bridges to Fluent UI v9
 */

import { Theme, webLightTheme, webDarkTheme } from "@fluentui/react-components";
import { spaarkeLight } from "../theme/brand";
import { ThemeMode } from "../types";

export interface IThemeContext {
  fluentDesignLanguage?: {
    tokenTheme?: Theme;
    isDarkMode?: boolean;
  };
}

/**
 * Detect theme from Power Platform context
 * @param context PCF context (cast to any to access fluentDesignLanguage)
 * @param themeMode User-configured theme mode
 * @returns Fluent UI v9 Theme
 */
export function detectTheme(
  context: any,
  themeMode: ThemeMode = "Auto"
): Theme {
  // User explicitly chose Spaarke theme
  if (themeMode === "Spaarke") {
    return spaarkeLight;
  }

  // User explicitly chose Host theme
  if (themeMode === "Host") {
    const hostTheme = (context as IThemeContext).fluentDesignLanguage?.tokenTheme;
    if (hostTheme) {
      return hostTheme;
    }
    // Fallback to web theme if host theme unavailable
    const isDark = (context as IThemeContext).fluentDesignLanguage?.isDarkMode;
    return isDark ? webDarkTheme : webLightTheme;
  }

  // Auto mode: Try host theme, fallback to Spaarke
  const hostTheme = (context as IThemeContext).fluentDesignLanguage?.tokenTheme;
  if (hostTheme) {
    return hostTheme;
  }

  // No host theme available - use Spaarke brand theme
  return spaarkeLight;
}

/**
 * Detect if dark mode is enabled
 * @param context PCF context
 * @returns true if dark mode, false otherwise
 */
export function isDarkMode(context: any): boolean {
  return (context as IThemeContext).fluentDesignLanguage?.isDarkMode ?? false;
}
