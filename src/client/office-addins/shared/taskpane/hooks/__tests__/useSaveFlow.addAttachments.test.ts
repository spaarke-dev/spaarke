/**
 * Task 141: `useSaveFlow`'s add-attachments save (`saveTarget.mode === 'addAttachments'`).
 *
 * - It names the saved `.eml` (`email.savedDocumentId`, canonical), files to the record the `.eml` is filed to
 *   (`targetEntity`, never the picker's selection) and asks for exactly the names it was given.
 * - It reads the email body only: no attachment bytes travel (the server adds children from the STORED `.eml`).
 * - It is refused before anything is sent when it names no attachment or no valid document.
 * - A 200 answer with NO job that names the saved document completes the save directly (nothing to track).
 * - A 202 job that completes without an artifact completes on the saved `.eml` it named.
 * - Its idempotency key is its own: never the key of an ordinary save of the same email.
 */
import React from 'react';
import { renderHook, act, waitFor } from '@testing-library/react';
import { useSaveFlow, buildIdempotencyCanonical, type SaveFlowContext, type SaveRequest } from '../useSaveFlow';
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

const ATTACHMENTS: AttachmentInfo[] = [
  { id: 'att-a', name: 'Exhibit A.pdf', contentType: 'application/pdf', size: 1024, isInline: false },
  { id: 'att-b', name: 'Exhibit B.pdf', contentType: 'application/pdf', size: 2048, isInline: false },
];

const addContext = (names: string[], savedDocumentId = EML_ID): SaveFlowContext => ({
  hostType: 'outlook',
  itemId: 'AAMk-item-141',
  internetMessageId: '<CAF0-141@mail.example.com>',
  itemName: 'RE: Discovery schedule',
  senderEmail: 'opposing@counsel.example',
  attachments: ATTACHMENTS,
  saveTarget: {
    mode: 'addAttachments',
    savedDocumentId,
    record: { entityType: 'Matter', id: MATTER_ID, name: 'Gamma Merger' },
    attachmentFileNames: names,
  },
});

const json = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});

const saveCalls = () => mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/save'));
const sentBody = (index = 0) => JSON.parse(String((saveCalls()[index]![1] as RequestInit).body));

const capture = jest.fn<Promise<EmailContentCapture>, Parameters<CaptureEmailContent>>(async () => ({
  body: '<p>Please see attached.</p>',
  isBodyHtml: true,
  attachments: [],
  selectedAttachmentFileNames: undefined,
  skipped: [],
}));

