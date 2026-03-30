/**
 * ScopeEditorShell
 *
 * FluentProvider wrapper with dark mode detection from PCF context.
 * Provides a consistent container layout for all scope editor variants.
 *
 * ADR-021: FluentProvider wraps all UI; design tokens for colors.
 * ADR-022: React 16 APIs. No createRoot.
 */

import * as React from 'react';
import { FluentProvider, webLightTheme, webDarkTheme, Theme, makeStyles, tokens } from '@fluentui/react-components';
import {
  getEffectiveDarkMode,
  setupThemeListener,
} from '@spaarke/ui-components/dist/utils/themeStorage';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IScopeEditorShellProps {
  /** Whether dark mode is active (detected from PCF context or URL params) */
  isDark?: boolean;
  children: React.ReactNode;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  shell: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    minHeight: '120px',
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    fontFamily: tokens.fontFamilyBase,
    fontSize: tokens.fontSizeBase300,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

const ShellContent: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const styles = useStyles();
  return <div className={styles.shell}>{children}</div>;
};

export const ScopeEditorShell: React.FC<IScopeEditorShellProps> = ({ isDark, children }) => {
  const [theme, setTheme] = React.useState<Theme>(() => {
    const dark = isDark !== undefined ? isDark : getEffectiveDarkMode();
    return dark ? webDarkTheme : webLightTheme;
  });

  // Update theme when prop changes
  React.useEffect(() => {
    if (isDark !== undefined) {
      setTheme(isDark ? webDarkTheme : webLightTheme);
    }
  }, [isDark]);

  // Listen for theme changes when isDark is uncontrolled
  // Uses shared library listener (no OS prefers-color-scheme per ADR-021)
  React.useEffect(() => {
    if (isDark !== undefined) return;

    const cleanup = setupThemeListener((isDarkNow: boolean) => {
      setTheme(isDarkNow ? webDarkTheme : webLightTheme);
    });
    return cleanup;
  }, [isDark]);

  return (
    <FluentProvider theme={theme}>
      <ShellContent>{children}</ShellContent>
    </FluentProvider>
  );
};
