/**
 * App.tsx — SpaarkeAi root application component.
 *
 * Wraps all UI in FluentProvider with theme detection per ADR-021.
 * Theme is resolved via resolveCodePageTheme() (localStorage -> URL flags -> navbar DOM -> light default)
 * and updated via setupCodePageThemeListener() without any OS prefers-color-scheme.
 *
 * This is a placeholder component for Wave 0. The three-pane layout and AI workspace
 * will be integrated in Wave 4 (task 040 — wire up ThreePaneLayout in SpaarkeAi).
 *
 * @see ADR-021 - Fluent v9, dark mode required, semantic tokens only
 * @see .claude/constraints/theme-consistency.md — resolveCodePageTheme pattern
 * @see src/solutions/LegalWorkspace/src/main.tsx — bootstrap pattern
 */

import * as React from "react";
import {
  FluentProvider,
  tokens,
  makeStyles,
  Text,
} from "@fluentui/react-components";
import {
  resolveCodePageTheme,
  setupCodePageThemeListener,
} from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    width: "100%",
    backgroundColor: tokens.colorNeutralBackground2,
    color: tokens.colorNeutralForeground1,
    boxSizing: "border-box",
  },
  placeholder: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    height: "100%",
    gap: "8px",
  },
  footer: {
    display: "flex",
    alignItems: "center",
    justifyContent: "flex-end",
    padding: "4px 16px",
    minHeight: "24px",
  },
  footerVersion: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
  },
});

const VERSION = "0.1.0";

// ---------------------------------------------------------------------------
// AppContent — uses hooks, must be inside FluentProvider
// ---------------------------------------------------------------------------

const AppContent: React.FC = () => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      {/* Placeholder: Three-pane AI workspace will be integrated in task 040 */}
      <div className={styles.placeholder}>
        <Text size={500} weight="semibold">
          SpaarkeAi
        </Text>
        <Text size={300} style={{ color: tokens.colorNeutralForeground3 }}>
          AI Workspace — scaffolded, awaiting Wave 1 components
        </Text>
      </div>

      <footer className={styles.footer}>
        <Text className={styles.footerVersion}>v{VERSION}</Text>
      </footer>
    </div>
  );
};

// ---------------------------------------------------------------------------
// App — top-level component that owns theme state
// ---------------------------------------------------------------------------

/**
 * Root App component.
 *
 * Theme resolution follows the Code Page priority chain (theme-consistency.md):
 *   1. localStorage `spaarke-theme` (user's explicit preference)
 *   2. URL `flags` parameter (themeOption=dark|light)
 *   3. Navbar DOM background color detection
 *   4. Default: light
 *
 * MUST NOT use OS prefers-color-scheme (per theme-consistency.md constraints).
 */
export const App: React.FC = () => {
  // resolveCodePageTheme() is the initializer — runs once at mount
  const [theme, setTheme] = React.useState(resolveCodePageTheme);

  // Listen for theme changes (localStorage events, custom spaarke-theme-changed events)
  React.useEffect(() => {
    const cleanup = setupCodePageThemeListener(setTheme);
    return cleanup;
  }, []);

  return (
    <FluentProvider theme={theme}>
      <AppContent />
    </FluentProvider>
  );
};
