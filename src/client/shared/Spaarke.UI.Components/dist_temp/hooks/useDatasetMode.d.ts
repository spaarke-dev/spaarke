/**
 * useDatasetMode - Extract data from PCF dataset binding
 * Used in model-driven apps where Power Platform provides the dataset
 */
import { IDatasetResult } from './types';
export interface IUseDatasetModeProps {
    dataset: ComponentFramework.PropertyTypes.DataSet;
}
export declare function useDatasetMode(props: IUseDatasetModeProps): IDatasetResult;
//# sourceMappingURL=useDatasetMode.d.ts.map