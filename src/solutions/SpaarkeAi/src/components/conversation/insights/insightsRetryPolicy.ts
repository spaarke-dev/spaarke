/**
 * insightsRetryPolicy.ts — Binding retry-decision state machine for the
 * Insights Assistant chat tool (R5 task 029 / D2-19).
 *
 * Two retry policies are encoded — both unconditional client-side behavior
 * per R5 CLAUDE.md §3.2 (no feature flags):
 *
 *   1. **503 `ai.intent-classification.disabled` + no `forceMode` + intent
 *      signal available** → retry ONCE with explicit `forceMode` set to the
 *      inferred intent. This recovers value when the user invoked a named
 *      tool / slash command and the classifier is operationally disabled.
 *
 *   2. **500 `INSIGHTS_ASSISTANT_INTERNAL_ERROR`** → retry ONCE with 1s
 *      backoff. Handles transient BFF failures (e.g., brief Service Bus
 *      blip) without forcing manual user retry; the 1s backoff prevents
 *      thundering-herd on a genuinely struggling BFF.
 *
 * Hard cap: `attemptNumber >= 2` → `no-retry` regardless of code. NO
 * infinite-loop possible. This cap is the load-bearing safety invariant.
 *
 * All other codes are NON-RETRYABLE via the auto-retry path:
 *   - 400-class (5 codes): client-side bugs / malformed inputs; surfacing
 *     the per-code message is the correct response.
 *   - 401: handled out-of-band via `@spaarke/auth` reauth flow (single
 *     post-reauth retry counts toward the hard cap).
 *   - 429: ADR-016 compliance — `Retry-After` countdown + manual click;
 *     NO auto-retry inside the renderer.
 *   - 503 `ai.insights.disabled` / `ai.rag.disabled` /
 *     `ai.assistant-default-playbook.unconfigured`: kill-switches / config
 *     bugs; surfacing the per-code message is correct.
 *
 * @see insightsErrorMessages.ts — per-code user message map
 * @see InsightsErrorRenderer.tsx — consumer (the renderer's error-state branch)
 * @see projects/spaarke-ai-platform-unification-r5/notes/insights-engine-assistant-integration-brief.md §5.1
 */

// ---------------------------------------------------------------------------
// Public types
// ---------------------------------------------------------------------------

/** The discriminated-union result of `decideRetry`. */
export type RetryDecision =
  | { readonly kind: 'no-retry' }
  | {
      readonly kind: 'retry-with-force-mode';
      /** The `forceMode` value the retry MUST set on the new request. */
      readonly forceMode: 'playbook' | 'rag';
    }
  | {
      readonly kind: 'retry-after-backoff';
      /** Milliseconds to await before re-issuing the request. Always 1000 in v1.0. */
      readonly backoffMs: number;
    };

/** Inputs to the retry-decision function — the failed response envelope. */
export interface RetryDecisionResponseInput {
  /** HTTP status code from the failed response. */
  readonly status: number;
  /** Stable `errorCode` extension from ProblemDetails (per ADR-019), if present. */
  readonly errorCode?: string;
}

/** Inputs describing the original request that triggered the failure. */
export interface RetryDecisionRequestInput {
  /**
   * `forceMode` set on the ORIGINAL request (before retry consideration). If
   * the original request already carried `forceMode` and the server still
   * returned 503 `ai.intent-classification.disabled`, that indicates a
   * contract bug — log diagnostic and do NOT retry.
   */
  readonly forceMode?: 'playbook' | 'rag' | null;
  /**
   * Intent signal available client-side, distinct from the actual `forceMode`
   * sent on the request. Source: the slash-command path (task 019) records
   * the inferred intent even when the original request omitted `forceMode`
   * (e.g., to let the BFF classifier handle the routing). When the BFF
   * subsequently returns 503 `ai.intent-classification.disabled`, R5 can
   * retry with the recorded intent — bypassing the disabled classifier.
   */
  readonly intentSignal?: 'playbook' | 'rag' | null;
}

// ---------------------------------------------------------------------------
// State machine
// ---------------------------------------------------------------------------

/** Hard cap: no more than ONE auto-retry per logical user turn. */
const MAX_AUTO_RETRY_ATTEMPTS = 2;

/** Backoff for the 500 retry path. Hard-coded per contract; not a config knob. */
const INTERNAL_ERROR_BACKOFF_MS = 1000;

/**
 * Decide whether to auto-retry a failed Insights request. Pure function —
 * no React state, no side effects, no DOM. Trivially unit-testable + can
 * be called from any frontend surface.
 *
 * @param response       The failed response (status + errorCode).
 * @param request        The original request inputs (forceMode + intentSignal).
 * @param attemptNumber  1-based attempt counter. 1 = original; 2 = first
 *                       (and only) auto-retry. Values >= 2 ALWAYS return
 *                       `no-retry` — the load-bearing hard-cap invariant.
 * @returns The retry decision.
 */
export function decideRetry(
  response: RetryDecisionResponseInput,
  request: RetryDecisionRequestInput,
  attemptNumber: number,
): RetryDecision {
  // Hard cap — no further retries past attempt 2. This is the load-bearing
  // no-infinite-loop invariant. Must be the FIRST check.
  if (attemptNumber >= MAX_AUTO_RETRY_ATTEMPTS) {
    return { kind: 'no-retry' };
  }

  // 503 `ai.intent-classification.disabled` + no forceMode + intent signal known.
  // The user invoked a named tool / slash command (intent known client-side);
  // we can bypass the disabled BFF classifier by retrying with explicit forceMode.
  if (
    response.status === 503
    && response.errorCode === 'ai.intent-classification.disabled'
    && !request.forceMode
    && (request.intentSignal === 'playbook' || request.intentSignal === 'rag')
  ) {
    return { kind: 'retry-with-force-mode', forceMode: request.intentSignal };
  }

  // 500 `INSIGHTS_ASSISTANT_INTERNAL_ERROR` — single retry with 1s backoff.
  if (
    response.status === 500
    && response.errorCode === 'INSIGHTS_ASSISTANT_INTERNAL_ERROR'
  ) {
    return { kind: 'retry-after-backoff', backoffMs: INTERNAL_ERROR_BACKOFF_MS };
  }

  // All other codes are non-retryable via the auto-retry path. 401 reauth
  // is handled by the renderer's reauth orchestration (NOT by this state
  // machine) — the post-reauth retry counts toward the same hard cap.
  // 429 is handled via manual user click (ADR-016) — NOT auto-retry.
  return { kind: 'no-retry' };
}

/**
 * Convenience: exposed for the renderer's "should I attempt reauth?" check.
 * Returns true for 401 responses regardless of `errorCode` (the contract
 * does not specify an `errorCode` on 401 — it's a pure auth challenge).
 */
export function isReauthCandidate(response: RetryDecisionResponseInput): boolean {
  return response.status === 401;
}
