/**
 * TypeScript types for the Daily Briefing notification data layer.
 *
 * These types model the data returned by Xrm.WebApi queries against
 * the appnotification entity and sprk_userpreference entity, plus the
 * grouped/categorized view consumed by UI components.
 *
 * Hoist note (R2 task 015 / FR-07):
 *   Originally lived at `src/solutions/DailyBriefing/src/types/notifications.ts`.
 *   Hoisted verbatim to `@spaarke/daily-briefing-components/types` to close out
 *   the shared-lib boundary debt left after tasks 011-014 (back-pointer imports
 *   from the new package into the standalone solution). Original location
 *   becomes a re-export shim so any pre-existing consumer keeps working
 *   through the standalone-solution import path until cleaned up in a later
 *   pass. Logic is byte-identical from the original — no behavior change.
 */

// ---------------------------------------------------------------------------
// Xrm WebApi types (lightweight, same as LegalWorkspace/types/xrm.ts)
// ---------------------------------------------------------------------------

/** Minimal type for a Dataverse entity record returned by WebApi queries. */
export type WebApiEntity = Record<string, unknown>;

/**
 * Daily-Briefing-scoped read-state values for the custom `sprk_briefingstate`
 * Choice column on `appnotification` (R3 task 001 / FR-1). These decouple the
 * widget's read/checked/removed lifecycle from the native bell-panel
 * (`toasttype`/`isread`) state per FR-7 invariant.
 *
 *   Unread  = 0  (default at Dataverse schema level; null on read coalesces to this)
 *   Checked = 1  (set by widget "Mark as read" action — FR-4)
 *   Removed = 2  (set by widget "Remove from briefing" action — FR-5; filtered from queries)
 */
export const BRIEFING_STATE_UNREAD = 0 as const;
export const BRIEFING_STATE_CHECKED = 1 as const;
export const BRIEFING_STATE_REMOVED = 2 as const;
export type BriefingState =
  | typeof BRIEFING_STATE_UNREAD
  | typeof BRIEFING_STATE_CHECKED
  | typeof BRIEFING_STATE_REMOVED;

/**
 * Documented shape of an `appnotification` entity record fetched via
 * `Xrm.WebApi.retrieveMultipleRecords`. R3 adds `sprk_briefingstate` (FR-3);
 * older rows may surface as `undefined` (FR-3 AC-3c — null-coalesce to Unread).
 *
 * This is a documentary alias of `WebApiEntity` — TypeScript erases the index
 * signature, so it's safe to widen at call sites with `as` casts where the
 * existing code does so.
 */
export interface AppNotificationEntity extends WebApiEntity {
  appnotificationid?: string;
  title?: string;
  body?: string;
  /** JSON-string payload — parsed by `parseNotificationData`. */
  data?: string;
  /** Microsoft "Timed" / "Persistent" display behavior (NOT a read marker — FR-7). */
  toasttype?: number;
  createdon?: string;
  /**
   * R3 custom Choice column — Daily Briefing read-state (Unread / Checked / Removed).
   * Optional because pre-rollout existing rows have no value (treated as Unread).
   */
  sprk_briefingstate?: number;
  /**
   * Time-to-live in seconds before Dataverse auto-purge. Extended by the
   * "Keep 7 more days" action (R3 FR-6) via `+ 604800`.
   */
  ttlinseconds?: number;
}

/** Result shape from retrieveMultipleRecords. */
export interface RetrieveMultipleResult {
  entities: WebApiEntity[];
  nextLink?: string;
}

/** Minimal WebApi interface matching the methods used by notification services. */
export interface IWebApi {
  retrieveMultipleRecords(
    entityLogicalName: string,
    options?: string,
    maxPageSize?: number
  ): Promise<RetrieveMultipleResult>;

  retrieveRecord(entityLogicalName: string, id: string, options?: string): Promise<WebApiEntity>;

  createRecord(entityLogicalName: string, data: WebApiEntity): Promise<{ id: string }>;

  updateRecord(entityLogicalName: string, id: string, data: WebApiEntity): Promise<{ id: string }>;

  deleteRecord(entityLogicalName: string, id: string): Promise<{ id: string }>;
}

// ---------------------------------------------------------------------------
// Result pattern (same as LegalWorkspace/types/result.ts)
// ---------------------------------------------------------------------------

