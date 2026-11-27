/**
 * Task 141 review (round 2): an add that is still running when the user leaves the Save tab, an add whose job cannot be
 * followed to a final phase, and the user's ticks while an add runs.
 *
 * - Leaving the tab unmounts SaveFlow and the hook (their watches stop) but `attachmentsAdding` lives on in the lifted state.
 *   On return the pane watches the job again, so the names settle instead of showing "Adding…" forever.
 * - A watch that stops waiting (three failed reads) without a final phase is NOT a failure: a neutral title, the names stay
 *   "Adding…", no "could not be added", no Save for them; the identity is re-read and a name the server lists becomes saved.
 * - A tick on a name that is "Adding…" is kept: if that add fails the name is unsaved and still ticked.
 *
 * Real SaveFlow + real useSaveFlow, with the saved state lifted like App.tsx does; only fetch, SSE, the Profile section and
 * the picker are doubled. Assertions are on visible text and controls, never the screen-reader live region.
 */
import { useState } from 'react';
import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { SaveFlow, DEFAULT_SAVED_DOCUMENT_PANE_STATE, type SavedDocumentPaneState } from '../SaveFlow';
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
const D = 'Exhibit D.pdf';

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
let setShownFromTest: (show: boolean) => void = () => undefined;
const retry = jest.fn();

/** Like App.tsx: the saved state and the identity live ABOVE SaveFlow, which can be unmounted (a tab switch). */
function OutlookPane({ initial, names }: { initial: DocumentIdentityState; names: string[] }) {
  const [identity, setIdentity] = useState<DocumentIdentityState | undefined>(initial);
  const [shown, setShown] = useState(true);
  const [savedState, setSavedState] = useState<SavedDocumentPaneState>(DEFAULT_SAVED_DOCUMENT_PANE_STATE);
  setIdentityFromTest = setIdentity;
  setShownFromTest = setShown;
  return (
    <FluentProvider theme={webLightTheme}>
      <EmailContentCaptureContext.Provider value={capture}>
        {shown && (
          <SaveFlow
            hostType="outlook"
            itemId="AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def="
            internetMessageId="<CAF0-141-resume@mail.example.com>"
            itemName="RE: Discovery schedule"
            senderEmail="opposing@counsel.example"
            sentDate={new Date(2026, 10, 20)}
            attachments={names.map(pdf)}
            getAccessToken={jest.fn().mockResolvedValue('token')}
            apiBaseUrl="https://bff"
            canOpenRecord
            savedState={savedState}
            onSavedStateChange={setSavedState}
            {...(identity !== undefined ? { documentIdentity: identity } : {})}
            onRetryDocumentIdentity={() => {
              retry();
              // The pane's real retry (App.resolveOpenEmailIdentity) shows 'checking' first; the test settles it.
              setIdentity('checking');
            }}
          />
        )}
      </EmailContentCaptureContext.Provider>
    </FluentProvider>
  );
}

const identitySettles = (next: DocumentIdentityState) => act(() => setIdentityFromTest(next));
const showTab = (show: boolean) => act(() => setShownFromTest(show));
const stateOf = (name: string) =>
  (screen.getByText(name).closest('[data-saved-state]') as HTMLElement | null)?.getAttribute('data-saved-state');
const addButton = () => screen.queryByRole('button', { name: 'Save' });
const tick = (name: string) => fireEvent.click(screen.getByText(name));

const STILL_ADDING = 'Still adding attachments. Check again in a moment.';

/**
 * The add answers that it queued `queued`. Job reads: the first is the save's own tracking (completes it); later reads are the
 * watch's, answered by `watchPhase()` (a phase), or a 500 when `failReads` is set.
 */
let queued: string[] = [];
let finished = false;
let failReads = false;
let jobReads = 0;
// Fake timers: the add watch polls on a timer (3 s in the pane), and waitFor advances fake time itself, so these tests do not
// depend on how fast the machine runs (a loaded CI runner used to starve the real-time waits).
beforeEach(() => {
  jest.useFakeTimers();
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
  queued = [B, C];
  finished = false;
  failReads = false;
  jobReads = 0;
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) {
      return response(202, {
        jobId: 'job-141',
        statusUrl: '/api/office/jobs/job-141',
        streamUrl: '/api/office/jobs/job-141/stream',
        status: 'Queued',
        duplicate: false,
        attachmentsAdded: queued,
      });
    }
    if (String(url).includes('/api/office/jobs/')) {
      jobReads += 1;
      if (jobReads > 1 && failReads) return response(500, { title: 'boom' });
      return response(200, {
        jobId: 'job-141',
        status: 'Completed',
        currentPhase: finished && jobReads > 1 ? 'AttachmentsIncomplete' : 'AttachmentsQueued',
        completedPhases: [],
        result: { artifact: { type: 'Document', id: EML_ID } },
      });
    }
    return response(200, { data: {} });
  });
});

