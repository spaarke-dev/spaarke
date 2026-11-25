/**
 * useDocumentProfile — following a Generate Profile job to its end (spaarkeai-word-add-in-r1 task 137, GitHub #1090).
 *
 * Before: after the 202 the hook re-read the document ONCE, at once, which landed before the queued job had started, so
 * the pane showed the status from before the click (Failed, say) and never read again. Now it re-reads every
 * PROFILE_POLL_INTERVAL_MS until the status leaves Pending, up to PROFILE_POLL_CAP_MS, and stops on unmount or a document
 * change. The server writes Pending before it answers 202 (contract tests: OfficeGenerateProfilePendingContractTests),
 * so no read here returns the old status as this request's result.
 *
 * Fake timers drive the clock; the network is the mocked apiClient, as in DocumentProfileSection.test.tsx.
 */

import { act, renderHook } from '@testing-library/react';
import { apiClient, ApiClientError } from '@shared/services';
import { PROFILE_POLL_CAP_MS, PROFILE_POLL_INTERVAL_MS, useDocumentProfile } from '../useDocumentProfile';

jest.mock('@shared/services', () => {
  const actual = jest.requireActual('@shared/services');
  return {
    ...actual,
    apiClient: {
      get: jest.fn(),
      post: jest.fn(),
      put: jest.fn(),
      delete: jest.fn(),
      uploadFile: jest.fn(),
      configure: jest.fn(),
    },
  };
});

const mockGet = apiClient.get as jest.Mock;
const mockPost = apiClient.post as jest.Mock;

const DOC = '11111111-1111-1111-1111-111111111111';
const OTHER_DOC = '22222222-2222-2222-2222-222222222222';

const FAILED = 100000004;
const PENDING = 100000001;
const COMPLETED = 100000002;

const read = (summaryStatus: number, extra: Record<string, unknown> = {}) => ({ data: { summaryStatus, ...extra } });
const completedRead = () =>
  read(COMPLETED, { summary: 'The new summary.', tldr: 'New TL;DR.', keywords: 'a, b', documentType: 'NDA' });

/** Lets the pending promise chain settle, then moves the fake clock. */
async function advance(ms: number) {
  await act(async () => {
    await jest.advanceTimersByTimeAsync(ms);
  });
}

