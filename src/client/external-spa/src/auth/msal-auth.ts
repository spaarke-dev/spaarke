/**
 * MSAL token acquisition for BFF API calls.
 *
 * Replaces the portal implicit grant flow (portal-auth.ts).
 * Acquires tokens silently via MSAL cache; falls back to redirect on
 * InteractionRequiredAuthError (consent required, MFA prompt, session expired, etc.).
 *
 * Called by bff-client.ts before every BFF API request.
 *
 * See: docs/architecture/power-pages-spa-guide.md — Authentication section
 * See: notes/auth-migration-b2b-msal.md
 */

import {
  BrowserAuthError,
  BrowserAuthErrorCodes,
  CacheLookupPolicy,
  ClientAuthErrorCodes,
  EventType,
  InteractionRequiredAuthError,
  InteractionRequiredAuthErrorCodes,
  InteractionType,
  PublicClientApplication,
  createNestablePublicClientApplication,
  type AccountInfo,
  type AuthenticationResult,
  type EventMessage,
  type IPublicClientApplication,
} from '@azure/msal-browser';
import { app as teamsApp, nestedAppAuth } from '@microsoft/teams-js';
import { msalInstance, workforceAuthorityConfig, buildMsalConfiguration, popupRedirectUri } from './msal-config';
import { activeBffScope, BackendSelectionError, type ActiveBackend } from './backend-selection';
import { isUserImpersonationScope } from '../config';

/**
 * Deep-link preservation through the MSAL login/token redirect (task 012).
 *
 * The redirect navigates away to the IdP and returns to the app's redirectUri (the origin),
 * which loses the originally requested route (e.g. an emailed `/project/{id}` link). We carry
 * the intended in-app route across the redirect in a per-tab sessionStorage key — consistent
 * with the SPA's sessionStorage MSAL cache (ADR-028 exception) and deterministic to restore,
 * unlike reading MSAL `state` which depends on msal-react event/handleRedirectPromise timing.
 * This complements MSAL's built-in navigateToLoginRequestUrl restore (it no-ops when MSAL has
 * already landed the user on the intended route).
 */
const RETURN_TO_KEY = 'spaarke.ext.returnTo';

/** Capture the current in-app route so it survives a login/token redirect. Best-effort. */
export function captureReturnTo(): void {
  try {
    sessionStorage.setItem(RETURN_TO_KEY, window.location.pathname + window.location.search);
  } catch {
    // sessionStorage unavailable (private mode / blocked) — deep-link restore degrades gracefully.
  }
}

/** Consume the captured return-to route once, if present AND a safe in-app relative path. */
export function consumeReturnTo(): string | null {
  let raw: string | null = null;
  try {
    raw = sessionStorage.getItem(RETURN_TO_KEY);
    if (raw !== null) sessionStorage.removeItem(RETURN_TO_KEY);
  } catch {
    return null;
  }
  return safeRelativePath(raw);
}

/**
 * Only in-app root-relative paths are restorable. Guards against an open-redirect via an
 * absolute (`http://evil`) or protocol-relative (`//evil`) URL smuggled into the return route.
 */
export function safeRelativePath(path: string | null | undefined): string | null {
  if (!path) return null;
  if (!path.startsWith('/')) return null; // must be root-relative
  if (path.startsWith('//')) return null; // reject protocol-relative
  if (path.includes('\\')) return null; // reject backslash tricks
  if (path.includes('://')) return null; // reject embedded scheme
  return path;
}

/**
 * Capture a Teams-provided deep-link sub-path (task 012) using the SAME sessionStorage key +
 * safety validation as `captureReturnTo()` above, so AuthGuard's existing post-auth restore effect
 * (`consumeReturnTo`) lands the Teams user on the intended in-app route with zero AuthGuard changes
 * — one restore mechanism shared by both hosts. `subPageId` (Teams `context.page.subPageId`) is
 * host-supplied and not guaranteed to be a root-relative in-app path (it is opaque unless a future
 * "Open in Teams" deep-link generator encodes one) — `safeRelativePath` rejects anything else and
 * this function no-ops silently in that case.
 */
export function captureTeamsDeepLink(subPageId: string | undefined | null): void {
  if (!subPageId) return;
  const candidate = subPageId.startsWith('/') ? subPageId : `/${subPageId}`;
  const path = safeRelativePath(candidate);
  if (!path) return;
  try {
    sessionStorage.setItem(RETURN_TO_KEY, path);
  } catch {
    // sessionStorage unavailable — deep-link restore degrades gracefully, same as captureReturnTo.
  }
}

/**
 * Options only the workforce browser plane supplies (standalone-plane.ts). The CIAM plane passes none,
 * so the token REQUEST it sends is unchanged from before task 040 (`{ scopes, account }`, no redirect URI,
 * no cache policy). Its error routing differs in one deliberate way: a `timed_out` silent renewal now
 * falls back to the interactive redirect (see {@link isInteractionNeeded}).
 */
export interface StandaloneAcquireOptions {
  /**
   * Spaarke's tenant id. When set, only accounts whose tenant profile is this tenant are considered:
   * MSAL's account list is not client-scoped, and both browser instances share the tab's sessionStorage,
   * so without the filter a cached CIAM account would be treated as a workforce sign-in.
   */
  tenantId?: string;
  /**
   * Redirect URI (`{origin}/auth-callback.html`, the page that runs MSAL's redirect bridge) for the
   * silent IFRAME retry only. The first silent attempt never carries a redirect URI: msal-browser
   * forwards an explicitly provided one into the refresh-token grant body (SilentRefreshClient ->
   * RefreshTokenClient `redirect_uri`), so supplying it on every silent call would change the wire request.
   */
  silentRedirectUri?: string;
  /**
   * Skip the cached ACCESS token and redeem the refresh token for a new one (`forceRefresh` on the silent request).
   * Set ONLY by bff-client.ts's 401 retry: the BFF just refused the token that is still in the cache, so
   * returning it again would repeat the 401 (a sign-in loop). Absent, every request is shaped exactly as before.
   */
  forceRefresh?: boolean;
}

