/**
 * useEmbedToken.ts
 * Custom hook to fetch a Power BI embed token from the BFF.
 *
 * Delegates to fetchEmbedToken() from reportingApi.ts which calls
 * GET /api/reporting/embed-token?reportId=...&allowEdit=... with a Bearer token.
 *
 * The BFF caches embed tokens in Redis (ADR-009) and returns a refreshAfter
 * hint at 80% of the token TTL (EMBED_TOKEN_REFRESH_THRESHOLD).
 *
 * Returns { embedConfig, loading, error, refreshToken }.
 *
 * @see reportingApi.ts - fetchEmbedToken()
 * @see ReportViewer.tsx - ReportEmbedConfig type
 */

import * as React from "react";
import { fetchEmbedToken } from "../services/reportingApi";
import type { ReportEmbedConfig } from "../components/ReportViewer";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseEmbedTokenParams {
  /**
   * The sprk_report record ID (Dataverse GUID).
   * Pass null when no report is selected — skips the fetch.
   */
  reportId: string | null;
  /**
   * When true, requests an edit-capable token (Author / Admin users only).
   * Defaults to false (view-only token).
   */
  allowEdit?: boolean;
}

export interface UseEmbedTokenResult {
  /**
   * Resolved embed config ready for ReportViewer.
   * Null while loading, or when no reportId is provided, or on error.
   */
  embedConfig: ReportEmbedConfig | null;
  /** True while the token fetch is in progress. */
  loading: boolean;
  /** Error message if the fetch failed, or null. */
  error: string | null;
  /**
   * Re-fetch the embed token manually.
   * Prefer using report.setAccessToken() for proactive refresh — this is for
   * explicit user-initiated refresh (e.g. clicking the Refresh button).
   */
  refreshToken: () => void;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Fetches a Power BI embed token for the given reportId.
 * Skips the fetch when reportId is null (no report selected).
 * Re-fetches when reportId, allowEdit, or refreshKey changes.
 */
export function useEmbedToken({
  reportId,
  allowEdit = false,
}: UseEmbedTokenParams): UseEmbedTokenResult {
  const [embedConfig, setEmbedConfig] = React.useState<ReportEmbedConfig | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [refreshKey, setRefreshKey] = React.useState(0);

  React.useEffect(() => {
    if (!reportId) {
      setEmbedConfig(null);
      setLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;

    const load = async () => {
      setLoading(true);
      setError(null);

      const result = await fetchEmbedToken(reportId, allowEdit);

      if (cancelled) return;

      if (result.ok) {
        const { token, embedUrl, reportId: pbiReportId, expiration, refreshAfter, workspaceId } = result.data;

        setEmbedConfig({
          id: pbiReportId,
          embedUrl,
          accessToken: token,
          expiry: expiration,
          refreshAfter,
          workspaceId,
        });
        setError(null);
      } else {
        console.error("[useEmbedToken] Failed to fetch embed token:", result.error);
        setError(result.error);
        setEmbedConfig(null);
      }

      setLoading(false);
    };

    void load();

    return () => {
      cancelled = true;
    };
  }, [reportId, allowEdit, refreshKey]);

  const refreshToken = React.useCallback(() => {
    setRefreshKey((k) => k + 1);
  }, []);

  return { embedConfig, loading, error, refreshToken };
}
