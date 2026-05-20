/**
 * useExportAnalysis - Export analysis to Word/PDF via BFF API
 *
 * Handles the complete export flow: API call, blob download, file save dialog.
 * Tracks export state for UI feedback (idle/exporting/completed/error).
 *
 * Spaarke Auth v2 (task 026): consumes `authenticatedFetch` instead of a
 * snapshotted token. The hook can be called any time; `doExport` will reject
 * if the user hasn't authenticated yet (rejectNotReady from useAuth).
 *
 * @see ADR-007 - Document access through BFF API
 */

import { useCallback, useState } from 'react';
import type { AuthenticatedFetchFn } from '@spaarke/auth';
import { exportAnalysis } from '../services/analysisApi';
import type { ExportState, ExportFormat } from '../types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseExportAnalysisOptions {
  /** GUID of the analysis record to export */
  analysisId: string;
  /** Whether the user is authenticated. */
  isAuthenticated: boolean;
  /** Authenticated fetch from useAuth(). */
  authenticatedFetch: AuthenticatedFetchFn;
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
 */
function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();

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
    case 'docx':
      return 'application/vnd.openxmlformats-officedocument.wordprocessingml.document';
    case 'pdf':
      return 'application/pdf';
    default:
      return 'application/octet-stream';
  }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useExportAnalysis(options: UseExportAnalysisOptions): UseExportAnalysisResult {
  const { analysisId, isAuthenticated, authenticatedFetch, analysisTitle } = options;

  const [exportState, setExportState] = useState<ExportState>('idle');
  const [exportError, setExportError] = useState<string | null>(null);

  const doExport = useCallback(
    async (format: ExportFormat) => {
      if (!analysisId || !isAuthenticated) {
        setExportError('Cannot export: missing analysis ID or not authenticated.');
        setExportState('error');
        return;
      }

      setExportState('exporting');
      setExportError(null);

      try {
        const blob = await exportAnalysis(analysisId, format, authenticatedFetch);

        // Generate filename from title or use a default
        const safeName = (analysisTitle ?? 'analysis')
          .replace(/[^a-zA-Z0-9\s-_]/g, '')
          .replace(/\s+/g, '_')
          .substring(0, 100);
        const filename = `${safeName}.${format}`;

        // Ensure blob has correct MIME type
        const typedBlob = new Blob([blob], { type: getMimeType(format) });
        downloadBlob(typedBlob, filename);

        setExportState('completed');

        // Reset to idle after a brief indicator
        setTimeout(() => setExportState('idle'), 2000);
      } catch (err: unknown) {
        const message =
          err && typeof err === 'object' && 'message' in err
            ? (err as { message: string }).message
            : 'Failed to export analysis';
        setExportError(message);
        setExportState('error');
        console.error('[useExportAnalysis] Export failed:', err);
      }
    },
    [analysisId, isAuthenticated, authenticatedFetch, analysisTitle]
  );

  return {
    exportState,
    exportError,
    doExport,
  };
}
