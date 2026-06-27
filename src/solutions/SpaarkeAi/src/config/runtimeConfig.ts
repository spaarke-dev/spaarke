/**
 * SpaarkeAi runtime-config singleton.
 *
 * Post-consolidation (R2 task 055 / FR-21): this file is a thin consumer of the
 * canonical `createRuntimeConfigStore` factory from `@spaarke/auth`. The
 * previous local implementation (singleton state, getters, error labeling,
 * simple `getTenantId()` reader) has been eliminated; the factory preserves
 * the exact pre-consolidation behavior via the `errorLabel: "SpaarkeAi"`
 * config (defaults handle the simple tenant-id read case). See
 * `projects/spaarke-daily-update-service-r2/notes/runtime-config-divergence.md`
 * for the divergence analysis informing the consolidation.
 *
 * Why this file still exists (FR-21 acceptance relaxation, owner decision):
 * Multiple consumer modules import `setRuntimeConfig` / `getBffBaseUrl` /
 * `getTenantId` from this path. Replacing every import with a direct
 * `@spaarke/auth` factory call would create N stores per solution (each
 * factory call produces a fresh store — wrong singleton semantics). Keeping
 * this file as a single factory-call + re-export preserves singleton-per-
 * solution semantics AND avoids touching consumer imports.
 *
 * @see @spaarke/auth#createRuntimeConfigStore — the canonical factory.
 * @see ADR-028 — Spaarke Auth Architecture.
 * @see ADR-006 — Code Pages for standalone dialogs.
 */

import { createRuntimeConfigStore } from "@spaarke/auth";

const store = createRuntimeConfigStore({
  errorLabel: "SpaarkeAi",
  // SpaarkeAi's getTenantId() is a simple stored-value read with no fallback —
  // factory defaults (lazyTenantResolveWithTelemetry: false) handle this
  // exactly the same way the pre-consolidation local impl did.
});

export const setRuntimeConfig = store.setRuntimeConfig;
export const getBffBaseUrl = store.getBffBaseUrl;
export const getBffOAuthScope = store.getBffOAuthScope;
export const getMsalClientId = store.getMsalClientId;
export const getTenantId = store.getTenantId;
