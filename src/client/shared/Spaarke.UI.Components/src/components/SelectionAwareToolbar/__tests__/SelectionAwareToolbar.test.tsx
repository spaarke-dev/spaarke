/**
 * SelectionAwareToolbar — unit tests.
 *
 * Covers acceptance criteria from smart-todo-r4 task 012:
 *  - renders null at selectedCount===0
 *  - renders count + buttons at selectedCount>=1
 *  - click on action fires correct onClick
 *  - disabled action ignores click + reflects in DOM
 *  - WCAG: toolbar landmark + aria-label on each button
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { SelectionAwareToolbar } from '../SelectionAwareToolbar';
import type { ToolbarAction } from '../types';

describe('SelectionAwareToolbar', () => {
  let openHandler: jest.Mock;
  let deleteHandler: jest.Mock;
  let emailHandler: jest.Mock;

  const buildActions = (): ToolbarAction[] => [
    { id: 'open', label: 'Open', onClick: openHandler },
    { id: 'delete', label: 'Delete', onClick: deleteHandler },
    { id: 'email', label: 'Email', onClick: emailHandler, disabled: true },
  ];

  beforeEach(() => {
    openHandler = jest.fn();
    deleteHandler = jest.fn();
    emailHandler = jest.fn();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  // ──────────────────────────────────────────────────────────────────────
  // Visibility (FR-08 — hides at 0, shows at ≥1)
  // ──────────────────────────────────────────────────────────────────────

  it('renders null when selectedCount === 0', () => {
    renderWithProviders(<SelectionAwareToolbar selectedCount={0} actions={buildActions()} />);
    // No toolbar landmark, no count label, no action buttons should be present.
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();
    expect(screen.queryByText(/selected$/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open' })).not.toBeInTheDocument();
  });

  it('renders null when selectedCount is negative (defensive)', () => {
    renderWithProviders(<SelectionAwareToolbar selectedCount={-1} actions={buildActions()} />);
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();
  });

  it('renders the toolbar and count label when selectedCount === 1', () => {
    renderWithProviders(<SelectionAwareToolbar selectedCount={1} actions={buildActions()} />);
    expect(screen.getByRole('toolbar', { name: 'Selection actions' })).toBeInTheDocument();
    expect(screen.getByText('1 selected')).toBeInTheDocument();
  });

  it('renders all action buttons with their labels at selectedCount >= 1', () => {
    renderWithProviders(<SelectionAwareToolbar selectedCount={3} actions={buildActions()} />);
    expect(screen.getByRole('button', { name: 'Open' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Email' })).toBeInTheDocument();
    expect(screen.getByText('3 selected')).toBeInTheDocument();
  });

  // ──────────────────────────────────────────────────────────────────────
  // Count label opt-out
  // ──────────────────────────────────────────────────────────────────────

  it('hides the count label when showCountLabel={false}', () => {
    renderWithProviders(<SelectionAwareToolbar selectedCount={2} actions={buildActions()} showCountLabel={false} />);
    expect(screen.queryByText('2 selected')).not.toBeInTheDocument();
    expect(screen.getByRole('toolbar')).toBeInTheDocument();
  });

  // ──────────────────────────────────────────────────────────────────────
  // Click round-trip
  // ──────────────────────────────────────────────────────────────────────

  it('fires the correct action onClick when its button is clicked', async () => {
    const user = userEvent.setup();
    renderWithProviders(<SelectionAwareToolbar selectedCount={1} actions={buildActions()} />);

    await user.click(screen.getByRole('button', { name: 'Open' }));
    expect(openHandler).toHaveBeenCalledTimes(1);
    expect(deleteHandler).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Delete' }));
    expect(deleteHandler).toHaveBeenCalledTimes(1);
  });

  it('does not fire onClick for a disabled action', async () => {
    const user = userEvent.setup();
    renderWithProviders(<SelectionAwareToolbar selectedCount={1} actions={buildActions()} />);

    const emailButton = screen.getByRole('button', { name: 'Email' });
    expect(emailButton).toBeDisabled();

    await user.click(emailButton);
    expect(emailHandler).not.toHaveBeenCalled();
  });
});
