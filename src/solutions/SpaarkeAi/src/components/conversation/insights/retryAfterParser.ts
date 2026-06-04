/**
 * retryAfterParser.ts — RFC 7231 §7.1.3 `Retry-After` header parser
 * (R5 task 029 / D2-19).
 *
 * Per ADR-016 + RFC 7231: `Retry-After` MAY be either:
 *   - an integer (delta-seconds), OR
 *   - an HTTP-date (RFC 1123 / RFC 850 / asctime).
 *
 * The parser accepts both forms and returns delta-seconds (an integer, rounded
 * UP via `Math.ceil` to ensure the user-facing countdown is never shorter
 * than the server's actual hold-off). When parsing fails OR when the header
 * is missing, returns `undefined` — callers fall back to a default countdown
 * (typically 60s, the contract's aggregate `ai-context` window).
 *
 * The renderer's 429 UX is MANUAL-CLICK ONLY — no auto-retry per ADR-016.
 * This parser is therefore solely a UX helper (driving the countdown), NOT
 * a retry scheduler.
 *
 * `now` is injectable for unit-test determinism (avoids `Date.now()` fakery
 * in test environments that don't ship `jest.useFakeTimers()` globally).
 *
 * @see RFC 7231 §7.1.3 — https://datatracker.ietf.org/doc/html/rfc7231#section-7.1.3
 * @see ADR-016 — rate-limit honoring
 */

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Parse a `Retry-After` header value into delta-seconds.
 *
 * @param value  The raw header value (e.g. `"30"` or `"Wed, 21 Oct 2026 07:28:30 GMT"`).
 *               `null`, `undefined`, or empty strings return `undefined`.
 * @param now    Reference time for HTTP-date deltas. Defaults to `Date.now()`.
 *               Injectable for deterministic unit tests.
 * @returns      Non-negative integer delta-seconds, or `undefined` if the
 *               header value is missing OR unparseable.
 */
export function parseRetryAfter(
  value: string | null | undefined,
  now: number = Date.now(),
): number | undefined {
  if (value === null || value === undefined) {
    return undefined;
  }
  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return undefined;
  }

  // Form 1: delta-seconds integer (RFC 7231 §7.1.3).
  // Per the spec, the value MUST be a non-negative integer when this form is
  // used. We tolerate leading "+0" defensively but reject negative values
  // (server bug — surface as "unparseable" rather than negative countdown).
  if (/^-?\d+$/.test(trimmed)) {
    const seconds = Number.parseInt(trimmed, 10);
    if (Number.isFinite(seconds) && seconds >= 0) {
      return seconds;
    }
    return undefined;
  }

  // Form 2: HTTP-date (RFC 1123 / RFC 850 / asctime). Parse via `Date.parse()`
  // (browser-native, accepts all three RFC formats). Compute delta-seconds
  // from `now`. Past dates produce 0 (caller may interpret as "try now").
  const parsedMs = Date.parse(trimmed);
  if (!Number.isFinite(parsedMs)) {
    return undefined;
  }
  const deltaMs = parsedMs - now;
  if (deltaMs <= 0) {
    return 0;
  }
  // Round UP — the user-facing countdown should never be shorter than the
  // server's actual hold-off.
  return Math.ceil(deltaMs / 1000);
}
