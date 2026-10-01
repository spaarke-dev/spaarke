/**
 * Task 060 (GitHub #1084): every save the pane tracks reaches a DEFINITE outcome, either success with a document or
 * a visible error, on BOTH the polling path and the SSE path.
 *
 * Defects this pins:
 * - A job reported `Completed` with no document matched neither completion branch, so `cleanup()` ran and the pane sat
 *   on "processing" forever, with no error.
 * - The SSE handler listened for `complete` / `failed` / `stage`, but the server sends `job-complete` / `job-failed` /
 *   `stage-update` (`SseHelper.EventTypes`). Its completion branch could never fire; the pane completed only through
 *   polling.
 * - The server's failure payloads carry `errorMessage`, and the handler read `message`, so a failure showed a generic
 *   text instead of the server's.
 */
import { renderHook, act, waitFor } from '@testing-library/react';
import { useSaveFlow, type SaveFlowContext } from '../useSaveFlow';
import { createSseConnection, type SseEvent } from '../../services/SseClient';

jest.mock('../../services/SseClient', () => ({
  createSseConnection: jest.fn(() => ({ close: jest.fn() })),
}));

const mockFetch = jest.fn();
global.fetch = mockFetch;

Object.defineProperty(global.crypto, 'subtle', {
  configurable: true,
  value: { digest: jest.fn(async () => new Uint8Array(32).buffer) },
});

const DOCUMENT_ID = '8c135b45-5da8-f111-aaab-7ced8ddc4a05';
const VERSION_DOCUMENT_ID = '11111111-2222-3333-4444-555555555555';
const DOCUMENT_URL = 'https://contoso.sharepoint.com/contentstorage/CSP_x/Document%20Library/Brief.docx';

function wordContext(overrides: Partial<SaveFlowContext> = {}): SaveFlowContext {
  return {
    hostType: 'word',
    itemId: DOCUMENT_URL,
    itemName: 'Brief',
    attachments: [],
    documentUrl: DOCUMENT_URL,
    documentContentBase64: 'UEsDBBQAAAAIAAAAIQA=',
    ...overrides,
  };
}

const json = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});

/** The save is accepted; the job poll answers `jobStatus`. */
function serverAnswers(jobStatus: Record<string, unknown>) {
  mockFetch.mockImplementation(async (url: string) =>
    String(url).includes('/api/office/save')
      ? json(202, {
          jobId: 'job-1',
          statusUrl: '/api/office/jobs/job-1',
          streamUrl: '/api/office/jobs/job-1/stream',
          status: 'Queued',
          duplicate: false,
        })
      : json(200, { jobId: 'job-1', completedPhases: [], ...jobStatus })
  );
}

/** The SSE options the hook passed to `createSseConnection`, so a test can deliver the server's events. */
function sseOptions(): { onEvent: (event: SseEvent) => void } {
  const calls = (createSseConnection as jest.Mock).mock.calls;
  expect(calls.length).toBeGreaterThan(0);
  return calls[calls.length - 1][1];
}

const getAccessToken = jest.fn().mockResolvedValue('token');

function renderSaveFlow() {
  const onComplete = jest.fn();
  const onError = jest.fn();
  const hook = renderHook(() =>
    useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff', onComplete, onError })
  );
  return { ...hook, onComplete, onError };
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('useSaveFlow — polling reaches a definite outcome (task 060)', () => {
  it('a create save whose job is Completed with no document ends in a visible error, not an endless "processing"', async () => {
    serverAnswers({ status: 'Completed', currentPhase: 'Complete', progress: 100 });
    const { result, onComplete, onError } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext());
    });

    await waitFor(() => expect(result.current.flowState).toBe('error'));
    expect(result.current.error?.message).toMatch(/could not confirm which document/i);
    expect(onError).toHaveBeenCalledTimes(1);
    expect(onComplete).not.toHaveBeenCalled();
  });

  it('a save the server reports abandoned shows the server’s explanation and can be retried', async () => {
    serverAnswers({
      status: 'Failed',
      currentPhase: 'Abandoned',
      error: {
        code: 'OFFICE_INTERNAL',
        message: 'This save did not finish. Check whether the document was saved, then try again.',
        retryable: true,
      },
    });
    const { result } = renderSaveFlow();

    await act(async () => {
      await result.current.startSave(wordContext());
    });

    await waitFor(() => expect(result.current.flowState).toBe('error'));
    expect(result.current.error?.message).toBe(
      'This save did not finish. Check whether the document was saved, then try again.'
    );
    expect(result.current.error?.recoverable).toBe(true);
  });
});

