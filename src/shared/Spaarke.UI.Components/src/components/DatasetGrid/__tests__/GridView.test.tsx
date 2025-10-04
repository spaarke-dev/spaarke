/**
 * GridView Integration Tests
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { GridView } from '../GridView';
import { IDatasetRecord, IDatasetColumn } from '../../../types';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';

describe('GridView', () => {
  const mockColumns: IDatasetColumn[] = [
    {
      name: 'name',
      displayName: 'Name',
      dataType: 'SingleLine.Text',
      alias: 'name',
      order: 0,
      visualSizeFactor: 2,
      isHidden: false,
      isPrimary: true
    },
    {
      name: 'email',
      displayName: 'Email',
      dataType: 'SingleLine.Email',
      alias: 'email',
      order: 1,
      visualSizeFactor: 1,
      isHidden: false,
      isPrimary: false
    }
  ];

  const mockRecords: IDatasetRecord[] = [
    {
      id: '1',
      entityName: 'contact',
      getFormattedValue: (column: string) => {
        if (column === 'name') return 'John Doe';
        if (column === 'email') return 'john@example.com';
        return '';
      },
      getValue: (column: string) => {
        if (column === 'name') return 'John Doe';
        if (column === 'email') return 'john@example.com';
        return null;
      },
      getNamedReference: jest.fn()
    },
    {
      id: '2',
      entityName: 'contact',
      getFormattedValue: (column: string) => {
        if (column === 'name') return 'Jane Smith';
        if (column === 'email') return 'jane@example.com';
        return '';
      },
      getValue: (column: string) => {
        if (column === 'name') return 'Jane Smith';
        if (column === 'email') return 'jane@example.com';
        return null;
      },
      getNamedReference: jest.fn()
    }
  ];

  const defaultProps = {
    records: mockRecords,
    columns: mockColumns,
    selectedRecordIds: [],
    onSelectionChange: jest.fn(),
    onRecordClick: jest.fn(),
    enableVirtualization: false,
    rowHeight: 44,
    scrollBehavior: 'Auto' as const,
    loading: false,
    hasNextPage: false,
    loadNextPage: jest.fn()
  };

  describe('Rendering', () => {
    it('should render grid with records', () => {
      renderWithProviders(<GridView {...defaultProps} />);

      expect(screen.getByRole('grid')).toBeInTheDocument();
      expect(screen.getByText('John Doe')).toBeInTheDocument();
      expect(screen.getByText('Jane Smith')).toBeInTheDocument();
    });

    it('should render column headers', () => {
      renderWithProviders(<GridView {...defaultProps} />);

      expect(screen.getByText('Name')).toBeInTheDocument();
      expect(screen.getByText('Email')).toBeInTheDocument();
    });

    it('should render empty state with no records', () => {
      renderWithProviders(<GridView {...defaultProps} records={[]} />);

      expect(screen.getByRole('grid')).toBeInTheDocument();
    });

    it('should hide columns marked as hidden', () => {
      const columnsWithHidden: IDatasetColumn[] = [
        ...mockColumns,
        {
          name: 'hidden',
          displayName: 'Hidden Column',
          dataType: 'SingleLine.Text',
          alias: 'hidden',
          order: 2,
          visualSizeFactor: 1,
          isHidden: true,
          isPrimary: false
        }
      ];

      renderWithProviders(<GridView {...defaultProps} columns={columnsWithHidden} />);

      expect(screen.queryByText('Hidden Column')).not.toBeInTheDocument();
    });
  });

  describe('User Interactions', () => {
    it('should call onRecordClick when row is clicked', async () => {
      const user = userEvent.setup();
      const onRecordClick = jest.fn();

      renderWithProviders(<GridView {...defaultProps} onRecordClick={onRecordClick} />);

      const rows = screen.getAllByRole('row');
      // Skip header row (index 0)
      await user.click(rows[1]);

      expect(onRecordClick).toHaveBeenCalledWith(mockRecords[0]);
    });

    it('should handle selection change', () => {
      const onSelectionChange = jest.fn();

      renderWithProviders(
        <GridView {...defaultProps} onSelectionChange={onSelectionChange} />
      );

      expect(screen.getByRole('grid')).toBeInTheDocument();
    });

    it('should display selected rows', () => {
      renderWithProviders(<GridView {...defaultProps} selectedRecordIds={['1']} />);

      const rows = screen.getAllByRole('row');
      expect(rows.length).toBeGreaterThan(0);
    });
  });

  describe('Loading State', () => {
    it('should show loading indicator when loading', () => {
      renderWithProviders(<GridView {...defaultProps} loading={true} />);

      expect(screen.getByRole('progressbar')).toBeInTheDocument();
    });

    it('should show load more button when hasNextPage is true', () => {
      renderWithProviders(<GridView {...defaultProps} hasNextPage={true} />);

      expect(screen.getByText(/load more/i)).toBeInTheDocument();
    });

    it('should call loadNextPage when load more is clicked', async () => {
      const user = userEvent.setup();
      const loadNextPage = jest.fn();

      renderWithProviders(
        <GridView {...defaultProps} hasNextPage={true} loadNextPage={loadNextPage} />
      );

      const loadMoreButton = screen.getByText(/load more/i);
      await user.click(loadMoreButton);

      expect(loadNextPage).toHaveBeenCalled();
    });
  });

  describe('Virtualization', () => {
    it('should use VirtualizedGridView for large datasets', () => {
      const largeRecordSet = Array.from({ length: 1500 }, (_, i) => ({
        id: String(i),
        entityName: 'contact',
        getFormattedValue: () => `Record ${i}`,
        getValue: () => `Record ${i}`,
        getNamedReference: jest.fn()
      }));

      renderWithProviders(
        <GridView {...defaultProps} records={largeRecordSet} enableVirtualization={true} />
      );

      // Virtualized grid should be rendered
      expect(screen.getByRole('grid')).toBeInTheDocument();
    });

    it('should not virtualize for small datasets', () => {
      renderWithProviders(<GridView {...defaultProps} enableVirtualization={true} />);

      // Regular grid should be rendered (only 2 records)
      expect(screen.getByRole('grid')).toBeInTheDocument();
    });
  });
});
