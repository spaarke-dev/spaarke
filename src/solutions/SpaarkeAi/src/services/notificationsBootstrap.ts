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
// FR-22 (messaging-communication-app-r3 task 045): the CommunicationsWorkspaceWidget consumes
// communication-arrived awareness via THIS one shared client — the host wires the register-only seam
// so the widget never constructs its own client (one-connection invariant, ADR-047).
import { setCommunicationArrivalsSubscribe } from "@spaarke/communication-components";

/**
 * Minimal structural shape of the fields these log-only handlers read. Declared inline (not imported
 * from `@spaarke/notifications`) because the package's built `dist` type declarations lag its source
 * for some event kinds — a value+type mixed import breaks the surface tsc gate. A superset of the
 * real `NotificationEvent`, so any real event is assignable to these handlers.
 */
type NotificationEventLite = { readonly outboxRowId: string; readonly source: string };

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

  client.registerHandler("communication-arrived", (event: NotificationEventLite) => {
    console.info("[SpaarkeAi] communication-arrived:", event.outboxRowId, `(source=${event.source})`);
  });
  client.registerHandler("communication-assessed", (event: NotificationEventLite) => {
    console.info("[SpaarkeAi] communication-assessed:", event.outboxRowId, `(source=${event.source})`);
  });
  client.registerHandler("suggestion", (event: NotificationEventLite) => {
    console.info("[SpaarkeAi] suggestion:", event.outboxRowId, `(source=${event.source})`);
  });

  // FR-22 (task 045): hand the CommunicationsWorkspaceWidget a register-only subscription bound to THIS
  // shared client, so its unread badge + toast ride the SAME connection + negotiate as every other consumer
  // (one-connection invariant). The widget reads this seam; it never constructs its own NotificationsClient.
  setCommunicationArrivalsSubscribe(onArrival =>
    client.registerHandler("communication-arrived", onArrival),
  );

  try {
    await client.start();
  } catch (err) {
    console.warn(
      "[SpaarkeAi] Notifications client failed to connect (poll-fallback is active in the background):",
      err instanceof Error ? err.message : String(err),
    );
  }
}
