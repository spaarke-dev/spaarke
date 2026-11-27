/**
 * Task 141 review (F2): the pane never claims "Saved" from the names an add QUEUED (`attachmentsAdded`) — only from the
 * identity's `savedAttachmentNames`.
 *
 * - The queued names show "Adding…": not ticked, not counted as unsaved, and Save is not offered for them.
 * - When the add's job finishes, the pane re-reads the identity. A name the server then lists is saved; one it does not
 *   (the worker could not create it) shows unsaved, with the "Unsaved attachments" warning and Save offered again.
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section and the picker are doubled.
 */
import { useState } from 'react';
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
const A = 'Exhibit A.pdf';
const B = 'Exhibit B.pdf';
const C = 'Exhibit C.pdf';

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

const savedEmail = (savedAttachmentNames: string[]): DocumentIdentityState => ({
  kind: 'resolved',
  documentId: EML_ID,
  documentName: 'RE: Discovery schedule',
  fileName: 'RE_ Discovery schedule_a1b2.eml',
  relatedRecord: FILED_RECORD,
  savedAttachmentNames,
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

let setIdentityFromTest: (next: DocumentIdentityState | undefined) => void = () => undefined;
const retry = jest.fn();

function OutlookPane({ initial }: { initial: DocumentIdentityState }) {
  const [identity, setIdentity] = useState<DocumentIdentityState | undefined>(initial);
  setIdentityFromTest = setIdentity;
  return (
    <FluentProvider theme={webLightTheme}>
      <EmailContentCaptureContext.Provider value={capture}>
        <SaveFlow
          hostType="outlook"
          itemId="AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def="
          internetMessageId="<CAF0-141-adding@mail.example.com>"
          itemName="RE: Discovery schedule"
          senderEmail="opposing@counsel.example"
          sentDate={new Date(2026, 10, 20)}
          attachments={[pdf(A), pdf(B), pdf(C)]}
          getAccessToken={jest.fn().mockResolvedValue('token')}
          apiBaseUrl="https://bff"
          canOpenRecord
          {...(identity !== undefined ? { documentIdentity: identity } : {})}
          onRetryDocumentIdentity={() => {
            retry();
            // The pane's real retry (App.resolveOpenEmailIdentity) shows 'checking' first; the test settles it.
            setIdentity('checking');
          }}
        />
      </EmailContentCaptureContext.Provider>
    </FluentProvider>
  );
}

const identitySettles = (next: DocumentIdentityState) => act(() => setIdentityFromTest(next));
const stateOf = (name: string) =>
  (screen.getByText(name).closest('[data-saved-state]') as HTMLElement | null)?.getAttribute('data-saved-state');
const warning = () => screen.queryByTestId('saved-bar-unsaved-attachments');
const addButton = () => screen.queryByRole('button', { name: 'Save' });
const tick = (name: string) => fireEvent.click(screen.getByText(name));

/** The add answers that it queued B and C; the job then reads `AttachmentsQueued` until `finished` is set. */
let finished = false;
// Fake timers: the add watch polls on a timer (3 s in the pane), and waitFor advances fake time itself, so these tests do not
// depend on how fast the machine runs (a loaded CI runner used to starve the real-time waits).
beforeEach(() => {
  jest.useFakeTimers();
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
  finished = false;
  mockFetch.mockImplementation(async (url: string) =>
    String(url).includes('/api/office/save')
      ? response(202, {
          jobId: 'job-141',
          statusUrl: '/api/office/jobs/job-141',
          streamUrl: '/api/office/jobs/job-141/stream',
          status: 'Queued',
          duplicate: false,
          attachmentsAdded: [B, C],
        })
      : String(url).includes('/api/office/jobs/')
        ? response(200, {
            jobId: 'job-141',
            status: 'Completed',
            currentPhase: finished ? 'AttachmentsIncomplete' : 'AttachmentsQueued',
            completedPhases: [],
            result: { artifact: { type: 'Document', id: EML_ID } },
          })
        : response(200, { data: {} })
  );
});

afterEach(() => {
  jest.useRealTimers();
});

async function addBAndC() {
  tick(B);
  tick(C);
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(stateOf(B)).toBe('adding'));
}

describe('SaveFlow — an add shows "Adding…" until its job finished (task 141 review F2)', () => {
  it('the queued names are adding: not saved, not tickable, no warning, no Save for them', async () => {
    render(<OutlookPane initial={savedEmail([A])} />);

    await addBAndC();

    expect(stateOf(C)).toBe('adding');
    expect(screen.getAllByText('Adding…')).toHaveLength(2);
    expect(screen.queryAllByRole('img', { name: 'Saved' })).toHaveLength(1);
    expect(screen.queryByRole('checkbox', { name: `Select ${B}` })).toBeNull();
    expect(screen.queryByRole('checkbox', { name: `Select ${C}` })).toBeNull();
    expect(warning()).toBeNull();
    expect(addButton()).toBeNull();

    // The re-read right after the save still lists only A: B and C stay adding, never unsaved.
    identitySettles(savedEmail([A]));
    await waitFor(() => expect(stateOf(A)).toBe('saved'));
    expect(stateOf(B)).toBe('adding');
    expect(stateOf(C)).toBe('adding');
    expect(warning()).toBeNull();
  });

  it('when the job finishes the identity is re-read: what the server lists is saved, the rest unsaved with the warning', async () => {
    render(<OutlookPane initial={savedEmail([A])} />);

    await addBAndC();
    identitySettles(savedEmail([A])); // the re-read right after the save
    expect(retry).toHaveBeenCalledTimes(1);

    // The worker finished, having created B but not C.
    finished = true;
    await waitFor(() => expect(retry).toHaveBeenCalledTimes(2), { timeout: 8000 });
    identitySettles(savedEmail([A, B]));

    await waitFor(() => expect(stateOf(B)).toBe('saved'));
    expect(stateOf(C)).toBe('unsaved');
    expect(screen.queryByText('Adding…')).toBeNull();
    expect(warning()).toBeInTheDocument();
    expect(addButton()).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: `Select ${C}` })).toBeInTheDocument();
  }, 15000);
});
