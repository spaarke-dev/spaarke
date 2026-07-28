/**
 * ConversationView.emailInFlow.test.tsx (task 021, FR-04)
 *
 * Email-in-flow rendering: an EMAIL-type communication renders as a COMPACT
 * block (`<EmailInFlowBlock />` — subject/from/to + a SINGLE "Email" indicator
 * + an open-icon) rather than a chat bubble, while MESSAGE-type keeps its
 * bubble. The open-icon fires `onOpenEmail`, which a host wires to the extended
 * `<SendEmailDialog />` (task 020: `threadId` / `regarding`) — proving FR-04's
 * "open → extended dialog with thread + regarding context".
 *
 * Per ADR-038/ADR-028: a fake `authenticatedFetch` is the I/O seam (no
 * `Mock<HttpMessageHandler>`, no `@spaarke/auth` import) — same convention as
 * the sibling `ConversationView.test.tsx`.
 */
import * as React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { ConversationView } from '../ConversationView';
import { SendEmailDialog } from '../../EmailComposer/wrappers/SendEmailDialog';
import type { ConversationViewProps } from '../ConversationView.types';
import type { TimelineMessage } from '../../CommunicationTimeline/CommunicationTimeline.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';

const USER_1 = '11111111-1111-1111-1111-111111111111';
const USER_2 = '22222222-2222-2222-2222-222222222222';

