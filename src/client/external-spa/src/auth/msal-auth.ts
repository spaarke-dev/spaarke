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
  InteractionRequiredAuthError,
  createNestablePublicClientApplication,
  type IPublicClientApplication,
} from '@azure/msal-browser';
import { app as teamsApp, authentication as teamsAuthentication } from '@microsoft/teams-js';
import { msalInstance, workforceAuthorityConfig, buildMsalConfiguration } from './msal-config';
import { MSAL_BFF_SCOPE } from '../config';

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
 * Acquire a BFF access token from a standalone collaboration-host MSAL instance.
 *
 * Broker-only (A1/A2 invariant): the returned token is used ONLY to call the BFF as a
 * Bearer credential — it is NEVER exchanged for a downstream Graph/SPE/Dataverse token (no
 * OBO on the collaboration path). Parameterized by instance + scope so the SAME acquisition
 * path serves every authority — the CIAM SPA today (via `acquireBffToken`), the workforce
 * Teams host via tasks 011/012 — without forking the module.
 *
 * Strategy:
 *   1. Try silent acquisition (MSAL cache / refresh token).
 *   2. On InteractionRequiredAuthError, trigger a redirect login.
 *   3. Throw after initiating redirect so callers abort the current request.
 *
 * MSAL handles token caching internally — no manual cache management needed.
 *
 * @param instance The standalone `PublicClientApplication` (from msal-config.ts) to acquire against.
 * @param bffScope The BFF API OAuth scope for the instance's authority.
 * @returns Access token string, ready for `Authorization: Bearer {token}` header.
 * @throws If silent acquisition fails for a non-interaction reason (network error, etc.).
 */
export async function acquireStandaloneBffToken(
  instance: IPublicClientApplication,
  bffScope: string
): Promise<string> {
  const accounts = instance.getAllAccounts();

  if (accounts.length === 0) {
    // No authenticated account — trigger login redirect (preserving the current route)
    captureReturnTo();
    await instance.acquireTokenRedirect({ scopes: [bffScope] });
    // acquireTokenRedirect navigates away; this throw aborts the current call chain
    throw new Error('No authenticated account — redirecting to login');
  }

  try {
    const result = await instance.acquireTokenSilent({
      scopes: [bffScope],
      account: accounts[0],
    });
    return result.accessToken;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      // Token expired, consent needed, MFA required, etc. — trigger redirect (preserving route)
      captureReturnTo();
      await instance.acquireTokenRedirect({
        scopes: [bffScope],
        account: accounts[0],
      });
      throw new Error('Interaction required — redirecting to login');
    }
    throw err;
  }
}

/**
 * Acquire an access token for the BFF API — external SPA (CIAM) path.
 *
 * Unchanged public API for bff-client.ts: delegates to `acquireStandaloneBffToken` with the
 * CIAM `msalInstance` singleton and the CIAM BFF scope, preserving the exact prior behavior
 * (FR-15 — no CIAM regression). See `acquireStandaloneBffToken` for the strategy.
 *
 * @returns Access token string, ready for `Authorization: Bearer {token}` header.
 */
export async function acquireBffToken(): Promise<string> {
  return acquireStandaloneBffToken(msalInstance, MSAL_BFF_SCOPE);
}

/**
 * Pluggable "which strategy is bff-client.ts currently using" seam (task 012 host-detection wiring).
 *
 * `bff-client.ts` calls `acquireActiveBffToken()` for every BFF request instead of hard-coding
 * `acquireBffToken`. The default remains the CIAM strategy (byte-for-byte unchanged behavior —
 * FR-15 no-CIAM-regression: a build that never runs inside Teams never calls `setActiveBffTokenAcquirer`
 * and this module behaves exactly as before). The Teams host adapter is the ONLY caller that ever
 * switches it, and it does so exactly once at bootstrap by re-pointing this seam at
 * `acquireTeamsWorkforceBffToken` — no acquisition LOGIC lives outside this file (ADR-028 A2: "the
 * adapter selects the strategy; it must NOT implement its own auth logic").
 */
export type BffTokenAcquirer = () => Promise<string>;

let activeBffTokenAcquirer: BffTokenAcquirer = acquireBffToken;

/** Host adapter seam: select which strategy `acquireActiveBffToken` delegates to. */
export function setActiveBffTokenAcquirer(acquirer: BffTokenAcquirer): void {
  activeBffTokenAcquirer = acquirer;
}

/** Acquire a BFF token via whichever strategy is currently active (CIAM by default). */
export function acquireActiveBffToken(): Promise<string> {
  return activeBffTokenAcquirer();
}

