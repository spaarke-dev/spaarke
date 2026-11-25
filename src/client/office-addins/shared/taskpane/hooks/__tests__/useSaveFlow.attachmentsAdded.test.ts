/**
 * Task 138 (#2209): a re-save of an email already saved to the same record, naming attachments that were not saved the
 * first time, adds exactly those under the saved email. The server lists them in `SaveResponse.attachmentsAdded`;
 * `useSaveFlow` carries their count to `onComplete`'s fourth argument so the pane can say what happened. Every other
 * answer (the property absent, empty or malformed) passes nothing extra: `onComplete` keeps the arguments it had.
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
function serverAnswers(saveBody: Record<string, unknown>) {
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
  const hook = renderHook(() =>
    useSaveFlow({ getAccessToken, pollingIntervalMs: 60_000, apiBaseUrl: 'https://bff', onComplete })
  );
  return { ...hook, onComplete };
}

async function save(hook: ReturnType<typeof renderSaveFlow>, calls: number) {
  await act(async () => {
    await hook.result.current.startSave(emailContext);
  });
  await waitFor(() => expect(hook.onComplete).toHaveBeenCalledTimes(calls));
}

beforeEach(() => {
  jest.clearAllMocks();
  sessionStorage.clear();
  mockFetch.mockReset();
});

describe('useSaveFlow — attachments added to an already-saved email (task 138)', () => {
  it('attachmentsAdded is handed to onComplete as a count, in its fourth argument', async () => {
    serverAnswers({ attachmentsAdded: ['Exhibit A.pdf', 'Exhibit B.pdf'] });
    const hook = renderSaveFlow();

    await save(hook, 1);

    expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '', undefined, 2]);
  });

  it('an add alongside an email-filing problem carries both', async () => {
    serverAnswers({ attachmentsAdded: ['Exhibit A.pdf'], emailFiling: 'NotPermitted' });
    const hook = renderSaveFlow();

    await save(hook, 1);

    expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '', 'NotPermitted', 1]);
  });

  it.each([[undefined], [[]], ['Exhibit A.pdf'], [42]])(
    'attachmentsAdded %p passes nothing extra: onComplete keeps its two arguments',
    async value => {
      serverAnswers(value !== undefined ? { attachmentsAdded: value } : {});
      const hook = renderSaveFlow();

      await save(hook, 1);

      expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '']);
    }
  );

  it('a later save that added nothing does not inherit the earlier count', async () => {
    const hook = renderSaveFlow();
    serverAnswers({ attachmentsAdded: ['Exhibit A.pdf'] });
    await save(hook, 1);
    expect(hook.onComplete.mock.calls[0]).toEqual([EML_ID, '', undefined, 1]);

    serverAnswers({});
    await save(hook, 2);

    expect(hook.onComplete.mock.calls[1]).toEqual([EML_ID, '']);
  });
});
