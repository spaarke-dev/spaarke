/**
 * Component prop interfaces for SemanticSearchControl.
 *
 * @see spec.md for component architecture
 */

import { SearchFilters, SearchResult, SearchScope, DateRange } from './search';
import { IInputs } from '../generated/ManifestTypes';

/**
 * Props for the main SemanticSearchControl component
 */
export interface ISemanticSearchControlProps {
  context: ComponentFramework.Context<IInputs>;
  notifyOutputChanged: () => void;
  onDocumentSelect: (documentId: string) => void;
  /** Whether the resolved Fluent theme is dark mode (from PCF context) */
  isDarkMode?: boolean;
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
  /** @deprecated Moved to ResultsList toolbar. Kept optional for backward compat. */
  onAddDocument?: () => void;
  /** @deprecated Moved to ResultsList toolbar. Kept optional for backward compat. */
  onOpenViewer?: () => void;
}

/**
 * Props for FilterPanel component
 */
export interface IFilterPanelProps {
  filters: SearchFilters;
  searchScope: SearchScope;
  scopeId: string | null;
  onFiltersChange: (filters: SearchFilters) => void;
  onApply?: () => void;
  disabled: boolean;
  onCollapse?: () => void;
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
  /** Minimum score threshold (0-100). Results below this are hidden. */
  threshold: number;
  onLoadMore: () => void;
  onResultClick: (result: SearchResult) => void;
  onOpenFile: (result: SearchResult, mode: 'web' | 'desktop') => void;
  onOpenRecord: (result: SearchResult, inModal: boolean) => void;
  onFindSimilar: (result: SearchResult) => void;
  onPreview: (result: SearchResult) => Promise<string | null>;
  onSummary: (result: SearchResult) => Promise<SummaryData>;
  onEmailDocument: (result: SearchResult) => void;
  onCopyLink: (result: SearchResult) => void;
  onToggleWorkspace: (result: SearchResult) => void;
  /** Check if a document is in the workspace. */
  isInWorkspace: (result: SearchResult) => boolean;
  onViewAll: () => void;
  onReload: () => void;
  /** Add document callback — icon in toolbar header */
  onAddDocument?: () => void;
  /** Open full viewer callback — icon in toolbar header */
  onOpenViewer?: () => void;
  /** Email selected/visible documents — opens DocumentEmailWizard with the current
   *  results. Wizard step 1 allows the user to deselect docs they don't want. */
  onEmailDocuments?: () => void;
  /**
   * v1.1.49 — Card-view selection wiring (Item 1). When provided, each
   * ResultCard renders a selection-checkbox overlay and reflects/toggles
   * the parent's `selectedIds` set. The host owns the set so it persists
   * across list/card toggles.
   */
  selectedIds?: Set<string>;
  onToggleSelect?: (documentId: string) => void;
  /**
   * v1.1.49 — Host-level preview hook (Item 6). When provided, ResultsList
   * passes it down to each ResultCard so opening preview from a card flows
   * through the SAME host-level FilePreviewDialog as the list view (shared
   * Prev/Next navigation set).
   */
  onOpenPreview?: (result: SearchResult) => void;
  /**
   * v1.1.49 — Hides the local toolbar header rendered by ResultsList.
   * When the host renders its own consolidated toolbar above ResultsList
   * (Item 2), this should be true so the duplicate row is suppressed.
   */
  hideToolbar?: boolean;
  /**
   * v1.1.49 — Lazy-load infinite-scroll sentinel (Item 9). When provided,
   * the host owns the loading-state + load-more flow and this callback is
   * fired when the bottom sentinel intersects the viewport.
   */
  onLoadMoreSentinel?: () => void;
  compactMode: boolean;
}

/**
 * Summary data fetched from Dataverse
 */
export interface SummaryData {
  summary: string | null;
  tldr: string | null;
}

/**
 * Props for ResultCard component
 */
export interface IResultCardProps {
  result: SearchResult;
  onClick: () => void;
  onOpenFile: (mode: 'web' | 'desktop') => void;
  onOpenRecord: (inModal: boolean) => void;
  onFindSimilar: () => void;
  /**
   * v1.1.49 — Open the shared host-level preview dialog for this card. The
   * host owns the FilePreviewDialog instance so list AND card views share
   * one navigation set (Item 6). When provided, ResultCard no longer
   * renders its own FilePreviewDialog.
   */
  onOpenPreview?: () => void;
  /**
   * v1.1.49 — Selection checkbox state + handler (Item 1). When `isSelected`
   * is undefined the card omits the selection overlay (back-compat for any
   * caller not yet wired). Click on the checkbox MUST stop propagation so
   * the card-click preview-open handler does not also fire.
   */
  isSelected?: boolean;
  onToggleSelect?: () => void;
  onPreview: () => Promise<string | null>;
  onSummary: () => Promise<SummaryData>;
  onEmailDocument: () => void;
  onCopyLink: () => void;
  onToggleWorkspace: () => void;
  isInWorkspace: boolean;
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
