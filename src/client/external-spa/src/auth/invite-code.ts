/**
 * The invite code (unified-access-control-r2 task 185; design note
 * `projects/unified-access-control-r2/notes/185-invite-code-design.md` §4).
 *
 * The invite email's link carries the code in its fragment, `?customer={key}#invite={code}` (§12 I-12: a fragment is
 * never sent to the SPA host, so no CDN, WAF or front-door log sees it); `?invite={code}` is still accepted. The code
 * proves to the BFF that the CIAM sign-in came from THIS environment's own invite (the first sign-in's email bind
 * requires it). This module:
 *   - captures it on landing, FIRST in `main()` and before MSAL runs: it stores the code in `sessionStorage` (per tab,
 *     like the realm and the MSAL cache: ADR-028 A1/A3, never `localStorage`) and removes `invite` from the address
 *     bar at once with `history.replaceState`, keeping `customer` and every other parameter. The clean-up must come
 *     first: `captureReturnTo()` and MSAL's return-to-start-URL would otherwise carry the code across the sign-in
 *     redirect and back into the address bar and history;
 *   - hands it to `bff-client.ts`, which sends it as the `X-Spaarke-Invite-Code` header on CIAM-plane BFF calls for
 *     the customer the link named (a code for customer A is never sent to customer B's origin);
 *   - clears it after the first successful CIAM-plane call (the contact is then resolved by its sign-in id and the code
 *     has no further use), on sign-out and on "Choose a different sign-in". A 403 keeps it, so "Sign in again" with
 *     the right account in the same tab still presents it.
 *
 * The code is never logged, and never written anywhere but this one sessionStorage entry.
 */

import { hasSingleConfiguredBackend, readCustomerQueryKey } from './backend-selection';

/** The parameter the invite link carries the code in: `#invite=` (§12 I-12), or `?invite=` (accepted as a fallback). */
export const INVITE_QUERY_PARAM = 'invite';

/** Per-tab sessionStorage key for the captured code. */
export const INVITE_CODE_STORAGE_KEY = 'spaarke.ext.inviteCode';

/** The request header the BFF reads the code from (`CiamContactPrincipalStrategy.InviteCodeHeader`). */
export const INVITE_CODE_HEADER = 'X-Spaarke-Invite-Code';

/** A code: 32 bytes as unpadded base64url, exactly 43 characters (the BFF's `InviteCode.IsWellFormed`). */
const INVITE_CODE_PATTERN = /^[A-Za-z0-9_-]{43}$/;

interface StoredInviteCode {
  code: string;
  /** The `?customer=` key of the invite link, or null when the link named none. */
  customerKey: string | null;
}

/** True when `value` has the shape of an invite code. */
export function isWellFormedInviteCode(value: unknown): value is string {
  return typeof value === 'string' && INVITE_CODE_PATTERN.test(value);
}

/**
 * Split `parts` (a query string without `?`, or a fragment without `#`) on `&` and take out every `invite=` segment.
 * Returns the first `invite` value (decoded; null when there is none) and the remaining segments joined exactly as they
 * were (never re-encoded), so every other parameter and a plain fragment such as `section` stay untouched.
 */
function takeInvite(parts: string): { found: boolean; value: string | null; rest: string } {
  let found = false;
  let value: string | null = null;
  const kept: string[] = [];
  for (const segment of parts.split('&')) {
    const eq = segment.indexOf('=');
    const rawKey = eq < 0 ? segment : segment.slice(0, eq);
    let key: string;
    try {
      key = decodeURIComponent(rawKey.replace(/\+/g, ' '));
    } catch {
      key = rawKey;
    }
    if (key === INVITE_QUERY_PARAM) {
      if (!found) {
        const raw = eq < 0 ? '' : segment.slice(eq + 1);
        try {
          value = decodeURIComponent(raw.replace(/\+/g, ' '));
        } catch {
          value = null;
        }
      }
      found = true;
    } else if (segment !== '') {
      kept.push(segment);
    }
  }
  return { found, value, rest: kept.join('&') };
}

/**
 * Capture the invite code from the current URL: `#invite=` (the link's form since §12 I-12: a fragment never reaches
 * the SPA host, CDN, WAF or front-door logs) and, as a fallback, `?invite=`. When both are present the fragment wins.
 * Both are stripped from the URL with `history.replaceState` (a malformed code is stripped and dropped), keeping every
 * other query parameter and fragment part as it was. MSAL's own redirect response (`#code=…&state=…`) carries no
 * `invite`, so it is never touched: this runs first in `main()`, before MSAL, and only rewrites a URL that carries an
 * invite. No `invite` anywhere → nothing happens. Never throws.
 */
export function captureInviteCode(): void {
  try {
    const search = window.location.search.replace(/^\?/, '');
    const hash = window.location.hash.replace(/^#/, '');
    const fromQuery = takeInvite(search);
    const fromHash = takeInvite(hash);
    if (!fromQuery.found && !fromHash.found) return;

    const code = fromHash.found ? fromHash.value : fromQuery.value;
    const customerKey = readCustomerQueryKey(window.location.search);
    const query = fromQuery.found ? fromQuery.rest : search;
    const fragment = fromHash.found ? fromHash.rest : hash;
    window.history.replaceState(
      window.history.state,
      '',
      `${window.location.pathname}${query ? `?${query}` : ''}${fragment ? `#${fragment}` : ''}`
    );

    if (isWellFormedInviteCode(code)) {
      const entry: StoredInviteCode = { code, customerKey };
      sessionStorage.setItem(INVITE_CODE_STORAGE_KEY, JSON.stringify(entry));
    }
  } catch {
    // sessionStorage or history unavailable: there is no code to carry, and the sign-in proceeds without one (the BFF
    // then answers "open the invitation link from your email in this browser").
  }
}

function readStored(): StoredInviteCode | null {
  try {
    const raw = sessionStorage.getItem(INVITE_CODE_STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<StoredInviteCode>;
    if (!isWellFormedInviteCode(parsed.code)) return null;
    return { code: parsed.code, customerKey: typeof parsed.customerKey === 'string' ? parsed.customerKey : null };
  } catch {
    return null;
  }
}

/**
 * The stored code to send to `backend`, or null. Only on the CIAM plane, and only to the customer the invite link
 * named (any customer when the link named none). Pass 1 K1: when this SPA's backend directory is exactly ONE backend
 * (e.g. the `default` dev entry), that backend is the only one the link can mean, so a different `customer` key still
 * matches. With several backends a different key never matches.
 */
export function inviteCodeFor(backend: { plane: string; customerKey: string }): string | null {
  if (backend.plane !== 'ciam') return null;
  const stored = readStored();
  if (!stored) return null;
  if (
    stored.customerKey !== null &&
    stored.customerKey !== backend.customerKey &&
    !hasSingleConfiguredBackend('ciam')
  ) {
    return null;
  }
  return stored.code;
}

/** Forget the stored code. Best-effort. */
export function clearInviteCode(): void {
  try {
    sessionStorage.removeItem(INVITE_CODE_STORAGE_KEY);
  } catch {
    /* sessionStorage unavailable: nothing is stored */
  }
}
