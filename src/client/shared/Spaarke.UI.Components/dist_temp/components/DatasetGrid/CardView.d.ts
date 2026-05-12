/**
 * CardView - Tile/Card layout for visual content
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md
 */
import * as React from 'react';
import { IDatasetRecord, IDatasetColumn, ScrollBehavior } from '../../types';
export interface ICardViewProps {
    records: IDatasetRecord[];
    columns: IDatasetColumn[];
    selectedRecordIds: string[];
    onSelectionChange: (selectedIds: string[]) => void;
    onRecordClick: (record: IDatasetRecord) => void;
    scrollBehavior: ScrollBehavior;
    loading: boolean;
    hasNextPage: boolean;
    loadNextPage: () => void;
}
export declare const CardView: React.FC<ICardViewProps>;
//# sourceMappingURL=CardView.d.ts.map