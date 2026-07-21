/**
 * Wiring tests for CommunicationAttachmentsApp:
 *   - auth gate resolves, attachments load (inline images filtered out),
 *   - clicking a file row opens the shared RichFilePreviewDialog (mocked),
 *   - clicking an .eml row routes to open/download (open-links fetch), NOT the modal.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CommunicationAttachmentsApp } from '../CommunicationAttachments/CommunicationAttachmentsApp';
import { authenticatedFetch } from '@spaarke/auth';
import { AttachmentType } from '../CommunicationAttachments/types';

const DOC = '_sprk_document_value';

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeContext(entities: any[]): any {
  return {
    parameters: {
      // Manifest props supplied → auth bootstrap skips the Dataverse env-var lookups.
      apiBaseUrl: { raw: 'https://bff.example' },
      tenantId: { raw: 'tenant-1' },
      clientAppId: { raw: 'client-1' },
      bffAppId: { raw: 'bff-1' },
      showVersionFooter: { raw: true },
    },
    mode: { isControlDisabled: false },
    page: { entityId: '11111111-1111-1111-1111-111111111111' },
    webAPI: {
      retrieveMultipleRecords: jest.fn(async () => ({ entities })),
    },
  };
}

const rows = [
  {
    sprk_communicationattachmentid: '1',
    sprk_name: 'Report.pdf',
    sprk_attachmenttype: AttachmentType.File,
    [DOC]: 'doc-1',
  },
  {
    sprk_communicationattachmentid: '2',
    sprk_name: 'Inline.png',
    sprk_attachmenttype: AttachmentType.InlineImage,
    [DOC]: 'doc-2',
  },
  {
    sprk_communicationattachmentid: '3',
    sprk_name: 'Thread.eml',
    sprk_attachmenttype: AttachmentType.File,
    [DOC]: 'doc-3',
  },
];

const renderApp = (context: unknown) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <CommunicationAttachmentsApp context={context as never} version="1.0.0" />
    </FluentProvider>
  );

beforeEach(() => {
  (authenticatedFetch as jest.Mock).mockClear();
});

describe('CommunicationAttachmentsApp', () => {
  it('loads and lists file attachments (inline images filtered out) after auth resolves', async () => {
    renderApp(makeContext(rows));
    expect(await screen.findByText('Report.pdf')).toBeInTheDocument();
    expect(screen.getByText('Thread.eml')).toBeInTheDocument();
    expect(screen.queryByText('Inline.png')).not.toBeInTheDocument();
  });

  it('opens the shared RichFilePreviewDialog for the clicked file attachment', async () => {
    renderApp(makeContext(rows));
    fireEvent.click(await screen.findByText('Report.pdf'));
    const dialog = await screen.findByTestId('rich-file-preview-dialog');
    expect(dialog).toBeInTheDocument();
    expect(dialog.getAttribute('data-document-id')).toBe('doc-1');
    expect(screen.getByTestId('preview-doc-name').textContent).toBe('Report.pdf');
  });

  it('routes an .eml attachment to open/download (open-links fetch), not the inline modal', async () => {
    renderApp(makeContext(rows));
    fireEvent.click(await screen.findByText('Thread.eml'));

    await waitFor(() => {
      const called = (authenticatedFetch as jest.Mock).mock.calls.some((c: unknown[]) =>
        String(c[0]).includes('/api/documents/doc-3/open-links')
      );
      expect(called).toBe(true);
    });
    // No inline preview modal for an email message.
    expect(screen.queryByTestId('rich-file-preview-dialog')).not.toBeInTheDocument();
  });

  it('wires prev/next nav across the previewable attachments and re-targets the document on navigate', async () => {
    // Two previewable file rows + one inline image (filtered upstream) + one
    // .eml (routes to download, excluded from the modal nav sequence).
    const navRows = [
      { sprk_communicationattachmentid: 'a', sprk_name: 'A.pdf', sprk_attachmenttype: AttachmentType.File, [DOC]: 'doc-a' },
      { sprk_communicationattachmentid: 'b', sprk_name: 'B.docx', sprk_attachmenttype: AttachmentType.File, [DOC]: 'doc-b' },
      { sprk_communicationattachmentid: 'i', sprk_name: 'Pic.png', sprk_attachmenttype: AttachmentType.InlineImage, [DOC]: 'doc-i' },
      { sprk_communicationattachmentid: 'e', sprk_name: 'Msg.eml', sprk_attachmenttype: AttachmentType.File, [DOC]: 'doc-e' },
    ];
    renderApp(makeContext(navRows));

    // Open the first previewable attachment.
    fireEvent.click(await screen.findByText('A.pdf'));
    const dialog = await screen.findByTestId('rich-file-preview-dialog');
    // Nav set = 2 previewable docs (inline image + .eml excluded); opened at index 0.
    expect(dialog.getAttribute('data-nav-total')).toBe('2');
    expect(dialog.getAttribute('data-nav-index')).toBe('0');
    expect(dialog.getAttribute('data-document-id')).toBe('doc-a');

    // Next → moves to doc-b and re-targets the dialog (drives preview-url re-resolution).
    fireEvent.click(screen.getByTestId('preview-next'));
    await waitFor(() => {
      expect(screen.getByTestId('rich-file-preview-dialog').getAttribute('data-document-id')).toBe('doc-b');
    });
    expect(screen.getByTestId('preview-doc-name').textContent).toBe('B.docx');
    expect(screen.getByTestId('rich-file-preview-dialog').getAttribute('data-nav-index')).toBe('1');

    // Prev → back to doc-a.
    fireEvent.click(screen.getByTestId('preview-prev'));
    await waitFor(() => {
      expect(screen.getByTestId('rich-file-preview-dialog').getAttribute('data-document-id')).toBe('doc-a');
    });
    expect(screen.getByTestId('rich-file-preview-dialog').getAttribute('data-nav-index')).toBe('0');
  });

  it('shows an empty state when the communication has no file attachments', async () => {
    renderApp(makeContext([]));
    expect(await screen.findByText('No attachments on this communication.')).toBeInTheDocument();
  });
});
