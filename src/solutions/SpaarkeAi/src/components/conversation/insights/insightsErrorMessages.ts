/**
 * insightsErrorMessages.ts — Binding 12-code → user-facing message map for the
 * Insights Assistant chat tool (R5 task 029 / D2-19).
 *
 * SOURCE OF TRUTH: `projects/spaarke-ai-platform-unification-r5/notes/
 *   insights-engine-assistant-integration-brief.md` §5.1 column 4.
 *
 * The 12 codes are the FR-16 binding contract. The Insights team
 * minor-versions the contract to add forward-compat codes; v1.0 ships with
 * the 12 codes below.
 *
 * ADR-018 (no information leakage): the renderer surfaces ONLY the column-4
 * message + `correlationId` (+ optional Retry-After UX). It NEVER renders
 * `detail`, `title`, or unknown ProblemDetails extensions — those carry
 * operator-relevant diagnostics that are NOT user-facing.
 *
 * Per R5 CLAUDE.md §3.2 / ADR-018: no new feature flags. Per CLAUDE.md §3.1
 * reuse mandate: this module imports the `InsightsQueryError` type from the
 * EXISTING HTTP client (task 025) — no parallel error class.
 *
 * @see types.ts — `InsightsErrorResponse` (the renderer's input shape)
 * @see insightsQueryClient.ts — `InsightsQueryError` (the HTTP client's typed throw)
 * @see InsightsErrorRenderer.tsx — consumer (the error-state branch of the renderer)
 */

import type { InsightsErrorCode } from '../../../services/insightsQueryClient';

// ---------------------------------------------------------------------------
// 12-code → user-message constant map (binding)
// ---------------------------------------------------------------------------

/**
 * The canonical mapping from `errorCode` (stable extension or synthetic key)
 * to the user-facing message. Text is taken VERBATIM from integration brief
 * §5.1 column 4 except where the brief gives only behavioral guidance — in
 * which case the quoted text is used as-is + minor punctuation polished for
 * conversational tone.
 *
 * Keys are the stable `errorCode` extensions from the contract PLUS two
 * synthetic keys for 401 / 429 (which do NOT carry an `errorCode` per the
 * contract):
 *
 *   - `auth.401` — synthetic key emitted by the HTTP client (task 025) when
 *     reauth is required. The renderer first attempts reauth via
 *     `@spaarke/auth`; the message is only surfaced if reauth fails.
 *   - `rate-limit.429` — synthetic key emitted by the HTTP client. The
 *     renderer surfaces this with the `Retry-After`-driven countdown UX;
 *     the message includes a `{seconds}` placeholder substituted at render
 *     time.
 *
 * Unknown codes (future v1.1+ additions OR transport-level failures) fall
 * back to the generic `INSIGHTS_ASSISTANT_INTERNAL_ERROR` message — same UX
 * + same correlationId surface so support can investigate.
 */
