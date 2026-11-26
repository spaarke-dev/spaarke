/**
 * queryDirtyState (task 195): the parent side of the dirty-check protocol, extracted from RecordNavigationModalShell so a modal that
 * composes SprkModal directly can run it from `nav.onBeforeNavigate`. The shell's own suite covers it through the shell (unchanged); this
 * covers the function and its one addition, `requireSourceMatch` (off by default, so the shell behaves as before).
 */

import { DIRTY_CHECK_REQUEST_TYPE, DIRTY_CHECK_RESULT_TYPE, queryDirtyState } from '..';

const SAME = 'http://localhost';
const trusted = (origin: string) => origin === SAME;

function frameWindow() {
  return { postMessage: jest.fn() } as unknown as Window & { postMessage: jest.Mock };
}

function answer(
  target: { postMessage: jest.Mock },
  dirty: boolean,
  opts: { origin?: string; source?: unknown; correlationId?: string } = {}
) {
  const request = target.postMessage.mock.calls[0][0] as { correlationId: string };
  window.dispatchEvent(
    new MessageEvent('message', {
      data: { type: DIRTY_CHECK_RESULT_TYPE, correlationId: opts.correlationId ?? request.correlationId, dirty },
      origin: opts.origin ?? SAME,
      source: ('source' in opts ? opts.source : target) as MessageEventSource,
    })
  );
}

afterEach(() => jest.useRealTimers());

describe('queryDirtyState', () => {
  it('posts a request with a correlationId to the target at the pinned origin and resolves with the answer', async () => {
    const target = frameWindow();
    const result = queryDirtyState({ targetWindow: target, targetOrigin: SAME, isOriginTrusted: trusted });
    expect(target.postMessage).toHaveBeenCalledWith(
      { type: DIRTY_CHECK_REQUEST_TYPE, correlationId: expect.any(String) },
      SAME
    );
    answer(target, true);
    await expect(result).resolves.toBe(true);
  });

  it('resolves clean with no window (no round trip)', async () => {
    await expect(queryDirtyState({ targetWindow: null, isOriginTrusted: trusted })).resolves.toBe(false);
    await expect(queryDirtyState({ targetWindow: undefined, isOriginTrusted: trusted })).resolves.toBe(false);
  });

  it('resolves clean when the frame does not answer within the timeout (1000 ms by default)', async () => {
    jest.useFakeTimers();
    const target = frameWindow();
    let settled: boolean | null = null;
    void queryDirtyState({ targetWindow: target, isOriginTrusted: trusted }).then(v => (settled = v));
    await jest.advanceTimersByTimeAsync(999);
    expect(settled).toBeNull();
    await jest.advanceTimersByTimeAsync(1);
    expect(settled).toBe(false);
  });

  it('resolves clean when the post throws (a detached frame)', async () => {
    const target = frameWindow();
    target.postMessage.mockImplementation(() => {
      throw new Error('detached');
    });
    await expect(queryDirtyState({ targetWindow: target, isOriginTrusted: trusted })).resolves.toBe(false);
  });

  it('ignores an untrusted origin and a wrong correlationId (a dirty answer then times out clean)', async () => {
    jest.useFakeTimers();
    const target = frameWindow();
    const result = queryDirtyState({ targetWindow: target, isOriginTrusted: trusted });
    answer(target, true, { origin: 'https://evil.example.com' });
    answer(target, true, { correlationId: 'other' });
    await jest.advanceTimersByTimeAsync(1000);
    await expect(result).resolves.toBe(false);
  });

  it("requireSourceMatch: an answer from another window is ignored; the frame's own is taken", async () => {
    jest.useFakeTimers();
    const target = frameWindow();
    const other = frameWindow();
    const result = queryDirtyState({ targetWindow: target, isOriginTrusted: trusted, requireSourceMatch: true });
    answer(target, true, { source: other });
    answer(target, true, { source: null });
    await jest.advanceTimersByTimeAsync(10);
    answer(target, true); // the frame itself
    await expect(result).resolves.toBe(true);
  });

  it("without requireSourceMatch (the shell's behaviour) the source is not checked", async () => {
    const target = frameWindow();
    const result = queryDirtyState({ targetWindow: target, isOriginTrusted: trusted });
    answer(target, true, { source: null });
    await expect(result).resolves.toBe(true);
  });
});
