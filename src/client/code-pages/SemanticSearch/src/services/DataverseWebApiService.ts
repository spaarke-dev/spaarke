/**
 * DataverseWebApiService — Direct WebAPI access for Code Pages
 *
 * Code Pages cannot use PCF context.webAPI. Instead, this service uses
 * direct fetch() to the Dataverse OData WebAPI for optionset metadata
 * and lookup entity records.
 *
 * Module-level cache ensures options are fetched once per session.
 *
 * @see DataverseMetadataService.ts (PCF version) for reference
 */

import type { FilterOption } from "../types";

// =============================================
// Module-level cache (singleton pattern)
// =============================================

const cache = new Map<string, FilterOption[]>();

// =============================================
// Static file types (not stored in Dataverse)
// =============================================

const STATIC_FILE_TYPES: FilterOption[] = [
    { value: "pdf", label: "PDF" },
    { value: "doc", label: "Word (DOC)" },
    { value: "docx", label: "Word (DOCX)" },
    { value: "xls", label: "Excel (XLS)" },
    { value: "xlsx", label: "Excel (XLSX)" },
    { value: "ppt", label: "PowerPoint (PPT)" },
    { value: "pptx", label: "PowerPoint (PPTX)" },
    { value: "txt", label: "Text (TXT)" },
    { value: "msg", label: "Email (MSG)" },
    { value: "eml", label: "Email (EML)" },
];

// =============================================
// OData response types
// =============================================

interface OptionMetadata {
    Value: number;
    Label: {
        UserLocalizedLabel: {
            Label: string;
        };
    };
}

// =============================================
// Helpers
// =============================================

/**
 * Get the Dataverse org URL from Xrm context, with fallback to window.location.origin.
 */
export function getOrgUrl(): string {
    try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm;
        if (xrm?.Utility?.getGlobalContext) {
            return xrm.Utility.getGlobalContext().getClientUrl();
        }
    } catch {
        /* ignore — Xrm not available */
    }
    return window.location.origin;
}

/** Standard OData headers for Dataverse WebAPI requests. */
const ODATA_HEADERS: HeadersInit = {
    Accept: "application/json",
    "OData-MaxVersion": "4.0",
    "OData-Version": "4.0",
};

// =============================================
// Public API
// =============================================

/**
 * Fetch optionset values from Dataverse EntityDefinitions metadata API.
 *
 * Queries: /api/data/v9.2/EntityDefinitions(LogicalName='{entityName}')
 *   /Attributes(LogicalName='{attributeName}')
 *   /Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$expand=OptionSet($select=Options)
 */
export async function fetchOptionsetValues(
    entityName: string,
    attributeName: string
): Promise<FilterOption[]> {
    const cacheKey = `optionset:${entityName}.${attributeName}`;
    const cached = cache.get(cacheKey);
    if (cached) return cached;

    try {
        const url =
            `${getOrgUrl()}/api/data/v9.2/EntityDefinitions(LogicalName='${entityName}')` +
            `/Attributes(LogicalName='${attributeName}')` +
            `/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?$expand=OptionSet($select=Options)`;

        const response = await fetch(url, { headers: ODATA_HEADERS });

        if (response.status === 404) {
            // Entity or attribute doesn't exist yet
            return [];
        }

        if (!response.ok) {
            console.error(`Failed to fetch optionset ${entityName}.${attributeName}: ${response.status}`);
            return [];
        }

        const data = await response.json();

        // Extract options from OptionSet.Options or top-level Options
        const rawOptions: OptionMetadata[] =
            data.OptionSet?.Options ?? data.Options ?? [];

        const options: FilterOption[] = rawOptions
            .map((opt: OptionMetadata) => ({
                value: opt.Value.toString(),
                label: opt.Label?.UserLocalizedLabel?.Label ?? `Value ${opt.Value}`,
            }))
            .sort((a: FilterOption, b: FilterOption) => a.label.localeCompare(b.label));

        cache.set(cacheKey, options);
        return options;
    } catch (error) {
        console.error(`Failed to fetch optionset ${entityName}.${attributeName}:`, error);
        return [];
    }
}

/**
 * Fetch lookup entity records from Dataverse.
 * Used for lookup fields where options come from another entity's records.
 *
 * Queries: /api/data/v9.2/{entitySetName}?$select={nameAttribute}&$filter=statecode eq 0&$orderby={nameAttribute} asc
 */
export async function fetchLookupValues(
    entitySetName: string,
    nameAttribute: string
): Promise<FilterOption[]> {
    const cacheKey = `lookup:${entitySetName}`;
    const cached = cache.get(cacheKey);
    if (cached) return cached;

    try {
        const entityLogicalName = entitySetName.endsWith("s")
            ? entitySetName.slice(0, -1)
            : entitySetName;
        const primaryKey = `${entityLogicalName}id`;

        const url =
            `${getOrgUrl()}/api/data/v9.2/${entitySetName}` +
            `?$select=${primaryKey},${nameAttribute}` +
            `&$filter=statecode eq 0` +
            `&$orderby=${nameAttribute} asc`;

        const response = await fetch(url, { headers: ODATA_HEADERS });

        if (response.status === 404) {
            return [];
        }

        if (!response.ok) {
            console.error(`Failed to fetch lookup options from ${entitySetName}: ${response.status}`);
            return [];
        }

        const data = await response.json();

        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const options: FilterOption[] = (data.value || []).map((record: Record<string, any>) => ({
            value: typeof record[primaryKey] === "string" ? record[primaryKey] : "",
            label: typeof record[nameAttribute] === "string" ? record[nameAttribute] : "Unknown",
        }));

        cache.set(cacheKey, options);
        return options;
    } catch (error) {
        console.error(`Failed to fetch lookup options from ${entitySetName}:`, error);
        return [];
    }
}

/**
 * Get static file type options (not stored in Dataverse optionsets).
 */
export function getFileTypeOptions(): FilterOption[] {
    return STATIC_FILE_TYPES;
}

/**
 * Clear all cached option values. Call before refresh.
 */
export function clearCache(): void {
    cache.clear();
}
