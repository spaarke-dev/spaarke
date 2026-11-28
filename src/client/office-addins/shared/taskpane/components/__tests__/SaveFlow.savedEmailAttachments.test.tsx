/**
 * Task 141 (owner Outlook test 2026-11-26): an email already saved to Spaarke, with attachments not all saved.
 *
 * Before: the saved state hid the attachments altogether — nothing said which were saved, and there was no way to save
 * the others except "Save again as a new document" (a second `.eml`).
 * Now:
 * - one compact "Saved to Spaarke" section (subject, From, Sent, "Filed to {name} ({number})", icon-only actions), then
 *   the Profile, then the attachments — no "Email" header, no separate "Filed to" card;
 * - each attachment is marked saved (a check labelled "Saved") or unsaved (a checkbox); a yellow "Unsaved attachments"
 *   warning shows while any is unsaved;
 * - Save shows only when an unsaved attachment can be added, is enabled only once one is ticked, and sends
 *   `email.savedDocumentId` with ONLY the ticked unsaved names, filed to the `.eml`'s record;
 * - after the add those names show "Adding…" (task 141 review F2: never saved from the queued list — only the identity
 *   re-read once the add's job finished says saved); the warning clears once none is unsaved;
 * - negatives: all saved → no Save, no warning; nothing ticked → Save disabled; names not determined → no claim that
 *   any is saved (or unsaved), nothing to save; not filed / record withheld → nothing to add; a refusal marks nothing.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section (a marker) and the picker are doubled.
 */
import { useState } from 'react';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow } from '../SaveFlow';
import { EmailContentCaptureContext, type CaptureEmailContent } from '../../hooks/emailContentCaptureContext';
import type { EmailContentCapture } from '../../services/emailContentCapture';
import {
  applyFiledRecord,
  type DocumentIdentityState,
  type ResolvedRelatedRecord,
} from '../../services/documentIdentityService';
import type { AttachmentInfo } from '@shared/adapters/types';

jest.mock('../DocumentProfileSection', () => {
  const ReactModule = jest.requireActual('react');
  return {
    DocumentProfileSection: (props: { showDocumentType?: boolean }) =>
      ReactModule.createElement('div', {
        'data-testid': 'profile-section',
        'data-show-document-type': String(props.showDocumentType ?? true),
      }),
  };
});
jest.mock('../RelatedToPicker', () => {
  const ReactModule = jest.requireActual('react');
  return {
    RelatedToPicker: () => ReactModule.createElement('div', { 'data-testid': 'related-to-picker' }),
  };
});
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
const ORG_URL = 'https://contoso.crm.dynamics.com';
const originalOrgUrl = process.env.ORG_URL;

const ATTACHMENTS: AttachmentInfo[] = [
  { id: 'att-a', name: 'Exhibit A.pdf', contentType: 'application/pdf', size: 2048, isInline: false },
  { id: 'att-b', name: 'Exhibit B.pdf', contentType: 'application/pdf', size: 2048, isInline: false },
  { id: 'att-c', name: 'Exhibit C.pdf', contentType: 'application/pdf', size: 2048, isInline: false },
];

const FILED_RECORD: ResolvedRelatedRecord = {
  entityType: 'sprk_matter',
  id: MATTER_ID,
  name: 'MAT-000777',
  displayName: 'Gamma Merger',
  number: 'MAT-000777',
};

const savedEmail = (
  savedAttachmentNames: string[] | undefined,
  relatedRecord: ResolvedRelatedRecord | null = FILED_RECORD
): DocumentIdentityState => ({
  kind: 'resolved',
  documentId: EML_ID,
  documentName: 'RE: Discovery schedule',
  fileName: 'RE_ Discovery schedule_a1b2.eml',
  relatedRecord,
  ...(savedAttachmentNames !== undefined ? { savedAttachmentNames } : {}),
});

const response = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  json: async () => body,
  text: async () => JSON.stringify(body),
});

const capture = jest.fn<Promise<EmailContentCapture>, Parameters<CaptureEmailContent>>(async () => ({
  body: '<p>Please see attached.</p>',
  isBodyHtml: true,
  attachments: [],
  selectedAttachmentFileNames: undefined,
  skipped: [],
}));

