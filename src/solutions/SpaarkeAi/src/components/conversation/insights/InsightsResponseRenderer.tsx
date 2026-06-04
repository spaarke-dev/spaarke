/**
 * InsightsResponseRenderer — Top-level renderer for `insights.query` chat-agent
 * tool responses (R5 task 026 / D2-16).
 *
 * Discriminates the Insights HTTP response envelope into four distinct render
 * cases per the Insights v1.0 contract (binding contract; brief §4):
 *
 *   1. Playbook-inference → mounts task 017's `StructuredOutputStreamWidget`
 *      via `INSIGHTS_PLAYBOOK_SCHEMA` (REUSE per R5 CLAUDE.md §3.1).
 *   2. Playbook-decline → Fluent v9 `MessageBar intent="warning"` + suggested
 *      actions list (per brief §4.5 + §6 D3).
 *   3. RAG observation (with citations) → citation-grounded prose with `[n]`
 *      clickable tokens (click stub; task 027 wires the dispatcher).
 *   4. RAG empty-result → muted hint per anti-hallucination guarantee
 *      (brief §4.4) — empty `answer` is NOT rendered verbatim.
 *
 * Error cases (12 codes per integration brief §5.1) are handled by task 029
 * (D2-19 — ProblemDetails surface). This renderer assumes the input is a
 * successful 200-OK response envelope; the chat-agent host wraps this
 * renderer in an error-aware Boundary that handles non-2xx outcomes upstream.
 *
 * ADR-013 §3.5 (Zone B boundary): the input is the typed `InsightsResponse`
 * union — no Insights server-internal types are imported. The discriminated
 * union is the public contract; v1.1 forward-compat fields (e.g.
 * `citations[].href`) survive via the LOOSE typing of envelope payloads in
 * `types.ts`.
 *
 * ADR-021 (Fluent v9 + dark mode): semantic tokens only.
 * ADR-022 (React 19): functional component + hooks.
 *
 * @see types.ts — `InsightsResponse` discriminated union + guards
 * @see PlaybookResponseRenderer.tsx — playbook-inference sub-renderer
 * @see DeclineResponseRenderer.tsx — decline sub-renderer
 * @see RagResponseRenderer.tsx — RAG sub-renderer (citation prose)
 * @see EmptyResultHint.tsx — empty-result hint
 */

