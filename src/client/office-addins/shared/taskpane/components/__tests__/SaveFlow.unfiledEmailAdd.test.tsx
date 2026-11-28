/**
 * Task 143 (owner's Outlook check of task 141, 2026-11-27): an email saved with NO record showed "Unsaved attachments" and
 * its unsaved attachment with neither a checkbox nor Save — a dead end. Now an `.eml` KNOWN to be filed to no record
 * offers the checkboxes and Save, and the add sends no record (the server adds only under an `.eml` filed to none).
 * A filed email still sends its record; an email whose filing is unknown still offers nothing.
 *
 * Also part 4: the saved card names itself as the email, so a subject that starts "Document:" is not read as an attachment.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section and the picker are doubled.
 */
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import { EmailContentCaptureContext, type CaptureEmailContent } from '../../hooks/emailContentCaptureContext';
import type { EmailContentCapture } from '../../services/emailContentCapture';
import type { DocumentIdentityState, ResolvedRelatedRecord } from '../../services/documentIdentityService';
import type { AttachmentInfo } from '@shared/adapters/types';

jest.mock('../DocumentProfileSection', () => ({ DocumentProfileSection: () => null }));
jest.mock('../RelatedToPicker', () => ({ RelatedToPicker: () => null }));
jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: { digest: jest.fn(async () => new Uint8Array(32).buffer) },
});

const EML_ID = '6fb8c3fa-13c3-f111-a05c-0022482913fc';
const MATTER_ID = '22222222-2222-2222-2222-222222222222';
const SUBJECT = 'Document: Lease renewal.docx';
const A = 'Lease renewal.docx';
const B = 'Exhibit B.pdf';

const pdf = (name: string): AttachmentInfo => ({
  id: `att-${name}`,
  name,
  contentType: 'application/pdf',
  size: 20_000,
  isInline: false,
});

const FILED_RECORD: ResolvedRelatedRecord = {
  entityType: 'sprk_matter',
  id: MATTER_ID,
  name: 'MAT-000777',
  displayName: 'Gamma Merger',
  number: 'MAT-000777',
};

const savedEmail = (
  relatedRecord: ResolvedRelatedRecord | null,
  extra: { relatedRecordKnown?: boolean } = {}
): DocumentIdentityState => ({
  kind: 'resolved',
  documentId: EML_ID,
  documentName: SUBJECT,
  fileName: 'Document_ Lease renewal_a1b2.eml',
  relatedRecord,
  savedAttachmentNames: [A],
  ...extra,
});

const capture = jest.fn<Promise<EmailContentCapture>, Parameters<CaptureEmailContent>>(async () => ({
  body: '<p>Please see attached.</p>',
  isBodyHtml: true,
  attachments: [],
  selectedAttachmentFileNames: undefined,
  skipped: [],
}));

const response = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  json: async () => body,
  text: async () => JSON.stringify(body),
});

function OutlookPane({ identity }: { identity: DocumentIdentityState }) {
  return (
    <FluentProvider theme={webLightTheme}>
      <EmailContentCaptureContext.Provider value={capture}>
        <SaveFlow
          hostType="outlook"
          itemId="AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def="
          internetMessageId="<CAF0-143-unfiled@mail.example.com>"
          itemName={SUBJECT}
          senderEmail="opposing@counsel.example"
          sentDate={new Date(2026, 10, 20)}
          attachments={[pdf(A), pdf(B)]}
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
          documentIdentity={identity}
          onRetryDocumentIdentity={retryIdentity}
        />
      </EmailContentCaptureContext.Provider>
    </FluentProvider>
  );
}

const retryIdentity = jest.fn();

const saveCalls = () => mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'));
// eslint-disable-next-line @typescript-eslint/no-explicit-any -- the request body is read loosely, as JSON
const bodyOf = (call: unknown[]) => JSON.parse((call[1] as RequestInit).body as string) as Record<string, any>;
const addButton = () => screen.queryByRole('button', { name: 'Save' });

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) =>
    String(url).includes('/api/office/save')
      ? response(200, { duplicate: true, artifact: { type: 'Document', id: EML_ID } })
      : response(200, { data: {} })
  );
});

