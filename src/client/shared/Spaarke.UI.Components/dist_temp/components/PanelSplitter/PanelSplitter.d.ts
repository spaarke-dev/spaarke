/**
 * PanelSplitter Component
 *
 * A draggable, keyboard-accessible vertical splitter between resizable panels.
 * Renders a 4px grip area with hover/focus indicators and an ARIA role="separator"
 * for accessibility compliance.
 *
 * Features:
 * - Mouse drag to resize
 * - Keyboard resize (ArrowLeft / ArrowRight)
 * - Double-click to reset to default split
 * - ARIA separator role with aria-valuenow
 * - Fluent v9 design tokens for all styling (ADR-021)
 *
 * @see ADR-021 - Fluent UI v9 design system
 * @see ADR-012 - Shared component library
 */
import * as React from 'react';
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
export declare function PanelSplitter({ onMouseDown, onKeyDown, onDoubleClick, isDragging, currentRatio, }: PanelSplitterProps): JSX.Element;
//# sourceMappingURL=PanelSplitter.d.ts.map