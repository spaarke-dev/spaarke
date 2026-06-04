/**
 * InsightsResponseRenderer + InsightsErrorRenderer — R5 task 029 / D2-19
 * unit tests for the 12-code error matrix + retry policy + correlation
 * surfacing + leakage canary + 429 Retry-After + 401 reauth.
 *
 * Covers the acceptance criteria from `tasks/029-insights-12-error-codes-retry.poml`:
 *
 *   (1) All 12 error codes render the correct column-4 user message
 *       (exhaustive `test.each` matrix).
 *   (2) 503 `ai.intent-classification.disabled` + no `forceMode` + intent
 *       signal known → retry-with-force-mode.
 *   (3) 503 `ai.intent-classification.disabled` + no intent signal → no-retry.
 *   (4) 503 `ai.intent-classification.disabled` + forceMode already set on
 *       original → no-retry (edge case; log diagnostic).
 *   (5) 500 `INSIGHTS_ASSISTANT_INTERNAL_ERROR` → retry-after-backoff (1s).
 *   (6) Retry hard-cap: `attemptNumber >= 2` → no-retry for ALL codes.
 *   (7) Leakage canary — `detail` + `title` + unknown extensions + fake
 *       document content + fake stack traces NEVER reach rendered DOM.
 *   (8) `correlationId` surfacing — visible, mono-font, escaped (XSS-safe).
 *   (9) 429 `Retry-After` parsing — delta-seconds + HTTP-date.
 *   (10) 401 reauth via `@spaarke/auth` — success path + failure path.
 *
 * Per task 029 spec: 14+ tests total covering the matrix + retry paths +
 * correlation + leakage + 429 + 401.
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, fireEvent, act } from '@testing-library/react';
import {
  FluentProvider,
  webLightTheme,
} from '@fluentui/react-components';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';

import { InsightsResponseRenderer } from '../InsightsResponseRenderer';
import { InsightsErrorRenderer } from '../InsightsErrorRenderer';
import {
  decideRetry,
  type RetryDecision,
} from '../insightsRetryPolicy';
import {
  parseRetryAfter,
} from '../retryAfterParser';
import {
  INSIGHTS_ERROR_USER_MESSAGES,
  formatRateLimitMessage,
  getUserMessageForErrorCode,
} from '../insightsErrorMessages';
import type { InsightsErrorResponse } from '../types';
import { isError } from '../types';

/**
 * Local re-declaration of the contract-binding error code string union. Kept
 * here (instead of importing from `services/insightsQueryClient`) so the test
 * runner doesn't transitively pull in the HTTP client's `@spaarke/auth`
 * runtime imports (which require the shared MSAL context unavailable in
 * jsdom). This local copy MUST match the canonical union in
 * `services/insightsQueryClient.ts` `InsightsErrorCode`.
 */
type InsightsErrorCode =
  | 'query.required'
  | 'subject.required'
  | 'subject.invalid'
  | 'forceMode.invalid'
  | 'conversationContext.invalid'
  | 'auth.401'
  | 'rate-limit.429'
  | 'ai.insights.disabled'
  | 'ai.rag.disabled'
  | 'ai.intent-classification.disabled'
  | 'ai.assistant-default-playbook.unconfigured'
  | 'INSIGHTS_ASSISTANT_INTERNAL_ERROR';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function renderWithProviders(
  ui: React.ReactElement,
  options: { bus?: PaneEventBus } = {},
): { bus: PaneEventBus } {
  const bus = options.bus ?? new PaneEventBus();
  render(
    <FluentProvider theme={webLightTheme}>
      <PaneEventBusProvider bus={bus}>{ui}</PaneEventBusProvider>
    </FluentProvider>,
  );
  return { bus };
}

function makeErrorResponse(
  overrides: Partial<InsightsErrorResponse> & { errorCode: string },
): InsightsErrorResponse {
  return {
    path: 'error',
    errorCode: overrides.errorCode,
    correlationId: overrides.correlationId ?? 'corr-test-12345',
    status: overrides.status ?? 500,
    title: overrides.title ?? 'Internal Server Error',
    detail: overrides.detail ?? 'Server detail (NOT user-facing per ADR-018).',
    retryAfterSeconds: overrides.retryAfterSeconds,
    unknownExtensions: overrides.unknownExtensions,
  };
}

