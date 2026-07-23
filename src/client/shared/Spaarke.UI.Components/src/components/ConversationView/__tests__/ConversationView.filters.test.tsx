/**
 * ConversationView.filters.test.tsx (task 014, FR-09)
 *
 * Additive in-conversation filters: Email/Message type toggles + a word
 * dropdown, combined with AND-of-active-facets semantics. The pure predicate
 * (`messagePassesFilters`) carries the highest-value, most exhaustive coverage
 * (type gating both directions, word substring, HTML strip, additive AND);
 * the component tests prove the toggles/dropdown/empty-state wiring and — the
 * key NFR — that filtering is PRESENTATIONAL (no re-fetch, no core mutation).
 *
 * Per ADR-038/ADR-028 a fake `authenticatedFetch` is the I/O seam (no
 * `Mock<HttpMessageHandler>`, no `@spaarke/auth` import).
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import {
  ConversationView,
  messagePassesFilters,
  messageSearchText,
  extractWordOptions,
  type ConversationFilters,
} from '../ConversationView';
import type { ConversationViewProps } from '../ConversationView.types';
import type { TimelineMessage } from '../../CommunicationTimeline/CommunicationTimeline.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';

const USER_2 = '22222222-2222-2222-2222-222222222222';

const ALL_ON: ConversationFilters = { emailEnabled: true, messageEnabled: true, word: '' };

// ---------------------------------------------------------------------------
// Pure helpers — the exhaustive, deterministic coverage
// ---------------------------------------------------------------------------

function msg(overrides: Partial<TimelineMessage> = {}): TimelineMessage {
  return {
    id: 'm1',
    channelType: 'email',
    channelTypeRaw: 100000000,
    sender: 'alice@example.com',
    senderSystemUserId: undefined,
    senderName: 'Alice',
    direction: undefined,
    sentOn: '2026-07-20T10:00:00Z',
    createdOn: '2026-07-20T10:00:00Z',
    body: 'the quarterly invoice',
    bodyFormat: 'text',
    privilege: 0,
    attachments: [],
    ...overrides,
  };
}

describe('messagePassesFilters (FR-09 additive AND-of-facets)', () => {
  it('passes an email bubble only when Email is enabled', () => {
    expect(messagePassesFilters(msg({ channelType: 'email' }), ALL_ON)).toBe(true);
    expect(messagePassesFilters(msg({ channelType: 'email' }), { ...ALL_ON, emailEnabled: false })).toBe(false);
  });

  it('passes a message bubble only when Message is enabled', () => {
    expect(messagePassesFilters(msg({ channelType: 'message' }), ALL_ON)).toBe(true);
    expect(messagePassesFilters(msg({ channelType: 'message' }), { ...ALL_ON, messageEnabled: false })).toBe(false);
  });

  it('word facet is a case-insensitive substring over body + sender + senderName', () => {
    const m = msg({ body: 'The Quarterly Invoice', sender: 'bob@corp.com', senderName: 'Bob' });
    expect(messagePassesFilters(m, { ...ALL_ON, word: 'invoice' })).toBe(true);
    expect(messagePassesFilters(m, { ...ALL_ON, word: 'INVOICE' })).toBe(true);
    expect(messagePassesFilters(m, { ...ALL_ON, word: 'bob' })).toBe(true); // senderName
    expect(messagePassesFilters(m, { ...ALL_ON, word: 'corp' })).toBe(true); // sender email
    expect(messagePassesFilters(m, { ...ALL_ON, word: 'refund' })).toBe(false);
  });

  it('is additive AND: a type-enabled bubble still fails if the word does not match', () => {
    const m = msg({ channelType: 'message', body: 'hello world' });
    expect(messagePassesFilters(m, { emailEnabled: true, messageEnabled: true, word: 'zzz' })).toBe(false);
    expect(messagePassesFilters(m, { emailEnabled: true, messageEnabled: true, word: 'world' })).toBe(true);
  });

  it('empty word = no word facet (type gate only)', () => {
    expect(messagePassesFilters(msg(), { ...ALL_ON, word: '   ' })).toBe(true);
  });

  it('word matching ignores HTML markup (matches visible text, not tags)', () => {
    const m = msg({ bodyFormat: 'html', body: '<p class="invoice-x">Please <b>pay</b> now</p>' });
    expect(messageSearchText(m)).not.toContain('<p');
    expect(messagePassesFilters(m, { ...ALL_ON, word: 'pay' })).toBe(true);
    expect(messagePassesFilters(m, { ...ALL_ON, word: 'class' })).toBe(false); // markup attr, not visible text
  });
});

describe('extractWordOptions', () => {
  it('returns distinct ≥3-char lowercased tokens, sorted', () => {
    const opts = extractWordOptions([msg({ body: 'Invoice invoice PAID', sender: 'a@b.co', senderName: 'Al' })]);
    // 'al' (2) and 'co'/'a'/'b' (<3) excluded; 'invoice'/'paid' kept + deduped.
    expect(opts).toContain('invoice');
    expect(opts).toContain('paid');
    expect(opts).not.toContain('al');
    expect(opts).toEqual([...opts].sort());
    // Deduped (single 'invoice' despite two occurrences + case difference).
    expect(opts.filter(w => w === 'invoice')).toHaveLength(1);
  });

  it('caps the option count', () => {
    const many = Array.from({ length: 100 }, (_, i) => msg({ id: `w${i}`, body: `word${i}` }));
    expect(extractWordOptions(many, 40)).toHaveLength(40);
  });
});

// ---------------------------------------------------------------------------
// Component — toggle/dropdown/empty wiring + presentational (no-refetch) proof
// ---------------------------------------------------------------------------

function dto(id: string, overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
  return {
    messageId: id,
    body: `body-${id}`,
    bodyFormat: 100000000, // PlainText → body renders as a findable text node
    communicationType: 100000000, // Email
    from: 'x@example.com',
    direction: null,
    sentBy: USER_2,
    sentByName: 'Colleague',
    sentAt: '2026-07-20T10:00:00Z',
    createdOn: '2026-07-20T10:00:00Z',
    inReplyTo: null,
    privilege: 0,
    attachments: [],
    ...overrides,
  };
}

// Email-type renders as `<EmailInFlowBlock />` (task 021 / FR-04) — a compact
// subject/from/to block (role="group", aria-label "Email: <subject>…"), NOT a
// chat bubble. `subject` makes the block identifiable; `body` is retained
// because the word-filter still matches on the underlying message data (the
// block just doesn't RENDER the body). Chat-type stays a bubble (role="article").
const EMAIL_MSG = dto('e1', { body: 'invoice details', subject: 'invoice memo', communicationType: 100000000 });
const CHAT_MSG = dto('m1', { body: 'quick chat', communicationType: 100000004 });

function jsonResponse(body: unknown): Response {
  return { ok: true, json: async () => body } as unknown as Response;
}

function buildFetch(messages: IThreadMessageDto[]) {
  const store = { reads: 0 };
  const fetchMock = jest.fn(async (url: string) => {
    if (url.includes('/unread-count')) {
      return jsonResponse({ threadId: 't1', since: null, unreadCount: 0 });
    }
    store.reads += 1;
    return jsonResponse({ threadId: 't1', name: null, messages, count: messages.length });
  });
  return { fetchMock, store };
}

function renderView(overrides: Partial<ConversationViewProps>, theme = webLightTheme) {
  const props: ConversationViewProps = {
    threadId: 't1',
    currentUserSystemUserId: '00000000-0000-0000-0000-000000000000',
    // Large interval so no interval poll fires mid-test — read-count changes would then only be re-fetches.
    pollIntervalMs: 100000,
    authenticatedFetch: undefined as unknown as ConversationViewProps['authenticatedFetch'],
    ...overrides,
  };
  return render(
    <FluentProvider theme={theme}>
      <ConversationView {...props} />
    </FluentProvider>
  );
}

describe('ConversationView — in-conversation filters (FR-09)', () => {
  // The email renders as a compact block (role="group", "Email: invoice memo…");
  // the chat renders as a bubble (role="article", "quick chat"). The type
  // toggles filter each independently.
  const emailBlock = () => screen.queryByRole('group', { name: /^Email: invoice memo/ });

  it('both toggles on shows both types; disabling Message hides message-type bubbles', async () => {
    const { fetchMock } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(emailBlock()).toBeInTheDocument());
    expect(screen.getByRole('article')).toBeInTheDocument();
    expect(screen.getByText('quick chat')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show chat messages' }));
    await waitFor(() => expect(screen.queryByText('quick chat')).not.toBeInTheDocument());
    expect(emailBlock()).toBeInTheDocument();
    expect(screen.queryByRole('article')).not.toBeInTheDocument();
  });

  it('disabling Email hides email-type blocks', async () => {
    const { fetchMock } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(emailBlock()).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Show email messages' }));
    await waitFor(() => expect(emailBlock()).not.toBeInTheDocument());
    expect(screen.getByText('quick chat')).toBeInTheDocument();
  });

  it('shows a distinct "no messages match filters" state when all types are disabled (≠ thread-empty)', async () => {
    const { fetchMock } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(emailBlock()).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Show email messages' }));
    await userEvent.click(screen.getByRole('button', { name: 'Show chat messages' }));

    expect(await screen.findByText('No messages match the current filters.')).toBeInTheDocument();
    expect(screen.queryByText('No messages yet.')).not.toBeInTheDocument();
    expect(screen.queryAllByRole('article')).toHaveLength(0);
    expect(emailBlock()).not.toBeInTheDocument();
  });

  it('filtering is presentational — toggling a filter does NOT re-fetch, and clearing restores the full set', async () => {
    const { fetchMock, store } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(emailBlock()).toBeInTheDocument());
    expect(screen.getByText('quick chat')).toBeInTheDocument();
    const readsAfterLoad = store.reads;

    await userEvent.click(screen.getByRole('button', { name: 'Show chat messages' }));
    await waitFor(() => expect(screen.queryByText('quick chat')).not.toBeInTheDocument());
    // No re-fetch: the read count is unchanged by filtering.
    expect(store.reads).toBe(readsAfterLoad);

    // Clearing the filter restores the full set from the SAME already-loaded state.
    await userEvent.click(screen.getByRole('button', { name: 'Show chat messages' }));
    await waitFor(() => expect(screen.getByText('quick chat')).toBeInTheDocument());
    expect(emailBlock()).toBeInTheDocument();
    expect(store.reads).toBe(readsAfterLoad);
  });

  it('the search box (revealed by the search icon) narrows to matching items (additive with the type toggles)', async () => {
    const { fetchMock } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(emailBlock()).toBeInTheDocument());

    // The text filter is hidden until the search icon is clicked (task 062 / §B7).
    expect(screen.queryByRole('textbox', { name: 'Filter messages by text' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Search messages' }));

    // 'invoice' matches the email's underlying body ('invoice details') + subject
    // ('invoice memo'), which the word facet still matches even though the block
    // doesn't render the body; 'quick chat' does not match.
    await userEvent.type(await screen.findByRole('textbox', { name: 'Filter messages by text' }), 'invoice');

    await waitFor(() => expect(screen.queryByText('quick chat')).not.toBeInTheDocument());
    expect(emailBlock()).toBeInTheDocument();
  });

  it('the Unread facet (additive) hides already-loaded messages — they count as read on open', async () => {
    const { fetchMock } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(emailBlock()).toBeInTheDocument());

    // Messages present at first load are "already read" (not newer than the
    // initial-load watermark), so enabling Unread-only narrows them all out —
    // proving the facet is wired + additive + keyboard-operable (aria-pressed).
    await userEvent.click(screen.getByRole('button', { name: 'Show unread messages only' }));

    expect(await screen.findByText('No messages match the current filters.')).toBeInTheDocument();
    expect(emailBlock()).not.toBeInTheDocument();
    expect(screen.queryByText('quick chat')).not.toBeInTheDocument();

    // Toggling it back off restores the full set (presentational — no re-fetch).
    await userEvent.click(screen.getByRole('button', { name: 'Show unread messages only' }));
    await waitFor(() => expect(emailBlock()).toBeInTheDocument());
    expect(screen.getByText('quick chat')).toBeInTheDocument();
  });

  it('the filter bar is hidden when the thread is empty (nothing to filter)', async () => {
    const { fetchMock } = buildFetch([]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    expect(await screen.findByText('No messages yet.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Show email messages' })).not.toBeInTheDocument();
  });

  it('renders the filter bar under dark mode via the host FluentProvider (ADR-021)', async () => {
    const { fetchMock } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView(
      { authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] },
      webDarkTheme
    );
    await waitFor(() => expect(screen.getByRole('button', { name: 'Show email messages' })).toBeInTheDocument());
  });
});

// ---------------------------------------------------------------------------
// Message toolbar changes (R3 UAT 2026-07-22): Send icon-only (item 6),
// Refresh relocated into the toolbar (item 7), Mark-as-read tool (item 5c).
// ---------------------------------------------------------------------------

describe('ConversationView — message toolbar (R3 UAT 2026-07-22)', () => {
  it('Send is icon-only (no visible "Send" text) and Refresh is a toolbar tool', async () => {
    const { fetchMock } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(screen.getByRole('article')).toBeInTheDocument());

    // Send: reachable by accessible name (aria-label), but renders no "Send" text label (item 6).
    const send = screen.getByRole('button', { name: 'Send message' });
    expect(send).toHaveTextContent('');
    // Refresh: present as a toolbar tool (item 7 — moved out of the compose row).
    expect(screen.getByRole('button', { name: 'Refresh conversation' })).toBeInTheDocument();
  });

  it('the "Mark conversation as read" tool fires onMarkThreadRead (item 5c)', async () => {
    const onMarkThreadRead = jest.fn();
    const { fetchMock } = buildFetch([EMAIL_MSG, CHAT_MSG]);
    renderView({
      authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'],
      onMarkThreadRead,
    });

    await waitFor(() => expect(screen.getByRole('article')).toBeInTheDocument());
    await userEvent.click(screen.getByRole('button', { name: 'Mark conversation as read' }));
    expect(onMarkThreadRead).toHaveBeenCalledTimes(1);
  });

  it('Refresh is available even when the thread is empty (item 7)', async () => {
    const { fetchMock } = buildFetch([]);
    renderView({ authenticatedFetch: fetchMock as unknown as ConversationViewProps['authenticatedFetch'] });

    expect(await screen.findByText('No messages yet.')).toBeInTheDocument();
    // The filter toggles + mark-as-read stay hidden (nothing to filter), but Refresh persists.
    expect(screen.getByRole('button', { name: 'Refresh conversation' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Mark conversation as read' })).not.toBeInTheDocument();
  });
});
