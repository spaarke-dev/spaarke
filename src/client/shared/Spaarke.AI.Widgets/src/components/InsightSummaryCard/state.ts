/**
 * @spaarke/ai-widgets — InsightSummaryCard state machine (Task 031)
 *
 * Implements the 6-state machine per spec FR-06:
 *
 *   idle  ─────────► loading ─────► loaded ──► stale ──► loading (refresh)
 *                       │              │
 *                       ├──► error     └──► (terminal until refresh)
 *                       │
 *                       └──► decline
 *
 * Design anchors:
 *   - Extends `AiSummaryPopover`'s 4-state baseline (idle / loading / loaded /
 *     error) with `decline` (insufficient evidence per FR-06) and `stale`
 *     (cache TTL expired — host triggers `MARK_STALE`).
 *   - `useReducer` (NOT chained `useState` hooks) per DR-001 §Consequences —
 *     6 states + 4 transitions earn explicit reducer dispatch.
 *   - `InsightEnvelope` is r1-local (richer than `AiSummaryPopover`'s
 *     `ISummaryData`). Citations + decline reason + cache freshness all live
 *     here. r2+ may extend the shape; consumers should accept
 *     non-exhaustively (forward-compatible).
 *
 * Owner constraints honoured:
 *   - Q-U1: no `@v1`/`@vN` identifier-suffix vernacular anywhere.
 *   - Q-U3: NO feedback state, NO feedback action.
 *
 * @see projects/ai-spaarke-insights-engine-widgets-r1/spec.md FR-06
 * @see projects/ai-spaarke-insights-engine-widgets-r1/decisions/DR-001-component-reuse.md
 */

import type { InsightCitationRef } from './InsightSummaryCard.types';

// ---------------------------------------------------------------------------
// Insight envelope — r1 payload shape returned by the lazy-load callback.
// ---------------------------------------------------------------------------

/**
 * Payload returned by `onFetchInsight`.
 *
 * Fields are optional so the BFF can return partial results (e.g., narrative
 * only, no citations) without breaking the type. Consumers should treat this
 * as forward-compatible — r2+ may add fields.
 */
export interface InsightEnvelope {
  /** Short headline / TLDR summary. */
  tldr?: string | null;
  /** Full narrative (markdown-friendly; rendered as plain text in r1). */
  narrative?: string | null;
  /** Citation references surfaced inline by the narrative. */
  citations?: InsightCitationRef[];
  /** Timestamp (ISO 8601) when the insight was produced. Drives "Last updated Nm ago". */
  generatedAt?: string;
}

// ---------------------------------------------------------------------------
// State union — the 6 FR-06 states.
// ---------------------------------------------------------------------------

/**
 * The 6 reachable insight card states per FR-06.
 *
 * - `idle`    — initial; KPI slot + sparkle visible, no narrative
 * - `loading` — invocation in flight; skeleton placeholder
 * - `loaded`  — narrative + citations rendered
 * - `error`   — graceful error message (e.g., FeatureDisabled 503 per ADR-032)
 * - `decline` — insufficient evidence (per FR-06 exact text)
 * - `stale`   — cache TTL expired; loaded content shown with refresh affordance
 */
export type InsightCardStatus = 'idle' | 'loading' | 'loaded' | 'error' | 'decline' | 'stale';

/**
 * Discriminated state shape. `data` is present on `loaded` and `stale`;
 * `error` and `decline` carry a message; `idle` and `loading` carry no
 * payload.
 */
export type InsightCardState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'loaded'; data: InsightEnvelope }
  | { status: 'stale'; data: InsightEnvelope }
  | { status: 'error'; message: string; diagnosticCode?: string }
  | { status: 'decline'; message: string; recommendedAction?: string };

// ---------------------------------------------------------------------------
// Actions — the explicit transitions.
// ---------------------------------------------------------------------------

/**
 * Reducer action union.
 *
 * Transitions per FR-06:
 *   - `BEGIN_FETCH`  : idle → loading; stale → loading (refresh)
 *   - `FETCH_SUCCESS`: loading → loaded
 *   - `FETCH_ERROR`  : loading → error
 *   - `FETCH_DECLINE`: loading → decline
 *   - `MARK_STALE`   : loaded → stale (host triggers when TTL expires)
 *   - `RESET`        : any → idle (e.g., subject change)
 */
