/**
 * Task 141 review (round 2): what finishes an add's watch, and what does not.
 *
 * - ONLY a final phase finishes it: `AttachmentsAdded`, `AttachmentsIncomplete`, or a job status of Failed / Cancelled. A phase
 *   that is not final (including one this build does not know) keeps the watch reading.
 * - Reaching the read cap, or three failed reads in a row, WITHOUT a final phase is not an outcome: the watch reports
 *   `onAttachmentsAddUnconfirmed` (nothing is known) and never `onAttachmentsAddFinished`, so the pane cannot call the names
 *   added or failed.
 */
import React from 'react';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useSaveFlow, ATTACHMENTS_ADD_WATCH_MAX_POLLS, type SaveFlowContext } from '../useSaveFlow';
import { EmailContentCaptureContext, type CaptureEmailContent } from '../emailContentCaptureContext';
import type { EmailContentCapture } from '../../services/emailContentCapture';
import type { AttachmentInfo } from '@shared/adapters/types';

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
const B = 'Exhibit B.pdf';

const ATTACHMENTS: AttachmentInfo[] = [
  { id: 'att-b', name: B, contentType: 'application/pdf', size: 2048, isInline: false },
];

const addContext = (): SaveFlowContext => ({
  hostType: 'outlook',
  itemId: 'AAMk-item-141',
  internetMessageId: '<CAF0-141-giveup@mail.example.com>',
  itemName: 'RE: Discovery schedule',
  senderEmail: 'opposing@counsel.example',
  attachments: ATTACHMENTS,
  saveTarget: {
    mode: 'addAttachments',
    savedDocumentId: EML_ID,
    record: { entityType: 'Matter', id: MATTER_ID, name: 'Gamma Merger' },
    attachmentFileNames: [B],
  },
});

const json = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});
const accepted = () =>
  json(202, {
    jobId: 'job-141',
    statusUrl: '/api/office/jobs/job-141',
    streamUrl: '/api/office/jobs/job-141/stream',
    status: 'Queued',
    duplicate: false,
    attachmentsAdded: [B],
  });
const job = (currentPhase: string, status = 'Completed') =>
  json(200, {
    jobId: 'job-141',
    status,
    currentPhase,
    completedPhases: [],
    result: { artifact: { type: 'Document', id: EML_ID } },
  });

const capture = jest.fn<Promise<EmailContentCapture>, Parameters<CaptureEmailContent>>(async () => ({
  body: '<p>Please see attached.</p>',
  isBodyHtml: true,
  attachments: [],
  selectedAttachmentFileNames: undefined,
  skipped: [],
}));

const jobReads = () => mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/jobs/')).length;

/**
 * The save answers `accepted()`. The first job read is the save's own tracking (it completes the save); every later read is the
 * watch's, answered by `watchRead(n)` (n = 1 for the watch's first read).
 */
function serve(watchRead: (n: number) => ReturnType<typeof json>) {
  let reads = 0;
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) return accepted();
    if (String(url).includes('/api/office/jobs/')) {
      reads += 1;
      return reads === 1 ? job('AttachmentsQueued') : watchRead(reads - 1);
    }
    return json(200, { data: {} });
  });
}

function renderSaveFlow() {
  const onComplete = jest.fn();
  const onAttachmentsAddFinished = jest.fn();
  const onAttachmentsAddUnconfirmed = jest.fn();
  const wrapper = ({ children }: { children: React.ReactNode }) =>
    React.createElement(EmailContentCaptureContext.Provider, { value: capture }, children);
  const hook = renderHook(
    () =>
      useSaveFlow({
        getAccessToken: jest.fn().mockResolvedValue('t'),
        apiBaseUrl: 'https://bff',
        onComplete,
        onAttachmentsAddFinished,
        onAttachmentsAddUnconfirmed,
        pollingIntervalMs: 5,
      }),
    { wrapper }
  );
  return { ...hook, onComplete, onAttachmentsAddFinished, onAttachmentsAddUnconfirmed };
}

async function startAdd(hook: ReturnType<typeof renderSaveFlow>) {
  await act(async () => {
    await hook.result.current.startSave(addContext());
  });
  await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));
}

// Fake milliseconds waitFor may spend: it advances 50 ms per check and the watch makes one read per check, so the 40-read cap needs about
// 40 checks. Fake time costs nothing real.
const FAKE_TIME_FOR_WATCH = 10_000;

