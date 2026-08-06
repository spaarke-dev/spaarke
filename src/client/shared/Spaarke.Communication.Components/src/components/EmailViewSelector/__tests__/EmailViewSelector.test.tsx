/**
 * EmailViewSelector.test.tsx
 *
 * RTL tests for the thin `<EmailViewSelector />` container
 * (email-communication-solution-r5 task 031, FR-04): renders the reused
 * `DataGridViewSelector`, emits `onViewChange`, shows an FR-19 error banner in
 * place of the picker when `error` is set, and themes in dark mode (ADR-021).
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import type { SavedView } from '@spaarke/ui-components';
import { EmailViewSelector } from '../EmailViewSelector';

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

const VIEWS: SavedView[] = [
  { id: 'view-inbox', name: 'Email — Inbox', isDefault: false },
  { id: 'view-sent', name: 'Email — Sent', isDefault: false },
];

describe('EmailViewSelector', () => {
  it('renders the reused ViewSelector with the active view label', () => {
    renderWithProvider(<EmailViewSelector views={VIEWS} activeViewId="view-inbox" onViewChange={jest.fn()} />);

    expect(screen.getByText('Email — Inbox')).toBeInTheDocument();
  });

  it('emits onViewChange when a different view is picked from the menu', () => {
    const onViewChange = jest.fn();
    renderWithProvider(<EmailViewSelector views={VIEWS} activeViewId="view-inbox" onViewChange={onViewChange} />);

    fireEvent.click(screen.getByRole('button', { name: /select view/i }));
    fireEvent.click(screen.getByRole('menuitemradio', { name: /Email — Sent/i }));

    expect(onViewChange).toHaveBeenCalledWith('view-sent');
  });

  it('renders an error banner instead of the picker when error is set (FR-19)', () => {
    renderWithProvider(
      <EmailViewSelector
        views={VIEWS}
        activeViewId="view-inbox"
        onViewChange={jest.fn()}
        error={new Error('No saved views found')}
      />
    );

    expect(screen.getByText('No saved views found')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /select view/i })).not.toBeInTheDocument();
  });

  it('renders legibly in dark mode (ADR-021)', () => {
    renderWithProvider(
      <EmailViewSelector views={VIEWS} activeViewId="view-inbox" onViewChange={jest.fn()} theme={webDarkTheme} />,
      webDarkTheme
    );

    expect(screen.getByText('Email — Inbox')).toBeInTheDocument();
  });
});
