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

/** The minimal client surface this hook needs — structurally satisfied by `@spaarke/notifications`' `NotificationsClient`. */
export interface ArrivalNotificationsClient {
  registerHandler(kind: 'communication-arrived', callback: (event: ArrivalEvent) => void): () => void;
  start(): Promise<void>;
  stop(): Promise<void>;
}

export interface UseCommunicationArrivalsOptions {
  /** Factory for the notifications client. Injected so the hook is testable without the real spine client. */
  createClient: () => ArrivalNotificationsClient;
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
  const { createClient, onArrival } = options;

  const [unreadCount, setUnreadCount] = React.useState(0);

  // Keep the latest onArrival without re-subscribing the client on every render.
  const onArrivalRef = React.useRef(onArrival);
  React.useEffect(() => {
    onArrivalRef.current = onArrival;
  }, [onArrival]);

  // `createClient` is captured once at mount; consumers pass a stable factory (module-level function).
  const createClientRef = React.useRef(createClient);

  React.useEffect(() => {
    let unregister: (() => void) | undefined;
    let client: ArrivalNotificationsClient | undefined;

    try {
      client = createClientRef.current();

      // Register BEFORE start() so no early signal is missed (README contract).
      unregister = client.registerHandler('communication-arrived', event => {
        // AWARENESS ONLY (NFR-03): bump the unread counter + notify the host. NEVER fetch content here.
        setUnreadCount(n => n + 1);
        onArrivalRef.current?.(event);
      });

      // start() rejects on live-connect failure, but poll-fallback has ALREADY started inside the client
      // (NFR-04 degrade) — so awareness still works. Swallow the rejection; do not surface a hard error.
      void client.start().catch((err: unknown) => {
        // eslint-disable-next-line no-console
        console.warn(
          '[communications] notification-spine live connect unavailable; awareness continues via poll-fallback:',
          err
        );
      });
    } catch (err) {
      // Defensive: a synchronous failure constructing/registering the client must not crash the widget.
      // eslint-disable-next-line no-console
      console.warn('[communications] notification-spine consumer failed to initialize (awareness disabled):', err);
    }

    return () => {
      try {
        unregister?.();
      } catch {
        /* no-op */
      }
      void client?.stop().catch(() => {
        /* best-effort teardown */
      });
    };
  }, []);

  const reset = React.useCallback(() => setUnreadCount(0), []);

  return { unreadCount, reset };
}
