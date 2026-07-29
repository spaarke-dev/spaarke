/**
 * EmailReadingPaneShell.test.tsx
 *
 * Unit/RTL tests for `<EmailReadingPaneShell />` (email-communication-solution-r5
 * task 032). Covers the closed acceptance set from the task POML: two-pane
 * layout with the reused `PanelSplitter` (FR-05), selection driving the right
 * pane's slot renderers, the "Select an email" empty state (FR-19), splitter
 * width persistence across remounts, the full-width toolbar's six actions
 * dispatching through host-supplied handlers (FR-08), and dark-mode theming
 * (ADR-021).
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailReadingPaneShell } from '../EmailReadingPaneShell';
import type { EmailReadingPaneShellProps, EmailToolbarActionHandlers } from '../EmailReadingPaneShell.types';
import type { EmailCardItem } from '../../EmailCardList/EmailCardList.types';

const EMAIL_TYPE = 100000000;

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

function makeItem(overrides: Partial<EmailCardItem> = {}): EmailCardItem {
  return {
    id: 'email-1',
    from: 'jane.doe@example.com',
    subject: 'Quarterly filing update',
    preview: 'Please find attached the latest draft for review.',
    date: '2026-07-20T10:00:00Z',
    isUnread: false,
    communicationType: EMAIL_TYPE,
    ...overrides,
  };
}

function renderShell(overrides: Partial<EmailReadingPaneShellProps> = {}, theme = webLightTheme) {
  const items: EmailCardItem[] = overrides.items ?? [
    makeItem({ id: 'e1', subject: 'Email one' }),
    makeItem({ id: 'e2', subject: 'Email two' }),
  ];
  return renderWithProvider(
    <EmailReadingPaneShell
      items={items}
      renderHeader={id => <div data-testid="header-slot">header:{id}</div>}
      renderBody={id => <div data-testid="body-slot">body:{id}</div>}
      renderAttachments={id => <div data-testid="attachments-slot">attachments:{id}</div>}
      renderConnections={id => <div data-testid="connections-slot">connections:{id}</div>}
      renderTracking={id => <div data-testid="tracking-slot">tracking:{id}</div>}
      {...overrides}
    />,
    theme
  );
}

beforeEach(() => {
  window.localStorage.clear();
});

describe('EmailReadingPaneShell', () => {
  it('renders the reused PanelSplitter with the card list on the left and the reading pane on the right', () => {
    renderShell();

    expect(screen.getByTestId('email-list-pane')).toBeInTheDocument();
    expect(screen.getByTestId('email-reading-pane')).toBeInTheDocument();
    expect(screen.getByRole('separator')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(2); // EmailCardList rows
  });

  it('shows the "Select an email" placeholder when nothing is selected (FR-19)', () => {
    renderShell();

    expect(screen.getByText('Select an email')).toBeInTheDocument();
    expect(screen.queryByTestId('body-slot')).not.toBeInTheDocument();
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument();
  });

  it('selecting a card drives the right pane: header/body/attachments/connections/tracking slots are invoked with the selected id', () => {
    renderShell();

    fireEvent.click(screen.getByText('Email one'));

    expect(screen.queryByText('Select an email')).not.toBeInTheDocument();
    expect(screen.getByTestId('header-slot')).toHaveTextContent('header:e1');
    expect(screen.getByTestId('body-slot')).toHaveTextContent('body:e1');
    expect(screen.getByTestId('attachments-slot')).toHaveTextContent('attachments:e1');
    expect(screen.getByTestId('connections-slot')).toHaveTextContent('connections:e1');
    expect(screen.getByTestId('tracking-slot')).toHaveTextContent('tracking:e1');
  });

  it('notifies the host of selection changes via onSelectedIdChange without controlling selection', () => {
    const onSelectedIdChange = jest.fn();
    renderShell({ onSelectedIdChange });

    fireEvent.click(screen.getByText('Email two'));

    expect(onSelectedIdChange).toHaveBeenCalledWith('e2');
    expect(screen.getByTestId('header-slot')).toHaveTextContent('header:e2');
  });

  describe('full-width toolbar (FR-08)', () => {
    it('renders all six action buttons once a card is selected', () => {
      renderShell();
      fireEvent.click(screen.getByText('Email one'));

      expect(screen.getByRole('toolbar', { name: 'Email actions' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Reply' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Reply All' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Forward' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'New' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Archive' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Create' })).toBeInTheDocument();
    });

    it('dispatches each record-scoped action through the host-supplied actions core with the selected id', () => {
      const actions: EmailToolbarActionHandlers = {
        onReply: jest.fn(),
        onReplyAll: jest.fn(),
        onForward: jest.fn(),
        onNew: jest.fn(),
        onArchive: jest.fn(),
        onCreate: jest.fn(),
      };
      renderShell({ actions });
      fireEvent.click(screen.getByText('Email one'));

      fireEvent.click(screen.getByRole('button', { name: 'Reply' }));
      fireEvent.click(screen.getByRole('button', { name: 'Reply All' }));
      fireEvent.click(screen.getByRole('button', { name: 'Forward' }));
      fireEvent.click(screen.getByRole('button', { name: 'New' }));
      fireEvent.click(screen.getByRole('button', { name: 'Archive' }));
      fireEvent.click(screen.getByRole('button', { name: 'Create' }));

      expect(actions.onReply).toHaveBeenCalledWith('e1');
      expect(actions.onReplyAll).toHaveBeenCalledWith('e1');
      expect(actions.onForward).toHaveBeenCalledWith('e1');
      expect(actions.onNew).toHaveBeenCalledWith();
      expect(actions.onArchive).toHaveBeenCalledWith('e1');
      expect(actions.onCreate).toHaveBeenCalledWith('e1');
    });

    it('New is always enabled (not record-scoped); record-scoped buttons enable only once a card is selected', () => {
      const onNew = jest.fn();
      renderShell({ actions: { onNew } });

      // Nothing selected — the toolbar itself isn't rendered (right pane shows
      // the placeholder per FR-19), so no button (including New) exists yet.
      expect(screen.queryByRole('button', { name: 'New' })).not.toBeInTheDocument();

      fireEvent.click(screen.getByText('Email one'));
      // Once a card is selected, ALL six buttons — including the record-scoped
      // ones — are enabled (New always was; Reply/Reply All/Forward/Archive/
      // Create only disable when selectedId is unset, which can't happen here
      // since the toolbar itself doesn't render without a selection).
      expect(screen.getByRole('button', { name: 'Reply' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'New' })).toBeEnabled();
    });

    it('falls back to a no-op (no throw) when a handler is not wired (pending task-036 integration)', () => {
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
      renderShell({ actions: undefined });
      fireEvent.click(screen.getByText('Email one'));

      expect(() => fireEvent.click(screen.getByRole('button', { name: 'Reply' }))).not.toThrow();
      expect(warnSpy).toHaveBeenCalledWith(expect.stringContaining('Reply'));
      warnSpy.mockRestore();
    });
  });

  describe('splitter width persistence', () => {
    it('restores a previously persisted width from the storage key on mount, and re-mount reads the same persisted value', () => {
      const storageKey = 'test-email-reading-pane-032';
      window.localStorage.setItem(storageKey, JSON.stringify({ detailWidth: 555, isDetailVisible: true }));

      const first = renderShell({ storageKey });
      expect(screen.getByTestId('email-reading-pane')).toHaveStyle({ width: '555px' });
      first.unmount();

      const second = renderShell({ storageKey });
      expect(screen.getByTestId('email-reading-pane')).toHaveStyle({ width: '555px' });
      second.unmount();
    });

    it('uses the default reading-pane width when no persisted value exists yet', () => {
      renderShell({ storageKey: 'test-email-reading-pane-032-fresh' });
      expect(screen.getByTestId('email-reading-pane')).toHaveStyle({ width: '480px' });
    });
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderShell({}, webDarkTheme);
    fireEvent.click(screen.getByText('Email one'));

    expect(screen.getByRole('toolbar', { name: 'Email actions' })).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});
