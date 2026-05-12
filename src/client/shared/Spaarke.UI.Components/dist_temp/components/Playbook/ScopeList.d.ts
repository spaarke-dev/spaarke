/**
 * ScopeList Component
 *
 * Generic checkbox/radio list for selecting scope items (actions, skills,
 * knowledge, tools). Supports multi-select (Checkbox) and single-select
 * (RadioGroup) modes, as well as a read-only locked state.
 *
 * Ported from src/client/pcf/AnalysisBuilder/control/components/ScopeList.tsx
 * and adapted for React 18 / Code Page usage with external selectedIds state.
 */
import React from 'react';
import { IScopeItem } from './types';
export interface IScopeListProps<T extends IScopeItem> {
    items: T[];
    selectedIds: string[];
    onSelectionChange: (selectedIds: string[]) => void;
    isLoading: boolean;
    /** When true, render Radio inputs (single select). Default: true (multi-select). */
    multiSelect?: boolean;
    /** Message shown when items array is empty. Default: "No items available". */
    emptyMessage?: string;
    /** When true, all inputs are disabled (scopes are locked). Default: false. */
    readOnly?: boolean;
}
export declare function ScopeList<T extends IScopeItem>({ items, selectedIds, onSelectionChange, isLoading, multiSelect, emptyMessage, readOnly, }: IScopeListProps<T>): React.ReactElement;
export default ScopeList;
//# sourceMappingURL=ScopeList.d.ts.map