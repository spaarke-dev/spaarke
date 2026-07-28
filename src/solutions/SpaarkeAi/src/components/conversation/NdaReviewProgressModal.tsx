/**
 * NdaReviewProgressModal — center-screen live-progress popup for a running NDA review
 * (ai-advanced-capabilities-nda-r1, UAT round-5 #9; UAT round-6 #6/#7 polish).
 *
 * The reviewer asked for a clear, center-of-screen indication of what the review is doing while it runs
 * (the old signal was just a tiny "Working…" chip). This renders the shared {@link AiProgressStepper}
 * inside a Fluent v9 {@link Dialog} so it comes with a proper elevated surface + scrim + true screen
 * centering (round-6 #6 — "needs a background color and elevation effect"), plus a rotating, legally-
 * flavoured "working…" status line (round-6 #7 — "like what Claude Code does but with legal words")
 * because the run can take a while.
 *
 * SYNTHESIZED STAGES (honest by design): the NDA-review dispatch path emits NO per-stage events — the
 * BFF runs the Action as one awaited call and returns a single terminal result (round-5 #9
 * investigation). So the intermediate steps are the REAL phases the review performs advanced on a
 * gentle timer, but the stepper NEVER shows "done" on its own: it advances to and HOLDS on "Analyzing
 * clauses" (the long model call) until the real result arrives, then marks every step complete. That
 * avoids the classic "bar sits at 100% while still working" lie — the terminal state is always driven by
 * the actual dispatch outcome.
 *
 * @see ./useNdaReviewRunProgress.ts — the state machine (begin/complete/fail/close)
 * @see ../../../../client/shared/Spaarke.UI.Components/src/components/AiProgressStepper — the shared stepper
 */
import * as React from 'react';
import {
  Dialog,
  DialogSurface,
  DialogBody,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { CheckmarkCircle20Filled, Circle20Regular, ErrorCircle20Filled } from '@fluentui/react-icons';
import type { AiProgressStep } from '@spaarke/ui-components';
import type { NdaRunStatus } from './useNdaReviewRunProgress';

/** The real phases an NDA review performs, in order. `analyzing` is the hold point (the long call). */
export const NDA_REVIEW_PROGRESS_STEPS: AiProgressStep[] = [
  { id: 'reading', label: 'Reading the document', description: 'Loading and extracting the NDA text…' },
  { id: 'retrieving', label: 'Retrieving firm standards', description: 'Pulling the matching standard clauses…' },
  { id: 'analyzing', label: 'Analyzing clauses', description: 'Comparing each clause against the standard…' },
  { id: 'writing', label: 'Writing advisory notes', description: 'Drafting the findings, comments, and summary…' },
];

/**
 * UAT round-6 #7 — a rotating, legally-flavoured "working…" line (Claude-Code-style) so the reviewer
 * sees continuous activity during the long model call. Deliberately professional-but-lively.
 */
export const NDA_REVIEW_WORKING_PHRASES: string[] = [
  'Poring over the fine print…',
  'Cross-referencing the firm playbook…',
  'Weighing the risk allocations…',
  'Checking definitions and carve-outs…',
  'Scrutinizing the confidentiality term…',
  'Hunting for open-ended obligations…',
  'Comparing against the standard…',
  'Reading between the clauses…',
  'Measuring twice, flagging once…',
  'Assessing indemnities and remedies…',
  'Consulting precedent…',
  'Drafting the advisory notes…',
];

/** Index the synthesized progression advances to and HOLDS on until the real result arrives. */
const HOLD_INDEX = 2; // 'analyzing'
/** Cadence for advancing the synthesized pre-hold steps. */
const STEP_ADVANCE_MS = 1600;
/** Cadence for rotating the "working…" phrase. */
const PHRASE_ROTATE_MS = 2200;
/** How long the terminal (complete/error) state lingers before the modal auto-dismisses. */
const COMPLETE_LINGER_MS = 900;
const ERROR_LINGER_MS = 3200;

const useStyles = makeStyles({
  // UAT follow-up: the flow diagram is now VERTICAL (one step per row) so it ALWAYS fits the popup
  // width regardless of label length — the prior horizontal track's `flex-shrink:0` chips overflowed
  // the surface. A vertical list also lets the active step carry its OWN spinner (see stepRow), so the
  // reviewer sees exactly which step is being worked on. The surface can therefore be comfortably narrow.
  surface: {
    width: '440px',
    maxWidth: '92vw',
  },
  body: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    textAlign: 'center',
    rowGap: tokens.spacingVerticalL,
    width: '100%',
  },
  // UAT follow-up: the rotating "working…" phrase now trails the title AFTER the ellipsis (italic),
  // e.g. "Reviewing your NDA… Scrutinizing the confidentiality term…" — instead of a separate line.
  title: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  titlePhrase: {
    fontWeight: tokens.fontWeightRegular,
    color: tokens.colorNeutralForeground2,
  },
  // Vertical step list — left-aligned within the centered body, capped so it reads as a tidy column.
  steps: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalM,
    width: '100%',
    maxWidth: '340px',
    textAlign: 'left',
  },
  stepRow: {
    display: 'flex',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
  },
  // Fixed-size leading slot so the label column stays aligned across checkmark / spinner / circle.
  stepIcon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '20px',
    height: '20px',
    flexShrink: 0,
  },
  stepLabel: {
    color: tokens.colorNeutralForeground3,
  },
  stepLabelActive: {
    color: tokens.colorNeutralForeground1,
    fontWeight: tokens.fontWeightSemibold,
  },
  stepLabelDone: {
    color: tokens.colorNeutralForeground2,
  },
  iconDone: {
    color: tokens.colorStatusSuccessForeground1,
  },
  iconPending: {
    color: tokens.colorNeutralForeground4,
  },
  iconError: {
    color: tokens.colorStatusDangerForeground1,
  },
});

