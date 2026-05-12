/**
 * UniversalDatasetGrid - Main component for dataset display
 * Routes to GridView, CardView, or ListView based on configuration
 * Standards: KM-UX-FLUENT-DESIGN-V9-STANDARDS.md, ADR-012
 */
import * as React from 'react';
import { IDatasetConfig } from '../../types';
export interface IUniversalDatasetGridProps {
    config?: IDatasetConfig;
    configJson?: string;
    dataset?: ComponentFramework.PropertyTypes.DataSet;
    headlessConfig?: {
        webAPI: ComponentFramework.WebApi;
        entityName: string;
        fetchXml?: string;
        pageSize: number;
    };
    selectedRecordIds: string[];
    onSelectionChange: (selectedIds: string[]) => void;
    onRecordClick: (recordId: string) => void;
    context: any;
}
export declare const UniversalDatasetGrid: React.FC<IUniversalDatasetGridProps>;
//# sourceMappingURL=UniversalDatasetGrid.d.ts.map