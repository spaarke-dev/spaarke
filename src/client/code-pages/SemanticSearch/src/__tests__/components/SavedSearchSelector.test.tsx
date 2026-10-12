/**
 * Unit tests for SavedSearchSelector component
 *
 * The selector is a labelled Fluent v9 Dropdown ("Saved Searches") listing four built-in default
 * searches plus the user's personal saved searches. Saving the current search is done from the command
 * bar's overflow menu ("Save Search"; see SearchCommandBar.test.tsx), not from this dropdown.
 *
 * Covers:
 *   - Label + trigger rendering (placeholder vs. current search name)
 *   - Default searches group, personal "My Searches" group (only when there are personal searches)
 *   - Loading state (spinner replaces the dropdown)
 *   - The active search is the selected option
 *   - Selection callback invocations with the full SavedSearch
 */

import React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SavedSearchSelector, type SavedSearchSelectorProps } from '../../components/SavedSearchSelector';
import type { SavedSearch, SearchFilters } from '../../types';

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

function makeSavedSearch(overrides: Partial<SavedSearch> = {}): SavedSearch {
  return {
    id: 'test-1',
    name: 'Test Search',
    searchDomain: 'documents',
    query: 'test',
    filters: DEFAULT_FILTERS,
    viewMode: 'grid',
    columns: ['name'],
    sortColumn: 'name',
    sortDirection: 'asc',
    ...overrides,
  };
}

const DEFAULT_PROPS: SavedSearchSelectorProps = {
  savedSearches: [],
  currentSearchName: null,
  onSelectSavedSearch: jest.fn(),
  isLoading: false,
};

function renderSelector(props: Partial<SavedSearchSelectorProps> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <SavedSearchSelector {...DEFAULT_PROPS} {...props} />
    </FluentProvider>
  );
}

const DEFAULT_SEARCH_NAMES = ['All Documents', 'All Matters', 'Recent Documents', 'High Similarity'];

function trigger() {
  return screen.getByRole('combobox', { name: 'Saved searches' });
}

