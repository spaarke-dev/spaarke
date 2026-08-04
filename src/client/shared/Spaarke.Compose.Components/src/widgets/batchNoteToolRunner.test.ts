/**
 * batchNoteToolRunner.test.ts — ai-advanced-capabilities-agreements-r1 task 041 (spec FR-11).
 *
 * Pure-function tests (no editor/DOM dependency — mirrors `layoutCommentGutterCards`'s /
 * `resolveMatchingThreadId`'s own testability precedent in `ComposeCommentGutter.test.tsx`):
 *  - ADR-016 sequentiality — never more than ONE `runOne` call in flight at any instant.
 *  - Strict input-order execution.
 *  - Failure isolation — a rejected note is recorded and the loop continues.
 *  - Live progress reporting (`onProgress`) before/after each note.
 *  - Empty-input no-op.
 */
import { runBatchNoteTool, type BatchNoteToolProgress } from './batchNoteToolRunner';

/** A controllable async "dispatch" — resolves/rejects only when the test tells it to. */
function makeDeferred<T>(): { promise: Promise<T>; resolve: (v: T) => void; reject: (e: unknown) => void } {
  let resolve!: (v: T) => void;
  let reject!: (e: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

describe('runBatchNoteTool — ADR-016 strict sequentiality', () => {
  it('never has more than one runOne call in flight at any instant', async () => {
    let inFlight = 0;
    let maxInFlight = 0;
    const calls: string[] = [];

    const runOne = jest.fn(async (threadId: string): Promise<void> => {
      inFlight += 1;
      maxInFlight = Math.max(maxInFlight, inFlight);
      calls.push(`start:${threadId}`);
      // Yield a microtask/macrotask so a buggy concurrent implementation WOULD overlap here.
      await new Promise(res => setTimeout(res, 1));
      calls.push(`end:${threadId}`);
      inFlight -= 1;
    });

    const outcomes = await runBatchNoteTool(['a', 'b', 'c'], runOne);

    expect(maxInFlight).toBe(1); // the core ADR-016 assertion
    expect(calls).toEqual(['start:a', 'end:a', 'start:b', 'end:b', 'start:c', 'end:c']);
    expect(runOne).toHaveBeenCalledTimes(3);
    expect(outcomes).toEqual([
      { threadId: 'a', ok: true },
      { threadId: 'b', ok: true },
      { threadId: 'c', ok: true },
    ]);
  });

  it('does not start note N+1 until note N settles (explicit deferred-promise gate)', async () => {
    const deferredA = makeDeferred<void>();
    const runOne = jest.fn(
      (threadId: string): Promise<void> => (threadId === 'a' ? deferredA.promise : Promise.resolve())
    );

    const run = runBatchNoteTool(['a', 'b'], runOne);

    // Give the loop a tick to reach note 'a' and await its (unresolved) promise.
    await Promise.resolve();
    await Promise.resolve();
    expect(runOne).toHaveBeenCalledTimes(1);
    expect(runOne).toHaveBeenCalledWith('a');

    // 'b' must NOT have been dispatched yet — 'a' is still in flight.
    expect(runOne).not.toHaveBeenCalledWith('b');

    deferredA.resolve();
    const outcomes = await run;

    expect(runOne).toHaveBeenCalledTimes(2);
    expect(runOne).toHaveBeenCalledWith('b');
    expect(outcomes.map(o => o.threadId)).toEqual(['a', 'b']);
  });

  it('runs strictly in the INPUT order given, not the order threads happen to resolve fastest', async () => {
    // Note 'a' takes longer than 'b' would if it were allowed to race ahead — proves order is
    // enforced by the loop, not by dispatch speed.
    const order: string[] = [];
    const runOne = jest.fn(async (threadId: string): Promise<void> => {
      order.push(threadId);
      const delay = threadId === 'a' ? 10 : 0;
      await new Promise(res => setTimeout(res, delay));
    });

    await runBatchNoteTool(['a', 'b', 'c'], runOne);
    expect(order).toEqual(['a', 'b', 'c']);
  });
});

describe('runBatchNoteTool — failure isolation', () => {
  it('a mid-batch rejection is recorded as a failed outcome and the loop continues to the remaining notes', async () => {
    const runOne = jest.fn(async (threadId: string): Promise<void> => {
      if (threadId === 'b') throw new Error('dispatch failed for b');
    });

    const outcomes = await runBatchNoteTool(['a', 'b', 'c'], runOne);

    expect(runOne).toHaveBeenCalledTimes(3); // 'c' still ran despite 'b' failing
    expect(outcomes).toEqual([
      { threadId: 'a', ok: true },
      { threadId: 'b', ok: false, error: 'dispatch failed for b' },
      { threadId: 'c', ok: true },
    ]);
  });

  it('stringifies a non-Error rejection reason', async () => {
    const runOne = jest.fn(async (threadId: string): Promise<void> => {
      if (threadId === 'a') return Promise.reject('a plain string rejection');
    });
    const outcomes = await runBatchNoteTool(['a'], runOne);
    expect(outcomes).toEqual([{ threadId: 'a', ok: false, error: 'a plain string rejection' }]);
  });

  it('every note failing still resolves with one failed outcome per note (no throw out of the batch)', async () => {
    const runOne = jest.fn(async (): Promise<void> => {
      throw new Error('always fails');
    });
    const outcomes = await runBatchNoteTool(['a', 'b'], runOne);
    expect(outcomes.every(o => !o.ok)).toBe(true);
    expect(outcomes).toHaveLength(2);
  });
});

describe('runBatchNoteTool — progress reporting', () => {
  it('reports progress before each note starts and after it settles', async () => {
    const snapshots: BatchNoteToolProgress[] = [];
    const runOne = jest.fn(async (): Promise<void> => undefined);

    await runBatchNoteTool(['a', 'b'], runOne, p => snapshots.push(p));

    // 4 snapshots: start-a, end-a, start-b, end-b.
    expect(snapshots).toHaveLength(4);
    expect(snapshots[0]).toMatchObject({ total: 2, completed: 0, currentThreadId: 'a' });
    expect(snapshots[1]).toMatchObject({ total: 2, completed: 1, currentThreadId: null });
    expect(snapshots[1].outcomes).toEqual([{ threadId: 'a', ok: true }]);
    expect(snapshots[2]).toMatchObject({ total: 2, completed: 1, currentThreadId: 'b' });
    expect(snapshots[3]).toMatchObject({ total: 2, completed: 2, currentThreadId: null });
    expect(snapshots[3].outcomes).toEqual([
      { threadId: 'a', ok: true },
      { threadId: 'b', ok: true },
    ]);
  });

  it('does not call onProgress for an empty threadIds list', async () => {
    const onProgress = jest.fn();
    const outcomes = await runBatchNoteTool([], jest.fn(), onProgress);
    expect(outcomes).toEqual([]);
    expect(onProgress).not.toHaveBeenCalled();
  });
});
