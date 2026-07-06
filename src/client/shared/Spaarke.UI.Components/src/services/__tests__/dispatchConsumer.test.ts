/**
 * dispatchConsumer tests — ai-architecture-redesign-r1 task 023 / FR-P1-04.
 *
 * Covers:
 *   - parseConsumerChips tolerant wire parsing
 *   - Click preconditions: missing bindingId, no session, empty-attachments
 *     guard (requiresAttachments + 0 attachments → NO fetch, NO bus events)
 *   - Happy delta path: streaming_started (once) → field_delta(s) →
 *     streaming_complete/complete; $.-prefix path normalization
 *   - Terminal complete-with-result synthesis (non-streaming executors):
 *     per-field field_delta events before streaming_complete
 *   - Error chunk → streaming_complete/declined + rejection
 *   - HTTP non-OK → ADR-019 errorCode surfaced + declined event + rejection
 *   - Stream with no terminal chunk → streaming_complete/empty
 *   - workspaceTarget → ONE widget_load (with correlationId=streamId) BEFORE
 *     the stream lifecycle events
 *   - Request contract: URL shape + { bindingId, args } body + Authorization
 */

import {
  createConsumerDispatcher,
  parseConsumerChips,
  buildDispatchUrl,
  DispatchPreconditionError,
  type DispatchWorkspaceEvent,
} from '../dispatchConsumer';

// jsdom does not polyfill TextEncoder / TextDecoder in all versions — use
// Node's implementations (same pattern as the sibling useSseStream tests).
import { TextEncoder as NodeTextEncoder, TextDecoder as NodeTextDecoder } from 'util';
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (globalThis as any).TextEncoder === 'undefined') {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).TextEncoder = NodeTextEncoder;
}
// eslint-disable-next-line @typescript-eslint/no-explicit-any
if (typeof (globalThis as any).TextDecoder === 'undefined') {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).TextDecoder = NodeTextDecoder;
}

const BFF_BASE = 'https://bff.test';
const SESSION_ID = '11111111-2222-3333-4444-555555555555';
const BINDING_ID = 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

interface Published {
  channel: string;
  event: DispatchWorkspaceEvent;
}

function makePublishSpy() {
  const events: Published[] = [];
  const publish = (channel: 'workspace', event: DispatchWorkspaceEvent): void => {
    events.push({ channel, event });
  };
  return { publish, events };
}

/** Response-like carrying an SSE body; each chunk arg becomes `data: <c>\n\n`. */
function sseResponse(chunks: string[], status = 200): Response {
  const wire = chunks.map(c => `data: ${c}\n\n`).join('');
  const encoder = new TextEncoder();
  const payload = encoder.encode(wire);

  let pulled = false;
  const reader = {
    async read(): Promise<{ done: boolean; value?: Uint8Array }> {
      if (pulled) return { done: true, value: undefined };
      pulled = true;
      return { done: false, value: payload };
    },
    releaseLock() {
      /* noop */
    },
  };

  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => ({}),
    text: async () => wire,
    body: { getReader: () => reader },
    headers: new Headers(),
  } as unknown as Response;
}

function problemResponse(status: number, errorCode?: string): Response {
  const body = errorCode ? { errorCode } : { detail: 'nope' };
  return {
    ok: false,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    headers: new Headers(),
    body: null,
  } as unknown as Response;
}

function makeDispatcher(
  publish: (channel: 'workspace', event: DispatchWorkspaceEvent) => void,
  sessionId: string | null = SESSION_ID
) {
  return createConsumerDispatcher({
    bffBaseUrl: BFF_BASE,
    getSessionId: () => sessionId,
    getAccessToken: async () => 'test-token',
    publishPaneEvent: publish,
  });
}

const mockFetch = jest.fn();

beforeEach(() => {
  mockFetch.mockReset();
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (globalThis as any).fetch = mockFetch;
});

// ---------------------------------------------------------------------------
// parseConsumerChips
// ---------------------------------------------------------------------------

describe('parseConsumerChips', () => {
  it('parses the declared wire shape', () => {
    const chips = parseConsumerChips([
      {
        target_binding_id: 'b-1',
        chip_label: 'Summarize all?',
        prefill_slots: { scope: 'all' },
        requires_attachments: true,
      },
      { target_binding_id: 'b-2', chip_label: 'Create matter' },
    ]);
    expect(chips).toEqual([
      {
        bindingId: 'b-1',
        label: 'Summarize all?',
        prefillSlots: { scope: 'all' },
        requiresAttachments: true,
      },
      { bindingId: 'b-2', label: 'Create matter', prefillSlots: undefined, requiresAttachments: false },
    ]);
  });

  it('tolerates the BFF camelCase serialization twin of the wire shape', () => {
    const chips = parseConsumerChips([{ targetBindingId: 'b-3', chipLabel: 'Summarize', requiresAttachments: true }]);
    expect(chips).toEqual([
      { bindingId: 'b-3', label: 'Summarize', prefillSlots: undefined, requiresAttachments: true },
    ]);
  });

  it('degrades malformed input to skipped entries / empty array (never throws)', () => {
    expect(parseConsumerChips(undefined)).toEqual([]);
    expect(parseConsumerChips('not-an-array')).toEqual([]);
    expect(parseConsumerChips({})).toEqual([]);
    expect(
      parseConsumerChips([
        null,
        42,
        { chip_label: 'no binding id' },
        { target_binding_id: 'no-label' },
        { target_binding_id: '', chip_label: 'empty id' },
        { target_binding_id: 'ok', chip_label: 'OK' },
      ])
    ).toEqual([{ bindingId: 'ok', label: 'OK', prefillSlots: undefined, requiresAttachments: false }]);
  });
});

