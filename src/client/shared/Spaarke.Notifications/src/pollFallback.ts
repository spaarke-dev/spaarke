import { authenticatedFetch } from '@spaarke/auth';
import type { NotificationEvent, PendingNotificationItem } from './types';

/**
 * Task 022's kind-generic pending/poll fallback endpoint (spec FR-06 — the ADR-032
 * degrade path every consumer shares): `GET /api/notifications/pending`, authenticated,
 * caller identity derived server-side from the JWT (no target-user parameter).
 *
 * Response shape CONFIRMED against the shipped
 * `Sprk.Bff.Api.Api.Notifications.NotificationsPendingResponse` (task 022, landed
 * during this task's parallel wave — verified post-hoc, not assumed): a JSON object
 * `{ "items": [ { "outboxRowId": "...", "kind": "...", "envelope": { ... } }, ... ] }`.
 * `extractItems` also accepts a bare array defensively (cheap robustness against a
 * future response-shape tweak) but the wrapped `{ items }` shape is what the server
 * actually returns today.
 */
const PENDING_PATH = '/notifications/pending';

/** Default poll interval when the live connection is unavailable (30s). */
export const DEFAULT_POLL_INTERVAL_MS = 30_000;

/** Ceiling for exponential backoff on repeated poll failures (5 min). */
export const DEFAULT_POLL_MAX_BACKOFF_MS = 5 * 60_000;

export interface PollFallbackOptions {
  /** Base interval between successful polls. Default {@link DEFAULT_POLL_INTERVAL_MS}. */
  intervalMs?: number;
  /** Ceiling for exponential backoff after consecutive failures. Default {@link DEFAULT_POLL_MAX_BACKOFF_MS}. */
  maxBackoffMs?: number;
  /** Invoked once per successfully-fetched pending item. */
  onEvent: (event: NotificationEvent) => void;
  /** Invoked when a poll tick fails (network error, non-2xx, parse error). Non-fatal — polling continues. */
  onError?: (err: unknown) => void;
}

export interface PollFallbackHandle {
  /** Stops the poll loop. Idempotent. */
  stop(): void;
  /** True while the loop is still scheduling ticks. */
  readonly isRunning: boolean;
}

/**
 * Starts a bounded-interval poll loop against task 022's pending endpoint (spec
 * FR-06 / NFR-04). This is the client half of the ADR-032 degrade path: when the
 * live SignalR connection is down or negotiate failed, the caller (see
 * `NotificationsClient`) starts this loop so signals are never silently dropped.
 *
 * Backoff: on a failed tick, the next interval doubles (capped at `maxBackoffMs`);
 * a successful tick resets the interval back to `intervalMs`.
 */
export function startPollFallback(options: PollFallbackOptions): PollFallbackHandle {
  const baseIntervalMs = options.intervalMs ?? DEFAULT_POLL_INTERVAL_MS;
  const maxBackoffMs = options.maxBackoffMs ?? DEFAULT_POLL_MAX_BACKOFF_MS;

  let stopped = false;
  let currentDelayMs = baseIntervalMs;
  let timer: ReturnType<typeof setTimeout> | undefined;

  const scheduleNext = (delayMs: number): void => {
    if (stopped) {
      return;
    }
    timer = setTimeout(tick, delayMs);
  };

  async function tick(): Promise<void> {
    if (stopped) {
      return;
    }

    try {
      const response = await authenticatedFetch(PENDING_PATH, { method: 'GET' });
      const data: unknown = await response.json();
      const items = extractItems(data);

      // currentDelayMs resets on a successful FETCH, independent of whether a caller's
      // onEvent handler subsequently throws — a bug in the caller's handler must not be
      // misreported as a poll-fetch failure (which would wrongly trigger backoff/onError
      // for a tick that actually succeeded).
      currentDelayMs = baseIntervalMs;

      for (const item of items) {
        try {
          options.onEvent({
            outboxRowId: item.outboxRowId,
            // Kind validity is checked downstream by KindRouter.dispatch — this
            // module does not filter/validate kind, per its kind-generic contract.
            kind: item.kind as NotificationEvent['kind'],
            envelope: item.envelope,
            source: 'poll',
          });
        } catch (handlerErr) {
          // eslint-disable-next-line no-console
          console.error('[@spaarke/notifications] pollFallback onEvent handler threw:', handlerErr);
        }
      }
    } catch (err) {
      options.onError?.(err);
      currentDelayMs = Math.min(currentDelayMs * 2, maxBackoffMs);
    } finally {
      scheduleNext(currentDelayMs);
    }
  }

  // Fire the first tick immediately (next-load delivery per spec FR-06), then settle into the interval.
  void tick();

  return {
    stop(): void {
      stopped = true;
      if (timer) {
        clearTimeout(timer);
        timer = undefined;
      }
    },
    get isRunning(): boolean {
      return !stopped;
    },
  };
}

function extractItems(data: unknown): PendingNotificationItem[] {
  if (Array.isArray(data)) {
    return data as PendingNotificationItem[];
  }
  if (data && typeof data === 'object' && Array.isArray((data as { items?: unknown }).items)) {
    return (data as { items: PendingNotificationItem[] }).items;
  }
  return [];
}
