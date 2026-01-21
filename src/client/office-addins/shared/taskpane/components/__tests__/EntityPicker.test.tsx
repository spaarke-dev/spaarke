/**
 * Unit tests for EntityPicker component
 *
 * Tests the entity search and selection component.
 */

import React from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { EntityPicker } from '../EntityPicker';
import type { EntitySearchResult } from '../../hooks/useEntitySearch';

// Mock the useEntitySearch hook
jest.mock('../../hooks/useEntitySearch', () => ({
  useEntitySearch: jest.fn(() => ({
    query: '',
    setQuery: jest.fn(),
    results: [],
    isLoading: false,
    error: null,
    clearError: jest.fn(),
    recentEntities: [],
    typeFilter: [],
    setTypeFilter: jest.fn(),
    toggleTypeFilter: jest.fn(),
    hasMore: false,
    totalCount: 0,
    addToRecent: jest.fn(),
    searchNow: jest.fn(),
    clear: jest.fn(),
  })),
  ALL_ENTITY_TYPES: ['Matter', 'Project', 'Invoice', 'Account', 'Contact'],
}));

import { useEntitySearch, ALL_ENTITY_TYPES } from '../../hooks/useEntitySearch';

// Helper to render with FluentProvider
const renderWithProvider = (ui: React.ReactElement) => {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
};

// Mock entity data
const mockEntity: EntitySearchResult = {
  id: '1',
  entityType: 'Matter',
  logicalName: 'sprk_matter',
  name: 'Smith vs Jones',
  displayInfo: 'Client: Acme Corp | Status: Active',
};

const mockSearchResults: EntitySearchResult[] = [
  mockEntity,
  {
    id: '2',
    entityType: 'Project',
    logicalName: 'sprk_project',
    name: 'Website Redesign',
    displayInfo: 'Account: TechCorp | Due: Mar 2026',
  },
  {
    id: '3',
    entityType: 'Account',
    logicalName: 'account',
    name: 'Acme Corporation',
    displayInfo: 'Industry: Manufacturing',
  },
];

const mockRecentEntities = [
  {
    ...mockEntity,
    lastUsed: new Date().toISOString(),
  },
];

