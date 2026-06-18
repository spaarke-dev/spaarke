/**
 * useBriefingActions — exposes the IO actions for the Daily Briefing
 * (mark-as-read, mark-all-as-read, dismiss-all, refresh).
 *
 * One of the three independent hooks decomposed from the original monolithic
 * `useNotificationData` (R2 FR-06 / task 014). Each of the three hooks
 * can be consumed independently — NO shared internal state, NO singletons,
 * NO context. Cross-hook coordination is the consumer's responsibility via an
 * effect (Option A, per project design.md / spec FR-06).
 *
 * Decomposition contract:
 *   - This hook owns only the action surface. It does NOT hold notification
 *     state — that lives in `useBriefingNotifications`.
 *   - After a successful action, the consumer should call
 *     `useBriefingNotifications.refetch()` to re-render with fresh data.
 *     Either subscribe to this hook's `refresh` token, or call `refetch`
 *     directly inside the action's `.then()` chain at the consumer.
 *
 * Usage:
 *   const { markAsRead, markAllAsRead, dismissAll, refresh } =
 *     useBriefingActions(webApi);
 *
 * Coordination example (consumer-layer effect — Option A):
 *   const { markAsRead, refresh } = useBriefingActions(webApi);
 *   const { refetch } = useBriefingNotifications(webApi);
 *   useEffect(() => { refetch(); }, [refresh]);  // refetch on any mutation
 *
 * Note on ADR-024:
 *   The dismiss path in this hook does NOT consume `TODO_REGARDING_CATALOG`.
 *   Catalog usage is intentionally isolated to `useInlineTodoCreate` (which
 *   creates `sprk_todo` records with multi-entity regarding resolution).
 *   `dismissAll` here only updates `appnotification.toasttype` to mark each
 *   record as dismissed.
 */

import { useState, useCallback } from "react";
import type { IWebApi } from "../types/notifications";
import {
  markNotificationRead,
  markAllNotificationsRead,
} from "../services/notificationService";

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

export interface UseBriefingActionsResult {
  /**
   * Mark a single notification as read. Returns `true` on success, `false` on
   * failure (logged to console; consumer can decide whether to surface).
   */
  markAsRead: (notificationId: string) => Promise<boolean>;
  /**
   * Mark all of the current user's notifications as read.
   * Returns `true` on success, `false` on failure.
   */
  markAllAsRead: () => Promise<boolean>;
  /**
   * Dismiss all currently visible notifications. In the appnotification model
   * "dismiss" === "mark as read" (toasttype = 200000000); kept as a separate
   * verb in the API for UX clarity and future extension (e.g., per-item
   * dismiss-without-read distinction).
   */
  dismissAll: () => Promise<boolean>;
  /**
   * A monotonically increasing counter bumped after every successful mutation.
   * Consumers can subscribe via `useEffect([refresh], () => refetch())` to
   * trigger a `useBriefingNotifications.refetch()` after this hook mutates
   * Dataverse. Counter starts at 0; never decreases.
   */
  refresh: number;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

/**
 * Exposes the Daily Briefing IO actions.
 *
 * @param webApi - Xrm.WebApi reference (from xrmProvider.getWebApi()).
 *                 Null while Xrm is still resolving — actions become no-ops
 *                 that resolve to `false`.
 */
export function useBriefingActions(
  webApi: IWebApi | null
): UseBriefingActionsResult {
  const [refresh, setRefresh] = useState<number>(0);

  const bump = useCallback(() => {
    setRefresh((k) => k + 1);
  }, []);

  const markAsRead = useCallback(
    async (notificationId: string): Promise<boolean> => {
      if (!webApi) return false;
      const result = await markNotificationRead(webApi, notificationId);
      if (!result.success) {
        console.error(
          "[DailyBriefing] Failed to mark read:",
          result.error.message
        );
        return false;
      }
      bump();
      return true;
    },
    [webApi, bump]
  );

  const markAllAsRead = useCallback(async (): Promise<boolean> => {
    if (!webApi) return false;
    const result = await markAllNotificationsRead(webApi);
    if (!result.success) {
      console.error(
        "[DailyBriefing] Failed to mark all read:",
        result.error.message
      );
      return false;
    }
    bump();
    return true;
  }, [webApi, bump]);

  /**
   * `dismissAll` is currently identical to `markAllAsRead` (the underlying
   * appnotification model uses toasttype=200000000 for both "dismissed" and
   * "read"). Kept as a distinct verb in the public contract for future
   * extension.
   */
  const dismissAll = useCallback(async (): Promise<boolean> => {
    return markAllAsRead();
  }, [markAllAsRead]);

  return {
    markAsRead,
    markAllAsRead,
    dismissAll,
    refresh,
  };
}