function openDropdown() {
  fireEvent.click(trigger());
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('SavedSearchSelector', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  // ---- Trigger ----

  describe('trigger', () => {
    it("renders the 'Saved Searches' label above the dropdown", () => {
      renderSelector();

      expect(screen.getByText('Saved Searches')).toBeInTheDocument();
      expect(trigger()).toBeInTheDocument();
    });

    it('shows the placeholder when no search is active', () => {
      renderSelector({ currentSearchName: null });

      expect(trigger()).toHaveTextContent('Select a saved search...');
    });

    it('shows the current search name when one is active', () => {
      renderSelector({ currentSearchName: 'My Custom Filter' });

      expect(trigger()).toHaveTextContent('My Custom Filter');
      expect(trigger()).not.toHaveTextContent('Select a saved search...');
    });

    it('renders a long current search name in full (truncation is CSS-only)', () => {
      const longName = 'This is a very long saved search name that should be truncated by CSS';
      renderSelector({ currentSearchName: longName });

      expect(trigger()).toHaveTextContent(longName);
    });
  });

  // ---- Loading ----

  describe('loading state', () => {
    it('shows a spinner instead of the dropdown while loading', () => {
      renderSelector({ isLoading: true });

      expect(screen.getByText('Loading...')).toBeInTheDocument();
      expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
    });

    it('shows the dropdown, not the spinner, when not loading', () => {
      renderSelector({ isLoading: false });

      expect(screen.queryByText('Loading...')).not.toBeInTheDocument();
      expect(trigger()).toBeInTheDocument();
    });
  });

  // ---- Options (requires opening the dropdown) ----

  describe('when the dropdown is opened', () => {
    it('lists the four default searches under the Default Searches group', () => {
      renderSelector();
      openDropdown();

      const group = screen.getByRole('group', { name: 'Default Searches' });
      const names = within(group)
        .getAllByRole('option')
        .map(o => o.textContent);
      expect(names).toEqual(DEFAULT_SEARCH_NAMES);
    });

    it('omits the My Searches group when there are no personal searches', () => {
      renderSelector({ savedSearches: [] });
      openDropdown();

      expect(screen.queryByRole('group', { name: 'My Searches' })).not.toBeInTheDocument();
      expect(screen.getAllByRole('option')).toHaveLength(DEFAULT_SEARCH_NAMES.length);
    });

    it('lists personal saved searches under the My Searches group, after the defaults', () => {
      const personal = [
        makeSavedSearch({ id: 'p1', name: 'Contracts Q1' }),
        makeSavedSearch({ id: 'p2', name: 'Invoices Review' }),
      ];
      renderSelector({ savedSearches: personal });
      openDropdown();

      const group = screen.getByRole('group', { name: 'My Searches' });
      expect(
        within(group)
          .getAllByRole('option')
          .map(o => o.textContent)
      ).toEqual(['Contracts Q1', 'Invoices Review']);
      expect(screen.getAllByRole('option')).toHaveLength(DEFAULT_SEARCH_NAMES.length + 2);
    });

    it('marks the option matching currentSearchName as selected (and only that one)', () => {
      renderSelector({ currentSearchName: 'All Matters' });
      openDropdown();

      expect(screen.getByRole('option', { name: 'All Matters' })).toHaveAttribute('aria-selected', 'true');
      expect(screen.getByRole('option', { name: 'All Documents' })).toHaveAttribute('aria-selected', 'false');
    });

    it('marks no option as selected when no search is active', () => {
      renderSelector({ currentSearchName: null });
      openDropdown();

      screen.getAllByRole('option').forEach(o => expect(o).toHaveAttribute('aria-selected', 'false'));
    });
  });

  // ---- Callbacks ----

  describe('callback invocations', () => {
    it('calls onSelectSavedSearch with the full default search when one is clicked', () => {
      const onSelect = jest.fn();
      renderSelector({ onSelectSavedSearch: onSelect });
      openDropdown();

      fireEvent.click(screen.getByRole('option', { name: 'All Documents' }));

      expect(onSelect).toHaveBeenCalledTimes(1);
      expect(onSelect.mock.calls[0][0]).toMatchObject({
        id: 'default-all-documents',
        name: 'All Documents',
        searchDomain: 'documents',
        viewMode: 'grid',
      });
    });

    it('resolves a default search of a different domain (All Matters → matters)', () => {
      const onSelect = jest.fn();
      renderSelector({ onSelectSavedSearch: onSelect });
      openDropdown();

      fireEvent.click(screen.getByRole('option', { name: 'All Matters' }));

      expect(onSelect.mock.calls[0][0]).toMatchObject({ id: 'default-all-matters', searchDomain: 'matters' });
    });

    it('applies the High Similarity default threshold (75)', () => {
      const onSelect = jest.fn();
      renderSelector({ onSelectSavedSearch: onSelect });
      openDropdown();

      fireEvent.click(screen.getByRole('option', { name: 'High Similarity' }));

      expect(onSelect.mock.calls[0][0].filters.threshold).toBe(75);
    });

    it('calls onSelectSavedSearch with the personal search object when one is clicked', () => {
      const onSelect = jest.fn();
      const mine = makeSavedSearch({ id: 'my-1', name: 'My Filter', query: 'indemnity' });
      renderSelector({ onSelectSavedSearch: onSelect, savedSearches: [mine] });
      openDropdown();

      fireEvent.click(screen.getByRole('option', { name: 'My Filter' }));

      expect(onSelect).toHaveBeenCalledTimes(1);
      expect(onSelect.mock.calls[0][0]).toBe(mine);
    });


    it('does not call onSelectSavedSearch just by opening the dropdown', () => {
      const onSelect = jest.fn();
      renderSelector({ onSelectSavedSearch: onSelect });
      openDropdown();

      expect(onSelect).not.toHaveBeenCalled();
    });
  });
});
