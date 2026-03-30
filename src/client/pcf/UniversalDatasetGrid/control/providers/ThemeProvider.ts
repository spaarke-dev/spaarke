/**
 * Theme Provider Utility
 *
 * Resolves the appropriate Fluent UI theme based on Power Apps context.
 * Delegates to @spaarke/ui-components themeStorage for detection logic.
 *
 * Theme priority (per ADR-021):
 * 1. localStorage ('spaarke-theme') user preference
 * 2. Power Apps context (fluentDesignLanguage.isDarkTheme)
 * 3. DOM navbar color fallback
 *
 * OS prefers-color-scheme is intentionally NOT consulted (ADR-021).
 *
 * Note: Per-control theme toggle removed in favor of global theme menu.
 * See: projects/mda-darkmode-theme/spec.md
 */

import { Theme, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';
import { logger } from '../utils/logger';
import {
  getUserThemePreference,
  getEffectiveDarkMode as sharedGetEffectiveDarkMode,
  setupThemeListener as sharedSetupThemeListener,
} from '@spaarke/ui-components/dist/utils/themeStorage';

// Re-export for consumers that import from this module
export { getUserThemePreference };

/**
 * Get effective dark mode state considering all sources.
 * Delegates to shared library (no OS prefers-color-scheme per ADR-021).
 */
export function getEffectiveDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
  return sharedGetEffectiveDarkMode(context);
}

/**
 * Set up listener for theme changes (localStorage and custom events).
 * OS prefers-color-scheme changes are NOT listened to (ADR-021).
 *
 * @param callback - Called when theme changes with new isDark value
 * @param context - PCF context (optional, for context-based theme detection)
 * @returns Cleanup function to remove listeners
 */
export function setupThemeListener(
  callback: (isDark: boolean) => void,
  context?: ComponentFramework.Context<IInputs>
): () => void {
  return sharedSetupThemeListener(callback, context);
}

/**
 * Resolve the appropriate Fluent UI theme based on user preference and context.
 *
 * Uses getEffectiveDarkMode() which implements the priority chain:
 * 1. localStorage user preference
 * 2. Power Apps context
 * 3. DOM navbar color
 *
 * @param context - PCF context with theme information
 * @returns Fluent UI theme object
 */
export function resolveTheme(context: ComponentFramework.Context<IInputs>): Theme {
  try {
    const isDark = getEffectiveDarkMode(context);
    logger.debug('ThemeProvider', `Theme resolved: ${isDark ? 'dark' : 'light'}`);
    return isDark ? webDarkTheme : webLightTheme;
  } catch (error) {
    logger.warn('ThemeProvider', 'Error resolving theme, using light theme fallback', error);
    return webLightTheme;
  }
}
