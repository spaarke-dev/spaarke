/**
 * MessageAttachments.test.tsx — task 042 / FR-20.
 *
 * The open/preview/download affordance for a message's attachments, routed to
 * the existing SPE document-viewer path via the host `onOpenAttachment` seam.
 *
 * Coverage:
 *   - an attachment that resolved to a governed Document (has `documentId`)
 *     renders a keyboard-operable "Open" button that hands the attachment +
 *     message back to the host (which mounts RichFilePreviewDialog — reused,
 *     not a new previewer).
 *   - NEGATIVE / NFR-01 access-filtering: an attachment WITHOUT a resolved
 *     `documentId` is NOT openable — no button, no retrieval path — so a caller
 *     can never trigger a retrieval for something the access-filtered read did
 *     not resolve (no over-disclosure). Same when no handler is wired.
 */
import * as React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { MessageAttachments } from '../subcomponents/MessageAttachments';
import type { TimelineAttachment, TimelineMessage } from '../CommunicationTimeline.types';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

function message(attachments: TimelineAttachment[]): TimelineMessage {
  return {
    id: 'comm-1',
    channelType: 'email',
    channelTypeRaw: 100000000,
    bodyFormat: 'text',
    privilege: 100000000,
    attachments,
  };
}

describe('MessageAttachments — open affordance (SPE path reuse)', () => {
  it('renders a keyboard-operable Open button for a document-backed attachment and hands it back to the host', () => {
    const onOpenAttachment = jest.fn();
    const att: TimelineAttachment = { id: 'a1', documentId: 'doc-1', fileName: 'contract.pdf' };
    const msg = message([att]);
    renderWithProvider(<MessageAttachments attachments={[att]} message={msg} onOpenAttachment={onOpenAttachment} />);

    const btn = screen.getByRole('button', { name: 'Open attachment contract.pdf' });
    expect(btn).toBeInTheDocument();
    fireEvent.click(btn);
    expect(onOpenAttachment).toHaveBeenCalledWith(att, msg);
  });
});

describe('MessageAttachments — access-filtering / no over-disclosure (NFR-01)', () => {
  it('does NOT render an Open button for an attachment with no resolved documentId (unretrievable)', () => {
    const onOpenAttachment = jest.fn();
    const att: TimelineAttachment = { id: 'a2', fileName: 'unresolved.bin' }; // no documentId
    renderWithProvider(
      <MessageAttachments attachments={[att]} message={message([att])} onOpenAttachment={onOpenAttachment} />
    );

    expect(screen.queryByRole('button', { name: /Open attachment/ })).toBeNull();
    // The filename still shows as a passive chip — visible, but not openable.
    expect(screen.getByText('unresolved.bin')).toBeInTheDocument();
    expect(onOpenAttachment).not.toHaveBeenCalled();
  });

  it('renders passive chips (no buttons) when no open handler is wired', () => {
    const att: TimelineAttachment = { id: 'a3', documentId: 'doc-3', fileName: 'brief.pdf' };
    renderWithProvider(<MessageAttachments attachments={[att]} message={message([att])} />);
    expect(screen.queryByRole('button')).toBeNull();
    expect(screen.getByText('brief.pdf')).toBeInTheDocument();
  });
});
