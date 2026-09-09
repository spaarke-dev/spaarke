/**
 * Unit tests for useAnnounce hook
 *
 * Tests the screen reader announcement hook for accessibility.
 *
 * ## Harness note (task 018 / NFR-11)
 *
 * `useAnnounce`'s live regions are now rendered React JSX (`liveRegion`)
 * that the CALLING component must include in its own tree -- the hook can
 * no longer attach anything to `document.body` on its own (that was the
 * production defect this task fixes: nodes created/removed entirely
 * outside React's tracked tree collided with React 19's own unmount
 * bookkeeping). `renderHook` alone can't exercise that contract because
 * nothing renders the hook's return value, so these tests use small
 * harness components (`AnnounceHarness` / `AnnounceOnChangeHarness`) that
 * play the role of "the calling component" -- they call the hook, return
 * `liveRegion` so RTL mounts it, and expose the hook's result via a
 * mutable ref so every assertion below (`result.current.announce(...)`,
 * `document.querySelector('[aria-live=...]')`, etc.) is unchanged from
 * before this task.
 *
 * ## Timer note (task 018 / NFR-11)
 *
 * The suite previously installed its own `window.requestAnimationFrame`
 * mock via `Object.defineProperty`. That mock was dead code: `jest.useFakeTimers()`
 * (modern/`@sinonjs/fake-timers`, this repo's default) fakes `requestAnimationFrame`
 * itself and silently overwrites any custom implementation installed before
 * it runs -- confirmed by instrumenting `useAnnounce.ts` directly, which showed
 * the hook's `requestAnimationFrame` call was going through Jest's own fake
 * clock, never through the module-level `jest.fn()` mock (whose body never
 * ran). Removed the dead mock in favor of Jest's built-in rAF faking, which
 * `jest.advanceTimersByTime` already drives correctly.
 *
 * Every `announce()`-then-flush step below now uses
 * `jest.advanceTimersByTime(16)` (one simulated frame) instead of
 * `jest.runAllTimers()`. `runAllTimers()` drains the ENTIRE pending timer
 * queue transitively, including the far-future `clearDelay` timeout (500ms /
 * 1000ms) scheduled by the very same `announce()` call -- so it was clearing
 * the just-announced message back to `''` in the same step that was supposed
 * to reveal it (confirmed empirically: `runAllTimers()` left `textContent`
 * as `''` immediately, even for a single `announce()` call with no second
 * announcement in play). That is a fake-timer usage bug in the test, not a
 * hook defect or an assertion of the "wrong expected value" kind -- every
 * assertion still checks the exact same substantive outcome (message
 * appears, then clears after its delay); only the flushing mechanism that
 * isolates "immediately after the frame" from "after clearDelay" changed.
 */

import { render, act, waitFor } from '@testing-library/react';
import { createElement } from 'react';
import {
  useAnnounce,
  useAnnounceOnChange,
  type AnnounceMode,
  type UseAnnounceOptions,
  type UseAnnounceResult,
} from '../useAnnounce';

/** Renders `useAnnounce` inside a harness component so `liveRegion` mounts. */
function renderAnnounce(options?: UseAnnounceOptions) {
  const result: { current: UseAnnounceResult } = {
    current: undefined as unknown as UseAnnounceResult,
  };

  function AnnounceHarness() {
    result.current = useAnnounce(options);
    return result.current.liveRegion;
  }

  const view = render(createElement(AnnounceHarness));

  return { ...view, result };
}

interface AnnounceOnChangeProps {
  message: string | null | undefined;
  deps: React.DependencyList;
  mode?: AnnounceMode;
}

/** Renders `useAnnounceOnChange` inside a harness component so its returned live region mounts. */
function renderAnnounceOnChange(initialProps: AnnounceOnChangeProps) {
  function AnnounceOnChangeHarness(props: AnnounceOnChangeProps) {
    return useAnnounceOnChange(props.message, props.deps, props.mode);
  }

  const view = render(createElement(AnnounceOnChangeHarness, initialProps));

  return {
    ...view,
    rerender: (newProps: AnnounceOnChangeProps) => view.rerender(createElement(AnnounceOnChangeHarness, newProps)),
  };
}