/** Error detail attached to a failed IResult. */
export interface IResultError {
  code: string;
  message: string;
  raw?: unknown;
}

export interface ISuccessResult<T> {
  success: true;
  data: T;
}

export interface IFailureResult {
  success: false;
  error: IResultError;
}

export type IResult<T> = ISuccessResult<T> | IFailureResult;

export function ok<T>(data: T): ISuccessResult<T> {
  return { success: true, data };
}

export function fail(code: string, message: string, raw?: unknown): IFailureResult {
  return { success: false, error: { code, message, raw } };
}

export async function tryCatch<T>(fn: () => Promise<T>, errorCode: string = 'UNKNOWN_ERROR'): Promise<IResult<T>> {
  try {
    const data = await fn();
    return ok(data);
  } catch (e: unknown) {
    const raw = e;
    let message = 'An unexpected error occurred.';
    if (e && typeof e === 'object') {
      const err = e as Record<string, unknown>;
      if (typeof err['message'] === 'string') {
        message = err['message'];
      }
    } else if (typeof e === 'string') {
      message = e;
    }
    return fail(errorCode, message, raw);
  }
}

// ---------------------------------------------------------------------------
// Notification categories (channels)
// ---------------------------------------------------------------------------

/**
 * Notification categories matching the 7 notification playbooks.
 * The category string is stored in appnotification.data JSON as
 * customData.category by CreateNotificationNodeExecutor.
 */
export type NotificationCategory =
  | 'tasks-overdue'
  | 'tasks-due-soon'
  | 'new-documents'
  | 'new-emails'
  | 'new-events'
  | 'matter-activity'
  | 'work-assignments'
  | 'system';

/** Display metadata for each notification category/channel. */
export interface ChannelMeta {
  category: NotificationCategory;
  label: string;
  /** Fluent icon name for the channel header. */
  iconName: string;
  /** Sort order for display. */
  order: number;
}

/** Registry of known notification channels with display metadata. */
export const CHANNEL_REGISTRY: Record<NotificationCategory, ChannelMeta> = {
  'tasks-overdue': {
    category: 'tasks-overdue',
    label: 'Overdue Tasks',
    iconName: 'Warning',
    order: 1,
  },
  'tasks-due-soon': {
    category: 'tasks-due-soon',
    label: 'Tasks Due Soon',
    iconName: 'Clock',
    order: 2,
  },
  'new-documents': {
    category: 'new-documents',
    label: 'New Documents',
    iconName: 'Document',
    order: 3,
  },
  'new-emails': {
    category: 'new-emails',
    label: 'New Emails',
    iconName: 'Mail',
    order: 4,
  },
  'new-events': {
    category: 'new-events',
    label: 'Upcoming Events',
    iconName: 'Calendar',
    order: 5,
  },
  'matter-activity': {
    category: 'matter-activity',
    label: 'Matter Activity',
    iconName: 'Briefcase',
    order: 6,
  },
  'work-assignments': {
    category: 'work-assignments',
    label: 'Work Assignments',
    iconName: 'People',
    order: 7,
  },
  system: {
    category: 'system',
    label: 'System',
    iconName: 'Info',
    order: 99,
  },
};

// ---------------------------------------------------------------------------
// Notification item (parsed from appnotification entity)
// ---------------------------------------------------------------------------

/** Priority levels for notifications. */
export type NotificationPriority = 'low' | 'normal' | 'high' | 'urgent';

/**
 * Parsed notification item extracted from an appnotification record.
 *
 * The appnotification entity stores its payload in a `data` JSON field.
 * The category and action URL live inside customData within that JSON.
 */
