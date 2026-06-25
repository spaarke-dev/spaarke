/**
 * Notification Service — queries appnotification records via Xrm.WebApi
 * and groups them by category for the Daily Briefing digest.
 *
 * Constraints:
 *   - MUST query via Xrm.WebApi.retrieveMultipleRecords (FR-09, spec)
 *   - MUST group by category from customData.category in notification data JSON
 *   - Individual channel failures show inline error (NFR-03)
 *
 * The appnotification entity stores its payload in a `data` column as JSON:
 *   {
 *     "iconUrl": "...",
 *     "customData": {
 *       "category": "tasks-overdue",
 *       "priority": "high",
 *       "actionUrl": "/main.aspx?...",
 *       "regardingName": "...",
 *       "regardingEntityType": "...",
 *       "regardingId": "...",
 *       "isAiGenerated": false,
 *       "aiConfidence": null
 *     }
 *   }
 *
 * Hoist note (R2 task 015 / FR-07):
 *   Originally lived at `src/solutions/DailyBriefing/src/services/notificationService.ts`.
 *   Hoisted verbatim alongside the `types/notifications.ts` hoist so the new
 *   package no longer reaches back across the solution boundary. Logic is
 *   byte-identical from the original — no behavior change. The two relative
 *   imports from `../types/notifications` now resolve intra-package.
 */

import type {
  IWebApi,
  WebApiEntity,
  NotificationItem,
  NotificationCategory,
  NotificationPriority,
  ChannelGroup,
  ChannelFetchResult,
} from '../types/notifications';
import { CHANNEL_REGISTRY, tryCatch, BRIEFING_STATE_CHECKED, BRIEFING_STATE_REMOVED } from '../types/notifications';
import type { IResult } from '../types/notifications';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/**
 * OData columns to select from appnotification.
 *
 * R3 task 020 / FR-3: added `sprk_briefingstate` (Daily-Briefing read-state) so
 * `toNotificationItem` can derive `isRead` from the new Choice column. `toasttype`
 * is RETAINED for display-behavior context (Microsoft semantics — NOT a read marker).
 */
const NOTIFICATION_SELECT = [
  'appnotificationid',
  'title',
  'body',
  'data',
  'toasttype',
  'createdon',
  'sprk_briefingstate',
  // R3 FR-6 follow-up: surface ttlinseconds so the "Keep 7 more days" action
  // can write `current + 604800` additively. NotificationService writes 604800
  // explicitly post-task-010; pre-rollout rows may be undefined (fall back to
  // 0 on Keep → write 604800).
  'ttlinseconds',
].join(',');

/**
 * Server-side filter (OData $filter expression body) excluding briefings the user
 * has marked Removed (sprk_briefingstate = 2). Includes `eq null` clause so
 * pre-rollout existing rows (no sprk_briefingstate value) remain visible — they
 * coalesce to Unread on read per FR-3 AC-3c.
 *
 * R3 task 020 / FR-3 AC-3b.
 */
const EXCLUDE_REMOVED_FILTER = '(sprk_briefingstate ne 2 or sprk_briefingstate eq null)';

/** Extension increment for the "Keep 7 more days" action (FR-6): 7 × 24 × 60 × 60 = 604800 seconds. */
const TTL_EXTEND_SECONDS = 604800;

/** Maximum notifications to fetch per query (unread, recent). */
const MAX_NOTIFICATIONS = 200;

// ---------------------------------------------------------------------------
// Parsing helpers
// ---------------------------------------------------------------------------

/**
 * Parse the `data` JSON column from an appnotification record.
 * Returns a typed customData object, or null if parsing fails.
 */
