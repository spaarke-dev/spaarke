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

import { InteractionRequiredAuthError } from '@azure/msal-browser';
import { msalInstance } from './msal-config';
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
 * Acquire an access token for the BFF API.
 *
 * Strategy:
 *   1. Try silent acquisition (MSAL cache / refresh token).
 *   2. On InteractionRequiredAuthError, trigger a redirect login.
 *   3. Throw after initiating redirect so callers abort the current request.
 *
 * MSAL handles token caching internally — no manual cache management needed
 * (unlike the previous portal implicit grant flow).
 *
 * @returns Access token string, ready for `Authorization: Bearer {token}` header.
 * @throws If silent acquisition fails for a non-interaction reason (network error, etc.).
 */
export async function acquireBffToken(): Promise<string> {
  const accounts = msalInstance.getAllAccounts();

  if (accounts.length === 0) {
    // No authenticated account — trigger login redirect (preserving the current route)
    captureReturnTo();
    await msalInstance.acquireTokenRedirect({ scopes: [MSAL_BFF_SCOPE] });
    // acquireTokenRedirect navigates away; this throw aborts the current call chain
    throw new Error('No authenticated account — redirecting to login');
  }

  try {
    const result = await msalInstance.acquireTokenSilent({
      scopes: [MSAL_BFF_SCOPE],
      account: accounts[0],
    });
    return result.accessToken;
  } catch (err) {
    if (err instanceof InteractionRequiredAuthError) {
      // Token expired, consent needed, MFA required, etc. — trigger redirect (preserving route)
      captureReturnTo();
      await msalInstance.acquireTokenRedirect({
        scopes: [MSAL_BFF_SCOPE],
        account: accounts[0],
      });
      throw new Error('Interaction required — redirecting to login');
    }
    throw err;
  }
}