export interface NotificationItem {
  /** appnotificationid GUID */
  id: string;
  /** Title text from the notification */
  title: string;
  /** Body text (narrative sentence) */
  body: string;
  /** Resolved category from customData.category */
  category: NotificationCategory;
  /** Priority level from customData.priority */
  priority: NotificationPriority;
  /** URL to navigate to the source record */
  actionUrl: string;
  /** Display name of the regarding record */
  regardingName: string;
  /** Logical name of the regarding entity */
  regardingEntityType: string;
  /** GUID of the regarding record */
  regardingId: string;
  /** Whether the notification has been read */
  isRead: boolean;
  /** Whether this notification was AI-generated */
  isAiGenerated: boolean;
  /** AI confidence score (0-100), if applicable */
  aiConfidence?: number;
  /** ISO timestamp when the notification was created */
  createdOn: string;
  /**
   * Optional ISO-8601 due date from notification customData.dueDate (R2.2).
   * Set by task playbooks (notification-tasks-overdue, notification-tasks-due-soon)
   * via the BFF CreateNotificationNodeExecutor's DueDate field. Null when the
   * source channel has no due-date concept (documents, emails, work-assignments,
   * matter-activity, events).
   */
  dueDate: string | null;
  /**
   * R3 FR-6: current Dataverse `appnotification.ttlinseconds` value, surfaced
   * so the "Keep 7 more days" action can compute `newTtl = current + 604800`
   * additively (rather than writing a flat 604800 which would shorten TTL
   * for items already extended). Undefined for pre-rollout existing rows
   * with no stored TTL (those fall back to tenant default 14d at Dataverse);
   * on Keep, we coerce to 0 → write 604800 (7d explicit). New rows after
   * task 010 ships always have ttlinseconds=604800 written by the producer.
   */
  ttlinseconds?: number;
}

// ---------------------------------------------------------------------------
// Grouped channel view
// ---------------------------------------------------------------------------

/**
 * A group of notifications under a single category/channel,
 * ready for rendering by the channel component.
 */
export interface ChannelGroup {
  /** Channel metadata (label, icon, order) */
  meta: ChannelMeta;
  /** Notification items in this channel, sorted by createdOn desc */
  items: NotificationItem[];
  /** Count of unread items in this channel */
  unreadCount: number;
}

/**
 * Fetch status for an individual channel within Promise.allSettled.
 * Allows per-channel error display without crashing the entire digest.
 */
export type ChannelFetchResult =
  | { status: 'success'; group: ChannelGroup }
  | { status: 'error'; category: NotificationCategory; error: string };

// ---------------------------------------------------------------------------
// User preferences
// ---------------------------------------------------------------------------

/**
 * Dataverse choice value for the DailyDigestPreferences preference type.
 * Must match the sprk_preferencetype option set value in Dataverse.
 */
export const PREFERENCE_TYPE_DAILY_DIGEST = 100000002;

/** Due-soon window options (days). */
export type DueWindowDays = 1 | 2 | 3 | 5 | 7;

/** Time window options for recency filtering. */
export type TimeWindow = '12h' | '24h' | '48h' | '7d';

/**
 * User preferences for the Daily Digest, stored as JSON in
 * sprk_userpreference.sprk_preferencevalue.
 *
 * Opt-out model: all channels enabled by default. Only overrides are stored.
 *
 * R4 task 044 / FR-17e (2026-06-26): an AI confidence threshold preference
 * was removed because the daily-briefing data is deterministic — there is no
 * probabilistic scoring concept to threshold. Legacy persisted JSON
 * containing the old field loads without error because `mergeWithDefaults`
 * only destructures known fields (TypeScript ignores extra keys at runtime).
 */
export interface DailyDigestPreferences {
  /** Channels the user has disabled (opt-out). Empty = all enabled. */
  disabledChannels: NotificationCategory[];
  /** Due-soon window in days (default: 3). */
  dueWithinDays: DueWindowDays;
  /** Recency time window (default: "24h"). */
  timeWindow: TimeWindow;
  /** Whether to auto-popup the digest on workspace launch (default: true). */
  autoPopup: boolean;
}

/** Default preferences (opt-out model: everything enabled). */
export const DEFAULT_DAILY_DIGEST_PREFERENCES: DailyDigestPreferences = {
  disabledChannels: [],
  dueWithinDays: 3,
  timeWindow: '24h',
  autoPopup: true,
};

// ---------------------------------------------------------------------------
// Aggregate data shape for the hook
// ---------------------------------------------------------------------------

/** Loading states for the notification data hook. */
export type LoadingState = 'idle' | 'loading' | 'loaded' | 'error';

/**
 * Aggregate data returned by useNotificationData() hook.
 */
export interface NotificationData {
  /** All channel fetch results (success or per-channel error). */
  channels: ChannelFetchResult[];
  /** Total unread count across all channels. */
  totalUnreadCount: number;
  /** User preferences (loaded or defaults). */
  preferences: DailyDigestPreferences;
  /** Overall loading state. */
  loadingState: LoadingState;
  /** Error message if the entire fetch failed. */
  error?: string;
}
