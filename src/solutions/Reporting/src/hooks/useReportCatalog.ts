/**
 * useReportCatalog.ts
 * Custom hook to fetch the report catalog from the BFF.
 *
 * Delegates to fetchReports() from reportingApi.ts which calls
 * GET /api/reporting/reports with a Bearer token.
 *
 * Returns { reports, loading, error, refetch }.
 *
 * @see reportingApi.ts - fetchReports()
 */

import * as React from "react";
import { fetchReports } from "../services/reportingApi";
import type { ReportCatalogItem } from "../types";

// Re-export so callers can get the type from this hook module
export type { ReportCatalogItem };

// ---------------------------------------------------------------------------
// Result type
// ---------------------------------------------------------------------------

export interface UseReportCatalogResult {
  /** The fetched report list. Empty array while loading or on error. */
  reports: ReportCatalogItem[];
  /** True while the initial fetch (or a refetch) is in progress. */
  loading: boolean;
  /** Error message if the fetch failed, or null. */
  error: string | null;
  /** Re-fetch the catalog (e.g. after a report is published). */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetches the report catalog from GET /api/reporting/reports on mount.
 * Re-fetches whenever refetch() is called.
 */
export function useReportCatalog(): UseReportCatalogResult {
  const [reports, setReports] = React.useState<ReportCatalogItem[]>([]);
  const [loading, setLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const [fetchKey, setFetchKey] = React.useState(0);

  React.useEffect(() => {
    let cancelled = false;

    const load = async () => {
      setLoading(true);
      setError(null);

      const result = await fetchReports();

      if (cancelled) return;

      if (result.ok) {
        setReports(result.data);
      } else {
        console.error("[useReportCatalog] Failed to fetch catalog:", result.error);
        setError(result.error);
        setReports([]);
      }

      setLoading(false);
    };

    void load();

    return () => {
      cancelled = true;
    };
  }, [fetchKey]);

  const refetch = React.useCallback(() => {
    setFetchKey((k) => k + 1);
  }, []);

  return { reports, loading, error, refetch };
}
