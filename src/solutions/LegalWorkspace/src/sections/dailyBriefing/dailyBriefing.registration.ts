/**
 * dailyBriefing.registration.ts — LegalWorkspace section-registration thin shim.
 *
 * Pattern D dual-use per Calendar (`calendar.registration.ts`, task 115) and
 * Smart Todo precedent (ADR-012). This shim delegates to the shared
 * `createDailyBriefingRegistration` factory in `@spaarke/ui-components` and
 * closes over LegalWorkspace-local `authenticatedFetch` + `trackEvent`
 * telemetry — preserving FR-25 / NFR-10.
 *
 * # History of the customization seam
 *
 * Pre-R2: STATIC `SectionRegistration` const that lost the factory's
 *   `loadNotificationContext` option entirely (see `notes/task-002-blocker.md`).
 *
 * Pre-Option D (R2 task 002 / Wave 8): module-mutable slot pattern. The default
 *   registration wrapped a late-bound loader that read from a module-level
 *   `_globalNotificationLoader`; SpaarkeAi `main.tsx` mutated the slot at
 *   bootstrap via `setLegalWorkspaceDailyBriefingNotificationLoader`. That was
 *   a band-aid that did not scale beyond one widget.
 *
 * Post-Option D (R2, 2026-06-18): the slot is gone. Per-host customization
 *   happens via
 *     `createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } })`
 *   in `sectionRegistry.ts` — the registry-as-composition factory pattern. See
 *   `projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md`.
 *   This file now exposes only the per-widget factory + a no-loader default
 *   registration for standalone consumers.
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
 * SpaarkeAi (via `createLegalWorkspaceSectionRegistry`) to flow real
 * notification context into the BFF `/narrate` envelope so embedded Daily
 * Briefing renders real bullets on cold load. Omitted by standalone
 * LegalWorkspace → empty-payload contract preserved.
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
 * Default LegalWorkspace Daily Briefing registration: no loader supplied →
 * the standalone empty-payload contract is preserved (BFF `/narrate` returns
 * empty bullets → empty-state UI). FR-25 / NFR-10.
 *
 * Embedding consumers (SpaarkeAi) do NOT use this const directly — they build
 * a custom registry via
 * `createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } })`
 * which calls `createLegalWorkspaceDailyBriefingRegistration(options.dailyBriefing)`
 * with their loader. See `sectionRegistry.ts` and Option D design rationale.
 */
export const dailyBriefingRegistration: SectionRegistration =
  createLegalWorkspaceDailyBriefingRegistration();

export default dailyBriefingRegistration;

// Re-export `getTenantId` for downstream consumers / tests.
export { getTenantId };
