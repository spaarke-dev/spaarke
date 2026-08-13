/**
 * CommunicationReconciliation runtime-config singleton.
 *
 * Thin consumer of the canonical `createRuntimeConfigStore` factory from
 * `@spaarke/auth` (FR-21 / ADR-028) — mirrors
 * `src/solutions/EmailPage/src/config/runtimeConfig.ts` exactly. Multiple
 * consumer modules import `setRuntimeConfig` / `waitForConfig` /
 * `getBffBaseUrl` etc. from this path; keeping this file as a single
 * factory-call + re-export preserves singleton-per-solution semantics.
 *
 * @see @spaarke/auth#createRuntimeConfigStore — the canonical factory.
 * @see ADR-028 — Spaarke Auth Architecture.
 * @see ../services/authInit.ts — uses `waitForConfig` as `beforeInit` hook.
 */

import { createRuntimeConfigStore } from "@spaarke/auth";

const store = createRuntimeConfigStore({
  errorLabel: "CommunicationReconciliation",
  // The auth bootstrap awaits waitForConfig() as the
  // createCodePageAuthInitializer `beforeInit` hook (mirrors EmailPage).
  enableWaitForConfig: true,
});

export const setRuntimeConfig = store.setRuntimeConfig;
export const waitForConfig = store.waitForConfig;
export const getBffBaseUrl = store.getBffBaseUrl;
export const getBffOAuthScope = store.getBffOAuthScope;
export const getMsalClientId = store.getMsalClientId;
