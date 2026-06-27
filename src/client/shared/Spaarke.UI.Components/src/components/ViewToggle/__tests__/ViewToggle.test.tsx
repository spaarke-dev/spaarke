/**
 * ViewToggle — unit tests.
 *
 * Covers acceptance criteria from smart-todo-r4 task 012:
 *  - renders both segment buttons
 *  - click on the inactive segment fires onChange with the new mode
 *  - click on the already-selected segment does NOT fire onChange
 *  - aria-pressed reflects current mode correctly
 *  - group landmark + aria-label present (WCAG 2.1 AA)
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { ViewToggle } from '../ViewToggle';

describe('ViewToggle', () => {
  let onChange: jest.Mock;

  beforeEach(() => {
    onChange = jest.fn();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  // ──────────────────────────────────────────────────────────────────────
  // Rendering
  // ──────────────────────────────────────────────────────────────────────

  it('renders both segment buttons with their accessible names', () => {
    renderWithProviders(<ViewToggle mode="list" onChange={onChange} />);
    expect(screen.getByRole('button', { name: 'List view' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Card view' })).toBeInTheDocument();
  });

  it('renders a group landmark with aria-label "View mode"', () => {
    renderWithProviders(<ViewToggle mode="list" onChange={onChange} />);
    expect(screen.getByRole('group', { name: 'View mode' })).toBeInTheDocument();
  });

  // ──────────────────────────────────────────────────────────────────────
  // aria-pressed state (WCAG 2.1 AA — selection state communicated to SR)
  // ──────────────────────────────────────────────────────────────────────

  it('sets aria-pressed="true" on the active list segment and "false" on the other', () => {
    renderWithProviders(<ViewToggle mode="list" onChange={onChange} />);
    expect(screen.getByRole('button', { name: 'List view' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Card view' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('sets aria-pressed="true" on the active card segment and "false" on the other', () => {
    renderWithProviders(<ViewToggle mode="card" onChange={onChange} />);
    expect(screen.getByRole('button', { name: 'List view' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: 'Card view' })).toHaveAttribute('aria-pressed', 'true');
  });

  // ──────────────────────────────────────────────────────────────────────
  // Click round-trip
  // ──────────────────────────────────────────────────────────────────────

  it('fires onChange("card") when clicking Card while mode is list', async () => {
    const user = userEvent.setup();
    renderWithProviders(<ViewToggle mode="list" onChange={onChange} />);
    await user.click(screen.getByRole('button', { name: 'Card view' }));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith('card');
  });

  it('fires onChange("list") when clicking List while mode is card', async () => {
    const user = userEvent.setup();
    renderWithProviders(<ViewToggle mode="card" onChange={onChange} />);
    await user.click(screen.getByRole('button', { name: 'List view' }));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith('list');
  });

  it('does NOT fire onChange when clicking the already-selected segment', async () => {
    const user = userEvent.setup();
    renderWithProviders(<ViewToggle mode="list" onChange={onChange} />);
    await user.click(screen.getByRole('button', { name: 'List view' }));
    expect(onChange).not.toHaveBeenCalled();
  });
});
