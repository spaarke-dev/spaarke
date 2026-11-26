/**
 * FilterPanel component
 *
 * Container for filter controls: Document Type, File Type, Date Range,
 * Threshold, and Search Mode — all using consistent FilterDropdown components.
 * Filters are applied when the user clicks Search or presses Enter.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 * @see spec.md for scope-aware filter rules
 */

import * as React from 'react';
import { useCallback } from 'react';
import { makeStyles, tokens, Button, Divider, Switch, Text } from '@fluentui/react-components';
import { Dismiss20Regular, ChevronLeft20Regular } from '@fluentui/react-icons';
import { FilterDropdown, DateRangeFilter } from '@spaarke/ui-components/dist/components/SearchFilters';
import type { FilterDropdownOption } from '@spaarke/ui-components/dist/components/SearchFilters';
import { IFilterPanelProps, SearchFilters, DateRange, SearchMode, FilterOption } from '../types';
import { useFilterOptions } from '../hooks/useFilterOptions';

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    width: '100%',
    minWidth: 0,
    overflow: 'hidden',
    boxSizing: 'border-box',
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
  },
  clearButton: {
    minWidth: 'auto',
  },
  collapseButton: {
    minWidth: 'auto',
  },
  filterSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalS,
    minWidth: 0,
  },
});

// Default empty filters
const emptyFilters: SearchFilters = {
  documentTypes: [],
  matterTypes: [],
  dateRange: null,
  fileTypes: [],
  threshold: 0,
  searchMode: 'hybrid',
};

// Threshold options (replaces Slider — 5 preset values)
const THRESHOLD_OPTIONS: FilterDropdownOption[] = [
  { value: '0', label: 'Off' },
  { value: '25', label: '25%' },
  { value: '50', label: '50%' },
  { value: '75', label: '75%' },
  { value: '100', label: '100%' },
];

// Search mode options
const MODE_OPTIONS: FilterDropdownOption[] = [
  { value: 'hybrid', label: 'Hybrid' },
  { value: 'vectorOnly', label: 'Concept Only' },
  { value: 'keywordOnly', label: 'Keyword Only' },
];

// Dataverse options are { key, label }; the shared FilterDropdown takes { value, label }.
const toDropdownOptions = (options: FilterOption[]): FilterDropdownOption[] =>
  options.map(o => ({ value: o.key, label: o.label }));

/**
 * FilterPanel component for search filters.
 *
 * @param props.filters - Current filter values
 * @param props.searchScope - Current search scope (all, entity, matter, project, etc.)
 * @param props.scopeId - ID for scoped search
 * @param props.onFiltersChange - Callback when filters change
 * @param props.onApply - Callback to apply filters (triggers search)
 * @param props.disabled - Whether filters are disabled
 */
