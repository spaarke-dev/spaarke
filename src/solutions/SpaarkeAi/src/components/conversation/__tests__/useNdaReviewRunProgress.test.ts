/**
 * useNdaReviewRunProgress tests — UAT round-5 #9 progress-modal state machine.
 *
 * Verifies the three real transitions + the race guard: `complete`/`fail` only act while a run is in
 * flight, and a `complete` that already fired wins a later `fail` (the success-before-settle ordering).
 */
import { renderHook, act } from '@testing-library/react';
import { useNdaReviewRunProgress } from '../useNdaReviewRunProgress';

describe('useNdaReviewRunProgress', () => {
  it('starts idle', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    expect(result.current.status).toBe('idle');
  });

  it('begin → running, complete → complete, close → idle', () => {
    const { result } = renderHook(() => useNdaReviewRunProgress());
    act(() => result.current.begin());
    expect(result.current.status).toBe('running');
    act(() => result.current.complete());
    expect(result.current.status).toBe('complete');
    act(() => result.current.close());
    expect(result.current.status).toBe('idle');
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