// ---------------------------------------------------------------------------
// Click preconditions
// ---------------------------------------------------------------------------

describe('dispatchConsumer preconditions', () => {
  it('rejects when bindingId is empty — no fetch, no bus events', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);

    await expect(dispatchConsumer('')).rejects.toMatchObject({
      name: 'DispatchPreconditionError',
      code: 'binding-id-required',
    });
    expect(mockFetch).not.toHaveBeenCalled();
    expect(events).toHaveLength(0);
  });

  it('rejects when there is no active session — no fetch, no bus events', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish, null);

    await expect(dispatchConsumer(BINDING_ID)).rejects.toMatchObject({
      code: 'no-session',
    });
    expect(mockFetch).not.toHaveBeenCalled();
    expect(events).toHaveLength(0);
  });

  it('empty-attachments guard: requiresAttachments + zero attachments rejects with NO network call', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);

    await expect(dispatchConsumer(BINDING_ID, { requiresAttachments: true, attachmentCount: 0 })).rejects.toMatchObject(
      { code: 'attachments-required' }
    );
    expect(mockFetch).not.toHaveBeenCalled();
    expect(events).toHaveLength(0);
  });

  it('empty-attachments guard passes when attachments are present', async () => {
    const { publish } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"complete","done":true}']));

    const result = await dispatchConsumer(BINDING_ID, {
      requiresAttachments: true,
      attachmentCount: 2,
    });
    expect(result.status).toBe('complete');
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it('DispatchPreconditionError instances carry stable name + code', () => {
    const err = new DispatchPreconditionError('attachments-required', 'msg');
    expect(err.name).toBe('DispatchPreconditionError');
    expect(err.code).toBe('attachments-required');
    expect(err).toBeInstanceOf(Error);
  });
});

// ---------------------------------------------------------------------------
// Request contract
// ---------------------------------------------------------------------------

describe('dispatchConsumer request contract', () => {
  it('POSTs { bindingId, args } to the binding-dispatch URL with a fresh bearer token', async () => {
    const { publish } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"complete","done":true}']));

    await dispatchConsumer(BINDING_ID, { slots: { style: 'executive' } });

    expect(mockFetch).toHaveBeenCalledTimes(1);
    const [url, init] = mockFetch.mock.calls[0] as [string, RequestInit];
    expect(url).toBe(buildDispatchUrl(BFF_BASE, SESSION_ID));
    expect(url).toBe(`${BFF_BASE}/api/ai/chat/sessions/${SESSION_ID}/dispatch`);
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({
      bindingId: BINDING_ID,
      args: { style: 'executive' },
    });
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe('Bearer test-token');
  });

  it('defaults args to {} when no slots are passed', async () => {
    const { publish } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"complete","done":true}']));

    await dispatchConsumer(BINDING_ID);
    const [, init] = mockFetch.mock.calls[0] as [string, RequestInit];
    expect(JSON.parse(init.body as string)).toEqual({ bindingId: BINDING_ID, args: {} });
  });
});

// ---------------------------------------------------------------------------
// Stream → PaneEventBus bridging
// ---------------------------------------------------------------------------