/* ─────────────────────────────────────────────────────────────────────────────────────────
 * Teams collaboration host — workforce SSO / NAA acquisition (tasks 011/012)
 * ───────────────────────────────────────────────────────────────────────────────────────────
 *
 * A SECOND acquisition strategy inside this SAME shared module — the Teams host — added WITHOUT
 * forking the module or re-adding a CIAM path. It reuses task 010's pluggable-authority builders
 * (`workforceAuthorityConfig` + `buildMsalConfiguration`) against the WORKFORCE-MULTITENANT
 * authority, and silently yields a BFF-valid workforce token with no visible second login. The
 * CIAM path above is untouched (FR-15). Task 012 (host adapter) decides WHEN this strategy runs
 * vs the CIAM `acquireBffToken`; this module only makes it available + correct.
 *
 * Invariants (ADR-028 A2 / NFR-02 / NFR-04):
 *   - Broker-only (NFR-02): the token authenticates to the BFF ONLY — never exchanged downstream
 *     (no OBO on the collaboration path). NAA/getAuthToken both return a Bearer used only vs the BFF.
 *   - Non-redirect (NFR-04): acquisition is silent → NAA/SSO broker popup — NEVER
 *     `acquireTokenRedirect`. A full-page redirect inside the Teams iframe breaks framing. This is
 *     why the Teams path does NOT reuse `acquireStandaloneBffToken` (whose interaction fallback is
 *     a redirect — correct for the standalone SPA, wrong inside Teams).
 *   - Fail loud (acceptance criterion 4): an unauthenticated / consent-missing caller throws
 *     `TeamsWorkforceAuthError` — the failure is surfaced, never silently degraded or retried into
 *     a broken state.
 *
 * Authority decision (task 010 handoff — resolved): use the workforce-multitenant `/organizations`
 * DEFAULT, NOT a build-time tenant pin. The app registration is MULTITENANT with per-customer admin
 * consent (ADR-028 A2), so the customer tenant is unknown at build time — a static pin is impossible.
 * The Teams host BROKERS the already-signed-in workforce account (NAA / getAuthToken), so
 * `/organizations` resolves to the user's real home tenant with no disambiguation prompt. (INV-3's
 * "never `/organizations`" targets INTERNAL `@spaarke/auth` iframe-`ssoSilent` surfaces, where AAD
 * cannot disambiguate session cookies inside an iframe — that surface is A2-exempt and not used
 * here.) Task 012 MAY still pass a tenant-pinned `authority` (derived from Teams context `tid`) via
 * the optional `authority` field if a specific deployment requires it — the seam is preserved.
 */

/** Config the Teams host supplies to select the workforce strategy (task 012 provides the values). */
export interface TeamsWorkforceAuthConfig {
  /** Workforce (multitenant) SPA app-registration client id. */
  clientId: string;
  /** BFF API OAuth scope acquired for the workforce plane (broker-only — used only to call the BFF). */
  bffScope: string;
  /** Optional tenant-pinned authority override; defaults to the multitenant `/organizations`. */
  authority?: string;
}

/**
 * Raised when the Teams workforce strategy cannot yield a BFF-valid token (no NAA bridge AND no
 * Teams SSO, cancelled/blocked interaction, missing consent, or not inside a Teams host). The
 * caller MUST surface this as a sign-in/consent error — acceptance criterion 4 (fail loud).
 */