function renderSaveFlow(withCapture = true) {
  const onComplete = jest.fn();
  const onError = jest.fn();
  const wrapper = ({ children }: { children: React.ReactNode }) =>
    React.createElement(EmailContentCaptureContext.Provider, { value: withCapture ? capture : undefined }, children);
  const hook = renderHook(
    () =>
      useSaveFlow({
        getAccessToken: jest.fn().mockResolvedValue('t'),
        apiBaseUrl: 'https://bff',
        onComplete,
        onError,
        pollingIntervalMs: 60_000,
      }),
    { wrapper }
  );
  return { ...hook, onComplete, onError };
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('useSaveFlow — add attachments to a saved email (task 141)', () => {
  it('names the saved .eml, files to its record and sends exactly the given names, with the body and no attachment bytes', async () => {
    // A stale picker selection must not be the target.
    sessionStorage.setItem(
      'spaarke-last-association',
      JSON.stringify({
        id: 'dddddddd-0000-0000-0000-00000000000d',
        entityType: 'Project',
        logicalName: 'sprk_project',
        name: 'Other',
      })
    );
    mockFetch.mockResolvedValue(
      json(200, { success: true, duplicate: true, artifact: { type: 'Document', id: EML_ID } })
    );
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext(['Exhibit B.pdf']));
    });

    expect(saveCalls()).toHaveLength(1);
    const body = sentBody();
    expect(body.contentType).toBe('Email');
    expect(body.targetEntity).toEqual({ entityType: 'Matter', entityId: MATTER_ID, displayName: 'Gamma Merger' });
    expect(body.email.savedDocumentId).toBe(EML_ID);
    expect(body.email.selectedAttachmentFileNames).toEqual(['Exhibit B.pdf']);
    expect(body.email.body).toBe('<p>Please see attached.</p>');
    expect(body.email).not.toHaveProperty('attachments');
    // The body only: the capture is asked for no attachment at all.
    expect(capture).toHaveBeenCalledTimes(1);
    const [captureAttachments, captureSelected] = capture.mock.calls[0] as unknown as [unknown[], Set<string>];
    expect(captureAttachments).toEqual([]);
    expect(captureSelected.size).toBe(0);
    expect(hook.result.current.getSentAttachmentFileNames()).toEqual(['Exhibit B.pdf']);
  });

  it('a 200 answer with no job that names the saved .eml completes directly — no job is polled', async () => {
    mockFetch.mockResolvedValue(
      json(200, { success: true, duplicate: true, artifact: { type: 'Document', id: EML_ID } })
    );
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext(['Exhibit B.pdf']));
    });

    expect(hook.result.current.flowState).toBe('complete');
    expect(hook.result.current.savedDocumentId).toBe(EML_ID);
    expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '']);
    expect(mockFetch.mock.calls.filter(([url]) => String(url).includes('/api/office/jobs/'))).toHaveLength(0);
  });

  it('a 200 answer naming names the stored email does not hold passes them on (fifth argument)', async () => {
    mockFetch.mockResolvedValue(
      json(200, {
        success: true,
        duplicate: true,
        artifact: { type: 'Document', id: EML_ID },
        attachmentsNotStored: ['Exhibit B.pdf'],
      })
    );
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext(['Exhibit B.pdf']));
    });

    expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '', undefined, undefined, ['Exhibit B.pdf']]);
  });

  it('a 202 add whose job completes without an artifact completes on the saved .eml it named', async () => {
    mockFetch.mockImplementation(async (url: string) =>
      String(url).includes('/api/office/save')
        ? json(202, {
            jobId: 'job-141',
            statusUrl: '/api/office/jobs/job-141',
            streamUrl: '/api/office/jobs/job-141/stream',
            status: 'Queued',
            duplicate: false,
            attachmentsAdded: ['Exhibit B.pdf'],
          })
        : json(200, { jobId: 'job-141', status: 'Completed', completedPhases: [] })
    );
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext(['Exhibit B.pdf']));
    });

    await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));
    expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '', undefined, 1]);
  });

  it('negative: an add naming no attachment is refused before anything is sent', async () => {
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext([]));
    });

    expect(saveCalls()).toHaveLength(0);
    expect(hook.result.current.flowState).toBe('error');
    expect(hook.onComplete).not.toHaveBeenCalled();
  });

  it('negative: an add whose saved document is not a GUID is refused before anything is sent', async () => {
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(addContext(['Exhibit B.pdf'], 'not-a-guid'));
    });

    expect(saveCalls()).toHaveLength(0);
    expect(hook.result.current.flowState).toBe('error');
  });

  it('an ordinary email save sends no savedDocumentId (unchanged)', async () => {
    mockFetch.mockResolvedValue(
      json(200, { success: true, duplicate: true, artifact: { type: 'Document', id: EML_ID } })
    );
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave({ ...addContext(['Exhibit B.pdf']), saveTarget: { mode: 'create' } });
    });

    expect(sentBody().email).not.toHaveProperty('savedDocumentId');
  });
});

describe('buildIdempotencyCanonical — an add has its own key (task 141)', () => {
  const request: SaveRequest = {
    sourceType: 'OutlookAttachment',
    associationType: 'Matter',
    associationId: MATTER_ID,
    content: { emailId: 'AAMk-item-141', includeBody: true, attachmentIds: ['att-b'] },
    processing: { profileSummary: true, ragIndex: true, deepAnalysis: false },
  };

  it('every other save keeps its canonical string unchanged', () => {
    expect(buildIdempotencyCanonical(request)).not.toContain('addAttachments');
  });

  it('an add differs from an ordinary save of the same email, record and ticks, and per failed attempt', () => {
    const plain = buildIdempotencyCanonical(request);
    const add = buildIdempotencyCanonical(request, undefined, {
      savedDocumentId: EML_ID,
      attachmentFileNames: ['Exhibit B.pdf'],
      failedAttempts: 0,
    });
    const retried = buildIdempotencyCanonical(request, undefined, {
      savedDocumentId: EML_ID,
      attachmentFileNames: ['Exhibit B.pdf'],
      failedAttempts: 1,
    });
    expect(add).not.toBe(plain);
    expect(add).toContain(EML_ID);
    expect(retried).not.toBe(add);
  });
});
