/**
 * EmailRecipients.test.tsx
 *
 * Unit/RTL tests for `<EmailRecipients />` — the reading-pane RECIPIENTS
 * block (email-communication-solution-r5, reading-pane layout redesign).
 * Covers: From/To always render (even when empty); Cc/Bcc render ONLY when
 * they have a value, and the row is omitted entirely otherwise; dark-mode
 * theming (ADR-021).
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailRecipients } from '../EmailRecipients';

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

describe('EmailRecipients', () => {
  it('always renders From and To rows, even when Cc/Bcc are omitted', () => {
    renderWithProvider(<EmailRecipients from="jane.doe@example.com" to="john.smith@example.com" />);

    expect(screen.getByTestId('email-recipients')).toBeInTheDocument();
    // owner UAT R2 item 6 — "From" is now plain "From:" text (not a boxed label).
    expect(screen.getByText('From:')).toBeInTheDocument();
    expect(screen.getByText('jane.doe@example.com')).toBeInTheDocument();
    expect(screen.getByText('To')).toBeInTheDocument();
    expect(screen.getByText('john.smith@example.com')).toBeInTheDocument();
    expect(screen.queryByText('Cc')).not.toBeInTheDocument();
    expect(screen.queryByText('Bcc')).not.toBeInTheDocument();
  });

  it('renders Cc/Bcc rows only when they have a value', () => {
    renderWithProvider(
      <EmailRecipients from="jane.doe@example.com" to="john.smith@example.com" cc="legal-team@example.com" bcc={null} />
    );

    expect(screen.getByText('Cc')).toBeInTheDocument();
    expect(screen.getByText('legal-team@example.com')).toBeInTheDocument();
    expect(screen.queryByText('Bcc')).not.toBeInTheDocument();
  });

  it('renders empty From/To rows (not a crash) when the values are null', () => {
    renderWithProvider(<EmailRecipients from={null} to={null} />);

    expect(screen.getByText('From:')).toBeInTheDocument();
    expect(screen.getByText('To')).toBeInTheDocument();
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    renderWithProvider(
      <EmailRecipients from="a@example.com" to="b@example.com" cc="c@example.com" bcc="d@example.com" />,
      webDarkTheme
    );

    expect(screen.getByText('a@example.com')).toBeInTheDocument();
    expect(consoleErrorSpy).not.toHaveBeenCalled();
    consoleErrorSpy.mockRestore();
  });
});
