/**
 * SaveControls.tsx
 * Save and Save As buttons for the Reporting toolbar (edit mode only).
 *
 * - "Save" calls report.save() then PATCHes the sprk_report catalog entry
 *   via PATCH /api/reporting/reports/{id} to keep modified date in sync.
 * - "Save As" opens a Fluent v9 Dialog for the new report name, calls
 *   report.saveAs({name, targetWorkspaceId}) (keeping the user's unsaved edits), captures the new Power BI report id
 *   from the SDK's "saved" event, then POSTs it to /api/reporting/reports with the SOURCE catalog row id. The BFF
 *   verifies the copy is in the source row's workspace before registering it (unified-access-control-r2 task 166 r1).
 *
 * Both buttons are only shown in edit mode for Author / Admin users.
 * Results are communicated via toast notifications (Fluent v9 Toast).
 * The caller must provide a <Toaster> instance — this component uses the
 * useToastController hook to dispatch toasts to the nearest Toaster.
 *
 * @see ADR-021 - Fluent UI v9 only; design tokens; dark mode required
 * @see ADR-008 - BFF endpoint filters for auth
 *
 * Power BI SDK reference:
 *   report.save()                              — in-place save
 *   report.saveAs({ name, targetWorkspaceId }) — copy to new report
 *   report.on("saved", handler)               — fires after save completes
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
  useToastController,
  Toast,
  ToastTitle,
  ToastBody,
  ToastIntent,
} from "@fluentui/react-components";
import { SaveRegular, SaveCopyRegular } from "@fluentui/react-icons";
import { updateReport, saveAsReport } from "../services/reportingApi";
import type { ReportCatalogItem } from "../types";

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
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
// Minimal PBI Report ref interface
// ---------------------------------------------------------------------------

/**
 * Subset of the Power BI Report API surface used by SaveControls.
 * Keeps the component decoupled from the full SDK type.
 */
export interface SaveableReport {
  /** Trigger an in-place save of the report. */
  save(): Promise<void>;
  /** Save the report as a new copy. */
  saveAs(config: { name: string; targetWorkspaceId: string }): Promise<void>;
  /** Register for Power BI report events. */
  on<T>(eventName: string, handler: (event: CustomEvent<T>) => void): void;
  /** Remove an event listener. */
  off(eventName: string): void;
}

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

/** How long Save As waits for the SDK's "saved" event that carries the new report id (task 166 r1). */
const SAVED_EVENT_TIMEOUT_MS = 30_000;

/** Toaster ID to dispatch toasts to. Must match the id on the <Toaster> element. */
export const REPORTING_TOASTER_ID = "reporting-toaster";

