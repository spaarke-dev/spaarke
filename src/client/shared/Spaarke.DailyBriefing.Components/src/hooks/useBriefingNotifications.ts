/**
 * useBriefingNotifications — fetches and groups appnotification records
 * via Xrm.WebApi for the Daily Briefing digest.
 *
 * One of the three independent hooks decomposed from the original monolithic
 * `useNotificationData` (R2 FR-06 / task 014). Each of the three hooks
 * (`useBriefingNotifications`, `useBriefingPreferences`, `useBriefingActions`)
 * can be consumed independently — NO shared internal state, NO singletons,
 * NO context. Cross-hook coordination (e.g., "refetch when preferences
 * change") is the consumer's responsibility via an effect (Option A, per
 * project design.md / spec FR-06).
 *
 * Constraints (preserved verbatim from `useNotificationData`):
 *   - Uses `fetchAndGroupNotifications` which itself uses Promise.allSettled
 *     for per-channel resilience (NFR-02 / NFR-03).
 *   - Total fetch failure surfaces in `error`; per-channel errors appear as
 *     `ChannelFetchResult { status: "error", ... }` in `channels`.
 *
 * Usage:
 *   const { channels, totalUnreadCount, loadingState, error, refetch } =
 *     useBriefingNotifications(webApi);
 *
 * Coordination example (consumer-layer effect — Option A):
 *   const { refetch, channels, ... } = useBriefingNotifications(webApi);
 *   const { preferences, ... } = useBriefingPreferences(webApi, userId);
 *   useEffect(() => { refetch(); }, [preferences.disabledChannels]);
 *
 * Note: filtering by `disabledChannels` happens at the CONSUMER (or inside
 * the rendering layer) — this hook returns the full channel set so each
 * consumer can decide whether/how to filter. This is intentional per FR-06:
 * the three hooks are independent; cross-hook concerns live at the consumer.
 */

import { useState, useEffect, useCallback } from "react";
import type {
  IWebApi,
  ChannelFetchResult,
  LoadingState,
} from "../types/notifications";
import { fetchAndGroupNotifications } from "../services/notificationService";

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

export interface UseBriefingNotificationsResult {
  /** All channel fetch results (success or per-channel error). */
  channels: ChannelFetchResult[];
  /** Total unread count across all successful channels. */
  totalUnreadCount: number;
  /** Overall loading state. */
  loadingState: LoadingState;
  /** Error message if the entire notification fetch failed. */
  error?: string;
  /** Trigger a refetch of notifications. */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

/**
 * Fetches and groups notifications for the Daily Briefing digest.
 *
 * @param webApi - Xrm.WebApi reference (from xrmProvider.getWebApi()).
 *                 Null while Xrm is still resolving — hook stays idle.
 */
export function useBriefingNotifications(
  webApi: IWebApi | null
): UseBriefingNotificationsResult {
  const [channels, setChannels] = useState<ChannelFetchResult[]>([]);
  const [loadingState, setLoadingState] = useState<LoadingState>("idle");
  const [error, setError] = useState<string | undefined>(undefined);
  const [refreshKey, setRefreshKey] = useState(0);

  useEffect(() => {
    if (!webApi) {
      setLoadingState("idle");
      return;
    }

    let cancelled = false;
    setLoadingState("loading");
    setError(undefined);

    fetchAndGroupNotifications(webApi)
      .then((results) => {
        if (cancelled) return;
        setChannels(results);
        setLoadingState("loaded");
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const message =
          err && typeof err === "object" && "message" in err
            ? String((err as { message: unknown }).message)
            : "Failed to load notifications.";
        setError(message);
        setChannels([]);
        setLoadingState("error");
      });

    return () => {
      cancelled = true;
    };
  }, [webApi, refreshKey]);

  // Computed: total unread count across successful channels
  const totalUnreadCount = channels.reduce((sum, ch) => {
    if (ch.status === "success") {
      return sum + ch.group.unreadCount;
    }
    return sum;
  }, 0);

  const refetch = useCallback(() => {
    setRefreshKey((k) => k + 1);
  }, []);

  return {
    channels,
    totalUnreadCount,
    loadingState,
    error,
    refetch,
  };
}
