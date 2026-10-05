/**
 * ExportButton.tsx
 * Export report to PDF or PPTX toolbar button.
 *
 * Visible to all roles (Viewer, Author, Admin).
 * Uses a Fluent v9 Menu anchored to a Button to present PDF / PPTX options.
 *
 * Flow (unified-access-control-r2 task 166 r1 — aligned with the BFF):
 *   1. User clicks "Export to PDF" or "Export to PPTX"
 *   2. POST /api/reporting/export is called with { reportId (catalog row id), format }
 *   3. The BFF runs Power BI ExportToFile to completion and returns the FILE in the response
 *      (there is no export id and no status route — the client used to poll one that never existed)
 *   4. The file is downloaded via a hidden <a> tag over an object URL
 *   5. Errors are shown inline via a MessageBar
 *
 * PBI exports can take 30-60 seconds — a Spinner with elapsed-time text
 * is shown while the export is in progress.
 *
 * @see ADR-021 - Fluent UI v9 only; design tokens; dark mode required
 */

import * as React from "react";
import {
  makeStyles,
  tokens,
  Button,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  MenuDivider,
  Spinner,
  MessageBar,
  MessageBarBody,
  Tooltip,
  Text,
} from "@fluentui/react-components";
import {
  ArrowDownloadRegular,
  DocumentPdfRegular,
  SlideTextRegular,
  ChevronDownRegular,
} from "@fluentui/react-icons";
import type { ExportFormat, ExportStatus } from "../types/reporting";
import { exportReport } from "../services/reportingApi";

// ---------------------------------------------------------------------------
// Styles — Fluent design tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  exportingRow: {
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
  exportingText: {
    color: tokens.colorNeutralForeground2,
  },
  errorMessage: {
    maxWidth: "320px",
  },
  menuIcon: {
    // Ensure the format icon and label are aligned
    display: "flex",
    alignItems: "center",
    gap: tokens.spacingHorizontalXS,
  },
});

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ExportButtonProps {
  /** The report's Dataverse record GUID — passed to the BFF. */
  reportId: string | null;
  /** Whether the button is disabled (e.g. no report loaded). */
  disabled?: boolean;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Human-readable label for each export format. */
function formatLabel(format: ExportFormat): string {
  return format === "PDF" ? "PDF" : "PowerPoint (PPTX)";
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Split-style menu button for exporting a report to PDF or PPTX.
 * Waits for the BFF's export (which returns the file) and triggers a browser download.
 */
export const ExportButton: React.FC<ExportButtonProps> = ({
  reportId,
  disabled = false,
}) => {
  const styles = useStyles();

  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  const [exportStatus, setExportStatus] = React.useState<ExportStatus | null>(null);
  const [activeFormat, setActiveFormat] = React.useState<ExportFormat | null>(null);
  const [elapsedSeconds, setElapsedSeconds] = React.useState(0);
  const [error, setError] = React.useState<string | null>(null);

  // Ref for the elapsed-time counter (cleared on completion and on unmount)
  const elapsedTimerRef = React.useRef<ReturnType<typeof setInterval> | null>(null);

  React.useEffect(() => {
    return () => {
      if (elapsedTimerRef.current) clearInterval(elapsedTimerRef.current);
    };
  }, []);

  const stopElapsedTimer = React.useCallback(() => {
    if (elapsedTimerRef.current) {
      clearInterval(elapsedTimerRef.current);
      elapsedTimerRef.current = null;
    }
  }, []);

  // ---------------------------------------------------------------------------
  // Download helper — the BFF returned the file itself
  // ---------------------------------------------------------------------------

  const triggerDownload = React.useCallback((blob: Blob, fileName: string) => {
    const objectUrl = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = objectUrl;
    a.download = fileName;
    a.style.display = "none";
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    // Give the browser a moment to start the download before releasing the URL.
    setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000);
  }, []);

  // ---------------------------------------------------------------------------
  // Export handler
  // ---------------------------------------------------------------------------

  const handleExport = React.useCallback(
    async (format: ExportFormat) => {
      if (!reportId || exportStatus === "pending" || exportStatus === "running") return;

      setError(null);
      setActiveFormat(format);
      setExportStatus("running");
      setElapsedSeconds(0);

      // Elapsed-time counter — a Power BI export can take 30-60 seconds
      elapsedTimerRef.current = setInterval(() => {
        setElapsedSeconds((s) => s + 1);
      }, 1_000);

      const result = await exportReport(reportId, format);
      stopElapsedTimer();

      if (!result.ok) {
        setExportStatus("failed");
        setError(`Export failed: ${result.error}`);
        return;
      }

      triggerDownload(result.data.blob, result.data.fileName);
      setExportStatus("completed");

      // Reset after a brief delay so the user sees "downloaded"
      setTimeout(() => {
        setExportStatus(null);
        setActiveFormat(null);
        setElapsedSeconds(0);
        setError(null);
      }, 2_000);
    },
    [reportId, exportStatus, stopElapsedTimer, triggerDownload]
  );

  // ---------------------------------------------------------------------------
  // Derived state
  // ---------------------------------------------------------------------------

  const isExporting =
    exportStatus === "pending" || exportStatus === "running";

  const isComplete = exportStatus === "completed";

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className={styles.container}>
      {/* Export menu button */}
      <Menu>
        <MenuTrigger disableButtonEnhancement>
          <Tooltip content="Export report" relationship="label">
            <Button
              appearance="subtle"
              size="medium"
              icon={<ArrowDownloadRegular />}
              iconPosition="before"
              disabled={disabled || isExporting || !reportId}
              aria-label="Export report"
              aria-haspopup="menu"
            >
              Export
              <ChevronDownRegular style={{ marginLeft: tokens.spacingHorizontalXXS }} />
            </Button>
          </Tooltip>
        </MenuTrigger>

        <MenuPopover>
          <MenuList>
            <MenuItem
              icon={<DocumentPdfRegular />}
              onClick={() => handleExport("PDF")}
              aria-label="Export to PDF"
            >
              Export to PDF
            </MenuItem>
            <MenuDivider />
            <MenuItem
              icon={<SlideTextRegular />}
              onClick={() => handleExport("PPTX")}
              aria-label="Export to PowerPoint"
            >
              Export to PowerPoint (PPTX)
            </MenuItem>
          </MenuList>
        </MenuPopover>
      </Menu>

      {/* Progress indicator during export */}
      {isExporting && activeFormat && (
        <div className={styles.exportingRow} role="status" aria-live="polite">
          <Spinner size="tiny" aria-label={`Exporting to ${formatLabel(activeFormat)}`} />
          <Text size={200} className={styles.exportingText}>
            Exporting to {formatLabel(activeFormat)}
            {elapsedSeconds > 0 ? ` (${elapsedSeconds}s)` : "…"}
          </Text>
        </div>
      )}

      {/* Completion confirmation */}
      {isComplete && activeFormat && (
        <div role="status" aria-live="polite">
          <Text size={200} className={styles.exportingText}>
            {formatLabel(activeFormat)} downloaded
          </Text>
        </div>
      )}

      {/* Error message */}
      {error && (
        <div className={styles.errorMessage} role="alert" aria-live="assertive">
          <MessageBar intent="error" layout="singleline">
            <MessageBarBody>{error}</MessageBarBody>
          </MessageBar>
        </div>
      )}
    </div>
  );
};
