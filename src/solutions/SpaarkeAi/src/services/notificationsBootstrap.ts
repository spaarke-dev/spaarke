/**
 * SpaarkeAi's FIRST consumer wiring for `@spaarke/notifications` (task 021, spec
 * FR-05). This is a THIN integration proving negotiate → connect → kind-routed
 * callback fires — it deliberately does NOT render a suggestion/communication UI
 * (that is task 051's job, the suggestion renderer branch). Handlers here only
 * log, so the wiring's correctness is independently observable (console +
 * `connectionState`) without depending on any not-yet-built renderer.
 *
 * Scope boundary (per task 021 POML step 7): "Wire a first consumer into the
 * SpaarkeAi workspace ONLY as a thin integration ... do not build the full
 * suggestion renderer UI." When task 051 lands, it should register its own
 * handlers via `getNotificationsClient().registerHandler(...)` rather than
 * duplicating this bootstrap.
 *
 * Non-fatal by design (mirrors the AppInsights init pattern in `main.tsx`):
 * a notifications-spine failure (SignalR unreachable, negotiate 401, etc.)
 * must never block SpaarkeAi's own bootstrap — `initNotificationsClient()` is
 * called fire-and-forget with all errors caught + logged.
 */

import {
  NotificationsClient,
  type NotificationsConnectionState,
} from "@spaarke/notifications";

let _client: NotificationsClient | null = null;

/**
 * Returns the process-wide `NotificationsClient` singleton, constructing it
 * (but NOT starting it) on first call. Later consumers (e.g. task 051's
 * suggestion renderer) should call this to register additional handlers
 * rather than constructing their own client — one client per host, per the
 * package's host-agnostic single-connection contract.
 */
export function getNotificationsClient(): NotificationsClient {
  if (!_client) {
    _client = new NotificationsClient({
      onConnectionStateChange: (state: NotificationsConnectionState) => {
        console.info(`[SpaarkeAi] Notifications connection state: ${state}`);
      },
    });
  }
  return _client;
}

/**
 * Registers the proof-of-wiring handlers for the three ACTIVE kinds and
 * starts the client. Call once, after auth is initialized (`ensureAuthInitialized`
 * in `main.tsx`'s bootstrap) so the negotiate call has a valid token to send.
 *
 * Non-fatal: on failure, `NotificationsClient.start()` has already activated
 * poll-fallback internally (NFR-04) before rejecting — this function only
 * logs the rejection so a degraded notifications connection never blocks the
 * rest of SpaarkeAi's bootstrap.
 */
export async function initNotificationsClient(): Promise<void> {
  const client = getNotificationsClient();

  client.registerHandler("communication-arrived", (event) => {
    console.info("[SpaarkeAi] communication-arrived:", event.outboxRowId, `(source=${event.source})`);
  });
  client.registerHandler("communication-assessed", (event) => {
    console.info("[SpaarkeAi] communication-assessed:", event.outboxRowId, `(source=${event.source})`);
  });
  client.registerHandler("suggestion", (event) => {
    console.info("[SpaarkeAi] suggestion:", event.outboxRowId, `(source=${event.source})`);
  });

  try {
    await client.start();
  } catch (err) {
    console.warn(
      "[SpaarkeAi] Notifications client failed to connect (poll-fallback is active in the background):",
      err instanceof Error ? err.message : String(err),
    );
  }
}
