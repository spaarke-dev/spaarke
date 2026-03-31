/**
 * useNotificationData — React hook that orchestrates notification and
 * preference fetching for the Daily Briefing digest.
 *
 * Uses Promise.allSettled for parallel fetching (NFR-02, constraint):
 *   - Notifications (appnotification via Xrm.WebApi)
 *   - User preferences (sprk_userpreference via Xrm.WebApi)
 *
 * Individual channel failure shows inline error without crashing the
 * entire digest (NFR-03).
 *
 * Usage:
 *   const { channels, totalUnreadCount, preferences, loadingState, actions } =
 *     useNotificationData({ webApi, userId });
 */

import { useState, useEffect, useCallback, useRef } from "react";
import type {
  IWebApi,
  DailyDigestPreferences,
  ChannelFetchResult,
  NotificationData,
  LoadingState,
} from "../types/notifications";
import { DEFAULT_DAILY_DIGEST_PREFERENCES } from "../types/notifications";
import {
  fetchAndGroupNotifications,
  markNotificationRead,
  markAllNotificationsRead,
} from "../services/notificationService";
import {
  fetchDigestPreferences,
  saveDigestPreferences,
} from "../services/preferencesService";

// ---------------------------------------------------------------------------
// Hook options and return types
// ---------------------------------------------------------------------------

export interface UseNotificationDataOptions {
  /** Xrm.WebApi reference (from xrmProvider.getWebApi()). */
  webApi: IWebApi | null;
  /** Current user's systemuser GUID. */
  userId: string;
}

export interface UseNotificationDataActions {
  /** Mark a single notification as read by ID. */
  markAsRead: (notificationId: string) => Promise<void>;
  /** Mark all notifications as read. */
  markAllAsRead: () => Promise<void>;
  /** Refresh all data (notifications + preferences). */
  refresh: () => void;
  /** Update and save user preferences. */
  updatePreferences: (prefs: Partial<DailyDigestPreferences>) => Promise<void>;
}

export interface UseNotificationDataResult extends NotificationData {
  /** Actions for interacting with notifications and preferences. */
  actions: UseNotificationDataActions;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useNotificationData(
  options: UseNotificationDataOptions
): UseNotificationDataResult {
  const { webApi, userId } = options;

  // State
  const [channels, setChannels] = useState<ChannelFetchResult[]>([]);
  const [preferences, setPreferences] = useState<DailyDigestPreferences>(
    DEFAULT_DAILY_DIGEST_PREFERENCES
  );
  const [loadingState, setLoadingState] = useState<LoadingState>("idle");
  const [error, setError] = useState<string | undefined>(undefined);

  // Track preference record ID for updates without re-querying
  const preferenceRecordIdRef = useRef<string | undefined>(undefined);

  // Refresh counter to trigger re-fetch
  const [refreshKey, setRefreshKey] = useState(0);

  // -------------------------------------------------------------------------
  // Parallel data fetching with Promise.allSettled
  // -------------------------------------------------------------------------

  useEffect(() => {
    if (!webApi || !userId) {
      setLoadingState("idle");
      return;
    }

    let cancelled = false;
    setLoadingState("loading");
    setError(undefined);

    // Fetch notifications and preferences in parallel using Promise.allSettled
    // so that failure of one does not prevent the other from loading.
    Promise.allSettled([
      fetchAndGroupNotifications(webApi),
      fetchDigestPreferences(webApi, userId),
    ]).then((results) => {
      if (cancelled) return;

      const [notificationsResult, preferencesResult] = results;

      // Process notification channels
      if (notificationsResult.status === "fulfilled") {
        // Filter out disabled channels based on preferences
        const prefData = preferencesResult.status === "fulfilled" && preferencesResult.value.success
          ? preferencesResult.value.data.preferences
          : DEFAULT_DAILY_DIGEST_PREFERENCES;

        const filtered = notificationsResult.value.filter((ch) => {
          if (ch.status !== "success") return true; // always show errors
          return !prefData.disabledChannels.includes(ch.group.meta.category);
        });
        setChannels(filtered);
      } else {
        setError("Failed to load notifications.");
        setChannels([]);
      }

      // Process preferences
      if (preferencesResult.status === "fulfilled") {
        if (preferencesResult.value.success) {
          setPreferences(preferencesResult.value.data.preferences);
          preferenceRecordIdRef.current = preferencesResult.value.data.recordId;
        }
        // On preference fetch failure, keep defaults (already set)
      }

      setLoadingState("loaded");
    });

    return () => {
      cancelled = true;
    };
  }, [webApi, userId, refreshKey]);

  // -------------------------------------------------------------------------
  // Computed values
  // -------------------------------------------------------------------------

  const totalUnreadCount = channels.reduce((sum, ch) => {
    if (ch.status === "success") {
      return sum + ch.group.unreadCount;
    }
    return sum;
  }, 0);

  // -------------------------------------------------------------------------
  // Actions
  // -------------------------------------------------------------------------

  const markAsRead = useCallback(
    async (notificationId: string) => {
      if (!webApi) return;

      // Optimistic update
      setChannels((prev) =>
        prev.map((ch) => {
          if (ch.status !== "success") return ch;
          const updated = ch.group.items.map((item) =>
            item.id === notificationId ? { ...item, isRead: true } : item
          );
          return {
            status: "success",
            group: {
              ...ch.group,
              items: updated,
              unreadCount: updated.filter((i) => !i.isRead).length,
            },
          };
        })
      );

      const result = await markNotificationRead(webApi, notificationId);
      if (!result.success) {
        console.error("[DailyBriefing] Failed to mark read:", result.error.message);
        // Revert by refreshing
        setRefreshKey((k) => k + 1);
      }
    },
    [webApi]
  );

  const markAllAsReadAction = useCallback(async () => {
    if (!webApi) return;

    // Optimistic update — mark all items read across all channels
    setChannels((prev) =>
      prev.map((ch) => {
        if (ch.status !== "success") return ch;
        return {
          status: "success",
          group: {
            ...ch.group,
            items: ch.group.items.map((item) => ({ ...item, isRead: true })),
            unreadCount: 0,
          },
        };
      })
    );

    const result = await markAllNotificationsRead(webApi);
    if (!result.success) {
      console.error("[DailyBriefing] Failed to mark all read:", result.error.message);
      setRefreshKey((k) => k + 1);
    }
  }, [webApi]);

  const refresh = useCallback(() => {
    setRefreshKey((k) => k + 1);
  }, []);

  const updatePreferences = useCallback(
    async (update: Partial<DailyDigestPreferences>) => {
      if (!webApi || !userId) return;

      const merged: DailyDigestPreferences = {
        ...preferences,
        ...update,
      };

      // Optimistic update
      setPreferences(merged);

      const result = await saveDigestPreferences(
        webApi,
        userId,
        merged,
        preferenceRecordIdRef.current
      );

      if (result.success) {
        preferenceRecordIdRef.current = result.data;
        // If disabled channels changed, refresh to re-filter
        if (update.disabledChannels !== undefined) {
          setRefreshKey((k) => k + 1);
        }
      } else {
        console.error("[DailyBriefing] Failed to save preferences:", result.error.message);
      }
    },
    [webApi, userId, preferences]
  );

  // -------------------------------------------------------------------------
  // Return
  // -------------------------------------------------------------------------

  return {
    channels,
    totalUnreadCount,
    preferences,
    loadingState,
    error,
    actions: {
      markAsRead,
      markAllAsRead: markAllAsReadAction,
      refresh,
      updatePreferences,
    },
  };
}
