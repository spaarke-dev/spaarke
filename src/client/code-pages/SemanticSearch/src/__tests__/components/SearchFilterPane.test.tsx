/**
 * Unit tests for SearchFilterPane component
 *
 * The pane composes: Search Index selector, Saved Searches selector, AI Search query textarea,
 * domain-aware filter dropdowns (Document Type / File Type / Matter Type), Date Range, Relevance
 * Threshold, Search Mode, and the Search / Cancel action row. Child components (SearchIndexSelector,
 * SavedSearchSelector, FilterDropdown, DateRangeFilter) are rendered for real.
 *
 * Covers:
 *   - Domain-aware filter section visibility
 *   - Expand/collapse behaviour
 *   - Search Index selector wiring (loading / empty / populated / selection)
 *   - Saved Searches selector wiring
 *   - AI Search query textarea (controlled; Ctrl/Cmd+Enter searches)
 *   - Type filters, Date Range preset, Relevance Threshold and Search Mode report through onFiltersChange
 *   - Search (disabled while loading) and Cancel actions
 */

import React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SearchFilterPane, type SearchFilterPaneProps } from '../../components/SearchFilterPane';
import type { SearchFilters, SearchDomain } from '../../types';
import type { AiSearchIndexRow } from '../../services/aiSearchIndexService';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const DEFAULT_FILTERS: SearchFilters = {
  documentTypes: [],
  fileTypes: [],
  matterTypes: [],
  dateRange: { from: null, to: null },
  threshold: 50,
  searchMode: 'rrf',
};

function makeIndex(id: string, displayName: string, overrides: Partial<AiSearchIndexRow> = {}): AiSearchIndexRow {
  return {
    sprk_aisearchindexid: id,
    sprk_displayname: displayName,
    sprk_searchindexname: `idx-${id}`,
    sprk_targetentitytype: null,
    sprk_targetentitytypeLabel: 'All',
    sprk_isdefault: false,
    sprk_displayorder: 1,
    ...overrides,
  };
}

const INDEX_A = makeIndex('index-a', 'Development Files');
const INDEX_B = makeIndex('index-b', 'Matter Records', { sprk_targetentitytypeLabel: 'Matter' });

const DEFAULT_PROPS: SearchFilterPaneProps = {
  activeDomain: 'documents',
  searchIndexes: [INDEX_A, INDEX_B],
  selectedSearchIndexId: 'index-a',
  isLoadingSearchIndexes: false,
  onSelectSearchIndex: jest.fn(),
  filters: DEFAULT_FILTERS,
  onFiltersChange: jest.fn(),
  onSearch: jest.fn(),
  filterOptions: {
    documentTypes: [
      { value: 'contract', label: 'Contract' },
      { value: 'nda', label: 'NDA' },
    ],
    fileTypes: [{ value: 'pdf', label: 'PDF' }],
    matterTypes: [{ value: 'litigation', label: 'Litigation' }],
  },
  isLoading: false,
  query: '',
  onQueryChange: jest.fn(),
  savedSearches: [],
  currentSearchName: null,
  onSelectSavedSearch: jest.fn(),
  isSavedSearchesLoading: false,
  onCancel: jest.fn(),
};

function renderPane(props: Partial<SearchFilterPaneProps> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SearchFilterPane {...DEFAULT_PROPS} {...props} />
    </FluentProvider>
  );
}

const ALL_DOMAINS: SearchDomain[] = ['documents', 'matters', 'projects', 'invoices'];

