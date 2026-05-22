/**
 * ThreePaneLayout Component
 *
 * A reusable three-pane layout with draggable splitters, collapsible side panes,
 * keyboard accessibility, and sessionStorage persistence.
 *
 * Layout:
 *   [Left (fixed px)] [Splitter] [Center (flex:1)] [Splitter] [Right (fixed px)]
 *
 * Extracted and generalized from AnalysisWorkspace App.tsx + usePanelLayout.ts.
 *
 * Features:
 * - Left and right panes collapse to a narrow strip with click-to-expand
 * - Draggable splitters with keyboard support (ArrowLeft/Right, Home/End)
 * - Double-click splitter to reset that pane to its default width
 * - Smooth CSS transition animations (disabled during drag, respects prefers-reduced-motion)
 * - sessionStorage persistence with configurable key prefix
 * - Code Page only — NOT PCF-safe (uses React 19 APIs freely)
 *
 * @see ADR-012 - Shared component library
 * @see ADR-021 - Fluent UI v9 design system (makeStyles + design tokens exclusively)
 */

import * as React from 'react';
import { makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { ChevronLeft16Regular, ChevronRight16Regular } from '@fluentui/react-icons';
import { PanelSplitter } from '../PanelSplitter/PanelSplitter';
import { ThreePaneLayoutProps } from './ThreePaneLayout.types';
import { useThreePaneLayout } from './useThreePaneLayout';

// ---------------------------------------------------------------------------
// Layout constants
// ---------------------------------------------------------------------------

/**
 * (Task 096) Collapsed-strip width in pixels. Set to 48 to mirror the
 * Model-Driven Apps left-nav collapsed-width, per operator feedback
 * 2026-05-22. Previously the strips were 28px (left/right) and 36px
 * (center) — both too narrow to read or click comfortably. Fluent v9 has
 * no design token that matches MDA's left-nav width, so this is a
 * deliberate layout-dimension literal (px); colors / spacing / borders
 * elsewhere remain token-based.
 */
const COLLAPSED_STRIP_PX = 48;

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only, no hard-coded colors (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'row',
    width: '100%',
    height: '100%',
    overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1,
  },

  // Left pane — fixed pixel width, user-resizable
  leftPane: {
    overflow: 'hidden',
    flexShrink: 0,
    height: '100%',
  },

  // Collapsed left strip — narrow click-to-expand indicator.
  // (Task 096) Width 48px (was 28px) mirrors MDA left-nav collapsed width.
  leftPaneCollapsed: {
    flex: `0 0 ${COLLAPSED_STRIP_PX}px`,
    minWidth: `${COLLAPSED_STRIP_PX}px`,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalS,
    gap: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    cursor: 'pointer',
    overflow: 'hidden',
    height: '100%',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: '-2px',
    },
  },

  // Center pane — flex:1, always fills remaining space
  centerPane: {
    flex: 1,
    minWidth: 0,
    overflow: 'hidden',
    position: 'relative',
    height: '100%',
  },

  // Right pane — fixed pixel width, user-resizable
  rightPane: {
    overflow: 'hidden',
    flexShrink: 0,
    height: '100%',
  },

  // Collapsed right strip — narrow click-to-expand indicator.
  // (Task 096) Width 48px (was 28px) mirrors MDA left-nav collapsed width.
  rightPaneCollapsed: {
    flex: `0 0 ${COLLAPSED_STRIP_PX}px`,
    minWidth: `${COLLAPSED_STRIP_PX}px`,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalS,
    gap: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderLeft: `1px solid ${tokens.colorNeutralStroke2}`,
    cursor: 'pointer',
    overflow: 'hidden',
    height: '100%',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: '-2px',
    },
  },

  // (Task 094) Collapsed center strip — narrow click-to-expand indicator.
  // Visual treatment matches left/right strips; bordered on both sides to
  // distinguish it from neighbouring (possibly also collapsed) strips.
  // (Task 096) Width 48px (was 36px) mirrors MDA left-nav collapsed width.
  centerPaneCollapsed: {
    flex: `0 0 ${COLLAPSED_STRIP_PX}px`,
    minWidth: `${COLLAPSED_STRIP_PX}px`,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    paddingTop: tokens.spacingVerticalS,
    gap: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderLeft: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    cursor: 'pointer',
    overflow: 'hidden',
    height: '100%',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground2Hover,
    },
    ':focus-visible': {
      outlineWidth: '2px',
      outlineStyle: 'solid',
      outlineColor: tokens.colorStrokeFocus2,
      outlineOffset: '-2px',
    },
  },

  // Rotated label text for collapsed strips (legacy / fallback when no
  // collapsedIcon prop is provided — Task 096).
  collapsedLabel: {
    writingMode: 'vertical-rl',
    transform: 'rotate(180deg)',
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    letterSpacing: '0.05em',
    userSelect: 'none',
  },

  /**
   * (Task 096) Centered-icon container used INSIDE a collapsed strip when
   * the consumer passes a `*CollapsedIcon` prop. Replaces the rotated-text
   * identifier. The icon is centered vertically + horizontally in the strip
   * and uses neutral foreground 2 (subdued) → 1 on hover (matches typical
   * Fluent v9 icon-button affordance for clickable surfaces).
   */
  collapsedIcon: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '100%',
    flexGrow: 1,
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase400,
    userSelect: 'none',
    ':hover': {
      color: tokens.colorNeutralForeground1,
    },
  },

  // Smooth CSS transitions for collapse/expand — disabled during active drag
  panelAnimated: {
    transitionProperty: 'width, opacity',
    transitionDuration: tokens.durationNormal,
    transitionTimingFunction: tokens.curveEasyEase,
    '@media (prefers-reduced-motion: reduce)': {
      transitionDuration: '0ms',
    },
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * ThreePaneLayout renders a three-pane layout with draggable splitters.
 *
 * The left and right panes are fixed-width and can be collapsed to a narrow
 * strip. The center pane uses flex:1 and always fills remaining space.
 * PanelSplitters between panes are keyboard-accessible.
 *
 * @example
 * ```tsx
 * <ThreePaneLayout
 *   leftPane={<NavigationPanel />}
 *   centerPane={<EditorPanel />}
 *   rightPane={<ChatPanel />}
 *   storageKey="my-workspace"
 *   leftPaneCollapseLabel="Navigation"
 *   rightPaneCollapseLabel="AI Chat"
 * />
 * ```
 */
export function ThreePaneLayout({
  leftPane,
  centerPane,
  rightPane,
  defaultLeftWidthPx = 280,
  defaultRightWidthPx = 360,
  minLeftWidthPx = 180,
  minRightWidthPx = 200,
  minCenterWidthPx = 300,
  storageKey = 'three-pane',
  defaultLeftVisible = true,
  defaultRightVisible = true,
  leftPaneCollapseLabel = 'Show left panel',
  rightPaneCollapseLabel = 'Show right panel',
  centerPaneCollapseLabel = 'Show center panel',
  // (Task 094) External collapse-state override props.
  leftCollapsed,
  centerCollapsed,
  rightCollapsed,
  onToggleLeft,
  onToggleCenter,
  onToggleRight,
  // (Task 096) Optional icon identifiers for collapsed strips.
  // When provided, the icon replaces the rotated-text identifier in
  // the corresponding pane's collapsed strip. The original
  // `*PaneCollapseLabel` text is retained as the strip's accessible name
  // via `aria-label` so screen readers still announce the pane name.
  // When omitted, the legacy rotated-text rendering is used
  // (backwards compatible — LegalWorkspace standalone unchanged).
  leftCollapsedIcon,
  centerCollapsedIcon,
  rightCollapsedIcon,
  className,
}: ThreePaneLayoutProps): JSX.Element {
  const styles = useStyles();

  const {
    leftWidthPx,
    rightWidthPx,
    isLeftVisible: internalLeftVisible,
    isRightVisible: internalRightVisible,
    toggleLeft: internalToggleLeft,
    toggleRight: internalToggleRight,
    leftSplitterHandlers,
    rightSplitterHandlers,
    isDragging,
    containerRef,
  } = useThreePaneLayout({
    defaultLeftWidthPx,
    defaultRightWidthPx,
    minLeftWidthPx,
    minRightWidthPx,
    minCenterWidthPx,
    storageKey,
    defaultLeftVisible,
    defaultRightVisible,
  });

  // (Task 094) Resolve visibility + toggles, preferring external control
  // when provided. When the consumer wires `leftCollapsed` + `onToggleLeft`
  // they fully own the state — useful for the SpaarkeAi shell where
  // collapse persists to localStorage via the `usePaneCollapse` hook.
  const isLeftVisible = leftCollapsed !== undefined ? !leftCollapsed : internalLeftVisible;
  const isRightVisible = rightCollapsed !== undefined ? !rightCollapsed : internalRightVisible;
  // Center pane has no internal collapse — it's opt-in via the props.
  const isCenterVisible = centerCollapsed !== undefined ? !centerCollapsed : true;

  const toggleLeft = onToggleLeft ?? internalToggleLeft;
  const toggleRight = onToggleRight ?? internalToggleRight;
  // toggleCenter is only meaningful when the consumer wired centerCollapsed.
  const toggleCenter = onToggleCenter;

  // Calculate ARIA ratios for PanelSplitter accessibility
  // Left splitter: proportion of (left pane) relative to total visible width
  // Right splitter: proportion of (left + center) relative to total visible width
  const containerWidth = containerRef.current?.getBoundingClientRect().width ?? 0;

  const leftRatio = containerWidth > 0 && isLeftVisible ? leftWidthPx / containerWidth : 0;

  const rightRatio = containerWidth > 0 && isRightVisible ? (containerWidth - rightWidthPx) / containerWidth : 1;

  return (
    <div className={mergeClasses(styles.root, className)} ref={containerRef as React.RefObject<HTMLDivElement>}>
      {/* ---- Left Pane ---- */}
      {isLeftVisible ? (
        <div
          className={mergeClasses(styles.leftPane, !isDragging && styles.panelAnimated)}
          style={{ width: `${leftWidthPx}px` }}
        >
          {leftPane}
        </div>
      ) : (
        <div
          className={styles.leftPaneCollapsed}
          onClick={toggleLeft}
          role="button"
          tabIndex={0}
          aria-label={leftPaneCollapseLabel}
          title={leftPaneCollapseLabel}
          onKeyDown={e => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              toggleLeft();
            }
          }}
        >
          {/* (Task 096) Render icon-only identifier when collapsedIcon prop
              is supplied; otherwise fall through to legacy rotated-text
              rendering for backwards compatibility (e.g. standalone
              LegalWorkspace). aria-label on the outer button retains the
              accessible name regardless of which visual path is chosen. */}
          {leftCollapsedIcon ? (
            <span className={styles.collapsedIcon} aria-hidden="true">
              {leftCollapsedIcon}
            </span>
          ) : (
            <>
              <ChevronRight16Regular style={{ color: tokens.colorNeutralForeground3, flexShrink: 0 }} />
              <span className={styles.collapsedLabel}>{leftPaneCollapseLabel}</span>
            </>
          )}
        </div>
      )}

      {/* ---- Left Splitter (between left and center) ---- */}
      {/* Hide the splitter when EITHER side is collapsed — there's nothing
          meaningful to resize when one side is a fixed-width strip. */}
      {isLeftVisible && isCenterVisible && (
        <PanelSplitter
          onMouseDown={leftSplitterHandlers.onMouseDown}
          onKeyDown={leftSplitterHandlers.onKeyDown}
          onDoubleClick={leftSplitterHandlers.onDoubleClick}
          isDragging={isDragging}
          currentRatio={leftRatio}
        />
      )}

      {/* ---- Center Pane (flex:1, or collapsed strip when centerCollapsed) ---- */}
      {/* (Task 094) When `centerCollapsed===true` the center pane renders as
          a narrow vertical strip with a rotated label, mirroring the left
          and right collapsed strips. Click / Enter / Space re-expands. */}
      {isCenterVisible ? (
        <div className={styles.centerPane}>{centerPane}</div>
      ) : (
        <div
          className={styles.centerPaneCollapsed}
          onClick={toggleCenter}
          role="button"
          tabIndex={0}
          aria-label={centerPaneCollapseLabel}
          title={centerPaneCollapseLabel}
          onKeyDown={e => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              toggleCenter?.();
            }
          }}
        >
          {/* (Task 096) Icon-only or legacy rotated-text — see leftPaneCollapsed comment. */}
          {centerCollapsedIcon ? (
            <span className={styles.collapsedIcon} aria-hidden="true">
              {centerCollapsedIcon}
            </span>
          ) : (
            <>
              <ChevronRight16Regular style={{ color: tokens.colorNeutralForeground3, flexShrink: 0 }} />
              <span className={styles.collapsedLabel}>{centerPaneCollapseLabel}</span>
            </>
          )}
        </div>
      )}

      {/* ---- Right Splitter (between center and right) ---- */}
      {isRightVisible && isCenterVisible && (
        <PanelSplitter
          onMouseDown={rightSplitterHandlers.onMouseDown}
          onKeyDown={rightSplitterHandlers.onKeyDown}
          onDoubleClick={rightSplitterHandlers.onDoubleClick}
          isDragging={isDragging}
          currentRatio={rightRatio}
        />
      )}

      {/* ---- Right Pane ---- */}
      {isRightVisible ? (
        <div
          className={mergeClasses(styles.rightPane, !isDragging && styles.panelAnimated)}
          style={{ width: `${rightWidthPx}px` }}
        >
          {rightPane}
        </div>
      ) : (
        <div
          className={styles.rightPaneCollapsed}
          onClick={toggleRight}
          role="button"
          tabIndex={0}
          aria-label={rightPaneCollapseLabel}
          title={rightPaneCollapseLabel}
          onKeyDown={e => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              toggleRight();
            }
          }}
        >
          {/* (Task 096) Icon-only or legacy rotated-text — see leftPaneCollapsed comment. */}
          {rightCollapsedIcon ? (
            <span className={styles.collapsedIcon} aria-hidden="true">
              {rightCollapsedIcon}
            </span>
          ) : (
            <>
              <ChevronLeft16Regular style={{ color: tokens.colorNeutralForeground3, flexShrink: 0 }} />
              <span className={styles.collapsedLabel}>{rightPaneCollapseLabel}</span>
            </>
          )}
        </div>
      )}
    </div>
  );
}
