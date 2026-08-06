/**
 * useNdaReviewRunProgress tests — UAT round-5 #9 progress-modal state machine; extended UAT round-3
 * item #8 with the non-blocking `visible`/`dismiss` behavior.
 *
 * Verifies the three real transitions + the race guard: `complete`/`fail` only act while a run is in
 * flight, and a `complete` that already fired wins a later `fail` (the success-before-settle ordering).
 */
import { renderHook, act } from '@testing-library/react';
import { useNdaReviewRunProgress } from '../useNdaReviewRunProgress';

describe('useNdaReviewRunProgress', () => {
  it('starts idle, not visible', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    expect(result.current.status).toBe('idle');
    expect(result.current.visible).toBe(false);
  });

  it('begin → running, complete → complete, close → idle (visible tracks status while not dismissed)', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.begin());
    expect(result.current.status).toBe('running');
    expect(result.current.visible).toBe(true);
    act(() => result.current.complete());
    expect(result.current.status).toBe('complete');
    expect(result.current.visible).toBe(true);
    act(() => result.current.close());
    expect(result.current.status).toBe('idle');
    expect(result.current.visible).toBe(false);
  });

  it('begin → running, fail → error', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.begin());
    act(() => result.current.fail());
    expect(result.current.status).toBe('error');
  });

  it('complete is a no-op when no run is in flight', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.complete());
    expect(result.current.status).toBe('idle');
  });

  it('fail is a no-op after complete already won the race', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.begin());
    act(() => result.current.complete()); // success arrives before the dispatch settles
    act(() => result.current.fail()); // settle fires fail — must NOT override the completed state
    expect(result.current.status).toBe('complete');
  });

  it('fail is a no-op when idle (e.g. a non-NDA dispatch settling)', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.fail());
    expect(result.current.status).toBe('idle');
  });
});

describe('useNdaReviewRunProgress — UAT round-3 item #8 (non-blocking dismiss)', () => {
  it('dismiss() hides the modal (visible=false) WITHOUT changing status — the review keeps running', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.begin());
    expect(result.current.status).toBe('running');
    act(() => result.current.dismiss());
    expect(result.current.status).toBe('running'); // unchanged — dismiss never touches status
    expect(result.current.visible).toBe(false);
  });

  it('a dismissed run stays hidden through complete() — it does NOT pop back up', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.begin());
    act(() => result.current.dismiss());
    expect(result.current.visible).toBe(false);
    act(() => result.current.complete());
    expect(result.current.status).toBe('complete');
    expect(result.current.visible).toBe(false);
  });

  it('a dismissed run stays hidden through fail() — it does NOT pop back up', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.begin());
    act(() => result.current.dismiss());
    act(() => result.current.fail());
    expect(result.current.status).toBe('error');
    expect(result.current.visible).toBe(false);
  });

  it('a fresh begin() ALWAYS starts visible, even if the PRIOR run was dismissed', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.begin());
    act(() => result.current.dismiss());
    act(() => result.current.close()); // prior run settles to idle
    expect(result.current.visible).toBe(false);

    act(() => result.current.begin()); // a SECOND, fresh run
    expect(result.current.status).toBe('running');
    expect(result.current.visible).toBe(true);
  });

  it('dismiss() while idle is a harmless no-op (visible stays false)', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.dismiss());
    expect(result.current.status).toBe('idle');
    expect(result.current.visible).toBe(false);
  });
});
