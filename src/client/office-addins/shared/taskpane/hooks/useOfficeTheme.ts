import { useState, useEffect, useCallback } from 'react';
import { webLightTheme, webDarkTheme, teamsHighContrastTheme } from '@fluentui/react-components';
import type { Theme } from '@fluentui/react-components';

/**
 * Office theme types that can be detected.
 */
export type OfficeThemeType = 'light' | 'dark' | 'high-contrast';

/**
 * Hook return value.
 */
export interface UseOfficeThemeResult {
  /** Current theme type */
  themeType: OfficeThemeType;
  /** Fluent UI v9 theme object */
  theme: Theme;
  /** Whether dark mode is active */
  isDarkMode: boolean;
  /** Whether high contrast mode is active */
  isHighContrast: boolean;
  /** Force theme refresh */
  refreshTheme: () => void;
}

/**
 * Detects Office theme from the Office context.
 * Falls back to system preference if Office theme is not available.
 */
function detectThemeType(): OfficeThemeType {
  // Try to detect from Office context
  if (typeof Office !== 'undefined' && Office.context?.officeTheme) {
    const officeTheme = Office.context.officeTheme;

    // High contrast detection
    if (officeTheme.controlBackgroundColor === '#000000' ||
        officeTheme.bodyBackgroundColor === '#000000') {
      return 'high-contrast';
    }

    // Dark mode detection based on background color
    const bgColor = officeTheme.bodyBackgroundColor?.toLowerCase() || '';
    if (bgColor.startsWith('#1') || bgColor.startsWith('#2') || bgColor.startsWith('#0')) {
      return 'dark';
    }

    return 'light';
  }

  // Fallback to system preference
  if (typeof window !== 'undefined' && window.matchMedia) {
    // Check high contrast first
    if (window.matchMedia('(forced-colors: active)').matches) {
      return 'high-contrast';
    }

    // Then check dark mode preference
    if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
      return 'dark';
    }
  }

  return 'light';
}

/**
 * Maps theme type to Fluent UI v9 theme object.
 */
function getThemeForType(themeType: OfficeThemeType): Theme {
  switch (themeType) {
    case 'dark':
      return webDarkTheme;
    case 'high-contrast':
      return teamsHighContrastTheme;
    case 'light':
    default:
      return webLightTheme;
  }
}

/**
 * React hook to detect and respond to Office theme changes.
 *
 * Supports light, dark, and high-contrast themes.
 * Uses Fluent UI v9 theme tokens per ADR-021.
 *
 * @example
 * ```tsx
 * const { theme, isDarkMode } = useOfficeTheme();
 * return (
 *   <FluentProvider theme={theme}>
 *     <div>Dark mode: {isDarkMode ? 'Yes' : 'No'}</div>
 *   </FluentProvider>
 * );
 * ```
 */
export function useOfficeTheme(): UseOfficeThemeResult {
  const [themeType, setThemeType] = useState<OfficeThemeType>(() => detectThemeType());

  const refreshTheme = useCallback(() => {
    const newThemeType = detectThemeType();
    setThemeType(newThemeType);
  }, []);

  useEffect(() => {
    // Set up system preference listeners
    if (typeof window !== 'undefined' && window.matchMedia) {
      const darkModeQuery = window.matchMedia('(prefers-color-scheme: dark)');
      const highContrastQuery = window.matchMedia('(forced-colors: active)');

      const handleChange = () => {
        refreshTheme();
      };

      // Modern event listener syntax
      darkModeQuery.addEventListener?.('change', handleChange);
      highContrastQuery.addEventListener?.('change', handleChange);

      return () => {
        darkModeQuery.removeEventListener?.('change', handleChange);
        highContrastQuery.removeEventListener?.('change', handleChange);
      };
    }
    return undefined;
  }, [refreshTheme]);

  useEffect(() => {
    // Set up Office theme change detection
    // Office doesn't have a direct theme change event, but we can poll
    // or listen for settings changes
    if (typeof Office !== 'undefined' && Office.context?.document?.settings) {
      const checkTheme = () => {
        refreshTheme();
      };

      // Check theme periodically (Office doesn't provide a reliable theme change event)
      const intervalId = setInterval(checkTheme, 5000);

      return () => {
        clearInterval(intervalId);
      };
    }
    return undefined;
  }, [refreshTheme]);

  const theme = getThemeForType(themeType);
  const isDarkMode = themeType === 'dark';
  const isHighContrast = themeType === 'high-contrast';

  return {
    themeType,
    theme,
    isDarkMode,
    isHighContrast,
    refreshTheme,
  };
}
