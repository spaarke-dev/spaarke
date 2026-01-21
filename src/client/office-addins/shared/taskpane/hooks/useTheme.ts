import { useState, useEffect, useCallback, useMemo } from 'react';
import {
  webLightTheme,
  webDarkTheme,
  teamsHighContrastTheme,
  type Theme,
} from '@fluentui/react-components';

/**
 * Theme storage key for user preference persistence.
 */
const THEME_STORAGE_KEY = 'spaarke-theme-preference';

/**
 * Theme preference options.
 */
export type ThemePreference = 'auto' | 'light' | 'dark';

/**
 * Resolved theme type after applying preference logic.
 */
export type ResolvedThemeType = 'light' | 'dark' | 'high-contrast';

/**
 * Hook return value.
 */
export interface UseThemeResult {
  /** Current user preference (auto, light, dark) */
  preference: ThemePreference;
  /** Resolved theme type after applying preference */
  resolvedType: ResolvedThemeType;
  /** Fluent UI v9 theme object */
  theme: Theme;
  /** Whether dark mode is active */
  isDarkMode: boolean;
  /** Whether high contrast mode is active */
  isHighContrast: boolean;
  /** Set user theme preference */
  setPreference: (preference: ThemePreference) => void;
  /** Toggle between light and dark (cycles auto -> light -> dark -> auto) */
  toggleTheme: () => void;
}

/**
 * Detects system/Office theme preference.
 */
function detectSystemTheme(): ResolvedThemeType {
  // Check for high contrast first
  if (typeof window !== 'undefined' && window.matchMedia) {
    if (window.matchMedia('(forced-colors: active)').matches) {
      return 'high-contrast';
    }
  }

  // Try to detect from Office context
  if (typeof Office !== 'undefined' && Office.context?.officeTheme) {
    const officeTheme = Office.context.officeTheme;

    // High contrast detection
    if (
      officeTheme.controlBackgroundColor === '#000000' ||
      officeTheme.bodyBackgroundColor === '#000000'
    ) {
      return 'high-contrast';
    }

    // Dark mode detection based on background color
    const bgColor = officeTheme.bodyBackgroundColor?.toLowerCase() || '';
    if (
      bgColor.startsWith('#1') ||
      bgColor.startsWith('#2') ||
      bgColor.startsWith('#0')
    ) {
      return 'dark';
    }

    return 'light';
  }

  // Fallback to system preference
  if (typeof window !== 'undefined' && window.matchMedia) {
    if (window.matchMedia('(prefers-color-scheme: dark)').matches) {
      return 'dark';
    }
  }

  return 'light';
}

/**
 * Gets stored user preference from sessionStorage.
 * Uses sessionStorage per auth.md constraints.
 */
function getStoredPreference(): ThemePreference {
  if (typeof sessionStorage === 'undefined') {
    return 'auto';
  }

  const stored = sessionStorage.getItem(THEME_STORAGE_KEY);
  if (stored === 'light' || stored === 'dark' || stored === 'auto') {
    return stored;
  }

  return 'auto';
}

/**
 * Saves user preference to sessionStorage.
 */
function savePreference(preference: ThemePreference): void {
  if (typeof sessionStorage !== 'undefined') {
    sessionStorage.setItem(THEME_STORAGE_KEY, preference);
  }
}

/**
 * Maps resolved theme type to Fluent UI v9 theme object.
 */
function getThemeForType(themeType: ResolvedThemeType): Theme {
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
 * Resolves the effective theme type based on user preference and system settings.
 */
function resolveThemeType(
  preference: ThemePreference,
  systemTheme: ResolvedThemeType
): ResolvedThemeType {
  // High contrast always takes precedence
  if (systemTheme === 'high-contrast') {
    return 'high-contrast';
  }

  // User explicit preference
  if (preference === 'light') {
    return 'light';
  }
  if (preference === 'dark') {
    return 'dark';
  }

  // Auto: follow system
  return systemTheme;
}

/**
 * React hook to manage theme preference with support for auto, light, dark modes.
 *
 * Follows ADR-021 requirements for Fluent UI v9 theming.
 * Uses design tokens for all theme values.
 *
 * @example
 * ```tsx
 * const { theme, isDarkMode, setPreference } = useTheme();
 * return (
 *   <FluentProvider theme={theme}>
 *     <Button onClick={() => setPreference('dark')}>
 *       Dark mode: {isDarkMode ? 'On' : 'Off'}
 *     </Button>
 *   </FluentProvider>
 * );
 * ```
 */
export function useTheme(): UseThemeResult {
  const [preference, setPreferenceState] = useState<ThemePreference>(() =>
    getStoredPreference()
  );
  const [systemTheme, setSystemTheme] = useState<ResolvedThemeType>(() =>
    detectSystemTheme()
  );

  // Set up system theme detection listeners
  useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) {
      return undefined;
    }

    const darkModeQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const highContrastQuery = window.matchMedia('(forced-colors: active)');

    const handleChange = () => {
      setSystemTheme(detectSystemTheme());
    };

    darkModeQuery.addEventListener?.('change', handleChange);
    highContrastQuery.addEventListener?.('change', handleChange);

    return () => {
      darkModeQuery.removeEventListener?.('change', handleChange);
      highContrastQuery.removeEventListener?.('change', handleChange);
    };
  }, []);

  // Set up Office theme polling (Office doesn't have a reliable theme change event)
  useEffect(() => {
    if (typeof Office === 'undefined' || !Office.context?.officeTheme) {
      return undefined;
    }

    const intervalId = setInterval(() => {
      setSystemTheme(detectSystemTheme());
    }, 5000);

    return () => {
      clearInterval(intervalId);
    };
  }, []);

  // Set preference and persist
  const setPreference = useCallback((newPreference: ThemePreference) => {
    setPreferenceState(newPreference);
    savePreference(newPreference);
  }, []);

  // Toggle through themes: auto -> light -> dark -> auto
  const toggleTheme = useCallback(() => {
    const nextPreference: ThemePreference =
      preference === 'auto' ? 'light' : preference === 'light' ? 'dark' : 'auto';
    setPreference(nextPreference);
  }, [preference, setPreference]);

  // Compute resolved values
  const resolvedType = useMemo(
    () => resolveThemeType(preference, systemTheme),
    [preference, systemTheme]
  );

  const theme = useMemo(() => getThemeForType(resolvedType), [resolvedType]);
  const isDarkMode = resolvedType === 'dark';
  const isHighContrast = resolvedType === 'high-contrast';

  return {
    preference,
    resolvedType,
    theme,
    isDarkMode,
    isHighContrast,
    setPreference,
    toggleTheme,
  };
}
