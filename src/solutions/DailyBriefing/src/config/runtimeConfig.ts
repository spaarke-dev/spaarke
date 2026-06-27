/**
 * DailyBriefing runtime-config singleton.
 *
 * Post-consolidation (R2 task 055 / FR-21): this file is a thin consumer of the
 * canonical `createRuntimeConfigStore` factory from `@spaarke/auth`. The
 * previous local implementation (singleton state, getters, error labeling,
 * `waitForConfig` Promise gate) has been eliminated; the factory preserves the
 * exact pre-consolidation behavior via the `enableWaitForConfig: true` +
 * `errorLabel: "DailyBriefing"` config. See
 * `projects/spaarke-daily-update-service-r2/notes/runtime-config-divergence.md`
 * for the divergence analysis informing the consolidation.
 *
 * Why this file still exists (FR-21 acceptance relaxation, owner decision):
 * Multiple consumer modules import `setRuntimeConfig` / `waitForConfig` /
 * `getBffBaseUrl` etc. from this path. Replacing every import with a direct
 * `@spaarke/auth` factory call would create N stores per solution (each
 * factory call produces a fresh store — wrong singleton semantics). Keeping
 * this file as a single factory-call + re-export preserves singleton-per-
 * solution semantics AND avoids touching consumer imports.
 *
 * @see @spaarke/auth#createRuntimeConfigStore — the canonical factory.
 * @see ADR-028 — Spaarke Auth Architecture.
 * @see ../services/authInit.ts — uses `waitForConfig` as `beforeInit` hook.
 */

import { createRuntimeConfigStore } from "@spaarke/auth";

const store = createRuntimeConfigStore({
  errorLabel: "DailyBriefing",
  // DailyBriefing's auth bootstrap awaits waitForConfig() as the
  // createCodePageAuthInitializer `beforeInit` hook. Keep gate enabled.
  enableWaitForConfig: true,
});

export const setRuntimeConfig = store.setRuntimeConfig;
export const waitForConfig = store.waitForConfig;
export const getBffBaseUrl = store.getBffBaseUrl;
export const getBffOAuthScope = store.getBffOAuthScope;
export const getMsalClientId = store.getMsalClientId;