describe('useAnnounce', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.clearAllMocks();
    // Clean up any existing live regions
    document.body.innerHTML = '';
  });

  afterEach(() => {
    jest.useRealTimers();
    // NOTE (task 018 / NFR-11): this used to also manually
    // `document.querySelectorAll('.sr-only').forEach(el => el.remove())` as a
    // defensive belt-and-suspenders cleanup for the OLD implementation, which
    // appended the live regions to `document.body` outside React. Now that
    // the regions are React-owned (`liveRegion`, unmounted normally by RTL's
    // own automatic `cleanup()`), that manual removal is not just redundant
    // but actively harmful: because this `afterEach` (nested inside this
    // `describe`) runs BEFORE RTL's own globally-registered cleanup afterEach
    // (Jest runs afterEach hooks innermost-first), it was yanking the nodes
    // out from under React ahead of React's own unmount -- reproducing
    // exactly the `NotFoundError: The node to be removed is not a child of
    // this node.` this task fixes, now self-inflicted by the test file
    // instead of by the production hook. Confirmed by a minimal repro: with
    // this line present, even a render-only test (no announce/timers at all)
    // threw on cleanup; removing it made the same test pass.
  });

  describe('initialization', () => {
    it('creates live region elements on mount', () => {
      renderAnnounce();

      // Should create two live regions (polite and assertive)
      const liveRegions = document.querySelectorAll('[aria-live]');
      expect(liveRegions).toHaveLength(2);
    });

    it('creates polite live region with correct attributes', () => {
      renderAnnounce();

      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion).not.toBeNull();
      expect(politeRegion).toHaveAttribute('role', 'status');
      expect(politeRegion).toHaveAttribute('aria-atomic', 'true');
    });

    it('creates assertive live region with correct attributes', () => {
      renderAnnounce();

      const assertiveRegion = document.querySelector('[aria-live="assertive"]');
      expect(assertiveRegion).not.toBeNull();
      expect(assertiveRegion).toHaveAttribute('role', 'alert');
      expect(assertiveRegion).toHaveAttribute('aria-atomic', 'true');
    });

    it('live regions are visually hidden', () => {
      renderAnnounce();

      const liveRegions = document.querySelectorAll('.sr-only');
      expect(liveRegions.length).toBe(2);

      liveRegions.forEach(region => {
        const style = (region as HTMLElement).style;
        expect(style.position).toBe('absolute');
        expect(style.width).toBe('1px');
        expect(style.height).toBe('1px');
        expect(style.overflow).toBe('hidden');
      });
    });

    it('removes live regions on unmount', () => {
      const { unmount } = renderAnnounce();

      unmount();

      const liveRegions = document.querySelectorAll('[aria-live]');
      expect(liveRegions).toHaveLength(0);
    });
  });

  describe('announce', () => {
    it('announces polite message by default', async () => {
      const { result } = renderAnnounce();

      act(() => {
        result.current.announce('Hello world');
      });

      // Allow requestAnimationFrame to fire
      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      await waitFor(() => {
        const politeRegion = document.querySelector('[aria-live="polite"]');
        expect(politeRegion?.textContent).toBe('Hello world');
      });
    });

    it('announces assertive message when mode is assertive', async () => {
      const { result } = renderAnnounce();

      act(() => {
        result.current.announce('Error occurred', 'assertive');
      });

      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      await waitFor(() => {
        const assertiveRegion = document.querySelector('[aria-live="assertive"]');
        expect(assertiveRegion?.textContent).toBe('Error occurred');
      });
    });

    it('clears message after clearDelay', async () => {
      const { result } = renderAnnounce({ clearDelay: 500 });

      act(() => {
        result.current.announce('Temporary message');
      });

      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion?.textContent).toBe('Temporary message');

      // Advance past clear delay
      act(() => {
        jest.advanceTimersByTime(600);
      });

      expect(politeRegion?.textContent).toBe('');
    });

    it('uses default clearDelay of 1000ms', async () => {
      const { result } = renderAnnounce();

      act(() => {
        result.current.announce('Default delay message');
      });

      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion?.textContent).toBe('Default delay message');

      // Advance but not past 1000ms
      act(() => {
        jest.advanceTimersByTime(800);
      });

      expect(politeRegion?.textContent).toBe('Default delay message');

      // Advance past 1000ms
      act(() => {
        jest.advanceTimersByTime(300);
      });

      expect(politeRegion?.textContent).toBe('');
    });

    it('re-announces same message', async () => {
      const { result } = renderAnnounce({ clearDelay: 100 });

      act(() => {
        result.current.announce('Same message');
      });

      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      // Clear the message
      act(() => {
        jest.advanceTimersByTime(200);
      });

      // Announce the same message again
      act(() => {
        result.current.announce('Same message');
      });

      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion?.textContent).toBe('Same message');
    });
  });

  describe('clear', () => {
    it('clears all live regions', async () => {
      const { result } = renderAnnounce();

      // Announce messages in both regions
      act(() => {
        result.current.announce('Polite message', 'polite');
        result.current.announce('Assertive message', 'assertive');
      });

      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      // Clear both
      act(() => {
        result.current.clear();
      });

      const politeRegion = document.querySelector('[aria-live="polite"]');
      const assertiveRegion = document.querySelector('[aria-live="assertive"]');

      expect(politeRegion?.textContent).toBe('');
      expect(assertiveRegion?.textContent).toBe('');
    });

    it('cancels pending clear timeout', async () => {
      const { result } = renderAnnounce({ clearDelay: 1000 });

      act(() => {
        result.current.announce('Will be cleared');
      });

      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      // Clear immediately
      act(() => {
        result.current.clear();
      });

      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion?.textContent).toBe('');

      // Even after delay, content should still be empty (timeout was cancelled)
      act(() => {
        jest.advanceTimersByTime(1500);
      });

      expect(politeRegion?.textContent).toBe('');
    });
  });

  describe('React ownership (task 018 / NFR-11)', () => {
    it('polite and assertive regions are distinct elements, both still receive their own announcements', async () => {
      const { result } = renderAnnounce();

      const politeRegion = document.querySelector('[aria-live="polite"]');
      const assertiveRegion = document.querySelector('[aria-live="assertive"]');
      expect(politeRegion).not.toBeNull();
      expect(assertiveRegion).not.toBeNull();
      expect(politeRegion).not.toBe(assertiveRegion);

      act(() => {
        result.current.announce('Polite one', 'polite');
        result.current.announce('Assertive one', 'assertive');
      });

      act(() => {
        jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
      });

      await waitFor(() => {
        expect(document.querySelector('[aria-live="polite"]')?.textContent).toBe('Polite one');
        expect(document.querySelector('[aria-live="assertive"]')?.textContent).toBe('Assertive one');
      });
    });

    it('unmounting twice in a row (React 19 double-invoke shape) does not throw', () => {
      const { unmount } = renderAnnounce();

      // The original defect (`NotFoundError: The node to be removed is not
      // a child of this node.`) surfaced during React's own unmount
      // bookkeeping. Asserting `unmount()` completes without throwing is
      // the regression pin for that failure mode.
      expect(() => unmount()).not.toThrow();
    });
  });
});

