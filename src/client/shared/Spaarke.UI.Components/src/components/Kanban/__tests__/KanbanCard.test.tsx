/**
 * KanbanCard (primitive) smoke tests — locks R2 a11y baseline (NFR-10).
 *
 * Covers:
 *   - render-without-crash
 *   - role=listitem + aria-label + aria-selected
 *   - keyboard activation (Enter, Space)
 *   - slot rendering (score, title, metadata, actions)
 */

import * as React from 'react';
import { screen, fireEvent } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { KanbanCard } from '../KanbanCard';

describe('KanbanCard primitive (shared — R2 baseline lock)', () => {
  // ─────────────────────────────────────────────────────────────────────
  // Render & a11y
  // ─────────────────────────────────────────────────────────────────────

  it('renders without crashing', () => {
    renderWithProviders(<KanbanCard titleSlot={<span>My Item</span>} ariaLabel="My Item" />);

    expect(screen.getByText('My Item')).toBeInTheDocument();
  });

  it('uses role=listitem with the supplied ariaLabel', () => {
    renderWithProviders(<KanbanCard titleSlot={<span>Title</span>} ariaLabel="Title. Due Mar 5. Open." />);

    const item = screen.getByRole('listitem');
    expect(item).toHaveAttribute('aria-label', 'Title. Due Mar 5. Open.');
  });

  it('exposes aria-selected based on isSelected prop', () => {
    const { rerender } = renderWithProviders(
      <KanbanCard titleSlot={<span>T</span>} ariaLabel="T" isSelected={false} />
    );
    expect(screen.getByRole('listitem')).toHaveAttribute('aria-selected', 'false');

    rerender(<KanbanCard titleSlot={<span>T</span>} ariaLabel="T" isSelected />);
    expect(screen.getByRole('listitem')).toHaveAttribute('aria-selected', 'true');
  });

  // ─────────────────────────────────────────────────────────────────────
  // Keyboard activation
  // ─────────────────────────────────────────────────────────────────────

  it('is keyboard-focusable (tabIndex=0)', () => {
    renderWithProviders(<KanbanCard titleSlot={<span>T</span>} ariaLabel="T" />);
    expect(screen.getByRole('listitem')).toHaveAttribute('tabIndex', '0');
  });

  it('fires onClick when Enter is pressed', () => {
    const onClick = jest.fn();
    renderWithProviders(<KanbanCard titleSlot={<span>T</span>} ariaLabel="T" onClick={onClick} />);

    fireEvent.keyDown(screen.getByRole('listitem'), { key: 'Enter' });
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('fires onClick when Space is pressed', () => {
    const onClick = jest.fn();
    renderWithProviders(<KanbanCard titleSlot={<span>T</span>} ariaLabel="T" onClick={onClick} />);

    fireEvent.keyDown(screen.getByRole('listitem'), { key: ' ' });
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('fires onClick on mouse click', () => {
    const onClick = jest.fn();
    renderWithProviders(<KanbanCard titleSlot={<span>T</span>} ariaLabel="T" onClick={onClick} />);

    fireEvent.click(screen.getByRole('listitem'));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  // ─────────────────────────────────────────────────────────────────────
  // Slot composition
  // ─────────────────────────────────────────────────────────────────────

  it('renders the score, title, metadata, and actions slots', () => {
    renderWithProviders(
      <KanbanCard
        ariaLabel="composed"
        scoreSlot={<span data-testid="score">42</span>}
        titleSlot={<span data-testid="title">Task</span>}
        metadataSlot={<span data-testid="meta">Due tomorrow</span>}
        actionsSlot={<button data-testid="pin">Pin</button>}
      />
    );

    expect(screen.getByTestId('score')).toBeInTheDocument();
    expect(screen.getByTestId('title')).toBeInTheDocument();
    expect(screen.getByTestId('meta')).toBeInTheDocument();
    expect(screen.getByTestId('pin')).toBeInTheDocument();
  });

  it('omits score wrapper when scoreSlot not provided', () => {
    const { container } = renderWithProviders(<KanbanCard ariaLabel="x" titleSlot={<span>X</span>} />);

    // Two columns expected: content + (no actions, no score)
    // Score wrapper uses aria-hidden — verify it's absent.
    expect(container.querySelectorAll('[aria-hidden="true"]').length).toBe(0);
  });
});
