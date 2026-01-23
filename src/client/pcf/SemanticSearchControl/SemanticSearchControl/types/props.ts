/**
 * Component prop interfaces for SemanticSearchControl.
 *
 * @see spec.md for component architecture
 */

import { SearchFilters, SearchResult, SearchScope, DateRange } from "./search";
import { IInputs } from "../generated/ManifestTypes";

/**
 * Props for the main SemanticSearchControl component
 */
export interface ISemanticSearchControlProps {
    context: ComponentFramework.Context<IInputs>;
    notifyOutputChanged: () => void;
    onDocumentSelect: (documentId: string) => void;
}

/**
 * Props for SearchInput component
 */
export interface ISearchInputProps {
    value: string;
    placeholder: string;
    disabled: boolean;
    onValueChange: (value: string) => void;
    onSearch: () => void;
}

/**
 * Props for FilterPanel component
 */
export interface IFilterPanelProps {
    filters: SearchFilters;
    searchScope: SearchScope;
    scopeId: string | null;
    onFiltersChange: (filters: SearchFilters) => void;
    disabled: boolean;
}

/**
 * Filter option for dropdowns
 */
export interface FilterOption {
    key: string;
    label: string;
}

/**
 * Props for FilterDropdown component
 */
export interface IFilterDropdownProps {
    label: string;
    options: FilterOption[];
    selectedKeys: string[];
    onSelectionChange: (keys: string[]) => void;
    disabled: boolean;
    multiSelect?: boolean;
}

/**
 * Props for DateRangeFilter component
 */
export interface IDateRangeFilterProps {
    label: string;
    value: DateRange | null;
    onChange: (range: DateRange | null) => void;
    disabled: boolean;
}

/**
 * Props for ResultsList component
 */
export interface IResultsListProps {
    results: SearchResult[];
    isLoading: boolean;
    isLoadingMore: boolean;
    hasMore: boolean;
    totalCount: number;
    onLoadMore: () => void;
    onResultClick: (result: SearchResult) => void;
    onOpenFile: (result: SearchResult) => void;
    onOpenRecord: (result: SearchResult, inModal: boolean) => void;
    onViewAll: () => void;
    compactMode: boolean;
}

/**
 * Props for ResultCard component
 */
export interface IResultCardProps {
    result: SearchResult;
    onClick: () => void;
    onOpenFile: () => void;
    onOpenRecord: (inModal: boolean) => void;
    compactMode: boolean;
}

/**
 * Props for SimilarityBadge component
 */
export interface ISimilarityBadgeProps {
    score: number;
}

/**
 * Props for HighlightedSnippet component
 */
export interface IHighlightedSnippetProps {
    text: string;
    maxLength?: number;
}

/**
 * Props for EmptyState component
 */
export interface IEmptyStateProps {
    query: string;
    hasFilters: boolean;
}

/**
 * Props for ErrorState component
 */
export interface IErrorStateProps {
    message: string;
    retryable: boolean;
    onRetry: () => void;
}

/**
 * Props for LoadingState component
 */
export interface ILoadingStateProps {
    count: number;
}