afterEach(() => {
  jest.useRealTimers();
});

async function addAndSettle(toAdd: string[]) {
  for (const name of toAdd) tick(name);
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(stateOf(toAdd[0]!)).toBe('adding'));
  identitySettles(savedEmail([A])); // the re-read right after the save
  await waitFor(() => expect(stateOf(A)).toBe('saved'));
}

describe('SaveFlow — leaving the Save tab while an add runs (task 141 review, round 2)', () => {
  it('on return the add is watched again and its names settle: what the job created is saved, the rest unsaved', async () => {
    render(<OutlookPane initial={savedEmail([A])} names={[A, B, C]} />);
    await addAndSettle([B, C]);
    expect(retry).toHaveBeenCalledTimes(1);

    // Leave the tab (SaveFlow and its watch unmount), and the worker finishes meanwhile, having created B but not C.
    showTab(false);
    finished = true;
    showTab(true);

    // Still the same names, "Adding…" until the job is read again — never a premature failure.
    expect(stateOf(B)).toBe('adding');
    await waitFor(() => expect(retry).toHaveBeenCalledTimes(2), { timeout: 8000 });
    identitySettles(savedEmail([A, B]));

    await waitFor(() => expect(stateOf(B)).toBe('saved'));
    expect(stateOf(C)).toBe('unsaved');
    expect(screen.queryByText('Adding…')).toBeNull();
    expect(screen.getByTestId('saved-bar-unsaved-attachments')).toBeInTheDocument();
  }, 15000);

  it('while the job is still queued on return the names stay "Adding…" (no failure is claimed)', async () => {
    render(<OutlookPane initial={savedEmail([A])} names={[A, B, C]} />);
    await addAndSettle([B, C]);

    showTab(false);
    showTab(true);
    await waitFor(() => expect(jobReads).toBeGreaterThanOrEqual(3)); // the watch read the job again

    expect(stateOf(B)).toBe('adding');
    expect(stateOf(C)).toBe('adding');
    expect(screen.queryByText(/could not be added/)).toBeNull();
    expect(addButton()).toBeNull();
    expect(retry).toHaveBeenCalledTimes(1);
  });
});

describe('SaveFlow — an add whose job cannot be followed is neither added nor failed (task 141 review, round 2)', () => {
  it('shows a neutral message, keeps the names "Adding…", offers no Save; a later identity read settles them', async () => {
    render(<OutlookPane initial={savedEmail([A])} names={[A, B, C]} />);
    await addAndSettle([B, C]);

    // Every status read of the job from here on fails: the watch gives up after three.
    failReads = true;
    await waitFor(() => expect(retry).toHaveBeenCalledTimes(2), { timeout: 12000 });
    identitySettles(savedEmail([A])); // the re-read lists neither name

    await waitFor(() => expect(screen.getByText(STILL_ADDING)).toBeInTheDocument());
    expect(screen.queryByText(/could not be added/)).toBeNull();
    expect(screen.queryByText('Unsaved attachments')).toBeNull();
    expect(stateOf(B)).toBe('adding');
    expect(stateOf(C)).toBe('adding');
    expect(addButton()).toBeNull();

    // A later read of what is saved settles the names it lists; the other stays adding-or-unknown, not failed.
    fireEvent.click(screen.getByRole('button', { name: 'Check again' }));
    await waitFor(() => expect(retry).toHaveBeenCalledTimes(3));
    identitySettles(savedEmail([A, B]));

    await waitFor(() => expect(stateOf(B)).toBe('saved'));
    expect(stateOf(C)).toBe('adding');
    expect(screen.queryByText(/could not be added/)).toBeNull();
  }, 30000);
});

describe('SaveFlow — a tick on a name that is adding is kept (task 141 review, round 2)', () => {
  it('when that add fails the name is unsaved and still ticked, even after another box was ticked meanwhile', async () => {
    queued = [B];
    render(<OutlookPane initial={savedEmail([A])} names={[A, B, D]} />);
    await addAndSettle([B]);

    // While B is "Adding…" the user ticks another attachment.
    tick(D);
    expect(screen.getByRole('checkbox', { name: `Select ${D}` })).toBeChecked();

    // B's add fails: the job finishes and the server lists no new name.
    finished = true;
    await waitFor(() => expect(retry).toHaveBeenCalledTimes(2), { timeout: 8000 });
    identitySettles(savedEmail([A]));

    await waitFor(() => expect(stateOf(B)).toBe('unsaved'));
    expect(screen.getByRole('checkbox', { name: `Select ${B}` })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: `Select ${D}` })).toBeChecked();
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
  }, 15000);
});
