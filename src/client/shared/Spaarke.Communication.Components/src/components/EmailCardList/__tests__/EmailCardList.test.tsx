/**
 * EmailCardList.test.tsx
 *
 * Unit/RTL tests for `<EmailCardList />` (email-communication-solution-r5 task
 * 030). Covers the closed acceptance set from the task POML: Email-only
 * rendering (incl. the negative non-Email-exclusion invariant, FR-03),
 * loading/empty states (FR-19), selection + unread visuals, and dark-mode
 * theming (ADR-021).
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailCardList } from '../EmailCardList';
import type { EmailCardItem } from '../EmailCardList.types';

const EMAIL_TYPE = 100000000;
const TEAMS_TYPE = 100000003;

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

describe('EmailCardList', () => {
  it('renders one card per Email item and skips non-Email items (FR-03 negative invariant)', () => {
    const items: EmailCardItem[] = [
      makeItem({ id: 'e1', subject: 'Email one' }),
      makeItem({ id: 'e2', subject: 'Email two' }),
      makeItem({ id: 'e3', subject: 'Email three' }),
      makeItem({ id: 't1', subject: 'Teams message', communicationType: TEAMS_TYPE }),
    ];

    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);

    expect(screen.getAllByRole('listitem')).toHaveLength(3);
    expect(screen.getByText('Email one')).toBeInTheDocument();
    expect(screen.getByText('Email two')).toBeInTheDocument();
    expect(screen.getByText('Email three')).toBeInTheDocument();
    expect(screen.queryByText('Teams message')).not.toBeInTheDocument();
  });

  it('shows from / subject / preview / date for each card', () => {
    const items: EmailCardItem[] = [makeItem()];
    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);

    expect(screen.getByText('jane.doe@example.com')).toBeInTheDocument();
    expect(screen.getByText('Quarterly filing update')).toBeInTheDocument();
    expect(screen.getByText('Please find attached the latest draft for review.')).toBeInTheDocument();
    expect(screen.getByText('Jul 20')).toBeInTheDocument();
  });

  it('renders skeleton cards when isLoading and no real cards', () => {
    const items: EmailCardItem[] = [makeItem()];
    renderWithProvider(<EmailCardList items={items} isLoading skeletonCount={4} onSelect={jest.fn()} />);

    expect(screen.getByRole('list', { name: 'Loading emails' })).toBeInTheDocument();
    expect(screen.queryByText('Quarterly filing update')).not.toBeInTheDocument();
    expect(screen.queryByRole('listitem')).not.toBeInTheDocument();
    expect(screen.queryByText('No emails in this view')).not.toBeInTheDocument();
  });

  it('renders the empty state when not loading and there are zero Email items', () => {
    renderWithProvider(<EmailCardList items={[]} isLoading={false} onSelect={jest.fn()} />);

    expect(screen.getByText('No emails in this view')).toBeInTheDocument();
    expect(screen.queryByRole('listitem')).not.toBeInTheDocument();
  });

  it('renders the empty state when the only items present are non-Email', () => {
    const items: EmailCardItem[] = [makeItem({ id: 't1', communicationType: TEAMS_TYPE })];
    renderWithProvider(<EmailCardList items={items} isLoading={false} onSelect={jest.fn()} />);

    expect(screen.getByText('No emails in this view')).toBeInTheDocument();
  });

  it('marks the selectedId card active and fires onSelect on click; shows the unread visual', () => {
    const onSelect = jest.fn();
    const items: EmailCardItem[] = [
      makeItem({ id: 'e1', subject: 'Unread email', isUnread: true }),
      makeItem({ id: 'e2', subject: 'Read email', isUnread: false }),
    ];

    renderWithProvider(<EmailCardList items={items} selectedId="e2" onSelect={onSelect} />);

    const rows = screen.getAllByRole('listitem');
    expect(rows[1]).toHaveAttribute('aria-selected', 'true');
    expect(rows[0]).toHaveAttribute('aria-selected', 'false');

    // Unread visual: an "Unread" labeled dot renders for the unread row only.
    expect(screen.getByLabelText('Unread')).toBeInTheDocument();

    fireEvent.click(rows[0]);
    expect(onSelect).toHaveBeenCalledWith('e1');
  });

  it('renders the association review status dot when a card carries a reviewTone (owner UAT 2026-07-30 R2 item 5)', () => {
    const items: EmailCardItem[] = [
      makeItem({ id: 'r', subject: 'Needs review', reviewTone: 'red' }),
      makeItem({ id: 'y', subject: 'Needs confirm', reviewTone: 'yellow' }),
      makeItem({ id: 'g', subject: 'Confirmed', reviewTone: 'green' }),
    ];

    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);

    // Each tone renders its labelled status dot (role="img"), left of the sender.
    expect(screen.getByRole('img', { name: 'Requires review' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Needs confirmation' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Confirmed' })).toBeInTheDocument();
  });

  it('fires onSelect on keyboard activation (Enter)', () => {
    const onSelect = jest.fn();
    const items: EmailCardItem[] = [makeItem({ id: 'e1' })];

    renderWithProvider(<EmailCardList items={items} onSelect={onSelect} />);

    const row = screen.getByRole('listitem');
    fireEvent.keyDown(row, { key: 'Enter' });
    expect(onSelect).toHaveBeenCalledWith('e1');
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const items: EmailCardItem[] = [makeItem({ isUnread: true })];

    renderWithProvider(<EmailCardList items={items} selectedId="email-1" onSelect={jest.fn()} />, webDarkTheme);

    expect(screen.getByText('Quarterly filing update')).toBeInTheDocument();
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });

  it('renders a "New email" list-toolbar button that fires onCreateNew when the handler is provided (owner UAT 2026-08-03 Item 2)', () => {
    const onCreateNew = jest.fn();
    const items: EmailCardItem[] = [makeItem({ id: 'e1' })];

    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} onCreateNew={onCreateNew} />);

    const newEmail = screen.getByRole('button', { name: 'New email' });
    expect(newEmail).toBeInTheDocument();
    fireEvent.click(newEmail);
    expect(onCreateNew).toHaveBeenCalledWith();
  });

  it('renders the "New email" button ICON-ONLY (no visible text label) but keeps it accessible (owner UAT 2026-08-03 Item 3)', () => {
    const items: EmailCardItem[] = [makeItem({ id: 'e1' })];

    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} onCreateNew={jest.fn()} />);

    // Reachable by its accessible name (aria-label / title)...
    const newEmail = screen.getByRole('button', { name: 'New email' });
    expect(newEmail).toBeInTheDocument();
    // ...but the visible "New email" TEXT child was removed — the button has no text content.
    expect(newEmail).toHaveTextContent('');
  });

  it('omits the "New email" button when no onCreateNew handler is provided', () => {
    const items: EmailCardItem[] = [makeItem({ id: 'e1' })];

    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);

    expect(screen.queryByRole('button', { name: 'New email' })).not.toBeInTheDocument();
  });

  it('renders a Refresh list-toolbar button that fires onRefresh when the handler is provided (owner UAT 2026-08-03 Item 2)', () => {
    const onRefresh = jest.fn();
    const items: EmailCardItem[] = [makeItem({ id: 'e1' })];

    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} onRefresh={onRefresh} />);

    const refresh = screen.getByRole('button', { name: 'Refresh' });
    expect(refresh).toBeInTheDocument();
    fireEvent.click(refresh);
    expect(onRefresh).toHaveBeenCalledWith();
  });

  it('omits the Refresh button when no onRefresh handler is provided', () => {
    const items: EmailCardItem[] = [makeItem({ id: 'e1' })];

    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);

    expect(screen.queryByRole('button', { name: 'Refresh' })).not.toBeInTheDocument();
  });
});
