/**
 * useSearchFilterOptions — load the semantic-search filter option lists (`searchFilterOptions`)
 * once on mount, with loading / error state and a `refresh()` that re-reads Dataverse.
 *
 * Shared by the SemanticSearch code page and the Spaarke Console's Semantic Search criteria
 * (#3131). React 16-safe (plain `useState` / `useEffect`).
 *
 * File types are a fixed list, so they are available from the first render (no spinner while the
 * Dataverse lists load). `error` is set when either Dataverse list failed; the other list still
 * shows.
 */

import * as React from 'react';
import type { FilterDropdownOption } from './FilterDropdown';
import {
  loadSearchFilterOptions,
  clearSearchFilterOptionsCache,
  SEARCH_FILE_TYPE_OPTIONS,
} from './searchFilterOptions';

export interface UseSearchFilterOptionsResult {
  documentTypes: FilterDropdownOption[];
  fileTypes: FilterDropdownOption[];
  matterTypes: FilterDropdownOption[];
  isLoading: boolean;
  /** "Failed to load filter options" when a Dataverse list could not be read; null otherwise. */
  error: string | null;
  /** Clear the cache and load again. */
  refresh: () => void;
}

const LOAD_ERROR = 'Failed to load filter options';

export function useSearchFilterOptions(): UseSearchFilterOptionsResult {
  const [documentTypes, setDocumentTypes] = React.useState<FilterDropdownOption[]>([]);
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
        setError(options.failed.length > 0 ? LOAD_ERROR : null);
        setIsLoading(false);
      }
    } catch (err) {
      console.error('Failed to fetch filter options:', err);
      if (mountedRef.current) {
        setError(LOAD_ERROR);
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

  return {
    documentTypes,
    fileTypes: SEARCH_FILE_TYPE_OPTIONS as FilterDropdownOption[],
    matterTypes,
    isLoading,
    error,
    refresh,
  };
}
