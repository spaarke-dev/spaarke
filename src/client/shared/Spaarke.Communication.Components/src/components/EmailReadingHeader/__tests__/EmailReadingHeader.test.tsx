/**
 * EmailReadingHeader.test.tsx
 *
 * Unit/RTL tests for `<EmailReadingHeader />` (email-communication-solution-r5
 * task 034, spec FR-11). Covers: header reflects the selected record's
 * from/to/cc/bcc/subject/sent/received (the closed acceptance criterion),
 * loading + error states, and dark-mode theming (ADR-021).
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailReadingHeader } from '../EmailReadingHeader';
import type { IEmailHeaderDataService } from '../EmailReadingHeader.types';

// jsdom has no ResizeObserver; `MessageBar` (error state) needs one to mount.
// Mirrors the same polyfill already used by
// `../EmailComposeActions/__tests__/useEmailComposeActions.test.tsx` in this package.
if (typeof globalThis.ResizeObserver === 'undefined') {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

function makeDataService(record: Record<string, unknown> | Error): IEmailHeaderDataService {
  return {
    retrieveRecord: jest.fn(() => (record instanceof Error ? Promise.reject(record) : Promise.resolve(record))),
  };
}

describe('EmailReadingHeader', () => {
  it('reflects the selected record: from/to/cc/bcc, subject, and sent/received dates', async () => {
    const dataService = makeDataService({
      sprk_subject: 'Quarterly filing update',
      sprk_from: 'jane.doe@example.com',
      sprk_to: 'john.smith@example.com',
      sprk_cc: 'legal-team@example.com',
      sprk_bcc: 'audit@example.com',
      sprk_sentat: '2026-07-20T10:00:00Z',
      sprk_receiveddate: '2026-07-20T10:05:00Z',
    });

    renderWithProvider(<EmailReadingHeader selectedId="comm-1" dataService={dataService} />);

    await waitFor(() => expect(screen.getByTestId('email-reading-header')).toBeInTheDocument());

    expect(screen.getByText('Quarterly filing update')).toBeInTheDocument();
    expect(screen.getByText('jane.doe@example.com')).toBeInTheDocument();
    expect(screen.getByText('john.smith@example.com')).toBeInTheDocument();
    expect(screen.getByText('legal-team@example.com')).toBeInTheDocument();
    expect(screen.getByText('audit@example.com')).toBeInTheDocument();
    expect(screen.getByText('From')).toBeInTheDocument();
    expect(screen.getByText('To')).toBeInTheDocument();
    expect(screen.getByText('Cc')).toBeInTheDocument();
    expect(screen.getByText('Bcc')).toBeInTheDocument();
    expect(screen.getByText('Sent')).toBeInTheDocument();
    expect(screen.getByText('Received')).toBeInTheDocument();
    expect(dataService.retrieveRecord as jest.Mock).toHaveBeenCalledWith(
      'sprk_communication',
      'comm-1',
      expect.stringContaining('sprk_subject')
    );
  });

  it('re-fetches when selectedId changes', async () => {
    const dataService = makeDataService({ sprk_subject: 'First' });
    const { rerender } = renderWithProvider(<EmailReadingHeader selectedId="comm-1" dataService={dataService} />);
    await waitFor(() => expect(screen.getByText('First')).toBeInTheDocument());

    (dataService.retrieveRecord as jest.Mock).mockResolvedValueOnce({ sprk_subject: 'Second' });
    rerender(
      <FluentProvider theme={webLightTheme}>
        <EmailReadingHeader selectedId="comm-2" dataService={dataService} />
      </FluentProvider>
    );

    await waitFor(() => expect(screen.getByText('Second')).toBeInTheDocument());
    expect(dataService.retrieveRecord as jest.Mock).toHaveBeenCalledTimes(2);
  });

  it('shows a skeleton while loading', () => {
    const dataService: IEmailHeaderDataService = {
      retrieveRecord: jest.fn(() => new Promise(() => {})), // never resolves
    };
    renderWithProvider(<EmailReadingHeader selectedId="comm-1" dataService={dataService} />);
    expect(screen.getByTestId('email-header-skeleton')).toBeInTheDocument();
  });

  it('renders an error banner when retrieveRecord rejects (no crash)', async () => {
    const dataService = makeDataService(new Error('boom'));
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderWithProvider(<EmailReadingHeader selectedId="comm-1" dataService={dataService} />);

    await waitFor(() => expect(screen.getByText('Could not load this email header.')).toBeInTheDocument());
    errorSpy.mockRestore();
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', async () => {
    const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const dataService = makeDataService({ sprk_subject: 'Dark mode check', sprk_from: 'a@example.com' });

    renderWithProvider(<EmailReadingHeader selectedId="comm-1" dataService={dataService} />, webDarkTheme);

    await waitFor(() => expect(screen.getByText('Dark mode check')).toBeInTheDocument());
    expect(consoleErrorSpy).not.toHaveBeenCalled();
    consoleErrorSpy.mockRestore();
  });
});
