/**
 * Unit tests for useAnnounce hook
 *
 * Tests the screen reader announcement hook for accessibility.
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import { useAnnounce, useAnnounceOnChange } from '../useAnnounce';

// Mock requestAnimationFrame
const mockRequestAnimationFrame = jest.fn((callback: FrameRequestCallback) => {
  setTimeout(() => callback(Date.now()), 0);
  return 1;
});

const mockCancelAnimationFrame = jest.fn();

Object.defineProperty(window, 'requestAnimationFrame', {
  value: mockRequestAnimationFrame,
  writable: true,
});

Object.defineProperty(window, 'cancelAnimationFrame', {
  value: mockCancelAnimationFrame,
  writable: true,
});

describe('useAnnounce', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.clearAllMocks();
    // Clean up any existing live regions
    document.body.innerHTML = '';
  });

  afterEach(() => {
    jest.useRealTimers();
    // Clean up any live regions created by tests
    document.querySelectorAll('.sr-only').forEach((el) => el.remove());
  });

  describe('initialization', () => {
    it('creates live region elements on mount', () => {
      renderHook(() => useAnnounce());

      // Should create two live regions (polite and assertive)
      const liveRegions = document.querySelectorAll('[aria-live]');
      expect(liveRegions).toHaveLength(2);
    });

    it('creates polite live region with correct attributes', () => {
      renderHook(() => useAnnounce());

      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion).not.toBeNull();
      expect(politeRegion).toHaveAttribute('role', 'status');
      expect(politeRegion).toHaveAttribute('aria-atomic', 'true');
    });

    it('creates assertive live region with correct attributes', () => {
      renderHook(() => useAnnounce());

      const assertiveRegion = document.querySelector('[aria-live="assertive"]');
      expect(assertiveRegion).not.toBeNull();
      expect(assertiveRegion).toHaveAttribute('role', 'alert');
      expect(assertiveRegion).toHaveAttribute('aria-atomic', 'true');
    });

    it('live regions are visually hidden', () => {
      renderHook(() => useAnnounce());

      const liveRegions = document.querySelectorAll('.sr-only');
      expect(liveRegions.length).toBe(2);

      liveRegions.forEach((region) => {
        const style = (region as HTMLElement).style;
        expect(style.position).toBe('absolute');
        expect(style.width).toBe('1px');
        expect(style.height).toBe('1px');
        expect(style.overflow).toBe('hidden');
      });
    });

    it('removes live regions on unmount', () => {
      const { unmount } = renderHook(() => useAnnounce());

      unmount();

      const liveRegions = document.querySelectorAll('[aria-live]');
      expect(liveRegions).toHaveLength(0);
    });
  });

  describe('announce', () => {
    it('announces polite message by default', async () => {
      const { result } = renderHook(() => useAnnounce());

      act(() => {
        result.current.announce('Hello world');
      });

      // Allow requestAnimationFrame to fire
      act(() => {
        jest.runAllTimers();
      });

      await waitFor(() => {
        const politeRegion = document.querySelector('[aria-live="polite"]');
        expect(politeRegion?.textContent).toBe('Hello world');
      });
    });

    it('announces assertive message when mode is assertive', async () => {
      const { result } = renderHook(() => useAnnounce());

      act(() => {
        result.current.announce('Error occurred', 'assertive');
      });

      act(() => {
        jest.runAllTimers();
      });

      await waitFor(() => {
        const assertiveRegion = document.querySelector('[aria-live="assertive"]');
        expect(assertiveRegion?.textContent).toBe('Error occurred');
      });
    });

    it('clears message after clearDelay', async () => {
      const { result } = renderHook(() => useAnnounce({ clearDelay: 500 }));

      act(() => {
        result.current.announce('Temporary message');
      });

      act(() => {
        jest.runAllTimers();
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
      const { result } = renderHook(() => useAnnounce());

      act(() => {
        result.current.announce('Default delay message');
      });

      act(() => {
        jest.runAllTimers();
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
      const { result } = renderHook(() => useAnnounce({ clearDelay: 100 }));

      act(() => {
        result.current.announce('Same message');
      });

      act(() => {
        jest.runAllTimers();
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
        jest.runAllTimers();
      });

      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion?.textContent).toBe('Same message');
    });
  });

  describe('clear', () => {
    it('clears all live regions', async () => {
      const { result } = renderHook(() => useAnnounce());

      // Announce messages in both regions
      act(() => {
        result.current.announce('Polite message', 'polite');
        result.current.announce('Assertive message', 'assertive');
      });

      act(() => {
        jest.runAllTimers();
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
      const { result } = renderHook(() => useAnnounce({ clearDelay: 1000 }));

      act(() => {
        result.current.announce('Will be cleared');
      });

      act(() => {
        jest.runAllTimers();
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
});

describe('useAnnounceOnChange', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    document.body.innerHTML = '';
  });

  afterEach(() => {
    jest.useRealTimers();
    document.querySelectorAll('.sr-only').forEach((el) => el.remove());
  });

  it('does not announce on initial render', () => {
    const { result } = renderHook(() => {
      useAnnounceOnChange('Initial message', [1]);
      return null;
    });

    const politeRegion = document.querySelector('[aria-live="polite"]');
    expect(politeRegion?.textContent || '').toBe('');
  });

  it('announces when dependency changes', async () => {
    let value = 'First';

    const { rerender } = renderHook(({ message, deps }) => {
      useAnnounceOnChange(message, deps);
    }, {
      initialProps: { message: value, deps: [value] },
    });

    // Initial render - no announcement
    expect(document.querySelector('[aria-live="polite"]')?.textContent || '').toBe('');

    // Change the value
    value = 'Second';
    rerender({ message: value, deps: [value] });

    act(() => {
      jest.runAllTimers();
    });

    await waitFor(() => {
      const politeRegion = document.querySelector('[aria-live="polite"]');
      expect(politeRegion?.textContent).toBe('Second');
    });
  });

  it('uses assertive mode when specified', async () => {
    let value = 'First';

    const { rerender } = renderHook(({ message, deps, mode }) => {
      useAnnounceOnChange(message, deps, mode);
    }, {
      initialProps: { message: value, deps: [value], mode: 'assertive' as const },
    });

    // Change the value
    value = 'Error!';
    rerender({ message: value, deps: [value], mode: 'assertive' as const });

    act(() => {
      jest.runAllTimers();
    });

    await waitFor(() => {
      const assertiveRegion = document.querySelector('[aria-live="assertive"]');
      expect(assertiveRegion?.textContent).toBe('Error!');
    });
  });

  it('does not announce when message is null', async () => {
    let message: string | null = 'First';

    const { rerender } = renderHook(({ msg, deps }) => {
      useAnnounceOnChange(msg, deps);
    }, {
      initialProps: { msg: message, deps: [message] },
    });

    // Change to null
    message = null;
    rerender({ msg: message, deps: [message] });

    act(() => {
      jest.runAllTimers();
    });

    const politeRegion = document.querySelector('[aria-live="polite"]');
    expect(politeRegion?.textContent || '').toBe('');
  });

  it('does not announce when message is undefined', async () => {
    let message: string | undefined = 'First';

    const { rerender } = renderHook(({ msg, deps }) => {
      useAnnounceOnChange(msg, deps);
    }, {
      initialProps: { msg: message, deps: [message] },
    });

    // Change to undefined
    message = undefined;
    rerender({ msg: message, deps: [message] });

    act(() => {
      jest.runAllTimers();
    });

    const politeRegion = document.querySelector('[aria-live="polite"]');
    expect(politeRegion?.textContent || '').toBe('');
  });
});
