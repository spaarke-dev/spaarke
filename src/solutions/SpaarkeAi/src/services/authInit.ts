/**
 * SpaarkeAi auth initializer.
 *
 * Post-migration (R2 task 054 / FR-20): this file is a thin consumer of the
 * canonical `createCodePageAuthInitializer` factory from `@spaarke/auth`. The
 * previous local implementation (`_initPromise`, `ensureAuthInitialized`,
 * `authenticatedFetch`, `getTenantId`) has been eliminated; the factory
 * preserves the exact pre-consolidation call sequence via the
 * `proactiveRefresh: true` + explicit `tenantId` + `logLabel: 'SpaarkeAi:authInit'`
 * config. See `projects/spaarke-daily-update-service-r2/notes/auth-init-divergence.md`
 * for the divergence analysis and ADR-028 for the canonical pattern.
 *
 * Lazy factory construction (R2 task 054 — IMPORTANT, mirrors task 053 reasoning):
 * SpaarkeAi's runtime config (`getMsalClientId`, `getRuntimeTenantId`,
 * `getBffBaseUrl`, `getBffOAuthScope`) is NOT available at module load — it's
 * populated by `setRuntimeConfig(...)` in `main.tsx` AFTER `resolveRuntimeConfig()`
 * (or the BFF `/api/config/client` fallback) resolves. The original local
 * `authInit.ts` worked because it called the getters INSIDE the once-only init
 * IIFE (i.e. at first call, by which time `setRuntimeConfig` had fired). The
 * factory captures config values eagerly in its closure, so we MUST defer
 * factory construction until first use of any exported method. Each exported
 * method ensures the lazy factory has been built before delegating.
 *
 * Why getTenantId aliasing matters: the original file relied on a static
 * import alias to dodge a shadow-recursion bug (see git blame for the warning
 * docblock). With the factory pattern, the shadow risk is gone — the local
 * `getTenantId` function delegates to the factory's `getTenantId` via the
 * returned object reference, not by re-importing.
 *
 * @see ADR-028 - Spaarke Auth Architecture
 * @see ADR-010 - DI Minimalism
 * @see src/solutions/DailyBriefing/src/services/authInit.ts — task 053 reference impl
 * @see src/solutions/LegalWorkspace/src/services/authInit.ts — task 054 sibling pattern
 */

import {
  createCodePageAuthInitializer,
  type CodePageAuthInitializer,
} from "@spaarke/auth";
import {
  getBffBaseUrl,
  getBffOAuthScope,
  getMsalClientId,
  getTenantId as getRuntimeTenantId,
} from "../config/runtimeConfig";

let _initializer: CodePageAuthInitializer | null = null;

/**
 * Build the factory once, on first use. By this time, the caller MUST have
 * arranged for `setRuntimeConfig(...)` to be called before any auth method
 * actually fires `initAuth(...)` — which is exactly what `bootstrap()` in
 * `main.tsx` does (it `setRuntimeConfig`s, THEN awaits `ensureAuthInitialized`).
 *
 * Each exported function calls this lazily, so the getters
 * (`getMsalClientId()`, `getRuntimeTenantId()`, etc.) fire on first auth
 * invocation rather than at module load — preventing the
 * "Runtime config not initialized" throw that an eager top-level factory call
 * would produce.
 */
function getInitializer(): CodePageAuthInitializer {
  if (!_initializer) {
    _initializer = createCodePageAuthInitializer({
      clientId: getMsalClientId(),
      // tenantId from sprk_TenantId env var → library constructs tenant-specific
      // authority `login.microsoftonline.com/{tenantId}`. Without this the
      // authority falls back to `/organizations` and ssoSilent can't
      // disambiguate the session cookie → popup-on-every-startup.
      tenantId: getRuntimeTenantId(),
      bffBaseUrl: getBffBaseUrl(),
      bffApiScope: getBffOAuthScope(),
      proactiveRefresh: true,
      logLabel: "SpaarkeAi:authInit",
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