export interface NdaReviewProgressModalProps {
  /** Current run status. The modal renders whenever this is not `idle`. */
  status: NdaRunStatus;
  /** Called to return to idle after the terminal state has been shown. */
  onClose: () => void;
}

export function NdaReviewProgressModal(props: NdaReviewProgressModalProps): React.JSX.Element | null {
  const { status, onClose } = props;
  const styles = useStyles();
  const [activeIdx, setActiveIdx] = React.useState(0);
  const [phraseIdx, setPhraseIdx] = React.useState(0);

  // While running, advance through the pre-hold steps on a timer, then HOLD on `analyzing`.
  React.useEffect(() => {
    if (status !== 'running') return;
    setActiveIdx(0);
    const id = setInterval(() => {
      setActiveIdx(prev => Math.min(prev + 1, HOLD_INDEX));
    }, STEP_ADVANCE_MS);
    return () => clearInterval(id);
  }, [status]);

  // Rotate the "working…" phrase while running (round-6 #7).
  React.useEffect(() => {
    if (status !== 'running') return;
    const id = setInterval(() => {
      setPhraseIdx(prev => (prev + 1) % NDA_REVIEW_WORKING_PHRASES.length);
    }, PHRASE_ROTATE_MS);
    return () => clearInterval(id);
  }, [status]);

  // On a terminal state, linger briefly so the reviewer sees the outcome, then dismiss.
  React.useEffect(() => {
    if (status !== 'complete' && status !== 'error') return;
    const id = setTimeout(onClose, status === 'complete' ? COMPLETE_LINGER_MS : ERROR_LINGER_MS);
    return () => clearTimeout(id);
  }, [status, onClose]);

  if (status === 'idle') return null;

  const steps = NDA_REVIEW_PROGRESS_STEPS;
  let activeStepId: string | null = null;
  let completedStepIds: string[] = [];
  let errorStepId: string | null = null;
  let title = 'Reviewing your NDA…';

  if (status === 'running') {
    activeStepId = steps[activeIdx].id;
    completedStepIds = steps.slice(0, activeIdx).map(s => s.id);
  } else if (status === 'complete') {
    completedStepIds = steps.map(s => s.id);
    title = 'Review complete';
  } else {
    // error — the step we were on is where it stopped; earlier steps stay completed.
    errorStepId = steps[activeIdx].id;
    completedStepIds = steps.slice(0, activeIdx).map(s => s.id);
    title = 'The review couldn’t finish';
  }

  return (
    // `modalType="alert"` = no light-dismiss (no backdrop/Escape close) — a progress modal auto-dismisses
    // on the real outcome. DialogSurface brings the elevated background + the Dialog brings the scrim
    // (round-6 #6). `open` is always true here; the parent conditionally renders this component.
    <Dialog open modalType="alert">
      <DialogSurface className={styles.surface} data-testid="nda-review-progress-modal">
        <DialogBody>
          <div className={styles.body}>
            {/* Title, with the rotating "working…" phrase trailing the ellipsis (italic) while running. */}
            <Text size={400} className={styles.title}>
              <span>{title}</span>
              {status === 'running' ? (
                <Text
                  as="span"
                  size={400}
                  italic
                  className={styles.titlePhrase}
                  aria-live="polite"
                  data-testid="nda-review-progress-working"
                >
                  {' '}
                  {NDA_REVIEW_WORKING_PHRASES[phraseIdx]}
                </Text>
              ) : null}
            </Text>

            {/* VERTICAL step list — fits any width; the ACTIVE step carries the spinner so it's clear
                which step is being worked on (completed = check, pending = hollow circle, error = ✕). */}
            <div className={styles.steps} role="status" aria-live="polite">
              {steps.map(step => {
                const isCompleted = completedStepIds.includes(step.id);
                const isActive = step.id === activeStepId;
                const isError = step.id === errorStepId;
                return (
                  <div key={step.id} className={styles.stepRow} data-testid={`nda-review-step-${step.id}`}>
                    <span className={styles.stepIcon}>
                      {isError ? (
                        <ErrorCircle20Filled className={styles.iconError} data-testid="nda-review-step-icon-error" />
                      ) : isCompleted ? (
                        <CheckmarkCircle20Filled className={styles.iconDone} />
                      ) : isActive ? (
                        <Spinner size="tiny" data-testid="nda-review-step-active-spinner" />
                      ) : (
                        <Circle20Regular className={styles.iconPending} />
                      )}
                    </span>
                    <Text
                      size={300}
                      className={
                        isActive ? styles.stepLabelActive : isCompleted ? styles.stepLabelDone : styles.stepLabel
                      }
                    >
                      {step.label}
                    </Text>
                  </div>
                );
              })}
            </div>
          </div>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

NdaReviewProgressModal.displayName = 'NdaReviewProgressModal';

export default NdaReviewProgressModal;
