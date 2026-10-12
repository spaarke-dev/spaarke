/**
 * `Spaarke.BffAuth` — the classic-web-resource face of `@spaarke/auth`.
 *
 * Dataverse form scripts and ribbon commands are framework-free scripts that cannot `import`
 * an npm package. They call the BFF through ONE global helper, `Spaarke.BffAuth`, loaded as
 * the web resource `sprk_/scripts/bff_auth.js` (ADR-006 2026-10-08 amendment). Until #1655
 * that file was a hand-written copy of the MSAL flow with two defects the library had already
 * fixed:
 *   1. its ssoSilent `loginHint` fell back to `Xrm userSettings.userName` — the DISPLAY NAME —
 *      so a browser with no cached MSAL account (an InPrivate window, a first visit) got
 *      AADSTS50058 and every form-script BFF call was skipped ("Access status unavailable");
 *   2. it called `acquireTokenSilent` with `getAllAccounts()[0]` — whichever account was cached
 *      first — so in a browser profile holding another user's account a form script (including
 *      the Access ribbon's grant and unshare) could act as the WRONG USER.
 *
 * This module is now a THIN wrapper over the library: token acquisition, account selection,
 * the login hint, the tenant authority and the MSAL cache are all `@spaarke/auth`
 * (`initAuth` → `SpaarkeAuthProvider` → `BrowserMsalStrategy`), bundled with MSAL into the
 * web resource by `scripts/build-webresource.mjs`. Only the caller contract below is kept,
 * unchanged, so no caller changes:
 *
 *   - `getToken(apiBaseUrl)` → `Promise<string | null>`. Never rejects; `null` when no token
 *     could be acquired silently (callers then skip the call).
 *   - `authenticatedFetch(url, options, apiBaseUrl)` → `Promise<Response | null>`. `null`
 *     WITHOUT sending the request when there is no token. Otherwise the `fetch` Response as-is,
 *     including a non-OK one (callers read `response.ok`, `response.status` and the body); a
 *     network error or an abort rejects, as `fetch` does. The token is sent ONLY to the BFF:
 *     a `url` whose origin is not the BFF's (`sprk_BffApiBaseUrl`) gets `null` and no request.
 *   - `normalizeBaseUrl(raw)` → the BFF host without a trailing `/api` (the env var holds
 *     `https://host/api`; callers append `/api/...` themselves). Added so the form scripts share
 *     the library's one rule instead of each copying it.
 *
 * ADR-028 / spaarke-sso-binding.md invariants:
 *   - INV-1/INV-2: `cacheLocation: 'localStorage'`, `storeAuthStateInCookie: true` — set by
 *     BrowserMsalStrategy, not here.
 *   - INV-3/INV-4: tenant-specific authority — the tenant comes from `sprk_TenantId` (via
 *     `resolveRuntimeConfig`), else the library's own discovery; inside Dataverse the library
 *     refuses `/organizations` (`requireTenantAuthority`).
 *   - INV-5: the hint is the chosen account's UPN or `userSettings.userPrincipalName`, never the
 *     display name; with several cached candidate accounts none is guessed and ssoSilent runs on
 *     the browser's Entra session (#1453).
 *   - The signed-in user (#1656): inside Dataverse only a cached account, and only a token, whose
 *     `oid` is the Dataverse user's Entra object id is used — never another user's lone account.
 *   - INV-7: the SAME client id and scope as every PCF on the form (`sprk_MsalClientId`,
 *     `api://{sprk_BffApiAppId}/user_impersonation`), so form scripts and PCFs share one MSAL
 *     localStorage cache. (The old copy used the BFF's own app id from `/api/config/client`,
 *     a separate cache.)
 *   - No interactive prompt, ever: `requireSilentOnly: true`. A form load or a ribbon enable rule
 *     is not explicit user intent to sign in; a cold cache yields `null` and the caller skips.
 *   - Token per request: `getAccessToken()` on every call; nothing is snapshotted here.
 */

import { getAuthProvider, initAuth } from '../initAuth';
import { resolveRuntimeConfig } from '../resolveRuntimeConfig';
import { normalizeBffBaseUrl } from '../bffClientConfig';
import { isValidTenant } from '../tenant';
import { VERSION } from '../version';

/** Version of this wrapper (the library's own version is `libraryVersion`). */
export const BFF_AUTH_VERSION = '2.0.0';

const LOG = '[BffAuth]';

/** Plain-object view of `RequestInit` as the framework-free callers pass it. */
type FetchOptions = RequestInit & { headers?: HeadersInit };

/** The public surface the form scripts and ribbon commands call. Same members as before #1655, plus normalizeBaseUrl. */
export interface IBffAuth {
  _version: string;
  libraryVersion: string;
  getToken(apiBaseUrl: string): Promise<string | null>;
  authenticatedFetch(url: string, options?: FetchOptions, apiBaseUrl?: string): Promise<Response | null>;
  /**
   * The BFF host from a `sprk_BffApiBaseUrl` value: trimmed, no trailing slash, no trailing `/api`
   * (the env var holds `https://host/api`; route paths carry their own `/api/...`). The library's
   * one rule (`normalizeBffBaseUrl`), shared so form scripts do not each copy it.
   */
  normalizeBaseUrl(raw: string): string;
}

