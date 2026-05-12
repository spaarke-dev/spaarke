/**
 * ViewToolbar Component
 *
 * Horizontal toolbar row below the CommandBar containing the ViewSelector
 * and optional Edit filters / Edit columns buttons.
 * Styled to match OOB Power Apps view toolbar.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-021 Fluent UI v9 Design System
 */
import * as React from 'react';
/**
 * Props for ViewToolbar component
 */
export interface IViewToolbarProps {
    /** Children - typically ViewSelector component */
    children?: React.ReactNode;
    /** View name to display (when not using children) */
    viewName?: string;
    /** Record count to display */
    recordCount?: number;
    /** Show "Edit filters" button */
    showEditFilters?: boolean;
    /** Show "Edit columns" button */
    showEditColumns?: boolean;
    /** Handler for "Edit filters" click */
    onEditFilters?: () => void;
    /** Handler for "Edit columns" click */
    onEditColumns?: () => void;
    /** Handler for view dropdown click (when using viewName instead of children) */
    onViewClick?: () => void;
    /** Compact mode */
    compact?: boolean;
    /** Additional CSS class */
    className?: string;
}
/**
 * ViewToolbar - Horizontal bar below CommandBar with view selector and actions
 */
export declare const ViewToolbar: React.FC<IViewToolbarProps>;
export default ViewToolbar;
//# sourceMappingURL=ViewToolbar.d.ts.map