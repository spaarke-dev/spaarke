/**
 * DailyBriefing auth initializer.
 *
 * Post-migration (R2 task 053 / FR-20): this file is a thin consumer of the
 * canonical `createCodePageAuthInitializer` factory from `@spaarke/auth`. The
 * previous local implementation (`_initPromise`, `ensureAuthInitialized`,
 * etc.) has been eliminated; the factory preserves the exact pre-consolidation
 * call sequence via the `proactiveRefresh: false` + `beforeInit: waitForConfig`
 * + `logLabel: 'DailyBriefing'` config. See
 * `projects/spaarke-daily-update-service-r2/notes/auth-init-divergence.md`
 * for the divergence analysis and ADR-028 for the canonical pattern.
 *
 * Lazy factory construction (R2 task 053 — IMPORTANT):
 * DailyBriefing's runtime config (`getMsalClientId`, `getBffBaseUrl`,
 * `getBffOAuthScope`) is NOT available at module load — it's populated by
 * `setRuntimeConfig(...)` in `main.tsx` AFTER `resolveRuntimeConfig()` resolves.
 * The original local `authInit.ts` worked because it called the getters INSIDE
 * the once-only init IIFE (after `await waitForConfig()`). The factory captures
 * config values eagerly in its closure, so we MUST defer factory construction
 * until first use of any exported method. Each exported method ensures the
 * lazy factory has been built before delegating.
 */

import {
  createCodePageAuthInitializer,
  type CodePageAuthInitializer,
  type OkResponse,
} from "@spaarke/auth";
import {
  getBffBaseUrl,
  getBffOAuthScope,
  getMsalClientId,
  getTenantId as getConfiguredTenantId,
  waitForConfig,
} from "../config/runtimeConfig";

let _initializer: CodePageAuthInitializer | null = null;

/**
 * Build the factory once, on first use. By this time, the caller MUST have
 * arranged for `setRuntimeConfig(...)` to be called before any auth method
 * actually fires `initAuth(...)` — which is exactly what `bootstrapAuth()` in
 * `main.tsx` does (it `setRuntimeConfig`s, THEN awaits `ensureAuthInitialized`).
 *
 * The factory's `beforeInit: waitForConfig` provides a second line of defense:
 * even if a caller invokes `ensureAuthInitialized()` before `setRuntimeConfig`,
 * the `waitForConfig()` hook will block until config is set. But the getters
 * used to build the config are called once at factory construction, so we
 * defer that construction until the first method invocation (when `main.tsx`
 * has already called `setRuntimeConfig`).
 */
function getInitializer(): CodePageAuthInitializer {
  if (!_initializer) {
    _initializer = createCodePageAuthInitializer({
      clientId: getMsalClientId(),
      // The environment's tenant (sprk_TenantId, via runtime config) — a tenant-specific authority is required
      // for B2B guests (#1453). The library validates it and ignores an invalid value.
      tenantId: getConfiguredTenantId(),
      bffBaseUrl: getBffBaseUrl(),
      bffApiScope: getBffOAuthScope(),
      proactiveRefresh: false, // Short-lived dialog
      logLabel: "DailyBriefing",
      beforeInit: waitForConfig, // Wait for runtimeConfig.setRuntimeConfig() in main.tsx
    });
  }
  return _initializer;
}

export function ensureAuthInitialized(): Promise<void> {
  return getInitializer().ensureAuthInitialized();
}

export function authenticatedFetch(url: string, init?: RequestInit): Promise<OkResponse> {
  return getInitializer().authenticatedFetch(url, init);
}

export function getTenantId(): Promise<string> {
  return getInitializer().getTenantId();
}