/** The origin of `url` (resolved against the page), or '' when it is not a URL. */
function originOf(url: string): string {
  try {
    return new URL(url, typeof location !== 'undefined' ? location.href : undefined).origin;
  } catch {
    return '';
  }
}

/**
 * Build a `Spaarke.BffAuth` object. One per page (the web resource installs one); each object
 * initialises the library at most once, retrying on a later call when initialisation failed.
 */
export function createBffAuth(): IBffAuth {
  let initPromise: Promise<void> | null = null;
  /** True once initialisation (and with it the library's eager first acquisition) has completed. */
  let initialized = false;

  function ensureInitialized(apiBaseUrl: string): Promise<void> {
    if (!initPromise) {
      initPromise = (async () => {
        // The same Dataverse environment variables every PCF on the form reads (session cookie, no token).
        const runtime = await resolveRuntimeConfig();
        await initAuth({
          clientId: runtime.msalClientId,
          bffApiScope: runtime.bffOAuthScope,
          ...(isValidTenant(runtime.tenantId) ? { tenantId: runtime.tenantId.trim() } : {}),
          bffBaseUrl: runtime.bffBaseUrl || normalizeBffBaseUrl(apiBaseUrl),
          requireTenantAuthority: true,
          requireSilentOnly: true,
          proactiveRefresh: false,
        });
        initialized = true;
      })().catch(err => {
        initPromise = null; // a later call retries
        throw err;
      });
    }
    return initPromise;
  }

  /** The token, or null. `apiBaseUrl` only gates the call (no base URL, no token), as before. */
  async function acquire(apiBaseUrl: string): Promise<string | null> {
    // A call that waited for initialisation shares its eager acquisition (initAuth acquires once
    // when it creates the provider): with no session that acquisition already ran the silent chain,
    // so it is not run a second time for the same answer.
    const waitedForInit = !initialized;
    await ensureInitialized(apiBaseUrl);
    const provider = getAuthProvider();
    const token = waitedForInit && !provider.isAuthenticated() ? '' : await provider.getAccessToken();
    if (!token) {
      console.warn(`${LOG} No BFF token could be acquired silently (the caller will skip the call).`);
      return null;
    }
    return token;
  }

  async function getToken(apiBaseUrl: string): Promise<string | null> {
    if (!apiBaseUrl) {
      return null;
    }
    try {
      return await acquire(apiBaseUrl);
    } catch (error) {
      console.warn(`${LOG} BFF token acquisition failed (the caller will skip the call):`, error);
      return null;
    }
  }

  async function authenticatedFetch(
    url: string,
    options?: FetchOptions,
    apiBaseUrl?: string
  ): Promise<Response | null> {
    const init: FetchOptions = options ?? {};
    if (!apiBaseUrl) {
      return null;
    }
    let token: string | null;
    try {
      await ensureInitialized(apiBaseUrl);
      // The token goes only to the BFF: a URL on any other origin gets no request at all.
      const bffOrigin = originOf(getAuthProvider().getConfig().bffBaseUrl);
      if (!bffOrigin || originOf(url) !== bffOrigin) {
        console.error(
          `${LOG} Refusing to send the BFF token to ${originOf(url) || url} (the BFF is ${bffOrigin || 'unknown'}).`
        );
        return null;
      }
      token = await acquire(apiBaseUrl);
    } catch (error) {
      console.warn(`${LOG} BFF token acquisition failed (the caller will skip the call):`, error);
      return null;
    }
    if (!token) {
      return null;
    }
    const headers = copyHeaders(init.headers);
    // Auth v2 (D-AUTH-7): the framework-free form-script contract returns non-OK Responses to
    // its callers (they read status and body), which @spaarke/auth's throwing authenticatedFetch
    // cannot. The token still comes from the library, per request.
    headers['Authorization'] = 'Bearer ' + token;
    return fetch(url, { ...init, headers });
  }

  return {
    _version: BFF_AUTH_VERSION,
    libraryVersion: VERSION,
    getToken,
    authenticatedFetch,
    normalizeBaseUrl: normalizeBffBaseUrl,
  };
}

/** Copy caller headers (a plain object, a Headers instance or entry pairs) into a plain object. */
function copyHeaders(source: HeadersInit | undefined): Record<string, string> {
  const out: Record<string, string> = {};
  if (!source) return out;
  if (Array.isArray(source)) {
    for (const [key, value] of source) out[key] = value;
  } else if (typeof (source as Headers).forEach === 'function') {
    (source as Headers).forEach((value, key) => {
      out[key] = value;
    });
  } else {
    for (const key of Object.keys(source)) out[key] = (source as Record<string, string>)[key];
  }
  return out;
}

/**
 * Install `Spaarke.BffAuth` on `target` (the form's window). Keeps an existing `Spaarke`
 * namespace (other libraries declare `var Spaarke = Spaarke || {}`) and replaces only `BffAuth`.
 */
export function installBffAuth(target: Window & { Spaarke?: Record<string, unknown> }): IBffAuth {
  const ns = (target.Spaarke = target.Spaarke || {});
  const api = createBffAuth();
  ns.BffAuth = api;
  console.info(`${LOG} v${BFF_AUTH_VERSION} (@spaarke/auth v${VERSION}) loaded`);
  return api;
}