export interface SaveControlsProps {
  /** The embedded Power BI report reference (null when no report is loaded). */
  report: SaveableReport | null;
  /** The currently selected report's Dataverse catalog entry. */
  selectedReport: ReportCatalogItem | null;
  /**
   * The Power BI workspace ID for the customer's workspace.
   * Required for saveAs — passed as targetWorkspaceId.
   */
  workspaceId: string | null;
  /** Whether the controls are disabled (e.g. while token is loading). */
  disabled?: boolean;
  /**
   * Called after saveAs succeeds so the parent can:
   *   1. Refresh the report catalog dropdown
   *   2. Select the newly saved copy
   */
  onSaveAsComplete: (newReport: ReportCatalogItem) => void;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Save and Save As toolbar controls for edit mode.
 *
 * Manages its own dialog state and surfaces results via Fluent v9 toasts.
 * Registers a `saved` event listener on the report for logging purposes;
 * the actual BFF sync is triggered imperatively from the save handlers.
 */
export const SaveControls: React.FC<SaveControlsProps> = ({
  report,
  selectedReport,
  workspaceId,
  disabled = false,
  onSaveAsComplete,
}) => {
  const styles = useStyles();
  const { dispatchToast } = useToastController(REPORTING_TOASTER_ID);

  // -- Save state --
  const [saving, setSaving] = React.useState(false);

  // -- The most recent "saved" event's new report id (Save As) — task 166 r1 --
  const savedAsReportIdRef = React.useRef<{ id: string | null; waiters: Array<(id: string | null) => void> }>({
    id: null,
    waiters: [],
  });

  // -- Save As dialog state --
  const [saveAsOpen, setSaveAsOpen] = React.useState(false);
  const [saveAsName, setSaveAsName] = React.useState("");
  const [savingAs, setSavingAs] = React.useState(false);
  const [saveAsError, setSaveAsError] = React.useState<string | null>(null);

  // -------------------------------------------------------------------------
  // Helper: dispatch a toast
  // -------------------------------------------------------------------------

  const showToast = React.useCallback(
    (title: string, body: string | undefined, intent: ToastIntent) => {
      dispatchToast(
        <Toast>
          <ToastTitle>{title}</ToastTitle>
          {body && <ToastBody>{body}</ToastBody>}
        </Toast>,
        { intent, pauseOnHover: true }
      );
    },
    [dispatchToast]
  );

  // -------------------------------------------------------------------------
  // Register report "saved" event — fires when PBI confirms save completion
  // -------------------------------------------------------------------------

  React.useEffect(() => {
    if (!report) return;

    const handleSaved = (event: CustomEvent<{ reportObjectId?: string; saveAs?: boolean }>) => {
      console.info("[SaveControls] Power BI 'saved' event received");
      // A Save As reports the NEW report's id; hand it to the waiting Save As handler (task 166 r1).
      if (event?.detail?.saveAs && event.detail.reportObjectId) {
        const state = savedAsReportIdRef.current;
        state.id = event.detail.reportObjectId;
        state.waiters.splice(0).forEach((resolve) => resolve(state.id));
      }
    };

    report.on<{ reportObjectId?: string; saveAs?: boolean }>("saved", handleSaved);

    return () => {
      report.off("saved");
    };
  }, [report]);

  // -------------------------------------------------------------------------
  // Save handler (in-place)
  // -------------------------------------------------------------------------

  const handleSave = React.useCallback(async () => {
    if (!report || !selectedReport || saving) return;

    setSaving(true);

    try {
      await report.save();

      // Sync modified date in the sprk_report Dataverse record
      const result = await updateReport(selectedReport.id, {});

      if (!result.ok) {
        console.warn("[SaveControls] BFF catalog sync failed after save:", result.error);
        showToast(
          "Report saved",
          "Report saved in Power BI but catalog sync failed. Changes may not appear immediately.",
          "warning"
        );
      } else {
        showToast("Report saved", undefined, "success");
      }
    } catch (err) {
      console.error("[SaveControls] Save failed:", err);
      showToast(
        "Save failed",
        `Could not save the report: ${String(err)}`,
        "error"
      );
    } finally {
      setSaving(false);
    }
  }, [report, selectedReport, saving, showToast]);

  // -------------------------------------------------------------------------
  // Save As dialog handlers
  // -------------------------------------------------------------------------

  const handleSaveAsOpenChange = React.useCallback(
    (_: unknown, data: { open: boolean }) => {
      if (data.open) {
        setSaveAsName(selectedReport?.name ? `${selectedReport.name} (copy)` : "");
        setSaveAsError(null);
      }
      setSaveAsOpen(data.open);
    },
    [selectedReport]
  );

  const handleSaveAs = React.useCallback(async () => {
    const trimmedName = saveAsName.trim();
    if (!trimmedName) {
      setSaveAsError("Please enter a name for the report copy.");
      return;
    }
    if (!report || !selectedReport) {
      setSaveAsError("No report is currently selected.");
      return;
    }
    if (!workspaceId) {
      setSaveAsError("Workspace information is unavailable. Please try again.");
      return;
    }

    setSavingAs(true);
    setSaveAsError(null);

    try {
      // Step 1: Call report.saveAs() via the PBI SDK, and wait for the "saved" event that names the new report.
      const state = savedAsReportIdRef.current;
      state.id = null;
      const newReportId = new Promise<string | null>((resolve) => {
        state.waiters.push(resolve);
        setTimeout(() => resolve(state.id), SAVED_EVENT_TIMEOUT_MS);
      });
      await report.saveAs({ name: trimmedName, targetWorkspaceId: workspaceId });
      const pbiReportId = await newReportId;

      if (!pbiReportId) {
        showToast(
          "Report copied",
          "Report saved in Power BI but its id was not reported, so it could not be registered in the catalog.",
          "warning"
        );
        setSaveAsOpen(false);
        return;
      }

      // Step 2: Register the copy as a new sprk_report catalog row via the BFF (by its SOURCE catalog row).
      const result = await saveAsReport({
        name: trimmedName,
        sourceReportId: selectedReport.id,
        pbiReportId,
      });

      if (!result.ok) {
        console.error("[SaveControls] saveAsReport BFF call failed:", result.error);
        // The PBI save succeeded but catalog sync failed — warn but don't fail
        showToast(
          "Report copied",
          "Report saved in Power BI but could not register in catalog. Please refresh.",
          "warning"
        );
        setSaveAsOpen(false);
        return;
      }

      // Build a catalog item for the new copy so the parent can select it
      const newItem: ReportCatalogItem = {
        id: result.data.reportId,
        name: result.data.name,
        embedUrl: result.data.embedUrl,
        datasetId: selectedReport.datasetId,
        category: "Custom",
        isCustom: true,
      };

      setSaveAsOpen(false);
      showToast("Report saved as", `"${trimmedName}" has been saved.`, "success");
      onSaveAsComplete(newItem);
    } catch (err) {
      console.error("[SaveControls] saveAs failed:", err);
      setSaveAsError(`Save As failed: ${String(err)}`);
    } finally {
      setSavingAs(false);
    }
  }, [saveAsName, report, selectedReport, workspaceId, showToast, onSaveAsComplete]);

  const handleSaveAsKeyDown = React.useCallback(
    (ev: React.KeyboardEvent<HTMLInputElement>) => {
      if (ev.key === "Enter" && !savingAs) {
        void handleSaveAs();
      }
    },
    [savingAs, handleSaveAs]
  );

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------

  return (
    <div className={styles.container}>
      {/* Save button */}
      <Button
        appearance="subtle"
        icon={saving ? <Spinner size="tiny" /> : <SaveRegular />}
        disabled={disabled || saving || !report}
        onClick={() => void handleSave()}
        aria-label="Save report"
      >
        Save
      </Button>

      {/* Save As dialog trigger */}
      <Dialog open={saveAsOpen} onOpenChange={handleSaveAsOpenChange}>
        <DialogTrigger disableButtonEnhancement>
          <Button
            appearance="subtle"
            icon={<SaveCopyRegular />}
            disabled={disabled || saving || savingAs || !report}
            aria-label="Save report as a copy"
          >
            Save As
          </Button>
        </DialogTrigger>

        <DialogSurface>
          <DialogBody>
            <DialogTitle>Save As</DialogTitle>

            <DialogContent>
              {/* Error banner */}
              {saveAsError && (
                <div className={styles.errorWrapper}>
                  <MessageBar intent="error" layout="singleline">
                    <MessageBarBody>{saveAsError}</MessageBarBody>
                  </MessageBar>
                </div>
              )}

              {/* New report name input */}
              <div className={styles.formRow}>
                <Label htmlFor="save-as-name" required>
                  Report name
                </Label>
                <Input
                  id="save-as-name"
                  placeholder="Enter a name for the copy"
                  value={saveAsName}
                  onChange={(_ev, data) => {
                    setSaveAsName(data.value);
                    if (saveAsError) setSaveAsError(null);
                  }}
                  onKeyDown={handleSaveAsKeyDown}
                  disabled={savingAs}
                  autoFocus
                />
              </div>
            </DialogContent>

            <DialogActions>
              <DialogTrigger disableButtonEnhancement>
                <Button appearance="secondary" disabled={savingAs}>
                  Cancel
                </Button>
              </DialogTrigger>
              <Button
                appearance="primary"
                onClick={() => void handleSaveAs()}
                disabled={savingAs || !saveAsName.trim()}
                icon={savingAs ? <Spinner size="tiny" /> : undefined}
              >
                {savingAs ? "Saving…" : "Save As"}
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </div>
  );
};
