/**
 * useNotificationFeed - polls the in-portal notification feed and marks it seen
 * (spaarke-SPA-external-access-platform-r3 task 055, FR-08).
 *
 * POLLING (project rule: at most every 60 s while the tab is visible, none in the background).
 *   - One request at a time, scheduled with a timer after the previous request, never `setInterval`: the gap between two
 *     requests of ANY kind (a poll or an open-refresh) is at least {@link POLL_INTERVAL_MS}, so the shared 50/min
 *     `dataverse-query` limit (thread reads and intake draw on it too) is never approached by the bell.
 *   - A hidden tab makes no request: the timer parks, and `visibilitychange` re-arms it for whatever is left of the
 *     minute. (The codebase had no visibility pattern before this one.)
 *   - Failures back off: the delay doubles per consecutive failure (429, 503, 500, a network error) up to
 *     {@link MAX_BACKOFF_MS}, and a success resets it. 401/403 stop polling altogether (sign in again / this caller cannot
 *     read a feed); an explicit open tries again.
 *   - A poll never starts a sign-in: it reads with `background: true`, so a token that cannot be had silently ends in the
 *     "sign in again" state (polling stops) instead of a redirect that would navigate the page away from whatever the user is
 *     doing. A click (the bell opened, "Try again") is a user gesture and reads the normal way.
 *   - Polling pauses while the popover is open (the open already refreshed) and resumes, measured from the last request,
 *     when it closes.
 *
 * MARKING SEEN. `refreshAndMarkSeen()` (the bell was opened) re-reads the feed and then, ONLY IF that read succeeded and
 * something in it is unread, posts `/seen` once. A failed read - above all 503 `EXT_NOTIFICATIONS_CONTACT_UNREADABLE`, which
 * means a source was not read - never reaches `/seen`: a marker set over a feed that was not read would bury the items
 * for good. A failed `/seen` leaves the badge as it was (the server marker did not move) and is retried the next time
 * the bell is opened, never in a loop.
 *
 * A failed read never becomes an empty feed: `failure` is set and `feed` keeps the last feed that was really read.
 */
import * as React from 'react';
import {
  listNotifications,
  markNotificationsSeen,
  notificationFailureOf,
  type NotificationFailure,
  type NotificationFeed,
} from '../api/notifications-client';

/** Minimum gap between two feed reads, and the poll period. */
export const POLL_INTERVAL_MS = 60_000;
/** Backoff ceiling. */
export const MAX_BACKOFF_MS = 10 * 60_000;

/** The delay before the next poll after `consecutiveFailures` failures in a row (0 = the last read succeeded). */
export function nextPollDelay(consecutiveFailures: number): number {
  return Math.min(POLL_INTERVAL_MS * 2 ** consecutiveFailures, MAX_BACKOFF_MS);
}

function tabHidden(): boolean {
  return typeof document !== 'undefined' && document.visibilityState === 'hidden';
}

export interface UseNotificationFeedOptions {
  /** True while the popover is open: polling pauses. */
  paused?: boolean;
}

export interface UseNotificationFeedResult {
  /** The last feed that was actually read (after a successful `/seen` its unread flags and count are cleared). */
  feed: NotificationFeed | null;
  /** The most recent read failure; null after a success. Never rendered as an empty feed. */
  failure: NotificationFailure | null;
  /** A read is in flight. */
  loading: boolean;
  /** The last `/seen` failed: the badge is still accurate and the next open retries. */
  seenFailed: boolean;
  /** Ids that were unread when the bell was last opened (so the list can still highlight them once the badge is clear). */
  freshIds: ReadonlySet<string>;
  /** The bell was opened: re-read, then mark seen once if the read succeeded and had unread items. */
  refreshAndMarkSeen: () => Promise<void>;
}

const NO_IDS: ReadonlySet<string> = new Set<string>();

