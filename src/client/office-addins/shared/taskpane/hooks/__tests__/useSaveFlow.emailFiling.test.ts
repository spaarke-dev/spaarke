/**
 * Task 131 (#2171): the save worked, but the email it reconciled to could not be filed to the chosen record. The server
 * says so in `SaveResponse.emailFiling` (`"NotPermitted"` / `"Failed"`); `useSaveFlow` carries it to `onComplete`'s third
 * argument so the pane can tell the user. Filed / AlreadyFiled / absent / a value from a newer server pass NOTHING extra:
 * `onComplete` keeps exactly the two arguments it always had.
 */
import { renderHook, act, waitFor } from '@testing-library/react';
import { useSaveFlow, type SaveFlowContext } from '../useSaveFlow';

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

const emailContext: SaveFlowContext = {
  hostType: 'outlook',
  itemId: 'email-123',
  itemName: 'Test Email',
  attachments: [],
};

const json = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});

/** The save is accepted with `saveBody`'s extra fields; the job poll answers Completed with a document. */
function serverAnswers(saveBody: Record<string, unknown>) {
  mockFetch.mockImplementation(async (url: string) =>
    String(url).includes('/api/office/save')
      ? json(202, {
          jobId: 'job-1',
          statusUrl: '/api/office/jobs/job-1',
          streamUrl: '/api/office/jobs/job-1/stream',
          status: 'Queued',
          duplicate: false,
          ...saveBody,
        })
      : json(200, {
          jobId: 'job-1',
          status: 'Completed',
          completedPhases: [],
          result: { artifact: { type: 'Document', id: DOCUMENT_ID } },
        })
  );
}

const getAccessToken = jest.fn().mockResolvedValue('token');

async function saveAndComplete() {
  const onComplete = jest.fn();
  const hook = renderHook(() =>
    useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff', onComplete })
  );
  await act(async () => {
    await hook.result.current.startSave(emailContext);
  });
  await waitFor(() => expect(hook.result.current.flowState).toBe('complete'));
  return { ...hook, onComplete };
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('useSaveFlow — the email filing outcome (task 131)', () => {
  it.each([['NotPermitted'], ['Failed']])(
    'emailFiling %s is handed to onComplete as its third argument',
    async outcome => {
      serverAnswers({ emailFiling: outcome });

      const { onComplete } = await saveAndComplete();

      expect(onComplete).toHaveBeenCalledTimes(1);
      expect(onComplete).toHaveBeenCalledWith(DOCUMENT_ID, '', outcome);
    }
  );

  it.each([['Filed'], ['AlreadyFiled'], ['SomethingNewer'], [42]])(
    'emailFiling %s passes nothing extra: onComplete keeps its two arguments',
    async outcome => {
      serverAnswers({ emailFiling: outcome });

      const { onComplete } = await saveAndComplete();

      expect(onComplete).toHaveBeenCalledTimes(1);
      expect(onComplete.mock.calls[0]).toEqual([DOCUMENT_ID, '']);
    }
  );

  it('an absent emailFiling (every response before task 131) passes nothing extra', async () => {
    serverAnswers({});

    const { onComplete } = await saveAndComplete();

    expect(onComplete.mock.calls[0]).toEqual([DOCUMENT_ID, '']);
  });

  it('a later save with no problem does not inherit the earlier save notice', async () => {
    serverAnswers({ emailFiling: 'NotPermitted' });
    const onComplete = jest.fn();
    const { result } = renderHook(() =>
      useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff', onComplete })
    );
    await act(async () => {
      await result.current.startSave(emailContext);
    });
    await waitFor(() => expect(onComplete).toHaveBeenCalledTimes(1));
    expect(onComplete.mock.calls[0]).toEqual([DOCUMENT_ID, '', 'NotPermitted']);

    serverAnswers({});
    await act(async () => {
      await result.current.startSave(emailContext);
    });
    await waitFor(() => expect(onComplete).toHaveBeenCalledTimes(2));
    expect(onComplete.mock.calls[1]).toEqual([DOCUMENT_ID, '']);
  });
});
