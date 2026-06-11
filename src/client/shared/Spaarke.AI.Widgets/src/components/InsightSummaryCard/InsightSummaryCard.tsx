/**
 * @spaarke/ai-widgets — InsightSummaryCard
 *
 * Per-record AI insight surface composed of a Fluent v9 `<Card>` (inline
 * surface) with a `<Popover>` for inline expand and a `<Dialog>` for the
 * manual modal-expand affordance (FR-02). Implements the 6-state machine
 * per FR-06 (idle / loading / loaded / error / decline / stale).
 *
 * IMPORTANT — Portal-gotcha rule (binding per
 * `.claude/patterns/ui/fluent-v9-portal-gotcha.md` + ADR-021):
 *   Both the Popover surface AND the Dialog surface render via React Portal
 *   and escape the host's outer FluentProvider DOM subtree. We re-wrap each
 *   portal surface in its own `<FluentProvider theme={theme}>` so dark mode
 *   (and any custom tenant theme) propagates correctly. Missing this re-wrap
 *   is the #1 cause of MDA dark-mode regressions in Spaarke (white popovers
 *   in dark mode).
 *
 * Design anchors:
 *   - Lazy-load pattern from `AiSummaryPopover` (`AiSummaryPopover.tsx`
 *     lines 99-133): on-open-fetch-once gate; callback-based service injection.
 *   - State machine from `state.ts` (useReducer; 6 states; explicit transitions
 *     per DR-001 §Consequences "Recommend useReducer in Phase 3").
 *   - Surface = Card + Popover for inline; Card footer "Expand" button opens
 *     the modal Dialog (FR-02 manual escalation; no auto-promote per
 *     spec.md assumption #12).
 *   - NO `onFeedback` (Q-U3 deferral); NO `@v1`/`@vN` vernacular (Q-U1 ban).
 *
 * @see ADR-021 — Fluent v9 + semantic tokens (binding)
 * @see ADR-012 — Component ships in @spaarke/ai-widgets per DR-001
 * @see .claude/patterns/ui/fluent-v9-portal-gotcha.md
 * @see projects/ai-spaarke-insights-engine-widgets-r1/decisions/DR-001-component-reuse.md
 * @see projects/ai-spaarke-insights-engine-widgets-r1/spec.md FR-01 / FR-02 / FR-06
 */

import * as React from 'react';
import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import {
  Button,
  Card,
  CardHeader,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  FluentProvider,
  Link,
  mergeClasses,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  Spinner,
  Text,
  webLightTheme,
} from '@fluentui/react-components';
import { ArrowSync20Regular, Sparkle20Regular } from '@fluentui/react-icons';

import { InsightDeclineError } from './InsightSummaryCard.types';
import type { InsightRegistryEntry, InsightSummaryCardProps } from './InsightSummaryCard.types';
import type { Citation } from './Citation.types';
import { isAssessmentCitation, isDocumentCitation } from './Citation.types';
import { DEFAULT_DECLINE_MESSAGE, DEFAULT_ERROR_MESSAGE, initialInsightCardState, insightCardReducer } from './state';
import type { InsightCardState } from './state';
import { useInsightSummaryCardStyles } from './useInsightSummaryCardStyles';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Display-friendly fallback for the topic identifier (e.g. `matter-health`
 * → "Matter Health"). Task 040+ host integration will source the display
 * label from `sprk_aitopicregistry.sprk_displayname`; until then this
 * fallback keeps the trigger / header labels readable.
 */