/** The Search action button (its accessible name is exactly "Search"; Cancel carries its own aria-label). */
function searchButton() {
  return screen.getByRole('button', { name: 'Search' });
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SearchFilterPane', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  // ---- Domain Visibility ----

  describe('domain-aware filter visibility', () => {
    it('shows Document Type, File Type and Matter Type filters for the documents domain', () => {
      renderPane({ activeDomain: 'documents' });

      expect(screen.getByText('Document Type')).toBeInTheDocument();
      expect(screen.getByText('File Type')).toBeInTheDocument();
      expect(screen.getByText('Matter Type')).toBeInTheDocument();
    });

    it('shows only the Matter Type filter for the matters domain', () => {
      renderPane({ activeDomain: 'matters' });

      expect(screen.getByText('Matter Type')).toBeInTheDocument();
      expect(screen.queryByText('Document Type')).not.toBeInTheDocument();
      expect(screen.queryByText('File Type')).not.toBeInTheDocument();
    });

    it.each<SearchDomain>(['projects', 'invoices'])('hides all three type filters for the %s domain', domain => {
      renderPane({ activeDomain: domain });

      expect(screen.queryByText('Document Type')).not.toBeInTheDocument();
      expect(screen.queryByText('File Type')).not.toBeInTheDocument();
      expect(screen.queryByText('Matter Type')).not.toBeInTheDocument();
    });

    it.each(ALL_DOMAINS)('always shows Date Range, Relevance Threshold and Search Mode (%s domain)', domain => {
      renderPane({ activeDomain: domain });

      expect(screen.getByText('Date Range')).toBeInTheDocument();
      expect(screen.getByText('Relevance Threshold')).toBeInTheDocument();
      expect(screen.getByText('Search Mode')).toBeInTheDocument();
    });
  });

  // ---- Expand / Collapse ----

  describe('expand/collapse toggle', () => {
    it('renders expanded by default with the Search Criteria title', () => {
      renderPane();

      expect(screen.getByText('Search Criteria')).toBeInTheDocument();
      expect(screen.getByLabelText('Collapse filters')).toBeInTheDocument();
    });

    it('collapses when the collapse button is clicked, leaving only the expand button', () => {
      renderPane();

      fireEvent.click(screen.getByLabelText('Collapse filters'));

      expect(screen.queryByText('Search Criteria')).not.toBeInTheDocument();
      expect(screen.getByLabelText('Expand filters')).toBeInTheDocument();
      expect(screen.queryByLabelText('Collapse filters')).not.toBeInTheDocument();
    });

    it('re-expands when the expand button is clicked after collapse', () => {
      renderPane();

      fireEvent.click(screen.getByLabelText('Collapse filters'));
      fireEvent.click(screen.getByLabelText('Expand filters'));

      expect(screen.getByText('Search Criteria')).toBeInTheDocument();
    });

    it('hides every control (index, saved searches, query, filters, actions) when collapsed', () => {
      renderPane({ activeDomain: 'documents' });

      fireEvent.click(screen.getByLabelText('Collapse filters'));

      expect(screen.queryByText('Search Index')).not.toBeInTheDocument();
      expect(screen.queryByText('Saved Searches')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('AI search query')).not.toBeInTheDocument();
      expect(screen.queryByText('Document Type')).not.toBeInTheDocument();
      expect(screen.queryByText('Date Range')).not.toBeInTheDocument();
      expect(screen.queryByText('Search Mode')).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Search' })).not.toBeInTheDocument();
    });

    it('does not trigger a search or filter change when collapsing', () => {
      const onSearch = jest.fn();
      const onFiltersChange = jest.fn();
      renderPane({ onSearch, onFiltersChange });

      fireEvent.click(screen.getByLabelText('Collapse filters'));

      expect(onSearch).not.toHaveBeenCalled();
      expect(onFiltersChange).not.toHaveBeenCalled();
    });
  });

  // ---- Search Index selector ----

  describe('search index selector', () => {
    it('shows the selected index name in the Search index dropdown', () => {
      renderPane({ selectedSearchIndexId: 'index-b' });

      expect(screen.getByRole('combobox', { name: 'Search index' })).toHaveTextContent('Matter Records');
    });

    it('calls onSelectSearchIndex with the chosen row', () => {
      const onSelectSearchIndex = jest.fn();
      renderPane({ onSelectSearchIndex });

      fireEvent.click(screen.getByRole('combobox', { name: 'Search index' }));
      fireEvent.click(screen.getByRole('option', { name: 'Matter Records' }));

      expect(onSelectSearchIndex).toHaveBeenCalledTimes(1);
      expect(onSelectSearchIndex).toHaveBeenCalledWith(INDEX_B);
    });

    it('shows a loading indicator instead of the dropdown while indexes load', () => {
      renderPane({ isLoadingSearchIndexes: true, searchIndexes: [] });

      expect(screen.getByText('Loading indexes...')).toBeInTheDocument();
      expect(screen.queryByRole('combobox', { name: 'Search index' })).not.toBeInTheDocument();
    });

    it('shows a disabled dropdown and a hint when no index is available for the role', () => {
      renderPane({ searchIndexes: [], selectedSearchIndexId: '' });

      expect(screen.getByText('No indexes configured for your role.')).toBeInTheDocument();
      expect(screen.getByRole('combobox', { name: 'Search index (none available)' })).toBeDisabled();
    });
  });

  // ---- Saved Searches selector ----

  describe('saved searches selector', () => {
    it('shows the active saved-search name', () => {
      renderPane({ currentSearchName: 'Recent Documents' });

      expect(screen.getByRole('combobox', { name: 'Saved searches' })).toHaveTextContent('Recent Documents');
    });

    it('forwards a selected saved search to onSelectSavedSearch', () => {
      const onSelectSavedSearch = jest.fn();
      renderPane({ onSelectSavedSearch });

      fireEvent.click(screen.getByRole('combobox', { name: 'Saved searches' }));
      fireEvent.click(screen.getByRole('option', { name: 'All Matters' }));

      expect(onSelectSavedSearch).toHaveBeenCalledTimes(1);
      expect(onSelectSavedSearch.mock.calls[0][0]).toMatchObject({ id: 'default-all-matters', searchDomain: 'matters' });
    });

    it('shows the saved-search loading indicator while saved searches load', () => {
      renderPane({ isSavedSearchesLoading: true });

      expect(screen.getByText('Loading...')).toBeInTheDocument();
      expect(screen.queryByRole('combobox', { name: 'Saved searches' })).not.toBeInTheDocument();
    });
  });

  // ---- AI Search query ----

  describe('AI search query', () => {
    it('shows the controlled query value', () => {
      renderPane({ query: 'indemnity clauses' });

      expect(screen.getByLabelText('AI search query')).toHaveValue('indemnity clauses');
    });

    it('reports typing through onQueryChange', () => {
      const onQueryChange = jest.fn();
      renderPane({ onQueryChange });

      fireEvent.change(screen.getByLabelText('AI search query'), { target: { value: 'termination' } });

      expect(onQueryChange).toHaveBeenCalledWith('termination');
    });

    it('searches with the current query and filters on Ctrl+Enter', () => {
      const onSearch = jest.fn();
      renderPane({ onSearch, query: 'termination' });

      fireEvent.keyDown(screen.getByLabelText('AI search query'), { key: 'Enter', ctrlKey: true });

      expect(onSearch).toHaveBeenCalledTimes(1);
      expect(onSearch).toHaveBeenCalledWith('termination', DEFAULT_FILTERS);
    });

    it('searches on Cmd+Enter as well', () => {
      const onSearch = jest.fn();
      renderPane({ onSearch, query: 'termination' });

      fireEvent.keyDown(screen.getByLabelText('AI search query'), { key: 'Enter', metaKey: true });

      expect(onSearch).toHaveBeenCalledTimes(1);
    });

    it('does not search on a plain Enter (newline in the textarea)', () => {
      const onSearch = jest.fn();
      renderPane({ onSearch, query: 'termination' });

      fireEvent.keyDown(screen.getByLabelText('AI search query'), { key: 'Enter' });

      expect(onSearch).not.toHaveBeenCalled();
    });
  });

  // ---- Type filters ----

  describe('type filters', () => {
    it('shows "All" on each visible type filter when nothing is selected', () => {
      renderPane({ activeDomain: 'documents' });

      ['Document Type', 'File Type', 'Matter Type'].forEach(label => {
        expect(screen.getByRole('combobox', { name: label })).toHaveTextContent('All');
      });
    });

    it('shows the selected option label on a type filter', () => {
      renderPane({ filters: { ...DEFAULT_FILTERS, documentTypes: ['nda'] } });

      expect(screen.getByRole('combobox', { name: 'Document Type' })).toHaveTextContent('NDA');
    });

    // These filters are `multiselect`, so Fluent renders the list as role="menu" with "menuitemcheckbox" items.
    it('reports a Document Type selection through onFiltersChange, preserving the other filters', () => {
      const onFiltersChange = jest.fn();
      renderPane({ onFiltersChange, filters: { ...DEFAULT_FILTERS, threshold: 80 } });

      fireEvent.click(screen.getByRole('combobox', { name: 'Document Type' }));
      fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Contract' }));

      expect(onFiltersChange).toHaveBeenCalledTimes(1);
      expect(onFiltersChange).toHaveBeenCalledWith({ ...DEFAULT_FILTERS, threshold: 80, documentTypes: ['contract'] });
    });

    it('reports a File Type selection under fileTypes', () => {
      const onFiltersChange = jest.fn();
      renderPane({ onFiltersChange });

      fireEvent.click(screen.getByRole('combobox', { name: 'File Type' }));
      fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'PDF' }));

      expect(onFiltersChange).toHaveBeenCalledWith({ ...DEFAULT_FILTERS, fileTypes: ['pdf'] });
    });

    it('reports a Matter Type selection under matterTypes', () => {
      const onFiltersChange = jest.fn();
      renderPane({ onFiltersChange, activeDomain: 'matters' });

      fireEvent.click(screen.getByRole('combobox', { name: 'Matter Type' }));
      fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Litigation' }));

      expect(onFiltersChange).toHaveBeenCalledWith({ ...DEFAULT_FILTERS, matterTypes: ['litigation'] });
    });
  });

  // ---- Date Range ----

  describe('date range', () => {
    it('shows the Off preset and no date inputs when no range is set', () => {
      renderPane();

      expect(screen.getByRole('combobox', { name: 'Date range preset' })).toHaveTextContent('Off');
      expect(screen.queryByLabelText('From date')).not.toBeInTheDocument();
    });

    it('shows the From/To inputs once a range is set', () => {
      renderPane({ filters: { ...DEFAULT_FILTERS, dateRange: { from: '2026-01-01', to: '2026-03-31' } } });

      expect(screen.getByLabelText('From date')).toHaveValue('2026-01-01');
      expect(screen.getByLabelText('To date')).toHaveValue('2026-03-31');
    });

    it('reports a populated dateRange when a preset is chosen', () => {
      const onFiltersChange = jest.fn();
      renderPane({ onFiltersChange });

      fireEvent.click(screen.getByRole('combobox', { name: 'Date range preset' }));
      fireEvent.click(screen.getByRole('option', { name: 'Last 30 days' }));

      expect(onFiltersChange).toHaveBeenCalledTimes(1);
      const { dateRange } = onFiltersChange.mock.calls[0][0] as SearchFilters;
      expect(dateRange.from).toEqual(expect.stringMatching(/^\d{4}-\d{2}-\d{2}/));
      expect(dateRange.to).toEqual(expect.stringMatching(/^\d{4}-\d{2}-\d{2}/));
      expect(dateRange.from! < dateRange.to!).toBe(true); // a 30-day window ends after it starts
    });
  });

  // ---- Threshold Slider ----

  describe('relevance threshold', () => {
    it.each([0, 75, 100])('displays a threshold of %i%%', threshold => {
      renderPane({ filters: { ...DEFAULT_FILTERS, threshold } });

      expect(screen.getByText(`${threshold}%`)).toBeInTheDocument();
      expect(screen.getByRole('slider')).toHaveValue(String(threshold));
    });

    it('reports a slider change through onFiltersChange', () => {
      const onFiltersChange = jest.fn();
      renderPane({ onFiltersChange });

      fireEvent.change(screen.getByRole('slider'), { target: { value: '80' } });

      expect(onFiltersChange).toHaveBeenCalledWith({ ...DEFAULT_FILTERS, threshold: 80 });
    });
  });

  // ---- Search Mode Dropdown ----

  describe('search mode dropdown', () => {
    it.each([
      ['rrf', 'Hybrid (RRF)'],
      ['vectorOnly', 'Vector Only'],
      ['keywordOnly', 'Keyword Only'],
    ] as const)('shows the %s mode as "%s"', (searchMode, label) => {
      renderPane({ filters: { ...DEFAULT_FILTERS, searchMode } });

      expect(screen.getByRole('combobox', { name: 'Search mode' })).toHaveTextContent(label);
    });

    it('reports a mode change through onFiltersChange', () => {
      const onFiltersChange = jest.fn();
      renderPane({ onFiltersChange });

      fireEvent.click(screen.getByRole('combobox', { name: 'Search mode' }));
      fireEvent.click(screen.getByRole('option', { name: 'Keyword Only' }));

      expect(onFiltersChange).toHaveBeenCalledWith({ ...DEFAULT_FILTERS, searchMode: 'keywordOnly' });
    });

    it('offers exactly the three search modes', () => {
      renderPane();

      fireEvent.click(screen.getByRole('combobox', { name: 'Search mode' }));

      const listbox = screen.getByRole('listbox');
      expect(
        within(listbox)
          .getAllByRole('option')
          .map(o => o.textContent)
      ).toEqual(['Hybrid (RRF)', 'Vector Only', 'Keyword Only']);
    });
  });

  // ---- Action row ----

  describe('search button', () => {
    it('calls onSearch with the current query and filters when clicked', () => {
      const onSearch = jest.fn();
      const filters = { ...DEFAULT_FILTERS, threshold: 65 };
      renderPane({ onSearch, query: 'force majeure', filters });

      fireEvent.click(searchButton());

      expect(onSearch).toHaveBeenCalledTimes(1);
      expect(onSearch).toHaveBeenCalledWith('force majeure', filters);
    });

    it('disables the Search button when isLoading is true, and does not search when clicked', () => {
      const onSearch = jest.fn();
      renderPane({ isLoading: true, onSearch });

      expect(searchButton()).toBeDisabled();
      fireEvent.click(searchButton());

      expect(onSearch).not.toHaveBeenCalled();
    });

    it('enables the Search button when isLoading is false', () => {
      renderPane({ isLoading: false });

      expect(searchButton()).not.toBeDisabled();
    });
  });

  describe('cancel button', () => {
    it('calls onCancel (clear search criteria) when clicked', () => {
      const onCancel = jest.fn();
      renderPane({ onCancel });

      fireEvent.click(screen.getByRole('button', { name: 'Clear search criteria' }));

      expect(onCancel).toHaveBeenCalledTimes(1);
    });

    it('stays enabled while a search is in progress so the user can clear', () => {
      renderPane({ isLoading: true });

      expect(screen.getByRole('button', { name: 'Clear search criteria' })).not.toBeDisabled();
    });
  });
});
