/**
 * ConversationView.forward.test.tsx (task 022, FR-08)
 *
 * A Forward affordance on EVERY message (both the chat `<MessageBubble/>` and
 * the `<EmailInFlowBlock/>`) hands the row back to the host via
 * `onForwardMessage`. The host opens the extended `<SendEmailDialog/>` in
 * FORWARD mode, seeding an `ISourceCommunicationRecord` from the message; the
 * composer derives the `Fwd:` subject + quoted body via its EXISTING
 * `deriveForwardState` (ADR-045 — no new forward send path). The draft lives
 * ONLY in the modal — ConversationView persists nothing on forward (FR-08 /
 * ADR-012).
 *
 * Per ADR-038/ADR-028: a fake `authenticatedFetch` is the I/O seam (no
 * `Mock<HttpMessageHandler>`, no `@spaarke/auth` import) — same convention as
 * the sibling `ConversationView.emailInFlow.test.tsx`. The integration test
 * drives the REAL `<SendEmailDialog/>` + `<EmailComposer/>`, exactly like
 * `EmailComposer/__tests__/SendEmailDialog.characterize.test.tsx`.
 */
import * as React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { ConversationView } from '../ConversationView';
import { SendEmailDialog } from '../../EmailComposer/wrappers/SendEmailDialog';
import type { ConversationViewProps } from '../ConversationView.types';
import type { TimelineMessage } from '../../CommunicationTimeline/CommunicationTimeline.types';
import type { ISourceCommunicationRecord } from '../../EmailComposer/EmailComposer.types';
import type { IThreadMessageDto } from '../../../services/communicationTimelineApi';

const USER_1 = '11111111-1111-1111-1111-111111111111';
const USER_2 = '22222222-2222-2222-2222-222222222222';

