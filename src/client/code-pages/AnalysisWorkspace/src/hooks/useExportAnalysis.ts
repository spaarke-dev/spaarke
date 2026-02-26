/**
 * useExportAnalysis - Export analysis to Word/PDF via BFF API
 *
 * Handles the complete export flow: API call, blob download, file save dialog.
 * Tracks export state for UI feedback (idle/exporting/completed/error).
 *
 * @see ADR-007 - Document access through BFF API
 */

import { useCallback, useState } from "react";
import { exportAnalysis } from "../services/analysisApi";
import type { ExportState, ExportFormat } from "../types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseExportAnalysisOptions {
    /** GUID of the analysis record to export */
    analysisId: string;
    /** Bearer auth token for BFF API calls */
    token: string | null;
    /** Title of the analysis (used as default filename) */
    analysisTitle?: string;
}

export interface UseExportAnalysisResult {
    /** Current export state: idle | exporting | completed | error */
    exportState: ExportState;
    /** Error message from the last failed export attempt */
    exportError: string | null;
    /** Trigger an export to the specified format */
    doExport: (format: ExportFormat) => Promise<void>;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Trigger a browser download for a Blob.
 * Creates a temporary anchor element, sets the download attribute, and clicks it.
 */
function downloadBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = filename;
    anchor.style.display = "none";
    document.body.appendChild(anchor);
    anchor.click();

    // Clean up after a brief delay to ensure download starts
    setTimeout(() => {
        URL.revokeObjectURL(url);
        document.body.removeChild(anchor);
    }, 100);
}

/**
 * Get the MIME type for a given export format.
 */
function getMimeType(format: ExportFormat): string {
    switch (format) {
        case "docx":
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        case "pdf":
            return "application/pdf";
        default:
            return "application/octet-stream";
    }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Export hook for downloading analysis content as Word or PDF.
 *
 * @example
 * ```tsx
 * const { exportState, exportError, doExport } = useExportAnalysis({
 *     analysisId: "abc-123",
 *     token: authToken,
 *     analysisTitle: "Contract Analysis",
 * });
 *
 * <Button onClick={() => doExport("docx")} disabled={exportState === "exporting"}>
 *     {exportState === "exporting" ? "Exporting..." : "Export to Word"}
 * </Button>
 * ```
 */
export function useExportAnalysis(options: UseExportAnalysisOptions): UseExportAnalysisResult {
    const { analysisId, token, analysisTitle } = options;

    const [exportState, setExportState] = useState<ExportState>("idle");
    const [exportError, setExportError] = useState<string | null>(null);

    const doExport = useCallback(
        async (format: ExportFormat) => {
            if (!analysisId || !token) {
                setExportError("Cannot export: missing analysis ID or authentication token.");
                setExportState("error");
                return;
            }

            setExportState("exporting");
            setExportError(null);

            try {
                const blob = await exportAnalysis(analysisId, format, token);

                // Generate filename from title or use a default
                const safeName = (analysisTitle ?? "analysis")
                    .replace(/[^a-zA-Z0-9\s-_]/g, "")
                    .replace(/\s+/g, "_")
                    .substring(0, 100);
                const filename = `${safeName}.${format}`;

                // Ensure blob has correct MIME type
                const typedBlob = new Blob([blob], { type: getMimeType(format) });
                downloadBlob(typedBlob, filename);

                setExportState("completed");

                // Reset to idle after a brief indicator
                setTimeout(() => setExportState("idle"), 2000);
            } catch (err: unknown) {
                const message =
                    err && typeof err === "object" && "message" in err
                        ? (err as { message: string }).message
                        : "Failed to export analysis";
                setExportError(message);
                setExportState("error");
                console.error("[useExportAnalysis] Export failed:", err);
            }
        },
        [analysisId, token, analysisTitle]
    );

    return {
        exportState,
        exportError,
        doExport,
    };
}