function topicDisplayFallback(topic: string): string {
  if (!topic) {
    return 'Insight';
  }
  return topic
    .split('-')
    .filter(Boolean)
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

/**
 * Type guard — distinguishes an `InsightDeclineError` (signals the `decline`
 * state) from a generic `Error` (signals the `error` state).
 */
function isDeclineError(err: unknown): err is InsightDeclineError {
  return (
    err instanceof InsightDeclineError ||
    (typeof err === 'object' && err !== null && 'kind' in err && (err as { kind?: unknown }).kind === 'decline')
  );
}

// ---------------------------------------------------------------------------
// Citation renderer (FR-07 / Task 033) — type-discriminated switch.
//
// Adding a new known citation type means adding a new `case` arm here PLUS
// a new variant in `Citation.types.ts`. NO new component, NO new prop —
// that's the FR-07 extensibility contract.
//
// The `default` arm is the FR-07 graceful-fallback acceptance: any unknown
// `type` value renders as plain text and does NOT invoke `onCitationClick`.
// ---------------------------------------------------------------------------

interface IRenderCitationProps {
  citation: Citation;
  styles: ReturnType<typeof useInsightSummaryCardStyles>;
  onCitationClick: ((citation: Citation) => void) | undefined;
}

const RenderCitation: React.FC<IRenderCitationProps> = ({ citation, styles, onCitationClick }) => {
  // Display text falls back to id when label is absent; both variants share this.
  const linkText = citation.label || citation.id;

  // We use type-guard functions (NOT a switch on `citation.type`) because
  // `UnknownCitation['type']` is widened to `string` so it can absorb any
  // future BFF-shipped type value. A literal `case 'assessment':` would
  // narrow to `AssessmentCitation | UnknownCitation` (since `'assessment'`
  // is assignable to `string`), defeating field access. The guards narrow
  // cleanly to the precise interface.

  if (isAssessmentCitation(citation)) {
    // In-product navigation — host translates `assessmentId` into a
    // `Xrm.Navigation.openForm` (or equivalent) call inside the callback.
    const handleClick = (ev: React.MouseEvent<HTMLAnchorElement>) => {
      ev.preventDefault();
      onCitationClick?.(citation);
    };
    return (
      <span className={styles.citationItem} data-testid="citation-assessment">
        <span className={styles.citationType}>assessment</span>
        <Link
          href="#"
          onClick={handleClick}
          data-testid="citation-assessment-link"
          data-assessment-id={citation.assessmentId}
        >
          {linkText}
        </Link>
      </span>
    );
  }

  if (isDocumentCitation(citation)) {
    // SPE href preferred; falls back to in-product navigation when only
    // a `documentId` is supplied. The host decides whether to open the
    // SPE viewer or the `sprk_document` MDA form via the callback.
    const handleClick = (ev: React.MouseEvent<HTMLAnchorElement>) => {
      ev.preventDefault();
      onCitationClick?.(citation);
    };
    // Render href when SPE href is present so middle-click / open-in-tab
    // works naturally; click handler still preempts and routes through
    // onCitationClick so the host can take over.
    const href = citation.speHref || '#';
    return (
      <span className={styles.citationItem} data-testid="citation-document">
        <span className={styles.citationType}>document</span>
        <Link
          href={href}
          onClick={handleClick}
          data-testid="citation-document-link"
          {...(citation.documentId ? { 'data-document-id': citation.documentId } : {})}
        >
          {linkText}
        </Link>
      </span>
    );
  }

  // FR-07 graceful-fallback acceptance: unknown `type` values render
  // as plain text. No click handler — the host can't act on a type
  // the card doesn't recognise.
  return (
    <span
      className={mergeClasses(styles.citationItem, styles.citationUnknown)}
      data-testid="citation-unknown"
      data-citation-type={citation.type}
    >
      {linkText}
    </span>
  );
};

// ---------------------------------------------------------------------------
// Body renderer — pure function mapping state → JSX. Shared by Popover
// surface AND Dialog body (single source of truth — only the chrome differs).
// ---------------------------------------------------------------------------

interface IInsightBodyProps {
  state: InsightCardState;
  styles: ReturnType<typeof useInsightSummaryCardStyles>;
  onCitationClick: ((citation: Citation) => void) | undefined;
}

const InsightBody: React.FC<IInsightBodyProps> = ({ state, styles, onCitationClick }) => {
  switch (state.status) {
    case 'idle':
      return (
        <div className={styles.idleAffordance}>
          <Text>Click to load insight</Text>
        </div>
      );

    case 'loading':
      return (
        <div className={styles.skeleton} aria-live="polite" aria-busy="true">
          <Spinner size="small" label="Loading insight..." />
          <div className={mergeClasses(styles.skeletonBar, styles.skeletonBarShort)} />
          <div className={mergeClasses(styles.skeletonBar, styles.skeletonBarLong)} />
          <div className={mergeClasses(styles.skeletonBar, styles.skeletonBarMedium)} />
        </div>
      );

    case 'error':
      return (
        <div className={styles.errorBlock} role="alert">
          <Text>{state.message}</Text>
          {state.diagnosticCode && (
            <Text className={styles.errorDiagnostic}>Diagnostic code: {state.diagnosticCode}</Text>
          )}
        </div>
      );

    case 'decline':
      // WCAG 4.1.3 Status Messages — Task 036 ISSUE-1 fix (2026-06-11).
      // AT users need to be told when loading transitions to decline; without
      // role="status" the announcement is silent.
      return (
        <div className={styles.declineBlock} role="status" aria-live="polite">
          <Text>{state.message}</Text>
          {state.recommendedAction && <Text className={styles.declineRecommendation}>{state.recommendedAction}</Text>}
        </div>
      );

    case 'loaded':
    case 'stale': {
      const { data } = state;
      const citations = data.citations ?? [];
      return (
        <div className={styles.narrative}>
          {state.status === 'stale' && (
            <div className={styles.staleBanner} role="status">
              <Text>Insight may be out of date. Click refresh to reload.</Text>
            </div>
          )}
          {data.tldr && <Text weight="semibold">{data.tldr}</Text>}
          {data.narrative && <Text className={styles.narrativeBody}>{data.narrative}</Text>}
          {!data.tldr && !data.narrative && <Text>No insight content available.</Text>}
          {citations.length > 0 && (
            <div className={styles.citationsList} data-testid="insight-summary-card-citations">
              <Text className={styles.citationsHeader}>Citations</Text>
              {citations.map(citation => (
                <RenderCitation
                  key={citation.id}
                  citation={citation}
                  styles={styles}
                  onCitationClick={onCitationClick}
                />
              ))}
            </div>
          )}
        </div>
      );
    }

    default: {
      // Exhaustiveness check — TS error if a status variant is added but
      // not handled above.
      const _exhaustive: never = state;
      return _exhaustive;
    }
  }
};

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * InsightSummaryCard — Fluent v9 per-record AI insight surface.
 *
 * Renders an inline Card with a Sparkle-trigger Popover (inline expand) and
 * an optional Dialog (modal expand). Implements the FR-06 6-state machine
 * via `useReducer`. Both portal surfaces are theme-re-wrapped per the
 * portal-gotcha pattern.
 *
 * NOTE: No `onFeedback` prop (Q-U3 deferred). Do not add one without
 * re-opening Q-U3 + ADR-015.
 *
 * @example
 * ```tsx
 * <InsightSummaryCard
 *   topic="matter-health"
 *   subject={`matter:${matterId}`}
 *   mode="single"
 *   theme={isDark ? webDarkTheme : webLightTheme}
 *   onFetchInsight={async () => {
 *     const resp = await bff.invokeInsight({ topic: 'matter-health', subject });
 *     if (resp.outcome === 'declined') {
 *       throw new InsightDeclineError(resp.message, resp.recommendedAction);
 *     }
 *     return resp.envelope;
 *   }}
 * />
 * ```
 */
export const InsightSummaryCard: React.FC<InsightSummaryCardProps> = ({
  topic,
  subject,
  mode = 'single',
  parameters: _parameters, // wired in Task 040+ host integration
  kpiSlot,
  onCitationClick, // wired in Task 033 citation rendering (FR-07)
  onFetchInsight,
  onFetchRegistry,
  onRegistryResolved,
  theme,
  triggerLabel,
  initialEnvelope,
  initialEnvelopeStale,
  className,
}) => {
  const styles = useInsightSummaryCardStyles();
  const topicLabel = topicDisplayFallback(topic);

  // FR-19 immediate-render path: when the host supplies a stored envelope
  // (e.g., Task 042 mount glue reads sprk_performancesummary), hydrate
  // directly into 'loaded' or 'stale' — skipping the idle/loading flash.
  // Added 2026-06-11 (Task 042 contract gap fix).
  const initialState = initialEnvelope
    ? insightCardReducer(initialInsightCardState, {
        type: 'HYDRATE',
        data: initialEnvelope,
        stale: initialEnvelopeStale,
      })
    : initialInsightCardState;

  const [state, dispatch] = useReducer(insightCardReducer, initialState);
  const [dialogOpen, setDialogOpen] = useState(false);

  // ── FR-05 registry mount-check (Task 032) ───────────────────────────────
  //
  // Possible registry states:
  //   - 'idle'        — pre-fetch (initial); no render until check completes.
  //                     When `onFetchRegistry` is absent, we IMMEDIATELY mark
  //                     'enabled' (back-compat for pre-Task-032 consumers and
  //                     for dev playgrounds with no Dataverse host).
  //   - 'checking'    — fetch in flight; no render (avoid sparkle flicker).
  //   - 'absent'      — no matching row → render nothing per FR-05.
  //   - 'disabled'    — row found but `sprk_enabled=false` → render nothing.
  //   - 'enabled'     — row found AND enabled → proceed to normal Card render.
  //   - 'error'       — registry fetch failed; surface via the existing error
  //                     state (not a separate render — the user needs to see
  //                     that something went wrong, distinct from "not configured").
  //
  // `useRef` on cancellation flag avoids state-update-after-unmount and the
  // double-fetch race when React strict mode mounts twice in dev. The
  // `(topic, mode)` tuple re-runs the check if the host changes scope while
  // the component remains mounted (rare; defensive).
  type RegistryStatus = 'idle' | 'checking' | 'absent' | 'disabled' | 'enabled' | 'error';
  const [registryStatus, setRegistryStatus] = useState<RegistryStatus>(onFetchRegistry ? 'idle' : 'enabled');
  const registryEntryRef = useRef<InsightRegistryEntry | null>(null);

  useEffect(() => {
    if (!onFetchRegistry) {
      // No adapter wired — skip the check (back-compat).
      setRegistryStatus('enabled');
      registryEntryRef.current = null;
      return undefined;
    }

    let cancelled = false;
    setRegistryStatus('checking');

    onFetchRegistry(topic, mode)
      .then(entry => {
        if (cancelled) {
          return;
        }
        if (entry === null) {
          // FR-05 binding: orphan sparkle forbidden → render nothing.
          registryEntryRef.current = null;
          setRegistryStatus('absent');
          return;
        }
        if (!entry.enabled) {
          // Soft-off by SME — render nothing.
          registryEntryRef.current = null;
          setRegistryStatus('disabled');
          return;
        }
        // Row exists AND enabled — proceed to idle render. Notify host so
        // it can wire downstream TTL behaviour (cacheTtlMinutes from the row
        // per FR-21 — host owns the timer, NOT this component).
        registryEntryRef.current = entry;
        setRegistryStatus('enabled');
        onRegistryResolved?.(entry);
      })
      .catch((err: unknown) => {
        if (cancelled) {
          return;
        }
        const message = err instanceof Error && err.message ? err.message : 'Failed to resolve insight topic registry';
        // Diagnostic log so operators can trace the failure. Per project
        // CLAUDE.md (Q-U1), no `@v1`/`@vN` vernacular in log message.
        // eslint-disable-next-line no-console
        console.warn('[InsightSummaryCard] registry fetch failed for topic=%s mode=%s: %s', topic, mode, message);
        registryEntryRef.current = null;
        setRegistryStatus('error');
        // Surface the failure via the existing FR-06 error state. We use
        // BEGIN_FETCH + FETCH_ERROR to enter the error branch cleanly (the
        // reducer guards FETCH_ERROR to only fire from 'loading').
        dispatch({ type: 'BEGIN_FETCH' });
        dispatch({ type: 'FETCH_ERROR', message });
      });

    return () => {
      cancelled = true;
    };
    // onRegistryResolved is intentionally NOT in deps — re-firing the
    // notification on host callback identity changes would surprise hosts
    // that rebuild closures per render. The `(topic, mode)` tuple is the
    // canonical re-fetch trigger.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [onFetchRegistry, topic, mode]);

  // FR-05 acceptance: when the registry row is absent OR disabled, the
  // component renders NOTHING (no Card, no sparkle, no chrome). 'idle' and
  // 'checking' also render nothing to avoid flicker pre-resolution. The
  // 'error' state falls through to the normal Card render so the failure is
  // visible.
  if (
    registryStatus === 'idle' ||
    registryStatus === 'checking' ||
    registryStatus === 'absent' ||
    registryStatus === 'disabled'
  ) {
    return null;
  }

  // Re-wrap theme — defaults to webLightTheme if the host did not pass one.
  // Production hosts (Matter form) MUST pass the active MDA theme per
  // ADR-021; defaulting here keeps dev playgrounds (Task 035) clean.
  const portalTheme = theme ?? webLightTheme;

  // ── Lazy-load callback — mirrors AiSummaryPopover's on-open-fetch-once gate.
  //
  // `force` (FR-20 / Task 034): when `true`, the component forwards
  // `{ force: true }` to the host adapter so the BFF call bypasses cache
  // (`/api/insights/ask?force=true`). The initial on-open-fetch and the
  // existing stale/error "Refresh" affordance both call with `force = false`
  // (cache honoured per FR-21 TTL); the manual refresh button in the
  // Popover footer calls with `force = true` (FR-20 binding).
  const runFetch = useCallback(
    (force: boolean = false) => {
      if (!onFetchInsight) {
        return;
      }
      dispatch({ type: 'BEGIN_FETCH' });
      void onFetchInsight(force ? { force: true } : undefined)
        .then(envelope => {
          dispatch({ type: 'FETCH_SUCCESS', data: envelope });
        })
        .catch((err: unknown) => {
          if (isDeclineError(err)) {
            const declineAction: { type: 'FETCH_DECLINE'; message: string; recommendedAction?: string } = {
              type: 'FETCH_DECLINE',
              message: err.message || DEFAULT_DECLINE_MESSAGE,
            };
            if (err.recommendedAction !== undefined) {
              declineAction.recommendedAction = err.recommendedAction;
            }
            dispatch(declineAction);
            return;
          }
          const message = err instanceof Error && err.message ? err.message : DEFAULT_ERROR_MESSAGE;
          dispatch({ type: 'FETCH_ERROR', message });
        });
    },
    [onFetchInsight]
  );

  // ── Popover open handler — fetches on first open per FR-02 inline expand.
  // Initial open uses `force = false` so the cache-stack TTL (FR-21) is
  // honoured. Manual refresh (below) is the only path that bypasses cache.
  const handlePopoverOpenChange = useCallback(
    (_ev: unknown, openData: { open: boolean }) => {
      if (openData.open && state.status === 'idle') {
        runFetch(false);
      }
    },
    [runFetch, state.status]
  );

  // ── Modal expand affordance — opens Dialog from Popover footer (FR-02).
  const handleExpandClick = useCallback(() => {
    setDialogOpen(true);
  }, []);

  const handleDialogOpenChange = useCallback((_ev: unknown, openData: { open: boolean }) => {
    setDialogOpen(openData.open);
  }, []);

  // ── Manual refresh affordance (FR-20 / Task 034).
  //
  // Always present in the Popover footer (next to "Expand to modal") once
  // the card has reached a terminal state (loaded / stale / error /
  // decline). Click → forwards `{ force: true }` to the host so the BFF
  // bypasses cache → state transitions to `loading` → existing
  // `InsightBody` `case 'loading'` renders the inline spinner per FR-20
  // ("blocking-style invocation; spinner shown during invocation"). On
  // success the card re-renders with the new envelope; failures fall to
  // the `error` / `decline` states via the existing reducer arms.
  //
  // Distinct from FR-18 background pre-warm (which is fire-and-forget and
  // does NOT update card state).
  const handleRefreshClick = useCallback(() => {
    runFetch(true);
  }, [runFetch]);

  const triggerText = triggerLabel || 'View Insight';
  const expandEnabled = state.status === 'loaded' || state.status === 'stale';
  // FR-20: refresh button visible in any terminal state (post-fetch). Hidden
  // during `idle` (nothing to refresh yet — the on-open gate handles that)
  // and during `loading` (already fetching — the spinner is the affordance).
  const refreshVisible =
    state.status === 'loaded' || state.status === 'stale' || state.status === 'error' || state.status === 'decline';

  return (
    <Card
      className={mergeClasses(styles.root, className)}
      data-testid="insight-summary-card"
      data-topic={topic}
      data-subject={subject}
      data-mode={mode}
      data-state={state.status}
    >
      {/* ── Header: topic label + KPI slot + Sparkle trigger Popover ───────── */}
      <CardHeader
        header={
          <div className={styles.headerRow}>
            <Text className={styles.headerLabel}>
              <Sparkle20Regular aria-hidden="true" />
              {topicLabel}
            </Text>
            {kpiSlot && <div className={styles.kpiSlot}>{kpiSlot}</div>}
          </div>
        }
      />

      {/* ── Body: lightweight idle/loaded summary line + trigger Popover ───── */}
      <div className={styles.body}>
        <Popover positioning="below" withArrow onOpenChange={handlePopoverOpenChange}>
          <PopoverTrigger disableButtonEnhancement>
            <Button appearance="primary" icon={<Sparkle20Regular />} data-testid="insight-summary-card-trigger">
              {triggerText}
            </Button>
          </PopoverTrigger>
          <PopoverSurface>
            {/*
              PORTAL-GOTCHA (binding per
              `.claude/patterns/ui/fluent-v9-portal-gotcha.md`):
              Popover renders via React Portal. Without this re-wrap, the
              content escapes the outer FluentProvider DOM subtree and the
              active theme (esp. dark mode) does NOT propagate.
            */}
            <FluentProvider theme={portalTheme} className={styles.popoverSurface}>
              <div className={styles.headerRow}>
                <Text className={styles.headerLabel}>
                  <Sparkle20Regular aria-hidden="true" />
                  {topicLabel}
                </Text>
              </div>
              <InsightBody state={state} styles={styles} onCitationClick={onCitationClick} />
              <div className={styles.popoverFooter}>
                {refreshVisible && (
                  <Button
                    appearance="subtle"
                    size="small"
                    icon={<ArrowSync20Regular />}
                    onClick={handleRefreshClick}
                    data-testid="insight-summary-card-refresh"
                    aria-label="Refresh insight"
                  >
                    Refresh
                  </Button>
                )}
                {expandEnabled && (
                  <Button
                    appearance="secondary"
                    size="small"
                    onClick={handleExpandClick}
                    data-testid="insight-summary-card-expand"
                  >
                    Expand to modal
                  </Button>
                )}
              </div>
            </FluentProvider>
          </PopoverSurface>
        </Popover>
      </div>

      {/* ── Footer: topic / subject / mode echo (debug + Storybook-equiv) ──── */}
      <div className={styles.footerRow}>
        <div className={styles.footerCell}>
          <Text className={styles.footerKey}>Topic:</Text>
          <Text className={styles.footerValue}>{topic}</Text>
        </div>
        <div className={styles.footerCell}>
          <Text className={styles.footerKey}>Subject:</Text>
          <Text className={styles.footerValue}>{subject}</Text>
        </div>
        <div className={styles.footerCell}>
          <Text className={styles.footerKey}>Mode:</Text>
          <Text className={styles.footerValue}>{mode}</Text>
        </div>
      </div>

      {/* ── Modal expand Dialog ──────────────────────────────────────────────── */}
      <Dialog open={dialogOpen} onOpenChange={handleDialogOpenChange}>
        <DialogSurface>
          {/*
            PORTAL-GOTCHA (binding per
            `.claude/patterns/ui/fluent-v9-portal-gotcha.md`):
            Dialog renders via React Portal — same rule as Popover above.
            Without this re-wrap, dark mode is the most common regression
            (Dialog renders with light background in dark mode).
          */}
          <FluentProvider theme={portalTheme}>
            <DialogBody>
              <DialogTitle>{topicLabel}</DialogTitle>
              <DialogContent className={styles.dialogBody}>
                <InsightBody state={state} styles={styles} onCitationClick={onCitationClick} />
              </DialogContent>
              <DialogActions>
                {refreshVisible && (
                  <Button
                    appearance="subtle"
                    icon={<ArrowSync20Regular />}
                    onClick={handleRefreshClick}
                    aria-label="Refresh insight"
                  >
                    Refresh
                  </Button>
                )}
                <Button appearance="primary" onClick={() => setDialogOpen(false)}>
                  Close
                </Button>
              </DialogActions>
            </DialogBody>
          </FluentProvider>
        </DialogSurface>
      </Dialog>
    </Card>
  );
};

export default InsightSummaryCard;
