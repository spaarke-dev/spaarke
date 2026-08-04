/**
 * useNdaReviewRunProgress — UAT round-4 item #10a (background-run PaneEventBus signal).
 *
 * After "Continue working in background" the run's liveness moves to the WORKSPACE tab strip. The hook
 * broadcasts `nda_review_background_run { backgroundRunActive }` on the workspace channel so WorkspacePane
 * (a sibling pane) can show/clear the tiny Compose-tab spinner. This suite pins the emit contract:
 *
 *   - false on mount (idle) and while the modal is VISIBLE (running, not dismissed);
 *   - true exactly when a running review is dismissed (liveness → tab);
 *   - false again when the backgrounded run completes or fails (spinner clears).
 *
 * @see ../useNdaReviewRunProgress.ts — emitter
 * @see ../../workspace/WorkspacePane.tsx — consumer (tracks the flag)
 * @see ../../workspace/WorkspaceTabManagerComponent.tsx — renders the tab spinner
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { renderHook, act } from '@testing-library/react';
import { PaneEventBus, PaneEventBusProvider } from '@spaarke/ai-widgets';
import type { WorkspacePaneEvent } from '@spaarke/ai-widgets';
import { useNdaReviewRunProgress } from '../useNdaReviewRunProgress';

function setup() {
  const bus = new PaneEventBus();
  const events: WorkspacePaneEvent[] = [];
  bus.subscribe('workspace', (e) => events.push(e));
  const wrapper = ({ children }: { children: React.ReactNode }): React.JSX.Element => (
    <PaneEventBusProvider bus={bus}>{children}</PaneEventBusProvider>
  );
  const { result } = renderHook(() => useNdaReviewRunProgress(), { wrapper });
  return { result, events };
}

/** The last `nda_review_background_run` payload broadcast, or undefined if none. */
function lastBackgroundRun(events: WorkspacePaneEvent[]): WorkspacePaneEvent | undefined {
  const bg = events.filter((e) => e.type === 'nda_review_background_run');
  return bg[bg.length - 1];
}

describe('useNdaReviewRunProgress — UAT round-4 item #10a (background-run signal)', () => {
  it('emits backgroundRunActive=false on mount (idle — nothing running)', () => {
    const { events } = setup();
    expect(lastBackgroundRun(events)?.backgroundRunActive).toBe(false);
  });

  it('stays false while the progress modal is VISIBLE (running, not dismissed)', () => {
    const { result, events } = setup();
    act(() => result.current.begin());
    // The modal is on screen; the tab spinner must NOT show yet (no double indicator).
    expect(lastBackgroundRun(events)?.backgroundRunActive).toBe(false);
  });

  it('emits true when a running review is dismissed — liveness moves to the tab', () => {
    const { result, events } = setup();
    act(() => result.current.begin());
    act(() => result.current.dismiss());
    expect(lastBackgroundRun(events)?.backgroundRunActive).toBe(true);
  });

  it('emits false again when the backgrounded run completes (tab spinner clears)', () => {
    const { result, events } = setup();
    act(() => result.current.begin());
    act(() => result.current.dismiss());
    expect(lastBackgroundRun(events)?.backgroundRunActive).toBe(true);
    act(() => result.current.complete());
    expect(lastBackgroundRun(events)?.backgroundRunActive).toBe(false);
  });

  it('emits false when a backgrounded run fails (tab spinner clears)', () => {
    const { result, events } = setup();
    act(() => result.current.begin());
    act(() => result.current.dismiss());
    act(() => result.current.fail());
    expect(lastBackgroundRun(events)?.backgroundRunActive).toBe(false);
  });
});