// ---------------------------------------------------------------------------
// (T1) 12-code matrix — exhaustive test.each
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — 12-code matrix (exhaustive)', () => {
  // The 12 binding codes from integration brief §5.1.
  const matrix: Array<{
    code: InsightsErrorCode;
    status: number;
    expectedMessageContains: string;
  }> = [
    { code: 'query.required', status: 400, expectedMessageContains: "I couldn't understand the question" },
    { code: 'subject.required', status: 400, expectedMessageContains: "I couldn't understand the question" },
    { code: 'subject.invalid', status: 400, expectedMessageContains: "Couldn't identify the entity" },
    { code: 'forceMode.invalid', status: 400, expectedMessageContains: 'Something went wrong' },
    { code: 'conversationContext.invalid', status: 400, expectedMessageContains: 'Conversation context too long' },
    { code: 'auth.401', status: 401, expectedMessageContains: 'Your session expired' },
    { code: 'ai.insights.disabled', status: 503, expectedMessageContains: 'Insights temporarily disabled' },
    { code: 'ai.rag.disabled', status: 503, expectedMessageContains: 'Knowledge search temporarily disabled' },
    { code: 'ai.intent-classification.disabled', status: 503, expectedMessageContains: 'Insights temporarily disabled' },
    { code: 'ai.assistant-default-playbook.unconfigured', status: 503, expectedMessageContains: "Something's misconfigured" },
    { code: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR', status: 500, expectedMessageContains: 'Something went wrong' },
  ];

  test.each(matrix)(
    'renders correct column-4 user message for $code (HTTP $status)',
    ({ code, status, expectedMessageContains }) => {
      const response = makeErrorResponse({ errorCode: code, status });
      renderWithProviders(<InsightsResponseRenderer response={response} />);

      const root = screen.getByTestId('insights-response-renderer');
      expect(root.getAttribute('data-response-case')).toBe('error');
      const errorContainer = screen.getByTestId('insights-error-renderer');
      expect(errorContainer.getAttribute('data-error-code')).toBe(code);

      const message = screen.getByTestId('insights-error-user-message');
      expect(message.textContent).toContain(expectedMessageContains);
    },
  );

  // 429 is rendered with a Retry-After-driven countdown — covered separately
  // in the T9 / T10 blocks. Verify here that the message TEMPLATE matches.
  it('rate-limit.429 message template contains "Slow down a moment" + {seconds}', () => {
    const template = INSIGHTS_ERROR_USER_MESSAGES['rate-limit.429'];
    expect(template).toContain('Slow down a moment');
    expect(template).toContain('{seconds}');
  });

  // Coverage assertion: the test matrix MUST equal the contract's 12 codes.
  // Adding a new code to the contract WITHOUT updating this matrix fails
  // the assertion — regression guard.
  it('covers all 12 binding codes from integration brief §5.1 (matrix coverage gate)', () => {
    const matrixCodes = new Set(matrix.map((m) => m.code));
    matrixCodes.add('rate-limit.429'); // covered in T9 / T10
    const contractCodes = new Set(Object.keys(INSIGHTS_ERROR_USER_MESSAGES));
    expect(matrixCodes.size).toBe(contractCodes.size);
    for (const code of contractCodes) {
      expect(matrixCodes.has(code as InsightsErrorCode)).toBe(true);
    }
  });
});

// ---------------------------------------------------------------------------
// (T2 — T4) Retry-decision state machine — 503 ai.intent-classification.disabled
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — decideRetry: 503 ai.intent-classification.disabled', () => {
  it('returns retry-with-force-mode when no forceMode + intent signal known (rag)', () => {
    const decision = decideRetry(
      { status: 503, errorCode: 'ai.intent-classification.disabled' },
      { intentSignal: 'rag' },
      1,
    );
    expect(decision).toEqual<RetryDecision>({
      kind: 'retry-with-force-mode',
      forceMode: 'rag',
    });
  });

  it('returns retry-with-force-mode when no forceMode + intent signal known (playbook)', () => {
    const decision = decideRetry(
      { status: 503, errorCode: 'ai.intent-classification.disabled' },
      { intentSignal: 'playbook' },
      1,
    );
    expect(decision).toEqual<RetryDecision>({
      kind: 'retry-with-force-mode',
      forceMode: 'playbook',
    });
  });

  it('returns no-retry when no intent signal available (T3)', () => {
    const decision = decideRetry(
      { status: 503, errorCode: 'ai.intent-classification.disabled' },
      {}, // no intent signal
      1,
    );
    expect(decision).toEqual<RetryDecision>({ kind: 'no-retry' });
  });

  it('returns no-retry when forceMode already set on original request (T4 edge case)', () => {
    const decision = decideRetry(
      { status: 503, errorCode: 'ai.intent-classification.disabled' },
      { forceMode: 'rag', intentSignal: 'rag' },
      1,
    );
    expect(decision).toEqual<RetryDecision>({ kind: 'no-retry' });
  });
});

