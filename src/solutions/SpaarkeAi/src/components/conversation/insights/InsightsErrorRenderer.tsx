/**
 * InsightsErrorRenderer — Error-state branch of the Insights Assistant
 * chat-tool response renderer (R5 task 029 / D2-19).
 *
 * Surfaces ALL 12 binding error codes from integration brief §5.1 with the
 * column-4 user-facing message, plus the 429 `Retry-After` countdown UX
 * (ADR-016 manual-click retry only), the post-reauth-failure 401 sign-in
 * CTA (ADR-028 reauth via `@spaarke/auth`), and the correlation-id
 * surfacing (FR-17 / SC-16).
 *
 * ADR-018 LOAD-BEARING DISCIPLINE: this renderer NEVER displays `detail`,
 * `title`, or unknown ProblemDetails extensions. Those fields are
 * `console.debug`-logged for ops diagnostics but never reach the DOM. The
 * "leakage canary" unit test asserts this regression-guard discipline.
 *
 * The retry orchestration (auto-retry for 503 `ai.intent-classification.disabled`
 * + 500 `INSIGHTS_ASSISTANT_INTERNAL_ERROR`) happens UPSTREAM in the
 * chat-agent host (consumer of `decideRetry()` from `insightsRetryPolicy.ts`).
 * By the time this component renders, the auto-retries are already exhausted
 * and the error is final. Manual-click retry (CTA) is exposed via the
 * `onManualRetry` prop — the host wires this to its own re-issue path.
 *
 * Per R5 CLAUDE.md §3.1 reuse mandate: this renderer is mounted by the
 * existing `InsightsResponseRenderer` (task 026) — NOT a parallel error
 * component. Mounted via the same `isError` discrimination as the four
 * existing success cases.
 *
 * Per R5 CLAUDE.md §3.2 / ADR-018: no new feature flags. Per ADR-013 §3.5
 * Zone B boundary: no imports from `src/server/api/...`.
 *
 * @see insightsErrorMessages.ts — 12-code → user-message map
 * @see insightsRetryPolicy.ts — retry-decision state machine
 * @see retryAfterParser.ts — RFC 7231 Retry-After parsing
 * @see InsightsResponseRenderer.tsx — top-level renderer that mounts this branch
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  MessageBar,
  MessageBarBody,
  Text,
  Button,
} from '@fluentui/react-components';
import { Copy24Regular } from '@fluentui/react-icons';
import type { InsightsErrorCode } from '../../../services/insightsQueryClient';
import type { InsightsErrorResponse } from './types';
import {
  formatRateLimitMessage,
  getUserMessageForErrorCode,
  RETRYABLE_VIA_MANUAL_CLICK,
} from './insightsErrorMessages';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface InsightsErrorRendererProps {
  /** Error envelope — surfaced verbatim from `InsightsQueryError` (task 025). */
  readonly response: InsightsErrorResponse;
  /**
   * Optional manual-retry handler. Wired by the parent (`InsightsResponseRenderer`
   * or the conversation host) — when invoked, re-issues the original request
   * with the same correlationId carried forward.
   *
   * When omitted, the "Try again" CTA is NOT rendered (the renderer assumes
   * the host has no retry surface to expose).
   */
  readonly onManualRetry?: () => void;
  /**
   * Optional sign-in handler — surfaced as a CTA when the error code is
   * `auth.401` (post-reauth-failure path). When omitted, the CTA is NOT
   * rendered.
   */
  readonly onSignInAgain?: () => void;
  /**
   * Optional clipboard helper for the correlationId copy button. Defaults to
   * `navigator.clipboard.writeText`. Injectable for unit-test determinism
   * (jsdom does NOT ship `navigator.clipboard`).
   */
  readonly copyToClipboard?: (text: string) => Promise<void> | void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    width: '100%',
  },
  messageRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: tokens.spacingHorizontalS,
  },
  correlationRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    paddingTop: tokens.spacingVerticalXXS,
  },
  correlationText: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  ctaRow: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalS,
    paddingTop: tokens.spacingVerticalXS,
  },
});

// ---------------------------------------------------------------------------
// 429 countdown UX
// ---------------------------------------------------------------------------

/**
 * Hook driving the 429 `Retry-After` countdown. Decrements `remaining`
 * every second; clamps at 0; never auto-fires retry (ADR-016 — manual
 * click only).
 *
 * Returns `null` when no countdown is active (no Retry-After header OR
 * non-429 response). When the countdown is active, returns the integer
 * seconds remaining.
 */
