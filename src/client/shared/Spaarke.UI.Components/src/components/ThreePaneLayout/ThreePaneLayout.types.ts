/**
 * ThreePaneLayout Types
 *
 * Type definitions for the ThreePaneLayout component and its supporting hook.
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design system
 */

import * as React from 'react';
import { SplitterHandlers } from '../../hooks/useTwoPanelLayout';
// SplitterHandlers is imported for use in this file but NOT re-exported here
// to avoid ambiguity with the hooks barrel export. Consumers should import it
// from '@spaarke/ui-components' directly (it comes through the hooks barrel).

// ---------------------------------------------------------------------------
// Hook return type
// ---------------------------------------------------------------------------

/** Return value of useThreePaneLayout */
export interface UseThreePaneLayoutResult {
  /** Current left pane width in pixels */
  leftWidthPx: number;
  /** Current right pane width in pixels */
  rightWidthPx: number;
  /** Whether the left pane is currently visible */
  isLeftVisible: boolean;
  /** Whether the right pane is currently visible */
  isRightVisible: boolean;
  /** Toggle left pane visibility */
  toggleLeft: () => void;
  /** Toggle right pane visibility */
  toggleRight: () => void;
  /** Handlers for the splitter between left and center panes */
  leftSplitterHandlers: SplitterHandlers;
  /** Handlers for the splitter between center and right panes */
  rightSplitterHandlers: SplitterHandlers;
  /** Whether any splitter is actively being dragged */
  isDragging: boolean;
  /** Reset all panes to default widths and full visibility */
  resetToDefaults: () => void;
  /**
   * (Task 119) Reset left/right widths to the frac-based defaults
   * (`defaultLeftWidthFrac` / `defaultRightWidthFrac` × `window.innerWidth`),
   * discarding any user-dragged pixel widths in sessionStorage. Falls back to
   * the legacy pixel defaults when frac is not provided. Does NOT touch
   * visibility — callers handle uncollapse separately.
   */
  resetToFracDefaults: () => void;
  /** Ref to attach to the outer container element for measuring available width */
  containerRef: React.RefObject<HTMLDivElement | null>;
}

// ---------------------------------------------------------------------------
// Component props
// ---------------------------------------------------------------------------

/** Props for the ThreePaneLayout component */
export interface ThreePaneLayoutProps {
  /**
   * Content for the left pane (e.g. navigation, source viewer).
   * Rendered only when isLeftVisible is true or the collapsed strip is shown.
   */
  leftPane: React.ReactNode;

  /**
   * Content for the center pane (e.g. main editor, content area).
   * Always rendered — uses flex:1 to fill remaining space.
   */
  centerPane: React.ReactNode;

  /**
   * Content for the right pane (e.g. AI chat, properties panel).
   * Rendered only when isRightVisible is true or the collapsed strip is shown.
   */
  rightPane: React.ReactNode;

  /**
   * Default width for the left pane in pixels.
   * @default 280
   */
  defaultLeftWidthPx?: number;

  /**
   * Default width for the right pane in pixels.
   * @default 360
   */
  defaultRightWidthPx?: number;

  /**
   * (Task 117) Initial width for the LEFT pane as a fraction of the viewport
   * (e.g. `0.25` for 25%). Applied ONLY on a fresh session where no per-user
   * pixel width exists in sessionStorage. After the user drags the splitter
   * the resulting pixel value is persisted and used for subsequent mounts.
   *
   * Precedence (highest to lowest) for the mount-time initial width:
   *   1. sessionStorage stored pixel width (user-dragged value persists)
   *   2. `defaultLeftWidthFrac` × `window.innerWidth` (NEW — percentage default)
   *   3. `defaultLeftWidthPx` (legacy pixel default; still required as fallback
   *      for SSR / non-browser environments and as a hard floor when the
   *      computed pixel value is below the minimum width)
   *
   * When omitted, ThreePaneLayout uses the legacy `defaultLeftWidthPx` path
   * (current behavior preserved for any consumer not opting in).
   *
   * Note: `window.innerWidth` is used as the viewport reference because the
   * layout's own bounding rect is not yet measurable on the first mount
   * (the container ref is populated post-mount). The SpaarkeAi shell fills
   * the viewport, so this is an accurate proxy in practice.
   */
  defaultLeftWidthFrac?: number;

