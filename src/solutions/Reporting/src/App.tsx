/**
 * App.tsx — Reporting Code Page Shell
 *
 * Root component for the Power BI Embedded Reporting module.
 * Provides the FluentProvider with theme detection and the top-level
 * layout: header toolbar and the main report content area.
 *
 * Layout:
 *   ┌─────────────────────────────────────────┐
 *   │  Header: title | [report selector ▾]    │
 *   ├─────────────────────────────────────────┤
 *   │                                         │
 *   │         Report area (placeholder)       │
 *   │                                         │
 *   └─────────────────────────────────────────┘
 *
 * @see ADR-021 - Fluent UI v9 only; no hard-coded colors; dark mode required
 * @see ADR-006 - Code Page pattern for full-page surfaces
 */

import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
  Spinner,
  Button,
  Tooltip,
} from "@fluentui/react-components";
import { DataTrendingRegular, ArrowClockwiseRegular } from "@fluentui/react-icons";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { ModuleGate } from "./components/ModuleGate";

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    height: "100%",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderBottomStyle: "solid",
    borderBottomWidth: "1px",
    minHeight: "48px",
    flexShrink: 0,
  },
  headerLeft: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalS,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  headerActions: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  reportSelectorPlaceholder: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    borderRadius: tokens.borderRadiusMedium,
    borderColor: tokens.colorNeutralStroke1,
    borderStyle: "solid",
    borderWidth: "1px",
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    cursor: "default",
    minWidth: "200px",
  },
  main: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    padding: tokens.spacingVerticalM,
  },
  reportViewerPlaceholder: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusLarge,
    borderColor: tokens.colorNeutralStroke2,
    borderStyle: "dashed",
    borderWidth: "1px",
    color: tokens.colorNeutralForeground3,
  },
  placeholderIcon: {
    fontSize: "48px",
    color: tokens.colorNeutralForeground4,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const App: React.FC = () => {
  const [theme, setTheme] = React.useState(resolveTheme);

  // Respond to theme changes (user preference toggle, Dataverse dark mode)
  React.useEffect(() => {
    const cleanup = setupThemeListener(() => {
      setTheme(resolveTheme());
    });
    return cleanup;
  }, []);

  const styles = useStyles();

  return (
    <FluentProvider theme={theme} style={{ height: "100%" }}>
      <div className={styles.root}>
        <ModuleGate>
          {/* ---- Header ---- */}
          <header className={styles.header}>
            <div className={styles.headerLeft}>
              <DataTrendingRegular fontSize={20} />
              <Text size={500} className={styles.headerTitle}>
                Reporting
              </Text>
            </div>

            <div className={styles.headerActions}>
              {/* Report selector dropdown — placeholder for task 011 */}
              <div
                className={styles.reportSelectorPlaceholder}
                aria-label="Report selector (not yet implemented)"
                role="combobox"
                aria-expanded={false}
                aria-haspopup="listbox"
              >
                <Text size={200} color="inherit">
                  Select a report...
                </Text>
              </div>

              {/* Refresh button — placeholder for task 012 */}
              <Tooltip content="Refresh report" relationship="label">
                <Button
                  appearance="subtle"
                  icon={<ArrowClockwiseRegular />}
                  aria-label="Refresh report"
                  disabled
                />
              </Tooltip>
            </div>
          </header>

          {/* ---- Main report area ---- */}
          <main className={styles.main}>
            {/* ReportViewer component placeholder — implemented in task 013 */}
            <div className={styles.reportViewerPlaceholder} role="region" aria-label="Report area">
              <Spinner size="large" label="Loading report..." labelPosition="below" />
              <Text size={200}>
                Select a report from the dropdown above to get started.
              </Text>
            </div>
          </main>
        </ModuleGate>
      </div>
    </FluentProvider>
  );
};
