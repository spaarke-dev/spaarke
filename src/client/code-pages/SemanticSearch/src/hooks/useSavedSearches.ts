/**
 * useSavedSearches — CRUD hook for saved search configurations
 *
 * Stores saved searches in sprk_gridconfiguration using Dataverse WebAPI:
 *   - sprk_name: saved search display name
 *   - sprk_configjson: full search config JSON with _type discriminator
 *   - sprk_viewtype: 2 (CustomFetchXML, reused for semantic searches)
 *   - sprk_entitylogicalname: "semantic_search" sentinel
 *   - Filter by _createdby_value for per-user scoping
 *
 * @see notes/spikes/gridconfiguration-schema.md — Option A (AS-IS)
 */

import { useState, useEffect, useCallback, useRef } from "react";
import { getOrgUrl } from "../services/DataverseWebApiService";
import type { SavedSearch } from "../types";

// =============================================
// Constants
// =============================================

const ENTITY_SET = "sprk_gridconfigurations";
const CONFIG_TYPE = "semantic-search";
const CONFIG_VERSION = 1;

/** OData headers for Dataverse WebAPI calls. */
const ODATA_HEADERS: HeadersInit = {
    "Accept": "application/json",
    "Content-Type": "application/json",
    "OData-MaxVersion": "4.0",
    "OData-Version": "4.0",
};

// =============================================
// Dataverse record shape
// =============================================

interface GridConfigRecord {
    sprk_gridconfigurationid: string;
    sprk_name: string;
    sprk_configjson?: string;
    _createdby_value?: string;
}

interface ConfigJson {
    _type: string;
    _version: number;
    searchDomain: string;
    query: string;
    filters: SavedSearch["filters"];
    viewMode: string;
    columns: string[];
    sortColumn: string;
    sortDirection: string;
    graphClusterBy?: string;
}

// =============================================
// Helpers
// =============================================

/** Get the current Dataverse user ID from Xrm context. */
function getCurrentUserId(): string | null {
    try {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const xrm = (window as any).Xrm;
        if (xrm?.Utility?.getGlobalContext) {
            // getUserId() returns GUID with braces: "{GUID}"
            return xrm.Utility.getGlobalContext().getUserId().replace(/[{}]/g, "");
        }
    } catch {
        /* Xrm not available */
    }
    return null;
}

/** Parse a Dataverse record into a SavedSearch object. Returns null if not a semantic search config. */
function parseRecord(record: GridConfigRecord): SavedSearch | null {
    if (!record.sprk_configjson) return null;

    try {
        const json: ConfigJson = JSON.parse(record.sprk_configjson);
        if (json._type !== CONFIG_TYPE) return null;

        return {
            id: record.sprk_gridconfigurationid,
            name: record.sprk_name,
            searchDomain: json.searchDomain as SavedSearch["searchDomain"],
            query: json.query ?? "",
            filters: json.filters,
            viewMode: json.viewMode as SavedSearch["viewMode"],
            columns: json.columns ?? [],
            sortColumn: json.sortColumn ?? "similarity",
            sortDirection: json.sortDirection as SavedSearch["sortDirection"],
            graphClusterBy: json.graphClusterBy as SavedSearch["graphClusterBy"],
        };
    } catch {
        return null;
    }
}

/** Build the configjson string for a SavedSearch. */
function buildConfigJson(search: SavedSearch): string {
    const json: ConfigJson = {
        _type: CONFIG_TYPE,
        _version: CONFIG_VERSION,
        searchDomain: search.searchDomain,
        query: search.query,
        filters: search.filters,
        viewMode: search.viewMode,
        columns: search.columns,
        sortColumn: search.sortColumn,
        sortDirection: search.sortDirection,
        graphClusterBy: search.graphClusterBy,
    };
    return JSON.stringify(json);
}

// =============================================
// Hook
// =============================================

export interface UseSavedSearchesResult {
    savedSearches: SavedSearch[];
    isLoading: boolean;
    isSaving: boolean;
    error: string | null;
    saveSearch: (search: SavedSearch) => Promise<void>;
    updateSearch: (id: string, search: SavedSearch) => Promise<void>;
    deleteSearch: (id: string) => Promise<void>;
    refresh: () => Promise<void>;
}