/** The accounts of one instance, optionally scoped to a tenant (case-insensitive GUID). */
function accountsFor(instance: IPublicClientApplication, tenantId?: string): AccountInfo[] {
  return tenantId ? instance.getAllAccounts({ tenantId: tenantId.toLowerCase() }) : instance.getAllAccounts();
}

/**
 * Instances that already carry the active-account callback (the CIAM singleton survives a plane re-resolve, so the
 * bootstrap can offer it more than once).
 */
const activeAccountSyncRegistered = new WeakSet<object>();

/**
 * Make the account that just completed an interactive sign-in the instance's ACTIVE account.
 *
 * `acquireStandaloneBffToken` prefers the active account over the oldest cached one. Without this, picking a different
 * identity on the login redirect (BffAccessState's "Sign in again", `prompt: 'select_account'`) would add a second
 * cached account that nothing ever selects, and the SPA would keep acquiring tokens for the refused first one.
 *
 * Call once per standalone instance BEFORE `initialize()` / `<MsalProvider>`, since the redirect response is handled
 * during start-up. Only LOGIN_SUCCESS and ACQUIRE_TOKEN_SUCCESS from an interactive (redirect or popup) call count: a
 * silent renewal must not move the active account. Idempotent per instance.
 */
export function syncActiveAccountOnInteractiveSignIn(instance: IPublicClientApplication): void {
  if (activeAccountSyncRegistered.has(instance)) return;
  activeAccountSyncRegistered.add(instance);
  instance.addEventCallback((event: EventMessage) => {
    // Same single callback also records a FAILED redirect sign-in (see "Redirect sign-in failure" below).
    if (isRedirectSignInFailure(event)) {
      setSignInFailure(safeSignInErrorCode(event.error));
      return;
    }
    const interactive =
      event.interactionType === InteractionType.Redirect || event.interactionType === InteractionType.Popup;
    const success = event.eventType === EventType.LOGIN_SUCCESS || event.eventType === EventType.ACQUIRE_TOKEN_SUCCESS;
    if (!interactive || !success) return;
    setSignInFailure(null);
    const account = (event.payload as AuthenticationResult | null)?.account;
    if (account) instance.setActiveAccount(account);
  });
}

// ---------------------------------------------------------------------------
// Redirect sign-in failure (task 056 review fix)
// ---------------------------------------------------------------------------
//
// When Entra returns an error to the redirect URI (`#error=access_denied`, e.g. AADSTS65004 for a new guest who declines
// the "Review permissions" page), msal-browser rejects `handleRedirectPromise` and emits ACQUIRE_TOKEN_FAILURE with
// InteractionType.Redirect (msal-browser 5.x has no separate LOGIN_FAILURE event). The account gate used to re-fire
// `loginRedirect` whenever there was no account and nothing in progress, so a declined sign-in redirected to Entra
// again, forever. The failure is therefore recorded here (the callback above is registered BEFORE `initialize()`, so it sees the response) and the gate shows a
// fail-loud screen instead; only the user's "Try again" starts another redirect.
//
// Only a SAFE error code is kept: the MSAL `errorCode` (a fixed snake_case identifier) and an `AADSTS` number. The raw
// description is never stored or shown — it can carry a tenant id, a correlation id or an address.
// Teams is not affected: it never redirects (the Teams bootstrap acquires the token via NAA / popup before <App> mounts).

/** What the failure screen may show. */
export interface SignInFailure {
  /** MSAL / OIDC error code, e.g. `access_denied`; `unknown_error` when none is recognisable. */
  errorCode: string;
  /** Entra error number from the description, e.g. `AADSTS65004`; null when there is none. */
  aadsts: string | null;
}

let signInFailure: SignInFailure | null = null;
const signInFailureListeners = new Set<() => void>();

function setSignInFailure(next: SignInFailure | null): void {
  signInFailure = next;
  signInFailureListeners.forEach(listener => listener());
}

/** The recorded failed redirect sign-in, or null. */
export function getSignInFailure(): SignInFailure | null {
  return signInFailure;
}

/** Forget the failure (before another sign-in attempt, or when the user picks a different sign-in). */
export function clearSignInFailure(): void {
  setSignInFailure(null);
}

/** `useSyncExternalStore` subscription. */
export function subscribeSignInFailure(listener: () => void): () => void {
  signInFailureListeners.add(listener);
  return () => {
    signInFailureListeners.delete(listener);
  };
}

/**
 * A redirect sign-in that FAILED: ACQUIRE_TOKEN_FAILURE of a redirect interaction (msal-browser 5.5 emits it for a
 * rejected `handleRedirectPromise`, and for a redirect that could not start). `interaction_in_progress` is not a failure of the
 * sign-in: it means another redirect is already running (e.g. a development double effect), so it is ignored.
 */
function isRedirectSignInFailure(event: EventMessage): boolean {
  if (event.interactionType !== InteractionType.Redirect) return false;
  if (event.eventType !== EventType.ACQUIRE_TOKEN_FAILURE) return false;
  return (event.error as { errorCode?: unknown } | null)?.errorCode !== BrowserAuthErrorCodes.interactionInProgress;
}

