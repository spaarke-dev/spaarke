/**
 * sprk_communicationconversationpage runtime-config singleton.
 *
 * Thin consumer of the canonical `createRuntimeConfigStore` factory from
 * `@spaarke/auth` (task 032, FR-14b) — the SAME factory DailyBriefing /
 * LegalWorkspace / SpaarkeAi / Reporting each wrap in their own
 * `config/runtimeConfig.ts`. See
 * `src/solutions/LegalWorkspace/src/config/runtimeConfig.ts` for the
 * consolidation history (R2 task 055 / FR-21).
 *
 * `enableWaitForConfig` is left at its default (`false`) and
 * `lazyTenantResolveWithTelemetry` is omitted — this page has no telemetry
 * module and blocks on `resolveRuntimeConfig()` + `ensureAuthInitialized()`
 * BEFORE rendering (see `main.tsx`), so neither knob is needed (mirrors
 * LegalWorkspace's blocking bootstrap, not DailyBriefing's non-blocking one —
 * the conversation list is this page's CORE function, not an optional
 * enhancement).
 *
 * @see @spaarke/auth#createRuntimeConfigStore — the canonical factory.
 * @see ADR-028 — Spaarke Auth Architecture.
 */

import { createRuntimeConfigStore } from '@spaarke/auth';

const store = createRuntimeConfigStore({
  errorLabel: 'CommunicationConversationPage',
});

export const setRuntimeConfig = store.setRuntimeConfig;
export const getBffBaseUrl = store.getBffBaseUrl;
export const getBffOAuthScope = store.getBffOAuthScope;
export const getMsalClientId = store.getMsalClientId;
export const getTenantId = store.getTenantId;
