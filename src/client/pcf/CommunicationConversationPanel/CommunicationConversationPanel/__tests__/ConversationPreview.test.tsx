import * as React from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import type { TimelineMessage } from '@spaarke/ui-components';
import { ConversationPreview } from '../ConversationPreview';
import type { PreviewModel } from '../previewModel';

function tm(id: string, sender: string, body: string): TimelineMessage {
  return {
    id,
    channelType: 'email',
    channelTypeRaw: 100000000,
    sender,
    senderName: sender,
    subject: `subject-${id}`,
    to: ['to@example.com'],
    sentOn: '2026-01-01T10:00:00Z',
    createdOn: '2026-01-01T10:00:00Z',
    body,
    bodyFormat: 'text',
    privilege: 0,
    attachments: [],
  };
}

function makeModel(): PreviewModel {
  return {
    threads: [
      {
        threadId: 'default-thread',
        name: 'Default Thread',
        threadMessageCount: 2,
        messages: [tm('m1', 'Alice', 'Hello from Alice'), tm('m2', 'Bob', 'Reply from Bob')],
        hasMore: false,
      },
      {
        threadId: 'other-thread',
        name: 'Other Thread',
        threadMessageCount: 1,
        messages: [tm('m3', 'Carol', 'Message in other thread')],
        hasMore: false,
      },
    ],
    shownThreadCount: 2,
    totalThreadCount: 5,
    totalMessageCount: 9,
    defaultExpandedThreadId: 'default-thread',
  };
}

function renderPreview(overrides?: Partial<React.ComponentProps<typeof ConversationPreview>>) {
  const onOpen = jest.fn();
  const utils = render(
    <FluentProvider theme={webLightTheme}>
      <ConversationPreview
        model={makeModel()}
        version="1.0.0"
        showVersionFooter={true}
        onOpen={onOpen}
        {...overrides}
      />
    </FluentProvider>
  );
  return { onOpen, ...utils };
}

describe('ConversationPreview — FR-13 preview UI', () => {
  it('renders the footer "N of M" counter from the model', () => {
    renderPreview();
    expect(screen.getByText('2 of 5')).toBeInTheDocument();
  });

  it('renders the total communication count alongside the N-of-M counter', () => {
    renderPreview();
    expect(screen.getByText(/9 messages/)).toBeInTheDocument();
  });

  it('auto-expands the default thread (its messages are visible)', () => {
    renderPreview();
    expect(screen.getByText(/Hello from Alice/)).toBeInTheDocument();
    expect(screen.getByText(/Reply from Bob/)).toBeInTheDocument();
  });

  it('collapses non-default threads until toggled', () => {
    renderPreview();
    // other-thread starts collapsed → its message is not rendered
    expect(screen.queryByText(/Message in other thread/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Other Thread/i }));
    expect(screen.getByText(/Message in other thread/)).toBeInTheDocument();
  });

  it('exposes the shared MessageQuickView popover per message', async () => {
    renderPreview();
    const trigger = screen.getByRole('button', { name: /Preview message from Alice/i });
    fireEvent.click(trigger);
    // MessageQuickView renders an "Email preview" header for email-channel messages
    const surface = await screen.findByLabelText('Message preview');
    expect(within(surface).getByText('Email preview')).toBeInTheDocument();
  });

  it('raises onOpen when the Open affordance is activated', () => {
    const { onOpen } = renderPreview();
    fireEvent.click(screen.getByRole('button', { name: /Open conversations in full view/i }));
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it('renders the version footer when enabled', () => {
    renderPreview();
    expect(screen.getByText('v1.0.0')).toBeInTheDocument();
  });
});
