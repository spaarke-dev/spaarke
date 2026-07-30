/**
 * EmailReadingHeader.test.tsx
 *
 * Unit/RTL tests for `<EmailReadingHeader />` — the reading-pane TITLE BAR
 * (email-communication-solution-r5, reading-pane MAIN-AREA redesign). The
 * component is now just the light-gray title bar showing the email subject:
 * the tracking trio was REMOVED from the reading pane, "Open full form" moved
 * to the toolbar, and From/To/Cc/Bcc live in `<EmailRecipients />`.
 *
 * Purely presentational — subject in, nothing out. No data fetch, no tracking,
 * no open-full-form trigger in this component anymore.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailReadingHeader } from '../EmailReadingHeader';
import type { IEmailReadingHeaderProps } from '../EmailReadingHeader.types';

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

function baseProps(overrides: Partial<IEmailReadingHeaderProps> = {}): IEmailReadingHeaderProps {
  return {
    subject: 'Quarterly filing update',
    ...overrides,
  };
}

describe('EmailReadingHeader (title bar)', () => {
  it('renders the subject prominently, with no tracking trio and no from/to/cc/bcc', () => {
    renderWithProvider(<EmailReadingHeader {...baseProps()} />);

    expect(screen.getByTestId('email-reading-header')).toBeInTheDocument();
    expect(screen.getByText('Quarterly filing update')).toBeInTheDocument();
    // The redesigned title bar no longer renders tracking, From/To, or an "Open
    // full form" trigger (those moved to the recipients block / toolbar).
    expect(screen.queryByTestId('email-tracking-panel')).not.toBeInTheDocument();
    expect(screen.queryByText('From')).not.toBeInTheDocument();
    expect(screen.queryByText('To')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Open full form' })).not.toBeInTheDocument();
  });

  it('renders "(no subject)" when the subject is null/empty', () => {
    renderWithProvider(<EmailReadingHeader {...baseProps({ subject: null })} />);
    expect(screen.getByText('(no subject)')).toBeInTheDocument();
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    renderWithProvider(<EmailReadingHeader {...baseProps({ subject: 'Dark mode check' })} />, webDarkTheme);

    expect(screen.getByText('Dark mode check')).toBeInTheDocument();
    expect(consoleErrorSpy).not.toHaveBeenCalled();
    consoleErrorSpy.mockRestore();
  });
});