const EMAIL = 100000000; // sprk_communicationtype: Email
const MESSAGE = 100000004; // sprk_communicationtype: Message

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function dto(id: string, overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
  return {
    messageId: id,
    body: `body-${id}`,
    bodyFormat: 100000001, // HTML
    communicationType: EMAIL,
    from: 'alice@example.com',
    subject: null,
    to: [],
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
// Email renders as a compact block, message stays a bubble
// ---------------------------------------------------------------------------

describe('ConversationView email-in-flow (FR-04)', () => {
  it('renders an email-type communication as a compact block (subject/from/to), NOT a chat bubble', async () => {
    const messages = [
      dto('email1', {
        communicationType: EMAIL,
        subject: 'Quarterly Review',
        from: 'alice@example.com',
        to: ['bob@example.com', 'carol@example.com'],
        sentBy: USER_1,
      }),
      dto('msg1', { communicationType: MESSAGE, body: 'hello there', sentBy: USER_2, sentByName: 'Colleague' }),
    ];
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    // The email block region (aria-label starts "Email: <subject>...").
    const block = await screen.findByRole('group', { name: /^Email: Quarterly Review/ });
    // The row wraps the header (pill/sender/time — OUTSIDE the card, item 4) + the card.
    const row = block.parentElement as HTMLElement;
    expect(within(block).getByText('Quarterly Review')).toBeInTheDocument();
    // Sender now sits in the meta header ABOVE the card (item 4), not in the card.
    expect(within(row).getByText('alice@example.com')).toBeInTheDocument();
    expect(within(block).getByText('bob@example.com, carol@example.com')).toBeInTheDocument();

    // The email is NOT rendered as a bubble (role="article"); the MESSAGE is.
    const articles = screen.getAllByRole('article');
    expect(articles).toHaveLength(1);
    expect(articles[0]).toHaveAccessibleName(/^Message from Colleague/);
    // And the email block is not itself an article.
    expect(within(block).queryByRole('article')).not.toBeInTheDocument();
  });

  it('renders exactly ONE "Email" indicator per block (no per-recipient duplication)', async () => {
    const messages = [
      dto('email1', {
        communicationType: EMAIL,
        subject: 'Kickoff',
        from: 'alice@example.com',
        to: ['bob@example.com', 'carol@example.com', 'dave@example.com'],
        sentBy: USER_1,
      }),
    ];
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    const block = await screen.findByRole('group', { name: /^Email: Kickoff/ });
    const row = block.parentElement as HTMLElement;
    // Exactly one "Email" ChannelBadge per email — in the meta header (item 4),
    // never one-per-recipient.
    expect(within(row).getAllByText('Email')).toHaveLength(1);
  });

  it('fires onOpenEmail with the email message when the open-icon is activated', async () => {
    const onOpenEmail = jest.fn();
    const messages = [
      dto('email1', { communicationType: EMAIL, subject: 'Please review', from: 'alice@example.com', sentBy: USER_1 }),
    ];
    renderView({
      authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'],
      onOpenEmail,
    });

    const block = await screen.findByRole('group', { name: /^Email: Please review/ });
    // Open-icon now lives in the meta header (item 4), a sibling of the card.
    const row = block.parentElement as HTMLElement;
    const openButton = within(row).getByRole('button', { name: 'Open email' });
    await userEvent.click(openButton);

    expect(onOpenEmail).toHaveBeenCalledTimes(1);
    expect(onOpenEmail).toHaveBeenCalledWith(expect.objectContaining({ id: 'email1', channelType: 'email' }));
  });

  it('integration: open-icon → host opens the extended SendEmailDialog with thread + regarding context', async () => {
    const messages = [
      dto('email1', { communicationType: EMAIL, subject: 'Contract draft', from: 'alice@example.com', sentBy: USER_1 }),
    ];
    const fetchMock = buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'];

    const Host: React.FC = () => {
      const [openMsg, setOpenMsg] = React.useState<TimelineMessage | null>(null);
      return (
        <FluentProvider theme={webLightTheme}>
          <ConversationView
            threadId="t1"
            currentUserSystemUserId={USER_1}
            authenticatedFetch={fetchMock}
            onOpenEmail={setOpenMsg}
          />
          <SendEmailDialog
            open={!!openMsg}
            onClose={() => setOpenMsg(null)}
            authenticatedFetch={fetchMock}
            bffBaseUrl="https://bff.example"
            threadId="t1"
            regarding={{ entityType: 'sprk_matter', id: 'MATTER-1', name: 'Smith v Jones' }}
          />
        </FluentProvider>
      );
    };

    render(<Host />);

    const block = await screen.findByRole('group', { name: /^Email: Contract draft/ });
    const row = block.parentElement as HTMLElement;
    // Dialog is closed before the open-icon is clicked.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(within(row).getByRole('button', { name: 'Open email' }));

    // The extended dialog appears, carrying the regarding record (folded into
    // the composer's association chips) — proving thread + regarding context.
    const dialog = await screen.findByRole('dialog', {}, { timeout: 4000 });
    expect(within(dialog).getByText(/Smith v Jones/)).toBeInTheDocument();
  });

  it('degrades gracefully with fallbacks when subject/from/to are missing (no crash)', async () => {
    const messages = [dto('email1', { communicationType: EMAIL, subject: null, from: null, to: [], sentBy: USER_1 })];
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    // aria-label uses the fallbacks: "Email: (no subject), from Unknown sender".
    const block = await screen.findByRole('group', { name: /^Email: \(no subject\), from Unknown sender/ });
    const row = block.parentElement as HTMLElement;
    expect(within(block).getByText('(no subject)')).toBeInTheDocument();
    // Sender fallback now renders in the meta header (item 4).
    expect(within(row).getByText('Unknown sender')).toBeInTheDocument();
    expect(within(block).getByText('—')).toBeInTheDocument();
  });

  it('shows a ~100-char body snippet + a sent/received date, and no attachment list (R3 UAT 2026-07-22 item 2)', async () => {
    const longBody = `<p>${'Lorem ipsum dolor sit amet consectetur '.repeat(6)}</p>`;
    const messages = [
      dto('email1', {
        communicationType: EMAIL,
        subject: 'Status',
        from: 'alice@example.com',
        to: ['bob@example.com'],
        direction: 100000000, // Incoming → "Received"
        sentBy: USER_2,
        body: longBody,
        bodyFormat: 100000001, // HTML — tags must be stripped from the snippet
        sentAt: '2026-07-20T10:00:00Z',
      }),
    ];
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    const block = await screen.findByRole('group', { name: /^Email: Status/ });
    const row = block.parentElement as HTMLElement;
    // HTML-stripped, ellipsised body preview (stays in the card).
    const snippet = within(block).getByText(/^Lorem ipsum dolor sit amet/);
    expect(snippet.textContent).toMatch(/…$/);
    expect(snippet.textContent).not.toContain('<'); // tags stripped
    // Sent/received time now sits in the meta header ABOVE the card (item 4).
    expect(within(row).getByText(/2026/)).toBeInTheDocument();
    // The attachment list was removed from the card (item 2) — no attachment chips.
    expect(within(block).queryByText(/attachment/i)).not.toBeInTheDocument();
  });

  it('renders the email block under dark mode via the host FluentProvider (ADR-021) without error', async () => {
    const messages = [
      dto('email1', { communicationType: EMAIL, subject: 'Dark mode', from: 'alice@example.com', sentBy: USER_1 }),
    ];
    renderView(
      { authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] },
      webDarkTheme
    );

    // No hand-authored inline colors — the block renders purely via semantic
    // tokens through the dark FluentProvider.
    const block = await screen.findByRole('group', { name: /^Email: Dark mode/ });
    expect(block).toBeInTheDocument();
    expect(within(block).getByText('Dark mode')).toBeInTheDocument();
    expect(block.getAttribute('style')).toBeFalsy();
  });
});
