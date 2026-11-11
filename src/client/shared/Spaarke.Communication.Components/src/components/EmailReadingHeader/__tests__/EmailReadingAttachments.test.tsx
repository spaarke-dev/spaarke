/**
 * EmailReadingAttachments.test.tsx
 *
 * Unit/RTL tests for `<EmailReadingAttachments />` (email-communication-solution-r5
 * task 034, spec FR-12). Covers: attachments render via the promoted (task 021)
 * `AttachmentList` + `CommunicationAttachmentsService` (inline `cid:` images
 * excluded by the REAL shared service, not re-implemented here), clicking a row
 * opens the shared `RichFilePreviewDialog` (mocked — pulls iframe/Graph
 * machinery unsuitable for jsdom, same rationale as the PCF's own test mock),
 * the zero-attachments negative case (empty state, no crash, no phantom rows),
 * and dark-mode theming (ADR-021).
 */
import * as React from 'react';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { EmailReadingAttachments } from '../EmailReadingAttachments';
import type { IEmailAttachmentsDataService, IEmailAttachmentsNavigationService } from '../EmailReadingHeader.types';

// `AttachmentApiService` (task 021, consumed via `../../logic/attachments`) imports
// `@spaarke/auth`'s `authenticatedFetch`. This package's jest.config.cjs intentionally
// does NOT map `@spaarke/auth` to source (it never initializes auth itself) — every
// test file that transitively needs it supplies its own mock (see jest.config.cjs
// docstring + `CommunicationsWorkspaceWidget.test.ts` / `EmailBodyView.test.tsx`).
const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  __esModule: true,
  authenticatedFetch: (...args: [string, RequestInit?]) => mockAuthenticatedFetch(...args),
}));

