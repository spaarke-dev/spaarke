/**
 * Jest runtime stub for `@spaarke/notifications` (mapped in `jest.config.cjs` moduleNameMapper).
 *
 * The real package pulls `@microsoft/signalr` (a peer dependency not installed in THIS package's
 * `node_modules`), so mounting `CommunicationsWorkspaceWidget` in jsdom would fail to resolve it. This
 * stub provides a no-op `NotificationsClient` so the widget mounts; tests that need to DRIVE arrivals
 * inject their own fake client into `useCommunicationArrivals` instead (see
 * `useCommunicationArrivals.test.tsx`) rather than relying on this stub.
 *
 * Mirrors the `src/__mocks__/{d3-force,marked,sdap-client}.ts` transitive-stub pattern already used by
 * this package's jest config for the `@spaarke/ui-components` barrel.
 */

export class NotificationsClient {
  registerHandler(): () => void {
    return () => {
      /* no-op */
    };
  }

  async start(): Promise<void> {
    /* no-op — no live connection in tests */
  }

  async stop(): Promise<void> {
    /* no-op */
  }
}
