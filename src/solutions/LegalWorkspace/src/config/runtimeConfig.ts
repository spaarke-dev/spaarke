/**
 * LegalWorkspace runtime-config singleton.
 *
 * Post-consolidation (R2 task 055 / FR-21): this file is a thin consumer of the
 * canonical `createRuntimeConfigStore` factory from `@spaarke/auth`. The
 * previous local implementation (singleton state, getters, error labeling,
 * `getTenantId()` with `resolveTenantIdSync` fallback + telemetry) has been
 * eliminated; the factory preserves the exact pre-consolidation behavior via
 * the `errorLabel: "LegalWorkspace"` + `lazyTenantResolveWithTelemetry: true`
 * + `onLazyTenantResolve: trackEvent(...)` config. See
 * `projects/spaarke-daily-update-service-r2/notes/runtime-config-divergence.md`
 * for the divergence analysis informing the consolidation.
 *
 * Why this file still exists (FR-21 acceptance relaxation, owner decision):
 * ~20+ consumer modules import `setRuntimeConfig` / `getBffBaseUrl` /
 * `getTenantId` from this path, and `LegalWorkspace/src/index.ts` re-exports
 * `setRuntimeConfig as setLegalWorkspaceRuntimeConfig` (part of the embedded-
 * mode host contract). Replacing every import with a direct `@spaarke/auth`
 * factory call would create N stores per solution (each factory call produces
 * a fresh store — wrong singleton semantics). Keeping this file as a single
 * factory-call + re-export preserves singleton-per-solution semantics AND
 * avoids touching ~20 consumer imports.
 *
 * @see @spaarke/auth#createRuntimeConfigStore — the canonical factory.
 * @see ADR-028 — Spaarke Auth Architecture.
 * @see ../components/RecordCards/DocumentCard.tsx — primary `getTenantId` consumer.
 */

import { createRuntimeConfigStore } from "@spaarke/auth";
import { trackEvent } from "../services/telemetry";

const store = createRuntimeConfigStore({
  errorLabel: "LegalWorkspace",
  // LegalWorkspace's getTenantId() falls back to resolveTenantIdSync() when the
  // stored tenantId is empty (intermittent Xrm timing issue) and emits a
  // telemetry event so we can monitor occurrence in App Insights.
  lazyTenantResolveWithTelemetry: true,
  onLazyTenantResolve: (payload) => trackEvent("TenantIdLazyResolve", payload),
});

export const setRuntimeConfig = store.setRuntimeConfig;
export const getBffBaseUrl = store.getBffBaseUrl;
export const getBffOAuthScope = store.getBffOAuthScope;
export const getMsalClientId = store.getMsalClientId;
export const getTenantId = store.getTenantId;
