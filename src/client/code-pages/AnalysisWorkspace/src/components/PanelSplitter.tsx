/**
 * PanelSplitter Component
 *
 * A draggable, keyboard-accessible vertical splitter between the editor and
 * source viewer panels. Renders a 4px grip area with hover/focus indicators
 * and an ARIA role="separator" for accessibility compliance.
 *
 * Features:
 * - Mouse drag to resize
 * - Keyboard resize (ArrowLeft / ArrowRight)
 * - Double-click to reset to default 60/40 split
 * - ARIA separator role with aria-valuenow
 * - Fluent v9 design tokens for all styling (ADR-021)
 *
 * @see ADR-021 - Fluent UI v9 design system
 */

import { makeStyles, mergeClasses, tokens } from "@fluentui/react-components";

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface PanelSplitterProps {
    /** Mouse down handler — start drag operation */
    onMouseDown: (e: React.MouseEvent) => void;
    /** Key down handler — keyboard resize */
    onKeyDown: (e: React.KeyboardEvent) => void;
    /** Double-click handler — reset to default split */
    onDoubleClick: () => void;
    /** Whether the splitter is actively being dragged */
    isDragging: boolean;
    /** Current split ratio (0-1) for ARIA aria-valuenow (left panel proportion) */
    currentRatio: number;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
    splitter: {
        width: "4px",
        cursor: "col-resize",
        backgroundColor: tokens.colorNeutralStroke1,
        transitionProperty: "background-color",
        transitionDuration: "150ms",
        transitionTimingFunction: "ease",
        position: "relative",
        flexShrink: 0,
        // Focus ring via outline
        outlineStyle: "none",
        ":hover": {
            backgroundColor: tokens.colorBrandBackground,
        },
        ":focus-visible": {
            backgroundColor: tokens.colorBrandBackground,
            outlineWidth: "2px",
            outlineStyle: "solid",
            outlineColor: tokens.colorStrokeFocus2,
            outlineOffset: "-2px",
        },
    },
    splitterDragging: {
        backgroundColor: tokens.colorBrandBackgroundPressed,
    },
    // Visual grip dots indicator (centered in the splitter)
    grip: {
        position: "absolute",
        top: "50%",
        left: "50%",
        transform: "translate(-50%, -50%)",
        display: "flex",
        flexDirection: "column",
        gap: "3px",
        pointerEvents: "none",
    },
    gripDot: {
        width: "3px",
        height: "3px",
        borderRadius: tokens.borderRadiusCircular,
        backgroundColor: tokens.colorNeutralForeground3,
    },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export function PanelSplitter({
    onMouseDown,
    onKeyDown,
    onDoubleClick,
    isDragging,
    currentRatio,
}: PanelSplitterProps): JSX.Element {
    const styles = useStyles();

    // Convert ratio (0-1) to integer percentage for ARIA
    const valueNow = Math.round(currentRatio * 100);

    return (
        <div
            className={mergeClasses(
                styles.splitter,
                isDragging && styles.splitterDragging,
            )}
            role="separator"
            aria-orientation="vertical"
            aria-valuenow={valueNow}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-label="Resize panels"
            tabIndex={0}
            onMouseDown={onMouseDown}
            onKeyDown={onKeyDown}
            onDoubleClick={onDoubleClick}
        >
            {/* Visual grip indicator — three dots */}
            <div className={styles.grip}>
                <div className={styles.gripDot} />
                <div className={styles.gripDot} />
                <div className={styles.gripDot} />
            </div>
        </div>
    );
}