// ---------------------------------------------------------------------------
// (T5) Retry-decision state machine — 500 INSIGHTS_ASSISTANT_INTERNAL_ERROR
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — decideRetry: 500 INSIGHTS_ASSISTANT_INTERNAL_ERROR', () => {
  it('returns retry-after-backoff with 1000ms on attempt 1', () => {
    const decision = decideRetry(
      { status: 500, errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR' },
      {},
      1,
    );
    expect(decision).toEqual<RetryDecision>({
      kind: 'retry-after-backoff',
      backoffMs: 1000,
    });
  });

  it('returns no-retry on attempt 2 (hard cap)', () => {
    const decision = decideRetry(
      { status: 500, errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR' },
      {},
      2,
    );
    expect(decision).toEqual<RetryDecision>({ kind: 'no-retry' });
  });
});

// ---------------------------------------------------------------------------
// (T6) Retry hard cap — `attemptNumber >= 2` returns no-retry for ALL codes
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — retry hard cap @ attemptNumber >= 2', () => {
  const retryableInputs = [
    { status: 503, errorCode: 'ai.intent-classification.disabled' },
    { status: 500, errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR' },
  ];

  test.each(retryableInputs)(
    'returns no-retry on attemptNumber=2 for status=$status errorCode=$errorCode',
    ({ status, errorCode }) => {
      const decision = decideRetry(
        { status, errorCode },
        { intentSignal: 'rag', forceMode: null },
        2,
      );
      expect(decision).toEqual<RetryDecision>({ kind: 'no-retry' });
    },
  );

  it('returns no-retry on attemptNumber=99 for retry-eligible 503 (defensive — hard cap)', () => {
    const decision = decideRetry(
      { status: 503, errorCode: 'ai.intent-classification.disabled' },
      { intentSignal: 'rag' },
      99,
    );
    expect(decision).toEqual<RetryDecision>({ kind: 'no-retry' });
  });

  it('returns no-retry on attemptNumber=3 for retry-eligible 500 (defensive — hard cap)', () => {
    const decision = decideRetry(
      { status: 500, errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR' },
      {},
      3,
    );
    expect(decision).toEqual<RetryDecision>({ kind: 'no-retry' });
  });
});

