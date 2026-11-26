/**
 * useFilterOptions — the filter dropdown options (Document Type, File Type, Matter Type).
 *
 * The loader and hook live in `@spaarke/ui-components` (`SearchFilters/useSearchFilterOptions`)
 * since #3131, so the Spaarke Console's search criteria offer the same values this page filters
 * on. This module keeps the page's import name.
 */

export { useSearchFilterOptions as useFilterOptions } from '@spaarke/ui-components/components/SearchFilters';
export type { UseSearchFilterOptionsResult as UseFilterOptionsResult } from '@spaarke/ui-components/components/SearchFilters';
