/**
 * RagResponseRenderer — Citation-grounded RAG prose renderer for Insights
 * Assistant responses (R5 task 026 / D2-16; task 027 / D2-17 wires the
 * citation click handler).
 *
 * Renders `response.answer` with `[n]` citation tokens tokenized into one of
 * two visual variants based on the v1.1 contract's optional `citations[i].href`
 * field (per spec UR-04 schema-plumbing outcome + NFR-11 graceful v1.0
 * fallback):
 *
 *   - **Clickable variant** — when `citation.href` is a non-empty string,
 *     `[n]` renders as a Fluent v9 `Button appearance="transparent"`. Clicking
 *     dispatches a `context.context_update` event on the existing `context`
 *     PaneEventBus channel (ADR-030 — no new channel, no new event-type
 *     discriminant). Payload: `{ type: 'context_update', contextType:
 *     'file-preview', contextData: { url: citation.href, displayName:
 *     citation.source } }`. The `'file-preview'` widgetType matches the
 *     `FilePreviewContextWidget` registry key (task 018; verified in
 *     `register-context-widgets.ts`).
 *
 *   - **Non-clickable variant** — when `citation.href` is null, undefined, or
 *     missing entirely (v1.0 deployment OR v1.1 observation-only-href spike
 *     outcome where document citations don't yet carry hrefs), `[n]` renders
 *     as a non-interactive `<span>` styled identically to the button text
 *     (same brand foreground + semibold weight). No click handler is wired,
 *     no error UX is shown, no console warnings fire — pure graceful
 *     degradation per spec NFR-11.
 *
 * Mixed-mode (some citations have `href`, some don't) is supported per-entry
 * via a `c.href ? <Button> : <span>` ternary — no all-or-nothing decision at
 * the array level (UR-04 observation-only-href contingency).
 *
 * Below the prose, a citation reference list (Fluent v9 `Card` with one
 * `Text` row per citation: `[n] source — excerpt`) is always rendered so
 * users can read citation details even when no `href` is available.
 *
 * ADR-021 (Fluent v9 + dark mode): semantic tokens only.
 * ADR-022 (React 19): functional component + hooks (`useDispatchPaneEvent`
 * called at component top level per React 19 hook rules; the resulting
 * dispatch function is used inside the click handler).
 * ADR-030 (PaneEventBus closed channels): dispatches existing
 * `context.context_update` discriminant on the existing `context` channel —
 * zero new channels, zero new event types.
 * ADR-013 §3.5 (Zone B boundary): consumes the HTTP response envelope only;
 * the URL in `citation.href` is pre-resolved server-side by the Insights BFF
 * via `DocumentCheckoutService.GetPreviewUrlAsync(driveId, itemId, ct)` per
 * `insights-engine-contract-v1.1-request.md` §0a — R5 passes it through
 * verbatim with no client-side construction.
 * ADR-018: no new feature flags; clickable behaviour is auto-detected from
 * per-citation `href` presence.
 *
 * @see types.ts — tokenizeCitations + Citation + RagObservationResponse types
 * @see ContextPaneController.tsx — `context_update` subscriber
 * @see FilePreviewContextWidget.tsx — registered as `'file-preview'` (task 018)
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Card,
} from '@fluentui/react-components';
import { useDispatchPaneEvent } from '@spaarke/ai-widgets';
import type { RagObservationResponse, Citation, AnswerToken } from './types';
import { tokenizeCitations } from './types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface RagResponseRendererProps {
  /** Discriminated-union response narrowed to the RAG observation variant. */
  readonly response: RagObservationResponse;
  /**
   * Optional override for the citation click handler.
   *
   * - When OMITTED (production default), the click dispatches the
   *   `context.context_update` PaneEventBus event with the citation's `href`
   *   (task 027 / D2-17 wiring). Non-clickable variants (citations without
   *   `href`) never invoke this handler regardless of whether it is provided.
   *
   * - When PROVIDED, the explicit handler is invoked INSTEAD of the
   *   PaneEventBus dispatch. Tests use this seam to assert click semantics in
   *   isolation; production callers can override to route the click somewhere
   *   other than the Context pane (e.g. open in a new browser tab, log
   *   telemetry, etc.).
   *
   * The handler is invoked ONLY when the citation has a non-empty `href`
   * (clickable variant). For citations without `href` (non-clickable span
   * variant), no click affordance is rendered and this prop is irrelevant.
   */
  readonly onCitationClick?: (citation: Citation) => void;
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 semantic tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    width: '100%',
  },
  prose: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    lineHeight: tokens.lineHeightBase400,
    margin: 0,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
  citationButton: {
    // Inline-flow button — keep it tight against surrounding text.
    minWidth: 'unset',
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    paddingTop: 0,
    paddingBottom: 0,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    verticalAlign: 'baseline',
  },
  // Non-clickable span variant — display-name-only fallback when `href` is
  // absent (v1.0 OR v1.1 observation-only-href contingency per UR-04). Styled
  // to the SAME visual baseline as `citationButton` so mixed-mode rendering
  // (some clickable, some not) reads uniformly in the prose flow. No
  // background, no border, no hover affordance — purely a non-interactive
  // marker.
  citationSpan: {
    display: 'inline',
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    verticalAlign: 'baseline',
  },
  referenceCard: {
    padding: tokens.spacingHorizontalM,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  referenceHeader: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: tokens.strokeWidthThin,
  },
  referenceRow: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
    lineHeight: tokens.lineHeightBase300,
  },
  referenceNumber: {
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    flexShrink: 0,
  },
  referenceBody: {
    flex: 1,
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'pre-wrap',
    wordBreak: 'break-word',
  },
});

