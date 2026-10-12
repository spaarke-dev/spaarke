/**
 * notifications-client - the in-portal notification feed over the external plane
 * (spaarke-SPA-external-access-platform-r3 task 055; server: task 037, FR-08).
 *
 *   GET  /api/v1/external/notifications        the derived feed, newest first, with an unread count
 *   POST /api/v1/external/notifications/seen   move the caller's last-seen marker to the server's "now"
 *
 * Both go through `bffApiCall` (active backend + active token acquirer), so no BFF URL or scope ever comes from a link.
 *
 * CONTRACT (read from `ExternalNotificationFeedEndpoints.cs` / `ExternalNotificationDtos.cs`, not from docs):
 *   - An item is an allow-list: kind, id, record type/id/title, time, sender name, unread flag. There is NO message body,
 *     subject or address, so this client has nothing of the kind to read or show. Privileged messages are filtered
 *     server-side.
 *   - `recordType` is an API route segment (`projects` | `matters` | `workassignments` | `invoices` | `servicerequests`), not an SPA
 *     route; `components/detail/record-targets.ts` maps it to the detail view.
 *   - `unreadCount` counts the RETURNED page only (at most 100). `truncated` (omitted when false) says the feed is known
 *     to be incomplete, so the real number may be higher.
 *   - Errors are ProblemDetails with `errorCode`: 503 `EXT_NOTIFICATIONS_CONTACT_UNREADABLE` (GET and POST; retryable,
 *     the feed was NOT read, so it is not "empty" and `/seen` must not follow), 503 `EXT_NOTIFICATIONS_SEEN_UNAVAILABLE`
 *     (POST), 500 `EXT_NOTIFICATIONS_INTERNAL_ERROR`, 429 (the shared `dataverse-query` limit, 50 per minute per user).
 */
import { bffApiCall } from '../auth/bff-client';
import { SignInRequiredError } from '../auth/msal-auth';
import { ApiError } from '../types';
import { messageProblemOf } from './messages-client';

export const NOTIFICATIONS_PATH = '/api/v1/external/notifications';
export const NOTIFICATIONS_SEEN_PATH = '/api/v1/external/notifications/seen';

/** The server's feed cap; a page this full is shown as "100+" even if `truncated` were somehow absent. */
export const NOTIFICATION_PAGE_SIZE = 100;

export type NotificationKind = 'grant' | 'message';

/** One feed item, as `ExternalNotificationDto` returns it (and nothing more). */
export interface ExternalNotification {
  kind: NotificationKind;
  /** The source row id (a grant row or a message). */
  id: string;
  /** Route segment: projects | matters | workassignments | invoices | servicerequests. */
  recordType: string;
  recordId: string;
  /** The record's display name; null when the server could not read it. */
  recordTitle: string | null;
  /** ISO-8601 UTC. */
  occurredOn: string;
  /** For a message, the sender's display name; null for a grant or an unrecorded sender. */
  senderName: string | null;
  isUnread: boolean;
}

/** `ExternalNotificationFeedResponse`, normalised. */
export interface NotificationFeed {
  /** Newest first. */
  value: ExternalNotification[];
  /** Unread items in `value` only (the returned page). */
  unreadCount: number;
  lastSeenAt: string | null;
  /** The feed is known to be incomplete (a cut or a failed source read). */
  truncated: boolean;
}

