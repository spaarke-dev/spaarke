/**
 * LegalWorkspace auth initializer.
 *
 * Post-migration (R2 task 054 / FR-20): this file is a thin consumer of the
 * canonical `createCodePageAuthInitializer` factory from `@spaarke/auth`. The
 * previous local implementation (`_initPromise`, `ensureAuthInitialized`,
 * `authenticatedFetch`, `getTenantId`) has been eliminated; the factory
 * preserves the exact pre-consolidation call sequence via the
 * `proactiveRefresh: true` + explicit `tenantId` + `logLabel: 'authInit'`
 * config. See `projects/spaarke-daily-update-service-r2/notes/auth-init-divergence.md`
 * for the divergence analysis and ADR-028 for the canonical pattern.
 *
 * Lazy factory construction (R2 task 054 — IMPORTANT, mirrors task 053 reasoning):
 * LegalWorkspace's runtime config (`getMsalClientId`, `getRuntimeTenantId`,
 * `getBffBaseUrl`, `getBffOAuthScope`) is NOT available at module load — it's
 * populated by `setRuntimeConfig(...)` in `main.tsx` AFTER `resolveRuntimeConfig()`
 * resolves. The original local `authInit.ts` worked because it called the
 * getters INSIDE the once-only init IIFE (i.e. at first call, by which time
 * `setRuntimeConfig` had fired). The factory captures config values eagerly in
 * its closure, so we MUST defer factory construction until first use of any
 * exported method. Each exported method ensures the lazy factory has been
 * built before delegating.
 *
 * Why this file still exists (FR-20 acceptance relaxation, owner decision):
 * 22 consumer modules across LegalWorkspace import `authenticatedFetch` /
 * `getTenantId` from this path. Outright deletion would require touching all
 * 22 imports — needless churn given the factory consolidation has already
 * eliminated the divergent implementation logic. The file is now a 4-line
 * factory consumer rather than 86-line forked logic.
 *
 * @see ADR-028 - Spaarke Auth Architecture
 * @see ADR-010 - DI Minimalism
 * @see src/solutions/DailyBriefing/src/services/authInit.ts — task 053 reference impl
 */

import {
  createCodePageAuthInitializer,
  type CodePageAuthInitializer,
} from '@spaarke/auth';
import {
  getBffBaseUrl,
  getBffOAuthScope,
  getMsalClientId,
  getTenantId as getRuntimeTenantId,
} from '../config/runtimeConfig';

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
      tenantId: getRuntimeTenantId(),
      bffBaseUrl: getBffBaseUrl(),
      bffApiScope: getBffOAuthScope(),
      proactiveRefresh: true,
      logLabel: 'authInit',
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
