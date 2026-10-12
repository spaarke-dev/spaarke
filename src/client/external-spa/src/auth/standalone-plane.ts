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
 *   - 'workforce' → a standalone `PublicClientApplication` built from the pluggable
 *                   `workforceAuthorityConfig`: the single-tenant Spaarke client with the authority
 *                   pinned to `https://login.microsoftonline.com/{Spaarke tenant id}` and the scope
 *                   `api://{customer BFF app id}/user_impersonation` (ADR-028 A7, task 040 / FR-16).
 *                   It uses the SAME client, tenant and scope as the Teams tab — only the acquisition
 *                   TRANSPORT differs (browser redirect here vs NAA / MSAL popup there).
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
import { getWorkforceClientConfig } from '../config';
import { activeBffScope } from './backend-selection';
import { msalInstance, createStandaloneMsalInstance, workforceAuthorityConfig, popupRedirectUri } from './msal-config';
import {
  acquireStandaloneBffToken,
  setActiveAccountTenantId,
  setActiveBffTokenAcquirer,
  setActiveLoginScope,
  type StandaloneAcquireOptions,
} from './msal-auth';

/** A resolved browser sign-in plane: the MSAL instance to mount + the BFF scope to request. */
export interface StandalonePlane {
  /** The plane this instance authenticates. */
  realm: Realm;
  /** The `PublicClientApplication` to `initialize()` and hand to `<MsalProvider>`. */
  instance: IPublicClientApplication;
  /** The BFF OAuth scope for this plane's authority (used for token acquisition + login redirect). */
  bffScope: string;
  /**
   * Workforce only: Spaarke's tenant id (account filter) and the bridge page for the silent-iframe retry.
   * Absent for CIAM, whose silent requests carry no redirect URI and are unchanged on the wire.
   */
  acquireOptions?: StandaloneAcquireOptions;
}

/**
 * Resolve the standalone MSAL instance + BFF scope for a chosen realm.
 *
 * The scope is the active backend's `api://{customer BFF app id}/user_impersonation` for this plane (task 041,
 * `backend-selection.ts`), so the bootstrap selects the backend first. It throws (fail closed) when no backend for
 * the plane can be resolved.
 *
 * CIAM returns the shared singleton unchanged; workforce builds a fresh standalone instance against
 * the Spaarke-tenant workforce authority. Throws (fail-loud) if the workforce env values are missing
 * or non-conforming (a non-GUID tenant, a scope other than `…/user_impersonation`) —
 * the caller (the realm chooser flow) surfaces this rather than silently signing in on the wrong
 * plane (mirrors msal-config's fail-safe authority validation).
 */
export function resolveStandalonePlane(realm: Realm): StandalonePlane {
  if (realm === 'ciam') {
    return { realm, instance: msalInstance, bffScope: activeBffScope('ciam') };
  }

  // 'workforce' — standalone browser redirect flow against the Spaarke-tenant workforce authority (A7).
  const { clientId, tenantId } = getWorkforceClientConfig();
  const bffScope = activeBffScope('workforce');
  const instance = createStandaloneMsalInstance(workforceAuthorityConfig({ clientId, tenantId, bffScope }));
  return { realm, instance, bffScope, acquireOptions: { tenantId, silentRedirectUri: popupRedirectUri() } };
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
  // `scope` is the active backend's scope passed by bff-client.ts (the same record as the request URL); the plane's
  // scope is the fallback for callers that pass none.
  // `options.forceRefresh` is set only by the 401 retry (bff-client.ts), `options.noInteraction` only by the bell's timer
  // poll; every other call shapes the request as before.
  setActiveBffTokenAcquirer((scope, options) =>
    acquireStandaloneBffToken(
      plane.instance,
      scope ?? plane.bffScope,
      options?.forceRefresh || options?.noInteraction
        ? {
            ...plane.acquireOptions,
            ...(options.forceRefresh ? { forceRefresh: true } : {}),
            ...(options.noInteraction ? { noInteraction: true } : {}),
          }
        : plane.acquireOptions
    )
  );
  setActiveLoginScope(plane.bffScope);
  // MSAL's account list is not client-scoped (both browser instances share the tab's sessionStorage), so the
  // workforce plane only counts accounts of Spaarke's tenant; CIAM keeps every account (null = no filter).
  setActiveAccountTenantId(plane.acquireOptions?.tenantId ?? null);
}
