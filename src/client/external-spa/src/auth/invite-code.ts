/**
 * The invite code (unified-access-control-r2 task 185; design note
 * `projects/unified-access-control-r2/notes/185-invite-code-design.md` §4).
 *
 * The invite email's link carries `?invite={code}` next to `?customer={key}`. The code proves to the BFF that the
 * CIAM sign-in came from THIS environment's own invite (the first sign-in's email bind requires it). This module:
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

import { readCustomerQueryKey } from './backend-selection';

/** The query parameter the invite link carries the code in. */
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
 * Capture `?invite=` from the current URL: store a well-formed code for this tab and strip the parameter from the URL
 * (a malformed one is stripped and dropped). No `invite` parameter → nothing happens. Never throws.
 */
export function captureInviteCode(): void {
  try {
    const params = new URLSearchParams(window.location.search);
    if (!params.has(INVITE_QUERY_PARAM)) return;

    const code = params.get(INVITE_QUERY_PARAM);
    const customerKey = readCustomerQueryKey(window.location.search);
    params.delete(INVITE_QUERY_PARAM);
    const query = params.toString();
    window.history.replaceState(
      window.history.state,
      '',
      `${window.location.pathname}${query ? `?${query}` : ''}${window.location.hash}`
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
 * named (any customer when the link named none).
 */
export function inviteCodeFor(backend: { plane: string; customerKey: string }): string | null {
  if (backend.plane !== 'ciam') return null;
  const stored = readStored();
  if (!stored) return null;
  if (stored.customerKey !== null && stored.customerKey !== backend.customerKey) return null;
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
