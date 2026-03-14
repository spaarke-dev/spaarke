import { useCallback, useEffect, useRef } from 'react';

/**
 * useAnnounce - Hook for screen reader announcements.
 *
 * Creates an ARIA live region and provides a function to announce
 * messages to screen readers. Supports both polite and assertive
 * announcements.
 *
 * This hook manages an invisible live region element that screen
 * readers monitor for changes. When announce() is called, the
 * message is inserted into this region and announced.
 *
 * WCAG 2.1 AA Compliance:
 * - 4.1.3 Status Messages (Level AA)
 *
 * @example
 * ```tsx
 * const { announce } = useAnnounce();
 *
 * // Polite announcement (default) - waits for current speech to finish
 * announce('Document saved successfully');
 *
 * // Assertive announcement - interrupts current speech
 * announce('Error: Connection lost', 'assertive');
 * ```
 */

export type AnnounceMode = 'polite' | 'assertive';

export interface UseAnnounceOptions {
  /** Delay in ms before clearing the announcement (for re-announcing same message) */
  clearDelay?: number;
}

export interface UseAnnounceResult {
  /** Function to announce a message to screen readers */
  announce: (message: string, mode?: AnnounceMode) => void;
  /** Function to clear any pending announcement */
  clear: () => void;
}

/**
 * Creates and returns an announce function for screen reader announcements.
 *
 * @param options - Configuration options
 * @returns Object with announce and clear functions
 */
export function useAnnounce(options: UseAnnounceOptions = {}): UseAnnounceResult {
  const { clearDelay = 1000 } = options;

  // Refs for the live region elements
  const politeRegionRef = useRef<HTMLDivElement | null>(null);
  const assertiveRegionRef = useRef<HTMLDivElement | null>(null);
  const clearTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Create live region elements on mount
  useEffect(() => {
    // Create polite live region
    const politeRegion = document.createElement('div');
    politeRegion.setAttribute('role', 'status');
    politeRegion.setAttribute('aria-live', 'polite');
    politeRegion.setAttribute('aria-atomic', 'true');
    politeRegion.className = 'sr-only';
    Object.assign(politeRegion.style, {
      position: 'absolute',
      width: '1px',
      height: '1px',
      padding: '0',
      margin: '-1px',
      overflow: 'hidden',
      clip: 'rect(0, 0, 0, 0)',
      whiteSpace: 'nowrap',
      border: '0',
    });
    document.body.appendChild(politeRegion);
    politeRegionRef.current = politeRegion;

    // Create assertive live region
    const assertiveRegion = document.createElement('div');
    assertiveRegion.setAttribute('role', 'alert');
    assertiveRegion.setAttribute('aria-live', 'assertive');
    assertiveRegion.setAttribute('aria-atomic', 'true');
    assertiveRegion.className = 'sr-only';
    Object.assign(assertiveRegion.style, {
      position: 'absolute',
      width: '1px',
      height: '1px',
      padding: '0',
      margin: '-1px',
      overflow: 'hidden',
      clip: 'rect(0, 0, 0, 0)',
      whiteSpace: 'nowrap',
      border: '0',
    });
    document.body.appendChild(assertiveRegion);
    assertiveRegionRef.current = assertiveRegion;

    // Cleanup on unmount
    return () => {
      if (politeRegionRef.current) {
        document.body.removeChild(politeRegionRef.current);
        politeRegionRef.current = null;
      }
      if (assertiveRegionRef.current) {
        document.body.removeChild(assertiveRegionRef.current);
        assertiveRegionRef.current = null;
      }
      if (clearTimeoutRef.current) {
        clearTimeout(clearTimeoutRef.current);
      }
    };
  }, []);

  const clear = useCallback(() => {
    if (politeRegionRef.current) {
      politeRegionRef.current.textContent = '';
    }
    if (assertiveRegionRef.current) {
      assertiveRegionRef.current.textContent = '';
    }
    if (clearTimeoutRef.current) {
      clearTimeout(clearTimeoutRef.current);
      clearTimeoutRef.current = null;
    }
  }, []);

  const announce = useCallback(
    (message: string, mode: AnnounceMode = 'polite') => {
      // Clear any pending timeout
      if (clearTimeoutRef.current) {
        clearTimeout(clearTimeoutRef.current);
      }

      // Select the appropriate region
      const region = mode === 'assertive' ? assertiveRegionRef.current : politeRegionRef.current;

      if (region) {
        // Clear first to ensure re-announcement of same message works
        region.textContent = '';

        // Use requestAnimationFrame to ensure the clear has been processed
        requestAnimationFrame(() => {
          if (region) {
            region.textContent = message;
          }
        });

        // Schedule clearing the message to allow re-announcement
        clearTimeoutRef.current = setTimeout(() => {
          if (region) {
            region.textContent = '';
          }
        }, clearDelay);
      }
    },
    [clearDelay]
  );

  return { announce, clear };
}

/**
 * Hook to announce a message when a value changes.
 *
 * Useful for announcing status changes, loading states, etc.
 *
 * @example
 * ```tsx
 * // Announce when loading state changes
 * useAnnounceOnChange(isLoading ? 'Loading...' : 'Content loaded', [isLoading]);
 *
 * // Announce job status changes
 * useAnnounceOnChange(`Job ${job.status}`, [job.status]);
 * ```
 */
export function useAnnounceOnChange(
  message: string | null | undefined,
  deps: React.DependencyList,
  mode: AnnounceMode = 'polite'
): void {
  const { announce } = useAnnounce();
  const isFirstRender = useRef(true);

  useEffect(() => {
    // Don't announce on initial render
    if (isFirstRender.current) {
      isFirstRender.current = false;
      return;
    }

    if (message) {
      announce(message, mode);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
}
