/**
 * FetchXmlService
 *
 * Executes FetchXML queries via Xrm.WebApi and parses layoutxml to column definitions.
 * Framework-agnostic: receives XrmContext as constructor argument.
 *
 * @see docs/architecture/universal-dataset-grid-architecture.md
 * @see ADR-012, ADR-022
 */
/**
 * Service for executing FetchXML queries and parsing layout XML.
 * Must be instantiated with an XrmContext for Dataverse access.
 */
export class FetchXmlService {
    /**
     * Create a new FetchXmlService instance
     * @param xrm - XrmContext providing WebApi access
     */
    constructor(xrm) {
        this.xrm = xrm;
    }
    /**
     * Execute a FetchXML query and return typed results
     * @param entityLogicalName - Entity to query
     * @param fetchXml - FetchXML query string
     * @param options - Pagination and execution options
     * @returns Promise resolving to fetch results with entities and paging info
     */
    async executeFetchXml(entityLogicalName, fetchXml, options = {}) {
        const { pageSize = 50, pageNumber = 1, pagingCookie, returnTotalRecordCount = false, maxPageSize } = options;
        // Apply pagination to FetchXML
        let paginatedFetchXml = this.applyPagination(fetchXml, pageSize, pageNumber, pagingCookie);
        // Apply count attribute if total count requested
        if (returnTotalRecordCount) {
            paginatedFetchXml = this.applyReturnTotalRecordCount(paginatedFetchXml);
        }
        try {
            // Use Xrm.WebApi.retrieveMultipleRecords with FetchXML
            const result = await this.xrm.WebApi.retrieveMultipleRecords(entityLogicalName, `?fetchXml=${encodeURIComponent(paginatedFetchXml)}`);
            const entities = (result.entities || []);
            // Respect maxPageSize if specified
            const limitedEntities = maxPageSize ? entities.slice(0, maxPageSize) : entities;
            return {
                entities: limitedEntities,
                totalRecordCount: result['@Microsoft.Dynamics.CRM.totalrecordcount'],
                pagingCookie: result['@Microsoft.Dynamics.CRM.fetchxmlpagingcookie'],
                moreRecords: result['@odata.nextLink'] !== undefined || (result['@Microsoft.Dynamics.CRM.morerecords'] ?? false),
                fetchXml: paginatedFetchXml,
            };
        }
        catch (error) {
            console.error('[FetchXmlService] Execute failed:', error);
            throw error;
        }
    }
    /**
     * Parse layoutxml into column definitions
     * @param layoutXml - Layout XML string from savedquery or sprk_gridconfiguration
     * @returns Array of column definitions
     */
    parseLayoutXml(layoutXml) {
        if (!layoutXml) {
            return [];
        }
        try {
            const parser = new DOMParser();
            const doc = parser.parseFromString(layoutXml, 'text/xml');
            // Check for parse errors
            const parseError = doc.querySelector('parsererror');
            if (parseError) {
                console.error('[FetchXmlService] Layout XML parse error:', parseError.textContent);
                return [];
            }
            // Find all cell elements (columns)
            const cells = doc.querySelectorAll('cell');
            const columns = [];
            cells.forEach((cell, index) => {
                const name = cell.getAttribute('name');
                const widthAttr = cell.getAttribute('width');
                const isPrimaryAttr = cell.getAttribute('isfirstcell') === 'true';
                if (name) {
                    columns.push({
                        name,
                        width: widthAttr ? parseInt(widthAttr, 10) : 100,
                        isPrimary: isPrimaryAttr || index === 0,
                        index,
                    });
                }
            });
            return columns;
        }
        catch (error) {
            console.error('[FetchXmlService] parseLayoutXml error:', error);
            return [];
        }
    }
    /**
     * Merge additional filter conditions into existing FetchXML
     * @param fetchXml - Original FetchXML
     * @param filterGroup - Filter group to merge
     * @returns Modified FetchXML with merged filters
     */
    mergeFetchXmlFilter(fetchXml, filterGroup) {
        if (!fetchXml || (!filterGroup.conditions.length && !filterGroup.filters?.length)) {
            return fetchXml;
        }
        try {
            const parser = new DOMParser();
            const doc = parser.parseFromString(fetchXml, 'text/xml');
            // Find entity element
            const entityElement = doc.querySelector('entity');
            if (!entityElement) {
                console.warn('[FetchXmlService] No entity element found in FetchXML');
                return fetchXml;
            }
            // Build the filter XML
            const filterXml = this.buildFilterXml(doc, filterGroup);
            // Find existing filter or create new one
            let existingFilter = entityElement.querySelector(':scope > filter');
            if (existingFilter) {
                // Wrap existing filter and new filter in an AND group
                const andFilter = doc.createElement('filter');
                andFilter.setAttribute('type', 'and');
                // Clone existing filter content
                const existingClone = existingFilter.cloneNode(true);
                andFilter.appendChild(existingClone);
                andFilter.appendChild(filterXml);
                existingFilter.parentNode?.replaceChild(andFilter, existingFilter);
            }
            else {
                // Add new filter directly to entity
                entityElement.appendChild(filterXml);
            }
            // Serialize back to string
            const serializer = new XMLSerializer();
            return serializer.serializeToString(doc);
        }
        catch (error) {
            console.error('[FetchXmlService] mergeFetchXmlFilter error:', error);
            return fetchXml;
        }
    }
    /**
     * Extract entity logical name from FetchXML
     * @param fetchXml - FetchXML string
     * @returns Entity logical name or undefined
     */
    getEntityFromFetchXml(fetchXml) {
        try {
            const parser = new DOMParser();
            const doc = parser.parseFromString(fetchXml, 'text/xml');
            const entityElement = doc.querySelector('entity');
            return entityElement?.getAttribute('name') ?? undefined;
        }
        catch {
            return undefined;
        }
    }
    /**
     * Get attributes from FetchXML for field mapping
     * @param fetchXml - FetchXML string
     * @returns Array of attribute names
     */
    getAttributesFromFetchXml(fetchXml) {
        try {
            const parser = new DOMParser();
            const doc = parser.parseFromString(fetchXml, 'text/xml');
            const attributes = doc.querySelectorAll('attribute');
            const names = [];
            attributes.forEach(attr => {
                const name = attr.getAttribute('name');
                if (name) {
                    names.push(name);
                }
            });
            return names;
        }
        catch {
            return [];
        }
    }
    // ─────────────────────────────────────────────────────────────────────────────
    // Private helper methods
    // ─────────────────────────────────────────────────────────────────────────────
    /**
     * Apply pagination attributes to FetchXML
     */
    applyPagination(fetchXml, pageSize, pageNumber, pagingCookie) {
        try {
            const parser = new DOMParser();
            const doc = parser.parseFromString(fetchXml, 'text/xml');
            const fetchElement = doc.querySelector('fetch');
            if (fetchElement) {
                fetchElement.setAttribute('count', String(pageSize));
                fetchElement.setAttribute('page', String(pageNumber));
                if (pagingCookie) {
                    fetchElement.setAttribute('paging-cookie', pagingCookie);
                }
            }
            const serializer = new XMLSerializer();
            return serializer.serializeToString(doc);
        }
        catch {
            return fetchXml;
        }
    }
    /**
     * Apply returntotalrecordcount attribute to FetchXML
     */
    applyReturnTotalRecordCount(fetchXml) {
        try {
            const parser = new DOMParser();
            const doc = parser.parseFromString(fetchXml, 'text/xml');
            const fetchElement = doc.querySelector('fetch');
            if (fetchElement) {
                fetchElement.setAttribute('returntotalrecordcount', 'true');
            }
            const serializer = new XMLSerializer();
            return serializer.serializeToString(doc);
        }
        catch {
            return fetchXml;
        }
    }
    /**
     * Build filter XML element from filter group
     */
    buildFilterXml(doc, filterGroup) {
        const filterElement = doc.createElement('filter');
        filterElement.setAttribute('type', filterGroup.type);
        // Add conditions
        for (const condition of filterGroup.conditions) {
            const conditionElement = this.buildConditionXml(doc, condition);
            filterElement.appendChild(conditionElement);
        }
        // Add nested filters
        if (filterGroup.filters) {
            for (const nestedFilter of filterGroup.filters) {
                const nestedElement = this.buildFilterXml(doc, nestedFilter);
                filterElement.appendChild(nestedElement);
            }
        }
        return filterElement;
    }
    /**
     * Build condition XML element
     */
    buildConditionXml(doc, condition) {
        const conditionElement = doc.createElement('condition');
        conditionElement.setAttribute('attribute', condition.attribute);
        conditionElement.setAttribute('operator', condition.operator);
        if (condition.value !== undefined && condition.value !== null) {
            conditionElement.setAttribute('value', this.formatConditionValue(condition.value));
        }
        // Handle 'in' operator with multiple values
        if (condition.values && condition.values.length > 0) {
            for (const value of condition.values) {
                const valueElement = doc.createElement('value');
                valueElement.textContent = String(value);
                conditionElement.appendChild(valueElement);
            }
        }
        return conditionElement;
    }
    /**
     * Format condition value for FetchXML
     */
    formatConditionValue(value) {
        if (value instanceof Date) {
            return value.toISOString();
        }
        return String(value);
    }
}
//# sourceMappingURL=FetchXmlService.js.map