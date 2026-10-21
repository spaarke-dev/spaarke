/**
 * Per-context authority selection for the STANDALONE (browser, non-Teams) bootstrap — task 013, reworked by task 058.
 *
 * The module-host SPA serves both identity planes from one URL (ADR-028 Amendment A3). In a browser,
 * home-realm discovery (the "Partner / company account" chooser) yields a {@link Realm}; this module
 * maps that realm to the concrete standalone-MSAL instance + BFF scope the shell authenticates with,
 * WITHOUT forking the auth module:
 *
 *   - 'ciam'      → the existing CIAM `msalInstance` singleton (msal-config.ts) + CIAM BFF scope.
 *                   Returned as-is so the CIAM path stays byte-for-byte unchanged (FR-15 / NFR-05):
 *                   same authority, same knownAuthorities, same per-tab sessionStorage cache.
 *   - 'workforce' → the workforce HOME sign-in (ADR-028 Amendment A9, task 058), prepared by
 *                   {@link prepareWorkforceHomePlane}: ONE multitenant public client (authority `organizations`,
 *                   discovery and login only) signs the contact in to THEIR OWN directory; the signed-in `tid` picks
 *                   the customer backend from the directory entries (`homeTenantIds`); only then is the BFF token
 *                   requested - on authority `https://login.microsoftonline.com/{tid}` with that entry's
 *                   `api://{bffAppId}/user_impersonation`. A `tid` no entry lists is "not set up": no token request,
 *                   no BFF call. The single-tenant A8 client is NOT used for the browser sign-in any more (it stays the
 *                   Teams tab's client and the guest-token link, msal-auth.ts).
 *
 * Broker-only invariant (ADR-028 A1/A2/A3 / NFR-01) holds on BOTH planes: the acquired token
 * authenticates to the BFF ONLY and is never exchanged for a downstream Graph/SPE/Dataverse token —
 * `acquireStandaloneBffToken` returns a Bearer used only against the BFF; there is no OBO here.
 *
 * This module deliberately does NOT touch `@spaarke/auth`: ADR-028 A3 requires the module-host SPA
 * to use the shared standalone-MSAL module (pluggable authority), NOT the Xrm-bound `@spaarke/auth`.
 */

import { PublicClientApplication, type IPublicClientApplication } from '@azure/msal-browser';
import type { Realm } from './realm';
import { getWorkforceClientConfig, getWorkforceHomeClientConfig, isUserImpersonationScope } from '../config';
import {
  activateBackend,
  activeBffScope,
  clearActiveBackend,
  getActiveBackend,
  resolveHomeBackendSelection,
  BackendSelectionError,
  type Backend,
  type BackendDirectory,
} from './backend-selection';
import {
  msalInstance,
  assertWorkforceHomeAuthority,
  buildMsalConfiguration,
  popupRedirectUri,
  workforceHomeAuthorityConfig,
  workforceHomeTokenAuthority,
} from './msal-config';
import {
  acquireStandaloneBffToken,
  isCiamAccount,
  isWorkforceHomeAccount,
  setActiveAccountFilter,
  setActiveAccountTenantId,
  setActiveBffTokenAcquirer,
  setActiveLoginScope,
  setWorkforceHomePlaneActive,
  syncActiveAccountOnInteractiveSignIn,
  type StandaloneAcquireOptions,
} from './msal-auth';

/** A resolved browser sign-in plane: the MSAL instance to mount + the BFF scope to request. */
export interface StandalonePlane {
  /** The plane this instance authenticates. */
  realm: Realm;
  /** The `PublicClientApplication` to `initialize()` and hand to `<MsalProvider>`. */
  instance: IPublicClientApplication;
  /**
   * The BFF OAuth scope for this plane's authority (used for token acquisition). Empty on the home plane's sign-in
   * stage, before the signed-in tenant has picked a backend: no token is requested then.
   */
  bffScope: string;
  /**
   * The scope `AuthGuard` asks for on the sign-in redirect. Defaults to {@link bffScope}; the home plane signs in with
   * `openid` only, because the BFF scope belongs to a backend that the signed-in tenant has not picked yet.
   */
  loginScope?: string;
  /** True for the workforce HOME plane (A9): enables the registration gate and refuses a token request without a backend. */
  home?: boolean;
  /** Account predicate: which cached accounts count as this plane's sign-in (home: workforce accounts; CIAM: CIAM accounts). */
  accountFilter?: (account: { tenantId?: string }) => boolean;
  /**
   * Workforce: the signed-in tenant (account filter), the bridge page for the silent-iframe retry and the per-request
   * token authority. CIAM: only `accountFilter` (#2166), which MSAL never sees, so its requests are unchanged on the wire.
   */
  acquireOptions?: StandaloneAcquireOptions;
}