/** Reduce an MSAL / OIDC error to the identifiers that are safe to show. Exported for tests. */
export function safeSignInErrorCode(error: unknown): SignInFailure {
  const e = (error ?? {}) as { errorCode?: unknown; errorMessage?: unknown; message?: unknown };
  const code = typeof e.errorCode === 'string' && /^[A-Za-z0-9_]{1,64}$/.test(e.errorCode) ? e.errorCode : null;
  const text = `${typeof e.errorMessage === 'string' ? e.errorMessage : ''} ${typeof e.message === 'string' ? e.message : ''}`;
  const aadsts = text.match(/AADSTS\d{3,7}/)?.[0] ?? null;
  return { errorCode: code ?? 'unknown_error', aadsts };
}

/**
 * Which account a standalone acquisition uses: the instance's ACTIVE account when it is one of the candidates (the
 * tenant-filtered list), otherwise the oldest candidate. An active account outside the list (another tenant on the
 * workforce plane) is never used. With a single candidate there is nothing to choose.
 */
function chooseAccount(instance: IPublicClientApplication, candidates: AccountInfo[]): AccountInfo {
  if (candidates.length > 1) {
    const activeId = instance.getActiveAccount()?.homeAccountId;
    const active = activeId ? candidates.find(a => a.homeAccountId === activeId) : undefined;
    if (active) return active;
  }
  return candidates[0];
}

/**
 * Acquire a BFF access token from a standalone collaboration-host MSAL instance.
 *
 * Broker-only (A1/A2 invariant): the returned token is used ONLY to call the BFF as a
 * Bearer credential — it is NEVER exchanged for a downstream Graph/SPE/Dataverse token (no
 * OBO on the collaboration path). Parameterized by instance + scope so the SAME acquisition
 * path serves every authority — the CIAM SPA today (via `acquireBffToken`), the workforce
 * browser plane (via standalone-plane.ts) — without forking the module.
 *
 * Strategy:
 *   1. Try silent acquisition (MSAL cache / refresh token). CIAM: the same `{ scopes, account }` request as before.
 *      Workforce (`options.silentRedirectUri` set): the same request with
 *      `CacheLookupPolicy.AccessTokenAndRefreshToken` and NO redirect URI — msal never opens an iframe for it.
 *   2. Workforce only: if that fails with an error msal itself would resolve by iframe (expired / invalid
 *      refresh token, no tokens found), retry once with `CacheLookupPolicy.Skip` and
 *      `redirectUri: options.silentRedirectUri` — a hidden iframe answered by the bridge page.
 *   3. On InteractionRequiredAuthError or a timed-out silent iframe renewal, trigger a redirect login.
 *   4. Throw after initiating redirect so callers abort the current request.
 *
 * MSAL handles token caching internally — no manual cache management needed.
 *
 * @param instance The standalone `PublicClientApplication` (from msal-config.ts) to acquire against.
 * @param bffScope The BFF API OAuth scope for the instance's authority.
 * @param options Workforce-browser-only options (tenant account filter, iframe redirect URI).
 * @returns Access token string, ready for `Authorization: Bearer {token}` header.
 * @throws If silent acquisition fails for a non-interaction reason (network error, etc.).
 */
export async function acquireStandaloneBffToken(
  instance: IPublicClientApplication,
  bffScope: string,
  options: StandaloneAcquireOptions = {}
): Promise<string> {
  const accounts = accountsFor(instance, options.tenantId);

  if (accounts.length === 0) {
    // No authenticated account — trigger login redirect (preserving the current route)
    captureReturnTo();
    await instance.acquireTokenRedirect({ scopes: [bffScope] });
    // acquireTokenRedirect navigates away; this throw aborts the current call chain
    throw new Error('No authenticated account — redirecting to login');
  }

  const account = chooseAccount(instance, accounts);

  try {
    return await acquireSilent(instance, bffScope, account, options.silentRedirectUri, options.forceRefresh === true);
  } catch (err) {
    if (isInteractionNeeded(err)) {
      // Token expired, consent needed, MFA required, silent renewal timed out, etc. — fall back to
      // the interactive redirect (preserving the route).
      captureReturnTo();
      await instance.acquireTokenRedirect({
        scopes: [bffScope],
        account,
      });
      throw new Error('Interaction required — redirecting to login');
    }
    throw err;
  }
}

/**
 * Silent acquisition. Without `silentRedirectUri` (CIAM): a single `acquireTokenSilent({ scopes, account })`.
 * With it (workforce browser plane): a refresh-token attempt with no redirect URI, then — only for an
 * error msal-browser treats as silently resolvable — one `Skip` retry through the redirect-bridge iframe.
 */
async function acquireSilent(
  instance: IPublicClientApplication,
  bffScope: string,
  account: AccountInfo,
  silentRedirectUri?: string,
  forceRefresh = false
): Promise<string> {
  // `forceRefresh` is added to the request only when asked for, so the 040 request shapes are otherwise unchanged.
  const refresh = forceRefresh ? { forceRefresh: true } : {};
  if (!silentRedirectUri) {
    const result = await instance.acquireTokenSilent({ scopes: [bffScope], account, ...refresh });
    return result.accessToken;
  }

  try {
    const result = await instance.acquireTokenSilent({
      scopes: [bffScope],
      account,
      cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
      ...refresh,
    });
    return result.accessToken;
  } catch (err) {
    if (!isSilentlyResolvableByIframe(err)) throw err;
  }

  // msal-browser v5 completes a silent iframe renewal only when the redirect page runs MSAL's redirect
  // bridge; the SPA's own index page does not, so the iframe lands on public/auth-callback.html.
  const result = await instance.acquireTokenSilent({
    scopes: [bffScope],
    account,
    cacheLookupPolicy: CacheLookupPolicy.Skip,
    redirectUri: silentRedirectUri,
  });
  return result.accessToken;
}

