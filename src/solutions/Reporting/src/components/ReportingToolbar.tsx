/**
 * ReportingToolbar.tsx
 * Toolbar for the Reporting Code Page header.
 *
 * Centralises all privilege-gated toolbar controls so App.tsx stays clean.
 * Renders controls based on the current user's privilege level:
 *
 *   Control               | Viewer | Author | Admin
 *   ──────────────────────|--------|--------|──────
 *   Report dropdown       |   ✓   |   ✓   |   ✓
 *   Refresh button        |   ✓   |   ✓   |   ✓
 *   Export button         |   ✓   |   ✓   |   ✓
 *   Mode toggle (edit)    |   ✗   |   ✓   |   ✓
 *   Delete Report button  |   ✗   |   ✗   |   ✓
 *
 * Controls that are not permitted for the current role are NOT rendered
 * (conditional render, not just disabled) per ADR-021 and security best
 * practice.
 *
 * The toolbar receives the report catalog from App.tsx (which owns catalog
 * state) so that deletions can optimistically remove entries without a full
 * catalog refetch.
 *
 * @see ADR-021 - Fluent UI v9 only; design tokens; dark mode required
 * @see ADR-008 - BFF endpoint filters for auth; privilege enforced server-side too
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Button,
  Tooltip,
  Divider,
} from "@fluentui/react-components";
import { ArrowClockwiseRegular } from "@fluentui/react-icons";
import { Report } from "powerbi-client";
import { canEditReports, canAdminReports } from "../hooks/useReportingPrivilege";
import { ReportDropdown } from "./ReportDropdown";
import { ExportButton } from "./ExportButton";
import { ModeToggle } from "./ModeToggle";
import { DeleteReportButton } from "./DeleteReportButton";
import { NewReportButton } from "./NewReportButton";
import { SaveControls } from "./SaveControls";
import type { ReportCatalogItem, ReportMode, UserPrivilege } from "../types";

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  toolbar: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  divider: {
    height: "20px",
    alignSelf: "center",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ReportingToolbarProps {
  /** Current user's privilege level — controls which toolbar items render. */
  privilege: UserPrivilege;
  /** Current report render mode (view / edit). */
  reportMode: ReportMode;
  /** Currently selected report, or null if nothing is selected. */
  selectedReport: ReportCatalogItem | null;
  /**
   * All reports in the catalog — used by ReportDropdown for the option list
   * and by DeleteReportButton to resolve the next selection after deletion.
   */
  reports: ReportCatalogItem[];
  /** Ref to the embedded Power BI Report object for mode switching. */
  reportRef: React.RefObject<Report | null>;
  /** True while the embed token is loading (or auto-refreshing). */
  tokenLoading: boolean;
  /**
   * Power BI workspace ID — required for Save As (targetWorkspaceId).
   * Comes from the embed token response; null until a report is loaded.
   */
  workspaceId: string | null;
  /** Called when the user selects a different report. */
  onReportSelect: (report: ReportCatalogItem) => void;
  /** Called when the user changes the report mode. */
  onModeChange: (mode: ReportMode) => void;
  /** Called when the refresh button is clicked. */
  onRefresh: () => void;
  /**
   * Called after successful deletion.
   * Receives the next report to select (or null if catalog is now empty).
   */
  onReportDeleted: (nextReport: ReportCatalogItem | null) => void;
  /**
   * Called after a new report is successfully created via NewReportButton.
   * App.tsx uses this to refresh the catalog and switch to edit mode.
   */
  onReportCreated: (newReport: ReportCatalogItem) => void;
  /**
   * Called after a Save As succeeds via SaveControls.
   * App.tsx uses this to refresh the catalog and select the new copy.
   */
  onSaveAsComplete: (newReport: ReportCatalogItem) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Privilege-aware toolbar rendered in the Reporting page header.
 *
 * Extracts all toolbar logic from App.tsx so the shell stays focused on
 * layout and state management while this component owns privilege-gated
 * rendering decisions.
 */
export const ReportingToolbar: React.FC<ReportingToolbarProps> = ({
  privilege,
  reportMode,
  selectedReport,
  reports,
  reportRef,
  tokenLoading,
  workspaceId,
  onReportSelect,
  onModeChange,
  onRefresh,
  onReportDeleted,
  onReportCreated,
  onSaveAsComplete,
}) => {
  const styles = useStyles();

  const canEdit = canEditReports(privilege);
  const canAdmin = canAdminReports(privilege);
  const isDisabled = !selectedReport || tokenLoading;

  // The presentational ReportDropdown fires the report ID string; resolve to
  // the full catalog item before calling onReportSelect.
  const handleDropdownSelect = React.useCallback(
    (reportId: string) => {
      const report = reports.find((r) => r.id === reportId);
      if (report) {
        onReportSelect(report);
      }
    },
    [reports, onReportSelect]
  );

  return (
    <div className={styles.toolbar}>
      {/* Report selector dropdown — visible to all roles */}
      <ReportDropdown
        reports={reports}
        selectedReportId={selectedReport?.id ?? null}
        onReportSelect={handleDropdownSelect}
        loading={reports.length === 0 && tokenLoading}
      />

      {/* Refresh button — visible to all roles */}
      <Tooltip content="Refresh report" relationship="label">
        <Button
          appearance="subtle"
          icon={<ArrowClockwiseRegular />}
          aria-label="Refresh report"
          disabled={isDisabled}
          onClick={onRefresh}
        />
      </Tooltip>

      {/* Export button — visible to all roles (task 023) */}
      <Divider vertical className={styles.divider} />
      <ExportButton
        reportId={selectedReport?.id ?? null}
        disabled={isDisabled}
      />

      {/* Author / Admin-only controls */}
      {canEdit && (
        <>
          <Divider vertical className={styles.divider} />

          {/* New Report — create a blank report and open in edit mode */}
          <NewReportButton
            datasetId={selectedReport?.datasetId ?? null}
            disabled={tokenLoading}
            onReportCreated={onReportCreated}
          />

          {/* Mode toggle — switches between view and edit mode */}
          <ModeToggle
            mode={reportMode}
            onModeChange={onModeChange}
            report={reportRef.current}
            reportId={selectedReport?.id ?? null}
            disabled={isDisabled}
          />

          {/* Save / Save As — only visible in edit mode */}
          {reportMode === "edit" && (
            <>
              <Divider vertical className={styles.divider} />
              <SaveControls
                report={reportRef.current}
                selectedReport={selectedReport}
                workspaceId={workspaceId}
                disabled={isDisabled}
                onSaveAsComplete={onSaveAsComplete}
              />
            </>
          )}
        </>
      )}

      {/* Delete button — visible to Admin only (not rendered for Viewer or Author) */}
      {canAdmin && (
        <>
          <Divider vertical className={styles.divider} />
          <DeleteReportButton
            selectedReport={selectedReport}
            reports={reports}
            onDeleted={onReportDeleted}
            disabled={isDisabled}
          />
        </>
      )}
    </div>
  );
};