function asString(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

/** Keeps only the allow-listed fields of an item; null for a row that cannot be shown at all. */
function normaliseItem(raw: unknown): ExternalNotification | null {
  if (!raw || typeof raw !== 'object') return null;
  const r = raw as Record<string, unknown>;
  const kind = r.kind === 'grant' || r.kind === 'message' ? r.kind : null;
  const id = asString(r.id);
  const recordType = asString(r.recordType);
  const recordId = asString(r.recordId);
  const occurredOn = asString(r.occurredOn);
  if (!kind || !id || !recordType || !recordId || !occurredOn) return null;
  return {
    kind,
    id,
    recordType,
    recordId,
    recordTitle: asString(r.recordTitle),
    occurredOn,
    senderName: asString(r.senderName),
    isUnread: r.isUnread === true,
  };
}

/** Newest first, whatever order arrived; items at the same instant keep the server's order. */
function newestFirst(items: ExternalNotification[]): ExternalNotification[] {
  return items
    .map((item, index) => ({ item, index, time: Date.parse(item.occurredOn) }))
    .sort((a, b) => {
      const at = Number.isNaN(a.time) ? -Infinity : a.time;
      const bt = Number.isNaN(b.time) ? -Infinity : b.time;
      return bt - at || a.index - b.index;
    })
    .map(entry => entry.item);
}

/** Turns a response body into a {@link NotificationFeed}. A body without a `value` array is not a feed (throws). */
export function normaliseFeed(body: unknown): NotificationFeed {
  const b = (body ?? {}) as Record<string, unknown>;
  if (!Array.isArray(b.value)) {
    throw new Error('The notification feed response had no item list.');
  }
  const value = newestFirst(b.value.map(normaliseItem).filter((i): i is ExternalNotification => i !== null));
  const unreadCount = typeof b.unreadCount === 'number' && b.unreadCount >= 0 ? Math.floor(b.unreadCount) : 0;
  return {
    value,
    unreadCount,
    lastSeenAt: asString(b.lastSeenAt),
    truncated: b.truncated === true,
  };
}

export interface ListNotificationsOptions {
  /**
   * True for the TIMER-driven poll: if the token cannot be had silently, the call rejects with `SignInRequiredError`
   * instead of starting a sign-in redirect (a timer must never navigate the page away). A click-driven read omits it.
   */
  background?: boolean;
}

/** GET the caller's feed. */
export async function listNotifications({
  background = false,
}: ListNotificationsOptions = {}): Promise<NotificationFeed> {
  return normaliseFeed(
    await (background
      ? bffApiCall<unknown>(NOTIFICATIONS_PATH, {}, { noInteraction: true })
      : bffApiCall<unknown>(NOTIFICATIONS_PATH))
  );
}

/** POST the last-seen marker (no body). Resolves with the marker the server stored. */
export async function markNotificationsSeen(): Promise<{ lastSeenAt: string | null }> {
  const body = await bffApiCall<{ lastSeenAt?: unknown } | undefined>(NOTIFICATIONS_SEEN_PATH, { method: 'POST' });
  return { lastSeenAt: asString(body?.lastSeenAt) };
}

/** What a failed call means for the bell. */
export type NotificationFailureKind =
  /** 401 (after bff-client's forced-refresh retry) or a sign-in a background read may not start: sign in again; polling stops. */
  | 'signin'
  /** 403: this caller cannot read a feed at all; polling stops. */
  | 'unauthorized'
  /** 429: the shared per-user limit; back off. */
  | 'rate-limited'
  /** 503 EXT_NOTIFICATIONS_CONTACT_UNREADABLE: nothing was read; retryable. NOT an empty feed. */
  | 'contact-unreadable'
  /** 503 EXT_NOTIFICATIONS_SEEN_UNAVAILABLE: the marker was not stored. */
  | 'seen-unavailable'
  /** 500 / other 5xx / anything else the server answered. */
  | 'server'
  /** No answer: network, token or backend selection failed. */
  | 'network';

export interface NotificationFailure {
  kind: NotificationFailureKind;
  status: number | null;
  errorCode: string | null;
}

export const ERROR_CONTACT_UNREADABLE = 'EXT_NOTIFICATIONS_CONTACT_UNREADABLE';
export const ERROR_SEEN_UNAVAILABLE = 'EXT_NOTIFICATIONS_SEEN_UNAVAILABLE';

/** Classifies a failed feed or seen call. */
export function notificationFailureOf(err: unknown): NotificationFailure {
  // A background read that needed an interactive sign-in (no redirect was started): the same state as a refused token.
  if (err instanceof SignInRequiredError) return { kind: 'signin', status: null, errorCode: null };
  if (!(err instanceof ApiError)) return { kind: 'network', status: null, errorCode: null };
  const { errorCode } = messageProblemOf(err);
  const status = err.statusCode;
  // Same meaning as `BffAccessState.accessFailureOf`: 401 -> sign in again, 403 -> not allowed.
  if (status === 401) return { kind: 'signin', status, errorCode };
  if (status === 403) return { kind: 'unauthorized', status, errorCode };
  if (status === 429) return { kind: 'rate-limited', status, errorCode };
  if (status === 503 && errorCode === ERROR_CONTACT_UNREADABLE)
    return { kind: 'contact-unreadable', status, errorCode };
  if (status === 503 && errorCode === ERROR_SEEN_UNAVAILABLE) return { kind: 'seen-unavailable', status, errorCode };
  return { kind: 'server', status, errorCode };
}

/**
 * The badge text for a feed, or null for no badge. The count is of the returned page only, so when the feed is known to
 * be incomplete (or the page is full) the number is a floor and is shown as "N+" ("100+" for a full page).
 */
export function unreadBadgeText(feed: Pick<NotificationFeed, 'unreadCount' | 'truncated'> | null): string | null {
  if (!feed || feed.unreadCount <= 0) return null;
  const count = Math.min(feed.unreadCount, NOTIFICATION_PAGE_SIZE);
  return feed.truncated || feed.unreadCount >= NOTIFICATION_PAGE_SIZE ? `${count}+` : String(count);
}