/**
 * Mirrors msal-browser 5.5.0 `checkIfRefreshTokenErrorCanBeResolvedSilently` (StandardController): the
 * errors for which the default policy would have opened a silent iframe — an expired / invalid /
 * missing refresh token (`invalid_grant`, `token_refresh_required`, `refresh_token_expired`,
 * `no_tokens_found`; a `bad_token` interaction error counts as silently resolvable, other
 * InteractionRequired errors do not). Everything else goes to the interactive fallback or rethrows.
 */
function isSilentlyResolvableByIframe(err: unknown): boolean {
  if (!(err instanceof Error)) return false;
  const code = (err as { errorCode?: string }).errorCode;
  const subError = (err as { subError?: string }).subError;
  const interactionRequired =
    err instanceof InteractionRequiredAuthError && subError !== InteractionRequiredAuthErrorCodes.badToken;
  const refreshRequired = code === 'invalid_grant' || code === ClientAuthErrorCodes.tokenRefreshRequired;
  return (
    (!interactionRequired && refreshRequired) ||
    code === InteractionRequiredAuthErrorCodes.noTokensFound ||
    code === InteractionRequiredAuthErrorCodes.refreshTokenExpired
  );
}

/**
 * True when a silent acquisition failure should fall back to the interactive path: MSAL says
 * interaction is required, or the silent iframe renewal timed out (`timed_out` — the redirect bridge
 * never answered, e.g. `/auth-callback.html` is not yet registered as a redirect URI on the client).
 * Everything else (network, config) still rethrows.
 */
function isInteractionNeeded(err: unknown): boolean {
  if (err instanceof InteractionRequiredAuthError) return true;
  return err instanceof BrowserAuthError && err.errorCode === BrowserAuthErrorCodes.timedOut;
}

/**
 * Acquire an access token for the BFF API — external SPA (CIAM) path.
 *
 * Delegates to `acquireStandaloneBffToken` with the CIAM `msalInstance` singleton, preserving the prior request
 * shape (FR-15 — no CIAM regression: the silent request is unchanged; the one deliberate routing change is that a
 * `timed_out` silent renewal now falls back to the redirect). See `acquireStandaloneBffToken`.
 *
 * Scope (task 041, FR-17; ADR-028 A7): the active CIAM backend's `api://{customer BFF app id}/user_impersonation`
 * (`activeBffScope('ciam')`, which throws — no token — when the active backend belongs to the workforce plane or none
 * can be resolved). Deliberately argument-free: pages hand this function to libraries as a token callback, so no
 * caller-supplied argument can become a scope.
 *
 * @returns Access token string, ready for `Authorization: Bearer {token}` header.
 */
export async function acquireBffToken(): Promise<string> {
  return acquireCiamBffToken();
}

/** The default (CIAM) acquirer: `scope` is the active backend's, passed by bff-client.ts; else the active CIAM scope. */
function acquireCiamBffToken(scope?: string, options?: BffTokenAcquireOptions): Promise<string> {
  return options?.forceRefresh
    ? acquireStandaloneBffToken(msalInstance, scope ?? activeBffScope('ciam'), { forceRefresh: true })
    : acquireStandaloneBffToken(msalInstance, scope ?? activeBffScope('ciam'));
}

/**
 * Pluggable "which strategy is bff-client.ts currently using" seam (task 012 host-detection wiring).
 *
 * `bff-client.ts` calls `acquireActiveBffToken()` for every BFF request instead of hard-coding
 * `acquireBffToken`. The default remains the CIAM strategy (unchanged request shape —
 * FR-15 no-CIAM-regression: a build that never runs inside Teams never calls `setActiveBffTokenAcquirer`
 * and this seam behaves as before). The Teams host adapter is the ONLY caller that ever
 * switches it, and it does so exactly once at bootstrap by re-pointing this seam at
 * `acquireTeamsWorkforceBffToken` — no acquisition LOGIC lives outside this file (ADR-028 A2: "the
 * adapter selects the strategy; it must NOT implement its own auth logic").
 */
export type BffTokenAcquirer = (scope?: string, options?: BffTokenAcquireOptions) => Promise<string>;

/**
 * Per-call options of the acquirer seam. `forceRefresh` is passed ONLY by bff-client.ts's / gridDataverseClient.ts's
 * single retry after a 401 (the cached token was just refused, so a cache hit would repeat the refusal); the first
 * attempt of every request calls the acquirer with the scope alone, exactly as before.
 */
export interface BffTokenAcquireOptions {
  forceRefresh?: boolean;
}

let activeBffTokenAcquirer: BffTokenAcquirer = acquireCiamBffToken;

/** Host adapter seam: select which strategy `acquireActiveBffToken` delegates to. */
export function setActiveBffTokenAcquirer(acquirer: BffTokenAcquirer): void {
  activeBffTokenAcquirer = acquirer;
}

/**
 * Acquire a BFF token via whichever strategy is currently active (CIAM by default). `backend` is the active-backend
 * snapshot bff-client.ts / gridDataverseClient.ts read ONCE for the request whose URL they build (task 041), so the
 * token and the host always belong to one backend; without it each strategy uses its plane's active backend scope.
 *
 * The seam takes the backend record, never a bare scope string, and refuses (rejects, no token request) a record whose
 * scope is not `api://{app id}/user_impersonation` (ADR-028 A7) — so no caller can turn an arbitrary string into a
 * scope for `acquireTokenSilent` / NAA.
 */
