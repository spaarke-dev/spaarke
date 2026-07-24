/**
 * Module-level injection seam for FR-22 communication-arrival awareness (task 045).
 *
 * WHY A SEAM (not a prop, not a `new NotificationsClient()`):
 * `CommunicationsWorkspaceWidget` is rendered by the generic `WorkspaceWidgetRegistry`
 * (`@spaarke/ai-widgets`, type string `communications-list`), which hands every widget the
 * SAME `WorkspaceWidgetProps` shape — there is no per-widget prop channel to pass the host's
 * notification client in. So the HOST wires awareness ONCE at bootstrap by calling
 * {@link setCommunicationArrivalsSubscribe} with a register-only subscription bound to its ONE
 * shared `@spaarke/notifications` client:
 *
 * ```ts
 * // host bootstrap (e.g. SpaarkeAi notificationsBootstrap.initNotificationsClient)
 * setCommunicationArrivalsSubscribe(
 *   (onArrival) => getNotificationsClient().registerHandler('communication-arrived', onArrival),
 * );
 * ```
 *
 * The widget READS the seam (never sets it). If the host never wired it, the seam is `undefined`
 * and awareness is simply OFF — the widget NEVER constructs its own client. Constructing one would
 * open a SECOND SignalR connection + a second `negotiate`, violating the spine's one-connection
 * invariant (ADR-047 / SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE §4 "Do NOT open a second SignalR
 * connection or a second negotiate"). The host owns the connection LIFECYCLE (start/stop);
 * consumers only register/unregister — mirroring the existing host-owned-singleton pattern
 * (`getNotificationsClient()` `let _client`) and how `useSuggestionCards` receives its `subscribe`.
 */

import type { ArrivalEvent, ArrivalSubscribe } from './useCommunicationArrivals';

let _subscribe: ArrivalSubscribe | undefined;

/**
 * HOST wiring (called ONCE at app bootstrap, bound to the host's shared notification client).
 * Pass `undefined` to clear (e.g. teardown/tests). Register-only: the returned unregister function
 * detaches this consumer WITHOUT stopping the shared connection.
 */
export function setCommunicationArrivalsSubscribe(subscribe: ArrivalSubscribe | undefined): void {
  _subscribe = subscribe;
}

/**
 * The widget reads this to obtain the host's register-only subscription. `undefined` means the host
 * never wired the spine — awareness is disabled (no rogue client is ever constructed as a fallback).
 */
export function getCommunicationArrivalsSubscribe(): ArrivalSubscribe | undefined {
  return _subscribe;
}

export type { ArrivalEvent, ArrivalSubscribe };
