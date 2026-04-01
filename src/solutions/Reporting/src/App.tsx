/**
 * App.tsx — Reporting Code Page Shell
 *
 * Root component for the Power BI Embedded Reporting module.
 * Provides the FluentProvider with theme detection and the top-level
 * layout: header toolbar and the main report content area.
 *
 * Layout:
 *   ┌─────────────────────────────────────────┐
 *   │  Header: title | [toolbar controls]     │
 *   ├─────────────────────────────────────────┤
 *   │                                         │
 *   │         ReportViewer (embed)            │
 *   │                                         │
 *   └─────────────────────────────────────────┘
 *
 * Privilege matrix enforced by ReportingToolbar:
 *
 *   Control               | Viewer | Author | Admin
 *   ──────────────────────|--------|--------|──────
 *   Report dropdown       |   ✓   |   ✓   |   ✓
 *   Refresh button        |   ✓   |   ✓   |   ✓
 *   Export button         |   ✓   |   ✓   |   ✓
 *   Mode toggle (edit)    |   ✗   |   ✓   |   ✓
 *   Delete Report button  |   ✗   |   ✗   |   ✓
 *
 * @see ADR-021 - Fluent UI v9 only; no hard-coded colors; dark mode required
 * @see ADR-006 - Code Page pattern for full-page surfaces
 * @see ADR-008 - BFF endpoint filters for auth; privilege enforced server-side
 */

import * as React from "react";
import {
  FluentProvider,
  makeStyles,
  tokens,
  Text,
  Spinner,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Toaster,
} from "@fluentui/react-components";
import { DataTrendingRegular } from "@fluentui/react-icons";
import { Report } from "powerbi-client";
import { resolveTheme, setupThemeListener } from "./providers/ThemeProvider";
import { ModuleGate } from "./components/ModuleGate";
import { ReportViewer } from "./components/ReportViewer";
import { ReportingToolbar } from "./components/ReportingToolbar";
import { REPORTING_TOASTER_ID } from "./components/SaveControls";
import { useEmbedToken } from "./hooks/useEmbedToken";
import { useTokenRefresh } from "./hooks/useTokenRefresh";
import { useReportingPrivilege } from "./hooks/useReportingPrivilege";
import { useReportCatalog } from "./hooks/useReportCatalog";
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
 *   - report catalog state (via useReportCatalog — allows deletion to remove entries)
 *   - selectedReport state (updated by ReportDropdown or post-delete resolution)
 *   - useEmbedToken (fetches embed token when a report is selected)
 *   - reportRef (stored Report reference for token refresh / mode switch)
 *   - privilege (fetched from BFF, passed to ReportingToolbar for role-gating)
 */
const ReportingShell: React.FC = () => {
  const styles = useStyles();

  // ---- Catalog state -------------------------------------------------------
  // Owned here (not inside ReportDropdownContainer) so the shell can
  // optimistically remove a deleted report without a full refetch.

  const { reports: fetchedReports, loading: catalogLoading, refetch } = useReportCatalog();
  const [reports, setReports] = React.useState<ReportCatalogItem[]>([]);

  // Sync local reports copy when the catalog fetch settles
  React.useEffect(() => {
    if (!catalogLoading) {
      setReports(fetchedReports);
    }
  }, [fetchedReports, catalogLoading]);

  // ---- Report selection state ----------------------------------------------

  // Selected report from the dropdown (set by ReportDropdown or post-delete)
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

  // Background token refresh error state — surfaced as a non-blocking warning
  const [refreshError, setRefreshError] = React.useState<string | null>(null);

  const handleRefreshError = React.useCallback((error: Error) => {
    console.error("[App] Proactive token refresh failed:", error);
    setRefreshError(
      "The report session could not be renewed automatically. Please refresh the page."
    );
  }, []);

  // Proactive token refresh at 80% TTL — silently calls report.setAccessToken()
  const { isRefreshing } = useTokenRefresh({
    reportRef,
    embedConfig,
    reportId: selectedReport?.id ?? null,
    onRefreshError: handleRefreshError,
  });

  // ---- Handlers ------------------------------------------------------------

  const handleReportSelect = React.useCallback((report: ReportCatalogItem) => {
    reportRef.current = null; // Clear stale reference when report changes
    setReportMode("view"); // Reset to view mode when switching reports
    setRefreshError(null); // Clear any stale refresh errors from the previous report
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

  /**
   * Called by DeleteReportButton after a successful deletion.
   * Removes the deleted report from the local catalog and selects the next one.
   */
  const handleReportDeleted = React.useCallback(
    (nextReport: ReportCatalogItem | null) => {
      if (selectedReport) {
        setReports((prev) => prev.filter((r) => r.id !== selectedReport.id));
      }
      reportRef.current = null;
      setReportMode("view");
      setRefreshError(null);
      setSelectedReport(nextReport);
    },
    [selectedReport]
  );

  /**
   * Called by NewReportButton after a blank report is successfully created.
   * Optimistically adds the new item to the catalog and selects it in edit mode.
   * Also triggers a background refetch to ensure the catalog is fully in sync.
   */
  const handleReportCreated = React.useCallback(
    (newReport: ReportCatalogItem) => {
      // Add to catalog immediately so it appears in the dropdown right away
      setReports((prev) => [...prev, newReport]);
      // Select the new report and switch to edit mode
      reportRef.current = null;
      setReportMode("edit");
      setRefreshError(null);
      setSelectedReport(newReport);
      // Background sync: refetch the full catalog from the BFF
      refetch();
    },
    [refetch]
  );

  /**
   * Called by SaveControls after a Save As succeeds.
   * Adds the new copy to the catalog and selects it in edit mode.
   */
  const handleSaveAsComplete = React.useCallback(
    (newReport: ReportCatalogItem) => {
      setReports((prev) => [...prev, newReport]);
      reportRef.current = null;
      setReportMode("edit");
      setRefreshError(null);
      setSelectedReport(newReport);
      refetch();
    },
    [refetch]
  );

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

        {/* Privilege-gated toolbar — all role logic lives in ReportingToolbar */}
        <ReportingToolbar
          privilege={privilege}
          reportMode={reportMode}
          selectedReport={selectedReport}
          reports={reports}
          reportRef={reportRef}
          tokenLoading={tokenLoading || isRefreshing}
          workspaceId={embedConfig?.workspaceId ?? null}
          onReportSelect={handleReportSelect}
          onModeChange={setReportMode}
          onRefresh={handleRefresh}
          onReportDeleted={handleReportDeleted}
          onReportCreated={handleReportCreated}
          onSaveAsComplete={handleSaveAsComplete}
        />
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

        {/* Background token refresh error — non-blocking warning, report still visible */}
        {refreshError && !tokenError && (
          <MessageBar intent="warning" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>Session renewal failed</MessageBarTitle>
              {refreshError}
            </MessageBarBody>
          </MessageBar>
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

      {/* Toaster — used by SaveControls for success/error notifications */}
      <Toaster toasterId={REPORTING_TOASTER_ID} position="bottom-end" />
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
