/**
 * Per-context authority selection for the STANDALONE (browser, non-Teams) bootstrap — task 013.
 *
 * The module-host SPA serves both identity planes from one URL (ADR-028 Amendment A3). In a browser,
 * home-realm discovery (the "My organization / Partner" chooser) yields a {@link Realm}; this module
 * maps that realm to the concrete standalone-MSAL instance + BFF scope the shell authenticates with,
 * WITHOUT forking the auth module:
 *
 *   - 'ciam'      → the existing CIAM `msalInstance` singleton (msal-config.ts) + CIAM BFF scope.
 *                   Returned as-is so the CIAM path stays byte-for-byte unchanged (FR-15 / NFR-05):
 *                   same authority, same knownAuthorities, same per-tab sessionStorage cache.
 *   - 'workforce' → a standalone `PublicClientApplication` built from task-010's pluggable
 *                   `workforceAuthorityConfig` (workforce-multitenant `/organizations` authority),
 *                   using the SAME workforce app registration + BFF scope as the proven teams-app-r1
 *                   NAA path — only the acquisition TRANSPORT differs (browser redirect here vs the
 *                   Teams host broker there), so the token's audience/issuer are identical to the
 *                   proven recipe (task 013 step 5 — no aud/iss mismatch, escalation does not fire).
 *
 * Broker-only invariant (ADR-028 A1/A2/A3 / NFR-01) holds on BOTH planes: the acquired token
 * authenticates to the BFF ONLY and is never exchanged for a downstream Graph/SPE/Dataverse token —
 * `acquireStandaloneBffToken` returns a Bearer used only against the BFF; there is no OBO here.
 *
 * This module deliberately does NOT touch `@spaarke/auth`: ADR-028 A3 requires the module-host SPA
 * to use the shared standalone-MSAL module (pluggable authority), NOT the Xrm-bound `@spaarke/auth`.
 */

import type { IPublicClientApplication } from '@azure/msal-browser';
import type { Realm } from './realm';
import { MSAL_BFF_SCOPE, getWorkforceEnvConfig } from '../config';
import {
  msalInstance,
  createStandaloneMsalInstance,
  workforceAuthorityConfig,
} from './msal-config';
import {
  acquireStandaloneBffToken,
  setActiveBffTokenAcquirer,
  setActiveLoginScope,
} from './msal-auth';

/** A resolved browser sign-in plane: the MSAL instance to mount + the BFF scope to request. */
export interface StandalonePlane {
  /** The plane this instance authenticates. */
  realm: Realm;
  /** The `PublicClientApplication` to `initialize()` and hand to `<MsalProvider>`. */
  instance: IPublicClientApplication;
  /** The BFF OAuth scope for this plane's authority (used for token acquisition + login redirect). */
  bffScope: string;
}

/**
 * Resolve the standalone MSAL instance + BFF scope for a chosen realm.
 *
 * CIAM returns the shared singleton unchanged; workforce builds a fresh standalone instance against
 * the workforce-multitenant authority. Throws (fail-loud) if the workforce env values are missing —
 * the caller (the realm chooser flow) surfaces this rather than silently signing in on the wrong
 * plane (mirrors msal-config's fail-safe authority validation).
 */
export function resolveStandalonePlane(realm: Realm): StandalonePlane {
  if (realm === 'ciam') {
    return { realm, instance: msalInstance, bffScope: MSAL_BFF_SCOPE };
  }

  // 'workforce' — standalone browser redirect flow against the workforce-multitenant authority.
  const { clientId, bffScope } = getWorkforceEnvConfig();
  const instance = createStandaloneMsalInstance(workforceAuthorityConfig({ clientId, bffScope }));
  return { realm, instance, bffScope };
}

/**
 * Point the app's active token-acquisition + login-redirect seams at the resolved plane, so every
 * downstream BFF call (`bff-client.ts` → `acquireActiveBffToken`) and the `AuthGuard` sign-in
 * redirect target the correct authority. Idempotent per bootstrap.
 *
 * For CIAM this is a no-op equivalence (the acquirer + scope defaults ALREADY are CIAM), so the CIAM
 * path remains byte-for-byte unchanged; it is applied uniformly only for clarity + determinism
 * across the login redirect (main() re-resolves from the stored realm on every load).
 */
export function applyStandalonePlane(plane: StandalonePlane): void {
  setActiveBffTokenAcquirer(() => acquireStandaloneBffToken(plane.instance, plane.bffScope));
  setActiveLoginScope(plane.bffScope);
}