export function acquireActiveBffToken(
  backend?: Pick<ActiveBackend, 'bffScope' | 'plane'>,
  options?: BffTokenAcquireOptions
): Promise<string> {
  if (backend === undefined) {
    return options?.forceRefresh
      ? activeBffTokenAcquirer(undefined, { forceRefresh: true })
      : activeBffTokenAcquirer(undefined);
  }
  const scope = typeof backend === 'object' && backend !== null ? backend.bffScope : undefined;
  if (!isUserImpersonationScope(scope)) {
    return Promise.reject(
      new BackendSelectionError(
        '[BackendSelection] The backend scope is not api://{app id}/user_impersonation; no token is requested.'
      )
    );
  }
  // The options argument is passed only when set, so the first-attempt call shape is `acquirer(scope)` as before.
  return options?.forceRefresh ? activeBffTokenAcquirer(scope, { forceRefresh: true }) : activeBffTokenAcquirer(scope);
}

/**
 * Active login SCOPE seam (task 013 dual-plane browser bootstrap).
 *
 * `AuthGuard` triggers the browser sign-in redirect (`loginRedirect({ scopes: [...] })`) and MUST
 * request the BFF scope for the CURRENTLY-SELECTED plane's authority — a CIAM instance cannot
 * mint a token for the workforce BFF resource and vice-versa. The default is the CIAM BFF scope,
 * so the CIAM browser path's login request is unchanged (FR-15): a build that only ever runs CIAM
 * never calls `setActiveLoginScope`. The standalone workforce browser plane (home-realm discovery →
 * "My organization") re-points this seam at the workforce BFF scope exactly once at bootstrap,
 * mirroring how `setActiveBffTokenAcquirer` re-points the acquirer. The Teams host never uses this
 * (it authenticates during bootstrap; AuthGuard's redirect branch is never reached inside Teams).
 */
let activeLoginScope: string | null = null;

/** Bootstrap seam: select which BFF scope `AuthGuard` requests on the sign-in redirect. */
export function setActiveLoginScope(scope: string): void {
  activeLoginScope = scope;
}

/**
 * The BFF scope for the currently-selected browser plane's authority. Defaults to the active CIAM backend's scope
 * (task 041: the run-time backend selection, never a link).
 */
export function getActiveLoginScope(): string {
  return activeLoginScope ?? activeBffScope('ciam');
}

/**
 * Active account TENANT seam (same shape as the login-scope seam). Null for CIAM (every cached account
 * belongs to the CIAM instance's authority, behaviour unchanged). The workforce browser plane sets
 * Spaarke's tenant id so a CIAM account cached in the same tab's sessionStorage is never treated as a
 * workforce sign-in by `AuthGuard` or the portal-user lookup.
 */
let activeAccountTenantId: string | null = null;

export function setActiveAccountTenantId(tenantId: string | null): void {
  activeAccountTenantId = tenantId;
}

/** The accounts that belong to the currently-selected browser plane (all of them for CIAM). */
export function accountsForActivePlane<T extends { tenantId?: string }>(accounts: readonly T[]): T[] {
  if (!activeAccountTenantId) return [...accounts];
  const tenant = activeAccountTenantId.toLowerCase();
  return accounts.filter(a => a.tenantId?.toLowerCase() === tenant);
}

/* ─────────────────────────────────────────────────────────────────────────────────────────
 * Teams collaboration host — workforce NAA, then MSAL popup (ADR-028 A7; task 040 / FR-16)
 * ───────────────────────────────────────────────────────────────────────────────────────────
 *
 * A SECOND acquisition strategy inside this SAME shared module — the Teams host — added WITHOUT
 * forking the module or re-adding a CIAM path. It reuses the pluggable-authority builders
 * (`workforceAuthorityConfig` + `buildMsalConfiguration`) against the WORKFORCE authority: the
 * single-tenant Spaarke client, pinned to `https://login.microsoftonline.com/{Spaarke tenant id}`,
 * requesting `api://{customer BFF app id}/user_impersonation`. The CIAM path above is untouched (FR-15).
 * The host adapter decides WHEN this strategy runs; this module only makes it available + correct.
 *
 * Acquisition order — the Office add-in's working shape (`OfficeNaaStrategy`):
 *   1. NAA: a nestable MSAL instance brokered by the Teams host (silent → ssoSilent → brokered popup).
 *   2. If NAA fails: a standard `PublicClientApplication` for the same client, tenant and scope, run
 *      through `OfficeNaaStrategy._tryAcquireWith`'s ladder — `acquireTokenSilent` with a cached
 *      account, then `acquireTokenPopup`. The popup lands on `{origin}/auth-callback.html`, the
 *      msal-browser v5 redirect-bridge page.
 *   3. If both fail: `TeamsWorkforceAuthError` carrying `{ naaError, popupError }`.
 *
 * Invariants (ADR-028 A2 as amended by A7 / NFR-02 / NFR-04):
 *   - Broker-only (NFR-02): the token authenticates to the BFF ONLY — never exchanged downstream
 *     (no OBO on the collaboration path).
 *   - Non-redirect (NFR-04): NAA → popup — NEVER `acquireTokenRedirect`. A full-page redirect inside
 *     the Teams iframe breaks framing. This is why the Teams path does NOT reuse
 *     `acquireStandaloneBffToken` (whose interaction fallback is a redirect — correct for the
 *     standalone SPA, wrong inside Teams).
 *   - No Teams SSO (A7 MUST NOT): `authentication.getAuthToken` is never called, and the Teams
 *     manifest declares no `webApplicationInfo`. A Teams-SSO token's audience would be the one
 *     resource the manifest names — wrong for a per-customer backend.
 *   - Tenant-pinned (A7 MUST NOT `/organizations`/`/common`): a guest signed in through Spaarke's
 *     tenant gets the guest `oid` its `systemuser` is keyed by; `/organizations` would sign the guest
 *     into their home tenant (different `oid`, no `systemuser` match). `buildMsalConfiguration` refuses
 *     any other workforce authority and any scope other than `…/user_impersonation`.
 *   - Fail loud: an unauthenticated / consent-missing caller throws `TeamsWorkforceAuthError` — the
 *     failure is surfaced, never silently degraded or retried into a broken state.
 */

