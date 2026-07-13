/**
 * SprkChatMessage.composeEdit.test.tsx — DEF-12 per-message Compose-edit controls.
 *
 * Proves the Assistant confirmation message (the AI↔user interaction surface) CARRIES the
 * Accept / Reject / Try-another controls and that clicking each invokes the host handler with the
 * message's `{ledgerRef, bindingId}` — the wiring ConversationPane routes to the EXISTING
 * usePendingRedline.accept / useEditSupersession.undo / useEditSupersession.tryAnother handlers.
 *
 * Also guards the "summary-only" + "live-edit gate" contracts:
 *  - the controls render ONLY while the edit is the live pending one (`composeEditActive`);
 *  - the message body is the brief confirmation — no proposed text, no reasoning is dumped here.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SprkChatMessage } from '../SprkChatMessage';
import type { IChatMessage } from '../types';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const CONFIRMATION =
  'I revised the selected text — review the tracked change in the document, then accept, reject, or try another.';

const composeEditMessage: IChatMessage = {
  role: 'Assistant',
  content: CONFIRMATION,
  timestamp: '2026-07-12T00:00:00.000Z',
  metadata: { responseType: 'markdown', composeEdit: { ledgerRef: 'binding-1@t1', bindingId: 'binding-1' } },
};

describe('SprkChatMessage — DEF-12 compose-edit controls', () => {
  it('renders Accept/Reject/Try-another on the live confirmation message and routes clicks to the handlers with the ledgerRef', () => {
    const onAccept = jest.fn();
    const onReject = jest.fn();
    const onTryAnother = jest.fn();

    renderWithProvider(
      <SprkChatMessage
        message={composeEditMessage}
        composeEditActive
        onComposeEditAccept={onAccept}
        onComposeEditReject={onReject}
        onComposeEditTryAnother={onTryAnother}
      />
    );

    // The controls are attached to THIS Assistant message.
    fireEvent.click(screen.getByTestId('compose-edit-accept'));
    expect(onAccept).toHaveBeenCalledWith('binding-1@t1', 'binding-1');

    fireEvent.click(screen.getByTestId('compose-edit-reject'));
    expect(onReject).toHaveBeenCalledWith('binding-1@t1', 'binding-1');

    fireEvent.click(screen.getByTestId('compose-edit-try-another'));
    expect(onTryAnother).toHaveBeenCalledWith('binding-1@t1', 'binding-1');

    // Summary-only: the brief confirmation is shown, and no proposed text / reasoning is dumped.
    expect(screen.getByText(CONFIRMATION)).toBeInTheDocument();
  });

  it('FIX #3: renders "Keep redline" and routes its click to onComposeEditKeep with the ledgerRef', () => {
    const onKeep = jest.fn();
    renderWithProvider(
      <SprkChatMessage
        message={composeEditMessage}
        composeEditActive
        onComposeEditAccept={jest.fn()}
        onComposeEditKeep={onKeep}
      />
    );

    fireEvent.click(screen.getByTestId('compose-edit-keep'));
    expect(onKeep).toHaveBeenCalledWith('binding-1@t1', 'binding-1');
  });

  it('FIX #3: the "Keep redline" control is disabled when onComposeEditKeep is omitted', () => {
    renderWithProvider(
      <SprkChatMessage message={composeEditMessage} composeEditActive onComposeEditAccept={jest.fn()} />
    );
    expect(screen.getByTestId('compose-edit-keep')).toBeDisabled();
  });

  it('suppresses the controls when the edit is NOT the live one (composeEditActive=false)', () => {
    renderWithProvider(
      <SprkChatMessage message={composeEditMessage} composeEditActive={false} onComposeEditAccept={jest.fn()} />
    );
    expect(screen.queryByTestId('compose-edit-accept')).toBeNull();
    expect(screen.queryByTestId('compose-edit-reject')).toBeNull();
    expect(screen.queryByTestId('compose-edit-try-another')).toBeNull();
  });

  it('renders no compose-edit controls on an ordinary assistant message (no composeEdit metadata)', () => {
    const plain: IChatMessage = {
      role: 'Assistant',
      content: 'Here is a summary.',
      timestamp: '2026-07-12T00:00:00.000Z',
      metadata: { responseType: 'markdown' },
    };
    renderWithProvider(<SprkChatMessage message={plain} composeEditActive onComposeEditAccept={jest.fn()} />);
    expect(screen.queryByTestId('compose-edit-accept')).toBeNull();
  });
});
