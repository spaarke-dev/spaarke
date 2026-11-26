/**
 * initialFilters — the search filters the page starts with: the defaults, seeded from the URL
 * envelope (`parseUrlParams`). Moved out of App.tsx (#3131) so the seeding is unit-tested.
 */

import type { SearchFilters, HybridMode, AppUrlParams } from '../types';

/** No filter, default threshold and mode. */
export const DEFAULT_FILTERS: SearchFilters = {
  documentTypes: [],
  fileTypes: [],
  matterTypes: [],
  dateRange: { from: null, to: null },
  threshold: 50,
  searchMode: 'rrf',
};

/**
 * Map the envelope `searchMode` literal (PCF/URL) to the API `HybridMode`
 * literal used by request bodies. See `EnvelopeSearchMode` doc-comment in
 * `types/index.ts` — `hybrid` is the envelope alias for the API's `rrf`.
 */
function envelopeSearchModeToHybridMode(mode: AppUrlParams['searchMode']): HybridMode | undefined {
  if (mode === 'hybrid') return 'rrf';
  if (mode === 'vectorOnly') return 'vectorOnly';
  if (mode === 'keywordOnly') return 'keywordOnly';
  return undefined;
}

/**
 * Seed `SearchFilters` state from URL-derived envelope params (FR-CP-03).
 * Used as the lazy `useState` initializer so values are present BEFORE the
 * first render commits and the auto-search effect fires — guaranteeing the
 * very first network POST uses URL-derived filters.
 *
 * Missing/undefined URL values fall back to `DEFAULT_FILTERS` so the page
 * still renders correctly when opened without any envelope.
 */
export function buildInitialFilters(urlParams: AppUrlParams | undefined): SearchFilters {
  if (!urlParams) return DEFAULT_FILTERS;
  const mappedSearchMode = envelopeSearchModeToHybridMode(urlParams.searchMode);
  return {
    ...DEFAULT_FILTERS,
    fileTypes: urlParams.fileTypes ?? DEFAULT_FILTERS.fileTypes,
    documentTypes: urlParams.documentTypes ?? DEFAULT_FILTERS.documentTypes,
    matterTypes: urlParams.matterTypes ?? DEFAULT_FILTERS.matterTypes,
    dateRange: {
      from: urlParams.dateFrom ?? null,
      to: urlParams.dateTo ?? null,
    },
    threshold: urlParams.threshold ?? DEFAULT_FILTERS.threshold,
    searchMode: mappedSearchMode ?? DEFAULT_FILTERS.searchMode,
  };
}
