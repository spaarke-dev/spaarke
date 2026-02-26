/**
 * useFilterOptions — Fetch filter dropdown options from Dataverse
 *
 * Code Page version: uses DataverseWebApiService (direct fetch) instead of PCF context.webAPI.
 * Options are cached at module level (singleton) to avoid re-fetching on component re-mount.
 *
 * Provides:
 *   - documentTypes: FilterOption[] — from sprk_document optionset
 *   - fileTypes: FilterOption[]     — static file type list
 *   - matterTypes: FilterOption[]   — from sprk_mattertype_ref lookup entity
 *   - isLoading: boolean
 *   - error: string | null
 *   - refresh(): void              — clears cache and re-fetches
 *
 * @see useFilterOptions.ts (PCF version) for reference pattern
 */

import { useState, useEffect, useCallback, useRef } from "react";
import type { FilterOption } from "../types";
import {
    fetchOptionsetValues,
    fetchLookupValues,
    getFileTypeOptions,
    clearCache,
} from "../services/DataverseWebApiService";

// =============================================
// Return type
// =============================================

export interface UseFilterOptionsResult {
    documentTypes: FilterOption[];
    fileTypes: FilterOption[];
    matterTypes: FilterOption[];
    isLoading: boolean;
    error: string | null;
    refresh: () => void;
}

// =============================================
// Hook
// =============================================

export function useFilterOptions(): UseFilterOptionsResult {
    const [documentTypes, setDocumentTypes] = useState<FilterOption[]>([]);
    const [fileTypes, setFileTypes] = useState<FilterOption[]>([]);
    const [matterTypes, setMatterTypes] = useState<FilterOption[]>([]);
    const [isLoading, setIsLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const mountedRef = useRef(true);

    const fetchAll = useCallback(async () => {
        setIsLoading(true);
        setError(null);

        try {
            // Fetch all three option sets in parallel
            const [docTypes, matTypes] = await Promise.all([
                fetchOptionsetValues("sprk_document", "sprk_documenttype"),
                fetchLookupValues("sprk_mattertype_refs", "sprk_mattertypename"),
            ]);

            // File types are static — no fetch needed
            const fTypes = getFileTypeOptions();

            if (mountedRef.current) {
                setDocumentTypes(docTypes);
                setMatterTypes(matTypes);
                setFileTypes(fTypes);
                setIsLoading(false);
            }
        } catch (err) {
            console.error("Failed to fetch filter options:", err);
            if (mountedRef.current) {
                setError("Failed to load filter options");
                setIsLoading(false);
            }
        }
    }, []);

    // Fetch on mount
    useEffect(() => {
        mountedRef.current = true;
        void fetchAll();
        return () => {
            mountedRef.current = false;
        };
    }, [fetchAll]);

    // Refresh: clear cache and re-fetch
    const refresh = useCallback(() => {
        clearCache();
        void fetchAll();
    }, [fetchAll]);

    return {
        documentTypes,
        fileTypes,
        matterTypes,
        isLoading,
        error,
        refresh,
    };
}

export default useFilterOptions;