describe('dispatchConsumer SSE → workspace-channel bridging', () => {
  it('delta path: started once → field_delta per delta → complete; $.-paths normalized', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        '{"type":"delta","delta":{"path":"$.tldr","content":"Short","sequence":0}}',
        '{"type":"delta","delta":{"path":"summary","content":"Long","sequence":1}}',
        '{"type":"complete","done":true}',
      ])
    );

    const result = await dispatchConsumer(BINDING_ID, { streamId: 'stream-1' });

    expect(result).toEqual({ streamId: 'stream-1', status: 'complete' });
    expect(events.map(e => e.event.type)).toEqual([
      'streaming_started',
      'field_delta',
      'field_delta',
      'streaming_complete',
    ]);
    expect(events[1].event).toMatchObject({
      streamId: 'stream-1',
      fieldPath: 'tldr', // "$." prefix normalized
      fieldContent: 'Short',
      sequence: 0,
    });
    expect(events[2].event).toMatchObject({ fieldPath: 'summary', fieldContent: 'Long' });
    expect(events[3].event).toMatchObject({ completionStatus: 'complete' });
    // every event carries the same channel + streamId
    expect(events.every(e => e.channel === 'workspace')).toBe(true);
  });

  it('terminal complete-with-result synthesizes per-field deltas before completion', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        JSON.stringify({
          type: 'complete',
          done: true,
          result: {
            tldr: 'Short version',
            keywords: ['a', 'b'],
            parsedSuccessfully: true, // widget-internal — skipped
            rawResponse: 'raw', // widget-internal — skipped
            empty: '', // empty — skipped
            nothing: null, // null — skipped
          },
        }),
      ])
    );

    const result = await dispatchConsumer(BINDING_ID, { streamId: 's-2' });

    expect(result.status).toBe('complete');
    expect(events.map(e => e.event.type)).toEqual([
      'streaming_started',
      'field_delta',
      'field_delta',
      'streaming_complete',
    ]);
    expect(events[1].event).toMatchObject({ fieldPath: 'tldr', fieldContent: 'Short version' });
    expect(events[2].event).toMatchObject({ fieldPath: 'keywords', fieldContent: '["a","b"]' });
  });

  it('error chunk publishes declined and rejects', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"error","error":"boom","done":true}']));

    await expect(dispatchConsumer(BINDING_ID, { streamId: 's-3' })).rejects.toThrow(/stream reported an error/);
    const terminal = events[events.length - 1].event;
    expect(terminal).toMatchObject({
      type: 'streaming_complete',
      streamId: 's-3',
      completionStatus: 'declined',
    });
    // ADR-019: raw server error text never appears on the bus.
    expect(JSON.stringify(events)).not.toContain('boom');
  });

  it('HTTP non-OK surfaces the ADR-019 errorCode, publishes declined, rejects', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(problemResponse(404, 'dispatch.binding-not-found'));

    await expect(dispatchConsumer(BINDING_ID, { streamId: 's-4' })).rejects.toThrow(
      /status=404.*errorCode=dispatch\.binding-not-found/
    );
    expect(events).toHaveLength(1);
    expect(events[0].event).toMatchObject({
      type: 'streaming_complete',
      streamId: 's-4',
      completionStatus: 'declined',
    });
  });

  it('stream ending without a terminal chunk publishes empty and resolves with status empty', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(
      sseResponse(['{"type":"delta","delta":{"path":"tldr","content":"x","sequence":0}}'])
    );

    const result = await dispatchConsumer(BINDING_ID, { streamId: 's-5' });
    expect(result.status).toBe('empty');
    const terminal = events[events.length - 1].event;
    expect(terminal).toMatchObject({
      type: 'streaming_complete',
      completionStatus: 'empty',
      streamId: 's-5',
    });
  });

  it('malformed SSE lines and unknown chunk types are tolerated', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        'this-is-not-json',
        '{"type":"text","content":"legacy free-form"}',
        '{"type":"delta"}', // delta without payload — ignored
        '{"type":"complete","done":true}',
      ])
    );

    const result = await dispatchConsumer(BINDING_ID, { streamId: 's-6' });
    expect(result.status).toBe('complete');
    expect(events.map(e => e.event.type)).toEqual(['streaming_started', 'streaming_complete']);
  });

  it('generates a streamId when none is supplied', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"complete","done":true}']));

    const result = await dispatchConsumer(BINDING_ID);
    expect(result.streamId).toMatch(/^dispatch-/);
    expect(events[0].event.streamId).toBe(result.streamId);
  });
});

// ---------------------------------------------------------------------------
// workspaceTarget (optional view config)
// ---------------------------------------------------------------------------

describe('dispatchConsumer workspaceTarget', () => {
  it('publishes ONE widget_load with correlationId=streamId BEFORE the stream lifecycle', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"complete","done":true}']));

    await dispatchConsumer(BINDING_ID, {
      streamId: 's-7',
      workspaceTarget: {
        widgetType: 'structured-output-stream',
        widgetData: { mode: 'streaming' },
        displayName: 'Summary: contract.pdf',
      },
    });

    expect(events.map(e => e.event.type)).toEqual(['widget_load', 'streaming_started', 'streaming_complete']);
    expect(events[0].event).toMatchObject({
      widgetType: 'structured-output-stream',
      displayName: 'Summary: contract.pdf',
    });
    expect(events[0].event.widgetData).toMatchObject({
      correlationId: 's-7',
      mode: 'streaming',
    });
  });

  it('does NOT publish widget_load when the empty-attachments guard trips first', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);

    await expect(
      dispatchConsumer(BINDING_ID, {
        requiresAttachments: true,
        attachmentCount: 0,
        workspaceTarget: { widgetType: 'structured-output-stream' },
      })
    ).rejects.toMatchObject({ code: 'attachments-required' });
    expect(events).toHaveLength(0);
  });
});
