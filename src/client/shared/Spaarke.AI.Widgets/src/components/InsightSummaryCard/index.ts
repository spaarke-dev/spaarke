/**
 * @spaarke/ai-widgets — InsightSummaryCard barrel
 *
 * Re-exports the component + its public type contract. The package-root
 * `src/index.ts` mounts this barrel; consumers import from `@spaarke/ai-widgets`,
 * NOT from this path directly.
 *
 * Task 030: scaffold (component + types + styles).
 * Task 031: state machine + Popover + Dialog composition (state.ts + decline helper).
 * Task 035: dev sandbox (SC-01 Storybook-equivalent) — see README.md in this folder.
 */

export { InsightSummaryCard, default } from './InsightSummaryCard';
// Task 035 — SC-01 dev sandbox; consumer hosts may import + render to preview
// all 6 FR-06 states with a light/dark toggle. See README.md.
export { InsightSummaryCardSandbox } from './InsightSummaryCardSandbox';
export type {
  InsightSummaryCardProps,
  InsightCitationRef,
  InsightEnvelope,
  // Task 032 — FR-05 topic registry mount-check contract
  InsightRegistryEntry,
  InsightRegistryFetchFn,
} from './InsightSummaryCard.types';
export { InsightDeclineError } from './InsightSummaryCard.types';
// Task 033 — discriminated citation union (FR-07)
export type { Citation, AssessmentCitation, DocumentCitation, UnknownCitation } from './Citation.types';
export { isAssessmentCitation, isDocumentCitation } from './Citation.types';
export type { InsightCardState, InsightCardStatus, InsightCardAction } from './state';
export { insightCardReducer, initialInsightCardState, DEFAULT_ERROR_MESSAGE, DEFAULT_DECLINE_MESSAGE } from './state';