/** Config the Teams host supplies to select the workforce strategy (values from `getWorkforceEnvConfig()`). */
export interface TeamsWorkforceAuthConfig {
  /** The single-tenant Spaarke workforce public client id (A7). */
  clientId: string;
  /** Spaarke's tenant id (a GUID) — the authority is `https://login.microsoftonline.com/{tenantId}`. */
  tenantId: string;
  /** `api://{customer BFF app id}/user_impersonation` (broker-only — used only to call the BFF). */
  bffScope: string;
}

/**
 * Raised when the Teams workforce strategy cannot yield a BFF-valid token (NAA and the MSAL popup
 * fallback both failed — e.g. a blocked or cancelled popup, missing consent — or not inside a Teams
 * host). The caller MUST surface this as a sign-in/consent error (fail loud).
 */
export class TeamsWorkforceAuthError extends Error {
  constructor(
    message: string,
    /** The underlying error(s) — e.g. `{ naaError, popupError }` — preserved for diagnostics. */
    public readonly cause?: unknown
  ) {
    super(message);
    this.name = 'TeamsWorkforceAuthError';
  }
}

/**
 * Idempotent Teams JS `app.initialize()`. The host bootstrap (task 012) may also initialize Teams;
 * caching the promise makes repeated calls a no-op and keeps this strategy self-contained.
 */
let teamsInitPromise: Promise<void> | null = null;
function ensureTeamsInitialized(): Promise<void> {
  if (!teamsInitPromise) {
    teamsInitPromise = teamsApp.initialize();
  }
  return teamsInitPromise;
}

/**
 * MSAL `Configuration` for both Teams instances: the tenant-pinned workforce authority, and the
 * redirect-bridge page as redirect URI. Under a live NAA bridge the redirect URI is not used (the
 * broker owns the exchange); when the host offers no bridge, `createNestablePublicClientApplication`
 * returns a standard instance, and its silent iframe and popup then land on the bridge page.
 */
function teamsWorkforceMsalConfiguration(cfg: TeamsWorkforceAuthConfig) {
  const authorityConfig = workforceAuthorityConfig({
    clientId: cfg.clientId,
    tenantId: cfg.tenantId,
    bffScope: cfg.bffScope,
  });
  return buildMsalConfiguration(authorityConfig, { redirectUri: popupRedirectUri() });
}

/**
 * Build the Nested App Auth (NAA) MSAL instance for the Teams host against the Spaarke-tenant workforce
 * authority. Unlike the standalone SPA's `new PublicClientApplication`, NAA runs MSAL NESTED in the
 * Teams host broker (no redirect) — the broker supplies the signed-in workforce account, so token
 * acquisition is silent for a consented user. Supplies no CIAM values (workforce plane only).
 */
export async function createTeamsWorkforceMsalInstance(
  cfg: TeamsWorkforceAuthConfig
): Promise<IPublicClientApplication> {
  return createNestablePublicClientApplication(teamsWorkforceMsalConfiguration(cfg));
}

/**
 * Memoized Teams workforce NAA instance (task 012). A fresh `PublicClientApplication` has an empty
 * MSAL cache, so re-creating one on every call would force a full broker round-trip on every single
 * BFF request instead of hitting `acquireTokenSilent`'s cache after the first. Keyed by
 * clientId+tenantId so a config change safely rebuilds it.
 */
let teamsWorkforceInstancePromise: Promise<IPublicClientApplication> | null = null;
let teamsWorkforceInstanceCacheKey: string | null = null;

export async function getTeamsWorkforceMsalInstance(cfg: TeamsWorkforceAuthConfig): Promise<IPublicClientApplication> {
  const key = `${cfg.clientId}|${cfg.tenantId}`;
  if (!teamsWorkforceInstancePromise || teamsWorkforceInstanceCacheKey !== key) {
    teamsWorkforceInstanceCacheKey = key;
    teamsWorkforceInstancePromise = createTeamsWorkforceMsalInstance(cfg);
  }
  return teamsWorkforceInstancePromise;
}

/**
 * Memoized standard (non-nested) instance for the popup fallback — the counterpart of
 * `OfficeNaaStrategy`'s fallback `PublicClientApplication`. Same client, tenant, scope, per-tab
 * sessionStorage cache and bridge-page redirect URI as the NAA instance. A failed creation is not
 * memoized, so a later call can retry it.
 */
let teamsWorkforcePopupInstancePromise: Promise<IPublicClientApplication> | null = null;
let teamsWorkforcePopupInstanceCacheKey: string | null = null;

