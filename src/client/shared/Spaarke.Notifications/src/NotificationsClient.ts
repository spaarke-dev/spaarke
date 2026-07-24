import type { HubConnection } from '@microsoft/signalr';
import { connectSignalR } from './negotiate';
import { KindRouter, type NotificationHandler } from './kindRouter';
import { startPollFallback, type PollFallbackHandle } from './pollFallback';
import type { NotificationEvent, NotificationKind } from './types';

export type NotificationsConnectionState = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'polling' | 'stopped';

export interface NotificationsClientOptions {
  /** Base poll interval used once the client falls back to polling. Default 30s (see `pollFallback.ts`). */
  pollIntervalMs?: number;
  /** Poll backoff ceiling after consecutive failures. Default 5 min. */
  pollMaxBackoffMs?: number;
  /** Invoked whenever the connection state transitions (for host UI — e.g. a degraded-state badge). */
  onConnectionStateChange?: (state: NotificationsConnectionState) => void;
}

/**
 * The public entrypoint for `@spaarke/notifications` (spec FR-05). Ties together
 * negotiate/connect (`negotiate.ts`), kind-routing (`kindRouter.ts`), and the
 * poll-fallback degrade path (`pollFallback.ts`) into one host-agnostic client.
 *
 * Usage (see README.md for the full contract):
 *
 * ```ts
 * const client = new NotificationsClient();
 * client.registerHandler('communication-arrived', (event) => { ... });
 * await client.start(); // throws on negotiate/connect failure — see start() docs
 * // ...
 * await client.stop();
 * ```
 */
export class NotificationsClient {
  private readonly router = new KindRouter();
  private readonly options: NotificationsClientOptions;
  private connection: HubConnection | undefined;
  private pollHandle: PollFallbackHandle | undefined;
  private state: NotificationsConnectionState = 'idle';
  /**
   * outboxRowIds already dispatched by THIS client instance (issue #688). The pending
   * endpoint returns every still-undismissed row on every poll tick (the poll never marks
   * rows delivered), and the poll loop restarts on each live→poll transition — so without
   * client-lifetime dedup the same row re-fires its handlers every ~30s (repeating toasts).
   * Keyed here at the dispatch boundary, not inside the poll loop, so it also suppresses
   * the live-then-poll duplicate (a row pushed live but still pending server-side) and
   * survives poll restarts. Cleared on stop(): a restarted client re-delivers still-pending
   * rows once, which is the intended next-load semantic (FR-06).
   */
  private seenOutboxRowIds = new Set<string>();
  /**
   * True while an intentional `stop()` is in flight. Guards the `onclose` handler below:
   * `HubConnection.stop()` itself triggers `onclose` internally, and without this guard
   * that would re-enter `startPolling()` right as the client is shutting down — leaking a
   * poll loop that runs forever after `stop()` has already returned.
   */
  private stopping = false;

  constructor(options: NotificationsClientOptions = {}) {
    this.options = options;
  }

  /** Registers `callback` for `kind`. See `KindRouter.registerHandler`. Returns an unregister function. */
  registerHandler(kind: NotificationKind, callback: NotificationHandler): () => void {
    return this.router.registerHandler(kind, callback);
  }

  get connectionState(): NotificationsConnectionState {
    return this.state;
  }

  /**
   * Negotiates + opens the live SignalR connection. On success, subsequent
   * disconnects/reconnect-exhaustion automatically switch to poll-fallback and
   * switch back once the live connection recovers.
   *
   * On FAILURE (negotiate rejected, connect failed, SignalR disabled
   * server-side), this method:
   *   1. Starts poll-fallback immediately, so signals are never silently
   *      dropped while the live connection is unavailable (NFR-04).
   *   2. Re-throws the original error as a typed error/rejected promise, so
   *      the caller (host) can render a degraded-connection state rather than
   *      being misled into thinking a live channel exists.
   */
  async start(): Promise<void> {
    this.stopping = false;
    this.setState('connecting');

    try {
      const connection = await connectSignalR(signal => {
        this.dispatchOnce({
          outboxRowId: signal.outboxRowId,
          // Kind validity is checked downstream by KindRouter.dispatch (same contract as pollFallback).
          kind: signal.kind as NotificationEvent['kind'],
          envelope: undefined, // live push is signal-only (NFR-02/03) — no envelope on the wire
          source: 'live',
        });
      });

      this.connection = connection;
      this.setState('connected');
      this.stopPolling();

      connection.onreconnecting(() => {
        this.setState('reconnecting');
        this.startPolling();
      });
      connection.onreconnected(() => {
        this.setState('connected');
        this.stopPolling();
      });
      connection.onclose(() => {
        if (this.stopping) {
          // Caused by our OWN connection.stop() call inside stop() below — that method
          // owns the final 'stopped' state transition; do not re-enter polling here.
          return;
        }
        // withAutomaticReconnect exhausted its retry policy — this is the
        // durable "live connection unavailable" state until start() is called again.
        this.setState('polling');
        this.startPolling();
      });
    } catch (err) {
      this.setState('polling');
      this.startPolling();
      throw err;
    }
  }

  /** Stops polling (if active), closes the live connection (if any), and clears all registered handlers. */
  async stop(): Promise<void> {
    this.stopping = true;
    this.stopPolling();
    if (this.connection) {
      await this.connection.stop();
      this.connection = undefined;
    }
    // Defense in depth: stopPolling() again in case the onclose guard above was somehow
    // bypassed (e.g. a future SignalR SDK change reorders callback timing).
    this.stopPolling();
    this.router.clear();
    this.seenOutboxRowIds = new Set<string>();
    this.setState('stopped');
  }

  /** Routes an event to handlers at most ONCE per outboxRowId per client lifetime (issue #688). */
  private dispatchOnce(event: NotificationEvent): void {
    if (this.seenOutboxRowIds.has(event.outboxRowId)) {
      return;
    }
    this.seenOutboxRowIds.add(event.outboxRowId);
    this.router.dispatch(event);
  }

  private startPolling(): void {
    if (this.pollHandle) {
      return;
    }
    this.pollHandle = startPollFallback({
      intervalMs: this.options.pollIntervalMs,
      maxBackoffMs: this.options.pollMaxBackoffMs,
      onEvent: event => this.dispatchOnce(event),
      onError: err => {
        // eslint-disable-next-line no-console
        console.warn('[@spaarke/notifications] Poll-fallback tick failed (non-fatal; will retry with backoff):', err);
      },
    });
  }

  private stopPolling(): void {
    this.pollHandle?.stop();
    this.pollHandle = undefined;
  }

  private setState(next: NotificationsConnectionState): void {
    this.state = next;
    this.options.onConnectionStateChange?.(next);
  }
}
