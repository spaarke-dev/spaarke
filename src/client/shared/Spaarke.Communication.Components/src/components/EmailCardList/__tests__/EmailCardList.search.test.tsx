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

describe('EmailCardList toolbar search', () => {
  it('renders a search box in the toolbar', () => {
    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);
    expect(screen.getByPlaceholderText('Search mail')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(3);
  });

  it('filters cards by sender OR subject, case-insensitively', () => {
    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);
    const box = screen.getByPlaceholderText('Search mail');

    // "jane" matches e1 by sender and e3 by subject ("JANE referral") — not e2.
    fireEvent.change(box, { target: { value: 'jane' } });

    expect(screen.getByText('Quarterly filing')).toBeInTheDocument();
    expect(screen.getByText('JANE referral note')).toBeInTheDocument();
    expect(screen.queryByText('Lunch plans')).not.toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(2);
  });

  it('shows a no-match state when the search excludes everything, keeping the toolbar', () => {
    renderWithProvider(<EmailCardList items={items} onSelect={jest.fn()} />);
    const box = screen.getByPlaceholderText('Search mail');

    fireEvent.change(box, { target: { value: 'zzz-no-match' } });

    expect(screen.getByText('No emails match your search')).toBeInTheDocument();
    expect(screen.queryAllByRole('listitem')).toHaveLength(0);
    // Toolbar remains so the user can clear the search.
    expect(screen.getByPlaceholderText('Search mail')).toBeInTheDocument();
  });
});
