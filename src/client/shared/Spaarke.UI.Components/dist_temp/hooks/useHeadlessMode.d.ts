/**
 * useHeadlessMode - Fetch data via Web API using FetchXML
 * Used in custom pages where no dataset binding exists
 */
import { IDatasetResult } from './types';
export interface IUseHeadlessModeProps {
    webAPI: ComponentFramework.WebApi;
    entityName: string;
    fetchXml?: string;
    pageSize: number;
    autoLoad?: boolean;
}
export declare function useHeadlessMode(props: IUseHeadlessModeProps): IDatasetResult;
//# sourceMappingURL=useHeadlessMode.d.ts.map