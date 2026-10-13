/**
 * tokenDiagnostics — task 113 (customer-provisioning-orchestration-r1 request R1, 2026-10-07): the claims a guest
 * sign-in test needs to read off the backend access token, for the dev-only Diagnostics view.
 *
 * DISPLAY ONLY. The token is decoded, never verified, and never shown, logged or copied — only the named claims
 * below leave this module. Authorization stays with the BFF, which validates the token on every call.
 */

/** One row of the Diagnostics view. */
export interface DiagnosticRow {
  label: string;
  value: string;
}

/** The claims the guest test reads, in display order (provisioning's R1: tid, oid, acct, idp, iss, aud). */
const CLAIM_ROWS: ReadonlyArray<{ claim: string; label: string }> = [
  { claim: 'tid', label: 'tid (tenant that issued the token)' },
  { claim: 'oid', label: 'oid (user object id in that tenant)' },
  { claim: 'acct', label: 'acct (account status)' },
  { claim: 'idp', label: 'idp (home identity provider)' },
  { claim: 'iss', label: 'iss (issuer)' },
  { claim: 'aud', label: 'aud (audience)' },
];

const NOT_PRESENT = '(not present)';

/**
 * The payload of a JWT, or `null` when the value is not a three-part token with a JSON payload. Never throws.
 */
export function decodeJwtPayload(token: string | null | undefined): Record<string, unknown> | null {
  const parts = token?.split('.');
  if (!parts || parts.length !== 3 || !parts[1]) return null;
  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const json = decodeURIComponent(
      Array.from(atob(padded), c => `%${c.charCodeAt(0).toString(16).padStart(2, '0')}`).join('')
    );
    const payload: unknown = JSON.parse(json);
    return payload && typeof payload === 'object' && !Array.isArray(payload)
      ? (payload as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

function formatClaim(claim: string, value: unknown): string {
  if (value === undefined || value === null || value === '') {
    // `idp` is emitted for guests only (their home issuer); its absence is itself the answer for a member.
    return claim === 'idp' ? `${NOT_PRESENT} — not a guest sign-in` : NOT_PRESENT;
  }
  if (claim === 'acct') {
    // Entra `acct`: 0 = member of the issuing tenant, 1 = guest.
    if (value === 0 || value === '0') return '0 — member';
    if (value === 1 || value === '1') return '1 — guest';
  }
  return Array.isArray(value) ? value.join(', ') : String(value);
}

/** The Diagnostics view's claim rows for a backend access token (all "(not present)" for an undecodable token). */
export function describeTokenClaims(token: string | null | undefined): DiagnosticRow[] {
  const payload = decodeJwtPayload(token) ?? {};
  return CLAIM_ROWS.map(({ claim, label }) => ({ label, value: formatClaim(claim, payload[claim]) }));
}

/** Plain-text form of the rows, for the view's Copy button (the owner pastes it into the test record). */
export function diagnosticsAsText(rows: readonly DiagnosticRow[]): string {
  return rows.map(r => `${r.label}: ${r.value}`).join('\n');
}