// jsdom has no ResizeObserver; `MessageBar` (error state) needs one to mount.
if (typeof globalThis.ResizeObserver === 'undefined') {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

// AttachmentType option-set values (see logic/attachments/types.ts).
const ATTACHMENT_TYPE_FILE = 100000000;
const ATTACHMENT_TYPE_INLINE_IMAGE = 100000001;

interface MockFilePreviewDialogProps {
  open: boolean;
  documentName: string;
  documentId: string;
  onClose: () => void;
  onOpenRecord: () => void;
  fetchPreviewUrl: () => Promise<string | null>;
  onOpenFile: (mode: 'desktop' | 'web') => void;
  onEmailDocument: () => void;
  onCopyLink: () => void;
  navigationTotal?: number;
  currentIndex?: number;
  onNavigate?: (nextIndex: number) => void;
}

jest.mock('@spaarke/ui-components', () => {
  const actual = jest.requireActual('@spaarke/ui-components');
  return {
    ...actual,
    RichFilePreviewDialog: (props: MockFilePreviewDialogProps) => {
      if (!props.open) return null;
      return (
        <div data-testid="rich-file-preview-dialog" data-document-id={props.documentId}>
          <span data-testid="preview-doc-name">{props.documentName}</span>
          <button data-testid="preview-open-record" onClick={() => props.onOpenRecord()}>
            open-record
          </button>
          <button data-testid="preview-close" onClick={() => props.onClose()}>
            close
          </button>
        </div>
      );
    },
  };
});

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

function makeDataService(
  attachmentRows: Record<string, unknown>[],
  documentRows: Record<string, unknown>[] = []
): IEmailAttachmentsDataService {
  return {
    retrieveMultipleRecords: jest.fn((entityName: string) => {
      if (entityName === 'sprk_communicationattachment') {
        return Promise.resolve({ entities: attachmentRows });
      }
      if (entityName === 'sprk_document') {
        return Promise.resolve({ entities: documentRows });
      }
      return Promise.resolve({ entities: [] });
    }),
  };
}

function makeNavigation(): IEmailAttachmentsNavigationService {
  return { openRecord: jest.fn().mockResolvedValue(undefined) };
}

describe('EmailReadingAttachments', () => {
  it('renders true attachments and excludes inline (cid:) images (FR-12)', async () => {
    const dataService = makeDataService(
      [
        {
          sprk_communicationattachmentid: 'a1',
          sprk_name: 'Contract.pdf',
          sprk_attachmenttype: ATTACHMENT_TYPE_FILE,
          _sprk_document_value: 'doc-1',
        },
        {
          sprk_communicationattachmentid: 'a2',
          sprk_name: 'logo.png',
          sprk_attachmenttype: ATTACHMENT_TYPE_INLINE_IMAGE,
          _sprk_document_value: 'doc-2',
        },
      ],
      [{ sprk_documentid: 'doc-1', sprk_hasfile: true, sprk_graphitemid: 'item-1' }]
    );

    renderWithProvider(
      <EmailReadingAttachments
        selectedId="comm-1"
        dataService={dataService}
        navigation={makeNavigation()}
        apiBaseUrl="https://bff.example.com"
      />
    );

    await waitFor(() => expect(screen.getByText('Contract.pdf')).toBeInTheDocument());
    expect(screen.queryByText('logo.png')).not.toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(1);
  });

  it('clicking an attachment opens RichFilePreviewDialog for that item', async () => {
    const dataService = makeDataService(
      [
        {
          sprk_communicationattachmentid: 'a1',
          sprk_name: 'Contract.pdf',
          sprk_attachmenttype: ATTACHMENT_TYPE_FILE,
          _sprk_document_value: 'doc-1',
        },
      ],
      [{ sprk_documentid: 'doc-1', sprk_hasfile: true, sprk_graphitemid: 'item-1' }]
    );
    const navigation = makeNavigation();

    renderWithProvider(
      <EmailReadingAttachments
        selectedId="comm-1"
        dataService={dataService}
        navigation={navigation}
        apiBaseUrl="https://bff.example.com"
      />
    );

    await waitFor(() => expect(screen.getByText('Contract.pdf')).toBeInTheDocument());
    fireEvent.click(screen.getByText('Contract.pdf'));

    expect(screen.getByTestId('rich-file-preview-dialog')).toHaveAttribute('data-document-id', 'doc-1');
    expect(screen.getByTestId('preview-doc-name')).toHaveTextContent('Contract.pdf');

    fireEvent.click(screen.getByTestId('preview-open-record'));
    expect(navigation.openRecord).toHaveBeenCalledWith('sprk_document', 'doc-1');
  });

  it('negative: zero attachments renders an empty state — no crash, no phantom rows', async () => {
    const dataService = makeDataService([]);

    renderWithProvider(
      <EmailReadingAttachments
        selectedId="comm-1"
        dataService={dataService}
        navigation={makeNavigation()}
        apiBaseUrl="https://bff.example.com"
      />
    );

    await waitFor(() => expect(screen.getByText('No attachments on this email.')).toBeInTheDocument());
    expect(screen.queryAllByRole('listitem')).toHaveLength(0);
  });

  it('renders an error banner when the attachment load rejects (no crash)', async () => {
    const dataService: IEmailAttachmentsDataService = {
      retrieveMultipleRecords: jest.fn().mockRejectedValue(new Error('boom')),
    };
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});

    renderWithProvider(
      <EmailReadingAttachments
        selectedId="comm-1"
        dataService={dataService}
        navigation={makeNavigation()}
        apiBaseUrl="https://bff.example.com"
      />
    );

    await waitFor(() => expect(screen.getByText('Could not load attachments for this email.')).toBeInTheDocument());
    errorSpy.mockRestore();
  });

  it('renders correctly under a dark FluentProvider theme (ADR-021) with no console errors', async () => {
    const consoleErrorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    const dataService = makeDataService(
      [
        {
          sprk_communicationattachmentid: 'a1',
          sprk_name: 'Contract.pdf',
          sprk_attachmenttype: ATTACHMENT_TYPE_FILE,
          _sprk_document_value: 'doc-1',
        },
      ],
      [{ sprk_documentid: 'doc-1', sprk_hasfile: true }]
    );

    renderWithProvider(
      <EmailReadingAttachments
        selectedId="comm-1"
        dataService={dataService}
        navigation={makeNavigation()}
        apiBaseUrl="https://bff.example.com"
      />,
      webDarkTheme
    );

    await waitFor(() => expect(screen.getByText('Contract.pdf')).toBeInTheDocument());
    expect(consoleErrorSpy).not.toHaveBeenCalled();
    consoleErrorSpy.mockRestore();
  });

  // ── ebilling-r1 task 118: the profile's Invoice flag → badge + "Submit as invoice" ─────────────────────────────
  describe('Invoice flag (task 118)', () => {
    const DOCUMENT_TYPE_INVOICE = 100000001;
    const DOCUMENT_TYPE_CONTRACT = 100000000;
    const row = (id: string, name: string, doc: string | null) => ({
      sprk_communicationattachmentid: id,
      sprk_name: name,
      sprk_attachmenttype: ATTACHMENT_TYPE_FILE,
      _sprk_document_value: doc,
    });

    function renderFlagged(onSubmitAsInvoice?: (documentId: string) => void) {
      const dataService = makeDataService(
        [
          row('a1', 'Invoice-10044725.pdf', 'doc-1'),
          row('a2', 'Contract.pdf', 'doc-2'),
          row('a3', 'notes.pdf', 'doc-3'),
          row('a4', 'orphan.pdf', null),
        ],
        [
          { sprk_documentid: 'doc-1', sprk_hasfile: true, sprk_documenttype: DOCUMENT_TYPE_INVOICE },
          { sprk_documentid: 'doc-2', sprk_hasfile: true, sprk_documenttype: DOCUMENT_TYPE_CONTRACT },
          // doc-3: not profiled yet - no type at all
          { sprk_documentid: 'doc-3', sprk_hasfile: true, sprk_documenttype: null },
        ]
      );
      renderWithProvider(
        <EmailReadingAttachments
          selectedId="comm-1"
          dataService={dataService}
          navigation={makeNavigation()}
          apiBaseUrl="https://bff.example.com"
          onSubmitAsInvoice={onSubmitAsInvoice}
        />
      );
      return dataService;
    }

    it('shows the Invoice badge only on the attachment whose document is typed Invoice', async () => {
      renderFlagged();
      await waitFor(() => expect(screen.getByText('Invoice-10044725.pdf')).toBeInTheDocument());
      const badges = screen.getAllByTestId('attachment-invoice-badge');
      expect(badges).toHaveLength(1);
      const flaggedRow = screen.getByText('Invoice-10044725.pdf').closest('[role="listitem"]') as HTMLElement;
      expect(flaggedRow).toContainElement(badges[0]);
      // Another type, no type yet, and no document at all show none.
      for (const name of ['Contract.pdf', 'notes.pdf', 'orphan.pdf']) {
        const other = screen.getByText(name).closest('[role="listitem"]') as HTMLElement;
        expect(other.querySelector('[data-testid="attachment-invoice-badge"]')).toBeNull();
        expect(other.querySelector('[data-testid="attachment-submit-as-invoice"]')).toBeNull();
      }
    });

    it('reads the type in the existing single sprk_document query (query count unchanged)', async () => {
      const dataService = renderFlagged();
      await waitFor(() => expect(screen.getByText('Invoice-10044725.pdf')).toBeInTheDocument());
      const calls = (dataService.retrieveMultipleRecords as jest.Mock).mock.calls;
      expect(calls.map(c => c[0])).toEqual(['sprk_communicationattachment', 'sprk_document']);
      expect(calls[1][1]).toContain('sprk_documenttype');
    });

    it('"Submit as invoice" opens the flow with that attachment document and does not open the preview', async () => {
      const onSubmitAsInvoice = jest.fn();
      renderFlagged(onSubmitAsInvoice);
      await waitFor(() => expect(screen.getByText('Invoice-10044725.pdf')).toBeInTheDocument());
      fireEvent.click(screen.getByTestId('attachment-submit-as-invoice'));
      expect(onSubmitAsInvoice).toHaveBeenCalledTimes(1);
      expect(onSubmitAsInvoice).toHaveBeenCalledWith('doc-1');
      expect(screen.queryByTestId('rich-file-preview-dialog')).not.toBeInTheDocument();
    });

    it('negative: a flagged attachment that is not a PDF keeps the badge but offers no "Submit as invoice"', async () => {
      const dataService = makeDataService(
        [row('a1', 'Invoice-scan.docx', 'doc-1'), row('a2', 'Invoice-10044725.PDF', 'doc-2')],
        [
          { sprk_documentid: 'doc-1', sprk_hasfile: true, sprk_documenttype: DOCUMENT_TYPE_INVOICE },
          { sprk_documentid: 'doc-2', sprk_hasfile: true, sprk_documenttype: DOCUMENT_TYPE_INVOICE },
        ]
      );
      renderWithProvider(
        <EmailReadingAttachments
          selectedId="comm-1"
          dataService={dataService}
          navigation={makeNavigation()}
          apiBaseUrl="https://bff.example.com"
          onSubmitAsInvoice={jest.fn()}
        />
      );
      await waitFor(() => expect(screen.getByText('Invoice-scan.docx')).toBeInTheDocument());
      expect(screen.getAllByTestId('attachment-invoice-badge')).toHaveLength(2);
      const docxRow = screen.getByText('Invoice-scan.docx').closest('[role="listitem"]') as HTMLElement;
      const pdfRow = screen.getByText('Invoice-10044725.PDF').closest('[role="listitem"]') as HTMLElement;
      expect(docxRow.querySelector('[data-testid="attachment-submit-as-invoice"]')).toBeNull();
      expect(pdfRow.querySelector('[data-testid="attachment-submit-as-invoice"]')).not.toBeNull();
    });

    it('without a handler the badge still shows and there is no action', async () => {
      renderFlagged(undefined);
      await waitFor(() => expect(screen.getByTestId('attachment-invoice-badge')).toBeInTheDocument());
      expect(screen.queryByTestId('attachment-submit-as-invoice')).not.toBeInTheDocument();
    });
  });
});
