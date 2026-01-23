/**
 * useFilterOptions hook
 *
 * Fetches filter options from Dataverse metadata service.
 * Manages loading state and caches results.
 *
 * @see spec.md for filter requirements
 */

import { useState, useEffect, useMemo } from "react";
import { FilterOption } from "../types";
import { DataverseMetadataService } from "../services";

/**
 * Loading state for individual filter options
 */
interface FilterOptionsState {
    documentTypes: FilterOption[];
    matterTypes: FilterOption[];
    fileTypes: FilterOption[];
    isLoading: boolean;
    error: string | null;
}

/**
 * Return type for useFilterOptions hook
 */
interface UseFilterOptionsResult {
    /** Document type options */
    documentTypeOptions: FilterOption[];

    /** Matter type options */
    matterTypeOptions: FilterOption[];

    /** File type options */
    fileTypeOptions: FilterOption[];

    /** Whether options are loading */
    isLoading: boolean;

    /** Error message if fetch failed */
    error: string | null;

    /** Refresh options from Dataverse */
    refresh: () => Promise<void>;
}

/**
 * Singleton instance of DataverseMetadataService
 */
let metadataServiceInstance: DataverseMetadataService | null = null;

/**
 * Get or create DataverseMetadataService instance
 */
function getMetadataService(): DataverseMetadataService {
    if (!metadataServiceInstance) {
        metadataServiceInstance = new DataverseMetadataService();
    }
    return metadataServiceInstance;
}

/**
 * Hook for fetching filter options from Dataverse.
 *
 * @example
 * ```tsx
 * const {
 *   documentTypeOptions,
 *   matterTypeOptions,
 *   fileTypeOptions,
 *   isLoading
 * } = useFilterOptions();
 *
 * // Use in FilterDropdown
 * <FilterDropdown
 *   label="Document Type"
 *   options={documentTypeOptions}
 *   loading={isLoading}
 *   ...
 * />
 * ```
 */
export function useFilterOptions(): UseFilterOptionsResult {
    const [state, setState] = useState<FilterOptionsState>({
        documentTypes: [],
        matterTypes: [],
        fileTypes: [],
        isLoading: true,
        error: null,
    });

    // Get metadata service
    const metadataService = useMemo(() => getMetadataService(), []);

    /**
     * Fetch all options
     */
    const fetchOptions = async (): Promise<void> => {
        setState((prev) => ({ ...prev, isLoading: true, error: null }));

        try {
            // Fetch all options in parallel
            const [documentTypes, matterTypes, fileTypes] = await Promise.all([
                metadataService.getDocumentTypeOptions(),
                metadataService.getMatterTypeOptions(),
                metadataService.getFileTypeOptions(),
            ]);

            setState({
                documentTypes,
                matterTypes,
                fileTypes,
                isLoading: false,
                error: null,
            });
        } catch (err) {
            console.error("Failed to fetch filter options:", err);
            setState((prev) => ({
                ...prev,
                isLoading: false,
                error: "Failed to load filter options",
            }));
        }
    };

    // Fetch options on mount (intentionally empty deps - fetch once)
    useEffect(() => {
        void fetchOptions();
    }, []);

    /**
     * Refresh options (force refetch)
     */
    const refresh = async (): Promise<void> => {
        metadataService.clearCache();
        await fetchOptions();
    };

    return {
        documentTypeOptions: state.documentTypes,
        matterTypeOptions: state.matterTypes,
        fileTypeOptions: state.fileTypes,
        isLoading: state.isLoading,
        error: state.error,
        refresh,
    };
}

export default useFilterOptions;
