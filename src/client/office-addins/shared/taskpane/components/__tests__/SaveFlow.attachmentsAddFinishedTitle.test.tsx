/**
 * Task 141 (owner re-check 2026-11-26): once an attachments add FINISHES, the confirmation title says what happened. It read
 * "Adding N attachments…" for as long as the pane was open, even after the job ended and the identity was re-read.
 *
 * - All the queued names are now stored (the identity lists them): "N attachments added to the saved email".
 * - Some are not (the identity does not list them): how many were added and how many could not be — the "Unsaved attachments"
 *   warning still names them.
 * - While the job is still running the title is still "Adding N attachments…".
 *
 * Real SaveFlow + real useSaveFlow; only fetch, SSE, the Profile section and the picker are doubled (the harness of
 * SaveFlow.attachmentsAddingUntilFinished.test.tsx). The assertions read the visible title, not the screen-reader live region.
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
          internetMessageId="<CAF0-141-finished-title@mail.example.com>"
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
const tick = (name: string) => fireEvent.click(screen.getByText(name));

/** The add answers that it queued B and C; the job reads `AttachmentsQueued` until `phaseWhenFinished` is set. */
let phaseWhenFinished: string | null = null;
// Fake timers: the add watch polls on a timer (3 s in the pane), and waitFor advances fake time itself, so these tests do not
// depend on how fast the machine runs (a loaded CI runner used to starve the real-time waits).
beforeEach(() => {
  jest.useFakeTimers();
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
  phaseWhenFinished = null;
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
            currentPhase: phaseWhenFinished ?? 'AttachmentsQueued',
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
  identitySettles(savedEmail([A])); // the re-read right after the save
  expect(retry).toHaveBeenCalledTimes(1);
}

/** The worker finished: the job's phase moves on, the pane re-reads the identity, and the server lists `savedNames`. */
async function jobFinishes(phase: string, savedNames: string[]) {
  phaseWhenFinished = phase;
  await waitFor(() => expect(retry).toHaveBeenCalledTimes(2), { timeout: 8000 });
  identitySettles(savedEmail(savedNames));
}

const titleStartingWith = (text: RegExp) => screen.queryAllByText(text).filter(el => !el.closest('.sr-only'));

describe('SaveFlow — the confirmation title after an attachments add finished (task 141)', () => {
  it('while the job is running the title still says the attachments are being added', async () => {
    render(<OutlookPane initial={savedEmail([A])} />);

    await addBAndC();

    expect(titleStartingWith(/^Adding 2 attachments to the saved email/)).toHaveLength(1);
  });

  it('when the job finished and every attachment is stored, the title says they were added', async () => {
    render(<OutlookPane initial={savedEmail([A])} />);

    await addBAndC();
    await jobFinishes('AttachmentsAdded', [A, B, C]);

    await waitFor(() => expect(stateOf(C)).toBe('saved'));
    expect(titleStartingWith(/^2 attachments added to the saved email$/)).toHaveLength(1);
    expect(titleStartingWith(/^Adding /)).toHaveLength(0);
  }, 15000);

  it('when the job finished and one attachment is not stored, the title says how many were added and how many were not', async () => {
    render(<OutlookPane initial={savedEmail([A])} />);

    await addBAndC();
    await jobFinishes('AttachmentsIncomplete', [A, B]);

    await waitFor(() => expect(stateOf(C)).toBe('unsaved'));
    expect(titleStartingWith(/^1 of 2 attachments added to the saved email\. 1 could not be added\.$/)).toHaveLength(1);
    expect(titleStartingWith(/^Adding /)).toHaveLength(0);
    expect(screen.getByTestId('saved-bar-unsaved-attachments')).toBeInTheDocument();
  }, 15000);

  it('when the job finished and none of the attachments is stored, the title says none could be added', async () => {
    render(<OutlookPane initial={savedEmail([A])} />);

    await addBAndC();
    await jobFinishes('AttachmentsIncomplete', [A]);

    await waitFor(() => expect(stateOf(B)).toBe('unsaved'));
    expect(titleStartingWith(/^2 attachments could not be added to the saved email\.$/)).toHaveLength(1);
    expect(titleStartingWith(/^Adding /)).toHaveLength(0);
  }, 15000);
});
