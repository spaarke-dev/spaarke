/**
 * useSseStream Hook - action_outcome dispatcher tests (spaarke-ai-architecture-redesign-r2 task 044c)
 *
 * THE GAP this closes: the server has always emitted an `action_outcome` SSE frame on the
 * AUTO-EXECUTE (no-dialog) gate leg (task 044, SideEffectGateAIFunction.cs ~line 490), carrying
 * the Completion Engine's OutcomeCard view-projection — but the client's `processEvent`
 * dispatcher (hooks/useSseStream.ts) never recognized the `action_outcome` type, so the frame
 * was silently dropped: no `pendingActionEvent` was ever set, and SprkChat never rendered
 * anything beyond the plain grounded text.
 *
 * KEEP rationale: anchors the dispatch-layer contract — an `action_outcome` SSE event MUST be
 * routed into `pendingActionEvent` (the SAME mechanism `action_confirmation` / `action_success` /
 * `action_error` / `dialog_open` / `navigate` already use) so SprkChat's existing action-event
 * useEffect can dispatch it. This test operates BELOW the React component tree — it exercises the
 * real `useSseStream` hook + the real `readSseStream` parse loop, not a mock — and asserts on the
 * frame's raw field shape (actionName/status/userSummary/linkUrl/linkLabel/nextSteps/
 * ledgerOutputKey), matching the server's `ChatSseActionOutcomeData` (Api/Ai/ChatEndpoints.cs).
 *
 * If the `action_outcome` branch in `processEvent` (hooks/useSseStream.ts) were removed or the
 * frame were otherwise dropped, `pendingActionEvent` would remain `null` after the stream
 * completes and every assertion below would fail.
 */

import { renderHook, act } from '@testing-library/react';
import { useSseStream, parseSseEvent } from '../hooks/useSseStream';

// ---------------------------------------------------------------------------
// Polyfills for jsdom (TextEncoder, TextDecoder, ReadableStream)
// ---------------------------------------------------------------------------

import { TextEncoder, TextDecoder } from 'util';
(global as any).TextEncoder = TextEncoder;
(global as any).TextDecoder = TextDecoder;

if (typeof globalThis.ReadableStream === 'undefined') {
  (globalThis as any).ReadableStream = class ReadableStream {
    private _source: any;
    constructor(source: any) {
      this._source = source;
    }
    getReader() {
      const chunks: Uint8Array[] = [];
      const controller = {
        enqueue: (chunk: Uint8Array) => chunks.push(chunk),
        close: () => {},
      };
      this._source.start(controller);
      let index = 0;
      return {
        read: async () => {
          if (index < chunks.length) {
            return { done: false, value: chunks[index++] };
          }
          return { done: true, value: undefined };
        },
        cancel: async () => {},
      };
    }
  };
}

// ---------------------------------------------------------------------------
// Mock fetch helpers
// ---------------------------------------------------------------------------

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

function createSseStream(events: Array<Record<string, unknown>>): any {
  const encoder = new TextEncoder();
  const lines = events.map(evt => `data: ${JSON.stringify(evt)}\n\n`);
  const encoded = encoder.encode(lines.join(''));

  return new ReadableStream({
    start(controller: any) {
      controller.enqueue(encoded);
      controller.close();
    },
  });
}

function createSseResponse(events: Array<Record<string, unknown>>, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    body: createSseStream(events),
    text: jest.fn().mockResolvedValue(''),
    headers: new Headers(),
  } as unknown as Response;
}

const _TEST_TOKEN_VALUE = `header.${btoa(JSON.stringify({ tid: 'tenant-1' }))}.signature`;
const TEST_TOKEN = (): Promise<string> => Promise.resolve(_TEST_TOKEN_VALUE);

beforeEach(() => {
  jest.clearAllMocks();
});

// ---------------------------------------------------------------------------
// The real server wire shape (mirrors ChatSseActionOutcomeData, camelCase per
// ChatEndpoints.cs JsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase).
// Emitted by SideEffectGateAIFunction.cs ~line 490.
// ---------------------------------------------------------------------------

