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
import {
  msalInstance,
  workforceAuthority,
  workforceAuthorityConfig,
  workforceHomeAuthorityConfig,
  workforceHomeTokenAuthority,
  buildMsalConfiguration,
  popupRedirectUri,
} from './msal-config';
import { activeBffScope, BackendSelectionError, type ActiveBackend } from './backend-selection';
import { MSAL_TENANT_ID, getWorkforceClientConfig, isTenantId, isUserImpersonationScope } from '../config';

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
 * Consume the captured return-to route ONLY when it is a safe in-app path AND `accept(path)` holds; otherwise leave it in
 * storage for its owner. For a consumer that wants one kind of captured route (task 055: the Teams host restores a captured
 * `?record=` deep link itself, because the account gate that normally restores it never runs inside Teams) without
 * swallowing a route that belongs to someone else.
 */
export function consumeReturnToWhere(accept: (path: string) => boolean): string | null {
  let raw: string | null = null;
  try {
    raw = sessionStorage.getItem(RETURN_TO_KEY);
  } catch {
    return null;
  }
  const path = safeRelativePath(raw);
  if (!path || !accept(path)) return null;
  try {
    sessionStorage.removeItem(RETURN_TO_KEY);
  } catch {
    // sessionStorage unavailable: the path is still returned once for this call.
  }
  return path;
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
 * Options for a standalone acquisition. The CIAM plane passes `accountFilter` (#2166), which is never sent to MSAL, and
 * `forceRefresh` / `noInteraction` only when set, so the token REQUEST it sends is unchanged from before task 040 (`{ scopes, account }`, no redirect URI,
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
  /**
   * BACKGROUND callers only (the notification bell's timer poll): when the token cannot be had silently, throw
   * {@link SignInRequiredError} instead of capturing the return-to and starting a redirect. A timer must never navigate
   * the page to Entra with no user action (a half-typed form or an open record would be lost). Records NO sign-in failure,
   * so a later user-initiated redirect is not blocked by it. Absent, behaviour is exactly as before.
   */
  noInteraction?: boolean;
  /**
   * Per-request authority (ADR-028 A9). The workforce HOME plane signs in on `organizations` but requests the BFF token
   * on `https://login.microsoftonline.com/{tid}`: it is added to the silent requests and to both interactive
   * redirects, only when set, so the CIAM requests and the Teams requests are unchanged.
   */
  authority?: string;
  /**
   * Only the accounts this predicate accepts are considered (applied after `tenantId`). The CIAM paths pass
   * {@link isCiamAccount}: MSAL's account list is shared by every client in the tab, so without it a company account
   * cached by the home plane was chosen for the CIAM token and msal-browser refused it with `authority_mismatch` (#2166).
   * Never sent to MSAL: the token request is unchanged.
   */
  accountFilter?: (account: { tenantId?: string }) => boolean;
}

/** The accounts of one instance, optionally scoped to a tenant (case-insensitive GUID) and a predicate. */
function accountsFor(
  instance: IPublicClientApplication,
  tenantId?: string,
  filter?: (account: { tenantId?: string }) => boolean
): AccountInfo[] {
  const accounts = tenantId ? instance.getAllAccounts({ tenantId: tenantId.toLowerCase() }) : instance.getAllAccounts();
  return filter ? accounts.filter(filter) : accounts;
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
      setSignInFailure(instance, safeSignInErrorCode(event.error));
      return;
    }
    const interactive =
      event.interactionType === InteractionType.Redirect || event.interactionType === InteractionType.Popup;
    const success = event.eventType === EventType.LOGIN_SUCCESS || event.eventType === EventType.ACQUIRE_TOKEN_SUCCESS;
    if (!interactive || !success) return;
    clearSignInFailure(instance);
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

// The failure belongs to the MSAL instance (= plane) whose redirect response produced it: each plane (CIAM / workforce) has
// its own instance, and the gate reads the failure for ITS instance only, so a failure on one plane never blocks the other.
let signInFailureState: { owner: IPublicClientApplication; failure: SignInFailure } | null = null;
const signInFailureListeners = new Set<() => void>();

function setSignInFailure(owner: IPublicClientApplication, failure: SignInFailure): void {
  signInFailureState = { owner, failure };
  signInFailureListeners.forEach(listener => listener());
}

/**
 * The recorded failed redirect sign-in, or null. Pass the MSAL instance the caller is bound to (the gate passes
 * `useMsal().instance`): a failure recorded for another instance is not returned. The result is referentially stable
 * until the failure changes (a `useSyncExternalStore` snapshot).
 */
export function getSignInFailure(instance?: IPublicClientApplication): SignInFailure | null {
  if (!signInFailureState) return null;
  return instance === undefined || signInFailureState.owner === instance ? signInFailureState.failure : null;
}

/**
 * Forget the failure: on a successful interactive sign-in or a successful token acquisition (below), for the instance
 * that succeeded (no argument: whichever instance holds it). A reload also clears it (module state), so a reload is a
 * fresh attempt. The gate does NOT clear it for a signed-in user: a failed token redirect is recorded precisely so that
 * `acquireStandaloneBffToken` does not start the same redirect again.
 */
export function clearSignInFailure(instance?: IPublicClientApplication): void {
  if (!signInFailureState) return;
  if (instance !== undefined && signInFailureState.owner !== instance) return;
  signInFailureState = null;
  signInFailureListeners.forEach(listener => listener());
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
 * A sign-in redirect for this instance already FAILED in this page load (the user cancelled the prompt or MFA, declined
 * consent, or Entra returned an error), so no further redirect is started automatically. Surfaced to the pages like a
 * 401: `accessFailureOf` maps it to the `signin` state, whose "Sign in again" button (a user gesture) starts the next
 * redirect. Without this, every BFF call after a failed token redirect started the same redirect again, indefinitely.
 * Carries the safe error code only.
 */
export class SignInRequiredError extends Error {
  constructor(public readonly failure: SignInFailure) {
    super(`Sign-in did not complete (${failure.errorCode}); sign in again to continue.`);
    this.name = 'SignInRequiredError';
  }
}

/**
 * The failure a `noInteraction` acquisition reports: nothing failed and nothing is recorded; the token simply needs an
 * interactive sign-in that a background caller must not start.
 */
const NO_INTERACTION_FAILURE: SignInFailure = { errorCode: 'interaction_required', aadsts: null };

/** Throws {@link SignInRequiredError} when a redirect for this instance already failed (see there). */
function assertRedirectAllowed(instance: IPublicClientApplication): void {
  const failure = getSignInFailure(instance);
  if (failure) throw new SignInRequiredError(failure);
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
 *   3. On InteractionRequiredAuthError or a timed-out silent iframe renewal, trigger a redirect login — unless a
 *      redirect for this instance already failed in this page load: then throw `SignInRequiredError` (no redirect).
 *   4. Throw after initiating redirect so callers abort the current request.
 *   `options.noInteraction` (background callers): steps 3-4 are replaced by a `SignInRequiredError`; no redirect.
 *
 * MSAL handles token caching internally — no manual cache management needed.
 *
 * @param instance The standalone `PublicClientApplication` (from msal-config.ts) to acquire against.
 * @param bffScope The BFF API OAuth scope for the instance's authority.
 * @param options Account scoping (CIAM: `accountFilter` only; workforce: tenant, iframe redirect URI, authority).
 * @returns Access token string, ready for `Authorization: Bearer {token}` header.
 * @throws If silent acquisition fails for a non-interaction reason (network error, etc.).
 */
export async function acquireStandaloneBffToken(
  instance: IPublicClientApplication,
  bffScope: string,
  options: StandaloneAcquireOptions = {}
): Promise<string> {
  const accounts = accountsFor(instance, options.tenantId, options.accountFilter);
  // The home plane's per-request authority (A9), added to a request only when set.
  const authority = options.authority ? { authority: options.authority } : {};

  if (accounts.length === 0) {
    // A background caller never starts a sign-in (and records nothing).
    if (options.noInteraction) throw new SignInRequiredError(NO_INTERACTION_FAILURE);
    // No authenticated account — trigger login redirect (preserving the current route), unless one already failed
    assertRedirectAllowed(instance);
    captureReturnTo();
    await instance.acquireTokenRedirect({ scopes: [bffScope], ...authority });
    // acquireTokenRedirect navigates away; this throw aborts the current call chain
    throw new Error('No authenticated account — redirecting to login');
  }

  const account = chooseAccount(instance, accounts);

  try {
    const token = await acquireSilent(
      instance,
      bffScope,
      account,
      options.silentRedirectUri,
      options.forceRefresh === true,
      options.authority
    );
    // A token again: any earlier failed redirect for this instance is over.
    clearSignInFailure(instance);
    return token;
  } catch (err) {
    if (isInteractionNeeded(err)) {
      // A background caller (the bell's poll) gets the sign-in state instead of a redirect: no return-to capture, no
      // navigation, no recorded failure.
      if (options.noInteraction) throw new SignInRequiredError(NO_INTERACTION_FAILURE);
      // Token expired, consent needed, MFA required, silent renewal timed out, etc. — fall back to
      // the interactive redirect (preserving the route) — unless a redirect already FAILED in this page load
      // (cancelled / declined / MFA abandoned): then answer with the `signin` state instead of redirecting again.
      assertRedirectAllowed(instance);
      captureReturnTo();
      await instance.acquireTokenRedirect({
        scopes: [bffScope],
        account,
        ...authority,
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
  forceRefresh = false,
  authorityOverride?: string
): Promise<string> {
  // `forceRefresh` is added to the request only when asked for, so the 040 request shapes are otherwise unchanged.
  const refresh = forceRefresh ? { forceRefresh: true } : {};
  // Likewise the home plane's per-request authority (A9).
  const authority = authorityOverride ? { authority: authorityOverride } : {};
  if (!silentRedirectUri) {
    const result = await instance.acquireTokenSilent({ scopes: [bffScope], account, ...refresh, ...authority });
    return result.accessToken;
  }

  try {
    const result = await instance.acquireTokenSilent({
      scopes: [bffScope],
      account,
      cacheLookupPolicy: CacheLookupPolicy.AccessTokenAndRefreshToken,
      ...refresh,
      ...authority,
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
    ...authority,
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
 * Scope (task 041, FR-17; ADR-028 A8): the active CIAM backend's `api://{customer BFF app id}/user_impersonation`
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
  const resolved = scope ?? activeBffScope('ciam');
  // Only CIAM accounts are considered (#2166); the flags are added only when set, so the MSAL request is as before.
  return acquireStandaloneBffToken(msalInstance, resolved, {
    accountFilter: isCiamAccount,
    ...(options?.forceRefresh ? { forceRefresh: true } : {}),
    ...(options?.noInteraction ? { noInteraction: true } : {}),
  });
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
  /**
   * Passed ONLY by a timer-driven background read (the notification bell's poll): if the token cannot be had without the
   * user, throw {@link SignInRequiredError} rather than redirect (browser) or open a popup (Teams). See
   * {@link StandaloneAcquireOptions.noInteraction}.
   */
  noInteraction?: boolean;
}

/** The options to hand an acquirer, carrying only what is set (so the default call shape never changes), or undefined. */
function definedAcquireOptions(options?: BffTokenAcquireOptions): BffTokenAcquireOptions | undefined {
  if (!options?.forceRefresh && !options?.noInteraction) return undefined;
  return {
    ...(options.forceRefresh ? { forceRefresh: true } : {}),
    ...(options.noInteraction ? { noInteraction: true } : {}),
  };
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
 * scope is not `api://{app id}/user_impersonation` (ADR-028 A8) — so no caller can turn an arbitrary string into a
 * scope for `acquireTokenSilent` / NAA.
 */
export function acquireActiveBffToken(
  backend?: Pick<ActiveBackend, 'bffScope' | 'plane'>,
  options?: BffTokenAcquireOptions
): Promise<string> {
  const passed = definedAcquireOptions(options);
  if (backend === undefined) {
    return passed ? activeBffTokenAcquirer(undefined, passed) : activeBffTokenAcquirer(undefined);
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
  return passed ? activeBffTokenAcquirer(scope, passed) : activeBffTokenAcquirer(scope);
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
 * Active account TENANT seam (same shape as the login-scope seam). Null for CIAM, whose accounts are scoped by the
 * {@link isCiamAccount} predicate instead (#2166: a company account cached in the tab is not a partner sign-in). The workforce browser plane sets
 * Spaarke's tenant id so a CIAM account cached in the same tab's sessionStorage is never treated as a
 * workforce sign-in by `AuthGuard` or the portal-user lookup.
 */
let activeAccountTenantId: string | null = null;

export function setActiveAccountTenantId(tenantId: string | null): void {
  activeAccountTenantId = tenantId;
}

/**
 * Active account PREDICATE seam (ADR-028 A9, task 058). The workforce HOME plane sets {@link isWorkforceHomeAccount} so
 * that, BEFORE the signed-in tenant is known, a CIAM account cached in the same tab's sessionStorage is still not counted
 * as a workforce sign-in; the CIAM plane sets {@link isCiamAccount}, the mirror (#2166). Null until a plane is applied.
 * Applied after the tenant filter.
 */
let activeAccountFilter: ((account: { tenantId?: string }) => boolean) | null = null;

export function setActiveAccountFilter(filter: ((account: { tenantId?: string }) => boolean) | null): void {
  activeAccountFilter = filter;
}

/**
 * True for an account a workforce contact signed in with in THEIR OWN directory: its tenant is a GUID and is not the CIAM
 * external tenant. MSAL's `environment` is deliberately not used (it reports the cache alias `login.windows.net` for
 * `login.microsoftonline.com`), and the Spaarke tenant is not excluded: a Spaarke-tenant sign-in simply finds no
 * directory entry and shows "not set up" - excluding it would send the sign-in round again, forever.
 */
export function isWorkforceHomeAccount(account: { tenantId?: string }): boolean {
  const tid = account.tenantId;
  return isTenantId(tid) && tid.toLowerCase() !== MSAL_TENANT_ID.toLowerCase();
}

/**
 * True for an account signed in on the CIAM (partner) plane: its tenant is the CIAM tenant. The CIAM plane's account
 * filter, the mirror of {@link isWorkforceHomeAccount}: a company account cached in the same tab (the home plane, then
 * "choose a different sign-in", then Partner) is not a partner sign-in, so it neither counts as signed in nor is used for
 * the CIAM token (#2166: msal-browser refused it with `authority_mismatch`).
 */
export function isCiamAccount(account: { tenantId?: string }): boolean {
  const tid = account.tenantId;
  return isTenantId(tid) && tid.toLowerCase() === MSAL_TENANT_ID.toLowerCase();
}

/** Whether the workforce HOME plane (A9) is the mounted browser plane; read by the registration gate. */
let workforceHomePlaneActive = false;

export function setWorkforceHomePlaneActive(active: boolean): void {
  workforceHomePlaneActive = active;
}

export function isWorkforceHomePlaneActive(): boolean {
  return workforceHomePlaneActive;
}

/** The accounts that belong to the currently-selected browser plane (CIAM: CIAM-tenant accounts only, #2166). */
export function accountsForActivePlane<T extends { tenantId?: string }>(accounts: readonly T[]): T[] {
  let result = [...accounts];
  if (activeAccountTenantId) {
    const tenant = activeAccountTenantId.toLowerCase();
    result = result.filter(a => a.tenantId?.toLowerCase() === tenant);
  }
  return activeAccountFilter ? result.filter(activeAccountFilter) : result;
}

/* ─────────────────────────────────────────────────────────────────────────────────────────
 * Teams collaboration host - multitenant client via NAA, then MSAL popup (ADR-028 A9 Teams amendment; task 075)
 * ───────────────────────────────────────────────────────────────────────────────────────────
 *
 * A SECOND acquisition strategy inside this SAME shared module - the Teams host - added WITHOUT forking the module or
 * re-adding a CIAM path. Since task 075 the Teams BFF token comes ONLY from the MULTITENANT home client (the same client
 * the browser "company account" sign-in uses), on authority `https://login.microsoftonline.com/{tid}`, where `tid` is the
 * Teams context tenant (`user.tenant.id`, validated and LISTED in the directory by the bootstrap before anything here
 * runs). It requests `api://{customer BFF app id}/user_impersonation`. The single-tenant A8 client is used in Teams ONLY
 * for the silent guest-token link ({@link acquireSpaarkeGuestToken}). The CIAM path above is untouched (FR-15).
 *
 * Acquisition order (the Office add-in's working shape, `OfficeNaaStrategy`):
 *   1. NAA: a nestable MSAL instance brokered by the Teams host (silent -> ssoSilent -> brokered popup).
 *   2. If NAA fails: a standard `PublicClientApplication` for the same client, tenant and scope, run through
 *      `OfficeNaaStrategy._tryAcquireWith`'s ladder - `acquireTokenSilent` with a cached account, then
 *      `acquireTokenPopup`. The popup lands on `{origin}/auth-callback.html`, the msal-browser v5 redirect-bridge page.
 *   3. If both fail: `TeamsWorkforceAuthError` carrying `{ naaError, popupError }`.
 *
 * Invariants (ADR-028 A2 as amended by A8 and A9 / NFR-02 / NFR-04):
 *   - Broker-only (NFR-02): the token authenticates to the BFF ONLY - never exchanged downstream (no OBO).
 *   - Non-redirect (NFR-04): NAA -> popup - NEVER `acquireTokenRedirect`. A full-page redirect inside the Teams iframe
 *     breaks framing. This is why the Teams path does NOT reuse `acquireStandaloneBffToken`.
 *   - No Teams SSO: `authentication.getAuthToken` is never called, and the Teams manifest declares no `webApplicationInfo`.
 *   - Per-request authority: under a live NAA bridge msal-browser 5.5.0 sends the host ONLY the per-request `authority`
 *     (`NestedAppAuthAdapter.toNaaTokenRequest`); the configured `auth.authority` is never sent. So `authority` is set on
 *     EVERY request (silent, ssoSilent, popup), NAA and standard alike.
 *   - Fail closed on tenant: a result whose `tenantId` is not the Teams context tid is never returned or used
 *     ({@link assertTeamsResultTenant}). The authority is `{tid}`, a GUID: `common` / `organizations` / `consumers`
 *     never appear ({@link workforceHomeTokenAuthority} refuses anything else).
 *   - Fail loud: an unauthenticated / consent-missing caller throws `TeamsWorkforceAuthError` - never silently degraded.
 */

/** Config the Teams host supplies to select the workforce strategy (the client id from `getWorkforceHomeClientConfig()`). */
export interface TeamsWorkforceAuthConfig {
  /** The MULTITENANT home public client id (A9, `VITE_WORKFORCE_HOME_MSAL_CLIENT_ID`), not the single-tenant A8 client. */
  clientId: string;
  /** The Teams context tenant id (a lower-case GUID); the authority is `https://login.microsoftonline.com/{tenantId}`. */
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
 * The authority of every Teams BFF token request: `https://login.microsoftonline.com/{tid}`. Throws
 * {@link TeamsWorkforceAuthError} for a tid that is not a GUID, so a bad context value reaches no MSAL instance.
 */
function teamsTokenAuthority(cfg: TeamsWorkforceAuthConfig): string {
  try {
    return workforceHomeTokenAuthority(cfg.tenantId);
  } catch (err) {
    throw new TeamsWorkforceAuthError('The Teams context tenant id is not valid; no token was requested.', err);
  }
}

/**
 * Decode a base64url JWT payload WITHOUT verifying the token (null when it is not a JWT). Only ever used for fail-closed
 * checks (the tenant) and the guest verdict; the BFF verifies the token.
 */
function accessTokenPayload(accessToken: string | undefined): Record<string, unknown> | null {
  try {
    const payload = accessToken?.split('.')[1];
    if (!payload) return null;
    const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
    const json = atob(base64 + '='.repeat((4 - (base64.length % 4)) % 4));
    const parsed: unknown = JSON.parse(json);
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, unknown>) : null;
  } catch {
    return null;
  }
}

/** The access token payload's `tid` (lower-case; '' when it cannot be read). */
function accessTokenTenantId(accessToken: string | undefined): string {
  const tid = accessTokenPayload(accessToken)?.tid;
  return typeof tid === 'string' ? tid.toLowerCase() : '';
}

/**
 * The tenant the TOKEN was issued by (lower-case; '' when it cannot be told): the id token's `tid` claim
 * (`result.idTokenClaims`), else the access token payload's `tid`. NOT `result.tenantId`: under NAA msal-browser 5.5.0 sets
 * that from the Teams HOST's account (`NestedAppAuthAdapter.fromNaaTokenResponse` -> `fromNaaAccountInfo`), so it is the
 * host tenant even for a token another tenant issued. Used only for fail-closed checks; the token is never verified here.
 */
function tokenTenantId(result: AuthenticationResult | null | undefined): string {
  const claimed = (result?.idTokenClaims as { tid?: unknown } | undefined)?.tid;
  if (typeof claimed === 'string' && claimed) return claimed.toLowerCase();
  return accessTokenTenantId(result?.accessToken);
}

/**
 * Fail closed: the TOKEN must be for the Teams context tenant ({@link tokenTenantId}). A missing or different tenant
 * throws and the token is never returned, made the active account, or sent.
 */
function assertTeamsResultTenant(result: AuthenticationResult | null | undefined, cfg: TeamsWorkforceAuthConfig): void {
  const got = tokenTenantId(result);
  if (!got || got !== cfg.tenantId.toLowerCase()) {
    throw new TeamsWorkforceAuthError(
      'The sign-in returned a token for a different organization than this Teams tab belongs to; it was not used.'
    );
  }
}

/**
 * Raised after a successful Teams sign-in when the signed-in account is a B2B GUEST of the Teams tenant. Teams gives a guest
 * `user.tenant.id` = the host tenant, so NAA through `/{tid}` returns a guest token the customer BFF refuses (401); signing in
 * again would reload into the same 401. The caller shows the guest screen and makes NO BFF call.
 */
export class TeamsGuestAccountError extends Error {
  constructor(
    /** Masked diagnostic for the guest screen ({@link TeamsGuestVerdict.detail}): no ids, names or addresses. */
    public readonly detail?: string
  ) {
    super('This Teams tab cannot be used with a guest account.');
    this.name = 'TeamsGuestAccountError';
  }
}

/**
 * Whether the last successful Teams sign-in was a guest of the Teams tenant, and a masked `detail` saying why:
 * `path=naa|popup acct=0|1|other|missing home=match|differs|unreadable|none ext=yes|no src=cache|fresh` (no ids, names or addresses).
 */
export interface TeamsGuestVerdict {
  guest: boolean;
  detail: string;
}

/**
 * The guest verdict for a Teams token result. The token's own `acct` claim decides when it is present (`0` = a member of
 * the issuing tenant; anything else = not a member): it is the claim the BFF's WorkforceHome scheme requires to be `0`, so
 * the tab and the BFF agree. The host's account description (`homeAccountId`, `#EXT#`; {@link isTeamsGuestAccount}) decides
 * only when the token carries no `acct`. On Teams desktop, members were shown the guest screen when the host-account check
 * decided alone (task 071, 2026-10-29; cause not yet confirmed, `detail` is there to confirm it).
 */
function teamsGuestVerdict(
  result: AuthenticationResult,
  cfg: TeamsWorkforceAuthConfig,
  path: 'naa' | 'popup'
): TeamsGuestVerdict {
  const raw = accessTokenPayload(result.accessToken)?.acct;
  const acct =
    raw === undefined || raw === null
      ? 'missing'
      : String(raw).trim() === '0'
        ? '0'
        : String(raw).trim() === '1'
          ? '1'
          : 'other';
  const account = result.account;
  const homePart = account?.homeAccountId?.split('.').pop();
  const home = !account
    ? 'none'
    : !isTenantId(homePart)
      ? 'unreadable'
      : homePart.toLowerCase() === cfg.tenantId.toLowerCase()
        ? 'match'
        : 'differs';
  const ext = typeof account?.username === 'string' && account.username.toUpperCase().includes('#EXT#') ? 'yes' : 'no';
  const guest = acct === 'missing' ? isTeamsGuestAccount(account, cfg.tenantId) : acct !== '0';
  return {
    guest,
    detail: `path=${path} acct=${acct} home=${home} ext=${ext} src=${result.fromCache ? 'cache' : 'fresh'}`,
  };
}

/** The verdict of the last successful Teams token acquisition (null before the first). */
let teamsWorkforceGuestVerdict: TeamsGuestVerdict | null = null;

/** The guest verdict of the last successful Teams sign-in, or null before the first. Read by the host adapter's guest check. */
export function getTeamsWorkforceGuestVerdict(): TeamsGuestVerdict | null {
  return teamsWorkforceGuestVerdict;
}

/**
 * A successful Teams token result: make ITS account the active one (always, so an account an earlier sign-in left active
 * in the shared cache never stands in for this one) and record its guest verdict. Returns the access token.
 */
function recordTeamsSignIn(
  instance: IPublicClientApplication,
  result: AuthenticationResult,
  cfg: TeamsWorkforceAuthConfig,
  path: 'naa' | 'popup'
): string {
  if (result.account) instance.setActiveAccount(result.account);
  teamsWorkforceGuestVerdict = teamsGuestVerdict(result, cfg, path);
  return result.accessToken;
}

/**
 * True when the account is a guest of the Teams tenant `tid`: its HOME tenant (the `utid` suffix of `homeAccountId`,
 * `{oid}.{tid}`) is a GUID that differs from `tid`; or, as a secondary signal, its username carries `#EXT#` (the external-user
 * UPN form). An account whose home tenant cannot be told is NOT a guest here (the BFF still refuses a guest token).
 */
export function isTeamsGuestAccount(
  account: { homeAccountId?: string; username?: string } | null | undefined,
  tid: string
): boolean {
  if (!account) return false;
  const home = account.homeAccountId?.split('.').pop();
  if (isTenantId(home) && home.toLowerCase() !== tid.toLowerCase()) return true;
  return typeof account.username === 'string' && account.username.toUpperCase().includes('#EXT#');
}

/**
 * MSAL `Configuration` for both Teams instances: the multitenant home client on the Teams tenant's authority, and the
 * redirect-bridge page as redirect URI. Under a live NAA bridge the redirect URI is not used (the
 * broker owns the exchange); when the host offers no bridge, `createNestablePublicClientApplication`
 * returns a standard instance, and its silent iframe and popup then land on the bridge page.
 */
function teamsWorkforceMsalConfiguration(cfg: TeamsWorkforceAuthConfig) {
  const authorityConfig = workforceHomeAuthorityConfig({ clientId: cfg.clientId, tenantId: cfg.tenantId });
  return buildMsalConfiguration(authorityConfig, { redirectUri: popupRedirectUri() });
}

/**
 * Build the Nested App Auth (NAA) MSAL instance for the Teams host: the multitenant home client on the Teams
 * context tenant's authority (`/{tid}`). Unlike the standalone SPA's `new PublicClientApplication`, NAA runs MSAL NESTED in the
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
  cfg: TeamsWorkforceAuthConfig,
  forceRefresh = false,
  noInteraction = false
): Promise<string> {
  // The per-request authority is the only one a live NAA bridge receives (see the section header).
  const request = { scopes: [cfg.bffScope], authority: teamsTokenAuthority(cfg) };
  const account = instance.getActiveAccount() ?? instance.getAllAccounts()[0] ?? null;
  try {
    const result = account
      ? await instance.acquireTokenSilent({ ...request, account, ...(forceRefresh ? { forceRefresh: true } : {}) })
      : await instance.ssoSilent(request);
    assertTeamsResultTenant(result, cfg);
    // The result's account becomes the active one, so useMsal()/useIsAuthenticated() (host adapter's MsalProvider)
    // reflect the signed-in user and the NEXT call above takes the fast acquireTokenSilent(account) branch.
    return recordTeamsSignIn(instance, result, cfg, 'naa');
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      // A background caller (the bell's poll) opens no popup.
      if (noInteraction) throw new SignInRequiredError(NO_INTERACTION_FAILURE);
      // Broker popup - Teams resolves this without a visible second login for a consented user.
      const result = await instance.acquireTokenPopup(request);
      assertTeamsResultTenant(result, cfg);
      return recordTeamsSignIn(instance, result, cfg, 'naa');
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
  cfg: TeamsWorkforceAuthConfig,
  trySilentFirst: boolean,
  forceRefresh = false,
  noInteraction = false
): Promise<string> {
  const request = { scopes: [cfg.bffScope], authority: teamsTokenAuthority(cfg) };
  const account = instance.getActiveAccount() ?? instance.getAllAccounts()[0] ?? null;

  if (account && trySilentFirst) {
    try {
      const result = await instance.acquireTokenSilent({
        ...request,
        account,
        ...(forceRefresh ? { forceRefresh: true } : {}),
      });
      // A token for another tenant is not used: it falls through to the popup, whose result is checked again.
      assertTeamsResultTenant(result, cfg);
      if (result.accessToken) return recordTeamsSignIn(instance, result, cfg, 'popup');
    } catch {
      // Fall through to the popup, as OfficeNaaStrategy._tryAcquireWith does.
    }
  }

  // A background caller (the bell's poll) opens no popup: the silent attempt above was its only try.
  if (noInteraction) throw new SignInRequiredError(NO_INTERACTION_FAILURE);

  const result = await instance.acquireTokenPopup({
    ...request,
    loginHint: account?.username,
    prompt: account ? undefined : 'select_account',
  });
  assertTeamsResultTenant(result, cfg);
  if (!result.accessToken) {
    throw new TeamsWorkforceAuthError('The MSAL popup returned no access token.');
  }
  return recordTeamsSignIn(instance, result, cfg, 'popup');
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
 * MSAL popup on a standard instance for the same client (ADR-028 A8: NAA first, then the popup; never
 * Teams SSO) — except after the user cancelled NAA's own sign-in, when no second popup is opened. Both are
 * broker-only (NFR-02) and non-redirect (NFR-04). If both fail, throws `TeamsWorkforceAuthError` — the
 * caller surfaces a sign-in/consent error rather than degrading silently.
 *
 * The popup here runs without a user gesture (bootstrap); a blocked popup is recovered by the error
 * screen's "Sign in" button, which calls {@link signInTeamsWorkforceViaPopup} on click.
 *
 * @param cfg The multitenant home client id, the Teams context tenant id and the `…/user_impersonation` BFF scope.
 * @returns Access token string, ready for `Authorization: Bearer {token}` against the BFF.
 * @throws {TeamsWorkforceAuthError} When not in a Teams host, or neither NAA nor the popup succeeds.
 */
export async function acquireTeamsWorkforceBffToken(
  cfg: TeamsWorkforceAuthConfig,
  options?: BffTokenAcquireOptions
): Promise<string> {
  const forceRefresh = options?.forceRefresh === true;
  const noInteraction = options?.noInteraction === true;
  // A tid that is not a GUID reaches no MSAL instance and no host (fail closed, zero token requests).
  teamsTokenAuthority(cfg);
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
        const token = await acquireBffTokenViaNaa(instance, cfg, forceRefresh, noInteraction);
        teamsWorkforcePath = 'naa';
        teamsWorkforceSignedInInstance = instance;
        return token;
      } catch (err) {
        // Sign-in needed and the caller may not interact: no popup fallback, no state change.
        if (err instanceof SignInRequiredError) throw err;
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
    return await signInViaPopupInstance(cfg, true, forceRefresh, noInteraction);
  } catch (popupError) {
    if (popupError instanceof SignInRequiredError) throw popupError; // background caller: leave the path state alone
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
  forceRefresh = false,
  noInteraction = false
): Promise<string> {
  const instance = await getTeamsWorkforcePopupMsalInstance(cfg);
  const token = await acquireBffTokenViaPopup(instance, cfg, trySilentFirst, forceRefresh, noInteraction);
  teamsWorkforcePath = 'popup';
  teamsWorkforceSignedInInstance = instance;
  return token;
}

/**
 * The MSAL popup path for the Teams error screen's "Sign in" button (ADR-028 A8 fallback), so the popup opens
 * from a USER GESTURE (bootstrap cannot guarantee one; Teams webviews and browsers may block a gesture-less
 * `window.open`). Goes straight to `acquireTokenPopup` with no silent attempt first, which could outlast the
 * click's transient user activation and get the popup blocked. Records the popup instance as the signed-in
 * instance on success.
 */
export async function signInTeamsWorkforceViaPopup(cfg: TeamsWorkforceAuthConfig): Promise<string> {
  return signInViaPopupInstance(cfg, false);
}

/* ─────────────────────────────────────────────────────────────────────────────────────────
 * Guest (Spaarke-tenant) token for the stored link (ADR-028 A9; task 058)
 * ───────────────────────────────────────────────────────────────────────────────────────────
 *
 * A licensed workforce user is a GUEST of Spaarke's tenant and the BFF must link the home sign-in's tid/oid to that
 * guest's `systemuser` row. The proof is a Spaarke-tenant token for the BFF, requested here with the single-tenant A8
 * client (authority pinned to Spaarke's tenant) and sent ONCE, in the body of `POST /api/v1/external/workforce/link`.
 *
 *   - The token is a one-shot credential, not a session: the instance keeps its cache in MEMORY (never sessionStorage or
 *     localStorage), the cache is cleared after every call, and the token is returned to the caller only. It is never
 *     logged, stored, put in a URL, or used as a Bearer token.
 *   - `silent` runs `ssoSilent` with the home account as `loginHint` (a hidden iframe: Chrome and Edge allow it; Safari
 *     and Firefox block third-party cookies, so it fails and the caller falls back to the profile form). ANY failure is
 *     "no token" (null): the caller shows the form, which fails closed.
 *   - `popup` runs `acquireTokenPopup` and is called ONLY from a click. A closed popup is `GuestLinkTokenError('cancelled')`.
 *   - Only the `api://{app id}/user_impersonation` scope is ever requested.
 */

/** A failed popup acquisition: the person closed the window (`cancelled`), or it failed for another reason (`failed`). */
export class GuestLinkTokenError extends Error {
  constructor(public readonly reason: 'cancelled' | 'failed') {
    super(
      reason === 'cancelled'
        ? 'The sign-in window was closed before it finished.'
        : 'The Spaarke sign-in could not be completed.'
    );
    this.name = 'GuestLinkTokenError';
  }
}

let guestLinkInstancePromise: Promise<IPublicClientApplication> | null = null;
let guestLinkInstanceCacheKey: string | null = null;

/**
 * The memoized A8 instance for the link: Spaarke-tenant authority, bridge-page redirect URI, in-memory cache. A failed
 * creation is not memoized, so a later call can retry it.
 */
async function getGuestLinkInstance(bffScope: string): Promise<IPublicClientApplication> {
  const { clientId, tenantId } = getWorkforceClientConfig();
  const key = `${clientId}|${tenantId}`;
  if (!guestLinkInstancePromise || guestLinkInstanceCacheKey !== key) {
    guestLinkInstanceCacheKey = key;
    const created = (async () => {
      const configuration = buildMsalConfiguration(workforceAuthorityConfig({ clientId, tenantId, bffScope }), {
        redirectUri: popupRedirectUri(),
        cacheLocation: 'memoryStorage',
      });
      const pca = new PublicClientApplication(configuration);
      await pca.initialize();
      return pca as IPublicClientApplication;
    })();
    guestLinkInstancePromise = created;
    created.catch(() => {
      if (guestLinkInstancePromise === created) {
        guestLinkInstancePromise = null;
        guestLinkInstanceCacheKey = null;
      }
    });
  }
  return guestLinkInstancePromise;
}

/**
 * Which host runs the guest link. The Teams host adapter sets `'teams'` once at auth setup (like
 * {@link setActiveBffTokenAcquirer}); the browser default is unchanged.
 */
let guestLinkHost: 'browser' | 'teams' = 'browser';

export function setGuestLinkHost(host: 'browser' | 'teams'): void {
  guestLinkHost = host;
}

/** How long the Teams silent guest link waits for the host bridge: a bridge request has no timeout of its own. */
const GUEST_NAA_TIMEOUT_MS = 10_000;

/** A short reason code for the console: an MSAL error code if there is one, never a message, token or claim. */
function guestNaaReason(err: unknown): string {
  const code = (err as { errorCode?: unknown } | null)?.errorCode;
  return typeof code === 'string' && /^[a-z0-9_.-]{1,64}$/i.test(code) ? code : 'error';
}

/**
 * The SILENT guest-token acquisition in Teams (ADR-028 A9 Teams amendment; task 075): the single-tenant A8 client through
 * NAA against Spaarke's tenant. Acquiring a token for another tenant's authority through the Teams NAA bridge is
 * undocumented (design section 12), so this is best effort: ANY failure - no bridge, a refusal, a timeout, a result that is
 * not the Spaarke tenant's - is `null`, and the gate shows the profile form (which fails closed). Every null path leaves a
 * `console.warn` with a short reason code (an MSAL error code, `timeout`, `tenant_mismatch`, ...) - never a token or a claim.
 *
 *   - `authority` is the Spaarke tenant, set per request (the only authority a live NAA bridge receives). `loginHint` is
 *     passed too, but a live bridge drops it (the bridge request has no such field: msal-browser 5.5.0 `toNaaTokenRequest`);
 *     only a bridge-less standard instance uses it.
 *   - The result is checked by the TOKEN's tenant (`idTokenClaims.tid`, else the access token payload's `tid`), never by
 *     `result.tenantId`, which under NAA is the Teams host account's tenant ({@link tokenTenantId}).
 *   - Bounded by {@link GUEST_NAA_TIMEOUT_MS}: bridge requests have no timeout, so a host that never answers would leave the
 *     gate on "Linking your account..." forever. On expiry the attempt is abandoned (its late result is discarded and its
 *     cleanup still runs when it settles).
 *   - A FRESH nestable instance per call, never memoized: the NAA controller does not support `clearCache` (it throws) and
 *     serves later requests from its own cache by account, so the only way the token lives "for the link call only" is
 *     for the instance, with its memory cache, to be dropped after the call. Cleanup is still account-scoped, never the
 *     no-account `clearCache()`, and an NAA cleanup throw is caught ({@link dropGuestLinkAccounts}).
 *   - Skipped (null) when the host does not recommend the NAA channel: a bridge-less nestable instance would run a hidden
 *     iframe with a 10 s timeout, which is the browser path that does not apply in Teams.
 */
async function acquireSpaarkeGuestTokenViaNaa(bffScope: string, loginHint: string): Promise<string | null> {
  const attempt = (async (): Promise<string | null> => {
    let instance: IPublicClientApplication | null = null;
    try {
      if (!isNaaChannelRecommended()) {
        console.warn('[GuestLink] Teams silent link skipped: naa_not_recommended');
        return null;
      }
      const { clientId, tenantId } = getWorkforceClientConfig();
      const configuration = buildMsalConfiguration(workforceAuthorityConfig({ clientId, tenantId, bffScope }), {
        redirectUri: popupRedirectUri(),
        cacheLocation: 'memoryStorage',
      });
      instance = await createNestablePublicClientApplication(configuration);
      const result = await instance.ssoSilent({
        scopes: [bffScope],
        authority: workforceAuthority(tenantId),
        loginHint,
      });
      if (!result?.accessToken) {
        console.warn('[GuestLink] Teams silent link returned no token: no_token');
        return null;
      }
      if (tokenTenantId(result) !== tenantId.toLowerCase()) {
        console.warn('[GuestLink] Teams silent link refused: tenant_mismatch');
        return null;
      }
      return result.accessToken;
    } catch (err) {
      console.warn(`[GuestLink] Teams silent link failed: ${guestNaaReason(err)}`);
      return null;
    } finally {
      if (instance) await dropGuestLinkAccounts(instance);
    }
  })();

  let timer: ReturnType<typeof setTimeout> | undefined;
  const timeout = new Promise<'timeout'>(resolve => {
    timer = setTimeout(() => resolve('timeout'), GUEST_NAA_TIMEOUT_MS);
  });
  try {
    const outcome = await Promise.race([attempt, timeout]);
    if (outcome === 'timeout') {
      console.warn('[GuestLink] Teams silent link failed: timeout');
      return null;
    }
    return outcome;
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Acquire the Spaarke-tenant guest token for the stored link. `silent` resolves to the token or null (any failure);
 * `popup` resolves to the token or throws {@link GuestLinkTokenError}. The instance's cache is cleared afterwards in
 * both modes.
 *
 * @param mode `silent` (hidden iframe, no user gesture) or `popup` (call ONLY from a click).
 * @param loginHint The home account's username (UPN), so the sign-in resolves to the same person.
 * @param bffScope `api://{customer BFF app id}/user_impersonation` of the active backend.
 */
export async function acquireSpaarkeGuestToken(
  mode: 'silent' | 'popup',
  loginHint: string,
  bffScope: string
): Promise<string | null> {
  if (!isUserImpersonationScope(bffScope)) {
    if (mode === 'popup') throw new GuestLinkTokenError('failed');
    return null;
  }

  // Teams: the silent attempt goes through NAA (a hidden iframe does not work in the Teams webview). The popup does not:
  // it stays on the standard guest instance below, from a click only.
  if (mode === 'silent' && guestLinkHost === 'teams') {
    const token = await acquireSpaarkeGuestTokenViaNaa(bffScope, loginHint);
    // No silent token: the gate shows the form and the "Link my account" click may follow. Build the standard guest
    // instance now (as prewarmTeamsWorkforcePopupInstance does), so that click does not spend its user-activation window
    // awaiting PublicClientApplication.initialize() before acquireTokenPopup. Best effort and idempotent.
    if (!token) void getGuestLinkInstance(bffScope).catch(() => undefined);
    return token;
  }

  let instance: IPublicClientApplication;
  try {
    instance = await getGuestLinkInstance(bffScope);
  } catch {
    if (mode === 'popup') throw new GuestLinkTokenError('failed');
    return null;
  }

  try {
    const result =
      mode === 'silent'
        ? await instance.ssoSilent({ scopes: [bffScope], loginHint })
        : await instance.acquireTokenPopup({ scopes: [bffScope], loginHint });
    if (!result.accessToken) {
      if (mode === 'popup') throw new GuestLinkTokenError('failed');
      return null;
    }
    return result.accessToken;
  } catch (err) {
    if (mode === 'silent') return null;
    if (err instanceof GuestLinkTokenError) throw err;
    throw new GuestLinkTokenError(isUserCancelled(err) ? 'cancelled' : 'failed');
  } finally {
    await dropGuestLinkAccounts(instance);
  }
}

/**
 * Remove every account (and so every token) this guest instance holds, so the token lives for one call only.
 *
 * NEVER `instance.clearCache()` with no account here: in msal-browser 5.5.0 that form runs `BrowserCacheManager.clear()`,
 * which deletes every sessionStorage key containing "msal" - the temporary cache is always sessionStorage, whatever the
 * instance's `cacheLocation` - and so signs the home (multitenant) plane and the CIAM plane out. The account form
 * (`clearCacheOnLogout` -> `removeAccount`) touches only this instance's own (memory) store.
 * Regression: tests/guest-token-cache-isolation.test.ts (real msal-browser).
 */
async function dropGuestLinkAccounts(instance: IPublicClientApplication): Promise<void> {
  let accounts: AccountInfo[] = [];
  try {
    accounts = instance.getAllAccounts();
  } catch {
    /* best effort: the cache is in memory only */
  }
  for (const account of accounts) {
    try {
      await instance.clearCache({ account });
    } catch {
      /* best effort: the cache is in memory only */
    }
  }
}
