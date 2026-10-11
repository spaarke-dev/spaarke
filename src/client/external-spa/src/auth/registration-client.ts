/**
 * Join-link client — the SPA side of workforce-contact self-registration (spaarke-SPA-external-access-platform-r3
 * task 056; spec FR-13; decision: notes/spikes/fr-12-self-registration-decision.md, Option B, owner 2026-10-11).
 *
 * Under Option B the "registration-only client" is SERVER-SIDE: the shared registration service (task 060) runs the
 * home-tenant OIDC sign-in (`/organizations`, `openid profile` + the `acct` claim) with its own confidential client
 * and an MI-FIC credential, decides, invites and adds the group. The SPA therefore creates NO MSAL instance here,
 * acquires NO token and requests NO scope — BFF or otherwise. It only:
 *   1. validates the `?customer={key}` of `/join?customer={key}` through the task-041 seam (`backend-selection.ts`);
 *   2. sends the browser to the FIXED registration service URL from build configuration
 *      (`VITE_REGISTRATION_SERVICE_URL`) with `customer` and a one-time `state` — never a URL, scope or return address
 *      taken from the link (X2 / NFR-02);
 *   3. reads the service's return (`result` + `state`, contract below) and maps it to a fixed outcome — free text from
 *      the URL is never shown;
 *   4. hands off to the normal workforce sign-in for that customer (Spaarke-tenant authority, task 040/041).
 *
 * The whole page is behind `VITE_JOIN_ENABLED` (default off in every environment) until task 060 ships the service.
 *
 * RETURN CONTRACT (task 060 must honour it; also in notes/task-056-notes.md):
 *   - Start:  GET {VITE_REGISTRATION_SERVICE_URL}?customer={key}&state={state}
 *   - Return: 302 to {SPA origin}/join?customer={key}&state={state}&result={code}. The service's return address is its
 *             own configuration (never a parameter of the start request). `state` is echoed unchanged.
 *   - `result` ∈ {@link JOIN_RESULT_CODES}; anything else (or a missing value) is treated as `error`.
 */

import { readOptionalEnv } from '../config';
import { setStoredRealm } from './realm';
import {
  clearActiveBackend,
  createBackendDirectory,
  isCustomerKey,
  readCustomerQueryKey,
  validateBackend,
  type BackendDirectory,
} from './backend-selection';

// ---------------------------------------------------------------------------
// Flag, path and service URL
// ---------------------------------------------------------------------------

/** The join route. Handled by the bootstrap (main.tsx) BEFORE the realm chooser and MSAL — it is a pre-sign-in page. */
export const JOIN_PATH = '/join';

/** True for `/join` and `/join/` (case-sensitive, like every other SPA route). */
export function isJoinPath(pathname: string): boolean {
  return pathname === JOIN_PATH || pathname === `${JOIN_PATH}/`;
}

/** The build-time flag. Only the exact string `true` turns the page on; unset, empty or a placeholder is off. */
export function isJoinEnabled(): boolean {
  return readOptionalEnv('VITE_JOIN_ENABLED') === 'true';
}

/**
 * The registration service's start URL from build configuration, or `null` when it is unset or refused. Accepted: an
 * absolute https URL (http only on localhost / 127.0.0.1, for local work) with no credentials, query or fragment.
 * A `null` keeps the page in the "not available" state (fail closed) even with the flag on.
 */
export function getRegistrationServiceUrl(): string | null {
  const raw = readOptionalEnv('VITE_REGISTRATION_SERVICE_URL');
  if (!raw) return null;
  let url: URL;
  try {
    url = new URL(raw);
  } catch {
    return null;
  }
  const localHttp = url.protocol === 'http:' && (url.hostname === 'localhost' || url.hostname === '127.0.0.1');
  const ok =
    (url.protocol === 'https:' || localHttp) &&
    !url.username &&
    !url.password &&
    !url.search &&
    !url.hash &&
    !raw.includes('?') &&
    !raw.includes('#');
  return ok ? url.href : null;
}

// ---------------------------------------------------------------------------
// Customer key (the task-041 seam)
// ---------------------------------------------------------------------------

/** Outcome of the key check. `unknown` covers a missing, malformed or unlisted key AND a directory failure. */
export type JoinCustomer = { kind: 'known'; customerKey: string } | { kind: 'unknown' };

/**
 * Validate `?customer=` exactly as task 041 does: the key must be well formed (`readCustomerQueryKey`) and must name a
 * backend that Spaarke-owned data returns (the T240c directory, or dev configuration until it ships); the record is
 * re-validated (`validateBackend`). The key lookup (`resolveByKey`) is used rather than the workforce membership
 * lookup, because a person who has not joined yet is not a member of any customer group.
 *
 * Every negative case — missing, malformed, unknown, a record for another key, a refused record, a directory error —
 * returns the same `unknown`, so the page shows one neutral message and is no oracle beyond "this link works".
 */
export async function resolveJoinCustomer(
  search: string = typeof window !== 'undefined' ? window.location.search : '',
  directory?: BackendDirectory
): Promise<JoinCustomer> {
  const key = readCustomerQueryKey(search);
  if (!key) return { kind: 'unknown' };
  try {
    const record = await (directory ?? createBackendDirectory('workforce')).resolveByKey(key);
    if (!record) return { kind: 'unknown' };
    const backend = validateBackend(record);
    return backend.customerKey === key ? { kind: 'known', customerKey: key } : { kind: 'unknown' };
  } catch (err) {
    console.warn('[Join] The customer key could not be checked:', err);
    return { kind: 'unknown' };
  }
}

