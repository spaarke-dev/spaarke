/**
 * useThreadPoll.ts
 *
 * ~5s poll loop (task 060, step 5 / NFR-07) driving `<CommunicationTimeline />`.
 * On each tick, calls `readThread` + `getUnreadCount` and hands the results
 * back to the caller via `onMessages`/`onUnread` — this hook owns NO reducer
 * state itself (the component does), keeping it a plain, testable I/O loop.
 *
 * Requirements implemented here:
 *   - Configurable interval, default 5000ms.
 *   - Pauses while `document.hidden` (Page Visibility API) — a tick simply
 *     no-ops rather than calling the network.
 *   - Resumes promptly on focus/visibility-regain (an immediate poll fires
 *     rather than waiting for the next interval tick).
 *   - Guards overlapping in-flight polls — a tick is skipped if the previous
 *     one hasn't resolved yet (bounds BFF load on slow responses).
 *   - Cleans up the interval + listeners on unmount (no leak).
 *
 * React-16.14-safe (ADR-022 — `Spaarke.UI.Components` ships into PCF): only
 * `useEffect`/`useCallback`/`useRef`, no React-18-only APIs.
 */
import * as React from 'react';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import { readThread, getUnreadCount } from '../../../services/communicationTimelineApi';
import { mapThreadMessageDtoToTimelineMessage } from '../CommunicationTimeline.buildTimeline';
import type { TimelineMessage } from '../CommunicationTimeline.types';

export interface UseThreadPollOptions {
  threadId: string;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl?: string;
  /** Default 5000ms (NFR-07). Prop-overridable per `CommunicationTimelineProps.pollIntervalMs`. */
  pollIntervalMs?: number;
  /** Newest rendered `createdOn` — read fresh each tick so the hook never closes over a stale cursor. */
  sinceCursorRef: React.MutableRefObject<string | undefined>;
  /** Current last-seen watermark — read fresh each tick, same reasoning. */
  unreadSinceRef: React.MutableRefObject<string | undefined>;
  onMessages: (messages: TimelineMessage[]) => void;
  onUnread: (unreadCount: number) => void;
  onError?: (error: unknown) => void;
  /** Set false to suspend polling entirely (e.g. missing threadId). Default true. */
  enabled?: boolean;
}

export interface UseThreadPollResult {
  /** Triggers an out-of-band poll immediately (used after a send, for optimistic-then-reconcile refresh). */
  pollNow: () => void;
}

export function useThreadPoll(options: UseThreadPollOptions): UseThreadPollResult {
  const {
    threadId,
    authenticatedFetch,
    bffBaseUrl,
    pollIntervalMs = 5000,
    sinceCursorRef,
    unreadSinceRef,
    onMessages,
    onUnread,
    onError,
    enabled = true,
  } = options;

  const inFlightRef = React.useRef(false);

  const poll = React.useCallback(
    async (isInitialLoad: boolean) => {
      if (inFlightRef.current) return; // guard overlapping in-flight polls
      if (typeof document !== 'undefined' && document.hidden) return; // pause while tab hidden

      inFlightRef.current = true;
      try {
        const since = isInitialLoad ? undefined : sinceCursorRef.current;
        const [threadResult, unreadResult] = await Promise.all([
          readThread(threadId, { since }, { authenticatedFetch, bffBaseUrl }),
          getUnreadCount(threadId, { since: unreadSinceRef.current }, { authenticatedFetch, bffBaseUrl }),
        ]);

        onMessages(threadResult.messages.map(mapThreadMessageDtoToTimelineMessage));
        onUnread(unreadResult.unreadCount);
      } catch (err) {
        onError?.(err);
      } finally {
        inFlightRef.current = false;
      }
    },
    [threadId, authenticatedFetch, bffBaseUrl, sinceCursorRef, unreadSinceRef, onMessages, onUnread, onError]
  );

  React.useEffect(() => {
    if (!enabled || !threadId) return undefined;

    let cancelled = false;

    void poll(true); // initial full load (no `since`)

    const intervalId = setInterval(() => {
      if (!cancelled) void poll(false);
    }, pollIntervalMs);

    const handleVisibilityOrFocus = () => {
      if (!cancelled && typeof document !== 'undefined' && !document.hidden) {
        void poll(false); // resume promptly rather than waiting for the next tick
      }
    };

    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', handleVisibilityOrFocus);
    }
    if (typeof window !== 'undefined') {
      window.addEventListener('focus', handleVisibilityOrFocus);
    }

    return () => {
      cancelled = true;
      clearInterval(intervalId);
      if (typeof document !== 'undefined') {
        document.removeEventListener('visibilitychange', handleVisibilityOrFocus);
      }
      if (typeof window !== 'undefined') {
        window.removeEventListener('focus', handleVisibilityOrFocus);
      }
    };
  }, [enabled, threadId, pollIntervalMs, poll]);

  const pollNow = React.useCallback(() => {
    void poll(false);
  }, [poll]);

  return { pollNow };
}