const EMAIL = 100000000; // sprk_communicationtype: Email
const MESSAGE = 100000004; // sprk_communicationtype: Message
const BODY_HTML = 100000001;
const BODY_PLAINTEXT = 100000000;

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function dto(id: string, overrides: Partial<IThreadMessageDto> = {}): IThreadMessageDto {
  return {
    messageId: id,
    body: `body-${id}`,
    bodyFormat: BODY_HTML,
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

/**
 * Host-side mapping of the enriched message → composer source record (FR-08).
 * Maps the message's `attachments` (`TimelineAttachment[]`) onto the composer's
 * `IAttachmentItem[]` so `deriveForwardState`'s "attachments default to
 * selected:true" branch is exercised end-to-end through the wired dialog.
 */
function toSourceRecord(m: TimelineMessage): ISourceCommunicationRecord {
  return {
    communicationId: m.id,
    from: m.sender ?? '',
    to: m.to ?? [],
    subject: m.subject ?? '',
    body: m.body ?? '',
    bodyFormat: m.bodyFormat === 'html' ? 'HTML' : 'PlainText',
    attachments: (m.attachments ?? []).map(a => ({
      id: a.id,
      source: 'related' as const,
      fileName: a.fileName ?? 'attachment',
      sizeBytes: 2048,
      documentId: a.documentId,
    })),
  };
}

// ---------------------------------------------------------------------------
// 1. Forward affordance per message + fires the callback
// ---------------------------------------------------------------------------

describe('ConversationView forward affordance (FR-08)', () => {
  it('renders a Forward affordance on EVERY message (email block AND chat bubble) when a handler is wired', async () => {
    const messages = [
      dto('email1', { communicationType: EMAIL, subject: 'Quarterly Review', from: 'alice@example.com', sentBy: USER_1 }),
      dto('msg1', { communicationType: MESSAGE, body: 'hello there', sentBy: USER_2, sentByName: 'Colleague' }),
    ];
    renderView({
      authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'],
      onForwardMessage: jest.fn(),
    });

    await screen.findByRole('group', { name: /^Email: Quarterly Review/ });
    // One Forward affordance per message — proving it wraps BOTH the email block
    // and the chat bubble (rendered in the shared anchor, not the subcomponent).
    // The accessible name is contextual per message (subject/sender), so match
    // on the "Forward …" prefix rather than an exact shared string (NFR-05).
    const forwardButtons = screen.getAllByRole('button', { name: /^Forward / });
    expect(forwardButtons).toHaveLength(2);
    // Contextualized names are distinct across messages.
    expect(screen.getByRole('button', { name: 'Forward Quarterly Review' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Forward Colleague' })).toBeInTheDocument();
  });

  it('does NOT render the Forward affordance when onForwardMessage is not wired (no focusable no-op)', async () => {
    const messages = [
      dto('email1', { communicationType: EMAIL, subject: 'Unwired', from: 'alice@example.com', sentBy: USER_1 }),
      dto('msg1', { communicationType: MESSAGE, body: 'hello there', sentBy: USER_2, sentByName: 'Colleague' }),
    ];
    // No onForwardMessage → the affordance must be absent (a keyboard user should
    // never reach a Forward button that does nothing). onOpenEmail is unaffected.
    renderView({ authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'] });

    await screen.findByRole('group', { name: /^Email: Unwired/ });
    expect(screen.queryAllByRole('button', { name: /^Forward/ })).toHaveLength(0);
  });

  it('fires onForwardMessage with the correct message when the Forward affordance is clicked', async () => {
    const onForwardMessage = jest.fn();
    const messages = [
      dto('email1', { communicationType: EMAIL, subject: 'Contract draft', from: 'alice@example.com', sentBy: USER_1 }),
      dto('msg1', { communicationType: MESSAGE, body: 'ping', sentBy: USER_2, sentByName: 'Colleague' }),
    ];
    const { container } = renderView({
      authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'],
      onForwardMessage,
    });

    await screen.findByRole('group', { name: /^Email: Contract draft/ });

    // Scope to the chat-message anchor so we assert the EXACT message forwarded.
    const msgAnchor = container.querySelector('[data-message-id="msg1"]') as HTMLElement;
    expect(msgAnchor).toBeTruthy();
    await userEvent.click(within(msgAnchor).getByRole('button', { name: /^Forward/ }));

    expect(onForwardMessage).toHaveBeenCalledTimes(1);
    expect(onForwardMessage).toHaveBeenCalledWith(expect.objectContaining({ id: 'msg1', channelType: 'message' }));
  });

  it('Forward affordance is keyboard-operable and ARIA-labeled (NFR-05)', async () => {
    const onForwardMessage = jest.fn();
    const messages = [
      dto('email1', { communicationType: EMAIL, subject: 'Keyboard', from: 'alice@example.com', sentBy: USER_1 }),
    ];
    const { container } = renderView({
      authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'],
      onForwardMessage,
    });

    await screen.findByRole('group', { name: /^Email: Keyboard/ });
    const anchor = container.querySelector('[data-message-id="email1"]') as HTMLElement;
    const forwardButton = within(anchor).getByRole('button', { name: /^Forward/ });

    // ARIA: resolvable, contextual accessible name (role + name folds in subject).
    expect(forwardButton).toHaveAccessibleName('Forward Keyboard');

    // Keyboard: focus the affordance and activate it with Enter (native button
    // semantics) — no pointer needed.
    forwardButton.focus();
    expect(forwardButton).toHaveFocus();
    await userEvent.keyboard('{Enter}');

    expect(onForwardMessage).toHaveBeenCalledTimes(1);
    expect(onForwardMessage).toHaveBeenCalledWith(expect.objectContaining({ id: 'email1' }));
  });
});

// ---------------------------------------------------------------------------
// 2. Integration — Forward opens the extended SendEmailDialog PREFILLED
// ---------------------------------------------------------------------------

describe('ConversationView forward → SendEmailDialog forward mode (FR-08, ADR-045)', () => {
  const forwardHostMessages = [
    dto('email1', {
      communicationType: EMAIL,
      subject: 'Contract draft',
      from: 'alice@example.com',
      to: ['bob@example.com'],
      body: 'Original body text',
      bodyFormat: BODY_PLAINTEXT, // PlainText → Textarea body (no Lexical timing)
      sentBy: USER_1,
      // A source attachment so the forward prefill's subject+body+ATTACHMENTS
      // claim is exercised end-to-end (deriveForwardState defaults it selected).
      attachments: [
        { communicationAttachmentId: 'att1', documentId: 'doc-1', fileName: 'contract.pdf', attachmentType: null },
      ],
    }),
  ];

  const ForwardHost: React.FC = () => {
    const [fwd, setFwd] = React.useState<TimelineMessage | null>(null);
    const fetchMock = React.useMemo(
      () => buildFetch(forwardHostMessages) as unknown as ConversationViewProps['authenticatedFetch'],
      []
    );
    return (
      <FluentProvider theme={webLightTheme}>
        <ConversationView
          threadId="t1"
          currentUserSystemUserId={USER_1}
          authenticatedFetch={fetchMock}
          onForwardMessage={setFwd}
        />
        <SendEmailDialog
          open={!!fwd}
          onClose={() => setFwd(null)}
          authenticatedFetch={fetchMock}
          bffBaseUrl="https://bff.example"
          mode="forward"
          sourceRecord={fwd ? toSourceRecord(fwd) : undefined}
          communicationId={fwd?.id}
          threadId="t1"
          regarding={{ entityType: 'sprk_matter', id: 'MATTER-1', name: 'Smith v Jones' }}
        />
      </FluentProvider>
    );
  };

  it('opens the dialog in FORWARD mode, prefilled from the source (Fwd: subject + quoted body) with thread + regarding context', async () => {
    const { container } = render(<ForwardHost />);

    await screen.findByRole('group', { name: /^Email: Contract draft/ });
    // Dialog is closed before the Forward affordance is clicked.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    // The Forward affordance lives in the shared anchor wrapper (a sibling of
    // the email block), so scope to the message anchor, not the block group.
    const anchor = container.querySelector('[data-message-id="email1"]') as HTMLElement;
    await userEvent.click(within(anchor).getByRole('button', { name: /^Forward/ }));

    const dialog = await screen.findByRole('dialog', {}, { timeout: 4000 });

    // Forward mode header (EmailComposer renders "Forward" for mode === 'forward').
    expect(within(dialog).getByRole('heading', { name: 'Forward' })).toBeInTheDocument();

    // Subject prefilled via deriveForwardState → dedupSubjectPrefix(..,'Fwd:').
    expect(within(dialog).getByRole('textbox', { name: 'Subject' })).toHaveValue('Fwd: Contract draft');

    // Quoted body prefilled — the forwarded-message header + the source body.
    const body = within(dialog).getByRole('textbox', { name: 'Message body' }) as HTMLTextAreaElement;
    expect(body.value).toContain('Forwarded message');
    expect(body.value).toContain('Original body text');

    // Attachment prefilled — the source attachment rides the forward (FR-08),
    // defaulted to included (deriveForwardState → selected:true). It renders in
    // the composer's attachment list, and its per-item "Attach" toggle is on.
    expect(within(dialog).getByText('contract.pdf')).toBeInTheDocument();
    expect(within(dialog).getByRole('checkbox', { name: 'Attach contract.pdf as a file' })).toBeChecked();

    // NOTE: the dialog's `regarding` prop is accepted additively (task 020) and
    // passed here, but in FORWARD mode the composer's `deriveForwardState`
    // derives `associations` FROM the `sourceRecord` (which carries none here) —
    // existing ADR-045 composer behavior, out of task-022 scope (documented on
    // the `sourceRecord` / `onForwardMessage` JSDoc) — so we assert the forward
    // PREFILL (the FR-08 contract), not the regarding chip.
  });

  it('persists NO conversation-side draft: the Forward affordance only fires the callback — ConversationView stores nothing (FR-08)', async () => {
    const onForwardMessage = jest.fn();
    const messages = [
      dto('email1', {
        communicationType: EMAIL,
        subject: 'Contract draft',
        from: 'alice@example.com',
        body: 'Original body text',
        bodyFormat: BODY_PLAINTEXT,
        sentBy: USER_1,
      }),
    ];
    const { container } = renderView({
      authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'],
      onForwardMessage,
    });

    await screen.findByRole('group', { name: /^Email: Contract draft/ });
    const anchor = container.querySelector('[data-message-id="email1"]') as HTMLElement;

    // Baseline: the in-conversation compose input is empty.
    expect(screen.getByRole('textbox', { name: 'Message' })).toHaveValue('');

    // Forwarding twice fires the callback each time (host owns the draft) …
    await userEvent.click(within(anchor).getByRole('button', { name: /^Forward/ }));
    await userEvent.click(within(anchor).getByRole('button', { name: /^Forward/ }));
    expect(onForwardMessage).toHaveBeenCalledTimes(2);

    // … but mutates NO ConversationView state: the compose input stays empty and
    // the message + its Forward affordance render unchanged. ConversationView
    // persists no forward draft — the modal owns it (FR-08 / ADR-012).
    expect(screen.getByRole('textbox', { name: 'Message' })).toHaveValue('');
    expect(screen.getByRole('group', { name: /^Email: Contract draft/ })).toBeInTheDocument();
    expect(within(anchor).getByRole('button', { name: /^Forward/ })).toBeInTheDocument();
  });
});

// ---------------------------------------------------------------------------
// 3. Dark mode (ADR-021) — semantic tokens only, no hand-authored inline colors
// ---------------------------------------------------------------------------

describe('ConversationView forward affordance — Dark Mode Compliance (ADR-021)', () => {
  it('renders the Forward affordance under a dark-theme FluentProvider without hand-authored inline colors', async () => {
    const messages = [
      dto('email1', { communicationType: EMAIL, subject: 'Dark mode', from: 'alice@example.com', sentBy: USER_1 }),
    ];
    const { container } = renderView(
      {
        authenticatedFetch: buildFetch(messages) as unknown as ConversationViewProps['authenticatedFetch'],
        onForwardMessage: jest.fn(),
      },
      webDarkTheme
    );

    await screen.findByRole('group', { name: /^Email: Dark mode/ });
    const anchor = container.querySelector('[data-message-id="email1"]') as HTMLElement;
    const forwardButton = within(anchor).getByRole('button', { name: /^Forward/ });
    expect(forwardButton).toBeInTheDocument();

    // Fluent tokens apply via Griffel classes, not inline styles — pin the
    // absence of hardcoded inline color/background-color on the action row.
    const actionsRow = anchor.querySelector('[data-message-actions]') as HTMLElement;
    expect(actionsRow.style.color).toBe('');
    expect(actionsRow.style.backgroundColor).toBe('');
    expect(forwardButton.style.color).toBe('');
    expect(forwardButton.style.backgroundColor).toBe('');
  });
});
