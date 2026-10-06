/**
 * NewReportButton.tsx
 * "New Report" button for the Reporting toolbar.
 *
 * Only rendered for Author and Admin users (canEditReports check).
 * Opens a Fluent v9 Dialog prompting for a report name, then calls
 * POST /api/reporting/reports with the selected catalog report as the source: the BFF clones it (same workspace and
 * semantic model, both derived server-side from the catalog row) and registers the copy (task 166 r1).
 *
 * On success:
 *   - Calls onReportCreated with the new catalog item
 *   - Caller (App.tsx) switches to edit mode and selects the new report
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
  Input,
  Label,
  Spinner,
  MessageBar,
  MessageBarBody,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { AddRegular } from "@fluentui/react-icons";
import { createReport } from "../services/reportingApi";
import type { ReportCatalogItem } from "../types";

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  formRow: {
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalM,
  },
  errorWrapper: {
    marginBottom: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface NewReportButtonProps {
  /**
   * The catalog report the new one is based on (unified-access-control-r2 task 166 r1). The BFF reads it as the
   * user and clones its Power BI report in the same workspace and dataset — the client names neither.
   * Comes from the currently selected report; null disables creation.
   */
  sourceReport: ReportCatalogItem | null;
  /** Whether the button is disabled (e.g. while a token is loading). */
  disabled?: boolean;
  /** Called with the new catalog item once the BFF has created and registered it. */
  onReportCreated: (newReport: ReportCatalogItem) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const NewReportButton: React.FC<NewReportButtonProps> = ({
  sourceReport,
  disabled = false,
  onReportCreated,
}) => {
  const styles = useStyles();

  const [open, setOpen] = React.useState(false);
  const [reportName, setReportName] = React.useState("");
  const [creating, setCreating] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Reset dialog state when opened
  const handleOpenChange = React.useCallback(
    (_: unknown, data: { open: boolean }) => {
      if (data.open) {
        setReportName("");
        setError(null);
      }
      setOpen(data.open);
    },
    []
  );

  const handleCreate = React.useCallback(async () => {
    const trimmedName = reportName.trim();
    if (!trimmedName) {
      setError("Please enter a report name.");
      return;
    }
    if (!sourceReport) {
      setError("Select an existing report first — the new report is based on it.");
      return;
    }

    setCreating(true);
    setError(null);

    const result = await createReport({ name: trimmedName, sourceReportId: sourceReport.id });

    setCreating(false);

    if (!result.ok) {
      console.error("[NewReportButton] createReport failed:", result.error);
      setError(`Could not create report: ${result.error}`);
      return;
    }

    // Build a minimal ReportCatalogItem from the response so the parent can
    // immediately select the new report without waiting for a full catalog refetch.
    const newItem: ReportCatalogItem = {
      id: result.data.reportId,
      name: result.data.name,
      embedUrl: result.data.embedUrl,
      datasetId: sourceReport.datasetId,
      category: "Custom",
      isCustom: true,
    };

    setOpen(false);
    onReportCreated(newItem);
  }, [reportName, sourceReport, onReportCreated]);

  // Allow Ctrl+Enter / Enter in the text field to trigger creation
  const handleKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLInputElement>) => {
      if (ev.key === "Enter" && !creating) {
        void handleCreate();
      }
    },
    [creating, handleCreate]
  );

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger disableButtonEnhancement>
        <Button
          appearance="subtle"
          icon={<AddRegular />}
          disabled={disabled}
          aria-label="New report"
        >
          New Report
        </Button>
      </DialogTrigger>

      <DialogSurface>
        <DialogBody>
          <DialogTitle>New Report</DialogTitle>

          <DialogContent>
            {/* Error banner */}
            {error && (
              <div className={styles.errorWrapper}>
                <MessageBar intent="error" layout="singleline">
                  <MessageBarBody>{error}</MessageBarBody>
                </MessageBar>
              </div>
            )}

            {/* Report name input */}
            <div className={styles.formRow}>
              <Label htmlFor="new-report-name" required>
                Report name
              </Label>
              <Input
                id="new-report-name"
                placeholder="Enter a name for the new report"
                value={reportName}
                onChange={(_ev, data) => {
                  setReportName(data.value);
                  if (error) setError(null);
                }}
                onKeyDown={handleKeyDown}
                disabled={creating}
                autoFocus
              />
            </div>
          </DialogContent>

          <DialogActions>
            <DialogTrigger disableButtonEnhancement>
              <Button appearance="secondary" disabled={creating}>
                Cancel
              </Button>
            </DialogTrigger>
            <Button
              appearance="primary"
              onClick={() => void handleCreate()}
              disabled={creating || !reportName.trim()}
              icon={creating ? <Spinner size="tiny" /> : undefined}
            >
              {creating ? "Creating…" : "Create"}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
};
