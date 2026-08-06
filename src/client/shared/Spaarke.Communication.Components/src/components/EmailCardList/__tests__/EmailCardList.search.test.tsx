/**
 * EmailCardList.search.test.tsx
 *
 * RTL tests for the list toolbar search box (email-communication-solution-r5 —
 * Outlook-style list toolbar). Search filters visible cards client-side by
 * sender + subject (case-insensitive substring), BEFORE date bucketing.
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { EmailCardList } from '../EmailCardList';
import { EMAIL_COMMUNICATION_TYPE, type EmailCardItem } from '../EmailCardList.types';

function renderWithProvider(ui: React.ReactElement) {
  return render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);
}

// Use "today" (wall-clock) dates so every card lands in a rendered bucket.
const TODAY_ISO = new Date().toISOString();

function makeItem(overrides: Partial<EmailCardItem> = {}): EmailCardItem {
  return {
    id: 'e1',
    from: 'jane.doe@example.com',
    subject: 'Quarterly filing',
    preview: 'Preview text',
    date: TODAY_ISO,
    isUnread: false,
    communicationType: EMAIL_COMMUNICATION_TYPE,
    ...overrides,
  };
}

const items: EmailCardItem[] = [
  makeItem({ id: 'e1', from: 'jane.doe@example.com', subject: 'Quarterly filing' }),
  makeItem({ id: 'e2', from: 'bob.smith@example.com', subject: 'Lunch plans' }),
  makeItem({ id: 'e3', from: 'carol@example.com', subject: 'JANE referral note' }),
];

describe('EmailCardList toolbar search (owner UAT 2026-07-30 R2 item 4 — collapsed by default)', () => {
  it('renders a collapsed search ICON by default; clicking it reveals the field', () => {
    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);

    // Collapsed: the field is NOT rendered — only a right-aligned search icon.
    expect(screen.queryByPlaceholderText('Search mail')).not.toBeInTheDocument();
    const toggle = screen.getByRole('button', { name: 'Search mail' });

    fireEvent.click(toggle);
    expect(screen.getByPlaceholderText('Search mail')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(3);
  });

  it('filters cards by sender OR subject, case-insensitively (after opening the field)', () => {
    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Search mail' }));
    const box = screen.getByPlaceholderText('Search mail');

    // "jane" matches e1 by sender and e3 by subject ("JANE referral") — not e2.
    fireEvent.change(box, { target: { value: 'jane' } });

    expect(screen.getByText('Quarterly filing')).toBeInTheDocument();
    expect(screen.getByText('JANE referral note')).toBeInTheDocument();
    expect(screen.queryByText('Lunch plans')).not.toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
  });

  it('shows a no-match state when the search excludes everything, keeping the field open', () => {
    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Search mail' }));
    const box = screen.getByPlaceholderText('Search mail');

    fireEvent.change(box, { target: { value: 'zzz-no-match' } });

    expect(screen.getByText('No emails match your search')).toBeInTheDocument();
    expect(screen.queryAllByRole('listitem')).toHaveLength(0);
    // The field stays open (query is non-empty) so the user can clear the filter.
    expect(screen.getByPlaceholderText('Search mail')).toBeInTheDocument();
  });

  it('collapses back to the icon on blur when the query is empty', () => {
    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Search mail' }));
    const box = screen.getByPlaceholderText('Search mail');

    fireEvent.blur(box);

    expect(screen.queryByPlaceholderText('Search mail')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Search mail' })).toBeInTheDocument();
  });
});
