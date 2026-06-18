/**
 * PinnedMemoryProvenanceBadge — unit tests
 *
 * Covers POML UI-test #8: "Provenance badge renders the stub label."
 *
 * Also covers:
 *   - Default (no source prop) → "Created via UI" stub label
 *   - Explicit source="chat" → "Created via chat"
 *   - Explicit source="ui" → "Created via UI"
 *   - Tooltip / accessibility metadata wiring
 *   - Dark mode parity sanity (component renders without color hard-codes)
 *
 * Task: R6-070 PART B.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen } from '@testing-library/react';
import {
  FluentProvider,
  webDarkTheme,
  webLightTheme,
} from '@fluentui/react-components';

import PinnedMemoryProvenanceBadge from '../PinnedMemoryProvenanceBadge';

function renderWithTheme(ui: React.ReactElement, dark = false): void {
  render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);
}

describe('PinnedMemoryProvenanceBadge', () => {
  it('renders the stub "Created via UI" label when source is omitted', () => {
    renderWithTheme(<PinnedMemoryProvenanceBadge />);
    expect(screen.getByText('Created via UI')).toBeInTheDocument();
  });

  it('renders "Created via UI" when source="ui"', () => {
    renderWithTheme(<PinnedMemoryProvenanceBadge source="ui" />);
    expect(screen.getByText('Created via UI')).toBeInTheDocument();
    const badge = screen.getByTestId('pinned-memory-provenance-badge');
    expect(badge).toHaveAttribute('data-source', 'ui');
  });

  it('renders "Created via chat" when source="chat"', () => {
    renderWithTheme(<PinnedMemoryProvenanceBadge source="chat" />);
    expect(screen.getByText('Created via chat')).toBeInTheDocument();
    const badge = screen.getByTestId('pinned-memory-provenance-badge');
    expect(badge).toHaveAttribute('data-source', 'chat');
  });

  it('exposes an accessible aria-label that matches the visible label', () => {
    renderWithTheme(<PinnedMemoryProvenanceBadge source="chat" />);
    const badge = screen.getByTestId('pinned-memory-provenance-badge');
    expect(badge).toHaveAttribute('aria-label', 'Created via chat');
  });

  it('renders without errors in dark mode (ADR-021 token usage)', () => {
    renderWithTheme(<PinnedMemoryProvenanceBadge source="ui" />, true);
    // If makeStyles tokens were hardcoded hex, FluentProvider with webDarkTheme
    // would still render but the badge would visually fail dark-mode parity.
    // The render() not throwing + element being present is the smoke check.
    expect(screen.getByText('Created via UI')).toBeInTheDocument();
  });
});
