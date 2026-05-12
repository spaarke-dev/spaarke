import * as React from 'react';
import { IDatasetRecord, IDatasetColumn } from '../../types/DatasetTypes';
export interface VirtualizedListViewProps {
    records: IDatasetRecord[];
    columns: IDatasetColumn[];
    selectedRecordIds: string[];
    itemHeight: number;
    overscanCount: number;
    onRecordClick?: (recordId: string) => void;
}
export declare const VirtualizedListView: React.FC<VirtualizedListViewProps>;
//# sourceMappingURL=VirtualizedListView.d.ts.map