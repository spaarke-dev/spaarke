/**
 * useNdaReviewRunProgress — drives the NDA-review progress modal (ai-advanced-capabilities-nda-r1,
 * UAT round-5 #9; extended UAT round-3 item #8 with a non-blocking "dismiss" affordance).
 *
 * The NDA-review dispatch path streams no per-stage progress (the BFF runs the Action as a single
 * awaited call and yields only a terminal `complete` chunk — see the round-5 #9 investigation). So this
 * tiny state machine is driven by the THREE real client-observable transitions and nothing more:
 *   - `begin()`  — the review dispatch STARTED (ConversationPane calls it when the NDA binding is the
 *                  one being dispatched, from the "Review an NDA" card OR its chip). Also clears any
 *                  prior `dismissed` flag - a fresh run always starts visible.
 *   - `complete()` — the terminal result arrived and is NDA-shaped (`onDispatchResult` +
 *                  `isNdaReviewResult`). No-ops unless a run is in flight.
 *   - `fail()`   — the dispatch settled WITHOUT an NDA result (error / empty). No-ops unless a run is
 *                  still `running` (a `complete()` that already fired wins the race, since
 *                  `onDispatchResult` runs before the dispatch's `.finally` clears `dispatching`).
 *
 * UAT round-3 (item #8): the progress modal used to be FULLY BLOCKING (`modalType="alert"`), holding
 * the whole app hostage for the ~20s-2min review. `dismiss()` is the new "Continue working in
 * background" affordance - it hides the modal WITHOUT touching `status` (the review keeps running
 * server-side either way; dismissing is purely a client-side visibility choice, never a cancel). Once
 * dismissed, the modal stays hidden for the REST of that run (it does not pop back up on
 * complete/error) - the 071 `ReviewCompleteToast` covers the notify-me case for a dismissed run that
 * finishes while the user is on a different workspace tab.
 *
 * The modal component ({@link ./NdaReviewProgressModal}) renders whenever `visible` is true and calls
 * `close()` to return to idle after it has shown the terminal (complete/error) state briefly, or
 * `dismiss()` when the user explicitly hides it mid-run.
 *
 * @see ./NdaReviewProgressModal.tsx — the center-screen AiProgressStepper modal this drives
 * @see ./ConversationPane.tsx — wiring (begin on dispatch-start, complete on NDA result, fail on settle;
 *      also broadcasts `visible` on the PaneEventBus so `ReviewCompleteToast` can avoid a
 *      double-notification while the modal is still showing)
 */
import * as React from 'react';
import { useOptionalDispatchPaneEvent } from '@spaarke/ai-widgets';

export type NdaRunStatus = 'idle' | 'running' | 'complete' | 'error';

export interface NdaReviewRunProgress {
  /** Current run status. */
  status: NdaRunStatus;
  /**
   * UAT round-3 (item #8): true while the modal should be shown - `status !== 'idle'` AND the user has
   * NOT dismissed it. The modal component renders whenever this is true and renders nothing otherwise
   * (idle, or dismissed-but-still-running).
   */
  visible: boolean;
  /** Mark a review run as started (opens the modal on the first step; clears any prior dismissal). */
  begin: () => void;
  /** Mark the run complete (all steps done) — no-op unless a run is in flight. */
  complete: () => void;
  /** Mark the run failed — no-op unless the run is still `running` (a prior `complete` wins). */
  fail: () => void;
  /** Return to idle (the modal calls this after showing the terminal state). */
  close: () => void;
  /**
   * UAT round-3 (item #8) - "Continue working in background": hide the modal while the run
   * continues. Does NOT change `status` and does NOT stop the review (it is already dispatched
   * server-side) - purely a visibility toggle for THIS run, cleared on the next `begin()`.
   */
  dismiss: () => void;
}

export function useNdaReviewRunProgress(): NdaReviewRunProgress {
  const [status, setStatus] = React.useState<NdaRunStatus>('idle');
  // UAT round-3 (item #8): explicit user dismissal, independent of `status` - the review keeps running
  // (or finishes) either way; this only controls whether the modal is currently shown.
  const [dismissed, setDismissed] = React.useState(false);
  // A ref mirror so the transition guards read the CURRENT status synchronously (the settle-effect and
  // the terminal callbacks can fire in the same tick as a state update).
  const statusRef = React.useRef<NdaRunStatus>('idle');
  const set = React.useCallback((next: NdaRunStatus): void => {
    statusRef.current = next;
    setStatus(next);
  }, []);

  const begin = React.useCallback((): void => {
    // A fresh run always starts visible, even if the PRIOR run was dismissed.
    setDismissed(false);
    set('running');
  }, [set]);
  const complete = React.useCallback((): void => {
    if (statusRef.current === 'running') set('complete');
  }, [set]);
  const fail = React.useCallback((): void => {
    if (statusRef.current === 'running') set('error');
  }, [set]);
  const close = React.useCallback((): void => set('idle'), [set]);
  const dismiss = React.useCallback((): void => setDismissed(true), []);

  const visible = status !== 'idle' && !dismissed;

  // UAT round-4 (item #10a): after "Continue working in background", the run's liveness moves to the
  // WORKSPACE tab strip. `backgroundRunActive` is true exactly while a run is STILL executing but its
  // modal has been dismissed - the state the tab spinner represents. We broadcast it on the PaneEventBus
  // (workspace channel, additive discriminant per ADR-030, mirroring the item-#8
  // `nda_review_progress_visibility` precedent) so WorkspacePane - which owns the tab strip and lives in
  // a sibling pane - can render the indicator. `useOptionalDispatchPaneEvent` no-ops when this hook is
  // rendered without a PaneEventBusProvider (e.g. its own unit test), so the hook stays bus-optional.
  const backgroundRunActive = status === 'running' && dismissed;
  const dispatch = useOptionalDispatchPaneEvent();
  React.useEffect(() => {
    dispatch('workspace', { type: 'nda_review_background_run', backgroundRunActive });
  }, [backgroundRunActive, dispatch]);

  return { status, visible, begin, complete, fail, close, dismiss };
}

export default useNdaReviewRunProgress;
