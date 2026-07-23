/**
 * ConversationView.test.tsx (task 011, FR-02/03)
 *
 * Component + pure-function coverage for the Teams-style bubble view.
 * Highest-value assertion (per the task notes): alignment is decided
 * STRICTLY from `senderSystemUserId`, never from the `sender` email string —
 * several fixtures deliberately use colliding/identical email addresses
 * across different senders (the shared-mailbox scenario flagged by task
 * 010's characterization notes) to prove the email string is never
 * consulted for alignment.
 *
 * Per ADR-038/ADR-028: a fake `authenticatedFetch` is the I/O seam (no
 * `Mock<HttpMessageHandler>`, no `@spaarke/auth` import) — same pattern as
 * `CommunicationTimeline.characterize.poll.test.ts`.
 */
import * as React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { ConversationView, buildConversationRenderItems, isOwnMessage } from '../ConversationView';
import type { ConversationViewProps } from '../ConversationView.types';
import type { TimelineEntry } from '../../CommunicationTimeline/CommunicationTimeline.buildTimeline';
import type { TimelineMessage } from '../../CommunicationTimeline/CommunicationTimeline.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';

const USER_1 = '11111111-1111-1111-1111-111111111111';
const USER_2 = '22222222-2222-2222-2222-222222222222';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function dto(id: string, overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
  return {
    messageId: id,
    body: `body-${id}`,
    bodyFormat: 100000001, // HTML
    // Message-type: this suite characterizes the CHAT-BUBBLE mechanics
    // (mine/others alignment, status, day dividers, keyboard focus) — those are
    // bubble concepts. Email-type now renders as `<EmailInFlowBlock />` (task
    // 021 / FR-04), covered by `ConversationView.emailInFlow.test.tsx`.
    communicationType: 100000004, // Message
    from: 'shared-mailbox@example.com',
    direction: null,
    sentBy: null,
    sentByName: null,
    sentAt: '2026-07-20T10:00:00Z',
    createdOn: '2026-07-20T10:00:00Z',
    inReplyTo: null,
    privilege: 0,
    attachments: [],
    ...overrides,
  };
}

function jsonResponse(body: unknown): Response {
  return { ok: true, json: async () => body } as unknown as Response;
}

function buildFetch(messages: IThreadMessageDto[]): jest.Mock {
  return jest.fn(async (url: string) => {
    if (url.includes('/unread-count')) {
      return jsonResponse({ threadId: 't1', since: null, unreadCount: 0 });
    }
    return jsonResponse({ threadId: 't1', name: null, messages, count: messages.length });
  });
}

function buildFailingFetch(): jest.Mock {
  return jest.fn(
    async () =>
      ({
        ok: false,
        status: 500,
        headers: { get: () => 'application/json' },
        json: async () => ({ detail: 'Server error' }),
      }) as unknown as Response
  );
}

function buildHangingFetch(): jest.Mock {
  return jest.fn(() => new Promise<Response>(() => {})); // never resolves — pins the loading state
}

function renderView(overrides: Partial<ConversationViewProps> = {}, theme = webLightTheme) {
  const authenticatedFetch = overrides.authenticatedFetch ?? buildFetch([]);
  const props: ConversationViewProps = {
    threadId: 't1',
    currentUserSystemUserId: USER_1,
    authenticatedFetch: authenticatedFetch as unknown as ConversationViewProps['authenticatedFetch'],
    ...overrides,
  };
  return render(
    <FluentProvider theme={theme}>
      <ConversationView {...props} />
    </FluentProvider>
  );
}

// ---------------------------------------------------------------------------
// isOwnMessage — pure function, the highest-value assertion (FR-02/FR-18)
// ---------------------------------------------------------------------------

