/**
 * useSearchViewDefinitions — Fetch grid view/column definitions from sprk_gridconfiguration.
 *
 * Queries Dataverse for view definitions stored as JSON in sprk_configjson,
 * with _type="semantic-search-view" discriminator. Falls back to hardcoded
 * domainColumns.ts when no Dataverse records exist (or Xrm is unavailable).
 *
 * @see notes/universal-dataset-grid-adaptation-spec.md § 5 — View Definitions
 * @see config/domainColumns.ts — fallback column definitions
 */

import { useState, useEffect, useCallback, useMemo } from "react";
import type { SearchDomain, GridColumnDef } from "../types";
import { getColumnsForDomain } from "../config/domainColumns";

// =============================================
// Types
// =============================================

/** Column definition as stored in sprk_configjson. */
interface IViewColumnDef {
    key: string;
    label: string;
    width?: number;
    dataType?: string;
    sortable?: boolean;
    isPrimary?: boolean;
}

/** Parsed view definition from sprk_gridconfiguration record. */
export interface SearchViewDefinition {
    id: string;
    name: string;
    domain: SearchDomain;
    columns: IViewColumnDef[];
    defaultSort?: { column: string; direction: "asc" | "desc" };
    isDefault: boolean;
    sortOrder: number;
}

/** Shape compatible with IDatasetColumn from @spaarke/ui-components. */
export interface IDatasetColumn {
    name: string;
    displayName: string;
    dataType: string;
    isKey?: boolean;
    isPrimary?: boolean;
    visualSizeFactor?: number;
}

/** Hook return value. */
export interface UseSearchViewDefinitionsResult {
    /** Parsed view definitions for the active domain. */
    views: SearchViewDefinition[];
    /** Currently active view (default or user-selected). */
    activeView: SearchViewDefinition | null;
    /** Set the active view by ID. */
    setActiveView: (viewId: string) => void;
    /** IDatasetColumn[] for the active view — ready for UniversalDatasetGrid. */
    columns: IDatasetColumn[];
    /** Whether views are loading from Dataverse. */
    isLoading: boolean;
    /** Error message, if any. */
    error: string | null;
}

// =============================================
// Constants
// =============================================

const ENTITY_SET = "sprk_gridconfigurations";
const VIEW_TYPE = "semantic-search-view";
const CACHE_KEY_PREFIX = "sprk_searchViewDefs_";
const CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes

const ODATA_HEADERS: HeadersInit = {
    Accept: "application/json",
    "Content-Type": "application/json",
    "OData-MaxVersion": "4.0",
    "OData-Version": "4.0",
};

// =============================================
// Helpers
// =============================================

/** Resolve render hint or explicit dataType to a DataverseAttributeType/ExtendedDataType. */
function resolveDataType(col: IViewColumnDef): string {
    if (col.dataType) return col.dataType;
    return "SingleLine.Text";
}

/** Convert IViewColumnDef → IDatasetColumn. */
function toDatasetColumn(col: IViewColumnDef): IDatasetColumn {
    return {
        name: col.key,
        displayName: col.label,
        dataType: resolveDataType(col),
        isPrimary: col.isPrimary,
        visualSizeFactor: col.width ? col.width / 100 : undefined,
    };
}

/** Convert GridColumnDef (from domainColumns.ts fallback) → IDatasetColumn. */
function fallbackToDatasetColumn(col: GridColumnDef): IDatasetColumn {
    // Infer dataType from the presence of a render function
    let dataType = "SingleLine.Text";
    // Use the column key to detect known types
    if (col.key === "combinedScore" || col.key === "confidenceScore") {
        dataType = "Percentage";
    } else if (col.key === "updatedAt" || col.key === "modifiedAt" || col.key === "createdAt") {
        dataType = "DateAndTime.DateOnly";
    } else if (col.key === "fileType") {
        dataType = "FileType";
    } else if (col.key === "organizations" || col.key === "referenceNumbers") {
        dataType = "StringArray";
    } else if (col.key === "amount") {
        dataType = "Currency";
    } else if (col.key === "parentEntityName" || col.key === "parentMatter") {
        dataType = "EntityLink";
    }

    return {
        name: col.key,
        displayName: col.label,
        dataType,
        visualSizeFactor: col.width ? col.width / 100 : undefined,
    };
}

/** Try to get the Dataverse org URL from Xrm context. */
function getOrgUrl(): string | null {
    try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm ?? (window.parent as any)?.Xrm;
        const url = xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.();
        return url || null;
    } catch {
        return null;
    }
}

