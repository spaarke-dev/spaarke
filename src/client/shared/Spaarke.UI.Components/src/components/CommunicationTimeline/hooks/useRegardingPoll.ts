/**
 * useRegardingPoll.ts
 *
 * Poll loop driving regarding mode's `<CommunicationTimeline mode="regarding" />`
 * (R2 task 020, FR-03). Mirrors `useThreadPoll.ts`'s cadence/visibility/
 * overlap-guard idiom (per the task's design-decision note: "reuse the R1
 * useThreadPoll cadence pattern if polling the grouped view"), adapted for
 * the `by-regarding` endpoint's shape:
 *   - No `since` cursor — the endpoint has no incremental mode; every tick
 *     re-fetches the full regarding-record snapshot (task 010 has no
 *     `since`/`top` params on `by-regarding`, unlike the per-thread read).
 *   - No unread count — unread state is a thread-id-mode concept (a single
 *     thread's last-seen watermark); the grouped, multi-thread view has no
 *     equivalent surface in this task.
 *
 * Requirements carried over from `useThreadPoll`:
 *   - Configurable interval, default 5000ms (NFR-07).
 *   - Pauses while `document.hidden` (Page Visibility API).
 *   - Resumes promptly on focus/visibility-regain.
 *   - Guards overlapping in-flight polls.
 *   - Cleans up the interval + listeners on unmount.
 *
 * React-16.14-safe (ADR-022 — `Spaarke.UI.Components` ships into PCF): only
 * `useEffect`/`useCallback`/`useRef`, no React-18-only APIs.
 */
import * as React from 'react';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import { readByRegarding } from '../../../services/communicationTimelineApi';
import { mapRegardingReadResultToGroups } from '../CommunicationTimeline.buildTimeline';
import type { RegardingThreadGroup } from '../CommunicationTimeline.types';

export interface UseRegardingPollOptions {
  entityType: string;
  id: string;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl?: string;
  /** Default 5000ms (NFR-07). Prop-overridable per `CommunicationTimelineRegardingModeProps.pollIntervalMs`. */
  pollIntervalMs?: number;
  onLoading: () => void;
  onGroups: (groups: RegardingThreadGroup[]) => void;
  onError?: (error: unknown) => void;
  /** Set false to suspend polling entirely (e.g. missing entityType/id). Default true. */
  enabled?: boolean;
}

export interface UseRegardingPollResult {
  /** Triggers an out-of-band re-fetch immediately. */
  pollNow: () => void;
}

export function useRegardingPoll(options: UseRegardingPollOptions): UseRegardingPollResult {
  const {
    entityType,
    id,
    authenticatedFetch,
    bffBaseUrl,
    pollIntervalMs = 5000,
    onLoading,
    onGroups,
    onError,
    enabled = true,
  } = options;

  const inFlightRef = React.useRef(false);

  const poll = React.useCallback(async () => {
    if (inFlightRef.current) return; // guard overlapping in-flight polls
    if (typeof document !== 'undefined' && document.hidden) return; // pause while tab hidden

    inFlightRef.current = true;
    onLoading();
    try {
      const result = await readByRegarding(entityType, id, { authenticatedFetch, bffBaseUrl });
      onGroups(mapRegardingReadResultToGroups(result));
    } catch (err) {
      onError?.(err);
    } finally {
      inFlightRef.current = false;
    }
  }, [entityType, id, authenticatedFetch, bffBaseUrl, onLoading, onGroups, onError]);

  React.useEffect(() => {
    if (!enabled || !entityType || !id) return undefined;

    let cancelled = false;

    void poll();

    const intervalId = setInterval(() => {
      if (!cancelled) void poll();
    }, pollIntervalMs);

    const handleVisibilityOrFocus = () => {
      if (!cancelled && typeof document !== 'undefined' && !document.hidden) {
        void poll(); // resume promptly rather than waiting for the next tick
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
  }, [enabled, entityType, id, pollIntervalMs, poll]);

  const pollNow = React.useCallback(() => {
    void poll();
  }, [poll]);

  return { pollNow };
}
