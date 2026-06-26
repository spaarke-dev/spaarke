/**
 * useBriefingActions — exposes the IO actions for the Daily Briefing
 * (mark-as-read, mark-all-as-read, dismiss-all, refresh, plus the R3 per-item
 * Check / Remove / Keep actions).
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
 * R3 task 030 (FR-4 / FR-5 / FR-6) — three new per-item handlers:
 *   - `markChecked(id, options?)`   — wraps `markBriefingChecked`.
 *   - `markRemoved(id, options?)`   — wraps `markBriefingRemoved`.
 *   - `extendTtl(id, currentTtl, options?)` — wraps `extendBriefingTtl`.
 *
 *   Each new handler implements the spec-mandated orchestration shape:
 *     1. Fire `onOptimistic(id)` callback (consumer applies UI optimism;
 *        e.g., mark item read locally, remove from list, advance expiry).
 *     2. Invoke the service function.
 *     3. On success: fire `onSuccess(payload)` callback (consumer dispatches
 *        success toast) and bump `refresh`.
 *     4. On failure: fire `onRevert(id)` callback (consumer rolls back UI)
 *        AND `onError(err)` callback (consumer dispatches error toast).
 *
 *   The hook is JSX-agnostic — it does NOT import `@fluentui/react-components`
 *   or create toast elements. Consumers (e.g., `DailyBriefingApp.tsx`) pass
 *   callbacks that wrap the existing `useToastController.dispatchToast` they
 *   already manage. This keeps the hook unit-testable in isolation and
 *   preserves the established Provider-pattern at the UI layer.
 *
 * Usage:
 *   const {
 *     markAsRead, markAllAsRead, dismissAll,
 *     markChecked, markRemoved, extendTtl,   // R3 task 030
 *     refresh,
 *   } = useBriefingActions(webApi);
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

import { useState, useCallback } from 'react';
import type { IWebApi } from '../types/notifications';
import {
  markBriefingChecked,
  markAllBriefingsChecked,
  markBriefingRemoved,
  extendBriefingTtl,
} from '../services/notificationService';

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

/**
 * Options bag for the three R3 per-item handlers (`markChecked`,
 * `markRemoved`, `extendTtl`).
 *
 * All callbacks are optional — the hook is functional without them — but
 * consumers SHOULD provide `onOptimistic` + `onRevert` (UI optimism per
 * spec FR-4/5/6) and `onSuccess` + `onError` (toast dispatch per spec).
 *
 * Each callback is fired exactly once per action invocation:
 *   - `onOptimistic(id)` fires BEFORE the service call (UI optimism).
 *   - On service success: `onSuccess(payload)` fires (payload type varies).
 *   - On service failure: `onRevert(id)` AND `onError(err)` both fire,
 *     in that order, so the UI rollback can complete BEFORE the toast
 *     references stale UI state.
 *
 * @template TSuccess - The success payload shape (per-handler).
 */
