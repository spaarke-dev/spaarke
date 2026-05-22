/**
 * notificationContextLoader.ts — SpaarkeAi-local notification context loader.
 *
 * Task 086 / Round 4 Fix 3 — Make the SpaarkeAi WorkspaceHomeTab's Daily Briefing
 * section FUNCTIONAL on cold load. Previously the shared `useDailyBriefing` hook
 * (task 069) auto-fired the narrate endpoint with an empty envelope, the BFF
 * returned 200/empty bullets (task 083), and the operator saw "Nothing to see
 * right now — enjoy your day" instead of a real briefing.
 *
 * This module mirrors the standalone Daily Briefing Code Page's working data
 * path EXACTLY (operator principle: REUSE working code). It is a faithful
 * SpaarkeAi-local copy of:
 *
 *   - `src/solutions/DailyBriefing/src/services/notificationService.ts`
 *       (fetchAndGroupNotifications via Xrm.WebApi `appnotification` query)
 *   - `src/solutions/DailyBriefing/src/services/briefingService.ts#buildNarrationRequest`
 *       (categories + priorityItems + channels payload assembly)
 *
 * Option C (Copy) was chosen over Option H (Hoist) per task 084 precedent:
 * the standalone code page is its own bundle and its `useNotificationData`
 * hook returns the SAME `ChannelFetchResult[]` shape but couples preferences,
 * mark-as-read, and refresh actions that SpaarkeAi's Home tab doesn't need.
 * A clean hoist would require deeper redesign — out of scope for a fix task.
 * The copy carries a JSDoc cross-reference to its source so both stay in sync.
 *
 * Constraints:
 *   - ADR-012: Solution-local module — does NOT touch `@spaarke/ui-components`.
 *     The shared lib is unaware of Dataverse entity strings (`appnotification`,
 *     `customData`, `toasttype`).
 *   - ADR-013: No new BFF service — the loader is purely client-side; the BFF
 *     narrate endpoint is unchanged.
 *   - ADR-028: This module never touches MSAL or access tokens. The Xrm.WebApi
 *     reference is resolved via frame-walking (in-MDA), which the platform
 *     already authenticates.
 *   - FR-25: Standalone LegalWorkspace + standalone Daily Briefing Code Page
 *     are unaffected — this module is SpaarkeAi-local.
 *
 * Returns a `NarrateRequest` shaped exactly to match
 * `useDailyBriefing.NarrateRequest`. Returns `null` when no notification data
 * is available (e.g., Xrm.WebApi unresolved during dev / direct-URL bootstrap)
 * so the shared hook falls back to the empty-payload contract (→ empty-state UI).
 */

import type { NarrateRequest } from "@spaarke/ui-components";

// ---------------------------------------------------------------------------
// Notification category registry (mirrors DailyBriefing solution's
// CHANNEL_REGISTRY in types/notifications.ts). The category strings here MUST
// match the values produced by `CreateNotificationNodeExecutor` server-side.
// ---------------------------------------------------------------------------

type NotificationCategory =
  | "tasks-overdue"
  | "tasks-due-soon"
  | "new-documents"
  | "new-emails"
  | "new-events"
  | "matter-activity"
  | "work-assignments"
  | "system";

type NotificationPriority = "low" | "normal" | "high" | "urgent";

interface ChannelMeta {
  category: NotificationCategory;
  label: string;
  order: number;
}

const CHANNEL_REGISTRY: Record<NotificationCategory, ChannelMeta> = {
  "tasks-overdue": { category: "tasks-overdue", label: "Overdue Tasks", order: 1 },
  "tasks-due-soon": { category: "tasks-due-soon", label: "Tasks Due Soon", order: 2 },
  "new-documents": { category: "new-documents", label: "New Documents", order: 3 },
  "new-emails": { category: "new-emails", label: "New Emails", order: 4 },
  "new-events": { category: "new-events", label: "Upcoming Events", order: 5 },
  "matter-activity": { category: "matter-activity", label: "Matter Activity", order: 6 },
  "work-assignments": { category: "work-assignments", label: "Work Assignments", order: 7 },
  system: { category: "system", label: "System", order: 99 },
};

