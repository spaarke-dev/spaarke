/**
 * Auth-failure diagnostics for the Teams bootstrap error screen (moved out of main.tsx so it is testable;
 * main.tsx runs the bootstrap on import).
 */

/**
 * Extracts the underlying NAA + MSAL-popup failure detail (error name / MSAL errorCode / subError /
 * any AADSTS code / message) from a TeamsWorkforceAuthError's `cause` (`{ naaError, popupError }`, ADR-028 A7),
 * so the on-screen error state can show the REAL diagnostic on hosts where dev tools aren't readily
 * available (e.g. the Teams desktop client). Diagnostic-only; contains no tokens or secrets.
 */
export function extractAuthDiagnostics(err: unknown): string {
  const describe = (label: string, e: unknown): string => {
    if (e == null) return `${label}: (none)`;
    if (typeof e === 'string') return `${label}: ${e}`;
    const any = e as Record<string, unknown>;
    const parts: string[] = [];
    if (any.name) parts.push(`name=${String(any.name)}`);
    if (any.errorCode) parts.push(`errorCode=${String(any.errorCode)}`);
    if (any.subError) parts.push(`subError=${String(any.subError)}`);
    const msg = (any.errorMessage ?? any.message) as string | undefined;
    if (msg) parts.push(`msg=${String(msg).slice(0, 600)}`);
    const aadsts = `${String(any.message ?? '')} ${String(any.errorMessage ?? '')}`.match(/AADSTS\d+/);
    if (aadsts) parts.push(`AADSTS=${aadsts[0]}`);
    if (parts.length === 0) {
      try {
        return `${label}: ${JSON.stringify(any).slice(0, 400)}`;
      } catch {
        return `${label}: (unserializable)`;
      }
    }
    return `${label}: ${parts.join('  ·  ')}`;
  };
  const cause = (err as { cause?: unknown } | null)?.cause as { naaError?: unknown; popupError?: unknown } | undefined;
  const lines = [describe('top', err)];
  if (cause && typeof cause === 'object') {
    lines.push(describe('NAA', cause.naaError));
    lines.push(describe('Popup', cause.popupError));
  }
  return lines.join('\n');
}
