/**
 * SprkChatMessage.insertToCompose.test.tsx — spaarkeai-compose-r2 R4 ("Insert into document").
 *
 * Proves the per-message "Insert into document" affordance: it renders on completed ASSISTANT
 * messages when the host provides `onInsertToCompose`, and clicking it hands the message's text to
 * the host (which routes it through the cross-pane bridge to the Compose editor's redline engine).
 * Guards the render gates: not on user messages, not while streaming, not without the callback,
 * and (P1-3, UAT 2026-07-18) only when the content is a substantial DRAFT worth inserting — short
 * conversational replies no longer offer it.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SprkChatMessage } from '../SprkChatMessage';
import type { IChatMessage } from '../types';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

// P1-3: an insertable DRAFT — long enough (>=240 chars) to clear the "rare" gate.
const DRAFT_BODY =
  'Consider adding an indemnification clause here: the Supplier shall indemnify and hold harmless the ' +
  'Customer against any and all claims, losses, liabilities, and expenses (including reasonable ' +
  "attorneys' fees) arising out of the Supplier's breach of this Agreement or its negligent acts or omissions.";

const assistantMessage: IChatMessage = {
  role: 'Assistant',
  content: DRAFT_BODY,
  timestamp: '2026-07-13T00:00:00.000Z',
};

describe('SprkChatMessage — R4 "Insert into document"', () => {
  it('renders the button on a completed assistant message and passes the message text to onInsertToCompose', () => {
    const onInsertToCompose = jest.fn();
    renderWithProvider(<SprkChatMessage message={assistantMessage} onInsertToCompose={onInsertToCompose} />);

    const btn = screen.getByTestId('insert-into-document');
    expect(btn).toHaveTextContent('Insert into document');

    fireEvent.click(btn);
    expect(onInsertToCompose).toHaveBeenCalledWith(DRAFT_BODY);
  });

  it('does NOT render on a short conversational reply (P1-3: only substantial drafts)', () => {
    const onInsertToCompose = jest.fn();
    renderWithProvider(
      <SprkChatMessage
        message={{ ...assistantMessage, content: 'Yes, I can help with that.' }}
        onInsertToCompose={onInsertToCompose}
      />
    );
    expect(screen.queryByTestId('insert-into-document')).not.toBeInTheDocument();
  });

  it('does NOT render when onInsertToCompose is absent', () => {
    renderWithProvider(<SprkChatMessage message={assistantMessage} />);
    expect(screen.queryByTestId('insert-into-document')).not.toBeInTheDocument();
  });

  it('does NOT render on user messages', () => {
    const onInsertToCompose = jest.fn();
    renderWithProvider(
      <SprkChatMessage message={{ ...assistantMessage, role: 'User' }} onInsertToCompose={onInsertToCompose} />
    );
    expect(screen.queryByTestId('insert-into-document')).not.toBeInTheDocument();
  });

  it('does NOT render while the assistant message is still streaming', () => {
    const onInsertToCompose = jest.fn();
    renderWithProvider(
      <SprkChatMessage message={assistantMessage} isStreaming onInsertToCompose={onInsertToCompose} />
    );
    expect(screen.queryByTestId('insert-into-document')).not.toBeInTheDocument();
  });

  it('renders on a structured markdown assistant message (routes the structured text)', () => {
    const onInsertToCompose = jest.fn();
    const structured: IChatMessage = {
      role: 'Assistant',
      content: '',
      timestamp: '2026-07-13T00:00:00.000Z',
      metadata: { responseType: 'markdown', data: { text: DRAFT_BODY } },
    };
    renderWithProvider(<SprkChatMessage message={structured} onInsertToCompose={onInsertToCompose} />);

    fireEvent.click(screen.getByTestId('insert-into-document'));
    expect(onInsertToCompose).toHaveBeenCalledWith(DRAFT_BODY);
  });
});