// Fake timers: the add watch polls on a timer (3 s in the pane), and waitFor advances fake time itself, so these tests do not
// depend on how fast the machine runs (a loaded CI runner used to starve the real-time waits).
beforeEach(() => {
  jest.useFakeTimers();
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

afterEach(() => {
  jest.useRealTimers();
});

describe('useSaveFlow — an add watch that stops waiting is not an outcome (task 141 review, round 2)', () => {
  it(`the read cap (${ATTACHMENTS_ADD_WATCH_MAX_POLLS} reads) without a final phase reports unconfirmed, never finished`, async () => {
    serve(() => job('AttachmentsQueued'));
    const hook = renderSaveFlow();
    await startAdd(hook);

    await waitFor(() => expect(hook.onAttachmentsAddUnconfirmed).toHaveBeenCalledTimes(1), {
      timeout: FAKE_TIME_FOR_WATCH,
    });
    expect(hook.onAttachmentsAddUnconfirmed).toHaveBeenCalledWith(EML_ID, [B], 'job-141');
    expect(hook.onAttachmentsAddFinished).not.toHaveBeenCalled();
    expect(jobReads()).toBe(1 + ATTACHMENTS_ADD_WATCH_MAX_POLLS);
  });

  it('three failed reads in a row without a final phase report unconfirmed, never finished', async () => {
    serve(() => json(500, { title: 'boom' }));
    const hook = renderSaveFlow();
    await startAdd(hook);

    await waitFor(() => expect(hook.onAttachmentsAddUnconfirmed).toHaveBeenCalledTimes(1), {
      timeout: FAKE_TIME_FOR_WATCH,
    });
    expect(hook.onAttachmentsAddUnconfirmed).toHaveBeenCalledWith(EML_ID, [B], 'job-141');
    expect(hook.onAttachmentsAddFinished).not.toHaveBeenCalled();
    expect(jobReads()).toBe(1 + 3);
  });

  it('a failed read that is followed by a good one does not count toward the three', async () => {
    serve(n => (n <= 2 || n === 4 ? json(500, {}) : n === 3 ? job('AttachmentsQueued') : job('AttachmentsAdded')));
    const hook = renderSaveFlow();
    await startAdd(hook);

    await waitFor(() => expect(hook.onAttachmentsAddFinished).toHaveBeenCalledTimes(1));
    expect(hook.onAttachmentsAddUnconfirmed).not.toHaveBeenCalled();
  });

  it('watchAttachmentsAdd resumes a job the pane kept (a watch that stopped waiting can be started again)', async () => {
    serve(() => job('AttachmentsQueued'));
    const hook = renderSaveFlow();
    await startAdd(hook);
    await waitFor(() => expect(hook.onAttachmentsAddUnconfirmed).toHaveBeenCalledTimes(1), {
      timeout: FAKE_TIME_FOR_WATCH,
    });

    serve(() => job('AttachmentsAdded'));
    act(() => hook.result.current.watchAttachmentsAdd('job-141', EML_ID, [B]));

    await waitFor(() => expect(hook.onAttachmentsAddFinished).toHaveBeenCalledWith(EML_ID, [B]));
  });
});

describe('useSaveFlow — only a final phase finishes an add watch (task 141 review, round 2)', () => {
  it('a phase that is neither final nor AttachmentsQueued keeps the watch reading', async () => {
    serve(n => (n <= 3 ? job('SomePhaseAddedLater') : job('AttachmentsAdded')));
    const hook = renderSaveFlow();
    await startAdd(hook);

    await waitFor(() => expect(hook.onAttachmentsAddFinished).toHaveBeenCalledTimes(1));
    expect(hook.onAttachmentsAddFinished).toHaveBeenCalledWith(EML_ID, [B]);
    // The unknown phase was read three times before the final one: it did not finish the watch.
    expect(jobReads()).toBe(1 + 4);
  });

  it('a job with no phase at all keeps the watch reading', async () => {
    serve(n => (n <= 2 ? json(200, { jobId: 'job-141', status: 'Running' }) : job('AttachmentsIncomplete')));
    const hook = renderSaveFlow();
    await startAdd(hook);

    await waitFor(() => expect(hook.onAttachmentsAddFinished).toHaveBeenCalledTimes(1));
    expect(jobReads()).toBe(1 + 3);
  });

  it.each(['Failed', 'Cancelled'])('a job status of %s finishes it, whatever the phase', async status => {
    serve(() => job('AttachmentsQueued', status));
    const hook = renderSaveFlow();
    await startAdd(hook);

    await waitFor(() => expect(hook.onAttachmentsAddFinished).toHaveBeenCalledWith(EML_ID, [B]));
    expect(hook.onAttachmentsAddUnconfirmed).not.toHaveBeenCalled();
  });
});