function OutlookPane({ initial }: { initial: DocumentIdentityState }) {
  const [identity, setIdentity] = useState<DocumentIdentityState | undefined>(initial);
  return (
    <FluentProvider theme={webLightTheme}>
      <EmailContentCaptureContext.Provider value={capture}>
        <SaveFlow
          hostType="outlook"
          itemId="AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def="
          itemName="RE: Discovery schedule"
          senderEmail="opposing@counsel.example"
          sentDate={new Date(2026, 10, 20)}
          attachments={ATTACHMENTS}
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
          canOpenRecord
          {...(identity !== undefined ? { documentIdentity: identity } : {})}
          onRetryDocumentIdentity={jest.fn()}
          onDocumentFiled={(documentId, record) => setIdentity(prev => applyFiledRecord(prev, documentId, record))}
        />
      </EmailContentCaptureContext.Provider>
    </FluentProvider>
  );
}

const saveCalls = () => mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'));
const sentBody = (index: number) => JSON.parse(String((saveCalls()[index]![1] as RequestInit).body));
const rowOf = (name: string) => screen.getByText(name).closest('[data-saved-state]') as HTMLElement;
const savedMarks = () => screen.queryAllByRole('img', { name: 'Saved' });
/** Tick by clicking the row text (the row toggles; clicking the checkbox would bubble to the row as well). */
const tick = (name: string) => fireEvent.click(screen.getByText(name));

beforeEach(() => {
  jest.clearAllMocks();
  mockFetch.mockReset();
  mockFetch.mockResolvedValue(response(200, { data: {} }));
  process.env.ORG_URL = ORG_URL;
});

afterAll(() => {
  if (originalOrgUrl === undefined) delete process.env.ORG_URL;
  else process.env.ORG_URL = originalOrgUrl;
});

