import * as React from 'react';
import { IDatasetRecord, IDatasetColumn } from '../../types/DatasetTypes';
export interface VirtualizedGridViewProps {
    records: IDatasetRecord[];
    columns: IDatasetColumn[];
    selectedRecordIds: string[];
    itemHeight: number;
    overscanCount: number;
    onRecordClick?: (recordId: string) => void;
}
export declare const VirtualizedGridView: React.FC<VirtualizedGridViewProps>;
//# sourceMappingURL=VirtualizedGridView.d.ts.map