export function useSavedSearches(): UseSavedSearchesResult {
    const [savedSearches, setSavedSearches] = useState<SavedSearch[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [isSaving, setIsSaving] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const mountedRef = useRef(true);

    const loadSavedSearches = useCallback(async () => {
        setIsLoading(true);
        setError(null);

        try {
            const userId = getCurrentUserId();
            const baseUrl = getOrgUrl();
            const select = "$select=sprk_gridconfigurationid,sprk_name,sprk_configjson,_createdby_value";
            const filter = [
                "sprk_entitylogicalname eq 'semantic_search'",
                "statecode eq 0",
                userId ? `_createdby_value eq '${userId}'` : "",
            ]
                .filter(Boolean)
                .join(" and ");

            const url = `${baseUrl}/api/data/v9.2/${ENTITY_SET}?${select}&$filter=${encodeURIComponent(filter)}&$orderby=sprk_name asc`;

            const response = await fetch(url, { headers: ODATA_HEADERS });
            if (!response.ok) {
                throw new Error(`Failed to load saved searches: ${response.status}`);
            }

            const data = await response.json();
            const records: GridConfigRecord[] = data.value ?? [];
            const searches = records
                .map(parseRecord)
                .filter((s): s is SavedSearch => s !== null);

            if (mountedRef.current) {
                setSavedSearches(searches);
            }
        } catch (err) {
            if (mountedRef.current) {
                setError(err instanceof Error ? err.message : "Failed to load saved searches");
            }
        } finally {
            if (mountedRef.current) {
                setIsLoading(false);
            }
        }
    }, []);

    const saveSearch = useCallback(async (search: SavedSearch) => {
        setIsSaving(true);
        setError(null);

        try {
            const baseUrl = getOrgUrl();
            const body = {
                sprk_name: search.name,
                sprk_entitylogicalname: "semantic_search",
                sprk_viewtype: 2,
                sprk_configjson: buildConfigJson(search),
            };

            const response = await fetch(`${baseUrl}/api/data/v9.2/${ENTITY_SET}`, {
                method: "POST",
                headers: ODATA_HEADERS,
                body: JSON.stringify(body),
            });

            if (!response.ok) {
                throw new Error(`Failed to save search: ${response.status}`);
            }

            await loadSavedSearches();
        } catch (err) {
            if (mountedRef.current) {
                setError(err instanceof Error ? err.message : "Failed to save search");
            }
        } finally {
            if (mountedRef.current) {
                setIsSaving(false);
            }
        }
    }, [loadSavedSearches]);

    const updateSearch = useCallback(async (id: string, search: SavedSearch) => {
        setIsSaving(true);
        setError(null);

        try {
            const baseUrl = getOrgUrl();
            const body = {
                sprk_name: search.name,
                sprk_configjson: buildConfigJson(search),
            };

            const response = await fetch(`${baseUrl}/api/data/v9.2/${ENTITY_SET}(${id})`, {
                method: "PATCH",
                headers: ODATA_HEADERS,
                body: JSON.stringify(body),
            });

            if (!response.ok) {
                throw new Error(`Failed to update search: ${response.status}`);
            }

            await loadSavedSearches();
        } catch (err) {
            if (mountedRef.current) {
                setError(err instanceof Error ? err.message : "Failed to update search");
            }
        } finally {
            if (mountedRef.current) {
                setIsSaving(false);
            }
        }
    }, [loadSavedSearches]);

    const deleteSearch = useCallback(async (id: string) => {
        setIsSaving(true);
        setError(null);

        try {
            const baseUrl = getOrgUrl();
            // Soft delete: set statecode=1 (inactive)
            const response = await fetch(`${baseUrl}/api/data/v9.2/${ENTITY_SET}(${id})`, {
                method: "PATCH",
                headers: ODATA_HEADERS,
                body: JSON.stringify({ statecode: 1 }),
            });

            if (!response.ok) {
                throw new Error(`Failed to delete search: ${response.status}`);
            }

            await loadSavedSearches();
        } catch (err) {
            if (mountedRef.current) {
                setError(err instanceof Error ? err.message : "Failed to delete search");
            }
        } finally {
            if (mountedRef.current) {
                setIsSaving(false);
            }
        }
    }, [loadSavedSearches]);

    // Load on mount
    useEffect(() => {
        mountedRef.current = true;
        loadSavedSearches();
        return () => {
            mountedRef.current = false;
        };
    }, [loadSavedSearches]);

    return {
        savedSearches,
        isLoading,
        isSaving,
        error,
        saveSearch,
        updateSearch,
        deleteSearch,
        refresh: loadSavedSearches,
    };
}
