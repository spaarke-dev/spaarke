/**
 * PlaybookCardGrid Component
 *
 * Responsive card grid selector for AI playbooks (Code Page / React 18).
 * Ported from PCF PlaybookSelector with a full grid layout replacing the
 * horizontal scroll strip used in the narrow PCF context.
 *
 * Features:
 * - Responsive CSS Grid: 3 columns (wide) → 2 (medium) → 1 (narrow)
 * - Selected card highlight using Fluent v9 brand tokens
 * - Info icon Popover for full description on hover/click
 * - Loading state (Fluent Spinner) and empty state message
 * - Zero hard-coded colors — all Fluent v9 semantic tokens
 */
import React from 'react';
import { IPlaybook } from './types';
export interface IPlaybookCardGridProps {
    playbooks: IPlaybook[];
    selectedId?: string;
    onSelect: (playbook: IPlaybook) => void;
    isLoading: boolean;
    /** Compact mode: smaller cards with icon + name only, description in popover. */
    compact?: boolean;
}
export declare const PlaybookCardGrid: React.FC<IPlaybookCardGridProps>;
export default PlaybookCardGrid;
//# sourceMappingURL=PlaybookCardGrid.d.ts.map