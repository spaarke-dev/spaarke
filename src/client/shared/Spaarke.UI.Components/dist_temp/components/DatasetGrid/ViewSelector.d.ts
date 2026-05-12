/**
 * ViewSelector Component
 *
 * Fluent UI v9 dropdown for selecting views from savedquery and custom configurations.
 * Styled to match OOB Power Apps view selector.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-021 Fluent UI v9 Design System
 */
import * as React from 'react';
import type { IViewDefinition } from '../../types/FetchXmlTypes';
import type { XrmContext } from '../../utils/xrmContext';
/**
 * Props for ViewSelector component
 */
export interface IViewSelectorProps {
    /** Xrm context for ViewService */
    xrm: XrmContext;
    /** Entity logical name to fetch views for */
    entityLogicalName: string;
    /** Currently selected view ID */
    selectedViewId?: string;
    /** Default view name to show when loading */
    defaultViewName?: string;
    /** Callback when view selection changes */
    onViewChange?: (view: IViewDefinition) => void;
    /** Include custom views from sprk_gridconfiguration */
    includeCustomViews?: boolean;
    /** Include personal views from userquery */
    includePersonalViews?: boolean;
    /** Group views by type in dropdown */
    groupByType?: boolean;
    /** Additional CSS class */
    className?: string;
    /** Compact mode (smaller size) */
    compact?: boolean;
    /** Disabled state */
    disabled?: boolean;
    /** Placeholder text */
    placeholder?: string;
}
/**
 * ViewSelector - Dropdown for selecting entity views
 */
export declare const ViewSelector: React.FC<IViewSelectorProps>;
export default ViewSelector;
//# sourceMappingURL=ViewSelector.d.ts.map