describe('isOwnMessage', () => {
  function message(overrides: Partial<TimelineMessage> = {}): TimelineMessage {
    return {
      id: 'm1',
      channelType: 'email',
      channelTypeRaw: 100000000,
      sender: 'shared-mailbox@example.com',
      senderSystemUserId: undefined,
      senderName: undefined,
      direction: undefined,
      sentOn: '2026-07-20T10:00:00Z',
      createdOn: '2026-07-20T10:00:00Z',
      body: 'hi',
      bodyFormat: 'html',
      privilege: 0,
      attachments: [],
      ...overrides,
    };
  }

  it('is true when senderSystemUserId matches the caller', () => {
    expect(isOwnMessage(message({ senderSystemUserId: USER_1 }), USER_1)).toBe(true);
  });

  it('is false when senderSystemUserId differs, even with the IDENTICAL sender email', () => {
    const m = message({ sender: 'shared-mailbox@example.com', senderSystemUserId: USER_2 });
    expect(isOwnMessage(m, USER_1)).toBe(false);
  });

  it('is true for the caller even when a DIFFERENT sender shares the SAME systemuserid impersonation is unaffected by', () => {
    const m = message({ sender: 'other-alias@example.com', senderSystemUserId: USER_1 });
    expect(isOwnMessage(m, USER_1)).toBe(true);
  });

  it('is false when senderSystemUserId is null/undefined, regardless of email match', () => {
    expect(isOwnMessage(message({ senderSystemUserId: null, sender: 'me@example.com' }), USER_1)).toBe(false);
    expect(isOwnMessage(message({ senderSystemUserId: undefined, sender: 'me@example.com' }), USER_1)).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// buildConversationRenderItems — pure day-divider grouping
// ---------------------------------------------------------------------------

describe('buildConversationRenderItems', () => {
  function entry(id: string, sentOn: string): TimelineEntry {
    return {
      depth: 0,
      message: {
        id,
        channelType: 'email',
        channelTypeRaw: 100000000,
        sender: 's@example.com',
        sentOn,
        createdOn: sentOn,
        body: 'x',
        bodyFormat: 'html',
        privilege: 0,
        attachments: [],
      },
    };
  }

  it('inserts exactly one leading divider (the first day header) when all entries share one calendar day', () => {
    const items = buildConversationRenderItems([
      entry('a', '2026-07-20T09:00:00Z'),
      entry('b', '2026-07-20T15:00:00Z'),
    ]);
    expect(items.map(i => i.kind)).toEqual(['divider', 'message', 'message']);
  });

  it('inserts a divider at each day boundary, preserving chronological order', () => {
    const items = buildConversationRenderItems([
      entry('a', '2026-07-19T09:00:00Z'),
      entry('b', '2026-07-20T09:00:00Z'),
      entry('c', '2026-07-20T15:00:00Z'),
    ]);
    expect(items.map(i => i.kind)).toEqual(['divider', 'message', 'divider', 'message', 'message']);
    expect(items.map(i => (i.kind === 'message' ? i.entry.message.id : i.label))).toEqual([
      expect.any(String),
      'a',
      expect.any(String),
      'b',
      'c',
    ]);
  });

  it('skips a divider for a message with no resolvable date, but still labels the next dated message', () => {
    const items = buildConversationRenderItems([entry('a', ''), entry('b', '2026-07-20T09:00:00Z')]);
    // 'a' (no date) gets no divider; 'b' is the first entry with a resolvable date, so it starts a group.
    expect(items.map(i => i.kind)).toEqual(['message', 'divider', 'message']);
    expect(items[0]).toMatchObject({ kind: 'message', key: 'a' });
    expect(items[2]).toMatchObject({ kind: 'message', key: 'b' });
  });
});

// ---------------------------------------------------------------------------
// Component — alignment, status/label placement, day dividers, states, dark mode
// ---------------------------------------------------------------------------

describe('ConversationView', () => {
  it('mine-right / others-left derives from sender identity, not email (same email, different systemuserid)', async () => {
    const messages = [
      dto('own', { sentBy: USER_1, sentByName: 'Me', from: 'shared-mailbox@example.com' }),
      dto('other', { sentBy: USER_2, sentByName: 'Colleague', from: 'shared-mailbox@example.com' }),
    ];
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(screen.getByRole('article', { name: /^Your message/ })).toBeInTheDocument());
    expect(screen.getByRole('article', { name: /^Message from Colleague/ })).toBeInTheDocument();
  });

  it('renders status on own bubbles only and sender label on others bubbles only', async () => {
    const messages = [
      dto('own', { sentBy: USER_1, sentByName: 'Me' }),
      dto('other', { sentBy: USER_2, sentByName: 'Colleague' }),
    ];
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    const ownBubble = await screen.findByRole('article', { name: /^Your message/ });
    expect(within(ownBubble).getByText('Sent')).toBeInTheDocument();

    const otherBubble = screen.getByRole('article', { name: /^Message from Colleague/ });
    expect(within(otherBubble).queryByText('Sent')).not.toBeInTheDocument();

    // Sender label renders above the OTHER bubble, not the own one.
    expect(screen.getByText('Colleague')).toBeInTheDocument();
    expect(screen.queryByText('Me')).not.toBeInTheDocument();
  });

  it('renders a day divider across a multi-day span, newest message last (bottom)', async () => {
    const messages = [
      dto('day1', {
        sentAt: '2026-07-19T09:00:00Z',
        createdOn: '2026-07-19T09:00:00Z',
        sentBy: USER_2,
        sentByName: 'Colleague',
      }),
      dto('day2', { sentAt: '2026-07-20T09:00:00Z', createdOn: '2026-07-20T09:00:00Z', sentBy: USER_1 }),
    ];
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    await waitFor(() => expect(screen.getAllByRole('article')).toHaveLength(2));
    // One divider per day group (incl. a leading divider for the first day) — two distinct days here.
    expect(screen.getAllByRole('separator')).toHaveLength(2);

    const bubbles = screen.getAllByRole('article');
    // Chronological, oldest-first in DOM order == newest rendered last (bottom in a column flex list).
    expect(bubbles[0]).toHaveAccessibleName(/^Message from Colleague/);
    expect(bubbles[bubbles.length - 1]).toHaveAccessibleName(/^Your message/);
  });

  it('renders a loading state before the first poll resolves', () => {
    renderView({ authenticatedFetch: buildHangingFetch() as unknown as ConversationViewProps['authenticatedFetch'] });
    expect(screen.getByText('Loading conversation…')).toBeInTheDocument();
  });

  it('renders an empty state when the thread has no messages', async () => {
    renderView({ authenticatedFetch: buildFetch([]) as unknown as ConversationViewProps['authenticatedFetch'] });
    expect(await screen.findByText('No messages yet.')).toBeInTheDocument();
  });

  it('renders an error state distinct from empty/loading when the initial load fails', async () => {
    renderView({ authenticatedFetch: buildFailingFetch() as unknown as ConversationViewProps['authenticatedFetch'] });
    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText('No messages yet.')).not.toBeInTheDocument();
    expect(screen.queryByText('Loading conversation…')).not.toBeInTheDocument();
  });

  it('renders under dark mode via the host FluentProvider (ADR-021) without error', async () => {
    const messages = [dto('m1', { sentBy: USER_1 })];
    renderView(
      { authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] },
      webDarkTheme
    );
    await waitFor(() => expect(screen.getByRole('article')).toBeInTheDocument());
  });

  it('exposes an ARIA log region for the message list (NFR-05)', async () => {
    renderView({ authenticatedFetch: buildFetch([]) as unknown as ConversationViewProps['authenticatedFetch'] });
    expect(screen.getByRole('log', { name: 'Conversation messages' })).toBeInTheDocument();
    // Let the mocked fetch's poll resolve before the test tears the component down, so the
    // MERGE_POLL dispatch it triggers doesn't fire after this test has already returned.
    await screen.findByText('No messages yet.');
  });

  it('each bubble is keyboard-focusable (tabIndex 0) in chronological DOM order (NFR-05)', async () => {
    const messages = [
      dto('a', { sentAt: '2026-07-20T09:00:00Z', createdOn: '2026-07-20T09:00:00Z', sentBy: USER_1 }),
      dto('b', { sentAt: '2026-07-20T10:00:00Z', createdOn: '2026-07-20T10:00:00Z', sentBy: USER_2 }),
    ];
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    const bubbles = await screen.findAllByRole('article');
    expect(bubbles).toHaveLength(2);
    for (const bubble of bubbles) {
      expect((bubble as HTMLElement).tabIndex).toBe(0);
    }
  });
});
