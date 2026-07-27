/**
 * NdaReviewProgressModal — center-screen live-progress popup for a running NDA review
 * (ai-advanced-capabilities-nda-r1, UAT round-5 #9).
 *
 * The reviewer asked for a clear, center-of-screen indication of what the review is doing while it runs
 * (the old signal was just a tiny "Working…" chip in the Assistant). This renders the shared
 * {@link AiProgressStepper} (`variant="card"`) inside a full-viewport portal so it is truly centered,
 * with a dimmed backdrop, driven by {@link useNdaReviewRunProgress}.
 *
 * SYNTHESIZED STAGES (honest by design): the NDA-review dispatch path emits NO per-stage events — the
 * BFF runs the Action as one awaited call and returns a single terminal result (round-5 #9
 * investigation). So the intermediate steps here are the REAL phases the review performs (read →
 * retrieve standards → analyze → write notes) advanced on a gentle timer, but the stepper NEVER shows
 * "done" on its own: it advances to and HOLDS on "Analyzing clauses" (the long model call) until the
 * real result arrives, then marks every step complete. That avoids the classic "bar sits at 100% while
 * still working" lie — the terminal state is always driven by the actual dispatch outcome.
 *
 * @see ./useNdaReviewRunProgress.ts — the state machine (begin/complete/fail/close)
 * @see ../../../../client/shared/Spaarke.UI.Components/src/components/AiProgressStepper — the shared stepper
 */
import * as React from 'react';
import { createPortal } from 'react-dom';
import { makeStyles, tokens } from '@fluentui/react-components';
import { AiProgressStepper, type AiProgressStep } from '@spaarke/ui-components';
import type { NdaRunStatus } from './useNdaReviewRunProgress';

/** The real phases an NDA review performs, in order. `analyzing` is the hold point (the long call). */
export const NDA_REVIEW_PROGRESS_STEPS: AiProgressStep[] = [
  { id: 'reading', label: 'Reading the document', description: 'Loading and extracting the NDA text…' },
  { id: 'retrieving', label: 'Retrieving firm standards', description: 'Pulling the matching standard clauses…' },
  { id: 'analyzing', label: 'Analyzing clauses', description: 'Comparing each clause against the standard…' },
  { id: 'writing', label: 'Writing advisory notes', description: 'Drafting the findings, comments, and summary…' },
];

/** Index the synthesized progression advances to and HOLDS on until the real result arrives. */
const HOLD_INDEX = 2; // 'analyzing'
/** Cadence for advancing the synthesized pre-hold steps. */
const STEP_ADVANCE_MS = 1600;
/** How long the terminal (complete/error) state lingers before the modal auto-dismisses. */
const COMPLETE_LINGER_MS = 900;
const ERROR_LINGER_MS = 3200;

const useStyles = makeStyles({
  // A full-viewport positioned ancestor so AiProgressStepper's `card` backdrop (absolute inset:0)
  // fills the screen and centers the card in the middle of the screen (not within the chat pane).
  viewport: {
    position: 'fixed',
    inset: 0,
    zIndex: 10000,
    // The stepper's own backdrop provides the dim; this layer only establishes the positioning context
    // and captures pointer events so the document behind can't be interacted with mid-run.
    backgroundColor: tokens.colorTransparentBackground,
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

  // While running, advance through the pre-hold steps on a timer, then HOLD on `analyzing`.
  React.useEffect(() => {
    if (status !== 'running') return;
    setActiveIdx(0);
    const id = setInterval(() => {
      setActiveIdx(prev => Math.min(prev + 1, HOLD_INDEX));
    }, STEP_ADVANCE_MS);
    return () => clearInterval(id);
  }, [status]);

  // On a terminal state, linger briefly so the reviewer sees the outcome, then dismiss.
  React.useEffect(() => {
    if (status !== 'complete' && status !== 'error') return;
    const id = setTimeout(onClose, status === 'complete' ? COMPLETE_LINGER_MS : ERROR_LINGER_MS);
    return () => clearTimeout(id);
  }, [status, onClose]);

  if (status === 'idle') return null;
  if (typeof document === 'undefined') return null; // SSR / non-DOM guard (never hit in the code page)

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

  return createPortal(
    <div className={styles.viewport} role="presentation" data-testid="nda-review-progress-modal">
      <AiProgressStepper
        variant="card"
        title={title}
        steps={steps}
        activeStepId={activeStepId}
        completedStepIds={completedStepIds}
        errorStepId={errorStepId}
        isStreaming={status === 'running'}
      />
    </div>,
    document.body
  );
}

NdaReviewProgressModal.displayName = 'NdaReviewProgressModal';

export default NdaReviewProgressModal;
