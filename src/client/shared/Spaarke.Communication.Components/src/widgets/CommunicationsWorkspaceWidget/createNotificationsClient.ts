/**
 * Default `@spaarke/notifications` client factory for the CommunicationsWorkspaceWidget (task 045 / FR-22).
 *
 * Isolated in its own module so the ONLY `@spaarke/notifications` import in this package lives here — the
 * awareness hook (`useCommunicationArrivals`) depends on a local structural interface and stays free of any
 * spine-client/`@microsoft/signalr` coupling, keeping it trivially unit-testable with a fake.
 *
 * Precondition (README of `@spaarke/notifications`): the HOST app (SpaarkeAi / LegalWorkspace) has already
 * called `@spaarke/auth`'s `initAuth(...)` at its root before any widget renders — this widget only ever
 * mounts inside such a host, mirroring its existing direct `authenticatedFetch` import.
 */

import { NotificationsClient } from '@spaarke/notifications';
import type { ArrivalNotificationsClient } from './useCommunicationArrivals';

/**
 * Constructs the real host-agnostic notification-spine client. The concrete `NotificationsClient` structurally
 * satisfies {@link ArrivalNotificationsClient} (registerHandler/start/stop); the cast pins the narrow surface
 * this widget uses without importing the full public type here.
 */
export const createNotificationsClient = (): ArrivalNotificationsClient =>
  new NotificationsClient() as unknown as ArrivalNotificationsClient;