async function settle() {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('useDocumentProfile — following a Generate Profile job (task 137, #1090)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    mockGet.mockReset();
    mockPost.mockReset();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  async function renderOnAFailedDocument(documentId = DOC) {
    mockGet.mockResolvedValueOnce(read(FAILED)); // the first read: the status from before the click
    const view = renderHook(({ id }: { id: string }) => useDocumentProfile(id), { initialProps: { id: documentId } });
    await settle();
    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Failed' });
    return view;
  }

  it('after a 202 on a Failed document shows Pending, keeps re-reading, and ends on the job’s own Completed', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet
      .mockResolvedValueOnce(read(PENDING)) // 3 s: the job is still running
      .mockResolvedValueOnce(read(PENDING)) // 6 s
      .mockResolvedValueOnce(completedRead()); // 9 s: the job's result

    await act(async () => {
      await view.result.current.generateProfile();
    });

    // 202: Pending at once, and no read yet — the old status is never shown as this request's result.
    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Pending' });
    expect(mockGet).toHaveBeenCalledTimes(1);

    await advance(PROFILE_POLL_INTERVAL_MS);
    expect(mockGet).toHaveBeenCalledTimes(2);
    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Pending' });

    await advance(PROFILE_POLL_INTERVAL_MS);
    expect(mockGet).toHaveBeenCalledTimes(3);
    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Pending' });

    await advance(PROFILE_POLL_INTERVAL_MS);
    expect(mockGet).toHaveBeenCalledTimes(4);
    expect(view.result.current.outcome).toMatchObject({ kind: 'completed', summary: 'The new summary.' });
  });

  it('stops reading once the status is terminal', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet.mockResolvedValueOnce(completedRead());

    await act(async () => {
      await view.result.current.generateProfile();
    });
    await advance(PROFILE_POLL_INTERVAL_MS);
    expect(view.result.current.outcome.kind).toBe('completed');

    await advance(PROFILE_POLL_CAP_MS);
    expect(mockGet).toHaveBeenCalledTimes(2); // the first read, and the one that found Completed
  });

  it('shows a job that failed as Failed, and stops', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet.mockResolvedValueOnce(read(PENDING)).mockResolvedValueOnce(read(FAILED));

    await act(async () => {
      await view.result.current.generateProfile();
    });
    await advance(PROFILE_POLL_INTERVAL_MS);
    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Pending' });
    await advance(PROFILE_POLL_INTERVAL_MS);
    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Failed' });

    await advance(PROFILE_POLL_CAP_MS);
    expect(mockGet).toHaveBeenCalledTimes(3);
  });

  it('gives up at the cap, leaves the last status (Pending) on screen, and never shows Completed without a Completed read', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet.mockResolvedValue(read(PENDING)); // the job never finishes

    await act(async () => {
      await view.result.current.generateProfile();
    });
    await advance(PROFILE_POLL_CAP_MS + PROFILE_POLL_INTERVAL_MS);

    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Pending' });
    const readsAtTheCap = mockGet.mock.calls.length;
    expect(readsAtTheCap).toBeGreaterThan(10); // it did keep reading
    expect(readsAtTheCap).toBeLessThanOrEqual(1 + PROFILE_POLL_CAP_MS / PROFILE_POLL_INTERVAL_MS + 1);

    await advance(PROFILE_POLL_CAP_MS); // and then it stopped
    expect(mockGet).toHaveBeenCalledTimes(readsAtTheCap);
    expect(view.result.current.outcome.kind).not.toBe('completed');
  });

  it('keeps Pending through a failed re-read and carries on', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet.mockRejectedValueOnce(new Error('network down')).mockResolvedValueOnce(completedRead());

    await act(async () => {
      await view.result.current.generateProfile();
    });
    await advance(PROFILE_POLL_INTERVAL_MS);
    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Pending' }); // not an error, not Completed

    await advance(PROFILE_POLL_INTERVAL_MS);
    expect(view.result.current.outcome.kind).toBe('completed');
  });

  it('stops reading when the pane unmounts', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet.mockResolvedValue(read(PENDING));

    await act(async () => {
      await view.result.current.generateProfile();
    });
    await advance(PROFILE_POLL_INTERVAL_MS);
    const reads = mockGet.mock.calls.length;

    view.unmount();
    await advance(PROFILE_POLL_CAP_MS);

    expect(mockGet).toHaveBeenCalledTimes(reads);
  });

  it('stops reading the old document when the document changes, and does not resume if it comes back', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockResolvedValueOnce({ documentId: DOC });
    mockGet.mockResolvedValue(read(PENDING));

    await act(async () => {
      await view.result.current.generateProfile();
    });
    await advance(PROFILE_POLL_INTERVAL_MS);
    mockGet.mockClear();

    view.rerender({ id: OTHER_DOC });
    await settle();
    expect(mockGet).toHaveBeenCalledTimes(1); // the new document's own first read
    expect(mockGet).toHaveBeenLastCalledWith(`/api/v1/documents/${OTHER_DOC}`);

    await advance(PROFILE_POLL_CAP_MS);
    expect(mockGet).toHaveBeenCalledTimes(1); // nothing polls: the wait for the first document ended

    view.rerender({ id: DOC });
    await settle();
    mockGet.mockClear();
    await advance(PROFILE_POLL_CAP_MS);
    expect(mockGet).not.toHaveBeenCalled(); // back on the first document: only its first read, no resumed wait
  });

  it('a refused Generate Profile (4xx) does not start polling', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockRejectedValueOnce(
      new ApiClientError({ type: 'about:blank', title: 'Forbidden', status: 403, detail: 'No write access.' })
    );

    let accepted: boolean | undefined;
    await act(async () => {
      accepted = await view.result.current.generateProfile();
    });
    await advance(PROFILE_POLL_CAP_MS);

    expect(accepted).toBe(false);
    expect(view.result.current.outcome).toEqual({ kind: 'status', status: 'Failed' }); // untouched
    expect(mockGet).toHaveBeenCalledTimes(1); // the first read only
  });

  it('a second Generate Profile restarts the wait with a fresh cap', async () => {
    const view = await renderOnAFailedDocument();
    mockPost.mockResolvedValue({ documentId: DOC });
    mockGet.mockResolvedValue(read(PENDING));

    await act(async () => {
      await view.result.current.generateProfile();
    });
    await advance(PROFILE_POLL_CAP_MS - PROFILE_POLL_INTERVAL_MS); // nearly at the first cap

    await act(async () => {
      await view.result.current.generateProfile();
    });
    mockGet.mockClear();
    await advance(PROFILE_POLL_CAP_MS / 2);

    expect(mockGet.mock.calls.length).toBeGreaterThan(5); // still reading, well past where the first wait would have ended
  });

  it('a 202 that arrives after the user moved to another document changes nothing on screen and starts no wait', async () => {
    const view = await renderOnAFailedDocument();
    let resolvePost: (value: unknown) => void = () => {
      throw new Error('resolvePost called before it was assigned');
    };
    mockPost.mockImplementationOnce(
      () =>
        new Promise(resolve => {
          resolvePost = resolve;
        })
    );

    let request: Promise<boolean> = Promise.resolve(false);
    act(() => {
      request = view.result.current.generateProfile(); // the request for DOC is in flight
    });

    mockGet.mockResolvedValueOnce(completedRead()); // the other document's own first read
    view.rerender({ id: OTHER_DOC });
    await settle();
    expect(view.result.current.outcome.kind).toBe('completed');

    await act(async () => {
      resolvePost({ documentId: DOC });
      await request;
    });

    expect(view.result.current.outcome.kind).toBe('completed'); // not "Pending": that is DOC's job, not this document's
    mockGet.mockClear();
    await advance(PROFILE_POLL_CAP_MS);
    expect(mockGet).not.toHaveBeenCalled();
  });
});
