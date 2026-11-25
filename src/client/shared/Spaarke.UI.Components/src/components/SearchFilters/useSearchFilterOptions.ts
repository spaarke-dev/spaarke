/**
 * useSearchFilterOptions — load the semantic-search filter option lists (`searchFilterOptions`)
 * once on mount, with loading / error state and a `refresh()` that re-reads Dataverse.
 *
 * Shared by the SemanticSearch code page and the Spaarke Console's Semantic Search criteria
 * (#3131). React 16-safe (plain `useState` / `useEffect`).
 */

import * as React from 'react';
import type { FilterDropdownOption } from './FilterDropdown';
import { loadSearchFilterOptions, clearSearchFilterOptionsCache } from './searchFilterOptions';

export interface UseSearchFilterOptionsResult {
  documentTypes: FilterDropdownOption[];
  fileTypes: FilterDropdownOption[];
  matterTypes: FilterDropdownOption[];
  isLoading: boolean;
  error: string | null;
  /** Clear the cache and load again. */
  refresh: () => void;
}

export function useSearchFilterOptions(): UseSearchFilterOptionsResult {
  const [documentTypes, setDocumentTypes] = React.useState<FilterDropdownOption[]>([]);
  const [fileTypes, setFileTypes] = React.useState<FilterDropdownOption[]>([]);
  const [matterTypes, setMatterTypes] = React.useState<FilterDropdownOption[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);
  const mountedRef = React.useRef(true);

  const fetchAll = React.useCallback(async () => {
    setIsLoading(true);
    setError(null);
    try {
      const options = await loadSearchFilterOptions();
      if (mountedRef.current) {
        setDocumentTypes(options.documentTypes);
        setMatterTypes(options.matterTypes);
        setFileTypes(options.fileTypes);
        setIsLoading(false);
      }
    } catch (err) {
      console.error('Failed to fetch filter options:', err);
      if (mountedRef.current) {
        setError('Failed to load filter options');
        setIsLoading(false);
      }
    }
  }, []);

  React.useEffect(() => {
    mountedRef.current = true;
    void fetchAll();
    return () => {
      mountedRef.current = false;
    };
  }, [fetchAll]);

  const refresh = React.useCallback(() => {
    clearSearchFilterOptionsCache();
    void fetchAll();
  }, [fetchAll]);

  return { documentTypes, fileTypes, matterTypes, isLoading, error, refresh };
}