/**
 * Resolve the standalone MSAL instance + BFF scope for the CIAM realm.
 *
 * The scope is the active backend's `api://{customer BFF app id}/user_impersonation` for this plane (task 041,
 * `backend-selection.ts`), so the bootstrap selects the backend first. It throws (fail closed) when no backend for
 * the plane can be resolved.
 *
 * CIAM returns the shared singleton unchanged. The workforce realm is NOT resolved here any more (A9, task 058): its
 * plane depends on the signed-in tenant and is prepared asynchronously by {@link prepareWorkforceHomePlane}.
 */
export function resolveStandalonePlane(realm: Realm): StandalonePlane {
  if (realm === 'ciam') {
    // Only CIAM accounts count and are used (#2166): a company account cached in this tab is not a partner sign-in.
    return {
      realm,
      instance: msalInstance,
      bffScope: activeBffScope('ciam'),
      accountFilter: isCiamAccount,
      acquireOptions: { accountFilter: isCiamAccount },
    };
  }

  throw new Error(
    '[CollabAuth] The browser workforce plane is the multitenant home sign-in (ADR-028 A9); ' +
      'prepare it with prepareWorkforceHomePlane().'
  );
}

/** What {@link prepareWorkforceHomePlane} decided. */
export type HomePlanePreparation =
  /** Mount the app on this plane: the sign-in stage (no account yet) or the signed-in plane with its backend active. */
  | { kind: 'plane'; plane: StandalonePlane }
  /** Several directory entries list the signed-in tenant: show the customer picker, then prepare again with the key. */
  | { kind: 'pick'; options: Backend[] }
  /** No directory entry lists the signed-in tenant: nothing was activated, requested or fetched. */
  | { kind: 'notSetUp'; instance: IPublicClientApplication };

export interface PrepareHomePlaneOptions {
  /** The customer key picked from a `pick` result; the backend `chooseBackend` activated is then used as is. */
  pickedKey?: string;
  /** Defaults to `window.location.search`; only its `customer` parameter is read. */
  search?: string;
  directory?: BackendDirectory;
}

/** The ONE multitenant discovery instance of this page load (StrictMode and the picker re-run the bootstrap). */
let homeInstancePromise: Promise<IPublicClientApplication> | null = null;
let homeInstanceClientId: string | null = null;

const MSAL_INTERACTION_STATUS_KEY = 'msal.interaction.status';

/**
 * msal-browser 5.5.0 keeps ONE interaction lock per tab: sessionStorage `msal.interaction.status`, value
 * `{"clientId":"<owner>","type":"<signin|...>"}` (`BrowserCacheManager.setInteractionInProgress`). An instance ignores a lock
 * another client owns when it asks "is MY interaction running", but its `loginRedirect` / `acquireTokenRedirect` still
 * throw `interaction_in_progress` while ANY lock exists.
 *
 * The guest-token link (`acquireSpaarkeGuestToken`, A8 client) opens a POPUP; the lock is set when it opens and removed
 * when it ends. A reload or navigation while the popup is open leaves the A8 lock behind, and every later home redirect
 * would fail until the tab closes. In the browser the A8 instance is memory-only and never redirects, so an A8 lock at
 * page load is stale by construction: remove it. A lock owned by any other client id (the home client's own redirect
 * response is still to be processed, CIAM's) and every other key are left alone.
 *
 * @returns true when a stale A8 lock was removed.
 */
export function clearStaleGuestInteractionLock(guestClientId: string): boolean {
  return clearStaleInteractionLock([guestClientId]);
}

/**
 * Remove the tab's interaction lock when its owner is one of `ownerClientIds` (case-insensitive); any other owner, an
 * unparsable lock and every other key are left alone. The generalized form of {@link clearStaleGuestInteractionLock}.
 *
 * @returns true when a lock was removed.
 */
