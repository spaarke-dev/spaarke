/**
 * Minimal Jest mock for the `@spaarke/notifications` package (task 021).
 *
 * The real package's built `dist` is ESM (`export ...`), which Jest does not
 * transform (it lives under node_modules, outside `transformIgnorePatterns`).
 * Before task 051, `@spaarke/notifications` was imported only by
 * `notificationsBootstrap.ts` — a module `main.tsx` loads at runtime but Jest
 * never touches. Task 051 wires `getNotificationsClient()` into
 * `ConversationPane.tsx`, pulling the package into every ConversationPane test's
 * module graph, where the untransformed ESM throws `Unexpected token 'export'`.
 *
 * The SpaarkeAi unit tests do NOT exercise the live SignalR/negotiate path — the
 * suggestion-renderer tests (`SuggestionCard.test.tsx`) inject their own
 * `subscribe`/`fetchPending` doubles. This stub only needs to let the bootstrap +
 * ConversationPane import chain RESOLVE so the sibling ConversationPane suites keep
 * passing. It provides a no-op `NotificationsClient` whose `registerHandler` returns
 * an unregister function (the one surface the ConversationPane wiring calls).
 *
 * Mirrors the existing `@spaarke/auth` / `@spaarke/sdap-client` jest-mock pattern.
 *
 * @see jest.config.ts moduleNameMapper — wires this file to `@spaarke/notifications`
 */

export type NotificationsConnectionState =
  | 'idle'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'polling'
  | 'stopped';

export interface NotificationsClientOptions {
  pollIntervalMs?: number;
  pollMaxBackoffMs?: number;
  onConnectionStateChange?: (state: NotificationsConnectionState) => void;
}

/** No-op stand-in for the real SignalR-backed client. */
export class NotificationsClient {
  constructor(_options: NotificationsClientOptions = {}) {
    /* no-op */
  }

  registerHandler(_kind: string, _callback: (event: unknown) => void): () => void {
    return () => {
      /* no-op unregister */
    };
  }

  get connectionState(): NotificationsConnectionState {
    return 'idle';
  }

  start(): Promise<void> {
    return Promise.resolve();
  }

  stop(): Promise<void> {
    return Promise.resolve();
  }
}
