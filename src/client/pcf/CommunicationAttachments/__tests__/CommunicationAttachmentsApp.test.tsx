/**
 * Wiring tests for CommunicationAttachmentsApp:
 *   - auth gate resolves, attachments load (inline images filtered out),
 *   - clicking a file row opens the shared RichFilePreviewDialog (mocked),
 *   - clicking an .eml row opens the SAME in-modal preview (UAT R4 B12-4),
 *   - "Open file" (desktop) behaves the same as every other surface (ISS-004): an Office file launches the
 *     Office app through a hidden iframe; any other file downloads through /content; a failure is shown.
 */

import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { CommunicationAttachmentsApp } from '../CommunicationAttachments/CommunicationAttachmentsApp';
import { authenticatedFetch } from '@spaarke/auth';
import { AttachmentType } from '@spaarke/communication-components/logic/attachments';

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

// The mock's default implementation (preview-url + open-links with a desktopUrl); tests below override it.
const defaultFetchImpl = (authenticatedFetch as jest.Mock).getMockImplementation();

beforeEach(() => {
  (authenticatedFetch as jest.Mock).mockClear();
});

afterEach(() => {
  jest.restoreAllMocks();
  (authenticatedFetch as jest.Mock).mockImplementation(defaultFetchImpl);
  document.querySelectorAll('iframe').forEach(frame => frame.remove());
  delete (window as unknown as { Xrm?: unknown }).Xrm;
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

  describe('Open file (desktop)', () => {
    const openPreview = async () => {
      renderApp(makeContext(rows));
      fireEvent.click(await screen.findByText('Report.pdf'));
      await screen.findByTestId('rich-file-preview-dialog');
    };

    it('Office file: launches the desktop URL through a hidden iframe, without downloading', async () => {
      await openPreview();
      fireEvent.click(screen.getByTestId('preview-open-file'));

      await waitFor(() => {
        const frame = document.querySelector('iframe');
        expect(frame).not.toBeNull();
        expect(frame!.getAttribute('src')).toBe('ms-word:ofe|u|https://spe/file.docx');
        expect(frame!.style.display).toBe('none');
      });
      const contentFetched = (authenticatedFetch as jest.Mock).mock.calls.some((call: unknown[]) =>
        String(call[0]).includes('/content')
      );
      expect(contentFetched).toBe(false);
    });

    it('non-Office file: downloads through the BFF /content route, never the SharePoint webUrl', async () => {
      const urlApi = URL as unknown as { createObjectURL?: () => string; revokeObjectURL?: () => void };
      urlApi.createObjectURL = jest.fn(() => 'blob:test');
      urlApi.revokeObjectURL = jest.fn();
      const openSpy = jest.spyOn(window, 'open').mockImplementation(() => null);
      const clickSpy = jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
      (authenticatedFetch as jest.Mock).mockImplementation(async (url: string) => {
        if (url.includes('/open-links')) {
          return { ok: true, status: 200, json: async () => ({ webUrl: 'https://spe/file', fileName: 'Report.pdf' }) };
        }
        if (url.includes('/content')) {
          return { ok: true, status: 200, blob: async () => new Blob(['x']) };
        }
        return { ok: true, status: 200, json: async () => ({ previewUrl: 'https://preview/embed' }) };
      });
      try {
        await openPreview();
        fireEvent.click(screen.getByTestId('preview-open-file'));

        await waitFor(() => expect(clickSpy).toHaveBeenCalledTimes(1));
        expect((authenticatedFetch as jest.Mock).mock.calls.map((call: unknown[]) => String(call[0]))).toContain(
          'https://bff.example/api/documents/doc-1/content'
        );
        expect(openSpy).not.toHaveBeenCalled();
        expect(document.querySelector('iframe')).toBeNull();
      } finally {
        clickSpy.mockRestore();
        openSpy.mockRestore();
      }
    });

    it('a failed download is shown to the user (platform error dialog)', async () => {
      const openErrorDialog = jest.fn();
      (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {}, Navigation: { openErrorDialog } };
      jest.spyOn(console, 'error').mockImplementation(() => undefined);
      (authenticatedFetch as jest.Mock).mockImplementation(async (url: string) => {
        if (url.includes('/open-links')) {
          return { ok: true, status: 200, json: async () => ({ webUrl: 'https://spe/file', fileName: 'Report.pdf' }) };
        }
        if (url.includes('/content')) throw new Error('HTTP 403');
        return { ok: true, status: 200, json: async () => ({ previewUrl: 'https://preview/embed' }) };
      });
      await openPreview();
      fireEvent.click(screen.getByTestId('preview-open-file'));

      await waitFor(() =>
        expect(openErrorDialog).toHaveBeenCalledWith({
          message: "Couldn't open the file. Try again, or download it from the record.",
        })
      );
    });
  });
});
