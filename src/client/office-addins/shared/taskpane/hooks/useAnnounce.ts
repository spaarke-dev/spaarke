import { useCallback, useEffect, useMemo, useRef, createElement, Fragment, type ReactElement } from 'react';

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
 * ## React ownership (task 018 / NFR-11)
 *
 * The live regions are now rendered as ordinary React elements (`liveRegion`)
 * rather than created with `document.createElement` and appended to
 * `document.body` by hand. **The calling component MUST render `liveRegion`
 * somewhere in its own JSX output** (placement doesn't matter visually --
 * the regions are `sr-only` -- but it must be mounted for announcements to
 * reach assistive technology). This fixes a React 19 defect where nodes
 * created and removed entirely outside React's tracked tree collided with
 * React's own unmount bookkeeping (`NotFoundError: The node to be removed
 * is not a child of this node.`). Guarding the old `removeChild` call would
 * only have silenced the symptom while leaving the nodes outside React's
 * ownership -- rendering them declaratively is the actual fix.
 *
 * @example
 * ```tsx
 * const { announce, liveRegion } = useAnnounce();
 *
 * // Polite announcement (default) - waits for current speech to finish
 * announce('Document saved successfully');
 *
 * // Assertive announcement - interrupts current speech
 * announce('Error: Connection lost', 'assertive');
 *
 * return (
 *   <>
 *     {liveRegion}
 *     ...rest of the component's JSX...
 *   </>
 * );
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
  /**
   * The polite + assertive live region markup. The calling component MUST
   * render this (e.g. `return <>{liveRegion}{...}</>`) so the regions are
   * mounted through React's own tree instead of being attached to
   * `document.body` outside it (task 018 / NFR-11).
   */
  liveRegion: ReactElement;
}

const SR_ONLY_STYLE = {
  position: 'absolute',
  width: '1px',
  height: '1px',
  padding: '0',
  margin: '-1px',
  overflow: 'hidden',
  clip: 'rect(0, 0, 0, 0)',
  whiteSpace: 'nowrap',
  border: '0',
} as const;

/**
 * Creates and returns an announce function for screen reader announcements.
 *
 * @param options - Configuration options
 * @returns Object with announce, clear, and the liveRegion element to render
 */
export function useAnnounce(options: UseAnnounceOptions = {}): UseAnnounceResult {
  const { clearDelay = 1000 } = options;

  // Refs for the live region elements. React creates/attaches/detaches the
  // underlying DOM nodes (via the `liveRegion` JSX below); these refs are
  // only used to imperatively write `textContent` for announcements, which
  // is safe because render never also supplies `children` for these nodes.
  const politeRegionRef = useRef<HTMLDivElement | null>(null);
  const assertiveRegionRef = useRef<HTMLDivElement | null>(null);
  const clearTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const liveRegion = useMemo(
    () =>
      createElement(
        Fragment,
        null,
        createElement('div', {
          key: 'polite',
          ref: politeRegionRef,
          role: 'status',
          'aria-live': 'polite',
          'aria-atomic': 'true',
          className: 'sr-only',
          style: SR_ONLY_STYLE,
        }),
        createElement('div', {
          key: 'assertive',
          ref: assertiveRegionRef,
          role: 'alert',
          'aria-live': 'assertive',
          'aria-atomic': 'true',
          className: 'sr-only',
          style: SR_ONLY_STYLE,
        })
      ),
    []
  );

  // Only timer bookkeeping remains here -- the live region DOM nodes
  // themselves are owned by React via `liveRegion` above, so there is
  // nothing DOM-related left to clean up on unmount.
  useEffect(() => {
    return () => {
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

  return { announce, clear, liveRegion };
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
 *
 * @returns The live region element -- like `useAnnounce`, the calling
 * component MUST render the returned element for announcements to reach
 * assistive technology (task 018 / NFR-11).
 */
export function useAnnounceOnChange(
  message: string | null | undefined,
  deps: React.DependencyList,
  mode: AnnounceMode = 'polite'
): ReactElement {
  const { announce, liveRegion } = useAnnounce();
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

  return liveRegion;
}