describe('SaveFlow — a saved email with unsaved attachments (task 141)', () => {
  it("owner's bug: the saved state marks each attachment, and Save sends savedDocumentId with only the ticked unsaved names", async () => {
    // The server's real add answer (task 141 review): a 202 whose attachmentsAdded names what it is adding — the pane
    // marks those, never what it sent.
    mockFetch.mockImplementation(async (url: string, init?: RequestInit) =>
      String(url).includes('/api/office/save')
        ? response(202, {
            jobId: 'job-141',
            statusUrl: '/api/office/jobs/job-141',
            streamUrl: '/api/office/jobs/job-141/stream',
            status: 'Queued',
            duplicate: false,
            attachmentsAdded: JSON.parse(String(init?.body)).email.selectedAttachmentFileNames,
          })
        : String(url).includes('/api/office/jobs/')
          ? response(200, { jobId: 'job-141', status: 'Completed', completedPhases: [] })
          : response(200, { data: {} })
    );
    render(<OutlookPane initial={savedEmail(['Exhibit A.pdf'])} />);

    // The attachments are listed, with their saved state.
    expect(rowOf('Exhibit A.pdf')).toHaveAttribute('data-saved-state', 'saved');
    expect(within(rowOf('Exhibit A.pdf')).getByRole('img', { name: 'Saved' })).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: 'Select Exhibit A.pdf' })).toBeNull();
    expect(screen.getByRole('checkbox', { name: 'Select Exhibit B.pdf' })).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Select Exhibit C.pdf' })).toBeInTheDocument();
    expect(screen.getByTestId('saved-bar-unsaved-attachments')).toHaveTextContent('Unsaved attachments');

    // Save shows, disabled until one is ticked.
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();
    tick('Exhibit B.pdf');
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(saveCalls()).toHaveLength(1));
    const body = sentBody(0);
    expect(body.email.savedDocumentId).toBe(EML_ID);
    expect(body.email.selectedAttachmentFileNames).toEqual(['Exhibit B.pdf']);
    expect(body.email).not.toHaveProperty('attachments');
    expect(body.targetEntity).toEqual({ entityType: 'Matter', entityId: MATTER_ID, displayName: 'Gamma Merger' });

    // B is now being added (F2: not "saved" — this harness's identity re-read never settles); C is still unsaved, so the
    // warning stays.
    await waitFor(() => expect(rowOf('Exhibit B.pdf')).toHaveAttribute('data-saved-state', 'adding'));
    expect(screen.queryByRole('checkbox', { name: 'Select Exhibit B.pdf' })).toBeNull();
    expect(rowOf('Exhibit A.pdf')).toHaveAttribute('data-saved-state', 'saved');
    expect(rowOf('Exhibit C.pdf')).toHaveAttribute('data-saved-state', 'unsaved');
    expect(screen.getByTestId('saved-bar-unsaved-attachments')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled();

    // Add C: nothing left unsaved — no warning, no Save.
    tick('Exhibit C.pdf');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(saveCalls()).toHaveLength(2));
    expect(sentBody(1).email.selectedAttachmentFileNames).toEqual(['Exhibit C.pdf']);
    expect(sentBody(1).email.savedDocumentId).toBe(EML_ID);
    await waitFor(() => expect(rowOf('Exhibit C.pdf')).toHaveAttribute('data-saved-state', 'adding'));
    expect(rowOf('Exhibit B.pdf')).toHaveAttribute('data-saved-state', 'adding');
    expect(savedMarks()).toHaveLength(1);
    expect(screen.queryByTestId('saved-bar-unsaved-attachments')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
  });

  it('one compact section: subject, From, Sent and "Filed to {name} ({number})" with icon-only actions, then Profile, then attachments', () => {
    render(<OutlookPane initial={savedEmail(['Exhibit A.pdf'])} />);

    const title = screen.getByText('Saved to Spaarke');
    expect(screen.getByText('RE: Discovery schedule')).toBeInTheDocument();
    expect(screen.getByText(/^From: opposing@counsel\.example$/)).toBeInTheDocument();
    expect(screen.getByText(/^Sent: /)).toBeInTheDocument();
    expect(screen.getByText('Gamma Merger')).toBeInTheDocument();
    expect(screen.getByText(/\(MAT-000777\)/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open Gamma Merger in Spaarke' })).not.toHaveTextContent(/\S/);
    expect(screen.getByRole('button', { name: 'View Document' })).not.toHaveTextContent(/\S/);
    expect(screen.getByRole('button', { name: 'Copy Link' })).not.toHaveTextContent(/\S/);
    expect(screen.getByRole('button', { name: 'Save again as a new document' })).toBeInTheDocument();

    // No "Email" header, no separate "Filed to" card.
    expect(screen.queryByText('Email', { exact: true })).toBeNull();
    expect(screen.queryByText('Filed to', { selector: 'span' })).toBeNull();

    // Order: section → Profile (no Document type for an email) → attachments.
    const profile = screen.getByTestId('profile-section');
    expect(profile).toHaveAttribute('data-show-document-type', 'false');
    const firstAttachment = screen.getByText('Exhibit A.pdf');
    expect(title.compareDocumentPosition(profile) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(profile.compareDocumentPosition(firstAttachment) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('negative: all saved → every attachment marked, no warning, no Save', () => {
    render(<OutlookPane initial={savedEmail(['exhibit a.pdf', 'Exhibit B.PDF', 'Exhibit C.pdf'])} />);

    expect(savedMarks()).toHaveLength(3);
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.queryByTestId('saved-bar-unsaved-attachments')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
  });

  it('negative: nothing ticked → Save is disabled and sends nothing', () => {
    render(<OutlookPane initial={savedEmail([])} />);

    const save = screen.getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();
    fireEvent.click(save);
    expect(saveCalls()).toHaveLength(0);
  });

  it('negative: names not determined → no "Saved" mark, no warning, nothing to tick or save', () => {
    render(<OutlookPane initial={savedEmail(undefined)} />);

    expect(screen.getByText('Exhibit A.pdf')).toBeInTheDocument();
    expect(savedMarks()).toHaveLength(0);
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.queryByTestId('saved-bar-unsaved-attachments')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
  });

  // Task 143: an email filed to NO record now takes an add (SaveFlow.unfiledEmailAdd.test.tsx); one whose filing is not known still does not.
  it('negative: filing record not known → attachments listed, none can be added', () => {
    render(
      <OutlookPane
        initial={
          { ...(savedEmail(['Exhibit A.pdf'], null) as object), relatedRecordKnown: false } as DocumentIdentityState
        }
      />
    );

    expect(savedMarks()).toHaveLength(1);
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.queryByRole('button', { name: 'Save' })).toBeNull();
  });

  it('negative: a refused add (403) marks nothing saved', async () => {
    mockFetch.mockImplementation(async (url: string) =>
      String(url).includes('/api/office/save')
        ? response(403, { type: 'about:blank', title: 'Forbidden', status: 403, errorCode: 'OFFICE_009' })
        : response(200, { data: {} })
    );
    render(<OutlookPane initial={savedEmail(['Exhibit A.pdf'])} />);

    tick('Exhibit B.pdf');
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(saveCalls()).toHaveLength(1));
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Saving...' })).toBeNull());

    expect(savedMarks()).toHaveLength(1);
    expect(screen.queryByText('Exhibit B.pdf')?.closest('[data-saved-state]')).not.toHaveAttribute(
      'data-saved-state',
      'saved'
    );
  });
});