import * as React from 'react';
import type {
  InsightsResponse,
  PlaybookDeclineResponse,
  PlaybookInferenceResponse,
  RagObservationResponse,
} from './types';
import { isDecline, isEmptyResult, isPlaybookInference, assertNever } from './types';
import { PlaybookResponseRenderer } from './PlaybookResponseRenderer';
import { RagResponseRenderer } from './RagResponseRenderer';
import { DeclineResponseRenderer } from './DeclineResponseRenderer';
import { EmptyResultHint } from './EmptyResultHint';
import { LowConfidenceBadge } from './LowConfidenceBadge';
import type { Citation } from './types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface InsightsResponseRendererProps {
  /**
   * Discriminated-union response envelope from `callInsightsQuery`
   * (task 025) or the BFF-side `InsightsQueryToolHandler` (task 024). The
   * renderer assumes this is a 200-OK success envelope; non-2xx error
   * handling is task 029's surface.
   */
  readonly response: InsightsResponse;
  /**
   * Controls the playbook-path rendering mode. When true, the structured-
   * output widget mounts in `streaming` mode and subscribes to PaneEventBus
   * field deltas (Wave F v1.1 SSE). When false, the widget mounts in `static`
   * mode with prefilled fields from the envelope. Defaults to false — v1.0
   * graceful fallback per NFR-11.
   */
  readonly streamingEnabled?: boolean;
  /**
   * When true, the playbook-path renderer dispatches `workspace.widget_load`
   * to open the structured output in a new Workspace pane tab instead of
   * rendering inline. The chat-pane host (ConversationPane) controls this
   * via a config flag; default is inline.
   */
  readonly dispatchPlaybookToWorkspace?: boolean;
  /**
   * Optional override for the RAG-path citation click handler. Defaults to
   * the stub `defaultCitationClickStub` in `RagResponseRenderer`. Task 027
   * (D2-17) will swap in a real PaneEventBus dispatch via this prop.
   */
  readonly onCitationClick?: (citation: Citation) => void;
  /**
   * Optional override for the low-confidence badge threshold (task 028 /
   * D2-18). When omitted, the threshold is read from the
   * `insightsRendererConfig` singleton (default 0.6 per spec FR-15). Per
   * R5 CLAUDE.md §3.2 + ADR-018 Flag Scope Discipline: this is a numeric
   * configuration value, NOT a feature flag. The badge itself is
   * unconditional UX surface area; only the threshold is tunable.
   */
  readonly confidenceThreshold?: number;
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const InsightsResponseRenderer: React.FC<InsightsResponseRendererProps> = ({
  response,
  streamingEnabled = false,
  dispatchPlaybookToWorkspace = false,
  onCitationClick,
  confidenceThreshold,
}) => {
  // Low-confidence advisory badge (task 028 / D2-18). Rendered at the TOP of
  // every response case wrapper (before the case-specific sub-renderer) so
  // it gates the entire output. The badge itself returns `null` when the
  // response confidence is at or above threshold (or absent / malformed) —
  // no DOM node when the advisory is not warranted.
  //
  // Per R5 CLAUDE.md §3.2 + ADR-018: threshold is a configuration VALUE
  // (numeric), not a feature flag. The badge is unconditional UX surface;
  // only the threshold is tunable.
  const lowConfidenceBadge = (
    <LowConfidenceBadge
      confidence={response.confidence}
      threshold={confidenceThreshold}
    />
  );

  // (1) Empty-result check FIRST — RAG-path with no citations + empty answer.
  //     Per brief §4.4, we MUST NOT render the empty `answer` verbatim.
  if (isEmptyResult(response)) {
    return (
      <div data-testid="insights-response-renderer" data-response-case="empty">
        {lowConfidenceBadge}
        <EmptyResultHint />
      </div>
    );
  }

  // (2) Decline check — playbook path with `structuredResult.kind === 'decline'`.
  //     200 OK structured "no" per brief §4.5.
  if (isDecline(response)) {
    return (
      <div data-testid="insights-response-renderer" data-response-case="decline">
        {lowConfidenceBadge}
        <DeclineResponseRenderer response={response as PlaybookDeclineResponse} />
      </div>
    );
  }

  // (3) + (4) Exhaustive switch over `path` for the remaining two cases.
  //     Playbook-inference → widget reuse; RAG observation → citation prose.
  switch (response.path) {
    case 'playbook': {
      // After the decline check above, the only remaining playbook variant
      // is `kind === 'inference'`. Narrow defensively via the type guard.
      if (isPlaybookInference(response)) {
        return (
          <div
            data-testid="insights-response-renderer"
            data-response-case="playbook-inference"
          >
            {lowConfidenceBadge}
            <PlaybookResponseRenderer
              response={response as PlaybookInferenceResponse}
              streamingEnabled={streamingEnabled}
              dispatchToWorkspace={dispatchPlaybookToWorkspace}
            />
          </div>
        );
      }
      // Defensive: unknown `structuredResult.kind` on the playbook branch.
      // Should be unreachable after the union-exhaustive guards above. Log
      // and fall through to an empty hint so the user is not left blank.
      // eslint-disable-next-line no-console
      console.warn(
        '[InsightsResponseRenderer] unhandled playbook structuredResult.kind',
        response.structuredResult,
      );
      return (
        <div
          data-testid="insights-response-renderer"
          data-response-case="unhandled-playbook"
        >
          {lowConfidenceBadge}
          <EmptyResultHint />
        </div>
      );
    }
    case 'rag':
      return (
        <div
          data-testid="insights-response-renderer"
          data-response-case="rag"
        >
          {lowConfidenceBadge}
          <RagResponseRenderer
            response={response as RagObservationResponse}
            onCitationClick={onCitationClick}
          />
        </div>
      );
    default:
      // Compile-time exhaustiveness — adding a new `path` value to the union
      // without updating this switch will fail the build.
      return assertNever(response);
  }
};

export default InsightsResponseRenderer;