describe('useSaveFlow — the SSE path uses the server’s event names (task 060)', () => {
  it('job-complete with a document completes the save', async () => {
    serverAnswers({ status: 'Running' });
    const { result, onComplete } = renderSaveFlow();
    await act(async () => {
      await result.current.startSave(wordContext());
    });

    act(() => sseOptions().onEvent({ event: 'job-complete', data: { jobId: 'job-1', documentId: DOCUMENT_ID } }));

    await waitFor(() => expect(result.current.flowState).toBe('complete'));
    expect(result.current.savedDocumentId).toBe(DOCUMENT_ID);
    expect(onComplete).toHaveBeenCalledWith(DOCUMENT_ID, '');
  });

  it('job-complete with no document on a create save ends in a visible error', async () => {
    serverAnswers({ status: 'Running' });
    const { result, onComplete } = renderSaveFlow();
    await act(async () => {
      await result.current.startSave(wordContext());
    });

    act(() => sseOptions().onEvent({ event: 'job-complete', data: { jobId: 'job-1' } }));

    await waitFor(() => expect(result.current.flowState).toBe('error'));
    expect(result.current.error?.message).toMatch(/could not confirm which document/i);
    expect(onComplete).not.toHaveBeenCalled();
  });

  it('job-complete with no document on a VERSION save completes on the document it named (task 024)', async () => {
    serverAnswers({ status: 'Running' });
    const { result } = renderSaveFlow();
    await act(async () => {
      await result.current.startSave(
        wordContext({ saveTarget: { mode: 'version', existingDocumentId: VERSION_DOCUMENT_ID } })
      );
    });

    act(() => sseOptions().onEvent({ event: 'job-complete', data: { jobId: 'job-1' } }));

    await waitFor(() => expect(result.current.flowState).toBe('complete'));
    expect(result.current.savedDocumentId).toBe(VERSION_DOCUMENT_ID);
  });

  it('a poll answer that lands after the stream already completed the save is ignored, so onComplete fires once', async () => {
    // Polling and SSE both track the job. With the SSE completion branch live, an in-flight poll can land after the
    // stream has completed the save.
    let releasePoll: (response: unknown) => void = () => undefined;
    mockFetch.mockImplementation(async (url: string) =>
      String(url).includes('/api/office/save')
        ? json(202, { jobId: 'job-1', statusUrl: '/api/office/jobs/job-1', streamUrl: '/api/office/jobs/job-1/stream' })
        : new Promise(resolve => {
            releasePoll = resolve;
          })
    );
    const { result, onComplete } = renderSaveFlow();
    await act(async () => {
      await result.current.startSave(wordContext());
    });

    act(() => sseOptions().onEvent({ event: 'job-complete', data: { jobId: 'job-1', documentId: DOCUMENT_ID } }));
    await waitFor(() => expect(result.current.flowState).toBe('complete'));
    await act(async () => {
      releasePoll(
        json(200, {
          jobId: 'job-1',
          status: 'Completed',
          completedPhases: [],
          result: { artifact: { type: 'Document', id: DOCUMENT_ID } },
        })
      );
    });

    expect(onComplete).toHaveBeenCalledTimes(1);
    expect(result.current.flowState).toBe('complete');
  });

  it('job-failed shows the server’s errorMessage and honours retryable', async () => {
    serverAnswers({ status: 'Running' });
    const { result } = renderSaveFlow();
    await act(async () => {
      await result.current.startSave(wordContext());
    });

    act(() =>
      sseOptions().onEvent({
        event: 'job-failed',
        data: { jobId: 'job-1', errorCode: 'OFFICE_012', errorMessage: 'Upload to storage failed', retryable: false },
      })
    );

    await waitFor(() => expect(result.current.flowState).toBe('error'));
    expect(result.current.error?.message).toBe('Upload to storage failed');
    expect(result.current.error?.recoverable).toBe(false);
  });
});
