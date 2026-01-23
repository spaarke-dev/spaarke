/**
 * useFilters hook
 *
 * Manages filter state for semantic search. Provides functions to update
 * individual filters and clear all filters.
 *
 * @see spec.md for filter behavior requirements
 */

import { useState, useCallback, useMemo } from "react";
import { SearchFilters, DateRange } from "../types";

/**
 * Initial/empty filter state
 */
const initialFilters: SearchFilters = {
    documentTypes: [],
    matterTypes: [],
    dateRange: null,
    fileTypes: [],
};

/**
 * Return type for useFilters hook
 */
interface UseFiltersResult {
    /** Current filter values */
    filters: SearchFilters;

    /** Set document type filter */
    setDocumentTypes: (types: string[]) => void;

    /** Set matter type filter */
    setMatterTypes: (types: string[]) => void;

    /** Set file type filter */
    setFileTypes: (types: string[]) => void;

    /** Set date range filter */
    setDateRange: (range: DateRange | null) => void;

    /** Clear all filters to initial state */
    clearFilters: () => void;

    /** Update all filters at once */
    setFilters: (filters: SearchFilters) => void;

    /** Whether any filters are currently active */
    hasActiveFilters: boolean;
}

/**
 * Hook for managing search filter state.
 *
 * @example
 * ```tsx
 * const {
 *   filters,
 *   setDocumentTypes,
 *   setDateRange,
 *   clearFilters,
 *   hasActiveFilters
 * } = useFilters();
 *
 * // Update document types
 * setDocumentTypes(['contract', 'amendment']);
 *
 * // Check if filters are active
 * if (hasActiveFilters) {
 *   // Show "Clear filters" button
 * }
 * ```
 */
export function useFilters(): UseFiltersResult {
    const [filters, setFiltersState] = useState<SearchFilters>(initialFilters);

    // Set document types
    const setDocumentTypes = useCallback((types: string[]) => {
        setFiltersState((prev) => ({
            ...prev,
            documentTypes: types,
        }));
    }, []);

    // Set matter types
    const setMatterTypes = useCallback((types: string[]) => {
        setFiltersState((prev) => ({
            ...prev,
            matterTypes: types,
        }));
    }, []);

    // Set file types
    const setFileTypes = useCallback((types: string[]) => {
        setFiltersState((prev) => ({
            ...prev,
            fileTypes: types,
        }));
    }, []);

    // Set date range
    const setDateRange = useCallback((range: DateRange | null) => {
        setFiltersState((prev) => ({
            ...prev,
            dateRange: range,
        }));
    }, []);

    // Set all filters at once
    const setFilters = useCallback((newFilters: SearchFilters) => {
        setFiltersState(newFilters);
    }, []);

    // Clear all filters
    const clearFilters = useCallback(() => {
        setFiltersState(initialFilters);
    }, []);

    // Compute whether any filters are active
    const hasActiveFilters = useMemo(() => {
        return (
            filters.documentTypes.length > 0 ||
            filters.matterTypes.length > 0 ||
            filters.fileTypes.length > 0 ||
            filters.dateRange !== null
        );
    }, [filters]);

    return {
        filters,
        setDocumentTypes,
        setMatterTypes,
        setFileTypes,
        setDateRange,
        setFilters,
        clearFilters,
        hasActiveFilters,
    };
}

export default useFilters;
