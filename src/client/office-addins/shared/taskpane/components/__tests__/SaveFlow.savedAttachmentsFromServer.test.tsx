/**
 * Task 141 review (findings 2, 3, 5): what the saved-email attachments section says is saved comes ONLY from the server.
 *
 * - Finding 2: after the pane's own save, "saved" was what the pane SENT — wrong when the save deduplicated to an existing
 *   email (no new children) or the worker left names out. Now the pane re-reads the identity after the save and uses its
 *   `savedAttachmentNames`; until it arrives nothing is marked, nothing is offered and there is no warning. An add shows
 *   what the server's answer says it is adding (`attachmentsAdded`) as "Adding…" — never what was ticked, and (F2) never
 *   "saved" until the identity says so.
 * - Finding 3: an attachment no email save ever creates (a calendar invite) shows "Can't be saved": never a check, never
 *   tickable, and it never keeps the "Unsaved attachments" warning up. A name the server reports in
 *   `attachmentsNotSaveable` is shown the same way.
 * - Finding 5: Save-to-add is offered only once the server has sent `savedAttachmentNames` (proof it knows
 *   `savedDocumentId`), never from names the pane worked out itself — not even right after its own save.
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
jest.mock('../RelatedToPicker', () => {
  const ReactModule = jest.requireActual('react');
  return {
    RelatedToPicker: (props: { onChange: (e: unknown) => void; disabled?: boolean }) =>
      ReactModule.createElement(
        'button',
        {
          type: 'button',
          disabled: props.disabled,
          onClick: () =>
            props.onChange({
              id: '22222222-2222-2222-2222-222222222222',
              entityType: 'Matter',
              logicalName: 'sprk_matter',
              name: 'Gamma Merger',
              displayInfo: 'MAT-000777',
            }),
        },
        'Pick matter'
      ),
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
const A = 'Exhibit A.pdf';
const B = 'Exhibit B.pdf';
const C = 'Exhibit C.pdf';
const INVITE = 'Meeting.ics';

const pdf = (name: string): AttachmentInfo => ({
  id: `att-${name}`,
  name,
  contentType: 'application/pdf',
  size: 20_000,
  isInline: false,
});
const invite: AttachmentInfo = {
  id: 'att-invite',
  name: INVITE,
  contentType: 'text/calendar',
  size: 4_000,
  isInline: false,
};

const FILED_RECORD: ResolvedRelatedRecord = {
  entityType: 'sprk_matter',
  id: MATTER_ID,
  name: 'MAT-000777',
  displayName: 'Gamma Merger',
  number: 'MAT-000777',
};

const savedEmail = (savedAttachmentNames: string[] | undefined): DocumentIdentityState => ({
  kind: 'resolved',
  documentId: EML_ID,
  documentName: 'RE: Discovery schedule',
  fileName: 'RE_ Discovery schedule_a1b2.eml',
  relatedRecord: FILED_RECORD,
  ...(savedAttachmentNames !== undefined ? { savedAttachmentNames } : {}),
});

const response = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  json: async () => body,
  text: async () => JSON.stringify(body),
});
const accepted = (extra: Record<string, unknown> = {}) =>
  response(202, {
    jobId: 'job-141',
    statusUrl: '/api/office/jobs/job-141',
    streamUrl: '/api/office/jobs/job-141/stream',
    status: 'Queued',
    duplicate: false,
    ...extra,
  });
const completedOn = (documentId: string) =>
  response(200, {
    jobId: 'job-141',
    status: 'Completed',
    completedPhases: [],
    result: { artifact: { type: 'Document', id: documentId } },
  });

const capture = jest.fn<Promise<EmailContentCapture>, Parameters<CaptureEmailContent>>(
  async (attachments, selected) => ({
    body: '<p>Please see attached.</p>',
    isBodyHtml: true,
    attachments: [],
    selectedAttachmentFileNames: attachments.filter(a => selected.has(a.id)).map(a => a.name),
    skipped: [],
  })
);

let setIdentityFromTest: (next: DocumentIdentityState | undefined) => void = () => undefined;
const retry = jest.fn();

function OutlookPane({
  attachments,
  initial,
}: {
  attachments: AttachmentInfo[];
  initial: DocumentIdentityState | undefined;
}) {
  const [identity, setIdentity] = useState<DocumentIdentityState | undefined>(initial);
  setIdentityFromTest = setIdentity;
  return (
    <FluentProvider theme={webLightTheme}>
      <EmailContentCaptureContext.Provider value={capture}>
        <SaveFlow
          hostType="outlook"
          itemId="AAMkADAxZWI4YzM4LWVjNTgtNDcwOC1iY2EzLTI1MTc1MGU1MmFhMQBGAAAAAAD/abc+def="
          internetMessageId="<CAF0-141-review@mail.example.com>"
          itemName="RE: Discovery schedule"
          senderEmail="opposing@counsel.example"
          sentDate={new Date(2026, 10, 20)}
          attachments={attachments}
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

/** The identity re-read settles with what the server answered. */
const identitySettles = (next: DocumentIdentityState | undefined) => act(() => setIdentityFromTest(next));

const saveCalls = () => mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'));
const stateOf = (name: string) =>
  (screen.getByText(name).closest('[data-saved-state]') as HTMLElement | null)?.getAttribute('data-saved-state');
