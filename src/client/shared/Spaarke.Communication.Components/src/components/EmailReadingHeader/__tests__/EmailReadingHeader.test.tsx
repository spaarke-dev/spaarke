/**
 * EmailReadingHeader.test.tsx
 *
 * Unit/RTL tests for `<EmailReadingHeader />` — the reading-pane HEADER BAND
 * (email-communication-solution-r5, reading-pane layout redesign; supersedes
 * task 034's original From/To/Cc/Bcc header). Covers: subject renders
 * prominently, the compact tracking trio reflects + writes values via the
 * host-supplied callbacks, "Open full form" dispatches with the selected id,
 * and dark-mode theming (ADR-021).
 *
 * This component is now PURELY presentational — no `dataService`, no
 * internal fetch, no loading/error state (that internal
 * `retrieveRecord(..., $select)` call was the "Could not load this email
 * header" bug; it has been removed). Every test therefore supplies props
 * directly instead of mocking a data service.
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailReadingHeader } from '../EmailReadingHeader';
import type { IEmailReadingHeaderProps } from '../EmailReadingHeader.types';

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

const ACCESS_PERMISSION_OPTIONS = [
  { value: 100000000, label: 'Standard' },
  { value: 100000001, label: 'Limited' },
  { value: 100000002, label: 'Restricted' },
];

function baseProps(overrides: Partial<IEmailReadingHeaderProps> = {}): IEmailReadingHeaderProps {
  return {
    communicationId: 'comm-1',
    subject: 'Quarterly filing update',
    monitor: false,
    highPriority: false,
    accessPermission: 100000000,
    accessPermissionOptions: ACCESS_PERMISSION_OPTIONS,
    onMonitorChange: jest.fn(),
    onHighPriorityChange: jest.fn(),
    onAccessPermissionChange: jest.fn(),
    onOpenFullForm: jest.fn(),
    ...overrides,
  };
}

describe('EmailReadingHeader', () => {
  it('renders the subject prominently and the compact tracking trio, with no from/to/cc/bcc', () => {
    renderWithProvider(<EmailReadingHeader {...baseProps()} />);

    expect(screen.getByTestId('email-reading-header')).toBeInTheDocument();
    expect(screen.getByText('Quarterly filing update')).toBeInTheDocument();
    expect(screen.getByTestId('email-tracking-panel')).toBeInTheDocument();
    // The redesigned header band no longer renders From/To/Cc/Bcc — those
    // moved to the dedicated `EmailRecipients` block.
    expect(screen.queryByText('From')).not.toBeInTheDocument();
    expect(screen.queryByText('To')).not.toBeInTheDocument();
    // Compact mode hides the "Tracking" section label and field captions.
    expect(screen.queryByText('Tracking')).not.toBeInTheDocument();
  });

  it('renders "(no subject)" when the subject is null/empty', () => {
    renderWithProvider(<EmailReadingHeader {...baseProps({ subject: null })} />);
    expect(screen.getByText('(no subject)')).toBeInTheDocument();
  });

  it('reflects tracking values and dispatches writes through the host-supplied callbacks', () => {
    const onMonitorChange = jest.fn();
    renderWithProvider(<EmailReadingHeader {...baseProps({ monitor: true, onMonitorChange })} />);

    // Compact mode hides field captions, so the switches have no accessible
    // name beyond their own Yes/No label — select by grid position instead
    // (Monitor is the first of the two switches; TrackingFieldTrio's fixed
    // column order — Monitor, High priority, Access — see its own suite).
    const [monitorSwitch] = screen.getAllByRole('switch');
    fireEvent.click(monitorSwitch);
    expect(onMonitorChange).toHaveBeenCalledWith(false);
  });

  it('renders the "Open full form" trigger and dispatches it with the selected communication id', () => {
    const onOpenFullForm = jest.fn();
    renderWithProvider(<EmailReadingHeader {...baseProps({ communicationId: 'comm-42', onOpenFullForm })} />);

    fireEvent.click(screen.getByRole('button', { name: 'Open full form' }));
    expect(onOpenFullForm).toHaveBeenCalledWith('comm-42');
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    renderWithProvider(<EmailReadingHeader {...baseProps({ subject: 'Dark mode check' })} />, webDarkTheme);

    expect(screen.getByText('Dark mode check')).toBeInTheDocument();
    expect(consoleErrorSpy).not.toHaveBeenCalled();
    consoleErrorSpy.mockRestore();
  });
});