export type InsightCardAction =
  | { type: 'BEGIN_FETCH' }
  | { type: 'FETCH_SUCCESS'; data: InsightEnvelope }
  | { type: 'FETCH_ERROR'; message: string; diagnosticCode?: string }
  | { type: 'FETCH_DECLINE'; message: string; recommendedAction?: string }
  | { type: 'MARK_STALE' }
  | { type: 'HYDRATE'; data: InsightEnvelope; stale?: boolean }
  | { type: 'RESET' };

// ---------------------------------------------------------------------------
// Default messages — owner-confirmed exact text per FR-06.
// ---------------------------------------------------------------------------

/**
 * Default error message per spec FR-06.
 *
 * Used when the fetch callback rejects without supplying a specific message
 * (e.g., a 503 FeatureDisabled response per ADR-032).
 */
export const DEFAULT_ERROR_MESSAGE = 'AI summaries unavailable in this environment';

/**
 * Default decline message per spec FR-06 (owner-confirmed exact text).
 *
 * Used when the fetch callback resolves with an insufficient-evidence outcome
 * but supplies no specific message.
 */
export const DEFAULT_DECLINE_MESSAGE = 'Insufficient data is available to provide Insights Analysis';

// ---------------------------------------------------------------------------
// Initial state + reducer.
// ---------------------------------------------------------------------------

/** The reducer's initial state — always `idle`. */
export const initialInsightCardState: InsightCardState = { status: 'idle' };

/**
 * Pure reducer mapping `(state, action)` → next state.
 *
 * Notes:
 *   - `MARK_STALE` is a no-op outside `loaded` — we don't want host TTL timers
 *     to fight in-flight loads or to "promote" a `decline`/`error` to `stale`.
 *   - `BEGIN_FETCH` from `loaded` (NOT `stale`) is allowed as an explicit
 *     refresh path (e.g., user clicks "refresh" before TTL expiry).
 *   - `RESET` returns to `idle` from any state — used when host swaps subject.
 *
 * @param state  current state
 * @param action transition trigger
 * @returns next state (or current state if the transition is a no-op)
 */
export function insightCardReducer(state: InsightCardState, action: InsightCardAction): InsightCardState {
  switch (action.type) {
    case 'BEGIN_FETCH':
      // Allowed from idle, stale, loaded, error, decline — but NOT from loading
      // (avoid double-fetch races; host-side guard plus this reducer-side guard).
      if (state.status === 'loading') {
        return state;
      }
      return { status: 'loading' };

    case 'FETCH_SUCCESS':
      // Only meaningful while loading; if a late-arriving success races a
      // RESET we drop it to avoid resurrecting stale data.
      if (state.status !== 'loading') {
        return state;
      }
      return { status: 'loaded', data: action.data };

    case 'FETCH_ERROR':
      if (state.status !== 'loading') {
        return state;
      }
      return {
        status: 'error',
        message: action.message || DEFAULT_ERROR_MESSAGE,
        ...(action.diagnosticCode ? { diagnosticCode: action.diagnosticCode } : {}),
      };

    case 'FETCH_DECLINE':
      if (state.status !== 'loading') {
        return state;
      }
      return {
        status: 'decline',
        message: action.message || DEFAULT_DECLINE_MESSAGE,
        ...(action.recommendedAction ? { recommendedAction: action.recommendedAction } : {}),
      };

    case 'MARK_STALE':
      // Only `loaded` content can go stale. `stale → stale` is a no-op.
      if (state.status !== 'loaded') {
        return state;
      }
      return { status: 'stale', data: state.data };

    case 'HYDRATE':
      // Hydrate from a host-provided envelope (e.g., Task 042 mount glue
      // reads sprk_performancesummary and seeds the card immediately per FR-19).
      // Skips the loading state to avoid a flash of skeleton when the host
      // already has the data. Stale flag lets the host signal that the stored
      // envelope is past its TTL and a background refresh is in flight.
      return action.stale ? { status: 'stale', data: action.data } : { status: 'loaded', data: action.data };

    case 'RESET':
      return initialInsightCardState;

    default: {
      // Exhaustiveness check — TS will error here if a new action variant is
      // added but not handled above.
      const _exhaustive: never = action;
      return _exhaustive;
    }
  }
}
