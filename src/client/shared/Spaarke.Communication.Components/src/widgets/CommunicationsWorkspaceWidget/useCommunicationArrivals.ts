/**
 * useCommunicationArrivals — the FR-22 client CONSUMER (task 045).
 *
 * Consumes the notification-spine `communication-arrived` kind via an injected `@spaarke/notifications`
 * client and turns each arrival into AWARENESS ONLY: it increments an unread counter and invokes an
 * `onArrival` callback (the widget raises a toast). It NEVER fetches message content from the signal —
 * the spine is not the content channel (NFR-03). Message content keeps loading via the existing ~5s
 * `ConversationView` poll (`pollIntervalMs` default 5000), which this hook does not touch.
 *
 * The notifications client is INJECTED (`createClient`) rather than imported here, so this hook is pure
 * and unit-testable with a fake client — no `@spaarke/notifications`/`@microsoft/signalr` runtime
 * dependency leaks into the awareness logic. The widget supplies the real client factory
 * (`createNotificationsClient`).
 *
 * Live SignalR pushes are signal-only (`event.envelope` is `undefined`; `source: 'live'`); poll-fallback
 * events carry the envelope. This hook uses ONLY `outboxRowId`/`kind`/`source` — never `envelope` content
 * — so awareness is identical on both paths and no content ever rides the awareness path.
 *
 * <b>Register-only (task 045 / Option A):</b> the hook receives a `subscribe` function that registers a
 * `communication-arrived` handler on the HOST's ONE shared `@spaarke/notifications` client and returns an
 * unregister function. The hook NEVER constructs a client and NEVER calls start/stop — the host owns the
 * connection lifecycle (SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE §4: one connection per host). If `subscribe`
 * is `undefined` (host never wired the spine), awareness is simply OFF — no rogue client, no second negotiate.
 * The widget supplies the real `subscribe` from the module-level seam (`communicationArrivalsSeam.ts`), which
 * the host sets once at bootstrap. This mirrors how `useSuggestionCards` receives its `subscribe`.
 */

import * as React from 'react';

/** The single arrival event shape this hook consumes (a structural subset of `@spaarke/notifications`' `NotificationEvent`). */
export interface ArrivalEvent {
  outboxRowId: string;
  kind: string;
  source: 'live' | 'poll';
  /** Present only on poll-fallback events; intentionally UNUSED here (awareness never reads content). */
  envelope?: unknown;
}

/**
 * Register-only subscription: registers a `communication-arrived` handler on the host's shared client and
 * returns an unregister function. Bound by the host to `getNotificationsClient().registerHandler(...)`; the
 * hook never sees the client itself, so it cannot start/stop the shared connection.
 */
export type ArrivalSubscribe = (onArrival: (event: ArrivalEvent) => void) => () => void;

export interface UseCommunicationArrivalsOptions {
  /**
   * Host-provided register-only subscription (from the `communicationArrivalsSeam`). `undefined` → the host
   * never wired the spine, so awareness stays OFF and NO client is constructed (one-connection invariant).
   */
  subscribe?: ArrivalSubscribe;
  /**
   * Invoked once per consumed `communication-arrived` event (the widget raises a toast). Receives the
   * signal-only event — callers MUST NOT treat it as content (NFR-03).
   */
  onArrival?: (event: ArrivalEvent) => void;
}

export interface UseCommunicationArrivalsResult {
  /** Count of unseen arrivals since mount / last {@link reset}. Drives the unread badge. */
  unreadCount: number;
  /** Clears the unread counter (e.g. when the user acknowledges the badge). */
  reset: () => void;
}

/**
 * Subscribes to `communication-arrived` for the lifetime of the mounting component and exposes an
 * unread-arrival counter. Awareness only — see the module header.
 */
export function useCommunicationArrivals(options: UseCommunicationArrivalsOptions): UseCommunicationArrivalsResult {
  const { subscribe, onArrival } = options;

  const [unreadCount, setUnreadCount] = React.useState(0);

  // Keep the latest onArrival without re-subscribing on every render.
  const onArrivalRef = React.useRef(onArrival);
  React.useEffect(() => {
    onArrivalRef.current = onArrival;
  }, [onArrival]);

  // `subscribe` is captured once at mount; consumers pass a stable reference (the module-level seam getter's
  // result, or a test fake). A null/undefined seam means the host never wired the spine → awareness OFF.
  const subscribeRef = React.useRef(subscribe);

  // Defence-in-depth vs issue #688: the poll-fallback path can re-deliver a still-pending row (the pending
  // endpoint returns undismissed rows every tick). The @spaarke/notifications client dedups at its dispatch
  // boundary, but this consumer guards independently so a duplicate outboxRowId can never re-toast or
  // double-count the unread badge, regardless of host client wiring.
  const seenRowIdsRef = React.useRef<Set<string>>(new Set());

  React.useEffect(() => {
    const doSubscribe = subscribeRef.current;
    if (!doSubscribe) {
      // Host did not wire the notification spine — awareness is disabled. We NEVER construct our own client
      // here: a second client would open a second SignalR connection + negotiate (one-connection invariant,
      // ADR-047 / SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE §4).
      return;
    }

    let unregister: (() => void) | undefined;
    try {
      // Register ONLY — the host owns the connection lifecycle (start/stop). Unmount just unregisters this
      // consumer; it never stops the shared connection (which would break every other consumer, e.g. suggestions).
      unregister = doSubscribe(event => {
        // Duplicate delivery of the same outbox row (poll-fallback re-tick, live-then-poll overlap) is
        // suppressed — one toast + one badge increment per row (issue #688).
        if (seenRowIdsRef.current.has(event.outboxRowId)) {
          return;
        }
        seenRowIdsRef.current.add(event.outboxRowId);
        // AWARENESS ONLY (NFR-03): bump the unread counter + notify the host (toast). NEVER fetch content here.
        setUnreadCount(n => n + 1);
        onArrivalRef.current?.(event);
      });
    } catch (err) {
      // Defensive: a synchronous failure registering the handler must not crash the widget.
      // eslint-disable-next-line no-console
      console.warn('[communications] notification-spine consumer failed to register (awareness disabled):', err);
    }

    return () => {
      try {
        unregister?.();
      } catch {
        /* no-op */
      }
    };
  }, []);

  const reset = React.useCallback(() => setUnreadCount(0), []);

  return { unreadCount, reset };
}
