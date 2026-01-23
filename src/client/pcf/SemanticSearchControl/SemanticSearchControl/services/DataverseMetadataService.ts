/**
 * DataverseMetadataService
 *
 * Service for fetching optionset values from Dataverse.
 * Retrieves Document Type, Matter Type, and File Type options for filters.
 *
 * @see spec.md for filter requirements
 */

import { FilterOption } from "../types";

/**
 * Xrm.WebApi response types
 */
interface OptionMetadata {
    Value: number;
    Label: {
        UserLocalizedLabel: {
            Label: string;
        };
    };
}

interface OptionSetMetadataResponse {
    Options: OptionMetadata[];
}

/**
 * Cache entry for optionset values
 */
interface CacheEntry {
    options: FilterOption[];
    timestamp: number;
}

/**
 * Cache duration in milliseconds (5 minutes)
 */
const CACHE_DURATION_MS = 5 * 60 * 1000;

/**
 * Static file type options (common document types)
 */
const STATIC_FILE_TYPES: FilterOption[] = [
    { key: "pdf", label: "PDF" },
    { key: "doc", label: "Word (DOC)" },
    { key: "docx", label: "Word (DOCX)" },
    { key: "xls", label: "Excel (XLS)" },
    { key: "xlsx", label: "Excel (XLSX)" },
    { key: "ppt", label: "PowerPoint (PPT)" },
    { key: "pptx", label: "PowerPoint (PPTX)" },
    { key: "txt", label: "Text (TXT)" },
    { key: "msg", label: "Email (MSG)" },
    { key: "eml", label: "Email (EML)" },
];

/**
 * Service for fetching Dataverse metadata
 */
export class DataverseMetadataService {
    private cache: Map<string, CacheEntry> = new Map();

    /**
     * Get document type options from Dataverse optionset
     * @param entityName - Entity logical name (default: sprk_document)
     * @param attributeName - Attribute logical name (default: sprk_documenttype)
     */
    async getDocumentTypeOptions(
        entityName = "sprk_document",
        attributeName = "sprk_documenttype"
    ): Promise<FilterOption[]> {
        return this.getOptionSetValues(entityName, attributeName);
    }

    /**
     * Get matter type options from Dataverse lookup entity
     * Matter Type is a lookup to sprk_mattertype_ref entity, not a picklist.
     * @param entitySetName - OData entity set name (default: sprk_mattertype_refs)
     * @param nameAttribute - Attribute containing the display name (default: sprk_mattertypename)
     */
    async getMatterTypeOptions(
        entitySetName = "sprk_mattertype_refs",
        nameAttribute = "sprk_mattertypename"
    ): Promise<FilterOption[]> {
        return this.getLookupOptions(entitySetName, nameAttribute);
    }

    /**
     * Get file type options (static list)
     * File types are not stored in Dataverse optionsets.
     */
    async getFileTypeOptions(): Promise<FilterOption[]> {
        return STATIC_FILE_TYPES;
    }

    /**
     * Fetch optionset values from Dataverse
     */
    private async getOptionSetValues(
        entityName: string,
        attributeName: string
    ): Promise<FilterOption[]> {
        const cacheKey = `${entityName}.${attributeName}`;

        // Check cache
        const cached = this.cache.get(cacheKey);
        if (cached && Date.now() - cached.timestamp < CACHE_DURATION_MS) {
            return cached.options;
        }

        try {
            // Check if Xrm is available
            if (typeof Xrm === "undefined" || !Xrm.WebApi) {
                console.warn("Xrm.WebApi not available, returning empty options");
                return [];
            }

            // Fetch optionset metadata
            const metadata = await this.fetchOptionSetMetadata(
                entityName,
                attributeName
            );

            // Map to FilterOption format
            const options: FilterOption[] = metadata.Options.map((opt) => ({
                key: opt.Value.toString(),
                label: opt.Label.UserLocalizedLabel?.Label ?? `Value ${opt.Value}`,
            }));

            // Sort alphabetically by label
            options.sort((a, b) => a.label.localeCompare(b.label));

            // Cache the result
            this.cache.set(cacheKey, {
                options,
                timestamp: Date.now(),
            });

            return options;
        } catch (error) {
            console.error(
                `Failed to fetch optionset ${entityName}.${attributeName}:`,
                error
            );

            // Return cached value if available (even if stale)
            if (cached) {
                return cached.options;
            }

            // Return empty array on error
            return [];
        }
    }

