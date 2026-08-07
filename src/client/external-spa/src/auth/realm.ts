/**
 * Home-realm discovery — which identity PLANE a browser (non-Teams) sign-in targets.
 *
 * The module-host SPA serves both planes from ONE URL (ADR-028 Amendment A3 — the dual-plane
 * external-app model is canonical). In a browser the plane cannot be inferred silently (there is
 * no host broker as in Teams), so the user makes an explicit "My organization / Partner" choice
 * (spec FR-03; email-domain sniffing is deferred — ux-brief §3). This module persists that choice
 * so it survives the MSAL login/token redirect and the subsequent page reload.
 *
 *   - 'workforce' → internal (incl. license-free workforce) user; workforce Entra authority.
 *   - 'ciam'      → external / outside-counsel user; Entra External ID (*.ciamlogin.com) authority.
 *
 * PER-TAB ISOLATION (NFR-05 / ADR-028 A1+A3): the realm is stored in `sessionStorage`, NOT
 * `localStorage` — deliberately consistent with the standalone-MSAL cache (each tab can be a
 * different user/organization/plane, so the plane choice must not leak across tabs). Do NOT switch
 * this to localStorage.
 *
 * The plane is NEVER surfaced inside module UI (ux-brief §4 "the identity plane is invisible"); it
 * only decides which authority the bootstrap authenticates against. The Teams host does its own
 * silent workforce SSO and never reads this (criterion 2 — no chooser inside Teams).
 */

/** The two browser sign-in planes (see module header). */
export type Realm = 'workforce' | 'ciam';

/** Per-tab sessionStorage key for the chosen sign-in plane. */
const REALM_KEY = 'spaarke.ext.realm';

/** Type guard: is a raw string one of the known realms? Rejects tampered/stale values fail-safe. */
function isRealm(value: string | null): value is Realm {
  return value === 'workforce' || value === 'ciam';
}

/**
 * Read the previously-chosen realm for THIS tab, if any. Returns null when the user has not yet
 * chosen (→ the bootstrap shows the realm chooser) or when sessionStorage is unavailable
 * (private mode / blocked) — in which case the chooser is shown again, which is the safe default.
 */
export function getStoredRealm(): Realm | null {
  try {
    const raw = sessionStorage.getItem(REALM_KEY);
    return isRealm(raw) ? raw : null;
  } catch {
    return null;
  }
}

/** Persist the chosen realm for this tab so it survives the MSAL redirect + reload. Best-effort. */
export function setStoredRealm(realm: Realm): void {
  try {
    sessionStorage.setItem(REALM_KEY, realm);
  } catch {
    // sessionStorage unavailable — the chooser will simply re-appear after the redirect; the
    // sign-in still completes because MSAL itself drives the redirect. Degrades gracefully.
  }
}

/** Clear the chosen realm (e.g. on sign-out) so the next sign-in re-prompts. Best-effort. */
export function clearStoredRealm(): void {
  try {
    sessionStorage.removeItem(REALM_KEY);
  } catch {
    // no-op — see setStoredRealm.
  }
}