describe('SaveFlow — add attachments to a saved email filed to no record (task 143)', () => {
  it('offers the unsaved attachment with a checkbox and Save, and the add sends no record', async () => {
    render(<OutlookPane identity={savedEmail(null)} />);

    expect(screen.queryByRole('checkbox', { name: `Select ${A}` })).toBeNull(); // saved
    fireEvent.click(screen.getByRole('checkbox', { name: `Select ${B}` }));
    expect(addButton()).toBeEnabled();

    fireEvent.click(addButton()!);
    await waitFor(() => expect(saveCalls()).toHaveLength(1));

    const body = bodyOf(saveCalls()[0]!);
    expect(body.contentType).toBe('Email');
    expect(body.email.savedDocumentId).toBe(EML_ID);
    expect(body.email.selectedAttachmentFileNames).toEqual([B]);
    expect(body.targetEntity).toBeUndefined();
  });

  it('negative: a filed email still sends its record', async () => {
    render(<OutlookPane identity={savedEmail(FILED_RECORD)} />);

    fireEvent.click(screen.getByRole('checkbox', { name: `Select ${B}` }));
    fireEvent.click(addButton()!);
    await waitFor(() => expect(saveCalls()).toHaveLength(1));

    expect(bodyOf(saveCalls()[0]!).targetEntity).toEqual({
      entityType: 'Matter',
      entityId: MATTER_ID,
      displayName: 'Gamma Merger',
    });
  });

  it('negative: an email whose filing is not known offers no checkbox and no Save', () => {
    render(<OutlookPane identity={savedEmail(null, { relatedRecordKnown: false })} />);

    expect(screen.getByText(B)).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: `Select ${B}` })).toBeNull();
    expect(addButton()).toBeNull();
  });
});

describe('SaveFlow — after a save, the add follows the filing the server reports (task 143 fix B)', () => {
  // Owner 2026-11-27: an email's .eml and its communication are always filed to the same record. The pane saved with no
  // record, but the identity re-read after the save reports the .eml filed (through its communication) to a matter: the
  // next add goes to that matter, never with no record (the server refuses that, 403).
  it('takes the re-read record: the next add sends it', async () => {
    const { rerender } = render(<OutlookPane identity={savedEmail(null)} />);

    fireEvent.click(screen.getByRole('checkbox', { name: `Select ${B}` }));
    fireEvent.click(addButton()!);
    await waitFor(() => expect(saveCalls()).toHaveLength(1));
    expect(bodyOf(saveCalls()[0]!).targetEntity).toBeUndefined();
    await waitFor(() => expect(retryIdentity).toHaveBeenCalled());

    // The re-read the completed add asks for: checking, then the server's answer.
    rerender(<OutlookPane identity="checking" />);
    rerender(<OutlookPane identity={savedEmail(FILED_RECORD)} />);
    await act(async () => {
      await Promise.resolve();
    });
    expect(document.body.textContent).toContain('Filed to Gamma Merger');

    // B is still ticked from the first add (nothing was added, so it is still unsaved).
    expect(await screen.findByRole('checkbox', { name: `Select ${B}` })).toBeChecked();
    fireEvent.click(addButton()!);
    await waitFor(() => expect(saveCalls()).toHaveLength(2));
    expect(bodyOf(saveCalls()[1]!).targetEntity).toEqual({
      entityType: 'Matter',
      entityId: MATTER_ID,
      displayName: 'Gamma Merger',
    });
  });

  it('negative: a re-read whose record is not known keeps what the pane saved with', async () => {
    const { rerender } = render(<OutlookPane identity={savedEmail(null)} />);

    fireEvent.click(screen.getByRole('checkbox', { name: `Select ${B}` }));
    fireEvent.click(addButton()!);
    await waitFor(() => expect(saveCalls()).toHaveLength(1));
    await waitFor(() => expect(retryIdentity).toHaveBeenCalled());

    rerender(<OutlookPane identity="checking" />);
    rerender(<OutlookPane identity={savedEmail(null, { relatedRecordKnown: false })} />);
    await act(async () => {
      await Promise.resolve();
    });

    // B is still ticked from the first add (nothing was added, so it is still unsaved).
    expect(await screen.findByRole('checkbox', { name: `Select ${B}` })).toBeChecked();
    fireEvent.click(addButton()!);
    await waitFor(() => expect(saveCalls()).toHaveLength(2));
    expect(bodyOf(saveCalls()[1]!).targetEntity).toBeUndefined();
  });
});

describe('SaveFlow — the saved card names itself as the email (task 143 part 4)', () => {
  it('shows an "Email" label before a subject that starts with "Document:"', () => {
    render(<OutlookPane identity={savedEmail(null)} />);

    const line = screen.getByTestId('saved-bar-email-subject');
    expect(line).toHaveTextContent(`Email: ${SUBJECT}`);
    expect(line.textContent?.startsWith('Email:')).toBe(true);
  });
});