function useRetryAfterCountdown(retryAfterSeconds: number | undefined): number | null {
  const [remaining, setRemaining] = React.useState<number | null>(
    typeof retryAfterSeconds === 'number' && retryAfterSeconds >= 0
      ? Math.max(0, Math.floor(retryAfterSeconds))
      : null,
  );

  React.useEffect(() => {
    if (remaining === null || remaining <= 0) {
      return undefined;
    }
    const handle = setTimeout(() => {
      setRemaining((prev) => (prev === null || prev <= 0 ? 0 : prev - 1));
    }, 1000);
    return () => clearTimeout(handle);
  }, [remaining]);

  // Re-sync if the prop changes (e.g., second 429 in the same session).
  React.useEffect(() => {
    if (typeof retryAfterSeconds === 'number' && retryAfterSeconds >= 0) {
      setRemaining(Math.max(0, Math.floor(retryAfterSeconds)));
    } else {
      setRemaining(null);
    }
  }, [retryAfterSeconds]);

  return remaining;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const InsightsErrorRenderer: React.FC<InsightsErrorRendererProps> = ({
  response,
  onManualRetry,
  onSignInAgain,
  copyToClipboard,
}) => {
  const styles = useStyles();

  // ADR-018 leakage discipline: detail + title + unknown extensions are
  // Console-logged for ops diagnostics but NEVER rendered. The console.debug
  // level keeps these out of production console noise but allows ops to
  // capture via App Insights browser SDK (when configured to harvest
  // verbose levels) — see Test 7 (leakage canary).
  React.useEffect(() => {
    // eslint-disable-next-line no-console
    console.debug(
      '[InsightsErrorRenderer] error diagnostic (NOT user-facing)',
      {
        errorCode: response.errorCode,
        status: response.status,
        correlationId: response.correlationId,
        // The following fields are intentionally logged but NEVER rendered.
        title: response.title,
        detail: response.detail,
        unknownExtensions: response.unknownExtensions,
      },
    );
  }, [response]);

  const countdown = useRetryAfterCountdown(
    response.errorCode === 'rate-limit.429' ? response.retryAfterSeconds : undefined,
  );

  // Compose the user-facing message. For rate-limit, substitute the
  // {seconds} placeholder with the live countdown value (or the initial
  // header value if the countdown is null).
  const userMessage = React.useMemo(() => {
    if (response.errorCode === 'rate-limit.429') {
      const seconds = countdown ?? response.retryAfterSeconds;
      return formatRateLimitMessage(seconds);
    }
    return getUserMessageForErrorCode(response.errorCode);
  }, [response.errorCode, response.retryAfterSeconds, countdown]);

  // CTA visibility logic — gates by error code, manual-retry handler
  // presence, and countdown state.
  const showManualRetryCta =
    onManualRetry !== undefined
    && RETRYABLE_VIA_MANUAL_CLICK.has(response.errorCode as InsightsErrorCode)
    // For 429, only show the CTA once the countdown is exhausted.
    && (response.errorCode !== 'rate-limit.429' || (countdown ?? 0) <= 0);

  const showSignInCta =
    onSignInAgain !== undefined && response.errorCode === 'auth.401';

  // Copy-to-clipboard helper. Defaults to navigator.clipboard.writeText;
  // injectable for unit-test determinism.
  const handleCopyCorrelationId = React.useCallback(() => {
    const writer = copyToClipboard
      ?? ((text: string): void => {
        if (typeof navigator !== 'undefined' && navigator.clipboard) {
          // Fire-and-forget; the Promise resolution is not surfaced to UI in v1.0.
          void navigator.clipboard.writeText(text);
        }
      });
    void writer(response.correlationId);
  }, [copyToClipboard, response.correlationId]);

  return (
    <div
      className={styles.container}
      data-testid="insights-error-renderer"
      data-error-code={response.errorCode}
      data-error-status={String(response.status)}
    >
      <MessageBar intent="error" data-testid="insights-error-messagebar">
        <MessageBarBody>
          {/*
            User-facing message — VERBATIM from `insightsErrorMessages.ts`.
            Fluent v9 `Text` default-escapes its children; rendering the
            string as `{userMessage}` is safe even if the message contains
            characters that look like HTML.
          */}
          <Text
            data-testid="insights-error-user-message"
            weight="semibold"
          >
            {userMessage}
          </Text>
        </MessageBarBody>
      </MessageBar>

      {/*
        Correlation-id — opaque ops-debugging key (FR-17 / SC-16). Rendered
        in small mono-font BELOW the user message; visually de-emphasized
        but visible + copyable for support tickets. Treated as untrusted
        display text — Fluent `Text` escapes by default; do NOT
        `dangerouslySetInnerHTML`.
      */}
      {response.correlationId ? (
        <div className={styles.correlationRow}>
          <Text
            className={styles.correlationText}
            data-testid="insights-error-correlation-id"
          >
            {response.correlationId}
          </Text>
          <Button
            appearance="subtle"
            size="small"
            icon={<Copy24Regular />}
            aria-label="Copy correlation ID"
            data-testid="insights-error-copy-correlation-id"
            onClick={handleCopyCorrelationId}
          />
        </div>
      ) : null}

      {/*
        CTAs — manual retry + sign-in. Show conditionally:
          - Retry: only for retryable codes; for 429, only once the
            countdown is exhausted.
          - Sign-in: only for `auth.401` (post-reauth-failure path).
      */}
      {(showManualRetryCta || showSignInCta) ? (
        <div className={styles.ctaRow}>
          {showManualRetryCta ? (
            <Button
              appearance="primary"
              size="small"
              data-testid="insights-error-retry-cta"
              onClick={onManualRetry}
            >
              Try again
            </Button>
          ) : null}
          {showSignInCta ? (
            <Button
              appearance="primary"
              size="small"
              data-testid="insights-error-sign-in-cta"
              onClick={onSignInAgain}
            >
              Sign in again
            </Button>
          ) : null}
        </div>
      ) : null}
    </div>
  );
};

export default InsightsErrorRenderer;