export const FilterPanel: React.FC<IFilterPanelProps> = ({
  filters,
  searchScope,
  onFiltersChange,
  disabled,
  onCollapse,
}) => {
  const styles = useStyles();

  // Fetch filter options from Dataverse
  const { matterTypeOptions, fileTypeOptions, isLoading: optionsLoading } = useFilterOptions();
  const fileTypeDropdownOptions = React.useMemo(() => toDropdownOptions(fileTypeOptions), [fileTypeOptions]);
  const matterTypeDropdownOptions = React.useMemo(() => toDropdownOptions(matterTypeOptions), [matterTypeOptions]);

  // Check if any filters are active
  const hasActiveFilters =
    filters.documentTypes.length > 0 ||
    filters.matterTypes.length > 0 ||
    filters.dateRange !== null ||
    filters.fileTypes.length > 0 ||
    filters.threshold > 0 ||
    filters.searchMode !== 'hybrid';

  // Handle clear all filters
  const handleClearFilters = useCallback(() => {
    onFiltersChange(emptyFilters);
  }, [onFiltersChange]);

  // Handle document type change
  const handleDocumentTypesChange = useCallback(
    (keys: string[]) => {
      onFiltersChange({
        ...filters,
        documentTypes: keys,
      });
    },
    [filters, onFiltersChange]
  );

  // Handle matter type change
  const handleMatterTypesChange = useCallback(
    (keys: string[]) => {
      onFiltersChange({
        ...filters,
        matterTypes: keys,
      });
    },
    [filters, onFiltersChange]
  );

  // Handle file type change
  const handleFileTypesChange = useCallback(
    (keys: string[]) => {
      onFiltersChange({
        ...filters,
        fileTypes: keys,
      });
    },
    [filters, onFiltersChange]
  );

  // Handle date range change
  const handleDateRangeChange = useCallback(
    (range: DateRange | null) => {
      onFiltersChange({
        ...filters,
        dateRange: range,
      });
    },
    [filters, onFiltersChange]
  );

  // Handle threshold change (single-select dropdown, string[] → number)
  const handleThresholdChange = useCallback(
    (keys: string[]) => {
      const value = keys.length > 0 ? parseInt(keys[0], 10) : 0;
      onFiltersChange({
        ...filters,
        threshold: isNaN(value) ? 0 : value,
      });
    },
    [filters, onFiltersChange]
  );

  // Handle search mode change (single-select dropdown, string[] → SearchMode)
  const handleSearchModeChange = useCallback(
    (keys: string[]) => {
      if (keys.length > 0) {
        onFiltersChange({
          ...filters,
          searchMode: keys[0] as SearchMode,
        });
      }
    },
    [filters, onFiltersChange]
  );

  // Handle Associated Only toggle. The parent has a useEffect that detects
  // this specific flag changing and re-runs the search automatically — this
  // toggle switches the entire backend path (Dataverse-direct vs AI Search)
  // so requiring the user to also click Apply would be poor UX.
  const handleAssociatedOnlyChange = useCallback(
    (ev: React.ChangeEvent<HTMLInputElement>) => {
      onFiltersChange({
        ...filters,
        associatedOnly: ev.currentTarget.checked,
      });
    },
    [filters, onFiltersChange]
  );

  // Scope-aware visibility: hide Matter Type when on any entity-scoped form
  // Only show when scope is "all" (system-wide) or "custom" (document IDs)
  const showMatterTypeFilter = searchScope === 'all' || searchScope === 'custom';

  // Show "Associated Only" toggle only when on an entity form (not "all" scope)
  const showAssociatedOnlyToggle = searchScope !== 'all' && searchScope !== 'custom';

  return (
    <div className={styles.container}>
      {/* Header — Clear (left) and Collapse (right) */}
      <div className={styles.header}>
        {hasActiveFilters ? (
          <Button
            className={styles.clearButton}
            appearance="subtle"
            size="small"
            icon={<Dismiss20Regular />}
            onClick={handleClearFilters}
            disabled={disabled}
          >
            Clear
          </Button>
        ) : (
          <div />
        )}
        {onCollapse && (
          <Button
            className={styles.collapseButton}
            appearance="subtle"
            size="small"
            icon={<ChevronLeft20Regular />}
            onClick={onCollapse}
            aria-label="Collapse filters"
          />
        )}
      </div>

      <Divider />

      {/* Associated Only Toggle — show only documents directly linked to this record */}
      {showAssociatedOnlyToggle && (
        <div className={styles.filterSection}>
          <Switch
            label="Associated Only"
            checked={filters.associatedOnly ?? false}
            onChange={handleAssociatedOnlyChange}
            disabled={disabled}
          />
        </div>
      )}

      {/* File Type Filter */}
      <div className={styles.filterSection}>
        <FilterDropdown
          label="File Type"
          options={fileTypeDropdownOptions}
          selectedValues={filters.fileTypes}
          onChange={handleFileTypesChange}
          isLoading={optionsLoading}
          disabled={disabled}
          multiSelect
        />
      </div>

      {/* Matter Type Filter - hidden when scoped to matter */}
      {showMatterTypeFilter && (
        <div className={styles.filterSection}>
          <FilterDropdown
            label="Matter Type"
            options={matterTypeDropdownOptions}
            selectedValues={filters.matterTypes}
            onChange={handleMatterTypesChange}
            isLoading={optionsLoading}
            disabled={disabled}
            multiSelect
          />
        </div>
      )}

      {/* Date Range Filter */}
      <div className={styles.filterSection}>
        <DateRangeFilter
          label="Date Range"
          value={filters.dateRange}
          onChange={handleDateRangeChange}
          disabled={disabled}
        />
      </div>

      <Divider />

      {/* Threshold Filter */}
      <div className={styles.filterSection}>
        <FilterDropdown
          label="Threshold"
          options={THRESHOLD_OPTIONS}
          selectedValues={[String(filters.threshold)]}
          onChange={handleThresholdChange}
          disabled={disabled}
          multiSelect={false}
        />
      </div>

      {/* Search Mode Filter */}
      <div className={styles.filterSection}>
        <FilterDropdown
          label="Mode"
          options={MODE_OPTIONS}
          selectedValues={[filters.searchMode]}
          onChange={handleSearchModeChange}
          disabled={disabled}
          multiSelect={false}
        />
      </div>
    </div>
  );
};

export default FilterPanel;