export function clearStaleInteractionLock(ownerClientIds: readonly string[]): boolean {
  try {
    const raw = window.sessionStorage.getItem(MSAL_INTERACTION_STATUS_KEY);
    if (!raw) return false;
    const owner = (JSON.parse(raw) as { clientId?: unknown } | null)?.clientId;
    if (typeof owner !== 'string') return false;
    const lower = owner.toLowerCase();
    if (!ownerClientIds.some(id => id.toLowerCase() === lower)) return false;
    window.sessionStorage.removeItem(MSAL_INTERACTION_STATUS_KEY);
    return true;
  } catch {
    return false; // unreadable lock or no storage: msal-browser itself handles an unparsable value
  }
}

/**
 * Teams: remove a stale interaction lock at page load, BEFORE any MSAL instance is used (`bootstrapTeams`).
 *
 * The lock is one per-tab sessionStorage key that survives an iframe reload. Teams never redirects (NAA, then a popup), so a
 * lock found at page load can only be left over from a popup that the reload interrupted - and until it is removed every
 * `acquireTokenPopup` (the guest "Link my account" click, the BFF popup fallback, the "Sign in" button) throws
 * `interaction_in_progress`, until Teams restarts. Only the two clients that popup in Teams are cleared: the A8 client (the
 * guest link) and the multitenant home client (the BFF popup fallback). A lock owned by any other client (CIAM) and every
 * other key are left alone. A client id that is not configured is skipped.
 *
 * @returns true when a stale lock was removed.
 */
export function clearStaleTeamsInteractionLock(): boolean {
  const owners: string[] = [];
  for (const read of [getWorkforceClientConfig, getWorkforceHomeClientConfig]) {
    try {
      owners.push(read().clientId);
    } catch {
      /* not configured: that client could not have opened a popup */
    }
  }
  return clearStaleInteractionLock(owners);
}

async function getHomeInstance(clientId: string): Promise<IPublicClientApplication> {
  if (!homeInstancePromise || homeInstanceClientId !== clientId) {
    homeInstanceClientId = clientId;
    try {
      clearStaleGuestInteractionLock(getWorkforceClientConfig().clientId);
    } catch {
      /* no A8 client is configured, so no guest popup could have run */
    }
    const created = (async () => {
      const pca = new PublicClientApplication(buildMsalConfiguration(workforceHomeAuthorityConfig({ clientId })));
      // Before initialize(): the redirect response is handled there, and the account it signs in must become the
      // active one (so "Sign in again" with a different identity switches the account the SPA acquires tokens for).
      syncActiveAccountOnInteractiveSignIn(pca);
      await pca.initialize();
      // initialize() does not process the login redirect's response; the backend is chosen by the signed-in tenant, so
      // the account must exist BEFORE the app mounts. MsalProvider calls this again and receives the same (memoized)
      // result. A failed sign-in is recorded by the event callback above and shown by the account gate.
      await pca.handleRedirectPromise().catch(() => null);
      return pca as IPublicClientApplication;
    })();
    homeInstancePromise = created;
    created.catch(() => {
      if (homeInstancePromise === created) {
        homeInstancePromise = null;
        homeInstanceClientId = null;
      }
    });
  }
  return homeInstancePromise;
}

/**
 * Re-check a picked customer key against the signed-in tenant (the browser home plane and the Teams bootstrap share it):
 * true only when a directory entry that LISTS the tid has that key. `search` is empty so a `?customer=` key cannot stand in
 * for the picked one. A directory failure throws (fail closed, no default host).
 */
export async function pickedKeyListsTenant(
  tid: string,
  pickedKey: string,
  directory?: BackendDirectory
): Promise<boolean> {
  const recheck = await resolveHomeBackendSelection(tid, { directory, search: '' });
  if (recheck.kind === 'none') throw new Error(recheck.reason);
  return recheck.kind === 'pick'
    ? recheck.options.some(o => o.customerKey === pickedKey)
    : recheck.kind === 'selected' && recheck.backend.customerKey === pickedKey;
}

/**
 * Prepare the workforce HOME plane (ADR-028 A9). Order: the multitenant client id (fail loud when missing) -> the
 * discovery instance (redirect response processed) -> the cached home account, if any -> its `tid` -> the directory
 * entry that lists it -> the backend. No account yet is the sign-in stage: the app mounts so `AuthGuard` can start the
 * `openid` login redirect; nothing is requested for the BFF.
 */