/** Parse a sprk_gridconfiguration record into a SearchViewDefinition. */
function parseRecord(record: Record<string, unknown>): SearchViewDefinition | null {
    try {
        const configJson =
            typeof record.sprk_configjson === "string"
                ? JSON.parse(record.sprk_configjson)
                : null;
        if (!configJson || configJson._type !== VIEW_TYPE) return null;

        return {
            id: String(record.sprk_gridconfigurationid ?? ""),
            name: String(record.sprk_name ?? ""),
            domain: configJson.domain as SearchDomain,
            columns: configJson.columns ?? [],
            defaultSort: configJson.defaultSort,
            isDefault: record.sprk_isdefault === true,
            sortOrder:
                typeof record.sprk_sortorder === "number"
                    ? record.sprk_sortorder
                    : 100,
        };
    } catch {
        return null;
    }
}

// =============================================
// Simple session cache
// =============================================

const viewCache = new Map<string, { data: SearchViewDefinition[]; ts: number }>();

function getCached(domain: SearchDomain): SearchViewDefinition[] | null {
    const entry = viewCache.get(CACHE_KEY_PREFIX + domain);
    if (entry && Date.now() - entry.ts < CACHE_TTL_MS) return entry.data;
    return null;
}

function setCache(domain: SearchDomain, data: SearchViewDefinition[]): void {
    viewCache.set(CACHE_KEY_PREFIX + domain, { data, ts: Date.now() });
}

// =============================================
// Hook
// =============================================

export function useSearchViewDefinitions(
    domain: SearchDomain,
): UseSearchViewDefinitionsResult {
    const [views, setViews] = useState<SearchViewDefinition[]>([]);
    const [activeViewId, setActiveViewId] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    // Fetch views from Dataverse
    const fetchViews = useCallback(async () => {
        // Check cache first
        const cached = getCached(domain);
        if (cached) {
            setViews(cached);
            return;
        }

        const orgUrl = getOrgUrl();
        if (!orgUrl) {
            // No Xrm context — use fallback columns only
            console.log("[useSearchViewDefinitions] No Xrm context, using fallback columns for:", domain);
            setViews([]);
            return;
        }

        setIsLoading(true);
        setError(null);

        try {
            const entityLogicalName = `semantic_search_${domain}`;
            const filter = `sprk_entitylogicalname eq '${entityLogicalName}' and statecode eq 0`;
            const params = new URLSearchParams({
                $select: "sprk_gridconfigurationid,sprk_name,sprk_configjson,sprk_isdefault,sprk_sortorder",
                $filter: filter,
                $orderby: "sprk_sortorder asc",
            });
            const url = `${orgUrl}/api/data/v9.2/${ENTITY_SET}?${params.toString()}`;

            console.log("[useSearchViewDefinitions] Fetching views for domain:", domain);
            const response = await fetch(url, { headers: ODATA_HEADERS });
            if (!response.ok) {
                throw new Error(`HTTP ${response.status}`);
            }

            const json = await response.json();
            const records: Record<string, unknown>[] = json.value ?? [];
            const parsed = records
                .map(parseRecord)
                .filter((v): v is SearchViewDefinition => v !== null)
                .sort((a, b) => a.sortOrder - b.sortOrder);

            console.log("[useSearchViewDefinitions] Loaded", parsed.length, "view(s) for", domain);
            setCache(domain, parsed);
            setViews(parsed);
        } catch (err) {
            // Non-critical — fallback to domainColumns.ts
            console.log(
                "[useSearchViewDefinitions] Fetch failed, using fallback columns for:",
                domain,
                err instanceof Error ? err.message : err,
            );
            setError(err instanceof Error ? err.message : "Failed to load views");
            setViews([]);
        } finally {
            setIsLoading(false);
        }
    }, [domain]);

    // Fetch on mount and domain change
    useEffect(() => {
        fetchViews();
    }, [fetchViews]);

    // Reset active view when domain changes
    useEffect(() => {
        setActiveViewId(null);
    }, [domain]);

    // Derive active view
    const activeView = useMemo(() => {
        if (views.length === 0) return null;
        if (activeViewId) {
            const found = views.find((v) => v.id === activeViewId);
            if (found) return found;
        }
        // Default: first view with isDefault=true, or first view
        return views.find((v) => v.isDefault) ?? views[0];
    }, [views, activeViewId]);

    // Derive columns: from active Dataverse view, or fallback to domainColumns.ts
    const columns: IDatasetColumn[] = useMemo(() => {
        if (activeView && activeView.columns.length > 0) {
            return activeView.columns.map(toDatasetColumn);
        }
        // Fallback to hardcoded domainColumns.ts
        const fallbackCols = getColumnsForDomain(domain);
        return fallbackCols.map(fallbackToDatasetColumn);
    }, [activeView, domain]);

    const setActiveView = useCallback((viewId: string) => {
        setActiveViewId(viewId);
    }, []);

    return {
        views,
        activeView,
        setActiveView,
        columns,
        isLoading,
        error,
    };
}