function parseNotificationData(raw: unknown): {
  category: NotificationCategory;
  priority: NotificationPriority;
  actionUrl: string;
  regardingName: string;
  regardingEntityType: string;
  regardingId: string;
  isAiGenerated: boolean;
  aiConfidence?: number;
  /** R2.2: ISO-8601 due date from customData.dueDate (set by task playbooks). */
  dueDate: string | null;
} | null {
  if (!raw || typeof raw !== 'string') return null;

  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;

    // Support both flat format (NotificationService) and nested customData format (playbooks)
    const customData = (parsed['customData'] as Record<string, unknown>) ?? parsed;

    return {
      category:
        (customData['category'] as NotificationCategory) ?? (customData['channel'] as NotificationCategory) ?? 'system',
      priority: (customData['priority'] as NotificationPriority) ?? 'normal',
      actionUrl: (customData['actionUrl'] as string) ?? (parsed['actionUrl'] as string) ?? '',
      regardingName: (customData['regardingName'] as string) ?? '',
      regardingEntityType:
        (customData['regardingEntityType'] as string) ?? (customData['regardingType'] as string) ?? '',
      regardingId: (customData['regardingId'] as string) ?? (parsed['regardingId'] as string) ?? '',
      isAiGenerated: (customData['isAiGenerated'] as boolean) ?? false,
      aiConfidence: typeof customData['aiConfidence'] === 'number' ? (customData['aiConfidence'] as number) : undefined,
      dueDate: (customData['dueDate'] as string) ?? null,
    };
  } catch {
    console.warn('[DailyBriefing] Failed to parse notification data JSON');
    return null;
  }
}

/**
 * Convert a raw appnotification WebApi entity to a typed NotificationItem.
 * Returns null if the record cannot be parsed.
 */
function toNotificationItem(entity: WebApiEntity): NotificationItem | null {
  const id = entity['appnotificationid'] as string | undefined;
  const title = entity['title'] as string | undefined;
  if (!id || !title) return null;

  const customData = parseNotificationData(entity['data']);

  return {
    id,
    title,
    body: (entity['body'] as string) ?? '',
    category: customData?.category ?? 'system',
    priority: customData?.priority ?? 'normal',
    actionUrl: customData?.actionUrl ?? '',
    regardingName: customData?.regardingName ?? '',
    regardingEntityType: customData?.regardingEntityType ?? '',
    regardingId: customData?.regardingId ?? '',
    // R3 FR-3 AC-3a/AC-3c: derive read-state from the Daily-Briefing-scoped
    // `sprk_briefingstate` Choice column, NOT from `toasttype` (which is
    // Microsoft's display-behavior "Timed"/"Persistent" setting and was the
    // root cause of the UAT empty-state defect). Null-coalesce to Unread (0)
    // so pre-rollout existing rows render correctly without a backfill.
    isRead: ((entity['sprk_briefingstate'] as number) ?? 0) === BRIEFING_STATE_CHECKED,
    isAiGenerated: customData?.isAiGenerated ?? false,
    aiConfidence: customData?.aiConfidence,
    createdOn: (entity['createdon'] as string) ?? new Date().toISOString(),
    dueDate: customData?.dueDate ?? null,
    // R3 FR-6 follow-up: pass through the row's ttlinseconds so the UI's
    // "Keep 7 more days" action can compute current + 604800 additively.
    // Undefined for pre-rollout rows (no producer-side write); UI coerces
    // to 0 in that case so the action writes an explicit 604800.
    ttlinseconds:
      typeof entity['ttlinseconds'] === 'number' ? (entity['ttlinseconds'] as number) : undefined,
  };
}

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Fetch all recent unread notifications for the current user.
 *
 * Queries appnotification where:
 *   - Current user is the owner (Xrm automatically scopes to current user)
 *   - Sorted by createdon desc
 *
 * @param webApi - Xrm.WebApi reference (from xrmProvider)
 * @param options - Optional filters
 * @returns IResult<NotificationItem[]>
 */
export async function fetchNotifications(
  webApi: IWebApi,
  options: { unreadOnly?: boolean; top?: number } = {}
): Promise<IResult<NotificationItem[]>> {
  const top = options.top ?? MAX_NOTIFICATIONS;
  const unreadOnly = options.unreadOnly ?? false;

  // Build OData query — appnotification is automatically scoped to the current user.
  //
  // R3 task 020 / FR-3:
  //   - ALWAYS exclude items the user has Removed from the briefing
  //     (`sprk_briefingstate eq 2`), but keep nulls (pre-rollout existing rows
  //     coalesce to Unread per FR-3 AC-3c).
  //   - When `unreadOnly`, AND-join the not-Checked predicate. Anything that is
  //     NOT `sprk_briefingstate = 1` (Checked) counts as unread for the digest,
  //     including nulls.
  //
  // FR-7 invariant: filters do NOT read or write `toasttype` / `isread` for
  // read-state purposes — those are bell-panel concerns.
  const predicates: string[] = [EXCLUDE_REMOVED_FILTER];
  if (unreadOnly) {
    predicates.push('(sprk_briefingstate ne 1 or sprk_briefingstate eq null)');
  }
  const filter = `&$filter=${predicates.join(' and ')}`;

  const query = `?$select=${NOTIFICATION_SELECT}` + filter + `&$orderby=createdon desc` + `&$top=${top}`;

  return tryCatch(async () => {
    const result = await webApi.retrieveMultipleRecords('appnotification', query, top);

    const items: NotificationItem[] = [];
    for (const entity of result.entities) {
      const item = toNotificationItem(entity);
      if (item) {
        items.push(item);
      }
    }

    return items;
  }, 'NOTIFICATIONS_FETCH_ERROR');
}