// ---------------------------------------------------------------------------
// Internal types matching DailyBriefing/types/notifications.ts shape
// ---------------------------------------------------------------------------

interface NotificationItem {
  id: string;
  title: string;
  body: string;
  category: NotificationCategory;
  priority: NotificationPriority;
  regardingName: string;
  regardingEntityType: string;
  regardingId: string;
  isRead: boolean;
  createdOn: string;
}

interface ChannelGroup {
  meta: ChannelMeta;
  items: NotificationItem[];
  unreadCount: number;
}

// ---------------------------------------------------------------------------
// Minimal Xrm.WebApi typing (mirrors DailyBriefing/types/notifications.ts#IWebApi)
// ---------------------------------------------------------------------------

interface RetrieveMultipleResult {
  entities: Record<string, unknown>[];
  nextLink?: string;
}

interface IWebApi {
  retrieveMultipleRecords(
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number,
  ): Promise<RetrieveMultipleResult>;
}

// ---------------------------------------------------------------------------
// Xrm frame-walk resolution (mirrors WorkspacePaneMenu.getXrm + DailyBriefing's
// main.tsx polling logic — Xrm may not be present in dev/Vite).
// ---------------------------------------------------------------------------

function getXrm(): {
  WebApi?: IWebApi;
  Utility?: {
    getGlobalContext?: () => {
      userSettings?: { userId?: string };
    };
  };
} | null {
  if (typeof window === "undefined") return null;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const w = window as any;
  return w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm ?? null;
}

// ---------------------------------------------------------------------------
// Parse the appnotification.data JSON column (mirrors
// DailyBriefing/services/notificationService.ts#parseNotificationData).
// ---------------------------------------------------------------------------

function parseNotificationData(raw: unknown): {
  category: NotificationCategory;
  priority: NotificationPriority;
  regardingName: string;
  regardingEntityType: string;
  regardingId: string;
} | null {
  if (!raw || typeof raw !== "string") return null;
  try {
    const parsed = JSON.parse(raw) as Record<string, unknown>;
    const customData = (parsed["customData"] as Record<string, unknown>) ?? parsed;
    return {
      category:
        ((customData["category"] as NotificationCategory) ??
          (customData["channel"] as NotificationCategory) ??
          "system"),
      priority: ((customData["priority"] as NotificationPriority) ?? "normal"),
      regardingName: (customData["regardingName"] as string) ?? "",
      regardingEntityType:
        (customData["regardingEntityType"] as string) ??
        (customData["regardingType"] as string) ??
        "",
      regardingId:
        (customData["regardingId"] as string) ??
        (parsed["regardingId"] as string) ??
        "",
    };
  } catch {
    return null;
  }
}

function toNotificationItem(entity: Record<string, unknown>): NotificationItem | null {
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
    regardingName: customData?.regardingName ?? "",
    regardingEntityType: customData?.regardingEntityType ?? "",
    regardingId: customData?.regardingId ?? "",
    isRead: (entity["toasttype"] as number) === 200000000,
    createdOn: (entity["createdon"] as string) ?? new Date().toISOString(),
  };
}

// ---------------------------------------------------------------------------
// Query + group (mirrors fetchAndGroupNotifications + groupByCategory).
// ---------------------------------------------------------------------------

const NOTIFICATION_SELECT = [
  "appnotificationid",
  "title",
  "body",
  "data",
  "toasttype",
  "createdon",
].join(",");

const MAX_NOTIFICATIONS = 200;

