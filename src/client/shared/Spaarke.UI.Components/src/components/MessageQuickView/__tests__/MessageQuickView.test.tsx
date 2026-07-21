/**
 * MessageQuickView.test.tsx (task 023, FR-05)
 *
 * Coverage for the message-preview popover:
 *   - body truncated to 200 chars (+ ellipsis)
 *   - email renders to / from / date / subject
 *   - missing fields degrade gracefully (no crash, fallback labels)
 *   - open→pin invokes `onPin(message.id)` and dismisses
 *   - keyboard Esc dismisses + returns focus to the trigger
 *   - dark mode renders via the host FluentProvider (ADR-021)
 *
 * Popover mechanics mirror `AiSummaryPopover`; the message shape reuses
 * `TimelineMessage` (NFR-06). No I/O — the message is a prop, the jump is
 * delegated via `onPin`.
 */
import * as React from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme, Button } from '@fluentui/react-components';
import { MessageQuickView } from '../MessageQuickView';
import type { IMessageQuickViewProps } from '../MessageQuickView';
import type { TimelineMessage } from '../../CommunicationTimeline/CommunicationTimeline.types';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function message(overrides: Partial<TimelineMessage> = {}): TimelineMessage {
  return {
    id: 'm1',
    channelType: 'email',
    channelTypeRaw: 100000000,
    sender: 'alice@example.com',
    senderSystemUserId: null,
    senderName: 'Alice Adams',
    direction: 'incoming',
    sentOn: '2026-07-20T10:00:00Z',
    createdOn: '2026-07-20T10:00:00Z',
    body: 'Hello there, this is the body.',
    bodyFormat: 'text',
    privilege: 0,
    attachments: [],
    ...overrides,
  };
}

function renderQuickView(overrides: Partial<IMessageQuickViewProps> = {}, theme = webLightTheme) {
  const props: IMessageQuickViewProps = {
    trigger: <Button>Preview</Button>,
    message: message(),
    ...overrides,
  };
  return render(
    <FluentProvider theme={theme}>
      <MessageQuickView {...props} />
    </FluentProvider>
  );
}

async function openPopover(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Preview' }));
}

// ---------------------------------------------------------------------------
// truncatePreview — pure helper
// ---------------------------------------------------------------------------

describe('MessageQuickView', () => {
  it('truncates the body to 200 characters with an ellipsis', async () => {
    const user = userEvent.setup();
    const longBody = 'a'.repeat(300);
    renderQuickView({ message: message({ body: longBody, channelType: 'message' }) });

    await openPopover(user);

    const expected = `${'a'.repeat(200)}…`;
    const node = await screen.findByText(expected);
    expect(node).toBeInTheDocument();
    expect(node.textContent).toHaveLength(201); // 200 chars + ellipsis
  });

  it('does not append an ellipsis when the body is <= 200 chars', async () => {
    const user = userEvent.setup();
    renderQuickView({ message: message({ body: 'short body', channelType: 'message' }) });
    await openPopover(user);
    expect(await screen.findByText('short body')).toBeInTheDocument();
  });

  it('strips HTML markup from an html body before previewing', async () => {
    const user = userEvent.setup();
    renderQuickView({
      message: message({ body: '<p>Hello <strong>bold</strong> world</p>', bodyFormat: 'html', channelType: 'message' }),
    });
    await openPopover(user);
    expect(await screen.findByText('Hello bold world')).toBeInTheDocument();
  });

  it('renders to / from / date / subject for an email', async () => {
    const user = userEvent.setup();
    renderQuickView({
      message: message({ channelType: 'email', senderName: 'Alice Adams' }),
      to: ['bob@example.com', 'carol@example.com'],
      subject: 'Quarterly review',
    });
    await openPopover(user);

    await screen.findByText('Email preview');
    expect(screen.getByText('From')).toBeInTheDocument();
    expect(screen.getByText('Alice Adams')).toBeInTheDocument();
    expect(screen.getByText('To')).toBeInTheDocument();
    expect(screen.getByText('bob@example.com, carol@example.com')).toBeInTheDocument();
    expect(screen.getByText('Subject')).toBeInTheDocument();
    expect(screen.getByText('Quarterly review')).toBeInTheDocument();
    expect(screen.getByText('Date')).toBeInTheDocument();
  });

  it('degrades gracefully when email fields are missing', async () => {
    const user = userEvent.setup();
    renderQuickView({
      message: message({
        channelType: 'email',
        sender: null,
        senderName: null,
        sentOn: null,
        body: null,
      }),
      // no `to`, no `subject`
    });
    await openPopover(user);

    expect(await screen.findByText('Unknown sender')).toBeInTheDocument();
    expect(screen.getByText('—')).toBeInTheDocument(); // empty To
    expect(screen.getByText('Unknown date')).toBeInTheDocument();
    expect(screen.getByText('(no subject)')).toBeInTheDocument();
    expect(screen.getByText('No preview available.')).toBeInTheDocument();
  });

  it('renders an error state when message is null', async () => {
    const user = userEvent.setup();
    renderQuickView({ message: null });
    await openPopover(user);
    expect(await screen.findByRole('alert')).toHaveTextContent('Message preview unavailable.');
  });

  it('does NOT render the email field block for a chat message', async () => {
    const user = userEvent.setup();
    renderQuickView({ message: message({ channelType: 'message' }) });
    await openPopover(user);
    expect(await screen.findByText('Message preview')).toBeInTheDocument();
    expect(screen.queryByText('Subject')).not.toBeInTheDocument();
    expect(screen.queryByText('From')).not.toBeInTheDocument();
  });

  it('open→pin invokes onPin with the message id and dismisses the popover', async () => {
    const user = userEvent.setup();
    const onPin = jest.fn();
    renderQuickView({ message: message({ id: 'msg-42' }), onPin });
    await openPopover(user);

    const pinButton = await screen.findByRole('button', { name: 'Go to message in conversation' });
    await user.click(pinButton);

    expect(onPin).toHaveBeenCalledTimes(1);
    expect(onPin).toHaveBeenCalledWith('msg-42');
    // popover dismisses
    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Go to message in conversation' })).not.toBeInTheDocument()
    );
  });

  it('hides the pin control when no onPin is provided', async () => {
    const user = userEvent.setup();
    renderQuickView({ message: message(), onPin: undefined });
    await openPopover(user);
    await screen.findByText('Email preview');
    expect(screen.queryByRole('button', { name: 'Go to message in conversation' })).not.toBeInTheDocument();
  });

  it('dismisses on Esc and returns focus to the trigger', async () => {
    const user = userEvent.setup();
    renderQuickView({ message: message({ channelType: 'message' }) });
    const trigger = screen.getByRole('button', { name: 'Preview' });
    await user.click(trigger);
    await screen.findByText('Message preview');

    await user.keyboard('{Escape}');

    await waitFor(() => expect(screen.queryByText('Message preview')).not.toBeInTheDocument());
    expect(trigger).toHaveFocus();
  });

  it('renders under dark mode via the host FluentProvider (ADR-021) without error', async () => {
    const user = userEvent.setup();
    renderQuickView({ message: message() }, webDarkTheme);
    await openPopover(user);
    expect(await screen.findByText('Email preview')).toBeInTheDocument();
  });
});
