/**
 * ExportButton.tsx
 * Export report to PDF or PPTX toolbar button.
 *
 * Visible to all roles (Viewer, Author, Admin).
 * Uses a Fluent v9 Menu anchored to a Button to present PDF / PPTX options.
 *
 * Flow:
 *   1. User clicks "Export to PDF" or "Export to PPTX"
 *   2. POST /api/reporting/export is called with { reportId, format }
 *   3. BFF starts Power BI ExportToFile and polls for completion
 *   4. Frontend polls GET /api/reporting/export/{exportId}/status
 *   5. On completion the file is downloaded via a hidden <a> tag
 *   6. Errors are shown inline via a MessageBar
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
import { exportReport, getExportStatus } from "../services/reportingApi";
import { getBffBaseUrl } from "../config/runtimeConfig";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** How often to poll for export status (ms) */
const POLL_INTERVAL_MS = 3_000;

/** Maximum total wait time before giving up (ms) — 5 minutes */
const POLL_TIMEOUT_MS = 5 * 60_000;

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

/** File extension for each export format. */
function formatExtension(format: ExportFormat): string {
  return format === "PDF" ? "pdf" : "pptx";
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * Split-style menu button for exporting a report to PDF or PPTX.
 * Polls the BFF for export completion and triggers a browser download.
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

  // Refs for cleanup
  const pollTimerRef = React.useRef<ReturnType<typeof setTimeout> | null>(null);
  const elapsedTimerRef = React.useRef<ReturnType<typeof setInterval> | null>(null);
  const startTimeRef = React.useRef<number>(0);

  // ---------------------------------------------------------------------------
  // Cleanup on unmount
  // ---------------------------------------------------------------------------

  React.useEffect(() => {
    return () => {
      if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
      if (elapsedTimerRef.current) clearInterval(elapsedTimerRef.current);
    };
  }, []);

  // ---------------------------------------------------------------------------
  // Download helper
  // ---------------------------------------------------------------------------

  const triggerDownload = React.useCallback(
    (downloadUrl: string, fileName: string) => {
      const a = document.createElement("a");
      // If the URL is relative, make it absolute using the BFF base
      a.href = downloadUrl.startsWith("http")
        ? downloadUrl
        : `${getBffBaseUrl()}${downloadUrl}`;
      a.download = fileName;
      a.style.display = "none";
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
    },
    []
  );

  // ---------------------------------------------------------------------------
  // Polling loop
  // ---------------------------------------------------------------------------

  const stopElapsedTimer = React.useCallback(() => {
    if (elapsedTimerRef.current) {
      clearInterval(elapsedTimerRef.current);
      elapsedTimerRef.current = null;
    }
  }, []);

  const pollForCompletion = React.useCallback(
    async (exportId: string, format: ExportFormat) => {
      const elapsed = Date.now() - startTimeRef.current;

      if (elapsed >= POLL_TIMEOUT_MS) {
        stopElapsedTimer();
        setExportStatus("failed");
        setError("Export timed out. Please try again.");
        return;
      }

      const result = await getExportStatus(exportId);

      if (!result.ok) {
        stopElapsedTimer();
        setExportStatus("failed");
        setError(`Export failed: ${result.error}`);
        return;
      }

      const { status, downloadUrl, fileName } = result.data;

      setExportStatus(status);

      if (status === "completed") {
        stopElapsedTimer();
        const resolvedFileName =
          fileName ?? `report.${formatExtension(format)}`;
        if (downloadUrl) {
          triggerDownload(downloadUrl, resolvedFileName);
        }
        // Reset after a brief delay so the user sees "completed"
        setTimeout(() => {
          setExportStatus(null);
          setActiveFormat(null);
          setElapsedSeconds(0);
          setError(null);
        }, 2_000);
        return;
      }

      if (status === "failed") {
        stopElapsedTimer();
        setError("Export failed. Please try again.");
        return;
      }

      // Still running or pending — schedule next poll
      pollTimerRef.current = setTimeout(
        () => pollForCompletion(exportId, format),
        POLL_INTERVAL_MS
      );
    },
    [stopElapsedTimer, triggerDownload]
  );

  // ---------------------------------------------------------------------------
  // Export handler
  // ---------------------------------------------------------------------------

  const handleExport = React.useCallback(
    async (format: ExportFormat) => {
      if (!reportId || exportStatus === "pending" || exportStatus === "running") return;

      setError(null);
      setActiveFormat(format);
      setExportStatus("pending");
      setElapsedSeconds(0);
      startTimeRef.current = Date.now();

      // Start elapsed-time counter
      elapsedTimerRef.current = setInterval(() => {
        setElapsedSeconds((s) => s + 1);
      }, 1_000);

      const initResult = await exportReport(reportId, format);

      if (!initResult.ok) {
        stopElapsedTimer();
        setExportStatus("failed");
        setError(`Could not start export: ${initResult.error}`);
        return;
      }

      setExportStatus("running");

      // Begin polling
      pollTimerRef.current = setTimeout(
        () => pollForCompletion(initResult.data.exportId, format),
        POLL_INTERVAL_MS
      );
    },
    [reportId, exportStatus, pollForCompletion, stopElapsedTimer]
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