export async function getTeamsWorkforcePopupMsalInstance(
  cfg: TeamsWorkforceAuthConfig
): Promise<IPublicClientApplication> {
  const key = `${cfg.clientId}|${cfg.tenantId}`;
  if (!teamsWorkforcePopupInstancePromise || teamsWorkforcePopupInstanceCacheKey !== key) {
    teamsWorkforcePopupInstanceCacheKey = key;
    const created = (async () => {
      const pca = new PublicClientApplication(teamsWorkforceMsalConfiguration(cfg));
      await pca.initialize();
      return pca as IPublicClientApplication;
    })();
    teamsWorkforcePopupInstancePromise = created;
    created.catch(() => {
      if (teamsWorkforcePopupInstancePromise === created) {
        teamsWorkforcePopupInstancePromise = null;
        teamsWorkforcePopupInstanceCacheKey = null;
      }
    });
  }
  return teamsWorkforcePopupInstancePromise;
}

/**
 * Which Teams path last yielded a token, and the instance it used. Once the popup fallback has
 * signed the user in, later calls go straight to that instance's silent cache instead of failing NAA
 * again on every BFF request. A failed popup path resets this, so the next call tries NAA first again.
 */
let teamsWorkforcePath: 'naa' | 'popup' | null = null;
let teamsWorkforceSignedInInstance: IPublicClientApplication | null = null;

/**
 * The MSAL instance that holds the signed-in Teams workforce account (the NAA instance, or the popup
 * fallback instance), or null before the first successful acquisition. The host adapter hands it to
 * `<MsalProvider>` so `useMsal()` reflects the signed-in user whichever path signed them in.
 */
export function getTeamsWorkforceSignedInInstance(): IPublicClientApplication | null {
  return teamsWorkforceSignedInInstance;
}

/**
 * Acquire the BFF token via NAA: silent (cached account) → `ssoSilent` (broker account bootstrap) →
 * `acquireTokenPopup` on `InteractionRequiredAuthError`. The popup is brokered by the Teams host, so
 * a consented workforce user sees no visible second login. NEVER redirects (NFR-04).
 */
async function acquireBffTokenViaNaa(
  instance: IPublicClientApplication,
  bffScope: string,
  forceRefresh = false
): Promise<string> {
  const scopes = [bffScope];
  const account = instance.getActiveAccount() ?? instance.getAllAccounts()[0] ?? null;
  try {
    const result = account
      ? await instance.acquireTokenSilent({ scopes, account, ...(forceRefresh ? { forceRefresh: true } : {}) })
      : await instance.ssoSilent({ scopes });
    // Establish the active account once so useMsal()/useIsAuthenticated() (host adapter's
    // MsalProvider) reflect the signed-in user immediately, and so the NEXT call above takes the
    // fast acquireTokenSilent(account) branch instead of ssoSilent again.
    if (!instance.getActiveAccount() && result.account) {
      instance.setActiveAccount(result.account);
    }
    return result.accessToken;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      // Broker popup — Teams resolves this without a visible second login for a consented user.
      const result = await instance.acquireTokenPopup({ scopes });
      if (!instance.getActiveAccount() && result.account) {
        instance.setActiveAccount(result.account);
      }
      return result.accessToken;
    }
    throw err;
  }
}

/**
 * The popup fallback ladder, mirroring `OfficeNaaStrategy._tryAcquireWith`: `acquireTokenSilent` with a
 * cached account (a failure falls through), then `acquireTokenPopup` with the account as `loginHint`
 * (or `prompt: 'select_account'` when there is none). Throws if the popup fails or yields no token.
 *
 * `trySilentFirst` is false on the user-gesture (click) path: a silent attempt can spend up to ~10 s in a
 * hidden iframe, and browsers only honour `window.open` within a few seconds of the click (transient user
 * activation), so the click path goes straight to the popup.
 */
async function acquireBffTokenViaPopup(
  instance: IPublicClientApplication,
  bffScope: string,
  trySilentFirst: boolean,
  forceRefresh = false
): Promise<string> {
  const scopes = [bffScope];
  const account = instance.getActiveAccount() ?? instance.getAllAccounts()[0] ?? null;

  if (account && trySilentFirst) {
    try {
      const result = await instance.acquireTokenSilent({
        scopes,
        account,
        ...(forceRefresh ? { forceRefresh: true } : {}),
      });
      if (result.accessToken) return result.accessToken;
    } catch {
      // Fall through to the popup, as OfficeNaaStrategy._tryAcquireWith does.
    }
  }

  const result = await instance.acquireTokenPopup({
    scopes,
    loginHint: account?.username,
    prompt: account ? undefined : 'select_account',
  });
  if (!result.accessToken) {
    throw new TeamsWorkforceAuthError('The MSAL popup returned no access token.');
  }
  if (!instance.getActiveAccount() && result.account) {
    instance.setActiveAccount(result.account);
  }
  return result.accessToken;
}

/**
 * True when the Teams host recommends the NAA channel (`nestedAppAuth.isNAAChannelRecommended()`). On a
 * bridge-less host `createNestablePublicClientApplication` returns a STANDARD instance, so the "NAA" step
 * would really be `ssoSilent` in a hidden iframe with a 10 s timeout — pure latency before the popup. If
 * the host cannot answer (the call throws), NAA is attempted as before.
 */
function isNaaChannelRecommended(): boolean {
  try {
    return nestedAppAuth.isNAAChannelRecommended();
  } catch {
    return true;
  }
}

/**
 * True when the user closed or dismissed the NAA sign-in — do not open a second popup. Two codes mean this:
 * `user_cancelled` (BrowserAuthError, a closed MSAL popup) and `user_canceled` (one `l`, ClientAuthError),
 * which is what `NestedAppAuthAdapter.fromBridgeError` maps `BridgeStatusCode.UserCancel` to when the user
 * dismisses the Teams-brokered NAA prompt (msal-browser 5.5.0).
 */