async function fetchAndGroupNotifications(webApi: IWebApi): Promise<ChannelGroup[]> {
  const query =
    `?$select=${NOTIFICATION_SELECT}` +
    `&$orderby=createdon desc` +
    `&$top=${MAX_NOTIFICATIONS}`;

  const result = await webApi.retrieveMultipleRecords(
    "appnotification",
    query,
    MAX_NOTIFICATIONS,
  );

  const items: NotificationItem[] = [];
  for (const entity of result.entities) {
    const item = toNotificationItem(entity);
    if (item) items.push(item);
  }

  // Group by category
  const groups = new Map<NotificationCategory, NotificationItem[]>();
  for (const item of items) {
    const existing = groups.get(item.category);
    if (existing) existing.push(item);
    else groups.set(item.category, [item]);
  }

  const channelGroups: ChannelGroup[] = [];
  for (const [category, categoryItems] of groups) {
    const meta = CHANNEL_REGISTRY[category] ?? CHANNEL_REGISTRY["system"];
    channelGroups.push({
      meta,
      items: categoryItems.sort(
        (a, b) => new Date(b.createdOn).getTime() - new Date(a.createdOn).getTime(),
      ),
      unreadCount: categoryItems.filter((i) => !i.isRead).length,
    });
  }

  channelGroups.sort((a, b) => a.meta.order - b.meta.order);
  return channelGroups;
}

// ---------------------------------------------------------------------------
// Build the narrate request payload (mirrors briefingService.ts#buildNarrationRequest).
// ---------------------------------------------------------------------------

function buildNarrationRequest(channels: ChannelGroup[]): NarrateRequest {
  const categories: NarrateRequest["categories"] = [];
  const priorityItems: NarrateRequest["priorityItems"] = [];
  const channelInputs: NarrateRequest["channels"] = [];
  let totalCount = 0;

  for (const ch of channels) {
    const { meta, items, unreadCount } = ch;
    categories.push({
      name: meta.label,
      count: items.length,
      unreadCount,
    });
    totalCount += items.length;

    for (const item of items) {
      if (
        (item.priority === "high" || item.priority === "urgent") &&
        priorityItems.length < 5
      ) {
        priorityItems.push({
          category: meta.label,
          title: item.title,
          dueDate: null,
        });
      }
    }

    channelInputs.push({
      category: meta.category,
      label: meta.label,
      items: items.map((item) => ({
        id: item.id,
        title: item.title,
        body: item.body,
        priority: item.priority,
        regardingName: item.regardingName,
        regardingEntityType: item.regardingEntityType,
        regardingId: item.regardingId,
        createdOn: item.createdOn,
      })),
    });
  }

  return {
    categories,
    priorityItems,
    totalNotificationCount: totalCount,
    channels: channelInputs,
  };
}

// ---------------------------------------------------------------------------
// Public API — the callback supplied to `useDailyBriefing.loadNotificationContext`.
// ---------------------------------------------------------------------------

/**
 * Load the Daily Briefing notification context for SpaarkeAi's
 * WorkspaceHomeTab. Resolves Xrm.WebApi via frame-walk, queries the
 * `appnotification` entity (same logic as the standalone Daily Briefing
 * Code Page), groups by category, and builds the `NarrateRequest` payload
 * expected by `POST /api/ai/daily-briefing/narrate`.
 *
 * Returns `null` when:
 *   - Xrm.WebApi is unavailable (dev / Vite bootstrap / direct-URL with no
 *     MDA host). The shared hook then falls back to the empty payload so the
 *     section renders the empty-state UI instead of throwing.
 *   - The query returns zero notifications. The shared hook still sends the
 *     populated (but empty) payload — the BFF returns empty bullets and the
 *     section renders the empty state.
 *
 * Throws ONLY if the Xrm.WebApi call itself rejects with an unexpected
 * error — the shared hook catches and logs a warning, then falls back to
 * the empty payload (no UI crash).
 */
export async function loadSpaarkeAiNotificationContext(): Promise<NarrateRequest | null> {
  const xrm = getXrm();
  const webApi = xrm?.WebApi;
  if (!webApi) {
    // No Xrm host — caller falls back to empty payload (empty-state UX).
    return null;
  }

  const channels = await fetchAndGroupNotifications(webApi);
  if (channels.length === 0) {
    // No notifications at all — return null so the empty-payload fallback
    // triggers the BFF's 200/empty-bullets short-circuit (task 083).
    return null;
  }

  return buildNarrationRequest(channels);
}
