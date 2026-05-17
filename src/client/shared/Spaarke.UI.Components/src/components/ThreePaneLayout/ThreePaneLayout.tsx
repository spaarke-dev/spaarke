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

  // Collapsed left strip — narrow click-to-expand indicator
  leftPaneCollapsed: {
    flex: '0 0 28px',
    minWidth: '28px',
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

  // Collapsed right strip — narrow click-to-expand indicator
  rightPaneCollapsed: {
    flex: '0 0 28px',
    minWidth: '28px',
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

  // Rotated label text for collapsed strips
  collapsedLabel: {
    writingMode: 'vertical-rl',
    transform: 'rotate(180deg)',
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    letterSpacing: '0.05em',
    userSelect: 'none',
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
  className,
}: ThreePaneLayoutProps): JSX.Element {
  const styles = useStyles();

  const {
    leftWidthPx,
    rightWidthPx,
    isLeftVisible,
    isRightVisible,
    toggleLeft,
    toggleRight,
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
          <ChevronRight16Regular style={{ color: tokens.colorNeutralForeground3, flexShrink: 0 }} />
          <span className={styles.collapsedLabel}>{leftPaneCollapseLabel}</span>
        </div>
      )}

      {/* ---- Left Splitter (between left and center) ---- */}
      {isLeftVisible && (
        <PanelSplitter
          onMouseDown={leftSplitterHandlers.onMouseDown}
          onKeyDown={leftSplitterHandlers.onKeyDown}
          onDoubleClick={leftSplitterHandlers.onDoubleClick}
          isDragging={isDragging}
          currentRatio={leftRatio}
        />
      )}

      {/* ---- Center Pane (flex:1) ---- */}
      <div className={styles.centerPane}>{centerPane}</div>

      {/* ---- Right Splitter (between center and right) ---- */}
      {isRightVisible && (
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
          <ChevronLeft16Regular style={{ color: tokens.colorNeutralForeground3, flexShrink: 0 }} />
          <span className={styles.collapsedLabel}>{rightPaneCollapseLabel}</span>
        </div>
      )}
    </div>
  );
}
