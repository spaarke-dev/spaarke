/**
 * Wiring tests for CommunicationAttachmentsApp:
 *   - auth gate resolves, attachments load (inline images filtered out),
 *   - clicking a file row opens the shared RichFilePreviewDialog (mocked),
 *   - clicking an .eml row opens the SAME in-modal preview (UAT R4 B12-4).
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

  it('opens the same in-modal preview for an .eml attachment (UAT R4 B12-4 — no external-open route)', async () => {
    renderApp(makeContext(rows));
    fireEvent.click(await screen.findByText('Thread.eml'));

    // .eml now opens the shared RichFilePreviewDialog for its document, exactly
    // like any other attachment — no forced open-links/external-open branch.
    const dialog = await screen.findByTestId('rich-file-preview-dialog');
    expect(dialog).toBeInTheDocument();
    expect(dialog.getAttribute('data-document-id')).toBe('doc-3');
    // Clicking the row did NOT eagerly fetch open-links (that only happens from
    // inside the modal's open affordance, not on row activation).
    const openLinksFetched = (authenticatedFetch as jest.Mock).mock.calls.some((c: unknown[]) =>
      String(c[0]).includes('/api/documents/doc-3/open-links')
    );
    expect(openLinksFetched).toBe(false);
  });

  it('mounts the preview dialog WITH prev/next nav across ALL document-bearing attachments incl. .eml (A11-1 + B12-4)', async () => {
    // A11-1 is fixed in the shared lib (dialog passes showTitle={false} in shell
    // mode → single title), so we KEEP the nav trio and browse the previewable
    // set. Per B12-4 the `.eml` row now participates in the nav sequence too;
    // only inline images / rows without a documentId are excluded.
    const files = [
      {
        sprk_communicationattachmentid: '1',
        sprk_name: 'Report.pdf',
        sprk_attachmenttype: AttachmentType.File,
        [DOC]: 'doc-1',
      },
      {
        sprk_communicationattachmentid: '2',
        sprk_name: 'Memo.pdf',
        sprk_attachmenttype: AttachmentType.File,
        [DOC]: 'doc-4',
      },
      {
        sprk_communicationattachmentid: '3',
        sprk_name: 'Thread.eml',
        sprk_attachmenttype: AttachmentType.File,
        [DOC]: 'doc-3',
      },
    ];
    renderApp(makeContext(files));
    fireEvent.click(await screen.findByText('Report.pdf'));
    const dialog = await screen.findByTestId('rich-file-preview-dialog');
    expect(dialog.getAttribute('data-document-id')).toBe('doc-1');
    // Nav trio passed: total = 3 previewable attachments (the .eml is INCLUDED), index 0.
    expect(dialog.getAttribute('data-nav-total')).toBe('3');
    expect(dialog.getAttribute('data-nav-index')).toBe('0');
    // Next → moves to the second previewable attachment.
    fireEvent.click(screen.getByTestId('preview-next'));
    await waitFor(() =>
      expect(screen.getByTestId('rich-file-preview-dialog').getAttribute('data-document-id')).toBe('doc-4')
    );
  });

  it('renders the default section title "ATTACHMENTS" when no sectionTitle property is set', async () => {
    renderApp(makeContext(rows));
    expect(await screen.findByText('ATTACHMENTS')).toBeInTheDocument();
  });

  it('renders the configured sectionTitle property value (uppercased via CSS, raw text preserved)', async () => {
    const ctx = makeContext(rows);
    ctx.parameters.sectionTitle = { raw: 'Files' };
    renderApp(ctx);
    // textTransform:uppercase is a display transform — the DOM text stays 'Files'.
    expect(await screen.findByText('Files')).toBeInTheDocument();
  });

  it('shows an empty state when the communication has no file attachments', async () => {
    renderApp(makeContext([]));
    expect(await screen.findByText('No attachments on this communication.')).toBeInTheDocument();
  });
});
