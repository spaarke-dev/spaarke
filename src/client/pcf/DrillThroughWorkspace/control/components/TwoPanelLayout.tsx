/**
 * Two-Panel Layout Component
 * Responsive layout: Chart (1/3) | Dataset Grid (2/3)
 * Stacks vertically on small screens per ADR-021
 */

import * as React from "react";
import { makeStyles, tokens, shorthands } from "@fluentui/react-components";

// Responsive breakpoint for stacking
const STACK_BREAKPOINT = 768;

const useStyles = makeStyles({
  container: {
    display: "flex",
    flexDirection: "row",
    height: "100%",
    width: "100%",
    overflow: "hidden",
    backgroundColor: tokens.colorNeutralBackground1,
  },
  containerStacked: {
    flexDirection: "column",
  },
  leftPanel: {
    width: "33.33%",
    minWidth: "280px",
    maxWidth: "500px",
    height: "100%",
    display: "flex",
    flexDirection: "column",
    borderRight: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    overflow: "hidden",
  },
  leftPanelStacked: {
    width: "100%",
    minWidth: "unset",
    maxWidth: "unset",
    height: "40%",
    minHeight: "200px",
    borderRight: "none",
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
  },
  rightPanel: {
    flex: 1,
    height: "100%",
    display: "flex",
    flexDirection: "column",
    backgroundColor: tokens.colorNeutralBackground1,
    overflow: "hidden",
  },
  rightPanelStacked: {
    height: "60%",
    flex: "unset",
  },
  panelContent: {
    flex: 1,
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalL),
    overflow: "auto",
  },
  panelHeader: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalL),
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    minHeight: "40px",
  },
  panelTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  resizeHandle: {
    width: "4px",
    height: "100%",
    backgroundColor: "transparent",
    cursor: "col-resize",
    position: "relative",
    flexShrink: 0,
    "&:hover": {
      backgroundColor: tokens.colorBrandBackground,
    },
    "&::after": {
      content: '""',
      position: "absolute",
      top: "50%",
      left: "50%",
      transform: "translate(-50%, -50%)",
      width: "2px",
      height: "32px",
      backgroundColor: tokens.colorNeutralStroke1,
      borderRadius: tokens.borderRadiusSmall,
    },
  },
  resizeHandleStacked: {
    width: "100%",
    height: "4px",
    cursor: "row-resize",
    "&::after": {
      width: "32px",
      height: "2px",
    },
  },
});

export interface ITwoPanelLayoutProps {
  /** Content for the left panel (chart) */
  leftContent: React.ReactNode;
  /** Content for the right panel (dataset grid) */
  rightContent: React.ReactNode;
  /** Optional title for left panel */
  leftTitle?: string;
  /** Optional title for right panel */
  rightTitle?: string;
  /** Optional actions for left panel header */
  leftActions?: React.ReactNode;
  /** Optional actions for right panel header */
  rightActions?: React.ReactNode;
  /** Show panel headers */
  showHeaders?: boolean;
  /** Enable resize handle */
  enableResize?: boolean;
  /** Callback when resize occurs */
  onResize?: (leftWidth: number) => void;
}

export const TwoPanelLayout: React.FC<ITwoPanelLayoutProps> = ({
  leftContent,
  rightContent,
  leftTitle = "Chart",
  rightTitle = "Dataset",
  leftActions,
  rightActions,
  showHeaders = true,
  enableResize = false,
  onResize,
}) => {
  const styles = useStyles();
  const [isStacked, setIsStacked] = React.useState(false);
  const [leftWidth, setLeftWidth] = React.useState(33.33);
  const containerRef = React.useRef<HTMLDivElement>(null);
  const isResizing = React.useRef(false);

  // Check container width and determine if layout should stack
  React.useEffect(() => {
    const checkWidth = () => {
      if (containerRef.current) {
        const width = containerRef.current.offsetWidth;
        setIsStacked(width < STACK_BREAKPOINT);
      }
    };

    checkWidth();

    // Use ResizeObserver for responsive updates
    const resizeObserver = new ResizeObserver(checkWidth);
    if (containerRef.current) {
      resizeObserver.observe(containerRef.current);
    }

    return () => {
      resizeObserver.disconnect();
    };
  }, []);

  // Handle resize drag
  const handleResizeStart = React.useCallback(
    (e: React.MouseEvent | React.TouchEvent) => {
      if (!enableResize || isStacked) return;

      e.preventDefault();
      isResizing.current = true;

      const startX = "touches" in e ? e.touches[0].clientX : e.clientX;
      const containerWidth = containerRef.current?.offsetWidth || 1;
      const startWidth = leftWidth;

      const handleMove = (moveEvent: MouseEvent | TouchEvent) => {
        if (!isResizing.current) return;

        const currentX =
          "touches" in moveEvent
            ? moveEvent.touches[0].clientX
            : moveEvent.clientX;
        const deltaX = currentX - startX;
        const deltaPercent = (deltaX / containerWidth) * 100;
        const newWidth = Math.min(50, Math.max(20, startWidth + deltaPercent));

        setLeftWidth(newWidth);
        onResize?.(newWidth);
      };

      const handleEnd = () => {
        isResizing.current = false;
        document.removeEventListener("mousemove", handleMove);
        document.removeEventListener("mouseup", handleEnd);
        document.removeEventListener("touchmove", handleMove);
        document.removeEventListener("touchend", handleEnd);
      };

      document.addEventListener("mousemove", handleMove);
      document.addEventListener("mouseup", handleEnd);
      document.addEventListener("touchmove", handleMove);
      document.addEventListener("touchend", handleEnd);
    },
    [enableResize, isStacked, leftWidth, onResize]
  );

  // Dynamic styles for resizable layout
  const leftPanelStyle: React.CSSProperties = enableResize && !isStacked
    ? { width: `${leftWidth}%`, minWidth: "200px", maxWidth: "60%" }
    : {};

  return (
    <div
      ref={containerRef}
      className={`${styles.container} ${isStacked ? styles.containerStacked : ""}`}
    >
      {/* Left Panel - Chart */}
      <div
        className={`${styles.leftPanel} ${isStacked ? styles.leftPanelStacked : ""}`}
        style={leftPanelStyle}
      >
        {showHeaders && (
          <div className={styles.panelHeader}>
            <span className={styles.panelTitle}>{leftTitle}</span>
            {leftActions}
          </div>
        )}
        <div className={styles.panelContent}>{leftContent}</div>
      </div>

      {/* Resize Handle */}
      {enableResize && (
        <div
          className={`${styles.resizeHandle} ${isStacked ? styles.resizeHandleStacked : ""}`}
          onMouseDown={handleResizeStart}
          onTouchStart={handleResizeStart}
          role="separator"
          aria-orientation={isStacked ? "horizontal" : "vertical"}
          aria-label="Resize panels"
          tabIndex={0}
        />
      )}

      {/* Right Panel - Dataset Grid */}
      <div
        className={`${styles.rightPanel} ${isStacked ? styles.rightPanelStacked : ""}`}
      >
        {showHeaders && (
          <div className={styles.panelHeader}>
            <span className={styles.panelTitle}>{rightTitle}</span>
            {rightActions}
          </div>
        )}
        <div className={styles.panelContent}>{rightContent}</div>
      </div>
    </div>
  );
};
