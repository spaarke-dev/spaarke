/**
 * FetchXmlService
 *
 * Executes FetchXML queries via Xrm.WebApi and parses layoutxml to column definitions.
 * Framework-agnostic: receives XrmContext as constructor argument.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-012, ADR-022
 */
import type { IFetchXmlResult, IFetchXmlOptions, IColumnDefinition, IFilterGroup } from '../types/FetchXmlTypes';
import type { XrmContext } from '../utils/xrmContext';
/**
 * Service for executing FetchXML queries and parsing layout XML.
 * Must be instantiated with an XrmContext for Dataverse access.
 */
export declare class FetchXmlService {
    private xrm;
    /**
     * Create a new FetchXmlService instance
     * @param xrm - XrmContext providing WebApi access
     */
    constructor(xrm: XrmContext);
    /**
     * Execute a FetchXML query and return typed results
     * @param entityLogicalName - Entity to query
     * @param fetchXml - FetchXML query string
     * @param options - Pagination and execution options
     * @returns Promise resolving to fetch results with entities and paging info
     */
    executeFetchXml<T extends Record<string, unknown>>(entityLogicalName: string, fetchXml: string, options?: IFetchXmlOptions): Promise<IFetchXmlResult<T>>;
    /**
     * Parse layoutxml into column definitions
     * @param layoutXml - Layout XML string from savedquery or sprk_gridconfiguration
     * @returns Array of column definitions
     */
    parseLayoutXml(layoutXml: string): IColumnDefinition[];
    /**
     * Merge additional filter conditions into existing FetchXML
     * @param fetchXml - Original FetchXML
     * @param filterGroup - Filter group to merge
     * @returns Modified FetchXML with merged filters
     */
    mergeFetchXmlFilter(fetchXml: string, filterGroup: IFilterGroup): string;
    /**
     * Extract entity logical name from FetchXML
     * @param fetchXml - FetchXML string
     * @returns Entity logical name or undefined
     */
    getEntityFromFetchXml(fetchXml: string): string | undefined;
    /**
     * Get attributes from FetchXML for field mapping
     * @param fetchXml - FetchXML string
     * @returns Array of attribute names
     */
    getAttributesFromFetchXml(fetchXml: string): string[];
    /**
     * Apply pagination attributes to FetchXML
     */
    private applyPagination;
    /**
     * Apply returntotalrecordcount attribute to FetchXML
     */
    private applyReturnTotalRecordCount;
    /**
     * Build filter XML element from filter group
     */
    private buildFilterXml;
    /**
     * Build condition XML element
     */
    private buildConditionXml;
    /**
     * Format condition value for FetchXML
     */
    private formatConditionValue;
}
export type { IFetchXmlResult, IFetchXmlOptions, IColumnDefinition, IFilterGroup, IFilterCondition, ColumnDataType, } from '../types/FetchXmlTypes';
//# sourceMappingURL=FetchXmlService.d.ts.map