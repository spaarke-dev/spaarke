/**
 * useBriefingRender — single-call hook that fetches the Daily Briefing via the
 * BFF `/api/ai/daily-briefing/render` endpoint and returns the typed response.
 *
 * R7 Wave 12 widget cutover (2026-06-30):
 *   Replaces the legacy two-hook chain (`useBriefingNotifications` →
 *   `useBriefingNarration`) that gated /render behind a successful
 *   `appnotification` table load. The old chain meant that when the operator's
 *   appnotification table was empty (notifications dismissed, scheduler not
 *   running, etc.), /render NEVER fired and the widget early-exited to
 *   EmptyState regardless of what live Dataverse data /render WOULD return.
 *
 *   This hook fires /render unconditionally on mount. Empty data is a
 *   property of the response (`status: 'empty'`), not a gate on the call.
 *
 * Contract:
 *   - Fires `POST /api/ai/daily-briefing/render` on mount.
 *   - `refetch()` triggers a new fetch (e.g., for the DigestHeader Refresh
 *     button).
 *   - Status discriminates: idle → loading → (success | empty | unavailable
 *     | error).
 *   - `empty` means the call succeeded but `channelNarratives.length === 0`
 *     AND `tldr` is blank — legitimate "nothing to render" state. Distinct
 *     from `unavailable` (AI service down) and `error` (unexpected failure).
 *
 * The /render endpoint itself reads the caller's AAD oid from the OBO token
 * and resolves systemuserid server-side — no request body, no client-side
 * identity plumbing.
 */

import { useCallback, useEffect, useState } from 'react';
import type { NarrateResponse } from '../services/briefingService';
import { fetchBriefingLive } from '../services/briefingService';

// ---------------------------------------------------------------------------
// Hook types
// ---------------------------------------------------------------------------

export type BriefingRenderStatus = 'idle' | 'loading' | 'success' | 'empty' | 'unavailable' | 'error';

export interface UseBriefingRenderResult {
  /** Discriminator for the current state. */
  status: BriefingRenderStatus;
  /** The /render response. Non-null when status is 'success' or 'empty'. */
  data: NarrateResponse | null;
  /** Human-readable reason when status is 'unavailable'. */
  unavailableReason: string | null;
  /** Human-readable error message when status is 'error'. */
  error: string | null;
  /** Trigger a fresh /render call. */
  refetch: () => void;
  /** Timestamp of the most recent successful response. */
  lastFetchedAt: Date | null;
}

// ---------------------------------------------------------------------------
// Empty detection helper
// ---------------------------------------------------------------------------

function isEmptyResponse(response: NarrateResponse): boolean {
  const hasChannels = response.channelNarratives.length > 0;
  if (hasChannels) return false;
  const tldr = response.tldr;
  const hasTldrText =
    (tldr?.summary?.trim().length ?? 0) > 0 ||
    (tldr?.topAction?.trim().length ?? 0) > 0 ||
    (Array.isArray(tldr?.keyTakeaways) && tldr.keyTakeaways.length > 0);
  if (hasTldrText) return false;
  // R7 W12 feedback item 9 (2026-07-01): high-priority items ALONE are enough
  // to keep the widget out of the empty state — operators want flagged records
  // visible even when narrative channels are empty.
  const hasHighPriority = Array.isArray(response.highPriorityItems) && response.highPriorityItems.length > 0;
  return !hasHighPriority;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

/**
 * Fetch the Daily Briefing render response. Fires once on mount, again on
 * `refetch()`. The /render call has no request body and authenticates via
 * the OBO token — the BFF resolves the caller's systemuserid server-side.
 */
export function useBriefingRender(): UseBriefingRenderResult {
  const [status, setStatus] = useState<BriefingRenderStatus>('idle');
  const [data, setData] = useState<NarrateResponse | null>(null);
  const [unavailableReason, setUnavailableReason] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lastFetchedAt, setLastFetchedAt] = useState<Date | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    let cancelled = false;
    setStatus('loading');
    setError(null);
    setUnavailableReason(null);

    fetchBriefingLive().then(result => {
      if (cancelled) return;

      switch (result.status) {
        case 'success': {
          setData(result.data);
          setLastFetchedAt(new Date());
          setStatus(isEmptyResponse(result.data) ? 'empty' : 'success');
          break;
        }
        case 'unavailable': {
          setData(null);
          setUnavailableReason(result.reason);
          setStatus('unavailable');
          break;
        }
        case 'error': {
          setData(null);
          setError(result.message);
          setStatus('error');
          break;
        }
      }
    });

    return () => {
      cancelled = true;
    };
  }, [refreshKey]);

  const refetch = useCallback(() => {
    setRefreshKey(k => k + 1);
  }, []);

  return {
    status,
    data,
    unavailableReason,
    error,
    refetch,
    lastFetchedAt,
  };
}
