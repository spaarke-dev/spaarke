/**
 * useNdaReviewRunProgress — drives the NDA-review progress modal (ai-advanced-capabilities-nda-r1,
 * UAT round-5 #9).
 *
 * The NDA-review dispatch path streams no per-stage progress (the BFF runs the Action as a single
 * awaited call and yields only a terminal `complete` chunk — see the round-5 #9 investigation). So this
 * tiny state machine is driven by the THREE real client-observable transitions and nothing more:
 *   - `begin()`  — the review dispatch STARTED (ConversationPane calls it when the NDA binding is the
 *                  one being dispatched, from the "Review an NDA" card OR its chip).
 *   - `complete()` — the terminal result arrived and is NDA-shaped (`onDispatchResult` +
 *                  `isNdaReviewResult`). No-ops unless a run is in flight.
 *   - `fail()`   — the dispatch settled WITHOUT an NDA result (error / empty). No-ops unless a run is
 *                  still `running` (a `complete()` that already fired wins the race, since
 *                  `onDispatchResult` runs before the dispatch's `.finally` clears `dispatching`).
 *
 * The modal component ({@link ./NdaReviewProgressModal}) renders whenever `status !== 'idle'` and calls
 * `close()` to return to idle after it has shown the terminal (complete/error) state briefly.
 *
 * @see ./NdaReviewProgressModal.tsx — the center-screen AiProgressStepper modal this drives
 * @see ./ConversationPane.tsx — wiring (begin on dispatch-start, complete on NDA result, fail on settle)
 */
import * as React from 'react';

export type NdaRunStatus = 'idle' | 'running' | 'complete' | 'error';

export interface NdaReviewRunProgress {
  /** Current run status. The modal renders whenever this is not `idle`. */
  status: NdaRunStatus;
  /** Mark a review run as started (opens the modal on the first step). */
  begin: () => void;
  /** Mark the run complete (all steps done) — no-op unless a run is in flight. */
  complete: () => void;
  /** Mark the run failed — no-op unless the run is still `running` (a prior `complete` wins). */
  fail: () => void;
  /** Return to idle (the modal calls this after showing the terminal state). */
  close: () => void;
}

export function useNdaReviewRunProgress(): NdaReviewRunProgress {
  const [status, setStatus] = React.useState<NdaRunStatus>('idle');
  // A ref mirror so the transition guards read the CURRENT status synchronously (the settle-effect and
  // the terminal callbacks can fire in the same tick as a state update).
  const statusRef = React.useRef<NdaRunStatus>('idle');
  const set = React.useCallback((next: NdaRunStatus): void => {
    statusRef.current = next;
    setStatus(next);
  }, []);

  const begin = React.useCallback((): void => set('running'), [set]);
  const complete = React.useCallback((): void => {
    if (statusRef.current === 'running') set('complete');
  }, [set]);
  const fail = React.useCallback((): void => {
    if (statusRef.current === 'running') set('error');
  }, [set]);
  const close = React.useCallback((): void => set('idle'), [set]);

  return { status, begin, complete, fail, close };
}

export default useNdaReviewRunProgress;