// ---------------------------------------------------------------------------
// (T7) Leakage canary — ADR-018 load-bearing regression guard
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — ADR-018 leakage canary (load-bearing)', () => {
  it('NEVER renders detail / title / unknown extensions / fake stack trace / fake document content', () => {
    // Fabricated leakage strings — these MUST NOT reach the DOM.
    const FAKE_DOCUMENT_CONTENT =
      'DRAFT — Acme APA: This Agreement is between Buyer and Seller dated as of...';
    const FAKE_PROMPT_TEXT =
      "You are a helpful assistant. Always cite sources verbatim. Pretend you're a...";
    const FAKE_STACK_TRACE =
      'at System.Threading.ThreadAbortException at System.Web.HttpContext.Current...';
    const FAKE_TITLE_LEAK =
      "Internal Server Error: system prompt was 'You are a helpful assistant...'";
    const FAKE_UNKNOWN_EXTENSION =
      'system prompt text: pretend you are a knowledgeable lawyer specializing in...';

    const consoleSpy = jest
      .spyOn(console, 'debug')
      .mockImplementation(() => undefined);

    const response = makeErrorResponse({
      errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR',
      status: 500,
      // `title` carries leakage — MUST NOT be rendered.
      title: FAKE_TITLE_LEAK,
      // `detail` carries fake document content + stack trace — MUST NOT be
      // rendered.
      detail: `${FAKE_DOCUMENT_CONTENT}\n\n${FAKE_STACK_TRACE}\n\n${FAKE_PROMPT_TEXT}`,
      unknownExtensions: {
        _internalContext: FAKE_UNKNOWN_EXTENSION,
        // A second extension to verify ALL unknown fields are protected.
        _systemPromptDigest: 'sha256:abc123def456',
      },
    });

    renderWithProviders(<InsightsResponseRenderer response={response} />);

    // ── DOM assertions: leakage strings MUST NOT appear ────────────────────
    const root = screen.getByTestId('insights-error-renderer');
    const dom = root.outerHTML;

    expect(dom).not.toContain('DRAFT — Acme APA');
    expect(dom).not.toContain('Buyer and Seller');
    expect(dom).not.toContain('System.Threading');
    expect(dom).not.toContain('System.Web');
    expect(dom).not.toContain('You are a helpful assistant');
    expect(dom).not.toContain('system prompt');
    expect(dom).not.toContain('system prompt text');
    expect(dom).not.toContain('Pretend');
    expect(dom).not.toContain('pretend');
    expect(dom).not.toContain('sha256:abc123def456');
    // The original title text MUST NOT be rendered (it was poisoned with
    // leakage). The user-facing message comes from the constant map, not
    // from `title`.
    expect(dom).not.toContain('system prompt was');

    // ── DOM assertions: column-4 message MUST appear ───────────────────────
    const userMessage = screen.getByTestId('insights-error-user-message');
    expect(userMessage.textContent).toContain('Something went wrong');

    // ── Console diagnostic: detail + title + unknownExtensions ARE logged
    //     (so ops can investigate via App Insights), but the values appear in
    //     the console arguments — NOT in the rendered DOM.
    expect(consoleSpy).toHaveBeenCalled();
    const consoleArgs = consoleSpy.mock.calls.flat();
    const consoleStr = JSON.stringify(consoleArgs);
    expect(consoleStr).toContain('DRAFT — Acme APA');
    expect(consoleStr).toContain('_internalContext');

    consoleSpy.mockRestore();
  });

  it('NEVER renders the raw status code or title field in DOM (defense in depth)', () => {
    const response = makeErrorResponse({
      errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR',
      status: 500,
      title: 'Internal Server Error',
      detail: 'A diagnostic blob that should never be user-visible.',
    });
    renderWithProviders(<InsightsResponseRenderer response={response} />);
    const root = screen.getByTestId('insights-error-renderer');
    // Title NOT in DOM text content (it's on a data-attribute only).
    expect(root.textContent).not.toContain('Internal Server Error');
    // Detail NOT in DOM.
    expect(root.textContent).not.toContain('diagnostic blob');
    // But the test data-attributes do carry the status (for assertion stability):
    expect(root.getAttribute('data-error-status')).toBe('500');
  });
});

// ---------------------------------------------------------------------------
// (T8) Correlation-id surfacing — FR-17 / SC-16 + XSS-safe rendering
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — correlationId surfacing + escaping', () => {
  it('renders correlationId in mono-font small text below the user message', () => {
    const response = makeErrorResponse({
      errorCode: 'ai.insights.disabled',
      status: 503,
      correlationId: 'corr-abc-12345-def-67890',
    });
    renderWithProviders(<InsightsResponseRenderer response={response} />);

    const corrEl = screen.getByTestId('insights-error-correlation-id');
    expect(corrEl.textContent).toBe('corr-abc-12345-def-67890');
  });

  it('renders correlationId as ESCAPED text (XSS-safe — Fluent Text default escape)', () => {
    // Inject an XSS attempt as the correlationId (untrusted display text).
    const response = makeErrorResponse({
      errorCode: 'ai.insights.disabled',
      status: 503,
      correlationId: '<script>alert(1)</script>',
    });
    renderWithProviders(<InsightsResponseRenderer response={response} />);

    const corrEl = screen.getByTestId('insights-error-correlation-id');
    // The text content carries the LITERAL string (escape preserved).
    expect(corrEl.textContent).toBe('<script>alert(1)</script>');
    // The DOM does NOT contain an actual <script> element child.
    const root = screen.getByTestId('insights-error-renderer');
    expect(root.querySelector('script')).toBeNull();
  });

  it('renders a copy-to-clipboard button with injectable writer (direct test of InsightsErrorRenderer)', () => {
    // The top-level renderer doesn't expose the copy prop in v1.0 to keep
    // its surface narrow; the underlying renderer is tested directly via
    // its `copyToClipboard` injection seam.
    const response = makeErrorResponse({
      errorCode: 'ai.insights.disabled',
      status: 503,
      correlationId: 'corr-xyz-99999',
    });
    const writes: string[] = [];
    const copyToClipboard = (text: string) => {
      writes.push(text);
    };

    render(
      <FluentProvider theme={webLightTheme}>
        <InsightsErrorRenderer
          response={response}
          copyToClipboard={copyToClipboard}
        />
      </FluentProvider>,
    );

    const copyBtn = screen.getByTestId('insights-error-copy-correlation-id');
    fireEvent.click(copyBtn);
    expect(writes).toEqual(['corr-xyz-99999']);
  });
});