export const INSIGHTS_ERROR_USER_MESSAGES: Readonly<Record<InsightsErrorCode, string>> = Object.freeze({
  // 400-class (5 codes) — non-retryable; surface per-code wording.
  'query.required':
    "I couldn't understand the question.",
  'subject.required':
    "I couldn't understand the question.",
  'subject.invalid':
    "Couldn't identify the entity — try again from a matter, project, or invoice context.",
  'forceMode.invalid':
    'Something went wrong — try again.',
  'conversationContext.invalid':
    'Conversation context too long — starting a fresh thread.',
  // 401 — synthetic; only surfaced when reauth FAILS (post-reauth-failure path).
  'auth.401':
    'Your session expired — sign in again.',
  // 429 — synthetic; surfaced with `Retry-After`-driven countdown.
  // `{seconds}` is substituted at render time from the parsed Retry-After header.
  'rate-limit.429':
    'Slow down a moment — you can try again in {seconds} seconds.',
  // 503 (4 codes) — kill-switches; non-retryable except for the
  // `ai.intent-classification.disabled` retry-with-forceMode path (handled
  // structurally by the retry state machine, not via this message).
  'ai.insights.disabled':
    'Insights temporarily disabled — please try again later.',
  'ai.rag.disabled':
    'Knowledge search temporarily disabled — please try again later.',
  'ai.intent-classification.disabled':
    'Insights temporarily disabled — please try again later.',
  'ai.assistant-default-playbook.unconfigured':
    "Something's misconfigured — try again later or contact support.",
  // 500 — retryable ONCE; this message is surfaced after retry exhaustion.
  INSIGHTS_ASSISTANT_INTERNAL_ERROR:
    'Something went wrong — try again. If this keeps happening, contact support with the ID below.',
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Lookup the user-facing message for an error code. Unknown codes fall back
 * to the generic internal-error message so the user always sees a
 * deterministic, ADR-018-compliant message.
 *
 * For `rate-limit.429`, the caller MUST substitute the `{seconds}`
 * placeholder via `formatRateLimitMessage(seconds)` (defined below) — this
 * function does NOT perform the substitution because it has no knowledge of
 * the `Retry-After` header.
 */
export function getUserMessageForErrorCode(
  errorCode: string | undefined | null,
): string {
  if (!errorCode) {
    return INSIGHTS_ERROR_USER_MESSAGES.INSIGHTS_ASSISTANT_INTERNAL_ERROR;
  }
  // Narrow to the known key set; unknown codes fall through.
  if (Object.prototype.hasOwnProperty.call(INSIGHTS_ERROR_USER_MESSAGES, errorCode)) {
    return INSIGHTS_ERROR_USER_MESSAGES[errorCode as InsightsErrorCode];
  }
  return INSIGHTS_ERROR_USER_MESSAGES.INSIGHTS_ASSISTANT_INTERNAL_ERROR;
}

/**
 * Substitute `{seconds}` in the rate-limit message with a non-negative
 * integer count. Returns the unmodified template if `seconds` is undefined
 * (defensive — should never happen because the HTTP client always parses
 * `Retry-After` for 429 responses).
 */
export function formatRateLimitMessage(seconds: number | undefined): string {
  const template = INSIGHTS_ERROR_USER_MESSAGES['rate-limit.429'];
  if (seconds === undefined || !Number.isFinite(seconds) || seconds < 0) {
    return template;
  }
  const clamped = Math.max(0, Math.floor(seconds));
  return template.replace('{seconds}', String(clamped));
}

/**
 * Stable set of error codes for which the renderer renders a "Try again" CTA
 * (manual user-initiated retry — distinct from the auto-retry policy in
 * `insightsRetryPolicy.ts`). This list is the union of:
 *
 *   - All 5 400-class codes (the user may be able to fix + retry, e.g. by
 *     attaching context or rephrasing). The CTA does NOT auto-rephrase;
 *     it re-issues the same request to give the user a second click chance
 *     in case of a transient validation glitch.
 *   - 429 — after the Retry-After countdown completes.
 *   - 500 — after auto-retry exhaustion, manual retry remains available.
 *   - 503 kill-switches — manual retry tests if ops has restored the gate.
 *
 * `auth.401` (post-reauth-failure) is NOT in the CTA set — the user must
 * sign in again, not retry the call.
 */
export const RETRYABLE_VIA_MANUAL_CLICK: ReadonlySet<InsightsErrorCode> = Object.freeze(
  new Set<InsightsErrorCode>([
    'query.required',
    'subject.required',
    'subject.invalid',
    'forceMode.invalid',
    'conversationContext.invalid',
    'rate-limit.429',
    'ai.insights.disabled',
    'ai.rag.disabled',
    'ai.intent-classification.disabled',
    'ai.assistant-default-playbook.unconfigured',
    'INSIGHTS_ASSISTANT_INTERNAL_ERROR',
  ]),
);