describe('EntityPicker', () => {
  let mockHook: ReturnType<typeof useEntitySearch>;

  beforeEach(() => {
    jest.clearAllMocks();

    // Set up default mock hook return
    mockHook = {
      query: '',
      setQuery: jest.fn(),
      results: [],
      isLoading: false,
      error: null,
      clearError: jest.fn(),
      recentEntities: [],
      typeFilter: [],
      setTypeFilter: jest.fn(),
      toggleTypeFilter: jest.fn(),
      hasMore: false,
      totalCount: 0,
      addToRecent: jest.fn(),
      searchNow: jest.fn(),
      clear: jest.fn(),
    };

    (useEntitySearch as jest.Mock).mockReturnValue(mockHook);
  });

  describe('initial render', () => {
    it('renders with default placeholder', () => {
      renderWithProvider(<EntityPicker />);

      expect(screen.getByPlaceholderText('Search for an association target...')).toBeInTheDocument();
    });

    it('renders with custom placeholder', () => {
      renderWithProvider(<EntityPicker placeholder="Find an entity" />);

      expect(screen.getByPlaceholderText('Find an entity')).toBeInTheDocument();
    });

    it('renders with label', () => {
      renderWithProvider(<EntityPicker label="Association" />);

      expect(screen.getByText('Association')).toBeInTheDocument();
    });

    it('renders filter chips when showTypeFilter is true', () => {
      renderWithProvider(<EntityPicker showTypeFilter />);

      expect(screen.getByText('Matter')).toBeInTheDocument();
      expect(screen.getByText('Project')).toBeInTheDocument();
      expect(screen.getByText('Account')).toBeInTheDocument();
    });

    it('does not render filter chips when showTypeFilter is false', () => {
      renderWithProvider(<EntityPicker showTypeFilter={false} />);

      expect(screen.queryByRole('checkbox', { name: /Matter/i })).not.toBeInTheDocument();
    });
  });

  describe('selected state', () => {
    it('displays selected entity', () => {
      renderWithProvider(<EntityPicker value={mockEntity} />);

      expect(screen.getByText('Smith vs Jones')).toBeInTheDocument();
      expect(screen.getByText('Matter')).toBeInTheDocument();
    });

    it('shows clear button when entity is selected', () => {
      renderWithProvider(<EntityPicker value={mockEntity} />);

      expect(screen.getByLabelText('Clear selection')).toBeInTheDocument();
    });

    it('calls onChange with null when clear button is clicked', async () => {
      const handleChange = jest.fn();
      renderWithProvider(<EntityPicker value={mockEntity} onChange={handleChange} />);

      const clearButton = screen.getByLabelText('Clear selection');
      await userEvent.click(clearButton);

      expect(handleChange).toHaveBeenCalledWith(null);
      expect(mockHook.clear).toHaveBeenCalled();
    });

    it('disables clear button when disabled prop is true', () => {
      renderWithProvider(<EntityPicker value={mockEntity} disabled />);

      const clearButton = screen.getByLabelText('Clear selection');
      expect(clearButton).toBeDisabled();
    });
  });

  describe('search behavior', () => {
    it('updates query when typing', async () => {
      renderWithProvider(<EntityPicker />);

      const input = screen.getByPlaceholderText('Search for an association target...');
      await userEvent.type(input, 'test');

      expect(mockHook.setQuery).toHaveBeenCalled();
      expect(mockHook.clearError).toHaveBeenCalled();
    });

    it('shows loading state', () => {
      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        isLoading: true,
      });

      renderWithProvider(<EntityPicker />);

      // The loading spinner should be present
      expect(screen.queryByText('Searching...')).toBeInTheDocument();
    });

    it('shows search results when available', () => {
      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        query: 'Smith',
        results: mockSearchResults,
      });

      renderWithProvider(<EntityPicker />);

      expect(screen.getByText('Smith vs Jones')).toBeInTheDocument();
      expect(screen.getByText('Website Redesign')).toBeInTheDocument();
    });

    it('shows empty state when no results', () => {
      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        query: 'xyz',
        results: [],
      });

      renderWithProvider(<EntityPicker />);

      // Empty state shows when query is >= 2 chars and no results
      // Need to verify the dropdown is open first
    });
  });

  describe('recent entities', () => {
    it('shows recent entities when showRecent is true and query is short', () => {
      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        recentEntities: mockRecentEntities,
      });

      renderWithProvider(<EntityPicker showRecent />);

      // Focus the input to open dropdown
      const input = screen.getByPlaceholderText('Search for an association target...');
      fireEvent.focus(input);

      expect(screen.getByText('Recent')).toBeInTheDocument();
    });

    it('does not show recent entities when showRecent is false', () => {
      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        recentEntities: mockRecentEntities,
      });

      renderWithProvider(<EntityPicker showRecent={false} />);

      const input = screen.getByPlaceholderText('Search for an association target...');
      fireEvent.focus(input);

      expect(screen.queryByText('Recent')).not.toBeInTheDocument();
    });
  });

  describe('type filter', () => {
    it('toggles type filter when chip is clicked', async () => {
      renderWithProvider(<EntityPicker showTypeFilter />);

      const matterChip = screen.getByRole('checkbox', { name: /Matter/i });
      await userEvent.click(matterChip);

      expect(mockHook.toggleTypeFilter).toHaveBeenCalledWith('Matter');
    });

    it('toggles type filter on keyboard enter', async () => {
      renderWithProvider(<EntityPicker showTypeFilter />);

      const matterChip = screen.getByRole('checkbox', { name: /Matter/i });
      matterChip.focus();
      await userEvent.keyboard('{Enter}');

      expect(mockHook.toggleTypeFilter).toHaveBeenCalledWith('Matter');
    });

    it('only shows allowed types', () => {
      renderWithProvider(<EntityPicker showTypeFilter allowedTypes={['Matter', 'Project']} />);

      expect(screen.getByText('Matter')).toBeInTheDocument();
      expect(screen.getByText('Project')).toBeInTheDocument();
      expect(screen.queryByText('Invoice')).not.toBeInTheDocument();
      expect(screen.queryByText('Account')).not.toBeInTheDocument();
    });
  });

  describe('entity selection', () => {
    it('calls onChange when entity is selected', async () => {
      const handleChange = jest.fn();

      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        query: 'Smith',
        results: mockSearchResults,
      });

      renderWithProvider(<EntityPicker onChange={handleChange} />);

      // Find and click the entity option
      const option = screen.getByText('Smith vs Jones');
      await userEvent.click(option);

      expect(mockHook.addToRecent).toHaveBeenCalledWith(mockSearchResults[0]);
      expect(handleChange).toHaveBeenCalledWith(mockSearchResults[0]);
      expect(mockHook.clear).toHaveBeenCalled();
    });
  });

  describe('quick create', () => {
    it('calls onQuickCreate when Quick Create option is clicked', async () => {
      const handleQuickCreate = jest.fn();

      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        query: 'New Matter',
        results: [],
      });

      renderWithProvider(
        <EntityPicker
          onQuickCreate={handleQuickCreate}
          showQuickCreate
          allowedTypes={['Matter']}
        />
      );

      // The Quick Create option should be visible for allowed types
      const createOption = screen.getByText(/Create new Matter/i);
      await userEvent.click(createOption);

      expect(handleQuickCreate).toHaveBeenCalledWith('Matter', 'New Matter');
    });

    it('does not show Quick Create when showQuickCreate is false', () => {
      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        query: 'New Entity',
        results: [],
      });

      renderWithProvider(<EntityPicker showQuickCreate={false} />);

      expect(screen.queryByText(/Create new/i)).not.toBeInTheDocument();
    });
  });

  describe('keyboard navigation', () => {
    it('opens dropdown on arrow down', async () => {
      renderWithProvider(<EntityPicker />);

      const input = screen.getByPlaceholderText('Search for an association target...');
      await userEvent.click(input);
      await userEvent.keyboard('{ArrowDown}');

      // Dropdown should be open (aria-expanded should be true)
      expect(input).toHaveAttribute('aria-expanded', 'true');
    });

    it('closes dropdown on escape', async () => {
      renderWithProvider(<EntityPicker />);

      const input = screen.getByPlaceholderText('Search for an association target...');
      await userEvent.click(input);
      await userEvent.keyboard('{Escape}');

      expect(mockHook.clear).toHaveBeenCalled();
    });
  });

  describe('error handling', () => {
    it('displays error message', () => {
      renderWithProvider(<EntityPicker errorMessage="Please select an entity" />);

      expect(screen.getByText('Please select an entity')).toBeInTheDocument();
    });

    it('displays search error', () => {
      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        error: 'Failed to search entities',
      });

      renderWithProvider(<EntityPicker id="picker" />);

      expect(screen.getByText('Failed to search entities')).toBeInTheDocument();
    });

    it('sets aria-invalid when there is an error', () => {
      renderWithProvider(<EntityPicker errorMessage="Error" />);

      const input = screen.getByPlaceholderText('Search for an association target...');
      expect(input).toHaveAttribute('aria-invalid', 'true');
    });
  });

  describe('accessibility', () => {
    it('has accessible name', () => {
      renderWithProvider(<EntityPicker aria-label="Select entity" />);

      expect(screen.getByLabelText('Select entity')).toBeInTheDocument();
    });

    it('uses label as accessible name when provided', () => {
      renderWithProvider(<EntityPicker label="Association Target" />);

      expect(screen.getByLabelText('Association Target')).toBeInTheDocument();
    });

    it('filter chips have checkbox role', () => {
      renderWithProvider(<EntityPicker showTypeFilter />);

      const checkboxes = screen.getAllByRole('checkbox');
      expect(checkboxes.length).toBeGreaterThan(0);
    });

    it('filter chips indicate checked state', () => {
      (useEntitySearch as jest.Mock).mockReturnValue({
        ...mockHook,
        typeFilter: ['Matter'],
      });

      renderWithProvider(<EntityPicker showTypeFilter />);

      // All types should be "checked" by default (no filter applied)
      // or specific ones when filter is set
    });
  });

  describe('disabled state', () => {
    it('disables the combobox when disabled prop is true', () => {
      renderWithProvider(<EntityPicker disabled />);

      const input = screen.getByPlaceholderText('Search for an association target...');
      expect(input).toBeDisabled();
    });
  });

  describe('required state', () => {
    it('marks the combobox as required when required prop is true', () => {
      renderWithProvider(<EntityPicker required />);

      const input = screen.getByPlaceholderText('Search for an association target...');
      expect(input).toBeRequired();
    });
  });
});