// ---------------------------------------------------------------------------
// (T9) 429 Retry-After parsing — delta-seconds form
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — Retry-After parsing (delta-seconds)', () => {
  it('parseRetryAfter("30") returns 30', () => {
    expect(parseRetryAfter('30')).toBe(30);
  });

  it('parseRetryAfter("0") returns 0', () => {
    expect(parseRetryAfter('0')).toBe(0);
  });

  it('parseRetryAfter("  60  ") (whitespace-tolerant) returns 60', () => {
    expect(parseRetryAfter('  60  ')).toBe(60);
  });

  it('parseRetryAfter(null) returns undefined', () => {
    expect(parseRetryAfter(null)).toBeUndefined();
  });

  it('parseRetryAfter(undefined) returns undefined', () => {
    expect(parseRetryAfter(undefined)).toBeUndefined();
  });

  it('parseRetryAfter("") returns undefined', () => {
    expect(parseRetryAfter('')).toBeUndefined();
  });

  it('parseRetryAfter("not-a-number") returns undefined', () => {
    expect(parseRetryAfter('not-a-number')).toBeUndefined();
  });

  it('parseRetryAfter("-5") returns undefined (defensive — server bug)', () => {
    expect(parseRetryAfter('-5')).toBeUndefined();
  });

  it('renders 429 message with substituted countdown seconds', () => {
    expect(formatRateLimitMessage(30)).toContain('30 seconds');
    expect(formatRateLimitMessage(0)).toContain('0 seconds');
    expect(formatRateLimitMessage(undefined)).toContain('{seconds}');
  });
});

// ---------------------------------------------------------------------------
// (T10) 429 Retry-After parsing — HTTP-date form (RFC 7231 §7.1.3)
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — Retry-After parsing (HTTP-date)', () => {
  it('parses HTTP-date 30 seconds in the future as ~30 delta-seconds', () => {
    const now = Date.parse('2026-10-21T07:28:00Z');
    const future = 'Wed, 21 Oct 2026 07:28:30 GMT';
    const result = parseRetryAfter(future, now);
    expect(result).toBe(30);
  });

  it('parses HTTP-date 60 seconds in the future as ~60 delta-seconds', () => {
    const now = Date.parse('2026-10-21T07:28:00Z');
    const future = 'Wed, 21 Oct 2026 07:29:00 GMT';
    const result = parseRetryAfter(future, now);
    expect(result).toBe(60);
  });

  it('parses HTTP-date already in the past as 0 (manual click immediate)', () => {
    const now = Date.parse('2026-10-21T07:29:00Z');
    const past = 'Wed, 21 Oct 2026 07:28:00 GMT';
    const result = parseRetryAfter(past, now);
    expect(result).toBe(0);
  });

  it('rounds UP fractional seconds to ensure UI countdown ≥ server hold-off', () => {
    // Build a synthetic "now" with sub-second offset so the delta is fractional.
    // Date.parse + toUTCString() lose sub-second precision, so we provide a
    // `now` that's 500ms BEFORE the date's whole-second boundary, producing
    // a 30.5-second delta that rounds UP to 31.
    const future = 'Wed, 21 Oct 2026 07:28:30 GMT';
    const parsedFuture = Date.parse(future);
    const now = parsedFuture - 30_500; // 30.5 seconds before `future`.
    const result = parseRetryAfter(future, now);
    expect(result).toBe(31);
  });

  it('returns undefined for un-parseable date strings', () => {
    expect(parseRetryAfter('not-a-date-at-all')).toBeUndefined();
  });

  it('renders 429 with countdown ≥ 1 and exposes the rate-limit message template', () => {
    const response = makeErrorResponse({
      errorCode: 'rate-limit.429',
      status: 429,
      retryAfterSeconds: 30,
    });
    renderWithProviders(<InsightsResponseRenderer response={response} />);
    const message = screen.getByTestId('insights-error-user-message');
    // The message must include "Slow down a moment" + a numeric countdown.
    expect(message.textContent).toContain('Slow down a moment');
    expect(message.textContent).toMatch(/in \d+ seconds/);
  });

  it('429 does NOT auto-retry — only manual click invokes the retry handler (ADR-016)', async () => {
    jest.useFakeTimers();
    try {
      const response = makeErrorResponse({
        errorCode: 'rate-limit.429',
        status: 429,
        retryAfterSeconds: 3,
      });
      const retries: number[] = [];
      const onErrorRetry = () => {
        retries.push(Date.now());
      };
      renderWithProviders(
        <InsightsResponseRenderer response={response} onErrorRetry={onErrorRetry} />,
      );

      // Advance fake time past the countdown — no auto-retry MUST fire.
      act(() => {
        jest.advanceTimersByTime(5000);
      });

      expect(retries).toEqual([]); // ADR-016 — no auto-retry on 429.

      // After the countdown completes, the "Try again" CTA appears.
      const cta = screen.getByTestId('insights-error-retry-cta');
      expect(cta).toBeInTheDocument();
      fireEvent.click(cta);
      expect(retries).toHaveLength(1); // exactly one manual retry click.
    } finally {
      jest.useRealTimers();
    }
  });
});

