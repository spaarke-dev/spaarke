/**
 * CommandBar Component
 *
 * OOB-style command bar for Custom Pages.
 * Matches Power Apps entity homepage ribbon styling.
 *
 * Features:
 * - Standard commands: New, Delete, Refresh
 * - Custom commands via props
 * - Selection-aware delete button
 * - Optional search box
 * - Dark mode support
 * - Keyboard shortcuts
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-021 Fluent UI v9 Design System
 */
import * as React from 'react';
/**
 * Command item for custom commands
 */
export interface ICommandBarItem {
    /** Unique key for the command */
    key: string;
    /** Display label */
    label: string;
    /** Fluent icon element */
    icon?: React.ReactElement;
    /** Whether command is disabled */
    disabled?: boolean;
    /** Tooltip description */
    description?: string;
    /** Click handler */
    onClick?: () => void;
    /** Show divider after this command */
    dividerAfter?: boolean;
}
/**
 * Props for CommandBar component
 */
export interface ICommandBarProps {
    /** Entity logical name for context */
    entityLogicalName: string;
    /** Currently selected record IDs */
    selectedIds?: string[];
    /** Custom commands to render */
    commands?: ICommandBarItem[];
    /** Handler for New button */
    onNew?: () => void;
    /** Handler for Delete button */
    onDelete?: (selectedIds: string[]) => void;
    /** Handler for Refresh button */
    onRefresh?: () => void;
    /** Handler for search */
    onSearch?: (searchText: string) => void;
    /** Show the New button (default: true) */
    showNew?: boolean;
    /** Show the Delete button (default: true) */
    showDelete?: boolean;
    /** Show the Refresh button (default: true) */
    showRefresh?: boolean;
    /** Show the Search box (default: false) */
    showSearch?: boolean;
    /** Search placeholder text */
    searchPlaceholder?: string;
    /** Whether New action is allowed (security) */
    canCreate?: boolean;
    /** Whether Delete action is allowed (security) */
    canDelete?: boolean;
    /** Compact mode (smaller height) */
    compact?: boolean;
    /** Additional CSS class name */
    className?: string;
}
/**
 * CommandBar - OOB-style command bar for Power Apps Custom Pages
 */
export declare const CommandBar: React.FC<ICommandBarProps>;
export default CommandBar;
//# sourceMappingURL=CommandBar.d.ts.map