const savedMarks = () => screen.queryAllByRole('img', { name: 'Saved' });
const warning = () => screen.queryByTestId('saved-bar-unsaved-attachments');
const addButton = () => screen.queryByRole('button', { name: 'Save' });
const tick = (name: string) => fireEvent.click(screen.getByText(name));

async function firstSaveOfTheEmail() {
  fireEvent.click(screen.getByRole('button', { name: 'Pick matter' }));
  fireEvent.click(screen.getByRole('button', { name: 'Save' }));
  await waitFor(() => expect(screen.getByText('Saved to Spaarke')).toBeInTheDocument());
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
  mockFetch.mockImplementation(async (url: string) =>
    String(url).includes('/api/office/save')
      ? accepted()
      : String(url).includes('/api/office/jobs/')
        ? completedOn(EML_ID)
        : response(200, { data: {} })
  );
});

describe('SaveFlow — saved attachments come from the server (task 141 review)', () => {
  it('after its own save the pane marks nothing until the identity re-read, then shows what the server saved', async () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} initial={undefined} />);

    await firstSaveOfTheEmail();
    expect(saveCalls()).toHaveLength(1);

    // Both names were sent, but nothing is claimed before the server says what it holds.
    expect(retry).toHaveBeenCalledTimes(1);
    expect(savedMarks()).toHaveLength(0);
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(addButton()).toBeNull();
    expect(warning()).toBeNull();

    // The server saved only A (the worker left B out).
    identitySettles(savedEmail([A]));

    await waitFor(() => expect(stateOf(A)).toBe('saved'));
    expect(stateOf(B)).toBe('unsaved');
    expect(warning()).toBeInTheDocument();
    expect(addButton()).toBeInTheDocument();
  });

  it('negative (finding 5): no savedAttachmentNames from the server → no Save-to-add, even right after its own save', async () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} initial={undefined} />);

    await firstSaveOfTheEmail();
    identitySettles(savedEmail(undefined));

    await waitFor(() => expect(screen.getByText(A)).toBeInTheDocument());
    expect(savedMarks()).toHaveLength(0);
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(addButton()).toBeNull();
    expect(warning()).toBeNull();
  });

  it('negative: a re-read that settles on another document marks nothing', async () => {
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} initial={undefined} />);

    await firstSaveOfTheEmail();
    identitySettles({
      ...(savedEmail([A, B]) as object),
      documentId: '99999999-0000-4000-8000-000000000099',
    } as DocumentIdentityState);

    await waitFor(() => expect(retry).toHaveBeenCalledTimes(1));
    expect(savedMarks()).toHaveLength(0);
    expect(addButton()).toBeNull();
  });

  it('an add shows what the server says it is adding as "Adding…" (never saved), never what was ticked', async () => {
    mockFetch.mockImplementation(async (url: string) =>
      String(url).includes('/api/office/save')
        ? accepted({ attachmentsAdded: [B] })
        : String(url).includes('/api/office/jobs/')
          ? response(200, { jobId: 'job-141', status: 'Completed', completedPhases: [] })
          : response(200, { data: {} })
    );
    render(<OutlookPane attachments={[pdf(A), pdf(B), pdf(C)]} initial={savedEmail([A])} />);

    tick(B);
    tick(C);
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(saveCalls()).toHaveLength(1));
    expect(JSON.parse(String((saveCalls()[0]![1] as RequestInit).body)).email.selectedAttachmentFileNames).toEqual([
      B,
      C,
    ]);

    // Task 141 review F2: B is queued, not saved — "Adding…" until the add's job finished and the identity says so.
    await waitFor(() => expect(stateOf(B)).toBe('adding'));
    expect(stateOf(C)).toBe('unsaved');
    expect(warning()).toBeInTheDocument();

    // The re-read right after the save lands before the worker has created B: B stays "Adding…", never saved.
    expect(retry).toHaveBeenCalledTimes(1);
    identitySettles(savedEmail([A]));
    await waitFor(() => expect(stateOf(C)).toBe('unsaved'));
    expect(stateOf(A)).toBe('saved');
    expect(stateOf(B)).toBe('adding');
  });

  it('finding 3: an attachment no save creates shows "Can\'t be saved", cannot be ticked, and raises no warning', () => {
    render(<OutlookPane attachments={[pdf(A), invite]} initial={savedEmail([A])} />);

    expect(stateOf(A)).toBe('saved');
    expect(stateOf(INVITE)).toBe('notSaveable');
    expect(screen.getByText("Can't be saved")).toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: `Select ${INVITE}` })).toBeNull();
    expect(warning()).toBeNull();
    expect(addButton()).toBeNull();
  });

  it('finding 3: a name the server reports not saveable is shown so, with a notice, and the warning clears', async () => {
    mockFetch.mockImplementation(async (url: string) =>
      String(url).includes('/api/office/save')
        ? response(200, {
            success: true,
            duplicate: true,
            artifact: { type: 'Document', id: EML_ID },
            attachmentsNotSaveable: [B],
          })
        : response(200, { data: {} })
    );
    render(<OutlookPane attachments={[pdf(A), pdf(B)]} initial={savedEmail([A])} />);
    expect(warning()).toBeInTheDocument();

    tick(B);
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(stateOf(B)).toBe('notSaveable'));
    expect(savedMarks()).toHaveLength(1);
    expect(screen.getByText(/can't be saved to Spaarke/i)).toBeInTheDocument();
    expect(warning()).toBeNull();
    expect(addButton()).toBeNull();
  });
});
