/**
 * EmailPage auth initializer.
 *
 * Thin consumer of the canonical `createCodePageAuthInitializer` factory from
 * `@spaarke/auth` (ADR-028) — mirrors
 * `src/solutions/DailyBriefing/src/services/authInit.ts` exactly. No new
 * auth surface, no token snapshots (NFR-07): every `.eml` render call and
 * composer send goes through the `authenticatedFetch` exported here.
 *
 * Lazy factory construction (mirrors DailyBriefing): EmailPage's runtime
 * config (`getMsalClientId`, `getBffBaseUrl`, `getBffOAuthScope`) is NOT
 * available at module load — it's populated by `setRuntimeConfig(...)` in
 * `main.tsx` AFTER `resolveRuntimeConfig()` resolves. The factory captures
 * config values eagerly in its closure, so factory construction is deferred
 * until first use of any exported method.
 */

import { createCodePageAuthInitializer, type CodePageAuthInitializer } from "@spaarke/auth";
import { getBffBaseUrl, getBffOAuthScope, getMsalClientId, waitForConfig } from "../config/runtimeConfig";

let _initializer: CodePageAuthInitializer | null = null;

/**
 * Build the factory once, on first use. By this time, the caller MUST have
 * arranged for `setRuntimeConfig(...)` to be called before any auth method
 * actually fires `initAuth(...)` — which is exactly what `bootstrapAuth()` in
 * `main.tsx` does (it `setRuntimeConfig`s, THEN awaits `ensureAuthInitialized`).
 *
 * The factory's `beforeInit: waitForConfig` provides a second line of defense:
 * even if a caller invokes `ensureAuthInitialized()` before `setRuntimeConfig`,
 * the `waitForConfig()` hook will block until config is set.
 */
function getInitializer(): CodePageAuthInitializer {
  if (!_initializer) {
    _initializer = createCodePageAuthInitializer({
      clientId: getMsalClientId(),
      bffBaseUrl: getBffBaseUrl(),
      bffApiScope: getBffOAuthScope(),
      // EmailPage-specific: omit tenantId → factory falls back to Xrm
      // (preserves the DailyBriefing precedent for standalone code pages).
      proactiveRefresh: false, // Short-lived dialog / standalone tab
      logLabel: "EmailPage",
      beforeInit: waitForConfig, // Wait for runtimeConfig.setRuntimeConfig() in main.tsx
    });
  }
  return _initializer;
}

export function ensureAuthInitialized(): Promise<void> {
  return getInitializer().ensureAuthInitialized();
}

export function authenticatedFetch(url: string, init?: RequestInit): Promise<Response> {
  return getInitializer().authenticatedFetch(url, init);
}

export function getTenantId(): Promise<string> {
  return getInitializer().getTenantId();
}
