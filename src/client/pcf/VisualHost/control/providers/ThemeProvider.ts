/**
 * Theme Provider for Visual Host PCF
 *
 * Thin wrapper around @spaarke/ui-components themeStorage utilities.
 * Detects and applies Fluent UI v9 themes based on Power Apps theme.
 * Supports light, dark, and high-contrast modes per ADR-021.
 *
 * Theme priority (per established pattern):
 * 1. localStorage ('spaarke-theme') user preference
 * 2. Power Apps context (fluentDesignLanguage.isDarkTheme)
 * 3. DOM navbar color fallback
 *
 * OS prefers-color-scheme is intentionally NOT consulted (ADR-021).
 */

import { webLightTheme, webDarkTheme, teamsHighContrastTheme, Theme } from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';
import { logger } from '../utils/logger';
import {
  getUserThemePreference,
  getEffectiveDarkMode as sharedGetEffectiveDarkMode,
  setupThemeListener as sharedSetupThemeListener,
  type ThemeChangeHandler,
} from '@spaarke/ui-components/dist/utils/themeStorage';

/**
 * Theme mode enumeration
 */
export type ThemeMode = 'light' | 'dark' | 'high-contrast';

/**
 * Detect if high-contrast mode is active
 */
export function isHighContrast(): boolean {
  // Method 1: Check for forced-colors media query (modern browsers)
  if (window.matchMedia) {
    const forcedColors = window.matchMedia('(forced-colors: active)');
    if (forcedColors.matches) {
      return true;
    }

    // Method 2: Check for -ms-high-contrast (legacy Edge/IE)
    const msHighContrast = window.matchMedia('(-ms-high-contrast: active)');
    if (msHighContrast.matches) {
      return true;
    }
  }

  // Method 3: Check for specific high-contrast classes on body
  if (document.body.classList.contains('high-contrast') || document.body.classList.contains('ms-highContrast')) {
    return true;
  }

  return false;
}

/**
 * Get effective dark mode state considering all sources
 *
 * Delegates to shared library. OS prefers-color-scheme is NOT consulted (ADR-021).
 *
 * @param context - PCF context (optional)
 * @returns boolean - true if dark mode should be active
 */
export function getEffectiveDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
  const isDark = sharedGetEffectiveDarkMode(context);
  logger.debug('ThemeProvider', `Effective dark mode: ${isDark}`);
  return isDark;
}

/**
 * Detect if dark mode is active (legacy function for compatibility)
 */
export function isDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
  return getEffectiveDarkMode(context);
}

/**
 * Get current theme mode
 */
export function getThemeMode(context?: ComponentFramework.Context<IInputs>): ThemeMode {
  if (isHighContrast()) {
    return 'high-contrast';
  }
  if (isDarkMode(context)) {
    return 'dark';
  }
  return 'light';
}

/**
 * Resolve the appropriate Fluent theme based on environment
 * Supports light, dark, and high-contrast modes per ADR-021
 */
export function resolveTheme(context?: ComponentFramework.Context<IInputs>): Theme {
  try {
    // High-contrast mode takes precedence (accessibility requirement)
    if (isHighContrast()) {
      logger.debug('ThemeProvider', 'Resolved high-contrast theme');
      return teamsHighContrastTheme;
    }

    const isDark = getEffectiveDarkMode(context);
    logger.debug('ThemeProvider', `Theme resolved: ${isDark ? 'dark' : 'light'}`);
    return isDark ? webDarkTheme : webLightTheme;
  } catch (error) {
    logger.warn('ThemeProvider', 'Error resolving theme, using light theme fallback', error);
    return webLightTheme;
  }
}

/**
 * Set up a listener for theme changes
 * Listens for localStorage changes and custom theme events.
 * OS prefers-color-scheme changes are NOT listened to (ADR-021).
 *
 * Returns a cleanup function
 */
export function setupThemeListener(
  onThemeChange: (themeMode: ThemeMode) => void,
  context?: ComponentFramework.Context<IInputs>
): () => void {
  // Use shared library listener (no OS prefers-color-scheme)
  const cleanupShared = sharedSetupThemeListener((isDark: boolean) => {
    const newMode = getThemeMode(context);
    logger.info('ThemeProvider', `Theme changed: ${newMode}`);
    onThemeChange(newMode);
  }, context);

  // Also listen for high-contrast mode changes
  let forcedColorsQuery: MediaQueryList | null = null;
  const handleHighContrastChange = () => {
    const newMode = getThemeMode(context);
    logger.info('ThemeProvider', `High-contrast changed: ${newMode}`);
    onThemeChange(newMode);
  };

  try {
    forcedColorsQuery = window.matchMedia('(forced-colors: active)');
    forcedColorsQuery.addEventListener('change', handleHighContrastChange);
  } catch {
    // matchMedia not available
  }

  // Return combined cleanup function
  return () => {
    cleanupShared();
    if (forcedColorsQuery) {
      forcedColorsQuery.removeEventListener('change', handleHighContrastChange);
    }
  };
}