// ---------------------------------------------------------------------------
// Default click handler — PaneEventBus dispatch (task 027)
// ---------------------------------------------------------------------------

/**
 * Compute whether a citation should render as the clickable variant. A
 * citation is clickable iff `href` is a non-empty string (per spec UR-04 —
 * v1.0 fallback when `href: null` or absent renders as non-clickable span).
 *
 * Exported for unit-test convenience.
 */
export function isClickableCitation(citation: Citation): boolean {
  return typeof citation.href === 'string' && citation.href.length > 0;
}

/**
 * Default citation click stub (legacy seam from task 026).
 *
 * Task 027 wires the real PaneEventBus dispatch INSIDE the renderer via the
 * `useDispatchPaneEvent` hook (React 19 hook rules require the hook to be
 * called at component top level, not inside a stub). This standalone export
 * remains for backward compatibility with task 026's evidence + tests that
 * spy on it, and as a no-op fallback for callers that explicitly pass it as
 * `onCitationClick` (e.g. a test that wants to assert the function shape
 * without depending on a `PaneEventBusProvider`).
 *
 * The export is INTENTIONALLY a no-op + console log; production click
 * semantics live in the component body's `handleClick` callback.
 */
export function defaultCitationClickStub(citation: Citation): void {
  // eslint-disable-next-line no-console
  console.debug(
    '[RagResponseRenderer] citation click (stub — production dispatch lives in the component)',
    {
      n: citation.n,
      source: citation.source,
      observationId: citation.observationId,
      chunkId: citation.chunkId,
      href: citation.href ?? null,
    },
  );
}

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const RagResponseRenderer: React.FC<RagResponseRendererProps> = ({
  response,
  onCitationClick,
}) => {
  const styles = useStyles();
  // useDispatchPaneEvent MUST be called at component top level per React 19
  // hook rules (and the hook's own contract). The returned `dispatch`
  // function is stable across re-renders, so it is safe to reference inside
  // `useCallback`-memoized handlers.
  const dispatch = useDispatchPaneEvent();

  const tokens_ = React.useMemo<readonly AnswerToken[]>(
    () => tokenizeCitations(response.answer),
    [response.answer],
  );

  // Resolve a citation by its `n` token value. Returns undefined for
  // out-of-range tokens (e.g. `[5]` referenced in prose but only 3 citations
  // in the array). Out-of-range tokens render as non-clickable spans via the
  // `isClickableCitation` check below (no synthetic citation invented).
  const citationsByN = React.useMemo<ReadonlyMap<number, Citation>>(
    () => {
      const map = new Map<number, Citation>();
      for (const citation of response.citations) {
        map.set(citation.n, citation);
      }
      return map;
    },
    [response.citations],
  );

  // Production click handler — fires ONLY for clickable variants (citations
  // with a non-empty `href`). The non-clickable span variant has no
  // `onClick` wiring at all, so this is never invoked for it.
  //
  // Dispatch path (task 027 / D2-17):
  //   1. Look up the citation by `n` (out-of-range → log + no-op).
  //   2. If the caller passed an explicit `onCitationClick`, invoke that
  //      INSTEAD of the PaneEventBus dispatch (test seam + production
  //      override path).
  //   3. Otherwise dispatch `context.context_update` with:
  //        - `contextType: 'file-preview'` (matches the
  //          `FilePreviewContextWidget` registry key from task 018 +
  //          `register-context-widgets.ts`)
  //        - `contextData: { url: citation.href, displayName: citation.source }`
  //      The `FilePreviewContextWidget` (or its host wrapper) consumes the
  //      event via `ContextPaneController` and opens the URL.
  const handleClick = React.useCallback(
    (n: number) => {
      const citation = citationsByN.get(n);
      if (citation === undefined) {
        // Defensive: prose references an `[n]` token but the citations array
        // has no entry. Log via debug so the click is observable; no error
        // UX (NFR-11 — graceful degradation).
        // eslint-disable-next-line no-console
        console.debug(
          '[RagResponseRenderer] citation click — n not found in citations array',
          { n, availableNs: Array.from(citationsByN.keys()) },
        );
        return;
      }
      // Caller-provided override takes precedence (tests + production
      // alternate routes). Production callers typically OMIT this prop and
      // rely on the PaneEventBus dispatch below.
      if (onCitationClick !== undefined) {
        onCitationClick(citation);
        return;
      }
      // PaneEventBus dispatch (task 027 default behaviour). `href` MUST be
      // non-empty here — `handleClick` is only wired to the clickable
      // variant. We re-check defensively to silence the TS narrowing.
      const url = citation.href;
      if (typeof url !== 'string' || url.length === 0) {
        // Should be unreachable — the button isn't rendered without a
        // truthy href. Defensive no-op to keep the contract pure.
        return;
      }
      dispatch('context', {
        type: 'context_update',
        contextType: 'file-preview',
        contextData: {
          url,
          displayName: citation.source,
        },
      });
    },
    [citationsByN, dispatch, onCitationClick],
  );

  return (
    <div className={styles.container} data-testid="rag-response-renderer">
      {/* Prose with inline citation markers (clickable OR non-clickable). */}
      <p className={styles.prose} data-testid="rag-prose">
        {tokens_.map((token, idx) => {
          if (token.type === 'text') {
            return (
              <span key={`text-${idx}`} data-token-type="text">
                {token.content}
              </span>
            );
          }
          // Citation token. The variant is decided per-citation based on the
          // resolved Citation's `href`:
          //   - clickable (Button) when citation.href is a non-empty string
          //   - non-clickable (span) otherwise
          //
          // The lookup is by `n`; if `n` is out of range, render as a span
          // (the citation cannot be resolved, so we cannot dispatch).
          const citation = citationsByN.get(token.n);
          const clickable = citation !== undefined && isClickableCitation(citation);

          if (clickable) {
            return (
              <Button
                key={`citation-${idx}-n${token.n}`}
                appearance="transparent"
                size="small"
                className={styles.citationButton}
                onClick={() => handleClick(token.n)}
                data-token-type="citation"
                data-citation-n={token.n}
                data-citation-variant="clickable"
                data-testid={`citation-token-${token.n}`}
                aria-label={`Citation ${token.n}: open ${citation!.source}`}
              >
                [{token.n}]
              </Button>
            );
          }

          // Non-clickable span variant — display-name-only per spec FR-14 +
          // NFR-11. Same data-testid as the clickable variant so existing
          // tests that locate by `citation-token-{n}` continue to find the
          // marker; `data-citation-variant="non-clickable"` lets tests
          // disambiguate. Out-of-range tokens (citation === undefined) still
          // render as spans — the prose `[n]` is preserved verbatim so the
          // reader can see the citation marker even if its target is
          // missing.
          return (
            <span
              key={`citation-${idx}-n${token.n}`}
              className={styles.citationSpan}
              data-token-type="citation"
              data-citation-n={token.n}
              data-citation-variant="non-clickable"
              data-testid={`citation-token-${token.n}`}
              aria-label={
                citation !== undefined
                  ? `Citation ${token.n}: ${citation.source} (source not directly openable)`
                  : `Citation ${token.n}`
              }
            >
              [{token.n}]
            </span>
          );
        })}
      </p>

      {/* Citation reference list — visible regardless of click wiring. */}
      {response.citations.length > 0 && (
        <Card className={styles.referenceCard} data-testid="rag-citations-list">
          <Text className={styles.referenceHeader}>References</Text>
          {response.citations.map(citation => (
            <div
              key={`ref-${citation.n}-${citation.chunkId}`}
              className={styles.referenceRow}
              data-citation-n={citation.n}
            >
              <Text className={styles.referenceNumber}>[{citation.n}]</Text>
              <Text className={styles.referenceBody}>
                {citation.source}
                {citation.excerpt ? ` — ${citation.excerpt}` : ''}
              </Text>
            </div>
          ))}
        </Card>
      )}
    </div>
  );
};

export default RagResponseRenderer;
