/**
 * ListView - Compact list layout for simple records
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */
import * as React from 'react';
import { IDatasetRecord, IDatasetColumn, ScrollBehavior } from '../../types';
export interface IListViewProps {
    records: IDatasetRecord[];
    columns: IDatasetColumn[];
    selectedRecordIds: string[];
    onSelectionChange: (selectedIds: string[]) => void;
    onRecordClick: (record: IDatasetRecord) => void;
    scrollBehavior: ScrollBehavior;
    loading: boolean;
    hasNextPage: boolean;
    loadNextPage: () => void;
    enableVirtualization?: boolean;
}
export declare const ListView: React.FC<IListViewProps>;
//# sourceMappingURL=ListView.d.ts.map