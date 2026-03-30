/**
 * Theme resolution service for RelatedDocumentCount.
 *
 * Delegates to @spaarke/ui-components themeStorage for all theme detection.
 * Supports light, dark, and high-contrast modes per ADR-021.
 * OS prefers-color-scheme is intentionally NOT consulted (ADR-021).
 */

import { Theme, webLightTheme, webDarkTheme, teamsHighContrastTheme } from '@fluentui/react-components';
import { IInputs } from '../generated/ManifestTypes';
import {
  getUserThemePreference,
  getEffectiveDarkMode as sharedGetEffectiveDarkMode,
  setupThemeListener as sharedSetupThemeListener,
  type ThemePreference,
} from '@spaarke/ui-components/dist/utils/themeStorage';

// Re-export for consumers of this module
export { getUserThemePreference };
export type { ThemePreference };

/**
 * Get the effective dark mode state considering all sources.
 * Delegates to shared library (no OS prefers-color-scheme per ADR-021).
 */
export function getEffectiveDarkMode(context?: ComponentFramework.Context<IInputs>): boolean {
  return sharedGetEffectiveDarkMode(context);
}

/**
 * Resolve the appropriate Fluent theme based on context.
 */
export function resolveTheme(context?: ComponentFramework.Context<IInputs>): Theme {
  if (context?.fluentDesignLanguage?.tokenTheme) {
    const tokenTheme = String(context.fluentDesignLanguage.tokenTheme);
    if (
      tokenTheme === 'TeamsHighContrast' ||
      tokenTheme === 'HighContrastWhite' ||
      tokenTheme === 'HighContrastBlack'
    ) {
      return teamsHighContrastTheme;
    }
  }

  const isDark = getEffectiveDarkMode(context);
  return isDark ? webDarkTheme : webLightTheme;
}

/**
 * Set up listeners for theme changes.
 * OS prefers-color-scheme changes are NOT listened to (ADR-021).
 */
export function setupThemeListener(
  callback: (isDark: boolean) => void,
  context?: ComponentFramework.Context<IInputs>
): () => void {
  return sharedSetupThemeListener(callback, context);
}

export default {
  resolveTheme,
  setupThemeListener,
  getUserThemePreference,
  getEffectiveDarkMode,
};