export function useNotificationFeed({ paused = false }: UseNotificationFeedOptions = {}): UseNotificationFeedResult {
  const [feed, setFeed] = React.useState<NotificationFeed | null>(null);
  const [failure, setFailure] = React.useState<NotificationFailure | null>(null);
  const [loading, setLoading] = React.useState(false);
  const [seenFailed, setSeenFailed] = React.useState(false);
  const [freshIds, setFreshIds] = React.useState<ReadonlySet<string>>(NO_IDS);
  // 401/403: no more polling until an explicit open succeeds.
  const [stopped, setStopped] = React.useState(false);

  const mountedRef = React.useRef(true);
  const seqRef = React.useRef(0);
  const failuresRef = React.useRef(0);
  const lastRequestAtRef = React.useRef<number | null>(null);
  const seenInFlightRef = React.useRef(false);

  React.useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  /** One feed read. Only the newest request may change state (an older answer arriving late is dropped). */
  const readFeed = React.useCallback(async (background = false): Promise<NotificationFeed | null> => {
    const seq = ++seqRef.current;
    lastRequestAtRef.current = Date.now();
    setLoading(true);
    try {
      const next = await listNotifications({ background });
      // A newer read took over while this one was in flight: this answer is stale. It changes no state, and it is NOT
      // returned, so an older open-read can never drive `/seen` for a feed the user never saw.
      if (!mountedRef.current || seq !== seqRef.current) return null;
      failuresRef.current = 0;
      setFeed(next);
      setFailure(null);
      setStopped(false);
      return next;
    } catch (err) {
      if (mountedRef.current && seq === seqRef.current) {
        const f = notificationFailureOf(err);
        failuresRef.current += 1;
        setFailure(f);
        // A refused token (401) or 403 stops the poll. A silent acquisition that failed on a background read (no status:
        // `SignInRequiredError`) may be a transient renewal timeout, so it backs off and retries silently instead; a
        // later silent success clears the sign-in state.
        if (f.kind === 'unauthorized' || (f.kind === 'signin' && f.status !== null)) setStopped(true);
      }
      return null;
    } finally {
      if (mountedRef.current && seq === seqRef.current) setLoading(false);
    }
  }, []);

  // The poll loop.
  React.useEffect(() => {
    if (paused || stopped) return;
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | null = null;
    let parked = false;

    // Time left before the next request is allowed: the backoff delay, measured from the last request of any kind.
    const waitMs = (): number => {
      const last = lastRequestAtRef.current;
      if (last === null) return 0;
      return Math.max(0, nextPollDelay(failuresRef.current) - (Date.now() - last));
    };

    const arm = (delay: number): void => {
      if (timer) clearTimeout(timer);
      parked = false;
      timer = setTimeout(() => void tick(), delay);
    };

    const tick = async (): Promise<void> => {
      timer = null;
      if (cancelled) return;
      if (tabHidden()) {
        parked = true; // no request in the background; visibilitychange re-arms
        return;
      }
      await readFeed(true); // background: a timer must never start a sign-in redirect
      if (!cancelled) arm(waitMs());
    };

    const onVisibility = (): void => {
      if (cancelled || tabHidden() || !parked) return;
      arm(waitMs());
    };

    document.addEventListener('visibilitychange', onVisibility);
    arm(waitMs());
    return () => {
      cancelled = true;
      if (timer) clearTimeout(timer);
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [paused, stopped, readFeed]);

  const refreshAndMarkSeen = React.useCallback(async (): Promise<void> => {
    const read = await readFeed();
    // Never /seen after a failed read: a marker over a feed that was not read buries what was never shown.
    if (!read || !mountedRef.current) return;
    setFreshIds(new Set(read.value.filter(i => i.isUnread).map(i => i.id)));
    if (read.unreadCount <= 0) {
      setSeenFailed(false);
      return;
    }
    if (seenInFlightRef.current) return;
    seenInFlightRef.current = true;
    try {
      const marker = await markNotificationsSeen();
      if (!mountedRef.current) return;
      setSeenFailed(false);
      setFeed(current =>
        current
          ? {
              ...current,
              unreadCount: 0,
              lastSeenAt: marker.lastSeenAt ?? current.lastSeenAt,
              value: current.value.map(i => (i.isUnread ? { ...i, isUnread: false } : i)),
            }
          : current
      );
    } catch {
      if (mountedRef.current) setSeenFailed(true);
    } finally {
      seenInFlightRef.current = false;
    }
  }, [readFeed]);

  return { feed, failure, loading, seenFailed, freshIds, refreshAndMarkSeen };
}
