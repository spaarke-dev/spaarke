/**
 * useTokenRefresh.ts
 * Proactive Power BI embed token refresh hook.
 *
 * The BFF returns `refreshAfter` (an ISO-8601 DateTimeOffset) in the embed
 * config, calculated at 80% of the token TTL. This hook schedules a timer
 * for that point in time and silently refreshes the token by calling
 * report.setAccessToken(newToken) — no page reload required.
 *
 * Fallback: also registers the Power BI SDK `tokenExpired` event. In normal
 * operation this event should never fire (proactive refresh prevents expiry).
 * If it does fire a warning is logged and a full re-fetch is triggered.
 *
 * Error recovery: if a refresh attempt fails, the hook retries once after
 * 10 seconds before surfacing the error via onRefreshError.
 *
 * All timers and event listeners are cleaned up on unmount to prevent leaks.
 *
 * @see ADR-009 - Redis-first caching; embed tokens are cached BFF-side
 * @see ReportViewer.tsx  - exposes onReportReady for the Report reference
 * @see useEmbedToken.ts  - supplies embedConfig with refreshAfter
 */

import * as React from "react";
import { Report } from "powerbi-client";
import { fetchEmbedToken } from "../services/reportingApi";
import type { ReportEmbedConfig } from "../components/ReportViewer";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Milliseconds to wait before a single retry after a refresh failure. */
const REFRESH_RETRY_DELAY_MS = 10_000;

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseTokenRefreshOptions {
  /**
   * Mutable ref holding the embedded Report instance.
   * Populated by ReportViewer via onReportReady / onReportLoaded.
   * May be null while the report is still loading.
   */
  reportRef: React.MutableRefObject<Report | null>;
  /**
   * Current embed config supplied by useEmbedToken.
   * When this changes (new report selected or manual refresh) the timer is
   * reset to the new refreshAfter timestamp.
   */
  embedConfig: ReportEmbedConfig | null;
  /**
   * The sprk_report Dataverse record ID used to call the BFF embed-token
   * endpoint on refresh. Must match the ID used to obtain embedConfig.
   */
  reportId: string | null;
  /**
   * Optional — called after the retry also fails so the caller can surface
   * an error to the user (e.g. show a MessageBar).
   */
  onRefreshError?: (error: Error) => void;
}