export async function prepareWorkforceHomePlane(options: PrepareHomePlaneOptions = {}): Promise<HomePlanePreparation> {
  const { clientId } = getWorkforceHomeClientConfig();
  const instance = await getHomeInstance(clientId);

  const candidates = instance.getAllAccounts().filter(isWorkforceHomeAccount);
  const activeId = instance.getActiveAccount()?.homeAccountId;
  const account = candidates.find(a => a.homeAccountId === activeId) ?? candidates[0] ?? null;

  if (!account) {
    return {
      kind: 'plane',
      plane: {
        realm: 'workforce',
        instance,
        bffScope: '',
        loginScope: 'openid',
        home: true,
        accountFilter: isWorkforceHomeAccount,
      },
    };
  }

  const tid = account.tenantId.toLowerCase();

  let backendScope: string;
  if (options.pickedKey) {
    const active = getActiveBackend();
    if (active.plane !== 'workforce' || active.customerKey !== options.pickedKey) {
      throw new BackendSelectionError('[BackendSelection] The picked customer is not the active backend.');
    }
    // The picker offered only entries that list this tid, but the activated backend is re-checked against it here, so a
    // key that does not list the signed-in tenant can never reach a token request (fail closed to "not set up").
    if (!(await pickedKeyListsTenant(tid, options.pickedKey, options.directory))) {
      clearActiveBackend();
      return { kind: 'notSetUp', instance };
    }
    backendScope = active.bffScope;
  } else {
    const selection = await resolveHomeBackendSelection(tid, { directory: options.directory, search: options.search });
    if (selection.kind === 'none') throw new Error(selection.reason);
    if (selection.kind === 'notSetUp') return { kind: 'notSetUp', instance };
    if (selection.kind === 'pick') return { kind: 'pick', options: selection.options };
    backendScope = activateBackend('workforce', selection.backend).bffScope;
  }

  const tokenAuthority = workforceHomeTokenAuthority(tid);
  assertWorkforceHomeAuthority(tokenAuthority, clientId);
  instance.setActiveAccount(account);
  return {
    kind: 'plane',
    plane: {
      realm: 'workforce',
      instance,
      bffScope: backendScope,
      loginScope: 'openid',
      home: true,
      accountFilter: isWorkforceHomeAccount,
      acquireOptions: { tenantId: tid, silentRedirectUri: popupRedirectUri(), authority: tokenAuthority },
    },
  };
}

/**
 * Point the app's active token-acquisition + login-redirect seams at the resolved plane, so every
 * downstream BFF call (`bff-client.ts` → `acquireActiveBffToken`) and the `AuthGuard` sign-in
 * redirect target the correct authority. Idempotent per bootstrap.
 *
 * For CIAM the acquirer + scope match the defaults (the token request is unchanged on the wire); applying the plane also
 * sets the CIAM account filter ({@link isCiamAccount}, #2166). It is applied uniformly for determinism across the login
 * redirect (main() re-resolves from the stored realm on every load).
 */
export function applyStandalonePlane(plane: StandalonePlane): void {
  // `scope` is the active backend's scope passed by bff-client.ts (the same record as the request URL); the plane's
  // scope is the fallback for callers that pass none.
  // `options.forceRefresh` is set only by the 401 retry (bff-client.ts), `options.noInteraction` only by the bell's timer
  // poll; every other call shapes the request as before.
  setActiveBffTokenAcquirer((scope, options) => {
    const resolved = scope ?? plane.bffScope;
    // The home plane's sign-in stage has no backend and no scope: a token request is refused here, before MSAL is asked
    // for anything (fail closed; never a guessed scope).
    if (plane.home && !isUserImpersonationScope(resolved)) {
      return Promise.reject(
        new BackendSelectionError('[BackendSelection] No backend is active for this sign-in; no token is requested.')
      );
    }
    return acquireStandaloneBffToken(
      plane.instance,
      resolved,
      options?.forceRefresh || options?.noInteraction
        ? {
            ...plane.acquireOptions,
            ...(options.forceRefresh ? { forceRefresh: true } : {}),
            ...(options.noInteraction ? { noInteraction: true } : {}),
          }
        : plane.acquireOptions
    );
  });
  setActiveLoginScope(plane.loginScope ?? plane.bffScope);
  // MSAL's account list is not client-scoped (both browser instances share the tab's sessionStorage), so the
  // workforce plane only counts its own tenant's accounts and the CIAM plane only CIAM accounts (#2166).
  setActiveAccountTenantId(plane.acquireOptions?.tenantId ?? null);
  setActiveAccountFilter(plane.accountFilter ?? null);
  setWorkforceHomePlaneActive(plane.home === true);
}