export class TeamsWorkforceAuthError extends Error {
  constructor(
    message: string,
    /** The underlying error(s) — e.g. `{ naaError, ssoError }` — preserved for diagnostics. */
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
 * Build the Nested App Auth (NAA) MSAL instance for the Teams host against the workforce-multitenant
 * authority. Unlike the standalone SPA's `new PublicClientApplication`, NAA runs MSAL NESTED in the
 * Teams host broker (no iframe `ssoSilent`, no redirect) — the broker supplies the signed-in
 * workforce account, so token acquisition is silent for a consented user. Reuses task 010's config
 * builders; supplies no CIAM values (workforce plane only).
 */
export async function createTeamsWorkforceMsalInstance(
  cfg: TeamsWorkforceAuthConfig
): Promise<IPublicClientApplication> {
  const authorityConfig = workforceAuthorityConfig({
    clientId: cfg.clientId,
    bffScope: cfg.bffScope,
    authority: cfg.authority,
  });
  return createNestablePublicClientApplication(buildMsalConfiguration(authorityConfig));
}

/**
 * Memoized Teams workforce NAA instance (task 012). A fresh `PublicClientApplication` has an empty
 * MSAL cache, so re-creating one on every call (as a naive per-call `createTeamsWorkforceMsalInstance`
 * would) would force a full `ssoSilent` broker round-trip on every single BFF request instead of
 * hitting `acquireTokenSilent`'s cache after the first. This getter makes `acquireTeamsWorkforceBffToken`
 * reuse ONE instance per session, AND gives the host adapter a stable instance to hand to
 * `MsalProvider` (`useMsal()` reflects the same account the acquisition path authenticated). Keyed by
 * clientId+authority so a config change safely rebuilds it. No acquisition LOGIC changes — same
 * silent → ssoSilent → popup → Teams-SSO-fallback sequence as before.
 */
let teamsWorkforceInstancePromise: Promise<IPublicClientApplication> | null = null;
let teamsWorkforceInstanceCacheKey: string | null = null;

export async function getTeamsWorkforceMsalInstance(
  cfg: TeamsWorkforceAuthConfig
): Promise<IPublicClientApplication> {
  const key = `${cfg.clientId}|${cfg.authority ?? ''}`;
  if (!teamsWorkforceInstancePromise || teamsWorkforceInstanceCacheKey !== key) {
    teamsWorkforceInstanceCacheKey = key;
    teamsWorkforceInstancePromise = createTeamsWorkforceMsalInstance(cfg);
  }
  return teamsWorkforceInstancePromise;
}

/**
 * Acquire the BFF token via NAA: silent (cached account) → `ssoSilent` (broker account bootstrap) →
 * `acquireTokenPopup` on `InteractionRequiredAuthError`. The popup is brokered by the Teams host, so
 * a consented workforce user sees no visible second login. NEVER redirects (NFR-04).
 */
async function acquireBffTokenViaNaa(
  instance: IPublicClientApplication,
  bffScope: string
): Promise<string> {
  const scopes = [bffScope];
  const account = instance.getActiveAccount() ?? instance.getAllAccounts()[0] ?? null;
  try {
    const result = account
      ? await instance.acquireTokenSilent({ scopes, account })
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
 * Acquire the BFF token via Teams SSO (`authentication.getAuthToken`). The token targets the app's
 * exposed API (manifest `webApplicationInfo` resource) and is used broker-only against the BFF. This
 * is the fallback for hosts that do not expose an NAA bridge; it is not a downstream exchange.
 */
async function acquireBffTokenViaTeamsSso(): Promise<string> {
  const token = await teamsAuthentication.getAuthToken();
  if (!token) {
    throw new TeamsWorkforceAuthError('Teams SSO getAuthToken returned an empty token.');
  }
  return token;
}

/**
 * Acquire a BFF-valid workforce token inside the Teams host — the selectable strategy the host
 * adapter (task 012) invokes. Primary: NAA (directly targets `bffScope`, task 010 config reuse).
 * Fallback: Teams SSO `getAuthToken` (hosts without an NAA bridge). Both are broker-only (NFR-02)
 * and non-redirect (NFR-04). If BOTH fail, throws `TeamsWorkforceAuthError` — the caller surfaces a
 * sign-in/consent error rather than degrading silently (acceptance criterion 4).
 *
 * @param cfg Workforce app-registration values (client id, BFF scope, optional tenant-pin authority).
 * @returns Access token string, ready for `Authorization: Bearer {token}` against the BFF.
 * @throws {TeamsWorkforceAuthError} When not in a Teams host, or neither NAA nor Teams SSO succeeds.
 */
export async function acquireTeamsWorkforceBffToken(cfg: TeamsWorkforceAuthConfig): Promise<string> {
  try {
    await ensureTeamsInitialized();
  } catch (initError) {
    // Reset so a later retry (e.g. once the host is ready) can re-initialize.
    teamsInitPromise = null;
    throw new TeamsWorkforceAuthError(
      'Teams JS app.initialize() failed — not running inside a Teams host.',
      initError
    );
  }

  let naaError: unknown;
  try {
    const instance = await getTeamsWorkforceMsalInstance(cfg);
    return await acquireBffTokenViaNaa(instance, cfg.bffScope);
  } catch (err) {
    // NAA unavailable (no host bridge) or failed — fall through to the Teams SSO fallback.
    naaError = err;
  }

  try {
    return await acquireBffTokenViaTeamsSso();
  } catch (ssoError) {
    throw new TeamsWorkforceAuthError(
      'Teams workforce token acquisition failed (NAA primary + Teams SSO fallback). ' +
        'The caller must surface a sign-in/consent error — do not silently degrade.',
      { naaError, ssoError }
    );
  }
}
