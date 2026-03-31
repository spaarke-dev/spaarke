/**
 * useAiBriefing — React hook that fetches an AI-generated briefing summary
 * from the BFF endpoint when notification channel data is available.
 *
 * Fetches once when channels are loaded, caches the result for the session.
 * Returns loading/success/unavailable/error states for the UI.
 *
 * Usage:
 *   const { briefing, isLoading, isUnavailable, error } = useAiBriefing(channels, loadingState);
 */

import { useState, useEffect, useRef } from "react";
import type { ChannelFetchResult, LoadingState } from "../types/notifications";
import type { DailyBriefingSummaryResponse } from "../services/briefingService";
import { fetchAiBriefing } from "../services/briefingService";

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

export interface UseAiBriefingResult {
  /** The AI-generated briefing text (null if not yet loaded or unavailable). */
  briefing: DailyBriefingSummaryResponse | null;
  /** Whether the briefing is currently being fetched. */
  isLoading: boolean;
  /** Whether the AI service is unavailable (graceful fallback). */
  isUnavailable: boolean;
  /** Reason the AI service is unavailable (for display). */
  unavailableReason: string | null;
  /** Error message if the fetch failed unexpectedly. */
  error: string | null;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

/**
 * Fetch an AI briefing summary when notification data is available.
 *
 * @param channels - Channel fetch results from useNotificationData
 * @param dataLoadingState - Loading state of the notification data
 */
export function useAiBriefing(
  channels: ChannelFetchResult[],
  dataLoadingState: LoadingState
): UseAiBriefingResult {
  const [briefing, setBriefing] = useState<DailyBriefingSummaryResponse | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [isUnavailable, setIsUnavailable] = useState(false);
  const [unavailableReason, setUnavailableReason] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Track whether we already fetched for this set of channels
  const hasFetchedRef = useRef(false);

  useEffect(() => {
    // Only fetch once notification data is fully loaded
    if (dataLoadingState !== "loaded") return;

    // Don't re-fetch if we already have a result
    if (hasFetchedRef.current) return;

    // Check if there are any successful channels to summarize
    const hasData = channels.some((ch) => ch.status === "success");
    if (!hasData) {
      setIsUnavailable(true);
      setUnavailableReason("No notification data to summarize.");
      hasFetchedRef.current = true;
      return;
    }

    let cancelled = false;
    hasFetchedRef.current = true;
    setIsLoading(true);
    setError(null);
    setIsUnavailable(false);

    fetchAiBriefing(channels).then((result) => {
      if (cancelled) return;

      switch (result.status) {
        case "success":
          setBriefing(result.data);
          break;
        case "unavailable":
          setIsUnavailable(true);
          setUnavailableReason(result.reason);
          break;
        case "error":
          setError(result.message);
          break;
      }

      setIsLoading(false);
    });

    return () => {
      cancelled = true;
    };
  }, [channels, dataLoadingState]);

  return { briefing, isLoading, isUnavailable, unavailableReason, error };
}