const ACTION_OUTCOME_EVENT = {
  type: 'action_outcome',
  content: null,
  data: {
    actionName: 'sprk_create_todo',
    status: 'succeeded',
    userSummary: 'Follow-up task "Review NDA" was created.',
    linkUrl: 'https://org.crm.dynamics.com/main.aspx?etn=sprk_todo&id=abc-123&pagetype=entityrecord',
    linkLabel: 'Open record',
    nextSteps: ['Undo'],
    ledgerOutputKey: 'binding-42@t1',
  },
};

describe('parseSseEvent - action_outcome events', () => {
  it('parseSseEvent_ActionOutcomeEvent_ParsedCorrectly', () => {
    const line = `data: ${JSON.stringify(ACTION_OUTCOME_EVENT)}`;
    const result = parseSseEvent(line);

    expect(result).not.toBeNull();
    expect(result!.type).toBe('action_outcome');
    expect(result!.data?.actionName).toBe('sprk_create_todo');
    expect(result!.data?.status).toBe('succeeded');
    expect(result!.data?.userSummary).toBe('Follow-up task "Review NDA" was created.');
    expect(result!.data?.ledgerOutputKey).toBe('binding-42@t1');
    expect(result!.data?.nextSteps).toEqual(['Undo']);
  });
});

describe('useSseStream - action_outcome dispatch (task 044c)', () => {
  it('startStream_WithActionOutcomeEvent_SetsPendingActionEventWithFullPayload', async () => {
    const events = [
      { type: 'token', content: 'ACTION EXECUTED: created the follow-up task.' },
      ACTION_OUTCOME_EVENT,
      { type: 'done', content: null },
    ];
    mockFetch.mockResolvedValueOnce(createSseResponse(events));

    const { result } = renderHook(() => useSseStream());

    await act(async () => {
      result.current.startStream('https://api.example.com/stream', { message: 'create a follow-up' }, TEST_TOKEN);
      await new Promise(r => setTimeout(r, 50));
    });

    // THE GAP: before task 044c, `processEvent` had no branch for 'action_outcome',
    // so this would remain null forever (the frame silently dropped).
    expect(result.current.pendingActionEvent).not.toBeNull();
    expect(result.current.pendingActionEvent!.type).toBe('action_outcome');
    expect(result.current.pendingActionEvent!.data).toEqual(
      expect.objectContaining({
        actionName: 'sprk_create_todo',
        status: 'succeeded',
        userSummary: 'Follow-up task "Review NDA" was created.',
        linkUrl: 'https://org.crm.dynamics.com/main.aspx?etn=sprk_todo&id=abc-123&pagetype=entityrecord',
        linkLabel: 'Open record',
        nextSteps: ['Undo'],
        ledgerOutputKey: 'binding-42@t1',
      })
    );
  });

  it('startStream_ActionOutcomeWithoutLink_PendingEventStillSet', async () => {
    // A pure/notification-style outcome with no server-composed link (link is optional).
    const events = [
      {
        type: 'action_outcome',
        content: null,
        data: {
          actionName: 'sprk_send_notification',
          status: 'succeeded',
          userSummary: 'Notification sent.',
          linkUrl: null,
          linkLabel: null,
          nextSteps: [],
          ledgerOutputKey: 'binding-7@t3',
        },
      },
      { type: 'done', content: null },
    ];
    mockFetch.mockResolvedValueOnce(createSseResponse(events));

    const { result } = renderHook(() => useSseStream());

    await act(async () => {
      result.current.startStream('https://api.example.com/stream', { message: 'notify' }, TEST_TOKEN);
      await new Promise(r => setTimeout(r, 50));
    });

    expect(result.current.pendingActionEvent?.type).toBe('action_outcome');
    expect(result.current.pendingActionEvent?.data.linkUrl).toBeNull();
    expect(result.current.pendingActionEvent?.data.nextSteps).toEqual([]);
  });

  it('startStream_NoActionOutcomeEvent_PendingActionEventStaysNull', async () => {
    // Non-regression: an ordinary token/done stream must not synthesize an action event.
    const events = [
      { type: 'token', content: 'Just a normal reply.' },
      { type: 'done', content: null },
    ];
    mockFetch.mockResolvedValueOnce(createSseResponse(events));

    const { result } = renderHook(() => useSseStream());

    await act(async () => {
      result.current.startStream('https://api.example.com/stream', { message: 'hi' }, TEST_TOKEN);
      await new Promise(r => setTimeout(r, 50));
    });

    expect(result.current.pendingActionEvent).toBeNull();
  });
});
