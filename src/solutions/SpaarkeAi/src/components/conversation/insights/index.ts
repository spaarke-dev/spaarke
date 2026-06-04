/**
 * Barrel export for the Insights response-renderer sub-tree (R5 task 026 /
 * D2-16). Consumers import from this path; sub-renderer files are internal
 * implementation but exported here for testability + task-027 / task-028
 * downstream consumption.
 *
 * @see InsightsResponseRenderer.tsx — top-level renderer
 * @see types.ts — discriminated-union response + guards
 */

// Top-level renderer (default export from the file; named here for clarity).
export {
  InsightsResponseRenderer,
  default as InsightsResponseRendererDefault,
} from './InsightsResponseRenderer';
export type { InsightsResponseRendererProps } from './InsightsResponseRenderer';

// Sub-renderers — exported for tests + future per-case consumers.
export { PlaybookResponseRenderer, envelopeToFields, stringifyEnvelopeField } from './PlaybookResponseRenderer';
export type { PlaybookResponseRendererProps } from './PlaybookResponseRenderer';

export {
  RagResponseRenderer,
  defaultCitationClickStub,
  isClickableCitation,
} from './RagResponseRenderer';
export type { RagResponseRendererProps } from './RagResponseRenderer';

export { DeclineResponseRenderer } from './DeclineResponseRenderer';
export type { DeclineResponseRendererProps } from './DeclineResponseRenderer';

export { EmptyResultHint, EMPTY_RESULT_HINT_TEXT } from './EmptyResultHint';

// Low-confidence badge (R5 task 028 / D2-18). Mounted at the top of every
// response case wrapper inside `InsightsResponseRenderer`; also exported
// here for direct test consumption + future per-case reuse.
export {
  LowConfidenceBadge,
  LOW_CONFIDENCE_BADGE_TEXT,
  shouldShowLowConfidenceBadge,
} from './LowConfidenceBadge';
export type { LowConfidenceBadgeProps } from './LowConfidenceBadge';

// Error renderer + supporting modules (R5 task 029 / D2-19). Mounted by
// `InsightsResponseRenderer` when the response is an `InsightsErrorResponse`
// envelope (the `isError` discriminated-union branch). Re-exported here for
// direct test consumption + use by upstream orchestration code (chat-agent
// host's retry coordinator).
export { InsightsErrorRenderer } from './InsightsErrorRenderer';
export type { InsightsErrorRendererProps } from './InsightsErrorRenderer';
export {
  INSIGHTS_ERROR_USER_MESSAGES,
  RETRYABLE_VIA_MANUAL_CLICK,
  formatRateLimitMessage,
  getUserMessageForErrorCode,
} from './insightsErrorMessages';
export {
  decideRetry,
  isReauthCandidate,
} from './insightsRetryPolicy';
export type {
  RetryDecision,
  RetryDecisionRequestInput,
  RetryDecisionResponseInput,
} from './insightsRetryPolicy';
export { parseRetryAfter } from './retryAfterParser';

// Types + guards + helpers.
export type {
  Citation,
  Diagnostics,
  DeclineEnvelope,
  PlaybookInferenceEnvelope,
  RagObservationEnvelope,
  InsightsResponse,
  InsightsErrorResponse,
  PlaybookInferenceResponse,
  PlaybookDeclineResponse,
  RagObservationResponse,
  AnswerToken,
} from './types';

export {
  isEmptyResult,
  isDecline,
  isError,
  isPlaybookInference,
  isRagObservation,
  assertNever,
  tokenizeCitations,
} from './types';
