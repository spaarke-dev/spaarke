/**
 * Responsive Hook - Adaptive Modal Sizing
 *
 * Provides responsive sizing for the AI Assistant modal based on
 * viewport dimensions. Supports breakpoints and user preferences.
 *
 * @version 1.0.0
 * Task 054: Responsive modal sizing
 */

import { useState, useEffect, useCallback, useMemo } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface Breakpoint {
  /** Breakpoint name */
  name: string;
  /** Minimum width for this breakpoint */
  minWidth: number;
  /** Maximum width for this breakpoint (exclusive) */
  maxWidth: number;
}

export interface ModalSize {
  /** Width in pixels */
  width: number;
  /** Height in pixels */
  height: number;
  /** Minimum width */
  minWidth: number;
  /** Minimum height */
  minHeight: number;
  /** Maximum width */
  maxWidth: number;
  /** Maximum height */
  maxHeight: number;
}

export interface UseResponsiveOptions {
  /** Initial width (before responsive calculation) */
  initialWidth?: number;
  /** Initial height (before responsive calculation) */
  initialHeight?: number;
  /** User's preferred width (persisted) */
  preferredWidth?: number;
  /** User's preferred height (persisted) */
  preferredHeight?: number;
  /** Container element for relative sizing */
  containerRef?: React.RefObject<HTMLElement>;
  /** Custom breakpoints */
  breakpoints?: Breakpoint[];
}

export interface UseResponsiveReturn {
  /** Current viewport width */
  viewportWidth: number;
  /** Current viewport height */
  viewportHeight: number;
  /** Current breakpoint name */
  breakpoint: string;
  /** Calculated modal size */
  modalSize: ModalSize;
  /** Whether viewport is mobile-sized */
  isMobile: boolean;
  /** Whether viewport is tablet-sized */
  isTablet: boolean;
  /** Whether viewport is desktop-sized */
  isDesktop: boolean;
  /** Update preferred size (user resize) */
  setPreferredSize: (width: number, height: number) => void;
  /** Reset to default size for current breakpoint */
  resetSize: () => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Default Breakpoints
// ─────────────────────────────────────────────────────────────────────────────

const DEFAULT_BREAKPOINTS: Breakpoint[] = [
  { name: 'mobile', minWidth: 0, maxWidth: 640 },
  { name: 'tablet', minWidth: 640, maxWidth: 1024 },
  { name: 'desktop', minWidth: 1024, maxWidth: 1440 },
  { name: 'wide', minWidth: 1440, maxWidth: Infinity },
];

// ─────────────────────────────────────────────────────────────────────────────
// Size Configurations per Breakpoint
// ─────────────────────────────────────────────────────────────────────────────

const MODAL_SIZES: Record<string, ModalSize> = {
  mobile: {
    width: 320,
    height: 450,
    minWidth: 280,
    minHeight: 350,
    maxWidth: 360,
    maxHeight: 500,
  },
  tablet: {
    width: 360,
    height: 480,
    minWidth: 320,
    minHeight: 400,
    maxWidth: 480,
    maxHeight: 600,
  },
  desktop: {
    width: 400,
    height: 520,
    minWidth: 320,
    minHeight: 400,
    maxWidth: 600,
    maxHeight: 800,
  },
  wide: {
    width: 440,
    height: 560,
    minWidth: 360,
    minHeight: 450,
    maxWidth: 700,
    maxHeight: 900,
  },
};

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

export const useResponsive = (
  options: UseResponsiveOptions = {}
): UseResponsiveReturn => {
  const {
    preferredWidth,
    preferredHeight,
    containerRef,
    breakpoints = DEFAULT_BREAKPOINTS,
  } = options;

  // Viewport dimensions
  const [viewportWidth, setViewportWidth] = useState(
    typeof window !== 'undefined' ? window.innerWidth : 1024
  );
  const [viewportHeight, setViewportHeight] = useState(
    typeof window !== 'undefined' ? window.innerHeight : 768
  );

  // User's preferred size (persisted via props)
  const [userPreferredWidth, setUserPreferredWidth] = useState(preferredWidth);
  const [userPreferredHeight, setUserPreferredHeight] = useState(preferredHeight);

  // Track viewport changes
  useEffect(() => {
    const handleResize = () => {
      setViewportWidth(window.innerWidth);
      setViewportHeight(window.innerHeight);
    };

    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  // Track container size changes
  useEffect(() => {
    if (!containerRef?.current) return;

    const observer = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (entry) {
        // Could use container size instead of viewport if needed
      }
    });

    observer.observe(containerRef.current);
    return () => observer.disconnect();
  }, [containerRef]);

  // Determine current breakpoint
  const breakpoint = useMemo(() => {
    for (const bp of breakpoints) {
      if (viewportWidth >= bp.minWidth && viewportWidth < bp.maxWidth) {
        return bp.name;
      }
    }
    return 'desktop';
  }, [viewportWidth, breakpoints]);

  // Calculate modal size
  const modalSize = useMemo((): ModalSize => {
    const baseSize = MODAL_SIZES[breakpoint] || MODAL_SIZES.desktop;

    // If user has a preferred size, use it (within constraints)
    let width = userPreferredWidth ?? baseSize.width;
    let height = userPreferredHeight ?? baseSize.height;

    // Clamp to constraints
    width = Math.min(baseSize.maxWidth, Math.max(baseSize.minWidth, width));
    height = Math.min(baseSize.maxHeight, Math.max(baseSize.minHeight, height));

    // Ensure modal fits in viewport with padding
    const maxViewportWidth = viewportWidth - 40; // 20px padding each side
    const maxViewportHeight = viewportHeight - 100; // 50px padding top/bottom

    width = Math.min(width, maxViewportWidth);
    height = Math.min(height, maxViewportHeight);

    return {
      width,
      height,
      minWidth: baseSize.minWidth,
      minHeight: baseSize.minHeight,
      maxWidth: Math.min(baseSize.maxWidth, maxViewportWidth),
      maxHeight: Math.min(baseSize.maxHeight, maxViewportHeight),
    };
  }, [breakpoint, userPreferredWidth, userPreferredHeight, viewportWidth, viewportHeight]);

  // Breakpoint helpers
  const isMobile = breakpoint === 'mobile';
  const isTablet = breakpoint === 'tablet';
  const isDesktop = breakpoint === 'desktop' || breakpoint === 'wide';

  // Update preferred size (called on user resize)
  const setPreferredSize = useCallback((width: number, height: number) => {
    setUserPreferredWidth(width);
    setUserPreferredHeight(height);
  }, []);

  // Reset to default size
  const resetSize = useCallback(() => {
    setUserPreferredWidth(undefined);
    setUserPreferredHeight(undefined);
  }, []);

  return {
    viewportWidth,
    viewportHeight,
    breakpoint,
    modalSize,
    isMobile,
    isTablet,
    isDesktop,
    setPreferredSize,
    resetSize,
  };
};

export default useResponsive;
