/**
 * useBriefingNarration — React hook that fetches AI-generated narration
 * (TL;DR + per-channel narrative bullets) from the BFF endpoint when
 * notification channel data is available.
 *
 * Replaces useAiBriefing with richer per-channel narrative output.
 * Refetches whenever the `channels` reference changes (consumer-driven
 * cache invalidation). Falls back to template-based bullets when AI is
 * unavailable.
 *
 * Usage:
 *   const { tldr, channelNarratives, isLoading, error } = useBriefingNarration(channels, loadingState);
 *
 * Hoist note (R2 task 013 / FR-05):
 *   Originally lived at `src/solutions/DailyBriefing/src/hooks/useBriefingNarration.ts`.
 *   Hoisted verbatim to `@spaarke/daily-briefing-components/hooks` per ADR-012
 *   (Pattern D dual-use shape). Logic preserved byte-identical from original.
 *   The `../services/briefingService` import now resolves intra-package
 *   (task 012 hoisted briefingService here). The `../types/notifications`
 *   import is a relative back-pointer to the standalone solution's types file
 *   until task 014 hoists those types alongside the `useNotificationData`
 *   decomposition (mirrors task 012's `briefingService` → `notifications.ts`
 *   back-pointer pattern). Original location becomes a re-export shim
 *   (cleaned up in task 017/018).
 *
 * R4 task 033 / FR-15 (2026-06-26):
 *   Removed the `hasFetchedRef` session cache. The original implementation
 *   gated the fetch with a persistent `useRef(false)` so the hook fetched
 *   exactly once per mount. That broke `AC-15`: after the user clicked
 *   Check / Remove / Keep, `actionsRefresh` bumped → `useBriefingNotifications`
 *   refetched → `channels` changed → narration was supposed to refetch, but
 *   `hasFetchedRef.current === true` short-circuited the effect, so the TL;DR
 *   + bullets stayed stale. Now the effect refetches whenever `channels`
 *   (the dependency) changes. In-flight duplicate calls under rapid churn
 *   are guarded by the existing `cancelled` closure flag in the cleanup
 *   function (no AbortController needed because `fetchBriefingNarration`
 *   already swallows AbortError → 'error', and the `cancelled` flag prevents
 *   stale-result state writes).
 */

import { useState, useEffect } from 'react';
import type { ChannelFetchResult, LoadingState } from '../types/notifications';
import type { TldrResult, ChannelNarrationResult, NarrativeBulletResult } from '../services/briefingService';
import { fetchBriefingNarration } from '../services/briefingService';

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
function buildTemplateFallback(channels: ChannelFetchResult[]): ChannelNarrationResult[] {
  const results: ChannelNarrationResult[] = [];

  for (const ch of channels) {
    if (ch.status !== 'success') continue;

    const { meta, items } = ch.group;
    if (items.length === 0) continue;

    const bullets: NarrativeBulletResult[] = items.map(item => ({
      narrative: item.body ? `${item.title} \u2014 ${item.body}` : item.title,
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
  const [channelNarratives, setChannelNarratives] = useState<ChannelNarrationResult[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isUnavailable, setIsUnavailable] = useState(false);
  const [unavailableReason, setUnavailableReason] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [generatedAt, setGeneratedAt] = useState<string | null>(null);

  useEffect(() => {
    // Only fetch once notification data is fully loaded
    if (dataLoadingState !== 'loaded') return;

    // R4 task 033 / FR-15: refetch whenever `channels` changes (the consumer
    // bumps `channels` via `useBriefingNotifications`'s refetch in response to
    // `actionsRefresh` — see DailyBriefingApp.tsx Effect 2). No persistent
    // has-fetched flag; the `cancelled` closure prevents stale writes if a
    // newer fetch supersedes this one.

    // Check if there are any successful channels to narrate
    const hasData = channels.some(ch => ch.status === 'success');
    if (!hasData) {
      setIsUnavailable(true);
      setUnavailableReason('No notification data to narrate.');
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);
    setIsUnavailable(false);

    fetchBriefingNarration(channels).then(result => {
      if (cancelled) return;

      switch (result.status) {
        case 'success':
          setTldr(result.data.tldr);
          setChannelNarratives(result.data.channelNarratives);
          setGeneratedAt(result.data.generatedAtUtc);
          break;
        case 'unavailable':
          setIsUnavailable(true);
          setUnavailableReason(result.reason);
          // Fall back to template-based bullets so channels still render
          setChannelNarratives(buildTemplateFallback(channels));
          break;
        case 'error':
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
