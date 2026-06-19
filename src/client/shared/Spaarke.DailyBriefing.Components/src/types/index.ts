/**
 * @spaarke/daily-briefing-components — types barrel
 *
 * Public type exports for the Daily Briefing shared package.
 *
 * Populated by R2 task 015 (FR-07): defines the `BriefingDependencies`
 * abstraction surface used by `DailyBriefingApp` and the future Pattern D
 * SpaarkeAi workspace widget. Every hoisted component/hook accepts its
 * runtime dependencies through this interface (or as discrete props/params
 * for hooks called individually), which keeps the package solution-agnostic
 * per ADR-012.
 *
 * Also re-exports the full `notifications.ts` surface for ergonomic consumer
 * imports (`import type { NotificationItem } from "@spaarke/daily-briefing-components/types"`).
 */

// Re-export the notifications data-model surface so consumers can take a
// single dep on the package's types barrel.
export * from './notifications';

// ---------------------------------------------------------------------------
// BriefingDependencies — runtime dependency-injection interface (FR-07)
// ---------------------------------------------------------------------------

/**
 * `authenticatedFetch` signature compatible with `@spaarke/auth`'s
 * canonical export (`(input: RequestInfo | URL, init?: RequestInit) => Promise<Response>`).
 * Declared structurally so the package does not need to take a hard build-time
 * dependency on `@spaarke/auth` for type-only callers — runtime callers still
 * resolve `@spaarke/auth` per ADR-028.
 */
export type AuthenticatedFetch = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>;

/**
 * Minimal `IWebApi` surface from `./notifications` is the binding shape for
 * webApi-injecting consumers. Re-exported here for ergonomic discovery.
 */
export type { IWebApi } from './notifications';

/**
 * Runtime dependencies consumed by `DailyBriefingApp` and any hook called
 * from a host context (standalone code page, SpaarkeAi widget shell).
 *
 * Per FR-07, every hoisted component/hook accepts abstracted dependencies as
 * props/parameters rather than reaching into solution-local singletons. The
 * concrete consumer wiring lives at the standalone code page (`main.tsx` →
 * `<DailyBriefingApp />` with these props filled by `@spaarke/auth` + Xrm)
 * and at the SpaarkeAi widget shell (Pattern D registration shim — task 016).
 */
export interface BriefingDependencies {
  /**
   * Authenticated fetch wrapper. Canonical implementation is the
   * `authenticatedFetch` export from `@spaarke/auth` (ADR-028 Spaarke Auth v2).
   * Used by `briefingService.ts` for BFF `/summarize` and `/narrate` calls.
   */
  authenticatedFetch: AuthenticatedFetch;
  /**
   * Xrm.WebApi reference (the host-resolved Dataverse client). Standalone
   * code page resolves this via frame-walking + polling (welcome-screen
   * timing); the SpaarkeAi widget receives it from the workspace shell.
   * Used by notification + preference hooks.
   */
  webApi: import('./notifications').IWebApi;
  /**
   * Current user's systemuser GUID (stripped of braces). Used by
   * `useBriefingPreferences` to filter `sprk_userpreference` records.
   */
  userId: string;
  /**
   * Current tenant ID. Reserved for future telemetry / per-tenant feature
   * gating; not strictly required by the current hook surface but threaded
   * through for parity with sibling shared packages.
   */
  tenantId: string;
  /**
   * Optional callback invoked when a downstream BFF call returns 429
   * (rate-limited). Hosts can use this to surface a toast or pause polling.
   */
  onRateLimitError?: (info: { endpoint: string; retryAfterSeconds?: number }) => void;
  /**
   * Optional callback invoked when a user opens a record from a narrative
   * bullet. Hosts can intercept to navigate within their own surface (modal
   * preview, side pane, full-page open). Defaults to `actionUrl` navigation
   * when omitted.
   */
  onRecordOpen?: (info: { entityType: string; entityId: string; entityName?: string }) => void;
}
