/**
 * SearchFilterPane -- Collapsible left-side filter pane
 *
 * Phase G (Lookup-driven multi-index) — spec §6.
 *
 * Layout (top to bottom):
 *   1. "Search Criteria" header with collapse chevron
 *   2. Search Index dropdown (REPLACES the prior 4-domain ToggleButton grid)
 *   3. Saved Searches selector
 *   4. AI Search query textarea + (i) info popover
 *   5. Dashed separator
 *   6. Domain-aware filter dropdowns (Document Type, File Type, Matter Type)
 *   7. Date range
 *   8. Relevance Threshold + (i) info popover (MOVED IN from top-right overlay)
 *   9. Search Mode + (i) info popover (MOVED IN from top-right overlay)
 *  10. Search button + Cancel button
 *
 * The Relevance Threshold + Search Mode controls were previously in the
 * VisualizationSettings overlay (top-right) which was too hidden for users
 * to discover. They now live in the side pane below the existing filters,
 * above the Search button — keeping all search-shaping controls in one
 * discoverable surface.
 *
 * @see ADR-021 for Fluent UI v9 design system requirements
 * @see projects/spaarke-multi-container-multi-index-r1/notes/phase-g/spec.md §6
 */

import { useCallback, useState } from 'react';
import {
  makeStyles,
  tokens,
  mergeClasses,
  Textarea,
  Button,
  Label,
  Text,
  Slider,
  Dropdown,
  Option,
} from '@fluentui/react-components';
import { ChevronDoubleLeft20Regular, ChevronDoubleRight20Regular } from '@fluentui/react-icons';
import type { SearchDomain, SearchFilters, FilterOption, SavedSearch, HybridMode } from '../types';
import { SearchIndexSelector } from './SearchIndexSelector';
import { InfoPopover } from './InfoPopover';
import { FilterDropdown } from './FilterDropdown';
import { DateRangeFilter } from './DateRangeFilter';
import { SavedSearchSelector } from './SavedSearchSelector';
import type { AiSearchIndexRow } from '../services/aiSearchIndexService';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const EXPANDED_WIDTH = '280px';
const COLLAPSED_WIDTH = '40px';
const TRANSITION_DURATION = '200ms';

