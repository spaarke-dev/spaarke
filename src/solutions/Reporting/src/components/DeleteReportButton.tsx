/**
 * DeleteReportButton.tsx
 * Admin-only button that deletes the currently selected report.
 *
 * Shows a Fluent v9 confirmation Dialog before calling
 * DELETE /api/reporting/reports/{reportId}. On success the report is
 * removed from the catalog and the next available report is selected.
 *
 * This component is ONLY rendered when the current user has Admin privilege.
 * The BFF also enforces this server-side via ReportingAuthorizationFilter.
 *
 * @see ADR-021 - Fluent UI v9 only; design tokens; dark mode required
 * @see ADR-008 - BFF endpoint filters for auth
 */

import * as React from "react";
import {
  Button,
  Dialog,
  DialogTrigger,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogContent,
  DialogActions,
  Spinner,
  MessageBar,
  MessageBarBody,
  Tooltip,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";
import { DeleteRegular } from "@fluentui/react-icons";
import { deleteReport } from "../services/reportingApi";
import type { ReportCatalogItem } from "../types";

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  errorWrapper: {
    marginTop: tokens.spacingVerticalS,
  },
  reportName: {
    fontWeight: tokens.fontWeightSemibold,
  },
  dialogActions: {
    display: "flex",
    gap: tokens.spacingHorizontalS,
    justifyContent: "flex-end",
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface DeleteReportButtonProps {
  /** The currently selected report to delete. */
  selectedReport: ReportCatalogItem | null;
  /** Full list of reports — used to select the next report after deletion. */
  reports: ReportCatalogItem[];
  /** Called after successful deletion with the next report to select (or null). */
  onDeleted: (nextReport: ReportCatalogItem | null) => void;
  /** Whether the button is disabled (e.g. no report loaded). */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Admin-only delete button with confirmation dialog.
 *
 * Renders a subtle icon-only "Delete report" button. When clicked, a
 * Fluent v9 Dialog prompts the Admin to confirm before the DELETE
 * request is issued. On success, the parent is notified via onDeleted()
 * with the next available report in the catalog.
 */
export const DeleteReportButton: React.FC<DeleteReportButtonProps> = ({
  selectedReport,
  reports,
  onDeleted,
  disabled = false,
}) => {
  const styles = useStyles();

  const [dialogOpen, setDialogOpen] = React.useState(false);
  const [deleting, setDeleting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // ---- Helpers ----------------------------------------------------------------

  /** Pick the next report after the deleted one, or the previous one if at end. */
  const resolveNextReport = React.useCallback(
    (deletedId: string): ReportCatalogItem | null => {
      const remaining = reports.filter((r) => r.id !== deletedId);
      if (remaining.length === 0) return null;

      const deletedIndex = reports.findIndex((r) => r.id === deletedId);
      // Try the report after the deleted position first, then fall back to the last one
      return remaining[Math.min(deletedIndex, remaining.length - 1)];
    },
    [reports]
  );

  // ---- Handlers ---------------------------------------------------------------

  const handleOpenChange = React.useCallback(
    (_: unknown, data: { open: boolean }) => {
      if (!data.open) {
        // Reset error state when dialog closes
        setError(null);
      }
      setDialogOpen(data.open);
    },
    []
  );

  const handleConfirmDelete = React.useCallback(async () => {
    if (!selectedReport || deleting) return;

    setDeleting(true);
    setError(null);

    const result = await deleteReport(selectedReport.id);

    if (!result.ok) {
      setDeleting(false);
      setError(`Failed to delete report: ${result.error}`);
      return;
    }

    setDeleting(false);
    setDialogOpen(false);

    const nextReport = resolveNextReport(selectedReport.id);
    onDeleted(nextReport);
  }, [selectedReport, deleting, resolveNextReport, onDeleted]);

  const handleCancel = React.useCallback(() => {
    setDialogOpen(false);
    setError(null);
  }, []);

  // ---- Render -----------------------------------------------------------------

  return (
    <Dialog open={dialogOpen} onOpenChange={handleOpenChange}>
      <DialogTrigger disableButtonEnhancement>
        <Tooltip content="Delete report" relationship="label">
          <Button
            appearance="subtle"
            size="medium"
            icon={<DeleteRegular />}
            aria-label="Delete report"
            disabled={disabled || !selectedReport}
          />
        </Tooltip>
      </DialogTrigger>

      <DialogSurface>
        <DialogBody>
          <DialogTitle>Delete report?</DialogTitle>

          <DialogContent>
            <Text>
              Are you sure you want to delete{" "}
              <span className={styles.reportName}>
                {selectedReport?.name ?? "this report"}
              </span>
              ? This action cannot be undone.
            </Text>

            {error && (
              <div className={styles.errorWrapper} role="alert" aria-live="assertive">
                <MessageBar intent="error" layout="multiline">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              </div>
            )}
          </DialogContent>

          <DialogActions className={styles.dialogActions}>
            <Button
              appearance="secondary"
              onClick={handleCancel}
              disabled={deleting}
            >
              Cancel
            </Button>

            <Button
              appearance="primary"
              onClick={handleConfirmDelete}
              disabled={deleting || !selectedReport}
              icon={deleting ? <Spinner size="tiny" /> : undefined}
            >
              {deleting ? "Deleting…" : "Delete"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
