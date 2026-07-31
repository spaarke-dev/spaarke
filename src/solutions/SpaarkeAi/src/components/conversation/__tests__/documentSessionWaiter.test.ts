/**
 * documentSessionWaiter.test.ts — agreements-r1 task 031 (DEF-09 routing seam).
 *
 * Unit tests for the pure waiter/timeout state machine extracted from ConversationPane. Proves:
 *  - an already-known fileId resolves immediately (no wait) — the "already-open/ingested document"
 *    and "repeat review of an already-resolved file" cases.
 *  - a not-yet-known fileId resolves once `notify` fires for THAT fileId (the fresh-mount case).
 *  - a DIFFERENT fileId's `notify` never resolves an unrelated waiter (no cross-file leakage).
 *  - a genuinely never-notified waiter resolves `null` on timeout — never hangs, never throws
 *    (the "no open document"/failed-mount graceful-degrade case).
 *  - multiple concurrent waiters for the SAME fileId all resolve together.
 *  - `notify` with an empty/undefined session id is a no-op (a pointer-only registration).
 *  - `reset()` drops all known values and in-flight waiters (session reset).
 */
import { createDocumentSessionWaiter } from '../documentSessionWaiter';

describe('documentSessionWaiter', () => {
  it('resolves immediately for an already-known fileId (no wait)', async () => {
    const waiter = createDocumentSessionWaiter();
    waiter.notify('file-1', 'doc-session-A');

    const result = await waiter.awaitDocumentSessionId('file-1');
    expect(result).toBe('doc-session-A');
  });

  it('resolves once notify fires for the SAME fileId (fresh-mount async backfill)', async () => {
    const waiter = createDocumentSessionWaiter();
    const pending = waiter.awaitDocumentSessionId('file-2');

    // Not yet resolved (still no notify).
    let settled = false;
    void pending.then(() => {
      settled = true;
    });
    await Promise.resolve();
    await Promise.resolve();
    expect(settled).toBe(false);

    waiter.notify('file-2', 'doc-session-B');
    await expect(pending).resolves.toBe('doc-session-B');
  });

  it('a DIFFERENT fileId notify never resolves an unrelated waiter', async () => {
    jest.useFakeTimers();
    const waiter = createDocumentSessionWaiter();
    const pending = waiter.awaitDocumentSessionId('file-3', 100);

    waiter.notify('file-OTHER', 'doc-session-X');
    // The unrelated notify must not settle file-3's waiter — advance past ITS OWN timeout to prove
    // it degrades to null, not to the other file's session id.
    jest.advanceTimersByTime(100);
    await expect(pending).resolves.toBeNull();
    jest.useRealTimers();
  });

  it('degrades to null on timeout — never hangs, never throws (no open document / failed mount)', async () => {
    jest.useFakeTimers();
    const waiter = createDocumentSessionWaiter();
    const pending = waiter.awaitDocumentSessionId('file-4', 5000);

    jest.advanceTimersByTime(5000);
    await expect(pending).resolves.toBeNull();
    jest.useRealTimers();
  });

  it('multiple concurrent waiters for the SAME fileId all resolve together', async () => {
    const waiter = createDocumentSessionWaiter();
    const first = waiter.awaitDocumentSessionId('file-5');
    const second = waiter.awaitDocumentSessionId('file-5');

    waiter.notify('file-5', 'doc-session-C');
    await expect(first).resolves.toBe('doc-session-C');
    await expect(second).resolves.toBe('doc-session-C');
  });

  it('notify with an undefined/empty session id is a no-op (pointer-only registration)', async () => {
    jest.useFakeTimers();
    const waiter = createDocumentSessionWaiter();
    const pending = waiter.awaitDocumentSessionId('file-6', 50);

    waiter.notify('file-6', undefined);
    waiter.notify('file-6', '');
    jest.advanceTimersByTime(50);
    await expect(pending).resolves.toBeNull();
    jest.useRealTimers();
  });

  it('a resolved waiter does not fire its timeout after settling early (no leaked timer double-resolve)', async () => {
    jest.useFakeTimers();
    const waiter = createDocumentSessionWaiter();
    const pending = waiter.awaitDocumentSessionId('file-7', 1000);
    waiter.notify('file-7', 'doc-session-D');
    await expect(pending).resolves.toBe('doc-session-D');

    // Advancing time past the original timeout must not throw or double-settle anything observable.
    expect(() => jest.advanceTimersByTime(1000)).not.toThrow();
    jest.useRealTimers();
  });

  it('reset() drops known values and in-flight waiters — a new session must not resolve stale state', async () => {
    jest.useFakeTimers();
    const waiter = createDocumentSessionWaiter();
    waiter.notify('file-8', 'doc-session-E');

    const pendingBeforeReset = waiter.awaitDocumentSessionId('file-9', 50);
    waiter.reset();

    // The known file-8 mapping is gone — a fresh await for it must wait (not resolve immediately).
    const afterResetPending = waiter.awaitDocumentSessionId('file-8', 50);
    jest.advanceTimersByTime(50);
    await expect(afterResetPending).resolves.toBeNull();

    // The in-flight waiter registered BEFORE reset degrades to null on ITS OWN timeout (reset does not
    // resolve it early, but it also must not leak a stale resolution afterward).
    await expect(pendingBeforeReset).resolves.toBeNull();
    jest.useRealTimers();
  });
});
