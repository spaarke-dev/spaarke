/**
 * dailyBriefing.registration.ts — LegalWorkspace section-registration thin shim.
 *
 * Pattern D dual-use per Calendar (`calendar.registration.ts`, task 115) and
 * Smart Todo precedent (ADR-012). This shim delegates to the shared
 * `createDailyBriefingRegistration` factory in `@spaarke/ui-components` and
 * closes over LegalWorkspace-local `authenticatedFetch` + `trackEvent`
 * telemetry — preserving FR-25 / NFR-10.
 *
 * Pre-R2: STATIC `SectionRegistration` const that lost the factory's
 * `loadNotificationContext` option entirely (see `notes/task-002-blocker.md`).
 * Post-R2: thin shim that (1) re-exports a default static registration for
 * `sectionRegistry.ts` (standalone — loader omitted; empty-payload contract)
 * and (2) exposes a factory so SpaarkeAi `main.tsx` (task 002) can supply
 * `loadSpaarkeAiNotificationContext`. This is the P1 seam this task activates
 * (FR-01 / FR-02).
 *
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 tokens),
 *            ADR-022 (React 19), ADR-028 (function-based auth contract).
 */

import {
  createDailyBriefingRegistration,
  TELEMETRY_EVENT_DAILY_BRIEFING_429,
} from "@spaarke/ui-components";
import type { SectionRegistration, NarrateRequest } from "@spaarke/ui-components";
import { authenticatedFetch, getTenantId } from "../../services/authInit";
import { trackEvent } from "../../services/telemetry";

/**
 * Options for `createLegalWorkspaceDailyBriefingRegistration`.
 *
 * `loadNotificationContext` is the P1 seam (FR-01 / FR-02): supplied by
 * SpaarkeAi to flow real notification context into the BFF `/narrate`
 * envelope so embedded Daily Briefing renders real bullets on cold load.
 * Omitted by standalone LegalWorkspace → empty-payload contract preserved.
 */
export interface CreateLegalWorkspaceDailyBriefingRegistrationOptions {
  loadNotificationContext?: () => Promise<NarrateRequest | null>;
}

/** Route 429 telemetry through LegalWorkspace's App Insights helper. */
function routeRateLimitTelemetry(properties: Record<string, unknown>): void {
  const stringProps: Record<string, string> = {};
  for (const [k, v] of Object.entries(properties)) {
    stringProps[k] = String(v);
  }
  trackEvent(TELEMETRY_EVENT_DAILY_BRIEFING_429, stringProps);
}

// ---------------------------------------------------------------------------
// Module-mutable notification-loader slot (R2 task 002 — FR-01 / FR-02).
//
// Why a module slot?
//   `sectionRegistry.ts` consumes `dailyBriefingRegistration` as a STATIC
//   `readonly` array entry built at module-load time. The default factory
//   below runs ONCE at module-load with no loader. SpaarkeAi's `main.tsx`
//   needs to inject `loadSpaarkeAiNotificationContext` AFTER bootstrap
//   without rewriting `sectionRegistry.ts` (FR-25 / NFR-10 — standalone
//   LegalWorkspace bundle byte-identical).
//
// Pattern (mirrors `setDefaultWorkspaceRenderer` from @spaarke/ui-components):
//   - SpaarkeAi `main.tsx` calls `setLegalWorkspaceDailyBriefingNotificationLoader(loader)`
//     before React renders.
//   - The default registration's factory forwards a STABLE function reference
//     to the shared factory that reads `_globalNotificationLoader` at
//     call-time (per render fetch). The shared factory's closure captures
//     this reference once, but the function itself is a thin lookup against
//     the mutable slot — so the loader is "late-bound" at fetch time.
//
// Standalone LegalWorkspace + standalone Daily Briefing Code Page do NOT
// call the setter — the slot stays `null`, the wrapper returns `null`,
// `useDailyBriefing` falls back to the empty-payload contract (BFF returns
// empty bullets → empty-state UI). FR-25 / NFR-10 preserved.
// ---------------------------------------------------------------------------

let _globalNotificationLoader:
  | (() => Promise<NarrateRequest | null>)
  | null = null;

/**
 * Set the global notification-context loader for the DEFAULT Daily Briefing
 * registration consumed by `sectionRegistry.ts`. Call this BEFORE rendering
 * any tree that mounts `WorkspaceGrid` so the first fetch picks up the loader.
 *
 * Used by SpaarkeAi `main.tsx` to inject `loadSpaarkeAiNotificationContext`
 * (R2 task 002 — FR-02). Standalone LegalWorkspace does NOT call this.
 */
export function setLegalWorkspaceDailyBriefingNotificationLoader(
  loader: (() => Promise<NarrateRequest | null>) | null,
): void {
  _globalNotificationLoader = loader;
}

/**
 * Late-bound loader passed to the shared factory. The shared `useDailyBriefing`
 * hook calls `loadNotificationContext()` at fetch time — by then the SpaarkeAi
 * bootstrap has already set `_globalNotificationLoader`. Returns `null` when
 * no loader is configured (preserves empty-payload contract for standalone).
 */
function lateBoundNotificationLoader(): Promise<NarrateRequest | null> {
  return _globalNotificationLoader ? _globalNotificationLoader() : Promise.resolve(null);
}

/**
 * Create a Daily Briefing `SectionRegistration` bound to LegalWorkspace-local
 * auth + telemetry, with optional consumer-supplied notification loader.
 */
export function createLegalWorkspaceDailyBriefingRegistration(
  options: CreateLegalWorkspaceDailyBriefingRegistrationOptions = {},
): SectionRegistration {
  return createDailyBriefingRegistration({
    authenticatedFetch,
    // tenantId resolved lazily inside the shared `useDailyBriefing` hook
    // (anonymous cache-key fallback when undefined — acceptable per ADR-014).
    tenantId: undefined,
    onRateLimitError: routeRateLimitTelemetry,
    loadNotificationContext: options.loadNotificationContext,
  });
}

/**
 * Default LegalWorkspace registration: standalone path uses a late-bound
 * loader that reads from the module-mutable slot above. When the slot is
 * unset (standalone LegalWorkspace + standalone Daily Briefing), the wrapper
 * returns `null` → empty-payload contract preserved. When SpaarkeAi sets
 * the slot at bootstrap, the wrapper forwards to the supplied loader →
 * Daily Briefing renders real bullets on cold load (FR-02).
 *
 * Consumed by `sectionRegistry.ts` unchanged.
 */
export const dailyBriefingRegistration: SectionRegistration =
  createLegalWorkspaceDailyBriefingRegistration({
    loadNotificationContext: lateBoundNotificationLoader,
  });

export default dailyBriefingRegistration;

// Re-export `getTenantId` for downstream consumers / tests.
export { getTenantId };
