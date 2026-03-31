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
 */

import type {
  IWebApi,
  WebApiEntity,
  NotificationItem,
  NotificationCategory,
  NotificationPriority,
  ChannelGroup,
  ChannelFetchResult,
} from "../types/notifications";
import {
  CHANNEL_REGISTRY,
  tryCatch,
} from "../types/notifications";
import type { IResult } from "../types/notifications";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** OData columns to select from appnotification. */
const NOTIFICATION_SELECT = [
  "appnotificationid",
  "title",
  "body",
  "data",
  "isread",
  "createdon",
].join(",");

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
} | null {
  if (!raw || typeof raw !== "string") return null;

  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    const customData = parsed["customData"] as Record<string, unknown> | undefined;
    if (!customData) return null;

    return {
      category: (customData["category"] as NotificationCategory) ?? "system",
      priority: (customData["priority"] as NotificationPriority) ?? "normal",
      actionUrl: (customData["actionUrl"] as string) ?? "",
      regardingName: (customData["regardingName"] as string) ?? "",
      regardingEntityType: (customData["regardingEntityType"] as string) ?? "",
      regardingId: (customData["regardingId"] as string) ?? "",
      isAiGenerated: (customData["isAiGenerated"] as boolean) ?? false,
      aiConfidence: typeof customData["aiConfidence"] === "number"
        ? (customData["aiConfidence"] as number)
        : undefined,
    };
  } catch {
    console.warn("[DailyBriefing] Failed to parse notification data JSON");
    return null;
  }
}

/**
 * Convert a raw appnotification WebApi entity to a typed NotificationItem.
 * Returns null if the record cannot be parsed.
 */
function toNotificationItem(entity: WebApiEntity): NotificationItem | null {
  const id = entity["appnotificationid"] as string | undefined;
  const title = entity["title"] as string | undefined;
  if (!id || !title) return null;

  const customData = parseNotificationData(entity["data"]);

  return {
    id,
    title,
    body: (entity["body"] as string) ?? "",
    category: customData?.category ?? "system",
    priority: customData?.priority ?? "normal",
    actionUrl: customData?.actionUrl ?? "",
    regardingName: customData?.regardingName ?? "",
    regardingEntityType: customData?.regardingEntityType ?? "",
    regardingId: customData?.regardingId ?? "",
    isRead: (entity["isread"] as boolean) ?? false,
    isAiGenerated: customData?.isAiGenerated ?? false,
    aiConfidence: customData?.aiConfidence,
    createdOn: (entity["createdon"] as string) ?? new Date().toISOString(),
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

  // Build OData query — appnotification is automatically scoped to the current user
  let filter = "";
  if (unreadOnly) {
    filter = "&$filter=isread eq false";
  }

  const query =
    `?$select=${NOTIFICATION_SELECT}` +
    filter +
    `&$orderby=createdon desc` +
    `&$top=${top}`;

  return tryCatch(async () => {
    const result = await webApi.retrieveMultipleRecords(
      "appnotification",
      query,
      top
    );

    const items: NotificationItem[] = [];
    for (const entity of result.entities) {
      const item = toNotificationItem(entity);
      if (item) {
        items.push(item);
      }
    }

    return items;
  }, "NOTIFICATIONS_FETCH_ERROR");
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
    const meta = CHANNEL_REGISTRY[category] ?? CHANNEL_REGISTRY["system"];
    channelGroups.push({
      meta,
      items: categoryItems.sort(
        (a, b) => new Date(b.createdOn).getTime() - new Date(a.createdOn).getTime()
      ),
      unreadCount: categoryItems.filter((i) => !i.isRead).length,
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
export async function fetchAndGroupNotifications(
  webApi: IWebApi
): Promise<ChannelFetchResult[]> {
  const result = await fetchNotifications(webApi);

  if (!result.success) {
    // Total failure — return a single error entry
    return [
      {
        status: "error",
        category: "system",
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
      return { status: "success", group };
    } catch (err) {
      return {
        status: "error",
        category: group.meta.category,
        error: err instanceof Error ? err.message : "Unknown error",
      };
    }
  });

  const settled = await Promise.allSettled(channelPromises);

  return settled.map((result) => {
    if (result.status === "fulfilled") {
      return result.value;
    }
    return {
      status: "error" as const,
      category: "system" as NotificationCategory,
      error: result.reason instanceof Error ? result.reason.message : "Unknown error",
    };
  });
}

/**
 * Mark a single notification as read.
 *
 * @param webApi - Xrm.WebApi reference
 * @param notificationId - The appnotificationid GUID
 * @returns IResult<void>
 */
export async function markNotificationRead(
  webApi: IWebApi,
  notificationId: string
): Promise<IResult<void>> {
  return tryCatch(async () => {
    await webApi.updateRecord("appnotification", notificationId, {
      isread: true,
    });
  }, "NOTIFICATION_MARK_READ_ERROR");
}

/**
 * Mark all notifications as read for the current user.
 * Fetches unread notifications and updates each one.
 *
 * @param webApi - Xrm.WebApi reference
 * @returns IResult<{ succeeded: number; failed: number }>
 */
export async function markAllNotificationsRead(
  webApi: IWebApi
): Promise<IResult<{ succeeded: number; failed: number }>> {
  return tryCatch(async () => {
    const unread = await fetchNotifications(webApi, { unreadOnly: true });
    if (!unread.success) {
      throw new Error(unread.error.message);
    }

    let succeeded = 0;
    let failed = 0;

    // Use Promise.allSettled for parallel mark-read operations
    const results = await Promise.allSettled(
      unread.data.map((item) =>
        webApi.updateRecord("appnotification", item.id, { isread: true })
      )
    );

    for (const r of results) {
      if (r.status === "fulfilled") {
        succeeded++;
      } else {
        failed++;
      }
    }

    return { succeeded, failed };
  }, "NOTIFICATION_MARK_ALL_READ_ERROR");
}