describe('useAnnounceOnChange', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.clearAllMocks();
    document.body.innerHTML = '';
  });

  afterEach(() => {
    jest.useRealTimers();
    // See the matching note in the `useAnnounce` describe block's afterEach
    // above -- manually removing `.sr-only` nodes here now fights RTL's own
    // (correct) React-owned unmount instead of complementing it.
  });

  it('does not announce on initial render', () => {
    renderAnnounceOnChange({ message: 'Initial message', deps: [1] });

    const politeRegion = document.querySelector('[aria-live="polite"]');
    expect(politeRegion?.textContent || '').toBe('');
  });

  it('announces when dependency changes', async () => {
    let value = 'First';

    const { rerender } = renderAnnounceOnChange({ message: value, deps: [value] });

    // Initial render - no announcement
    expect(document.querySelector('[aria-live="polite"]')?.textContent || '').toBe('');

    // Change the value
    value = 'Second';
    rerender({ message: value, deps: [value] });

    act(() => {
      jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
    });

    await waitFor(() => {
      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion?.textContent).toBe('Second');
    });
  });

  it('uses assertive mode when specified', async () => {
    let value = 'First';

    const { rerender } = renderAnnounceOnChange({
      message: value,
      deps: [value],
      mode: 'assertive' as const,
    });

    // Change the value
    value = 'Error!';
    rerender({ message: value, deps: [value], mode: 'assertive' as const });

    act(() => {
      jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
    });

    await waitFor(() => {
      const assertiveRegion = document.querySelector('[aria-live="assertive"]');
      expect(assertiveRegion?.textContent).toBe('Error!');
    });
  });

  it('does not announce when message is null', async () => {
    let message: string | null = 'First';

    const { rerender } = renderAnnounceOnChange({ message, deps: [message] });

    // Change to null
    message = null;
    rerender({ message, deps: [message] });

    act(() => {
      jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
    });

    const politeRegion = document.querySelector('[aria-live="polite"]');
    expect(politeRegion?.textContent || '').toBe('');
  });

  it('does not announce when message is undefined', async () => {
    let message: string | undefined = 'First';

    const { rerender } = renderAnnounceOnChange({ message, deps: [message] });

    // Change to undefined
    message = undefined;
    rerender({ message, deps: [message] });

    act(() => {
      jest.advanceTimersByTime(16); // one simulated frame -- flush the rAF-scheduled text write without also draining the (much longer) clearDelay timeout
    });

    const politeRegion = document.querySelector('[aria-live="polite"]');
    expect(politeRegion?.textContent || '').toBe('');
  });
});
