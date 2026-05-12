/**
 * GridView - Table layout using Fluent UI DataGrid with infinite scroll
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */
import * as React from 'react';
import { IDatasetRecord, IDatasetColumn, ScrollBehavior } from '../../types';
export interface IGridViewProps {
    records: IDatasetRecord[];
    columns: IDatasetColumn[];
    selectedRecordIds: string[];
    onSelectionChange: (selectedIds: string[]) => void;
    onRecordClick: (record: IDatasetRecord) => void;
    enableVirtualization: boolean;
    rowHeight: number;
    scrollBehavior: ScrollBehavior;
    loading: boolean;
    hasNextPage: boolean;
    loadNextPage: () => void;
}
export declare const GridView: React.FC<IGridViewProps>;
//# sourceMappingURL=GridView.d.ts.map