function isUserCancelled(err: unknown): boolean {
  const code = (err as { errorCode?: string } | null)?.errorCode;
  return code === BrowserAuthErrorCodes.userCancelled || code === ClientAuthErrorCodes.userCanceled;
}

/** The error a failed Teams workforce sign-in surfaces — the caller renders it (fail loud). */
function teamsWorkforceFailure(naaError: unknown, popupError: unknown): TeamsWorkforceAuthError {
  return new TeamsWorkforceAuthError(
    'Teams workforce token acquisition failed (NAA primary + MSAL popup fallback). ' +
      'The caller must surface a sign-in/consent error — do not silently degrade.',
    { naaError, popupError }
  );
}

/**
 * Acquire a BFF-valid workforce token inside the Teams host — the selectable strategy the host
 * adapter invokes. Primary: NAA (skipped when the host does not recommend the NAA channel). Fallback: the
 * MSAL popup on a standard instance for the same client (ADR-028 A7: NAA first, then the popup; never
 * Teams SSO) — except after the user cancelled NAA's own sign-in, when no second popup is opened. Both are
 * broker-only (NFR-02) and non-redirect (NFR-04). If both fail, throws `TeamsWorkforceAuthError` — the
 * caller surfaces a sign-in/consent error rather than degrading silently.
 *
 * The popup here runs without a user gesture (bootstrap); a blocked popup is recovered by the error
 * screen's "Sign in" button, which calls {@link signInTeamsWorkforceViaPopup} on click.
 *
 * @param cfg The single-tenant client id, Spaarke tenant id and `…/user_impersonation` BFF scope.
 * @returns Access token string, ready for `Authorization: Bearer {token}` against the BFF.
 * @throws {TeamsWorkforceAuthError} When not in a Teams host, or neither NAA nor the popup succeeds.
 */
export async function acquireTeamsWorkforceBffToken(
  cfg: TeamsWorkforceAuthConfig,
  options?: BffTokenAcquireOptions
): Promise<string> {
  const forceRefresh = options?.forceRefresh === true;
  try {
    await ensureTeamsInitialized();
  } catch (initError) {
    // Reset so a later retry (e.g. once the host is ready) can re-initialize.
    teamsInitPromise = null;
    throw new TeamsWorkforceAuthError('Teams JS app.initialize() failed — not running inside a Teams host.', initError);
  }

  let naaError: unknown = null;
  if (teamsWorkforcePath !== 'popup') {
    if (isNaaChannelRecommended()) {
      try {
        const instance = await getTeamsWorkforceMsalInstance(cfg);
        const token = await acquireBffTokenViaNaa(instance, cfg.bffScope, forceRefresh);
        teamsWorkforcePath = 'naa';
        teamsWorkforceSignedInInstance = instance;
        return token;
      } catch (err) {
        // NAA unavailable (no host bridge) or failed — fall through to the MSAL popup fallback.
        naaError = err;
      }
    } else {
      naaError = new Error('The Teams host does not recommend the NAA channel (isNAAChannelRecommended() is false).');
    }
    if (isUserCancelled(naaError)) {
      // The user dismissed the sign-in: a second popup straight away would be hostile. The error screen's
      // "Sign in" button lets them start it again on purpose.
      teamsWorkforcePath = null;
      prewarmTeamsWorkforcePopupInstance(cfg);
      throw teamsWorkforceFailure(naaError, null);
    }
  }

  try {
    // Bootstrap / renewal path: no user gesture to protect, so the silent attempt (a signed-in popup
    // account's cache) is worth trying first.
    return await signInViaPopupInstance(cfg, true, forceRefresh);
  } catch (popupError) {
    teamsWorkforcePath = null;
    prewarmTeamsWorkforcePopupInstance(cfg);
    throw teamsWorkforceFailure(naaError, popupError);
  }
}

/**
 * Create + initialise the standard popup instance ahead of time, so the error screen's "Sign in" click does
 * not spend its user-activation window awaiting `PublicClientApplication.initialize()` before
 * `acquireTokenPopup`. Best effort and idempotent (the instance is memoized; a failed creation is retried by
 * the click itself).
 */
function prewarmTeamsWorkforcePopupInstance(cfg: TeamsWorkforceAuthConfig): void {
  void getTeamsWorkforcePopupMsalInstance(cfg).catch(() => undefined);
}

async function signInViaPopupInstance(
  cfg: TeamsWorkforceAuthConfig,
  trySilentFirst: boolean,
  forceRefresh = false
): Promise<string> {
  const instance = await getTeamsWorkforcePopupMsalInstance(cfg);
  const token = await acquireBffTokenViaPopup(instance, cfg.bffScope, trySilentFirst, forceRefresh);
  teamsWorkforcePath = 'popup';
  teamsWorkforceSignedInInstance = instance;
  return token;
}

/**
 * The MSAL popup path for the Teams error screen's "Sign in" button (ADR-028 A7 fallback), so the popup opens
 * from a USER GESTURE (bootstrap cannot guarantee one; Teams webviews and browsers may block a gesture-less
 * `window.open`). Goes straight to `acquireTokenPopup` with no silent attempt first, which could outlast the
 * click's transient user activation and get the popup blocked. Records the popup instance as the signed-in
 * instance on success.
 */
export async function signInTeamsWorkforceViaPopup(cfg: TeamsWorkforceAuthConfig): Promise<string> {
  return signInViaPopupInstance(cfg, false);
}