// ---------------------------------------------------------------------------
// Return contract
// ---------------------------------------------------------------------------

/**
 * The `result` codes the registration service returns (task 060 contract; spike §4 steps 3–6 and §6 F1/F5):
 *   - `registered`          — invited (no email) and added to `sprk-{customerId}-users`; hand off to sign-in.
 *   - `already_registered`  — an existing guest was reused (ObjectConflict, F1); group ensured; hand off to sign-in.
 *   - `tenant_not_allowed`  — home tenant not in the customer's `CustomerTenantIds` (or the list is empty). Nothing
 *                             is created.
 *   - `guest_account`       — `acct` missing or not `0`: the account is a guest of another organization.
 *   - `account_conflict`    — the address belongs to an existing MEMBER of Spaarke's tenant (spike §4 step 4).
 *   - `consent_required`    — the home tenant blocks user consent to "Spaarke Registration" (F5).
 *   - `cancelled`           — the user cancelled the home sign-in (OIDC `access_denied`).
 *   - `error`               — anything else (including a `state` the service could not round-trip).
 */
export const JOIN_RESULT_CODES = [
  'registered',
  'already_registered',
  'tenant_not_allowed',
  'guest_account',
  'account_conflict',
  'consent_required',
  'cancelled',
  'error',
] as const;

export type JoinResultCode = (typeof JOIN_RESULT_CODES)[number];

/** The service's return, as read from the query string. `null` when there is no `result` (the first landing). */
export interface JoinReturn {
  result: JoinResultCode;
  state: string | null;
}

/** Read `result` + `state`. An unrecognised `result` becomes `error`; no other parameter is read. */
export function parseJoinReturn(search: string): JoinReturn | null {
  let params: URLSearchParams;
  try {
    params = new URLSearchParams(search);
  } catch {
    return null;
  }
  if (!params.has('result')) return null;
  const raw = params.get('result');
  const result = (JOIN_RESULT_CODES as readonly string[]).includes(raw ?? '') ? (raw as JoinResultCode) : 'error';
  return { result, state: params.get('state') };
}

// ---------------------------------------------------------------------------
// One-time state (binds a return to the start made in this tab, for this customer)
// ---------------------------------------------------------------------------

/** sessionStorage: `{ state, customerKey }` of the registration started in this tab. Never a URL, scope or token. */
const JOIN_STATE_STORAGE_KEY = 'spaarke.ext.joinState';

const STATE_PATTERN = /^[0-9a-f]{32}$/;

function randomState(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
}

/** Create and remember a one-time state for a registration of `customerKey` started in this tab. */
export function createJoinState(customerKey: string): string {
  const state = randomState();
  try {
    sessionStorage.setItem(JOIN_STATE_STORAGE_KEY, JSON.stringify({ state, customerKey }));
  } catch {
    // sessionStorage unavailable: the return cannot be matched and shows the neutral "start again" state.
  }
  return state;
}

/**
 * True only when `state` is the one this tab created for `customerKey`. The stored state is removed either way
 * (one-time), so a replayed or crafted return link shows the neutral "start again" state instead of an outcome.
 */
export function consumeJoinState(state: string | null, customerKey: string): boolean {
  let stored: unknown = null;
  try {
    const raw = sessionStorage.getItem(JOIN_STATE_STORAGE_KEY);
    sessionStorage.removeItem(JOIN_STATE_STORAGE_KEY);
    stored = raw ? JSON.parse(raw) : null;
  } catch {
    return false;
  }
  const s = stored as { state?: unknown; customerKey?: unknown } | null;
  return !!state && STATE_PATTERN.test(state) && !!s && s.state === state && s.customerKey === customerKey;
}

// ---------------------------------------------------------------------------
// Navigation
// ---------------------------------------------------------------------------

/** How the page leaves the SPA (a full-page navigation). Injected in tests. */
export type Navigate = (url: string) => void;

export const assignLocation: Navigate = url => window.location.assign(url);

/**
 * The registration start URL: the configured service URL plus `customer` and `state` — nothing else. No scope, no
 * client id, no return address: the service owns all three (spike §4 step 2).
 */
export function buildRegistrationStartUrl(serviceUrl: string, customerKey: string, state: string): string {
  if (!isCustomerKey(customerKey)) throw new Error('[Join] Refusing to start registration for a malformed key.');
  const url = new URL(serviceUrl);
  url.search = '';
  url.searchParams.set('customer', customerKey);
  url.searchParams.set('state', state);
  return url.href;
}

/** Send the browser to the registration service for `customerKey`. Throws when the service URL is not configured. */
export function startRegistration(customerKey: string, navigate: Navigate = assignLocation): void {
  const serviceUrl = getRegistrationServiceUrl();
  if (!serviceUrl) throw new Error('[Join] The registration service is not configured.');
  navigate(buildRegistrationStartUrl(serviceUrl, customerKey, createJoinState(customerKey)));
}

/**
 * Hand off to the normal workforce sign-in for `customerKey` (spike §4 step 6): the tab's realm becomes "workforce"
 * (no chooser), any previous selection is cleared, and the SPA root loads with `?customer={key}`, so the task-041
 * bootstrap selects that customer's backend and the AuthGuard starts the Spaarke-tenant sign-in.
 */
export function startWorkforceSignIn(customerKey: string, navigate: Navigate = assignLocation): void {
  if (!isCustomerKey(customerKey)) throw new Error('[Join] Refusing to hand off for a malformed key.');
  setStoredRealm('workforce');
  clearActiveBackend();
  navigate(`/?customer=${encodeURIComponent(customerKey)}`);
}
