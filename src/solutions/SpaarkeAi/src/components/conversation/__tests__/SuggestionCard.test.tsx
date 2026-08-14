/**
 * SuggestionCard (presentational) tests.
 *
 * Originally authored by spaarke-notification-spine-r1 task 051 / FR-16 to cover BOTH the
 * `SuggestionCard` presentational component AND the spine-driven `useSuggestionCards` lifecycle
 * hook. assistant-enhancements-r2 task 001 (FR-E1) removed the spine-driven proactive-suggestion
 * surface (`useSuggestionCards.tsx` deleted), so the hook-lifecycle describe block was removed.
 * `SuggestionCard.tsx` is RETAINED — it is still reused by `useRerunFullAnalysisCard` (a separate,
 * client-local "Rerun a full analysis" card) — so its presentational tests remain.
 *
 * Covers:
 *   - renders-from-model: a suggestion model renders a bordered card with title + stable test id;
 *     the main region calls onAction, the 'x' calls onDismiss independently.
 *   - dark-mode-token-correctness (ADR-021): renders token-driven (no inline color) under both themes.
 *
 * ADR-021 note (mirrors ConsumerChips.test.tsx): token-driven styling is asserted STRUCTURALLY
 * here (no inline hardcoded colors); the pixel-level dark-mode check runs in the browser.
 */

import '@testing-library/jest-dom';
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import type { Theme } from '@fluentui/react-components';

import { SuggestionCard } from '../SuggestionCard';

function renderWithTheme(ui: React.ReactElement, theme: Theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

describe('SuggestionCard (presentational)', () => {
  it('renders the card from a suggestion model with its title and a stable test id', () => {
    const onAction = jest.fn();
    const onDismiss = jest.fn();
    renderWithTheme(
      <SuggestionCard
        suggestion={{ suggestionId: 'sugg-1', title: 'Review Acme v. Beta', actionHint: 'review' }}
        onAction={onAction}
        onDismiss={onDismiss}
      />
    );

    const card = screen.getByTestId('suggestion-card-sugg-1');
    expect(card).toBeInTheDocument();
    expect(card).toHaveTextContent('Review Acme v. Beta');
    expect(card).toHaveAttribute('data-suggestion-id', 'sugg-1');

    fireEvent.click(card);
    expect(onAction).toHaveBeenCalledTimes(1);

    // The dismiss 'x' is a distinct control (not nested in the main button) and calls onDismiss.
    fireEvent.click(screen.getByTestId('suggestion-dismiss-sugg-1'));
    expect(onDismiss).toHaveBeenCalledTimes(1);
    expect(onAction).toHaveBeenCalledTimes(1); // dismiss did NOT trigger the open action
  });

  // ADR-021: renders token-driven (no inline color) under BOTH themes.
  it.each([
    ['light', webLightTheme],
    ['dark', webDarkTheme],
  ])('renders under the %s theme with no inline hardcoded color', (_name, theme) => {
    const { unmount } = renderWithTheme(
      <SuggestionCard
        suggestion={{ suggestionId: 'sugg-x', title: 'Follow up on filing', actionHint: 'review' }}
        onAction={jest.fn()}
        onDismiss={jest.fn()}
      />,
      theme as Theme
    );

    const card = screen.getByTestId('suggestion-card-sugg-x');
    expect(card).toBeInTheDocument();
    // No hardcoded hex/rgb color literal leaked onto the element's inline style (tokens only).
    const inlineStyle = card.getAttribute('style') ?? '';
    expect(inlineStyle).not.toMatch(/#[0-9a-fA-F]{3,8}\b/);
    expect(inlineStyle).not.toMatch(/rgb\(/);
    unmount();
  });
});