const SEARCH_MODE_OPTIONS: { value: HybridMode; label: string }[] = [
  { value: 'rrf', label: 'Hybrid (RRF)' },
  { value: 'vectorOnly', label: 'Vector Only' },
  { value: 'keywordOnly', label: 'Keyword Only' },
];

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface SearchFilterPaneProps {
  /** Currently active search domain — drives which filter sections are visible. */
  activeDomain: SearchDomain;
  /** Active `sprk_aisearchindex` rows (already loaded by App). */
  searchIndexes: AiSearchIndexRow[];
  /** Selected index row PK (empty string when none). */
  selectedSearchIndexId: string;
  /** Whether the indexes are still being fetched. */
  isLoadingSearchIndexes: boolean;
  /** Called when the user selects a different Search Index row. */
  onSelectSearchIndex: (row: AiSearchIndexRow) => void;
  /** Current filter state */
  filters: SearchFilters;
  /** Callback when any filter value changes */
  onFiltersChange: (filters: SearchFilters) => void;
  /** Callback to trigger a search with updated filters */
  onSearch: (query: string, filters: SearchFilters) => void;
  /** Available filter option lists for dropdowns */
  filterOptions: {
    documentTypes: FilterOption[];
    fileTypes: FilterOption[];
    matterTypes: FilterOption[];
  };
  /** Whether a search is currently in progress */
  isLoading: boolean;
  /** Current search query (controlled by parent App) */
  query: string;
  /** Callback when query text changes */
  onQueryChange: (query: string) => void;
  /** Saved searches for the selector */
  savedSearches: SavedSearch[];
  /** Currently active saved search name */
  currentSearchName: string | null;
  /** Called when a saved search is selected */
  onSelectSavedSearch: (search: SavedSearch) => void;
  /** Called when user saves current search */
  onSaveCurrentSearch: () => void;
  /** Whether saved searches are loading */
  isSavedSearchesLoading: boolean;
  /**
   * Cancel handler — clears AI Search query + all filters + active saved-search
   * selection, returning the pane to its initial state.
   */
  onCancel: () => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  pane: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    borderRight: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
    overflowY: 'auto',
    overflowX: 'hidden',
    transitionProperty: 'width, min-width, padding',
    transitionDuration: TRANSITION_DURATION,
    transitionTimingFunction: 'ease-in-out',
  },
  expanded: {
    width: EXPANDED_WIDTH,
    minWidth: EXPANDED_WIDTH,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  collapsed: {
    width: COLLAPSED_WIDTH,
    minWidth: COLLAPSED_WIDTH,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalXS,
    paddingRight: tokens.spacingHorizontalXS,
    alignItems: 'center',
  },
  paneTitle: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    marginBottom: '20px',
  },
  collapseButton: {
    minWidth: 'auto',
  },
  searchIndexSection: {
    marginBottom: tokens.spacingVerticalM,
  },
  savedSearchSection: {
    marginBottom: tokens.spacingVerticalM,
  },
  querySection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    marginBottom: tokens.spacingVerticalM,
  },
  labelRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
  fieldLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  queryTextarea: {
    width: '100%',
  },
  separator: {
    borderBottom: `1px dashed ${tokens.colorNeutralStroke2}`,
    marginTop: tokens.spacingVerticalM,
    marginBottom: tokens.spacingVerticalM,
  },
  filterSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginBottom: tokens.spacingVerticalM,
  },
  sliderRow: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  sliderValue: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorBrandForeground1,
    fontWeight: tokens.fontWeightSemibold,
    minWidth: '36px',
    textAlign: 'right',
  },
  dropdown: {
    width: '100%',
    minWidth: 0,
  },
  actionRow: {
    display: 'flex',
    justifyContent: 'flex-end',
    columnGap: tokens.spacingHorizontalS,
    marginTop: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const SearchFilterPane: React.FC<SearchFilterPaneProps> = ({
  activeDomain,
  searchIndexes,
  selectedSearchIndexId,
  isLoadingSearchIndexes,
  onSelectSearchIndex,
  filters,
  onFiltersChange,
  onSearch,
  filterOptions,
  isLoading,
  query,
  onQueryChange,
  savedSearches,
  currentSearchName,
  onSelectSavedSearch,
  onSaveCurrentSearch,
  isSavedSearchesLoading,
  onCancel,
}) => {
  const styles = useStyles();
  const [isCollapsed, setIsCollapsed] = useState(false);

  // --- Domain-visibility logic ---
  const showDocumentTypeFilter = activeDomain === 'documents';
  const showFileTypeFilter = activeDomain === 'documents';
  const showMatterTypeFilter = activeDomain === 'documents' || activeDomain === 'matters';

  // --- Handlers ---

  const handleToggleCollapse = useCallback(() => {
    setIsCollapsed(prev => !prev);
  }, []);

  const handleSearch = useCallback(() => {
    onSearch(query, filters);
  }, [onSearch, query, filters]);

  const handleQueryKeyDown = useCallback(
    (ev: React.KeyboardEvent) => {
      if (ev.key === 'Enter' && (ev.ctrlKey || ev.metaKey)) {
        ev.preventDefault();
        onSearch(query, filters);
      }
    },
    [onSearch, query, filters]
  );

  const handleDocumentTypesChange = useCallback(
    (selected: string[]) => {
      onFiltersChange({ ...filters, documentTypes: selected });
    },
    [filters, onFiltersChange]
  );

  const handleFileTypesChange = useCallback(
    (selected: string[]) => {
      onFiltersChange({ ...filters, fileTypes: selected });
    },
    [filters, onFiltersChange]
  );

  const handleMatterTypesChange = useCallback(
    (selected: string[]) => {
      onFiltersChange({ ...filters, matterTypes: selected });
    },
    [filters, onFiltersChange]
  );

  const handleDateRangeChange = useCallback(
    (dateRange: { from: string | null; to: string | null }) => {
      onFiltersChange({ ...filters, dateRange });
    },
    [filters, onFiltersChange]
  );

  const handleThresholdChange = useCallback(
    (_ev: unknown, data: { value: number }) => {
      onFiltersChange({ ...filters, threshold: data.value });
    },
    [filters, onFiltersChange]
  );

  const handleSearchModeChange = useCallback(
    (_ev: unknown, data: { optionValue?: string }) => {
      if (!data.optionValue) return;
      onFiltersChange({ ...filters, searchMode: data.optionValue as HybridMode });
    },
    [filters, onFiltersChange]
  );

  // --- Render ---

  const paneClassName = mergeClasses(styles.pane, isCollapsed ? styles.collapsed : styles.expanded);

  // Collapsed state: only show expand button
  if (isCollapsed) {
    return (
      <div className={paneClassName} role="region" aria-label="Search filters">
        <Button
          className={styles.collapseButton}
          appearance="subtle"
          size="small"
          icon={<ChevronDoubleRight20Regular />}
          onClick={handleToggleCollapse}
          aria-label="Expand filters"
        />
      </div>
    );
  }

  const searchModeLabel = SEARCH_MODE_OPTIONS.find(o => o.value === filters.searchMode)?.label ?? 'Hybrid (RRF)';

  return (
    <div className={paneClassName} role="region" aria-label="Search filters">
      {/* Header: "Search Criteria" title + collapse chevron */}
      <div className={styles.paneTitle}>
        <Text weight="semibold" size={400}>
          Search Criteria
        </Text>
        <Button
          className={styles.collapseButton}
          appearance="subtle"
          size="small"
          icon={<ChevronDoubleLeft20Regular />}
          onClick={handleToggleCollapse}
          aria-label="Collapse filters"
        />
      </div>

      {/* Search Index dropdown (replaces the prior 4-domain ToggleButton grid) */}
      <div className={styles.searchIndexSection}>
        <SearchIndexSelector
          indexes={searchIndexes}
          selectedIndexId={selectedSearchIndexId}
          isLoading={isLoadingSearchIndexes}
          onSelectIndex={onSelectSearchIndex}
        />
      </div>

      {/* Dotted divider between search index and saved searches */}
      <div className={styles.separator} />

      {/* Saved Searches */}
      <div className={styles.savedSearchSection}>
        <SavedSearchSelector
          savedSearches={savedSearches}
          currentSearchName={currentSearchName}
          onSelectSavedSearch={onSelectSavedSearch}
          onSaveCurrentSearch={onSaveCurrentSearch}
          isLoading={isSavedSearchesLoading}
        />
      </div>

      {/* AI Search query textarea — with info popover */}
      <div className={styles.querySection}>
        <div className={styles.labelRow}>
          <Label className={styles.fieldLabel}>AI Search</Label>
          <InfoPopover ariaLabel="About AI search">
            Describe what you&apos;re looking for in natural language. The semantic search engine interprets meaning,
            not just keywords. Press <strong>Ctrl+Enter</strong> to search.
          </InfoPopover>
        </div>
        <Textarea
          className={styles.queryTextarea}
          placeholder="Describe what you're looking for..."
          value={query}
          onChange={(_ev, data) => onQueryChange(data.value)}
          onKeyDown={handleQueryKeyDown}
          resize="vertical"
          rows={6}
          aria-label="AI search query"
        />
      </div>

      {/* Dashed separator between query and filters */}
      <div className={styles.separator} />

      {/* Document Type Filter (Documents domain only) */}
      {showDocumentTypeFilter && (
        <div className={styles.filterSection}>
          <FilterDropdown
            label="Document Type"
            options={filterOptions.documentTypes}
            selectedValues={filters.documentTypes}
            onChange={handleDocumentTypesChange}
          />
        </div>
      )}

      {/* File Type Filter (Documents domain only) */}
      {showFileTypeFilter && (
        <div className={styles.filterSection}>
          <FilterDropdown
            label="File Type"
            options={filterOptions.fileTypes}
            selectedValues={filters.fileTypes}
            onChange={handleFileTypesChange}
          />
        </div>
      )}

      {/* Matter Type Filter (Documents + Matters domains) */}
      {showMatterTypeFilter && (
        <div className={styles.filterSection}>
          <FilterDropdown
            label="Matter Type"
            options={filterOptions.matterTypes}
            selectedValues={filters.matterTypes}
            onChange={handleMatterTypesChange}
          />
        </div>
      )}

      {/* Dotted divider between type filters and date range */}
      <div className={styles.separator} />

      {/* Date Range Filter (all domains) */}
      <div className={styles.filterSection}>
        <DateRangeFilter value={filters.dateRange} onChange={handleDateRangeChange} />
      </div>

      {/* Dotted divider between filters and the relocated threshold/mode */}
      <div className={styles.separator} />

      {/* Relevance Threshold (moved IN from the top-right overlay) */}
      <div className={styles.filterSection}>
        <div className={styles.sliderRow}>
          <div className={styles.labelRow}>
            <Label className={styles.fieldLabel}>Relevance Threshold</Label>
            <InfoPopover ariaLabel="About relevance threshold">
              Hide results scoring below this percentage. Higher values keep only the most relevant results. Default
              50%.
            </InfoPopover>
          </div>
          <Text className={styles.sliderValue}>{filters.threshold}%</Text>
        </div>
        <Slider
          min={0}
          max={100}
          value={filters.threshold}
          onChange={handleThresholdChange}
          aria-label="Relevance threshold"
        />
      </div>

      {/* Search Mode (moved IN from the top-right overlay) */}
      <div className={styles.filterSection}>
        <div className={styles.labelRow}>
          <Label className={styles.fieldLabel}>Search Mode</Label>
          <InfoPopover ariaLabel="About search mode">
            <strong>Hybrid (RRF):</strong> Combines meaning and keyword for the best overall results.
            <br />
            <strong>Vector Only:</strong> Pure meaning-based search — good for abstract queries.
            <br />
            <strong>Keyword Only:</strong> Traditional exact-word matching — good for specific terms or clause numbers.
          </InfoPopover>
        </div>
        <Dropdown
          className={styles.dropdown}
          size="small"
          value={searchModeLabel}
          selectedOptions={[filters.searchMode]}
          onOptionSelect={handleSearchModeChange}
          aria-label="Search mode"
        >
          {SEARCH_MODE_OPTIONS.map(opt => (
            <Option key={opt.value} value={opt.value}>
              {opt.label}
            </Option>
          ))}
        </Dropdown>
      </div>

      {/* Action row — Spaarke standard pattern. */}
      <div className={styles.actionRow}>
        <Button appearance="primary" size="small" onClick={handleSearch} disabled={isLoading}>
          Search
        </Button>
        <Button appearance="subtle" size="small" onClick={onCancel} aria-label="Clear search criteria">
          Cancel
        </Button>
      </div>
    </div>
  );
};

export default SearchFilterPane;