/**
 * Group a flat list of notification items by their category.
 * Returns a sorted array of ChannelGroup objects.
 *
 * @param items - Flat list of parsed notification items
 * @returns ChannelGroup[] sorted by channel order
 */
export function groupByCategory(items: NotificationItem[]): ChannelGroup[] {
  const groups = new Map<NotificationCategory, NotificationItem[]>();

  for (const item of items) {
    const existing = groups.get(item.category);
    if (existing) {
      existing.push(item);
    } else {
      groups.set(item.category, [item]);
    }
  }

  const channelGroups: ChannelGroup[] = [];
  for (const [category, categoryItems] of groups) {
    const meta = CHANNEL_REGISTRY[category] ?? CHANNEL_REGISTRY['system'];
    channelGroups.push({
      meta,
      items: categoryItems.sort((a, b) => new Date(b.createdOn).getTime() - new Date(a.createdOn).getTime()),
      unreadCount: categoryItems.filter(i => !i.isRead).length,
    });
  }

  // Sort by channel display order
  channelGroups.sort((a, b) => a.meta.order - b.meta.order);
  return channelGroups;
}

/**
 * Fetch notifications and group by category, returning per-channel results.
 * Uses Promise.allSettled internally so individual channel processing errors
 * do not crash the entire digest (NFR-03).
 *
 * @param webApi - Xrm.WebApi reference
 * @returns ChannelFetchResult[] — one entry per category that has notifications
 */
export async function fetchAndGroupNotifications(webApi: IWebApi): Promise<ChannelFetchResult[]> {
  const result = await fetchNotifications(webApi);

  if (!result.success) {
    // Total failure — return a single error entry
    return [
      {
        status: 'error',
        category: 'system',
        error: result.error.message,
      },
    ];
  }

  const groups = groupByCategory(result.data);

  // Wrap each group processing in Promise.allSettled to ensure per-channel
  // resilience (even though grouping is synchronous, this pattern extends
  // to future async per-channel enrichment like AI summaries).
  const channelPromises = groups.map(async (group): Promise<ChannelFetchResult> => {
    try {
      return { status: 'success', group };
    } catch (err) {
      return {
        status: 'error',
        category: group.meta.category,
        error: err instanceof Error ? err.message : 'Unknown error',
      };
    }
  });

  const settled = await Promise.allSettled(channelPromises);

  return settled.map(result => {
    if (result.status === 'fulfilled') {
      return result.value;
    }
    return {
      status: 'error' as const,
      category: 'system' as NotificationCategory,
      error: result.reason instanceof Error ? result.reason.message : 'Unknown error',
    };
  });
}

/**
 * Mark a single Daily Briefing item as "Checked" (read in the widget's terms).
 *
 * R3 task 020 / FR-4:
 *   Writes `{ sprk_briefingstate: 1 }` (Checked) directly to Dataverse via
 *   `Xrm.WebApi.updateRecord`. Does NOT touch `toasttype` or `isread` — those
 *   are bell-panel state and remain independent (FR-7 invariant).
 *
 * Renamed from R2's `markNotificationRead` (which previously wrote
 * `toasttype = 200000000`, conflating display-behavior with read-state — the
 * root cause of the empty-state defect). Hook + UI consumers in tasks 030/031
 * will import this new name.
 *
 * @param webApi - Xrm.WebApi reference
 * @param notificationId - The appnotificationid GUID
 * @returns IResult<void>
 */
export async function markBriefingChecked(webApi: IWebApi, notificationId: string): Promise<IResult<void>> {
  return tryCatch(async () => {
    await webApi.updateRecord('appnotification', notificationId, {
      sprk_briefingstate: BRIEFING_STATE_CHECKED,
    });
  }, 'BRIEFING_MARK_CHECKED_ERROR');
}