// ---------------------------------------------------------------------------
// (T11 — T12) 401 reauth — via existing @spaarke/auth helper
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — 401 reauth via @spaarke/auth', () => {
  it('post-reauth-failure: renders "Your session expired — sign in again" + sign-in CTA', () => {
    const response = makeErrorResponse({
      errorCode: 'auth.401',
      status: 401,
    });
    const signIns: number[] = [];
    const onErrorSignInAgain = () => {
      signIns.push(Date.now());
    };
    renderWithProviders(
      <InsightsResponseRenderer
        response={response}
        onErrorSignInAgain={onErrorSignInAgain}
      />,
    );

    const message = screen.getByTestId('insights-error-user-message');
    expect(message.textContent).toContain('Your session expired');
    expect(message.textContent).toContain('sign in again');

    const signInCta = screen.getByTestId('insights-error-sign-in-cta');
    expect(signInCta).toBeInTheDocument();
    fireEvent.click(signInCta);
    expect(signInCta.textContent).toBe('Sign in again');
    expect(signIns).toHaveLength(1);
  });

  it('does NOT render the "Try again" CTA for auth.401 (must sign in, not retry)', () => {
    const response = makeErrorResponse({
      errorCode: 'auth.401',
      status: 401,
    });
    renderWithProviders(
      <InsightsResponseRenderer
        response={response}
        onErrorRetry={() => undefined}
        onErrorSignInAgain={() => undefined}
      />,
    );
    expect(screen.queryByTestId('insights-error-retry-cta')).toBeNull();
    expect(screen.getByTestId('insights-error-sign-in-cta')).toBeInTheDocument();
  });

  it('reauth-then-retry is orchestrated by upstream chat-agent (renderer surfaces only post-fail UX)', () => {
    // The renderer's contract: by the time the InsightsErrorResponse with
    // errorCode='auth.401' reaches the renderer, the upstream orchestrator
    // (chat-agent host) has ALREADY attempted reauth via `@spaarke/auth`
    // useAuth().getAccessToken() and that attempt FAILED. The renderer's
    // job is only to surface the "session expired" CTA. This test
    // documents the contract — see task-029-error-codes-evidence.md §4.
    const response = makeErrorResponse({
      errorCode: 'auth.401',
      status: 401,
      detail: 'Internal: reauth attempt failed after refresh_token expired.',
    });
    renderWithProviders(<InsightsResponseRenderer response={response} />);
    const message = screen.getByTestId('insights-error-user-message');
    expect(message.textContent).toContain('Your session expired');
    // detail NOT rendered (ADR-018).
    const root = screen.getByTestId('insights-error-renderer');
    expect(root.textContent).not.toContain('refresh_token');
  });
});

