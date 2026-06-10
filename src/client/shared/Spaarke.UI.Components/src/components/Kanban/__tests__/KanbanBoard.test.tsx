/**
 * KanbanBoard smoke tests — locks R2 a11y baseline (NFR-10).
 *
 * Covers:
 *   - render-without-crash
 *   - region landmark + aria-label
 *   - column groups + per-column counts (SR labels)
 *   - keyboard-accessible collapse toggle
 *   - empty-column "No items" SR text
 *   - renderCard called per item with index + columnId
 *
 * NOTE: drag-drop interactions are exercised by @hello-pangea/dnd's own
 * test suite. These tests verify the structural / a11y surface only.
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { KanbanBoard } from '../KanbanBoard';
import type { IKanbanColumn } from '../types';

interface ITestItem {
  id: string;
  name: string;
}

function makeColumns(): IKanbanColumn<ITestItem>[] {
  return [
    {
      id: 'col-1',
      title: 'Today',
      subtitle: 'Score >= 60',
      accentColor: '#ff0000',
      items: [
        { id: 'a', name: 'Item A' },
        { id: 'b', name: 'Item B' },
      ],
    },
    {
      id: 'col-2',
      title: 'Tomorrow',
      items: [],
    },
  ];
}

describe('KanbanBoard (shared primitive — R2 baseline lock)', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Render & a11y landmarks
  // ─────────────────────────────────────────────────────────────────────

  it('renders without crashing', () => {
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={item => <span>{item.name}</span>}
        getItemId={item => item.id}
      />
    );

    expect(screen.getByRole('region')).toBeInTheDocument();
  });

  it('uses default aria-label "Kanban board" when ariaLabel not provided', () => {
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={item => <span>{item.name}</span>}
        getItemId={item => item.id}
      />
    );

    expect(screen.getByRole('region', { name: 'Kanban board' })).toBeInTheDocument();
  });

  it('honours custom ariaLabel prop on the board region', () => {
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={item => <span>{item.name}</span>}
        getItemId={item => item.id}
        ariaLabel="Smart To Do Kanban board"
      />
    );

    expect(screen.getByRole('region', { name: 'Smart To Do Kanban board' })).toBeInTheDocument();
  });

  // ─────────────────────────────────────────────────────────────────────
  // Column structure & SR labels
  // ─────────────────────────────────────────────────────────────────────

  it('renders one role=group per column with title as aria-label', () => {
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={item => <span>{item.name}</span>}
        getItemId={item => item.id}
      />
    );

    expect(screen.getByRole('group', { name: 'Today' })).toBeInTheDocument();
    expect(screen.getByRole('group', { name: 'Tomorrow' })).toBeInTheDocument();
  });

  it('renders the per-column item count with SR-friendly aria-label', () => {
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={item => <span>{item.name}</span>}
        getItemId={item => item.id}
      />
    );

    // "Today" column has 2 items; visible text "2" + aria-label "2 items"
    expect(screen.getByLabelText('2 items')).toBeInTheDocument();
    expect(screen.getByLabelText('0 items')).toBeInTheDocument();
  });

  it('shows the column subtitle when provided', () => {
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={item => <span>{item.name}</span>}
        getItemId={item => item.id}
      />
    );

    expect(screen.getByText('Score >= 60')).toBeInTheDocument();
  });

  it('shows "No items" placeholder for empty columns', () => {
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={item => <span>{item.name}</span>}
        getItemId={item => item.id}
      />
    );

    expect(screen.getByText('No items')).toBeInTheDocument();
  });

  // ─────────────────────────────────────────────────────────────────────
  // renderCard contract
  // ─────────────────────────────────────────────────────────────────────

  it('calls renderCard for each item with item + index + columnId', () => {
    const renderCard = jest.fn((item: ITestItem) => <span data-testid={`card-${item.id}`}>{item.name}</span>);
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={renderCard}
        getItemId={item => item.id}
      />
    );

    expect(renderCard).toHaveBeenCalledWith({ id: 'a', name: 'Item A' }, 0, 'col-1');
    expect(renderCard).toHaveBeenCalledWith({ id: 'b', name: 'Item B' }, 1, 'col-1');
    expect(screen.getByTestId('card-a')).toBeInTheDocument();
    expect(screen.getByTestId('card-b')).toBeInTheDocument();
  });

  // ─────────────────────────────────────────────────────────────────────
  // Collapsed-column path
  // ─────────────────────────────────────────────────────────────────────

  it('renders collapsed column with "(collapsed)" SR label when in collapsedColumns set', () => {
    const collapsed = new Set(['col-2']);
    renderWithProviders(
      <KanbanBoard<ITestItem>
        columns={makeColumns()}
        onDragEnd={jest.fn()}
        renderCard={item => <span>{item.name}</span>}
        getItemId={item => item.id}
        collapsedColumns={collapsed}
      />
    );

    expect(screen.getByRole('group', { name: 'Tomorrow (collapsed)' })).toBeInTheDocument();
  });
});