export interface BriefingActionOptions<TSuccess = void> {
  /** Fires immediately before the service call; consumer applies UI optimism. */
  onOptimistic?: (notificationId: string) => void;
  /** Fires after the service call resolves successfully; consumer dispatches success toast. */
  onSuccess?: (payload: TSuccess) => void;
  /** Fires after the service call rejects; consumer rolls back UI optimism. */
  onRevert?: (notificationId: string) => void;
  /** Fires after the service call rejects; consumer dispatches error toast. */
  onError?: (error: { code: string; message: string }) => void;
}

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
   * R3 task 030 / FR-4: mark a single briefing item as Checked (read in widget terms).
   * Writes `sprk_briefingstate = 1` via `markBriefingChecked`. Orchestrates
   * optimistic update → service call → success/error toast via the supplied
   * callbacks (see `BriefingActionOptions`).
   *
   * Returns `true` on success, `false` on failure or when webApi is null.
   */
  markChecked: (notificationId: string, options?: BriefingActionOptions<void>) => Promise<boolean>;
  /**
   * R3 task 030 / FR-5: remove a single briefing item from the widget.
   * Writes `sprk_briefingstate = 2` via `markBriefingRemoved`. Subsequent
   * fetches filter the row out server-side (per `EXCLUDE_REMOVED_FILTER`);
   * underlying record + bell-panel state are preserved (FR-7).
   *
   * Returns `true` on success, `false` on failure or when webApi is null.
   */
  markRemoved: (notificationId: string, options?: BriefingActionOptions<void>) => Promise<boolean>;
  /**
   * R3 task 030 / FR-6: extend a single briefing item's TTL by 7 calendar days.
   * Calls `extendBriefingTtl(id, currentTtl)` which computes
   * `newTtl = currentTtl + 604800` and writes `{ ttlinseconds: newTtl }`.
   *
   * On success, the `onSuccess(newTtl)` callback receives the new TTL value
   * (in seconds) so the consumer can render the new effective expiry date in
   * its toast (per FR-6 spec copy: "Extended for 7 more days (new expiry: {date}).").
   *
   * Returns `true` on success, `false` on failure or when webApi is null.
   */
  extendTtl: (
    notificationId: string,
    currentTtlSeconds: number,
    options?: BriefingActionOptions<number>
  ) => Promise<boolean>;
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
export function useBriefingActions(webApi: IWebApi | null): UseBriefingActionsResult {
  const [refresh, setRefresh] = useState<number>(0);

  const bump = useCallback(() => {
    setRefresh(k => k + 1);
  }, []);

  const markAsRead = useCallback(
    async (notificationId: string): Promise<boolean> => {
      if (!webApi) return false;
      const result = await markBriefingChecked(webApi, notificationId);
      if (!result.success) {
        console.error('[DailyBriefing] Failed to mark read:', result.error.message);
        return false;
      }
      bump();
      return true;
    },
    [webApi, bump]
  );

  const markAllAsRead = useCallback(async (): Promise<boolean> => {
    if (!webApi) return false;
    const result = await markAllBriefingsChecked(webApi);
    if (!result.success) {
      console.error('[DailyBriefing] Failed to mark all read:', result.error.message);
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

  // ---------------------------------------------------------------------------
  // R3 task 030 — FR-4 / FR-5 / FR-6 per-item action handlers.
  //
  // Each handler implements the same 4-step orchestration:
  //   1. If webApi is null  → no-op returning `false` (welcome-screen timing).
  //   2. Fire `onOptimistic(id)` BEFORE the service call.
  //   3. Invoke the service function.
  //   4. On success: fire `onSuccess(payload)`, bump `refresh`, return `true`.
  //      On failure: fire `onRevert(id)` THEN `onError(err)`, return `false`.
  //
  // The hook is JSX-agnostic — concrete `<Toast>` JSX construction stays at
  // the UI layer (see `DailyBriefingApp.tsx:238-287` `handleAddToTodo` for the
  // existing canonical toast pattern).
  // ---------------------------------------------------------------------------

  const markChecked = useCallback(
    async (notificationId: string, options?: BriefingActionOptions<void>): Promise<boolean> => {
      if (!webApi) return false;

      // Step 1: optimistic UI update (consumer-supplied).
      options?.onOptimistic?.(notificationId);

      // Step 2: service call.
      const result = await markBriefingChecked(webApi, notificationId);

      if (!result.success) {
        console.error('[DailyBriefing] Failed to mark checked:', result.error.message);
        // Revert FIRST so the UI is consistent before the toast renders.
        options?.onRevert?.(notificationId);
        options?.onError?.(result.error);
        return false;
      }

      options?.onSuccess?.(undefined);
      bump();
      return true;
    },
    [webApi, bump]
  );

  const markRemoved = useCallback(
    async (notificationId: string, options?: BriefingActionOptions<void>): Promise<boolean> => {
      if (!webApi) return false;

      options?.onOptimistic?.(notificationId);

      const result = await markBriefingRemoved(webApi, notificationId);

      if (!result.success) {
        console.error('[DailyBriefing] Failed to mark removed:', result.error.message);
        options?.onRevert?.(notificationId);
        options?.onError?.(result.error);
        return false;
      }

      options?.onSuccess?.(undefined);
      bump();
      return true;
    },
    [webApi, bump]
  );

  const extendTtl = useCallback(
    async (
      notificationId: string,
      currentTtlSeconds: number,
      options?: BriefingActionOptions<number>
    ): Promise<boolean> => {
      if (!webApi) return false;

      options?.onOptimistic?.(notificationId);

      const result = await extendBriefingTtl(webApi, notificationId, currentTtlSeconds);

      if (!result.success) {
        console.error('[DailyBriefing] Failed to extend TTL:', result.error.message);
        options?.onRevert?.(notificationId);
        options?.onError?.(result.error);
        return false;
      }

      // `result.data` is the new TTL in seconds (current + 604800). Forwarded
      // so the consumer toast can render the new effective expiry date from
      // `createdon + newTtl`.
      options?.onSuccess?.(result.data);
      bump();
      return true;
    },
    [webApi, bump]
  );

  return {
    markAsRead,
    markAllAsRead,
    dismissAll,
    markChecked,
    markRemoved,
    extendTtl,
    refresh,
  };
}
