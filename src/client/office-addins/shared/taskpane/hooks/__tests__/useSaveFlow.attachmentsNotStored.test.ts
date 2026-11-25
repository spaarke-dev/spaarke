/**
 * Task 138 (#2209): a re-save that ticked attachments the STORED email does not hold. The server lists them in
 * `SaveResponse.attachmentsNotStored`, on the add answer (202) and on the "already saved" answer (200, duplicate).
 * `useSaveFlow` hands the names to `onComplete`'s fifth argument (add answer) and appends the notice to the duplicate
 * message (already-saved answer). Every other answer passes nothing extra.
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

const EML_ID = '13813813-0138-4138-8138-00000000d0c0';

const emailContext: SaveFlowContext = {
  hostType: 'outlook',
  itemId: 'email-138',
  itemName: 'Settlement',
  attachments: [],
};

const json = (status: number, body: unknown) => ({
  ok: status < 400,
  status,
  text: async () => JSON.stringify(body),
  json: async () => body,
});

/** The save is accepted with `saveBody`'s extra fields; the job poll answers Completed naming the saved .eml. */
function serverAccepts(saveBody: Record<string, unknown>) {
  mockFetch.mockImplementation(async (url: string) =>
    String(url).includes('/api/office/save')
      ? json(202, {
          jobId: 'job-138',
          statusUrl: '/api/office/jobs/job-138',
          streamUrl: '/api/office/jobs/job-138/stream',
          status: 'Queued',
          duplicate: false,
          ...saveBody,
        })
      : json(200, {
          jobId: 'job-138',
          status: 'Completed',
          completedPhases: [],
          result: { artifact: { type: 'Document', id: EML_ID } },
        })
  );
}

const getAccessToken = jest.fn().mockResolvedValue('token');

function renderSaveFlow() {
  const onComplete = jest.fn();
  const onDuplicate = jest.fn();
  const hook = renderHook(() =>
    useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff', onComplete, onDuplicate })
  );
  return { ...hook, onComplete, onDuplicate };
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('useSaveFlow — attachments the stored email does not hold (task 138)', () => {
  it('on an add answer, the names are handed to onComplete as its fifth argument', async () => {
    serverAccepts({ attachmentsAdded: ['Exhibit A.pdf'], attachmentsNotStored: ['Exhibit B.pdf'] });
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(emailContext);
    });
    await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));

    expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '', undefined, 1, ['Exhibit B.pdf']]);
  });

  it.each([[undefined], [[]], ['Exhibit B.pdf'], [42]])(
    'attachmentsNotStored %p passes nothing extra: onComplete keeps its two arguments',
    async value => {
      serverAccepts(value !== undefined ? { attachmentsNotStored: value } : {});
      const hook = renderSaveFlow();

      await act(async () => {
        await hook.result.current.startSave(emailContext);
      });
      await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));

      expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '']);
    }
  );

  it('on the already-saved answer, the notice is appended to the duplicate message', async () => {
    mockFetch.mockImplementation(async () =>
      json(200, { duplicate: true, documentId: EML_ID, attachmentsNotStored: ['Exhibit B.pdf'] })
    );
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(emailContext);
    });

    await waitFor(() => expect(hook.onDuplicate).toHaveBeenCalledTimes(1));
    expect(hook.onDuplicate).toHaveBeenCalledWith(
      EML_ID,
      'This item was previously saved. This attachment could not be added because it was not saved with the email: Exhibit B.pdf. Save the email again as a new copy to include it.'
    );
    expect(hook.result.current.duplicateInfo?.message).toContain('Exhibit B.pdf');
  });

  it('the already-saved answer without the property keeps its own message', async () => {
    mockFetch.mockImplementation(async () => json(200, { duplicate: true, documentId: EML_ID }));
    const hook = renderSaveFlow();

    await act(async () => {
      await hook.result.current.startSave(emailContext);
    });

    await waitFor(() => expect(hook.onDuplicate).toHaveBeenCalledTimes(1));
    expect(hook.onDuplicate).toHaveBeenCalledWith(EML_ID, 'This item was previously saved.');
  });

  it('a later save does not inherit the earlier names', async () => {
    const hook = renderSaveFlow();
    serverAccepts({ attachmentsNotStored: ['Exhibit B.pdf'] });
    await act(async () => {
      await hook.result.current.startSave(emailContext);
    });
    await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(1));
    expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '', undefined, undefined, ['Exhibit B.pdf']]);

    serverAccepts({});
    await act(async () => {
      await hook.result.current.startSave(emailContext);
    });
    await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(2));

    expect(hook.onComplete.mock.calls[1]).toEqual([EML_ID, '']);
  });
});
