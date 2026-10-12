/**
 * The signed-in Dataverse user's Entra object id (#1656).
 *
 * MSAL's `localStorage` cache outlives a Dataverse sign-out. On a shared workstation (user A
 * signs out, user B signs in in the same browser profile) A's cached account and refresh token
 * are still there, and a lone cached account would be redeemed silently — every PCF, code page
 * and form script would call the BFF as A while the Dataverse session is B's.
 *
 * Inside a Dataverse host the library therefore asks Dataverse who is signed in — with the
 * session cookie, no token — and `BrowserMsalStrategy` uses only a cached account, and accepts
 * only a token, whose `oid` is that user's:
 *
 *   GET {clientUrl}/api/data/v9.2/systemusers({userId})?$select=azureactivedirectoryobjectid
 *
 * `userId` is `Xrm…userSettings.userId`; a page on the Dataverse domain without Xrm asks
 * `WhoAmI` for it. The answer is read once per page (the promise is shared) and kept for the tab
 * in `sessionStorage`, keyed by org and systemuser, so the several bundles on one form (each PCF
 * bundles its own copy of this library) make one request between them. A failed read is retried
 * after a short back-off; until then the caller fails closed (no cached account is used).
 *
 * @module dataverseUser
 */

import { isDataverseHost, isDataverseOrigin, xrmGlobalContext } from './tenant';
import { LOOKUP_FAILURE_BACKOFF_MS, LOOKUP_TIMEOUT_MS, timeoutSignal } from './bffClientConfig';

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const SS_PREFIX = '__spaarke_dv_user_oid__|';

/** A GUID, lower-cased without braces; `null` for anything else. */
export function normalizeObjectId(value: unknown): string | null {
  if (typeof value !== 'string') return null;
  const id = value
    .trim()
    .replace(/^\{|\}$/g, '')
    .toLowerCase();
  return GUID_RE.test(id) ? id : null;
}

let pending: { promise: Promise<string | null>; at: number; ok: boolean } | null = null;

/** Drop the per-page answer (tests; the sessionStorage entry stays). */
export function clearDataverseUserCache(): void {
  pending = null;
}

/**
 * The signed-in Dataverse user's Entra object id:
 *   - `undefined` — not a Dataverse host: there is no Dataverse session to match;
 *   - a promise of the lower-cased object id, or of `null` when it could not be read
 *     (the caller must then fail closed). Never rejects.
 */
export function dataverseUserObjectId(): Promise<string | null> | undefined {
  if (!isDataverseHost()) return undefined;
  if (pending && (pending.ok || Date.now() - pending.at < LOOKUP_FAILURE_BACKOFF_MS)) {
    return pending.promise;
  }
  const entry = { promise: Promise.resolve<string | null>(null), at: Date.now(), ok: false };
  entry.promise = readObjectId().then(oid => {
    entry.ok = oid !== null;
    return oid;
  });
  pending = entry;
  return entry.promise;
}

async function readObjectId(): Promise<string | null> {
  try {
    const ctx = xrmGlobalContext();
    let clientUrl = '';
    let userId: string | null = null;
    try {
      clientUrl = typeof ctx?.getClientUrl === 'function' ? String(ctx.getClientUrl() ?? '') : '';
    } catch {
      /* fall through */
    }
    try {
      userId = normalizeObjectId(ctx?.userSettings?.userId);
    } catch {
      /* fall through */
    }
    if (!clientUrl && isDataverseOrigin()) clientUrl = window.location.origin;
    if (!clientUrl) {
      console.warn('[SpaarkeAuth] Dataverse user check: no organization URL; no cached account will be used.');
      return null;
    }
    const api = `${clientUrl.replace(/\/+$/, '')}/api/data/v9.2`;

    if (!userId) {
      const who = await getJson(`${api}/WhoAmI`);
      userId = normalizeObjectId((who as { UserId?: unknown } | null)?.UserId);
      if (!userId) {
        console.warn('[SpaarkeAuth] Dataverse user check: WhoAmI gave no user id; no cached account will be used.');
        return null;
      }
    }

    const key = `${SS_PREFIX}${clientUrl.toLowerCase()}|${userId}`;
    const remembered = readSession(key);
    if (remembered) return remembered;

    const user = await getJson(`${api}/systemusers(${userId})?$select=azureactivedirectoryobjectid`);
    const oid = normalizeObjectId(
      (user as { azureactivedirectoryobjectid?: unknown } | null)?.azureactivedirectoryobjectid
    );
    if (!oid) {
      console.warn(
        '[SpaarkeAuth] Dataverse user check: the signed-in user has no readable Entra object id; ' +
          'no cached account will be used.'
      );
      return null;
    }
    writeSession(key, oid);
    return oid;
  } catch (err) {
    console.warn('[SpaarkeAuth] Dataverse user check failed; no cached account will be used.', err);
    return null;
  }
}

async function getJson(url: string): Promise<unknown> {
  const resp = await fetch(url, {
    method: 'GET',
    headers: { Accept: 'application/json', 'OData-MaxVersion': '4.0', 'OData-Version': '4.0' },
    credentials: 'include', // the Dataverse session cookie — no token
    signal: timeoutSignal(LOOKUP_TIMEOUT_MS),
  });
  if (!resp.ok) {
    console.warn(`[SpaarkeAuth] Dataverse user check: ${url.split('?')[0]} returned ${resp.status}`);
    return null;
  }
  return resp.json();
}

function readSession(key: string): string | null {
  try {
    return normalizeObjectId(sessionStorage.getItem(key));
  } catch {
    return null;
  }
}

function writeSession(key: string, oid: string): void {
  try {
    sessionStorage.setItem(key, oid);
  } catch {
    /* sessionStorage may be unavailable */
  }
}
