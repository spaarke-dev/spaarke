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
 *   │         ReportViewer (embed)            │
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
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Divider,
} from "@fluentui/react-components";
import { DataTrendingRegular, ArrowClockwiseRegular } from "@fluentui/react-icons";
import { Report } from "powerbi-client";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { ModuleGate } from "./components/ModuleGate";
import { ReportDropdownContainer } from "./components/ReportDropdown";
import { ReportViewer } from "./components/ReportViewer";
import { ModeToggle } from "./components/ModeToggle";
import { ExportButton } from "./components/ExportButton";
import { useEmbedToken } from "./hooks/useEmbedToken";
import { useReportingPrivilege, canEditReports } from "./hooks/useReportingPrivilege";
import type { ReportCatalogItem } from "./types";
import type { ReportMode } from "./types";

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
  toolbarDivider: {
    height: "20px",
    alignSelf: "center",
  },
  main: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    padding: tokens.spacingVerticalM,
  },
  /** Token loading state shown while fetching embed token */
  tokenLoading: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalM,
    color: tokens.colorNeutralForeground2,
  },
  /** Error banner wrapper — constrained width, centered */
  errorWrapper: {
    flex: 1,
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    padding: tokens.spacingHorizontalXXL,
  },
  errorMessageBar: {
    maxWidth: "560px",
    width: "100%",
  },
});

// ---------------------------------------------------------------------------
// ReportingShell — inner component rendered after ModuleGate passes
// ---------------------------------------------------------------------------

/**
 * Inner shell rendered only after the ModuleGate confirms the module is
 * enabled and the user has the sprk_ReportingAccess security role.
 *
 * Owns:
 *   - selectedReport state (updated by ReportDropdownContainer)
 *   - useEmbedToken (fetches embed token when a report is selected)
 *   - reportRef (stored Report reference for token refresh / mode switch)
 */
const ReportingShell: React.FC = () => {
  const styles = useStyles();

  // Selected report from the dropdown (set by ReportDropdownContainer)
  const [selectedReport, setSelectedReport] = React.useState<ReportCatalogItem | null>(null);

  // Current view/edit mode — starts in view mode
  const [reportMode, setReportMode] = React.useState<ReportMode>("view");

  // Current user's privilege level (Viewer / Author / Admin)
  const { privilege } = useReportingPrivilege();

  // Fetch embed token when a report is selected (uses sprk_report Dataverse ID)
  const {
    embedConfig,
    loading: tokenLoading,
    error: tokenError,
    refreshToken,
  } = useEmbedToken({ reportId: selectedReport?.id ?? null });

  // Stored Report reference — available after getEmbeddedComponent fires
  const reportRef = React.useRef<Report | null>(null);

  // ---- Handlers ------------------------------------------------------------

  const handleReportSelect = React.useCallback((report: ReportCatalogItem) => {
    reportRef.current = null; // Clear stale reference when report changes
    setReportMode("view"); // Reset to view mode when switching reports
    setSelectedReport(report);
  }, []);

  const handleReportReady = React.useCallback((report: Report) => {
    reportRef.current = report;
    console.info("[App] Report reference ready for token refresh / mode switch");
  }, []);

  const handleReportLoaded = React.useCallback((report: Report) => {
    reportRef.current = report;
    console.info("[App] Report loaded");
  }, []);

  const handleRefresh = React.useCallback(() => {
    refreshToken();
  }, [refreshToken]);

  // ---- Render --------------------------------------------------------------

  return (
    <>
      {/* ---- Header ---- */}
      <header className={styles.header}>
        <div className={styles.headerLeft}>
          <DataTrendingRegular fontSize={20} />
          <Text size={500} className={styles.headerTitle}>
            Reporting
          </Text>
        </div>

        <div className={styles.headerActions}>
          {/* Report selector dropdown — managed by ReportDropdownContainer */}
          <ReportDropdownContainer onReportSelect={handleReportSelect} />

          {/* Refresh button — re-fetches embed token */}
          <Tooltip content="Refresh report" relationship="label">
            <Button
              appearance="subtle"
              icon={<ArrowClockwiseRegular />}
              aria-label="Refresh report"
              disabled={!selectedReport || tokenLoading}
              onClick={handleRefresh}
            />
          </Tooltip>

          {/* Export button — visible to all roles (task 023) */}
          <Divider vertical className={styles.toolbarDivider} />
          <ExportButton
            reportId={selectedReport?.id ?? null}
            disabled={!selectedReport || tokenLoading}
          />

          {/* Mode toggle — only visible to Author and Admin (task 020) */}
          {canEditReports(privilege) && (
            <>
              <Divider vertical className={styles.toolbarDivider} />
              <ModeToggle
                mode={reportMode}
                onModeChange={setReportMode}
                report={reportRef.current}
                reportId={selectedReport?.id ?? null}
                disabled={!selectedReport || tokenLoading}
              />
            </>
          )}
        </div>
      </header>

      {/* ---- Main report area ---- */}
      <main className={styles.main}>
        {/* Token fetch error */}
        {tokenError && !tokenLoading && (
          <div className={styles.errorWrapper}>
            <div className={styles.errorMessageBar}>
              <MessageBar intent="error" layout="multiline">
                <MessageBarBody>
                  <MessageBarTitle>Unable to load report</MessageBarTitle>
                  {tokenError}
                </MessageBarBody>
              </MessageBar>
            </div>
          </div>
        )}

        {/* Token loading state */}
        {tokenLoading && !tokenError && (
          <div className={styles.tokenLoading} role="status" aria-label="Loading report">
            <Spinner size="large" label="Loading report…" labelPosition="below" />
          </div>
        )}

        {/* ReportViewer — shown when no loading/error states are active */}
        {!tokenLoading && !tokenError && (
          <ReportViewer
            embedConfig={embedConfig}
            editMode={reportMode === "edit"}
            onReportReady={handleReportReady}
            onReportLoaded={handleReportLoaded}
            onError={(event) => {
              console.error("[App] Report embed error:", event?.detail);
            }}
          />
        )}
      </main>
    </>
  );
};

// ---------------------------------------------------------------------------
// App — root component with FluentProvider + ModuleGate
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
          <ReportingShell />
        </ModuleGate>
      </div>
    </FluentProvider>
  );
};