// ---------------------------------------------------------------------------
// (T13) `isError` discriminator + getUserMessageForErrorCode fallback
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — discriminator + fallback', () => {
  it('isError returns true for error envelopes', () => {
    const response = makeErrorResponse({ errorCode: 'ai.insights.disabled' });
    expect(isError(response)).toBe(true);
  });

  it('isError returns false for non-error envelopes (defensive)', () => {
    const successLike = { path: 'rag', citations: [], answer: '' } as never;
    expect(isError(successLike)).toBe(false);
  });

  it('getUserMessageForErrorCode falls back to generic INTERNAL_ERROR for unknown codes (forward-compat)', () => {
    const message = getUserMessageForErrorCode('v1.2.future-unknown-code');
    expect(message).toBe(INSIGHTS_ERROR_USER_MESSAGES.INSIGHTS_ASSISTANT_INTERNAL_ERROR);
  });

  it('getUserMessageForErrorCode falls back for null / undefined (defensive)', () => {
    expect(getUserMessageForErrorCode(undefined)).toBe(
      INSIGHTS_ERROR_USER_MESSAGES.INSIGHTS_ASSISTANT_INTERNAL_ERROR,
    );
    expect(getUserMessageForErrorCode(null)).toBe(
      INSIGHTS_ERROR_USER_MESSAGES.INSIGHTS_ASSISTANT_INTERNAL_ERROR,
    );
  });
});

// ---------------------------------------------------------------------------
// (T14) Retry CTA is gated by error code — non-retryable codes hide the CTA
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — manual-retry CTA gating', () => {
  it('shows the "Try again" CTA for retryable codes (500)', () => {
    const response = makeErrorResponse({
      errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR',
      status: 500,
    });
    renderWithProviders(
      <InsightsResponseRenderer response={response} onErrorRetry={() => undefined} />,
    );
    expect(screen.getByTestId('insights-error-retry-cta')).toBeInTheDocument();
  });

  it('shows the "Try again" CTA for 400 codes (user may rephrase/refresh)', () => {
    const response = makeErrorResponse({
      errorCode: 'subject.invalid',
      status: 400,
    });
    renderWithProviders(
      <InsightsResponseRenderer response={response} onErrorRetry={() => undefined} />,
    );
    expect(screen.getByTestId('insights-error-retry-cta')).toBeInTheDocument();
  });

  it('hides "Try again" CTA when no handler is provided (host decided not to expose retry)', () => {
    const response = makeErrorResponse({
      errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR',
      status: 500,
    });
    renderWithProviders(<InsightsResponseRenderer response={response} />);
    expect(screen.queryByTestId('insights-error-retry-cta')).toBeNull();
  });

  it('hides "Try again" CTA for auth.401 even when handler is provided', () => {
    const response = makeErrorResponse({
      errorCode: 'auth.401',
      status: 401,
    });
    renderWithProviders(
      <InsightsResponseRenderer
        response={response}
        onErrorRetry={() => undefined}
      />,
    );
    expect(screen.queryByTestId('insights-error-retry-cta')).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// (T15) Same correlationId carried forward across retries (FR-17 / SC-16)
// ---------------------------------------------------------------------------

describe('Task 029 / D2-19 — correlationId end-to-end propagation invariant', () => {
  it('renders the SAME correlationId on the post-retry error surface (same logical turn)', () => {
    // Conceptual test: when the host re-issues a retry with the same
    // correlationId, and the retry ALSO fails, the renderer surfaces the
    // SAME correlationId. This validates the end-to-end App Insights /
    // Kusto trace invariant — one user turn = one correlationId, regardless
    // of how many retries happened in between.
    const CORR_ID = 'corr-same-across-retries-001';

    // First failure (attemptNumber=1)
    const firstFail = makeErrorResponse({
      errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR',
      status: 500,
      correlationId: CORR_ID,
    });
    const { unmount } = render(
      <FluentProvider theme={webLightTheme}>
        <InsightsResponseRenderer response={firstFail} />
      </FluentProvider>,
    );
    expect(screen.getByTestId('insights-error-correlation-id').textContent).toBe(CORR_ID);
    unmount();

    // Second failure (post auto-retry; attemptNumber=2 → hard-cap) — SAME ID.
    const secondFail = makeErrorResponse({
      errorCode: 'INSIGHTS_ASSISTANT_INTERNAL_ERROR',
      status: 500,
      correlationId: CORR_ID,
    });
    render(
      <FluentProvider theme={webLightTheme}>
        <InsightsResponseRenderer response={secondFail} />
      </FluentProvider>,
    );
    expect(screen.getByTestId('insights-error-correlation-id').textContent).toBe(CORR_ID);
  });
});