    /**
     * Fetch lookup entity records from Dataverse
     * Used for lookup fields where options come from another entity's records.
     */
    private async getLookupOptions(
        entitySetName: string,
        nameAttribute: string
    ): Promise<FilterOption[]> {
        const cacheKey = `lookup:${entitySetName}`;

        // Check cache
        const cached = this.cache.get(cacheKey);
        if (cached && Date.now() - cached.timestamp < CACHE_DURATION_MS) {
            return cached.options;
        }

        try {
            const clientUrl = this.getClientUrl();

            // Query the lookup target entity for all active records
            // Assumes primary key follows pattern: {entitylogicalname}id
            const entityLogicalName = entitySetName.endsWith("s")
                ? entitySetName.slice(0, -1)
                : entitySetName;
            const primaryKey = `${entityLogicalName}id`;

            const queryUrl = `${clientUrl}/api/data/v9.2/${entitySetName}?$select=${primaryKey},${nameAttribute}&$filter=statecode eq 0&$orderby=${nameAttribute} asc`;

            const response = await fetch(queryUrl, {
                headers: {
                    Accept: "application/json",
                    "OData-MaxVersion": "4.0",
                    "OData-Version": "4.0",
                },
            });

            // Handle 404 gracefully - entity doesn't exist yet
            if (response.status === 404) {
                console.info(
                    `Lookup entity ${entitySetName} not found`
                );
                return [];
            }

            if (!response.ok) {
                throw new Error(`Failed to fetch lookup options: ${response.status}`);
            }

            const data = await response.json();

            // Map records to FilterOption format
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            const options: FilterOption[] = (data.value || []).map((record: Record<string, any>) => {
                const keyValue = record[primaryKey];
                const nameValue = record[nameAttribute];
                return {
                    key: typeof keyValue === "string" ? keyValue : "",
                    label: typeof nameValue === "string" ? nameValue : "Unknown",
                };
            });

            // Cache the result
            this.cache.set(cacheKey, {
                options,
                timestamp: Date.now(),
            });

            return options;
        } catch (error) {
            console.error(
                `Failed to fetch lookup options from ${entitySetName}:`,
                error
            );

            // Return cached value if available (even if stale)
            if (cached) {
                return cached.options;
            }

            return [];
        }
    }

    /**
     * Fetch optionset metadata from Dataverse
     * Uses direct fetch to the metadata API since Xrm.WebApi.retrieveMultipleRecords
     * doesn't work with metadata entities like EntityDefinitions.
     */
    private async fetchOptionSetMetadata(
        entityName: string,
        attributeName: string
    ): Promise<OptionSetMetadataResponse> {
        // Go directly to metadata API - Xrm.WebApi doesn't support metadata entities
        return this.fetchMetadataDirectly(entityName, attributeName);
    }

    /**
     * Direct fetch of optionset metadata
     */
    private async fetchMetadataDirectly(
        entityName: string,
        attributeName: string
    ): Promise<OptionSetMetadataResponse> {
        // Use the Xrm.Utility.getGlobalContext for client URL
        const clientUrl = this.getClientUrl();

        const metadataUrl = `${clientUrl}/api/data/v9.2/EntityDefinitions(LogicalName='${entityName}')/Attributes(LogicalName='${attributeName}')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$expand=OptionSet($select=Options)`;

        const response = await fetch(metadataUrl, {
            headers: {
                Accept: "application/json",
                "OData-MaxVersion": "4.0",
                "OData-Version": "4.0",
            },
        });

        // Handle 404 gracefully - entity or attribute doesn't exist yet
        if (response.status === 404) {
            console.info(
                `Optionset ${entityName}.${attributeName} not found (entity or attribute may not exist yet)`
            );
            return { Options: [] };
        }

        if (!response.ok) {
            throw new Error(`Failed to fetch metadata: ${response.status}`);
        }

        const data = await response.json();

        // Extract options from the OptionSet property
        if (data.OptionSet && Array.isArray(data.OptionSet.Options)) {
            return { Options: data.OptionSet.Options };
        }

        // If OptionSet is directly on the response
        if (Array.isArray(data.Options)) {
            return { Options: data.Options };
        }

        // No options found - return empty array
        return { Options: [] };
    }

    /**
     * Get the Dataverse client URL
     */
    private getClientUrl(): string {
        if (typeof Xrm !== "undefined" && Xrm.Utility?.getGlobalContext) {
            return Xrm.Utility.getGlobalContext().getClientUrl();
        }

        // Fallback to current origin
        return window.location.origin;
    }

    /**
     * Clear the cache (useful for testing or forced refresh)
     */
    clearCache(): void {
        this.cache.clear();
    }
}

export default DataverseMetadataService;