  /**
   * (Task 117) Initial width for the RIGHT pane as a fraction of the viewport.
   * See `defaultLeftWidthFrac` for semantics and precedence.
   */
  defaultRightWidthFrac?: number;

  /**
   * Minimum width for the left pane in pixels.
   * @default 180
   */
  minLeftWidthPx?: number;

  /**
   * Minimum width for the right pane in pixels.
   * @default 200
   */
  minRightWidthPx?: number;

  /**
   * Minimum width for the center (flex) pane in pixels.
   * Prevents it from being squeezed below this size.
   * @default 300
   */
  minCenterWidthPx?: number;

  /**
   * Storage key prefix for sessionStorage persistence.
   * Allows multiple instances to have independent state.
   * @default 'three-pane'
   */
  storageKey?: string;

  /**
   * Whether the left pane is initially visible.
   * Overridden by persisted sessionStorage state.
   * @default true
   */
  defaultLeftVisible?: boolean;

  /**
   * Whether the right pane is initially visible.
   * Overridden by persisted sessionStorage state.
   * @default true
   */
  defaultRightVisible?: boolean;

  /**
   * Accessible label for the collapsed left pane strip.
   * @default 'Show left panel'
   */
  leftPaneCollapseLabel?: string;

  /**
   * Accessible label for the collapsed right pane strip.
   * @default 'Show right panel'
   */
  rightPaneCollapseLabel?: string;

  /**
   * (Task 094) Optional externally-controlled collapse state for the LEFT pane.
   * When defined, this overrides the internal `isLeftVisible` derived from
   * sessionStorage; the consumer fully owns the collapse state and persists
   * it however they choose. Used by SpaarkeAi's `usePaneCollapse` hook to
   * collapse panes via clicks on each pane's header.
   */
  leftCollapsed?: boolean;

  /**
   * (Task 094) Optional externally-controlled collapse state for the CENTER
   * pane. The base layout has no internal toggle for center collapse — this
   * is opt-in behaviour exposed for SpaarkeAi's three-pane shell. When the
   * center pane is collapsed it renders as a narrow vertical strip with a
   * rotated label, identical to the existing left/right collapsed strips.
   */
  centerCollapsed?: boolean;

  /**
   * (Task 094) Optional externally-controlled collapse state for the RIGHT pane.
   * See `leftCollapsed` for semantics.
   */
  rightCollapsed?: boolean;

  /**
   * (Task 094) Toggle callback for the LEFT pane. Invoked when the user
   * clicks the collapsed strip's expand button. If provided alongside
   * `leftCollapsed`, takes precedence over the internal `toggleLeft`.
   */
  onToggleLeft?: () => void;

  /**
   * (Task 094) Toggle callback for the CENTER pane. Invoked when the user
   * clicks the collapsed strip's expand button. Required when
   * `centerCollapsed` is wired.
   */
  onToggleCenter?: () => void;

  /**
   * (Task 094) Toggle callback for the RIGHT pane. See `onToggleLeft`.
   */
  onToggleRight?: () => void;

  /**
   * (Task 094) Accessible label for the collapsed CENTER pane strip.
   * @default 'Show center panel'
   */
  centerPaneCollapseLabel?: string;

  /**
   * (Task 096) Optional icon rendered, centered, in the collapsed LEFT strip
   * INSTEAD of the rotated label text. When provided, the collapsed strip
   * shows ONLY the icon — the `leftPaneCollapseLabel` text is retained as the
   * strip's accessible name (aria-label) for screen readers but is not
   * rendered visually. When omitted, the legacy rotated-text rendering is
   * used (backwards compatible — LegalWorkspace standalone unchanged).
   *
   * Pass any Fluent v9 React icon component (typically the same icon the
   * pane passes to `<PaneHeader>`).
   */
  leftCollapsedIcon?: React.ReactElement;

  /**
   * (Task 096) Optional icon rendered, centered, in the collapsed CENTER
   * strip INSTEAD of the rotated label text. See `leftCollapsedIcon`.
   */
  centerCollapsedIcon?: React.ReactElement;

  /**
   * (Task 096) Optional icon rendered, centered, in the collapsed RIGHT
   * strip INSTEAD of the rotated label text. See `leftCollapsedIcon`.
   */
  rightCollapsedIcon?: React.ReactElement;

  /**
   * Optional additional CSS class name for the root container.
   */
  className?: string;
}