export interface UseTokenRefreshResult {
  /** True while a background token refresh fetch is in-flight. */
  isRefreshing: boolean;
  /** Timestamp of the most recent successful token refresh, or null. */
  lastRefreshed: Date | null;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Schedules proactive Power BI embed token refresh at the time indicated by
 * embedConfig.refreshAfter, then calls report.setAccessToken(newToken) to
 * update the running embed without a page reload.
 *
 * Usage:
 *   const { isRefreshing, lastRefreshed } = useTokenRefresh({
 *     reportRef,
 *     embedConfig,
 *     reportId: selectedReport?.id ?? null,
 *     onRefreshError: (err) => setTokenError(err.message),
 *   });
 */
export function useTokenRefresh({
  reportRef,
  embedConfig,
  reportId,
  onRefreshError,
}: UseTokenRefreshOptions): UseTokenRefreshResult {
  const [isRefreshing, setIsRefreshing] = React.useState(false);
  const [lastRefreshed, setLastRefreshed] = React.useState<Date | null>(null);

  // Stable refs to avoid stale closures inside setTimeout callbacks
  const reportIdRef = React.useRef(reportId);
  const onRefreshErrorRef = React.useRef(onRefreshError);
  const isRefreshingRef = React.useRef(false);

  React.useEffect(() => {
    reportIdRef.current = reportId;
  }, [reportId]);

  React.useEffect(() => {
    onRefreshErrorRef.current = onRefreshError;
  }, [onRefreshError]);

  // ---------------------------------------------------------------------------
  // Core refresh logic (extracted so it can be called from both the timer and
  // the tokenExpired fallback event handler).
  // ---------------------------------------------------------------------------

  /**
   * Fetches a new embed token from the BFF and calls report.setAccessToken().
   * On failure, waits REFRESH_RETRY_DELAY_MS then tries once more.
   * If the retry also fails, calls onRefreshError.
   */
  const performRefresh = React.useCallback(
    async (attempt: number = 1): Promise<void> => {
      const currentReportId = reportIdRef.current;
      const report = reportRef.current;

      if (!currentReportId || !report) {
        // Nothing to refresh — report not yet loaded or no report selected.
        return;
      }

      if (isRefreshingRef.current) {
        // Prevent overlapping concurrent refreshes.
        return;
      }

      isRefreshingRef.current = true;
      setIsRefreshing(true);

      try {
        const result = await fetchEmbedToken(currentReportId, false);

        if (result.ok) {
          await report.setAccessToken(result.data.token);
          setLastRefreshed(new Date());
          console.info(
            `[useTokenRefresh] Token refreshed successfully (attempt ${attempt}). ` +
              `Next refresh scheduled by refreshAfter: ${result.data.refreshAfter}`
          );
        } else {
          throw new Error(result.error);
        }
      } catch (err) {
        const error = err instanceof Error ? err : new Error(String(err));
        console.error(`[useTokenRefresh] Token refresh failed (attempt ${attempt}):`, error);

        if (attempt < 2) {
          // Single retry after REFRESH_RETRY_DELAY_MS
          console.info(
            `[useTokenRefresh] Scheduling retry in ${REFRESH_RETRY_DELAY_MS / 1000}s…`
          );
          // Release the in-progress lock before the retry delay so the timeout
          // set below can acquire it.
          isRefreshingRef.current = false;
          setIsRefreshing(false);

          setTimeout(() => {
            void performRefresh(2);
          }, REFRESH_RETRY_DELAY_MS);

          return; // Do NOT fall through to the finally block reset below.
        }

        // Both attempts failed — surface to caller.
        console.error("[useTokenRefresh] Both refresh attempts failed. Notifying caller.");
        onRefreshErrorRef.current?.(error);
      } finally {
        // Only release the lock on the first attempt path and on the retry path.
        // The early-return above handles the retry scheduling without resetting here.
        if (attempt === 1 || attempt === 2) {
          isRefreshingRef.current = false;
          setIsRefreshing(false);
        }
      }
    },
    // reportRef is a mutable ref — stable identity, no need in deps array.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    []
  );

  // ---------------------------------------------------------------------------
  // Timer + tokenExpired event registration
  // Fires whenever embedConfig changes (new report or manual refresh).
  // ---------------------------------------------------------------------------

  React.useEffect(() => {
    if (!embedConfig?.refreshAfter || !reportId) {
      // No config or no report — nothing to schedule.
      return;
    }

    // Calculate milliseconds until the refreshAfter timestamp.
    const refreshAt = new Date(embedConfig.refreshAfter).getTime();
    const now = Date.now();
    const delayMs = Math.max(0, refreshAt - now);

    console.info(
      `[useTokenRefresh] Scheduling proactive token refresh in ${Math.round(delayMs / 1000)}s ` +
        `(refreshAfter: ${embedConfig.refreshAfter})`
    );

    // Schedule proactive refresh.
    const proactiveTimer = setTimeout(() => {
      void performRefresh(1);
    }, delayMs);

    // ---------------------------------------------------------------------------
    // tokenExpired fallback — should never fire if proactive refresh succeeds.
    // Register after report is ready (poll until report ref is populated).
    // ---------------------------------------------------------------------------

    let tokenExpiredCleanup: (() => void) | null = null;

    const registerTokenExpiredHandler = () => {
      const report = reportRef.current;
      if (!report) return;

      const handler = () => {
        console.warn(
          "[useTokenRefresh] tokenExpired event fired — proactive refresh did not prevent expiry. " +
            "Initiating emergency token refresh."
        );
        void performRefresh(1);
      };

      report.on("tokenExpired", handler);

      tokenExpiredCleanup = () => {
        try {
          report.off("tokenExpired", handler);
        } catch {
          // Ignore errors when the embed has already been destroyed.
        }
      };
    };

    // Try to register immediately; if the report isn't ready yet, poll briefly.
    registerTokenExpiredHandler();

    // If report ref wasn't available immediately, poll every 500ms for up to 5s.
    let pollAttempts = 0;
    const MAX_POLL_ATTEMPTS = 10;
    let pollTimer: ReturnType<typeof setTimeout> | null = null;

    if (!tokenExpiredCleanup) {
      const poll = () => {
        pollAttempts++;
        registerTokenExpiredHandler();

        if (!tokenExpiredCleanup && pollAttempts < MAX_POLL_ATTEMPTS) {
          pollTimer = setTimeout(poll, 500);
        } else if (pollAttempts >= MAX_POLL_ATTEMPTS && !tokenExpiredCleanup) {
          console.warn(
            "[useTokenRefresh] Could not register tokenExpired handler — report ref not available."
          );
        }
      };
      pollTimer = setTimeout(poll, 500);
    }

    // Cleanup: clear timer and deregister event handler on unmount or config change.
    return () => {
      clearTimeout(proactiveTimer);
      if (pollTimer) clearTimeout(pollTimer);
      tokenExpiredCleanup?.();
    };
    // performRefresh is stable (useCallback with empty deps).
    // reportRef is a mutable ref — not reactive.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [embedConfig?.refreshAfter, embedConfig?.accessToken, reportId, performRefresh]);

  return { isRefreshing, lastRefreshed };
}