/**
 * Mark all of the current user's unread briefing items as "Checked".
 * Fetches unread notifications and updates each one.
 *
 * R3 task 020 / FR-4 (bulk):
 *   Writes `{ sprk_briefingstate: 1 }` per item via `Promise.allSettled` to
 *   keep partial-failure semantics from the original implementation.
 *
 * Renamed from R2's `markAllNotificationsRead` (which wrote `toasttype`).
 *
 * @param webApi - Xrm.WebApi reference
 * @returns IResult<{ succeeded: number; failed: number }>
 */
export async function markAllBriefingsChecked(
  webApi: IWebApi
): Promise<IResult<{ succeeded: number; failed: number }>> {
  return tryCatch(async () => {
    const unread = await fetchNotifications(webApi, { unreadOnly: true });
    if (!unread.success) {
      throw new Error(unread.error.message);
    }

    let succeeded = 0;
    let failed = 0;

    // Per FR-4 bulk path: write sprk_briefingstate = Checked. Matches the
    // single-record markBriefingChecked write + the toNotificationItem read
    // derivation (line 144) + the EXCLUDE_REMOVED_FILTER (line 75). Field
    // alignment guarantees the next fetch reflects the bulk update.
    const results = await Promise.allSettled(
      unread.data.map(item =>
        webApi.updateRecord('appnotification', item.id, { sprk_briefingstate: BRIEFING_STATE_CHECKED })
      )
    );

    for (const r of results) {
      if (r.status === 'fulfilled') {
        succeeded++;
      } else {
        failed++;
      }
    }

    return { succeeded, failed };
  }, 'BRIEFING_MARK_ALL_CHECKED_ERROR');
}

/**
 * Remove a single Daily Briefing item from the widget.
 *
 * R3 task 020 / FR-5:
 *   Writes `{ sprk_briefingstate: 2 }` (Removed) so the item is filtered out of
 *   subsequent `fetchNotifications` calls server-side (per EXCLUDE_REMOVED_FILTER).
 *   The underlying `appnotification` record is preserved — the user can still
 *   see and dismiss it in the native bell panel (FR-7 AC-7b).
 *
 * @param webApi - Xrm.WebApi reference
 * @param notificationId - The appnotificationid GUID
 * @returns IResult<void>
 */
export async function markBriefingRemoved(webApi: IWebApi, notificationId: string): Promise<IResult<void>> {
  return tryCatch(async () => {
    await webApi.updateRecord('appnotification', notificationId, {
      sprk_briefingstate: BRIEFING_STATE_REMOVED,
    });
  }, 'BRIEFING_MARK_REMOVED_ERROR');
}

/**
 * Extend a single Daily Briefing item's time-to-live by 7 calendar days.
 *
 * R3 task 020 / FR-6:
 *   Computes `newTtl = currentTtlSeconds + 604800` and writes
 *   `{ ttlinseconds: newTtl }` via `Xrm.WebApi.updateRecord`. The caller is
 *   responsible for sourcing `currentTtlSeconds` from the item being extended
 *   (the widget displays this in the success toast via the returned new value).
 *
 * Per owner clarification (design.md): no weekend-aware date math; the future
 * due-date engine will own that. This is a literal +604800-second increment.
 *
 * @param webApi - Xrm.WebApi reference
 * @param notificationId - The appnotificationid GUID
 * @param currentTtlSeconds - Current `ttlinseconds` value of the item (>= 0)
 * @returns IResult<number> — the new TTL value in seconds (so the toast can render it)
 */
export async function extendBriefingTtl(
  webApi: IWebApi,
  notificationId: string,
  currentTtlSeconds: number
): Promise<IResult<number>> {
  return tryCatch(async () => {
    const newTtl = currentTtlSeconds + TTL_EXTEND_SECONDS;
    await webApi.updateRecord('appnotification', notificationId, {
      ttlinseconds: newTtl,
    });
    return newTtl;
  }, 'BRIEFING_EXTEND_TTL_ERROR');
}

// R3 task 030 (2026-06-24): the transitional aliases `markNotificationRead` /
// `markAllNotificationsRead` that briefly mirrored `markBriefingChecked` /
// `markAllBriefingsChecked` (added by task 020) have been removed now that
// `useBriefingActions.ts` (task 030) imports the canonical names directly.
// The smoke test `DailyBriefingApp.smoke.test.tsx` is rewired by task 031.
