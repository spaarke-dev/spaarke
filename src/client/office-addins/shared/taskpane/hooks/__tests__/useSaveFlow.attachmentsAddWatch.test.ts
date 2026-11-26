/**
 * Task 141 review (F2): `useSaveFlow` watches an add-attachments save's job after the save completed (it completes when the
 * add is QUEUED), and calls `onAttachmentsAddFinished(documentId, names)` once the job's worker has finished — its status
 * read leaves phase `AttachmentsQueued` — with the names the add's answer said it queued.
 *
 * - not before the worker finished, and once only;
 * - negatives: not for a save that is not an add, not for an add that queued nothing, and not after reset().
 */
import React from 'react';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useSaveFlow, type SaveFlowContext } from '../useSaveFlow';
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
  internetMessageId: '<CAF0-141-watch@mail.example.com>',
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
const accepted = (extra: Record<string, unknown> = {}) =>
  json(202, {
    jobId: 'job-141',
    statusUrl: '/api/office/jobs/job-141',
    streamUrl: '/api/office/jobs/job-141/stream',
    status: 'Queued',
    duplicate: false,
    ...extra,
  });
const jobAt = (currentPhase: string) =>
  json(200, {
    jobId: 'job-141',
    status: 'Completed',
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

/** The save answers with `answer`; the job reads in turn return `phases` (the last one repeats). */
function serve(answer: ReturnType<typeof json>, phases: string[]) {
  let reads = 0;
  mockFetch.mockImplementation(async (url: string) => {
    if (String(url).includes('/api/office/save')) return answer;
    if (String(url).includes('/api/office/jobs/')) {
      const phase = phases[Math.min(reads, phases.length - 1)]!;
      reads += 1;
      return jobAt(phase);
    }
    return json(200, { data: {} });
  });
}

function renderSaveFlow() {
  const onComplete = jest.fn();
  const onAttachmentsAddFinished = jest.fn();
  const wrapper = ({ children }: { children: React.ReactNode }) =>
    React.createElement(EmailContentCaptureContext.Provider, { value: capture }, children);
  const hook = renderHook(
    () =>
      useSaveFlow({
        getAccessToken: jest.fn().mockResolvedValue('t'),
        apiBaseUrl: 'https://bff',
        onComplete,
        onAttachmentsAddFinished,
        pollingIntervalMs: 20,
      }),
    { wrapper }
  );
  return { ...hook, onComplete, onAttachmentsAddFinished };
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('useSaveFlow — watching an add until its job finished (task 141 review F2)', () => {
  it('reports the add finished, with its queued names, only once the job leaves AttachmentsQueued', async () => {
    serve(accepted({ attachmentsAdded: [B] }), [
      'AttachmentsQueued', // the save's own tracking: completes the save
      'AttachmentsQueued', // the watch: the worker has not finished (reads 20 ms apart)
      'AttachmentsQueued',
      'AttachmentsQueued',
      'AttachmentsQueued',
      'AttachmentsQueued',
      'AttachmentsQueued',
      'AttachmentsIncomplete', // the worker finished (and left a name out)
    ]);
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext());
    });
    await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));
    expect(hook.onAttachmentsAddFinished).not.toHaveBeenCalled();

    await waitFor(() => expect(hook.onAttachmentsAddFinished).toHaveBeenCalledTimes(1));
    expect(hook.onAttachmentsAddFinished).toHaveBeenCalledWith(EML_ID, [B]);
    expect(jobReads()).toBeGreaterThanOrEqual(8);

    // Once only: no more reads after it finished.
    const reads = jobReads();
    await new Promise(resolve => setTimeout(resolve, 80));
    expect(jobReads()).toBe(reads);
    expect(hook.onAttachmentsAddFinished).toHaveBeenCalledTimes(1);
  });

  it("an add whose worker finished before the watch's first read is reported straight away", async () => {
    serve(accepted({ attachmentsAdded: [B] }), ['AttachmentsQueued', 'AttachmentsAdded']);
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext());
    });

    await waitFor(() => expect(hook.onAttachmentsAddFinished).toHaveBeenCalledWith(EML_ID, [B]));
  });

  it('negative: an add that queued nothing is not watched', async () => {
    serve(accepted(), ['AttachmentsQueued']);
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext());
    });
    await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));
    const reads = jobReads();
    await new Promise(resolve => setTimeout(resolve, 80));

    expect(jobReads()).toBe(reads);
    expect(hook.onAttachmentsAddFinished).not.toHaveBeenCalled();
  });

  it('negative: a save that is not an add is not watched', async () => {
    serve(accepted({ attachmentsAdded: [B] }), ['Complete']);
    const hook = renderSaveFlow();
    const { saveTarget: _add, ...plain } = addContext();

    await act(async () => {
      await hook.result.current.startSave({ ...plain, attachments: [] });
    });
    await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));
    await new Promise(resolve => setTimeout(resolve, 80));

    expect(hook.onAttachmentsAddFinished).not.toHaveBeenCalled();
  });

  it('negative: reset() stops the watch', async () => {
    serve(accepted({ attachmentsAdded: [B] }), [...Array<string>(8).fill('AttachmentsQueued'), 'AttachmentsAdded']);
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext());
    });
    await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));
    act(() => hook.result.current.reset());
    await new Promise(resolve => setTimeout(resolve, 100));

    expect(hook.onAttachmentsAddFinished).not.toHaveBeenCalled();
  });
});
