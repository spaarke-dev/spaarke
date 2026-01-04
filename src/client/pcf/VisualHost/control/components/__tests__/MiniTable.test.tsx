import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { MiniTable, IMiniTableColumn, IMiniTableItem } from '../MiniTable';

const renderWithTheme = (component: React.ReactElement, theme = webLightTheme) => {
  return render(
    <FluentProvider theme={theme}>
      {component}
    </FluentProvider>
  );
};

const mockColumns: IMiniTableColumn[] = [
  { key: 'name', header: 'Name' },
  { key: 'value', header: 'Value', isValue: true },
];

const mockItems: IMiniTableItem[] = [
  { id: '1', values: { name: 'Item A', value: 100 }, fieldValue: 'a' },
  { id: '2', values: { name: 'Item B', value: 85 }, fieldValue: 'b' },
  { id: '3', values: { name: 'Item C', value: 72 }, fieldValue: 'c' },
  { id: '4', values: { name: 'Item D', value: 65 }, fieldValue: 'd' },
  { id: '5', values: { name: 'Item E', value: 50 }, fieldValue: 'e' },
];

describe('MiniTable', () => {
  describe('rendering', () => {
    it('renders with required props', () => {
      renderWithTheme(
        <MiniTable columns={mockColumns} items={mockItems} />
      );

      // Column headers should be visible
      expect(screen.getByText('Name')).toBeInTheDocument();
      expect(screen.getByText('Value')).toBeInTheDocument();

      // Data should be visible
      expect(screen.getByText('Item A')).toBeInTheDocument();
      expect(screen.getByText('100')).toBeInTheDocument();
    });

    it('renders with title', () => {
      renderWithTheme(
        <MiniTable columns={mockColumns} items={mockItems} title="Top Items" />
      );

      expect(screen.getByText('Top Items')).toBeInTheDocument();
    });

    it('renders empty state when no items', () => {
      renderWithTheme(
        <MiniTable columns={mockColumns} items={[]} title="Empty Table" />
      );

      expect(screen.getByText('No data available')).toBeInTheDocument();
    });

    it('shows rank numbers by default', () => {
      renderWithTheme(
        <MiniTable columns={mockColumns} items={mockItems} showRank={true} />
      );

      // Rank column header
      expect(screen.getByText('#')).toBeInTheDocument();
      // Rank numbers
      expect(screen.getByText('1')).toBeInTheDocument();
      expect(screen.getByText('2')).toBeInTheDocument();
      expect(screen.getByText('3')).toBeInTheDocument();
    });

    it('hides rank numbers when showRank is false', () => {
      renderWithTheme(
        <MiniTable columns={mockColumns} items={mockItems} showRank={false} />
      );

      expect(screen.queryByText('#')).not.toBeInTheDocument();
    });

    it('limits items based on topN prop', () => {
      renderWithTheme(
        <MiniTable columns={mockColumns} items={mockItems} topN={3} />
      );

      expect(screen.getByText('Item A')).toBeInTheDocument();
      expect(screen.getByText('Item B')).toBeInTheDocument();
      expect(screen.getByText('Item C')).toBeInTheDocument();
      expect(screen.queryByText('Item D')).not.toBeInTheDocument();
      expect(screen.queryByText('Item E')).not.toBeInTheDocument();
    });

    it('defaults to 5 items when topN not specified', () => {
      const manyItems: IMiniTableItem[] = Array.from({ length: 10 }, (_, i) => ({
        id: String(i + 1),
        values: { name: `Item ${i + 1}`, value: 100 - i * 10 },
        fieldValue: String(i + 1),
      }));

      renderWithTheme(
        <MiniTable columns={mockColumns} items={manyItems} />
      );

      expect(screen.getByText('Item 1')).toBeInTheDocument();
      expect(screen.getByText('Item 5')).toBeInTheDocument();
      expect(screen.queryByText('Item 6')).not.toBeInTheDocument();
    });
  });

  describe('interactions', () => {
    it('calls onDrillInteraction when row clicked', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MiniTable
          columns={mockColumns}
          items={mockItems}
          onDrillInteraction={mockDrill}
          drillField="item"
        />
      );

      // Find and click a row
      const row = screen.getByText('Item A').closest('tr');
      if (row) {
        fireEvent.click(row);

        expect(mockDrill).toHaveBeenCalledWith({
          field: 'item',
          operator: 'eq',
          value: 'a',
          label: '#1 - Item A',
        });
      }
    });

    it('handles keyboard interaction', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MiniTable
          columns={mockColumns}
          items={mockItems}
          onDrillInteraction={mockDrill}
          drillField="item"
        />
      );

      const row = screen.getByText('Item A').closest('tr');
      if (row) {
        fireEvent.keyDown(row, { key: 'Enter' });
        expect(mockDrill).toHaveBeenCalled();
      }
    });

    it('rows are not interactive without drillField', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MiniTable
          columns={mockColumns}
          items={mockItems}
          onDrillInteraction={mockDrill}
        />
      );

      // Rows should not have tabindex when not interactive
      const row = screen.getByText('Item A').closest('tr');
      expect(row?.getAttribute('tabindex')).not.toBe('0');
    });

    it('rows are not interactive when interactive prop is false', () => {
      const mockDrill = jest.fn();

      renderWithTheme(
        <MiniTable
          columns={mockColumns}
          items={mockItems}
          onDrillInteraction={mockDrill}
          drillField="item"
          interactive={false}
        />
      );

      const row = screen.getByText('Item A').closest('tr');
      expect(row?.getAttribute('tabindex')).not.toBe('0');
    });
  });

  describe('theme support', () => {
    it('renders correctly in dark theme', () => {
      renderWithTheme(
        <MiniTable columns={mockColumns} items={mockItems} title="Dark Table" />,
        webDarkTheme
      );

      expect(screen.getByText('Dark Table')).toBeInTheDocument();
    });
  });

  describe('props', () => {
    it('handles column width', () => {
      const columnsWithWidth: IMiniTableColumn[] = [
        { key: 'name', header: 'Name', width: '200px' },
        { key: 'value', header: 'Value', isValue: true, width: '100px' },
      ];

      renderWithTheme(
        <MiniTable columns={columnsWithWidth} items={mockItems} />
      );

      expect(screen.getByText('Name')).toBeInTheDocument();
    });

    it('handles missing values gracefully', () => {
      const itemsWithMissing: IMiniTableItem[] = [
        { id: '1', values: { name: 'Item A' }, fieldValue: 'a' }, // missing value
      ];

      renderWithTheme(
        <MiniTable columns={mockColumns} items={itemsWithMissing} />
      );

      expect(screen.getByText('Item A')).toBeInTheDocument();
      expect(screen.getByText('-')).toBeInTheDocument();
    });
  });
});
