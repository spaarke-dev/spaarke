/**
 * sseToPaneEventBridge.test.ts — R5 task 036 / P2-CLOSEOUT-05.
 *
 * Covers:
 *   - Empty sequence (no chunks → no events)
 *   - Normal sequence (Started→2 FromDelta→Completed)
 *   - Error mid-stream
 *   - Malformed event does NOT throw (returns [] safely)
 *   - streaming_started fires exactly ONCE per bridge instance
 *   - streamId is propagated to every emitted event
 */

import {
  createSseToPaneEventBridge,
  type AnalysisChunk,
} from '../sseToPaneEventBridge';

const STREAM_ID = 'test-stream-1';

describe('createSseToPaneEventBridge', () => {
  test('empty sequence produces no events', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);
    // Nothing consumed — hasStarted should be false; no events to inspect.
    expect(bridge.hasStarted).toBe(false);
  });

  test('normal sequence: started + field_delta×2 + streaming_complete', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);

    const chunk1: AnalysisChunk = {
      type: 'delta',
      delta: { path: 'tldr', content: 'Hello ', sequence: 1 },
    };
    const chunk2: AnalysisChunk = {
      type: 'delta',
      delta: { path: 'tldr', content: 'world', sequence: 2 },
    };
    const chunk3: AnalysisChunk = { type: 'complete', done: true };

    const e1 = bridge.consume(chunk1);
    const e2 = bridge.consume(chunk2);
    const e3 = bridge.consume(chunk3);

    // First chunk emits streaming_started + the field_delta.
    expect(e1).toHaveLength(2);
    expect(e1[0]).toEqual({ type: 'streaming_started', streamId: STREAM_ID });
    expect(e1[1]).toEqual({
      type: 'field_delta',
      streamId: STREAM_ID,
      fieldPath: 'tldr',
      fieldContent: 'Hello ',
      sequence: 1,
    });

    // Second chunk emits ONLY the field_delta.
    expect(e2).toHaveLength(1);
    expect(e2[0]).toEqual({
      type: 'field_delta',
      streamId: STREAM_ID,
      fieldPath: 'tldr',
      fieldContent: 'world',
      sequence: 2,
    });

    // Terminal chunk emits streaming_complete.
    expect(e3).toHaveLength(1);
    expect(e3[0]).toEqual({
      type: 'streaming_complete',
      streamId: STREAM_ID,
      completionStatus: 'complete',
    });

    expect(bridge.hasStarted).toBe(true);
  });

  test('error mid-stream emits streaming_started + streaming_complete (declined)', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);

    const chunk1: AnalysisChunk = {
      type: 'delta',
      delta: { path: 'tldr', content: 'partial', sequence: 1 },
    };
    const chunkErr: AnalysisChunk = {
      type: 'error',
      done: true,
      error: 'something went wrong',
    };

    const e1 = bridge.consume(chunk1);
    const e2 = bridge.consume(chunkErr);

    expect(e1).toHaveLength(2);
    expect(e2).toHaveLength(1);
    expect(e2[0]).toEqual({
      type: 'streaming_complete',
      streamId: STREAM_ID,
      completionStatus: 'declined',
    });
  });

  test('error as FIRST event also emits started + declined', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);
    const chunkErr: AnalysisChunk = { type: 'error', done: true, error: 'boom' };
    const e1 = bridge.consume(chunkErr);

    expect(e1).toHaveLength(2);
    expect(e1[0].type).toBe('streaming_started');
    expect(e1[1].type).toBe('streaming_complete');
    expect((e1[1] as { completionStatus?: string }).completionStatus).toBe('declined');
  });

  test('streaming_started fires exactly once across many delta chunks', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);
    const allEvents: unknown[] = [];

    for (let i = 1; i <= 5; i += 1) {
      const out = bridge.consume({
        type: 'delta',
        delta: { path: 'summary', content: `tok${i}`, sequence: i },
      });
      allEvents.push(...out);
    }

    const startedCount = allEvents.filter(
      (e) => (e as { type: string }).type === 'streaming_started'
    ).length;
    expect(startedCount).toBe(1);
  });

  test('streamId is propagated to every emitted event', () => {
    const streamId = 'stream-abc-123';
    const bridge = createSseToPaneEventBridge(streamId);
    const out = [
      ...bridge.consume({
        type: 'delta',
        delta: { path: 'tldr', content: 'x', sequence: 1 },
      }),
      ...bridge.consume({ type: 'complete', done: true }),
    ];

    for (const ev of out) {
      expect((ev as { streamId?: string }).streamId).toBe(streamId);
    }
  });

  test('malformed chunk (missing type) returns empty array — does NOT throw', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);
    // Cast to AnalysisChunk to bypass static typing; runtime input from the
    // network may be malformed.
    const malformed = {} as AnalysisChunk;
    expect(() => bridge.consume(malformed)).not.toThrow();
    expect(bridge.consume(malformed)).toEqual([]);
    expect(bridge.hasStarted).toBe(false);
  });

  test('malformed delta chunk (missing payload) is ignored', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);
    const bad: AnalysisChunk = { type: 'delta' /* no delta payload */ };
    expect(bridge.consume(bad)).toEqual([]);
    expect(bridge.hasStarted).toBe(false);
  });

  test('malformed delta chunk (wrong field types) is ignored', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);
    // Sequence as string instead of number — runtime-malformed input.
    const bad = {
      type: 'delta',
      delta: { path: 'tldr', content: 'x', sequence: 'one' as unknown as number },
    } as AnalysisChunk;
    expect(bridge.consume(bad)).toEqual([]);
  });

  test('unknown event type is silently ignored', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);
    const out = bridge.consume({ type: 'unknown-event-foo' } as AnalysisChunk);
    expect(out).toEqual([]);
    expect(bridge.hasStarted).toBe(false);
  });

  test('"text" event (legacy free-form streaming) is ignored', () => {
    const bridge = createSseToPaneEventBridge(STREAM_ID);
    const out = bridge.consume({ type: 'text', content: 'token' });
    expect(out).toEqual([]);
    expect(bridge.hasStarted).toBe(false);
  });
});
