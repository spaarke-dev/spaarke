/**
 * useBriefingNarration — React hook that fetches AI-generated narration
 * (TL;DR + per-channel narrative bullets) from the BFF endpoint when
 * notification channel data is available.
 *
 * Replaces useAiBriefing with richer per-channel narrative output.
 * Fetches once when channels are loaded, caches the result for the session.
 * Falls back to template-based bullets when AI is unavailable.
 *
 * Usage:
 *   const { tldr, channelNarratives, isLoading, error } = useBriefingNarration(channels, loadingState);
 */

import { useState, useEffect, useRef } from "react";
import type { ChannelFetchResult, LoadingState } from "../types/notifications";
import type {
  TldrResult,
  ChannelNarrationResult,
  NarrativeBulletResult,
} from "../services/briefingService";
import { fetchBriefingNarration } from "../services/briefingService";

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

export interface UseBriefingNarrationResult {
  /** The AI-generated TL;DR summary (null if not yet loaded or unavailable). */
  tldr: TldrResult | null;
  /** Per-channel narrative bullets (AI or template fallback). */
  channelNarratives: ChannelNarrationResult[];
  /** Whether the narration is currently being fetched. */
  isLoading: boolean;
  /** Whether the AI service is unavailable (graceful fallback). */
  isUnavailable: boolean;
  /** Reason the AI service is unavailable (for display). */
  unavailableReason: string | null;
  /** Error message if the fetch failed unexpectedly. */
  error: string | null;
  /** ISO timestamp when the narration was generated. */
  generatedAt: string | null;
}

// ---------------------------------------------------------------------------
// Template fallback
// ---------------------------------------------------------------------------

/**
 * Generate simple template-based narrative bullets from raw notification data.
 * Used when AI narration is unavailable so channel components still render
 * the same ChannelNarrationResult shape.
 */
function buildTemplateFallback(
  channels: ChannelFetchResult[]
): ChannelNarrationResult[] {
  const results: ChannelNarrationResult[] = [];

  for (const ch of channels) {
    if (ch.status !== "success") continue;

    const { meta, items } = ch.group;
    if (items.length === 0) continue;

    const bullets: NarrativeBulletResult[] = items.map((item) => ({
      narrative: item.body
        ? `${item.title} \u2014 ${item.body}`
        : item.title,
      itemIds: [item.id],
      primaryEntityType: item.regardingEntityType,
      primaryEntityId: item.regardingId,
      primaryEntityName: item.regardingName,
    }));

    results.push({
      category: meta.category,
      bullets,
    });
  }

  return results;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

/**
 * Fetch AI narration (TL;DR + per-channel bullets) when notification data
 * is available. Falls back to template-based bullets when AI is unavailable.
 *
 * @param channels - Channel fetch results from useNotificationData
 * @param dataLoadingState - Loading state of the notification data
 */
export function useBriefingNarration(
  channels: ChannelFetchResult[],
  dataLoadingState: LoadingState
): UseBriefingNarrationResult {
  const [tldr, setTldr] = useState<TldrResult | null>(null);
  const [channelNarratives, setChannelNarratives] = useState<
    ChannelNarrationResult[]
  >([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isUnavailable, setIsUnavailable] = useState(false);
  const [unavailableReason, setUnavailableReason] = useState<string | null>(
    null
  );
  const [error, setError] = useState<string | null>(null);
  const [generatedAt, setGeneratedAt] = useState<string | null>(null);

  // Track whether we already fetched for this set of channels
  const hasFetchedRef = useRef(false);

  useEffect(() => {
    // Only fetch once notification data is fully loaded
    if (dataLoadingState !== "loaded") return;

    // Don't re-fetch if we already have a result
    if (hasFetchedRef.current) return;

    // Check if there are any successful channels to narrate
    const hasData = channels.some((ch) => ch.status === "success");
    if (!hasData) {
      setIsUnavailable(true);
      setUnavailableReason("No notification data to narrate.");
      hasFetchedRef.current = true;
      return;
    }

    let cancelled = false;
    hasFetchedRef.current = true;
    setIsLoading(true);
    setError(null);
    setIsUnavailable(false);

    fetchBriefingNarration(channels).then((result) => {
      if (cancelled) return;

      switch (result.status) {
        case "success":
          setTldr(result.data.tldr);
          setChannelNarratives(result.data.channelNarratives);
          setGeneratedAt(result.data.generatedAtUtc);
          break;
        case "unavailable":
          setIsUnavailable(true);
          setUnavailableReason(result.reason);
          // Fall back to template-based bullets so channels still render
          setChannelNarratives(buildTemplateFallback(channels));
          break;
        case "error":
          setError(result.message);
          // Fall back to template-based bullets so channels still render
          setChannelNarratives(buildTemplateFallback(channels));
          break;
      }

      setIsLoading(false);
    });

    return () => {
      cancelled = true;
    };
  }, [channels, dataLoadingState]);

  return {
    tldr,
    channelNarratives,
    isLoading,
    isUnavailable,
    unavailableReason,
    error,
    generatedAt,
  };
}
