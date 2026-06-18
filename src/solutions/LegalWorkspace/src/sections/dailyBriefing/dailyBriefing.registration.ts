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
 * Default LegalWorkspace registration: standalone path with NO notification
 * loader (empty-payload contract; preserves pre-R2 behavior). Consumed by
 * `sectionRegistry.ts` unchanged.
 */
export const dailyBriefingRegistration: SectionRegistration =
  createLegalWorkspaceDailyBriefingRegistration();

export default dailyBriefingRegistration;

// Re-export `getTenantId` for downstream consumers / tests.
export { getTenantId };
