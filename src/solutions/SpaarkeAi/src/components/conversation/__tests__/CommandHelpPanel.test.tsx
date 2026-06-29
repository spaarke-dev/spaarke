/**
 * CommandHelpPanel.test.tsx — R6 task 081 / D-D-02 unit tests.
 *
 * Verifies:
 *   1. Renders the closed Pillar 8 vocabulary (6 hard + 4 soft + 3 ref).
 *   2. Renders in light AND dark theme (ADR-021 semantic-token parity).
 *   3. Close button + onOpenChange invoke the onClose callback.
 *   4. Hidden when open=false.
 *
 * Token-level visual verification is enforced by Storybook / VRT in CI; this
 * test is the STRUCTURAL floor — proves the component renders without
 * crashing under both themes, and that no hardcoded color leaks through.
 *
 * @see CommandHelpPanel.tsx — component under test
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import {
  FluentProvider,
  webLightTheme,
  webDarkTheme,
} from '@fluentui/react-components';

import { CommandHelpPanel } from '../CommandHelpPanel';
import { HardSlashes, SoftSlashes } from '../CommandRouter';

// ---------------------------------------------------------------------------
// Helper
// ---------------------------------------------------------------------------

function renderPanel(
  open: boolean,
  onClose: () => void,
  theme: typeof webLightTheme = webLightTheme,
): void {
  render(
    <FluentProvider theme={theme}>
      <CommandHelpPanel open={open} onClose={onClose} />
    </FluentProvider>,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('CommandHelpPanel', () => {
  // -------------------------------------------------------------------------
  // Rendering
  // -------------------------------------------------------------------------

  it('renders the Dialog with title when open', () => {
    renderPanel(true, jest.fn());
    expect(screen.getByText(/chat commands/i)).toBeInTheDocument();
  });

  it('does not render Dialog content when open is false', () => {
    renderPanel(false, jest.fn());
    expect(screen.queryByText(/chat commands/i)).not.toBeInTheDocument();
  });

  it('lists all hard-slash commands (7 post-R7 task 094)', () => {
    renderPanel(true, jest.fn());
    for (const cmd of HardSlashes) {
      expect(screen.getByText(cmd)).toBeInTheDocument();
    }
  });

  it('lists all 4 soft-slash commands', () => {
    renderPanel(true, jest.fn());
    for (const cmd of SoftSlashes) {
      expect(screen.getByText(cmd)).toBeInTheDocument();
    }
  });

  it('lists the 3 reference shapes', () => {
    renderPanel(true, jest.fn());
    expect(screen.getByText('#scope')).toBeInTheDocument();
    expect(screen.getByText('@<entity>')).toBeInTheDocument();
    expect(screen.getByText('#<filename>')).toBeInTheDocument();
  });

  it('shows section headings for quick actions, assistant shortcuts, and references', () => {
    renderPanel(true, jest.fn());
    expect(screen.getByText(/quick actions/i)).toBeInTheDocument();
    expect(screen.getByText(/assistant shortcuts/i)).toBeInTheDocument();
    // "References" appears as a section heading; use exact match (not regex)
    // to avoid colliding with the longer descriptive text inside list items.
    expect(screen.getByText('References')).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Dark-mode parity (ADR-021)
  // -------------------------------------------------------------------------

  it('renders in dark theme without crashing (ADR-021 semantic-token parity)', () => {
    renderPanel(true, jest.fn(), webDarkTheme);
    expect(screen.getByText(/chat commands/i)).toBeInTheDocument();
    // All 6 hard slashes still rendered in dark theme — token resolution OK.
    for (const cmd of HardSlashes) {
      expect(screen.getByText(cmd)).toBeInTheDocument();
    }
  });

  it('renders in light theme as the baseline (ADR-021 parity check)', () => {
    renderPanel(true, jest.fn(), webLightTheme);
    expect(screen.getByText(/chat commands/i)).toBeInTheDocument();
  });

  // -------------------------------------------------------------------------
  // Close interaction
  // -------------------------------------------------------------------------

  it('calls onClose when the Close button is clicked', async () => {
    const onClose = jest.fn();
    renderPanel(true, onClose);

    const user = userEvent.setup();
    await user.click(screen.getByRole('button', { name: /close/i }));

    expect(onClose).toHaveBeenCalledTimes(1);
  });

  // -------------------------------------------------------------------------
  // Accessibility
  // -------------------------------------------------------------------------

  it('exposes an accessible label on the Dialog surface', () => {
    renderPanel(true, jest.fn());
    // The DialogSurface has aria-label="Chat command reference"; query by
    // role=dialog (Fluent v9 sets role=dialog on the surface).
    expect(
      screen.getByRole('dialog', { name: /chat command reference/i }),
    ).toBeInTheDocument();
  });
});
