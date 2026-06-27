/**
 * OrientationToggle — unit tests.
 *
 * Covers acceptance criteria from smart-todo-r4 task 012:
 *  - renders an icon button reflecting the current orientation
 *  - click on the button flips the state (fires onChange with the opposite)
 *  - aria-pressed reflects "vertical" mode (WCAG 2.1 AA)
 *  - tooltip / aria-label communicate the current + next state for SR users
 */

import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { OrientationToggle } from '../OrientationToggle';

describe('OrientationToggle', () => {
  let onChange: jest.Mock;

  beforeEach(() => {
    onChange = jest.fn();
  });

  afterEach(() => {
    jest.clearAllMocks();
  });

  // ──────────────────────────────────────────────────────────────────────
  // Rendering — aria-label communicates current state
  // ──────────────────────────────────────────────────────────────────────

  it('renders a single button with current-state aria-label when orientation=horizontal', () => {
    renderWithProviders(<OrientationToggle orientation="horizontal" onChange={onChange} />);
    const button = screen.getByRole('button');
    expect(button).toBeInTheDocument();
    expect(button.getAttribute('aria-label')).toMatch(/Current layout: Horizontal layout/);
    expect(button.getAttribute('aria-label')).toMatch(/switch to vertical/i);
  });

  it('renders a single button with current-state aria-label when orientation=vertical', () => {
    renderWithProviders(<OrientationToggle orientation="vertical" onChange={onChange} />);
    const button = screen.getByRole('button');
    expect(button.getAttribute('aria-label')).toMatch(/Current layout: Vertical layout/);
    expect(button.getAttribute('aria-label')).toMatch(/switch to horizontal/i);
  });

  // ──────────────────────────────────────────────────────────────────────
  // aria-pressed — WCAG 2.1 AA toggle semantics
  // ──────────────────────────────────────────────────────────────────────

  it('sets aria-pressed="false" when orientation=horizontal', () => {
    renderWithProviders(<OrientationToggle orientation="horizontal" onChange={onChange} />);
    expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'false');
  });

  it('sets aria-pressed="true" when orientation=vertical', () => {
    renderWithProviders(<OrientationToggle orientation="vertical" onChange={onChange} />);
    expect(screen.getByRole('button')).toHaveAttribute('aria-pressed', 'true');
  });

  // ──────────────────────────────────────────────────────────────────────
  // Click flips the state
  // ──────────────────────────────────────────────────────────────────────

  it('calls onChange("vertical") when clicked while horizontal', async () => {
    const user = userEvent.setup();
    renderWithProviders(<OrientationToggle orientation="horizontal" onChange={onChange} />);
    await user.click(screen.getByRole('button'));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith('vertical');
  });

  it('calls onChange("horizontal") when clicked while vertical', async () => {
    const user = userEvent.setup();
    renderWithProviders(<OrientationToggle orientation="vertical" onChange={onChange} />);
    await user.click(screen.getByRole('button'));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith('horizontal');
  });
});
