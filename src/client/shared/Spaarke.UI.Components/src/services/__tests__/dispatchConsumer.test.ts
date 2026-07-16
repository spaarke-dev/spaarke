/**
 * dispatchConsumer tests — ai-architecture-redesign-r1 task 023 / FR-P1-04.
 *
 * Covers:
 *   - parseConsumerChips tolerant wire parsing
 *   - Click preconditions: missing bindingId, no session, empty-attachments
 *     guard (requiresAttachments + 0 attachments → NO fetch, NO bus events)
 *   - Terminal complete-with-result section bridge (task 046 / amended
 *     ADR-037): streaming_started (once) → section_started +
 *     section_completed per top-level result key → streaming_complete.
 *     Strings ride finalContent; arrays/objects ride finalStructuredData.
 *   - Retired "delta" wire chunks are IGNORED (no server path emits them
 *     post-cutover; verified AnalysisChunk.FromDelta has zero call sites)
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
    // Ordering-focused tests below don't care about pacing (task 039) — 0 reveals
    // all sections back-to-back so assertions stay fast and deterministic. Pacing
    // itself is covered by the dedicated describe block further down.
    sectionRevealDelayMs: 0,
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

  // Task 023b — the SHIPPED Event-path chip wire shape (022's `EventChip`,
  // camelCase `{targetBindingId, label, args?}`; delivered on the top-level
  // `chips` event AND inside event_confirmation / event_notice payloads).
  // Single-wire-shape decision: the ONE parser accepts it; `args` maps to
  // prefillSlots and forwards verbatim to dispatchConsumer.
  it('parses the shipped EventChip wire shape ({targetBindingId, label, args})', () => {
    const chips = parseConsumerChips([
      {
        targetBindingId: '651194cd-3670-f111-ab0e-70a8a590c51c',
        label: 'Summarize all 3 files?',
        args: { fileIds: ['f-1', 'f-2', 'f-3'] },
      },
      // M4 confirm chip shape (args carries confirmedDocType too — verbatim pass-through)
      {
        targetBindingId: '651194cd-3670-f111-ab0e-70a8a590c51c',
        label: "Yes, it's a nda — summarize it",
        args: { fileIds: ['f-1'], confirmedDocType: 'nda' },
      },
      // args-less transition chip (e.g. "Summarize again" from sprk_chiptransitions)
      { targetBindingId: 'b-9', label: 'Summarize again' },
    ]);
    expect(chips).toEqual([
      {
        bindingId: '651194cd-3670-f111-ab0e-70a8a590c51c',
        label: 'Summarize all 3 files?',
        prefillSlots: { fileIds: ['f-1', 'f-2', 'f-3'] },
        requiresAttachments: false,
      },
      {
        bindingId: '651194cd-3670-f111-ab0e-70a8a590c51c',
        label: "Yes, it's a nda — summarize it",
        prefillSlots: { fileIds: ['f-1'], confirmedDocType: 'nda' },
        requiresAttachments: false,
      },
      { bindingId: 'b-9', label: 'Summarize again', prefillSlots: undefined, requiresAttachments: false },
    ]);
  });

  it('prefers authored chip_label / prefill_slots over EventChip twins when both present', () => {
    const chips = parseConsumerChips([
      {
        target_binding_id: 'b-1',
        chip_label: 'Authored label',
        label: 'EventChip label',
        prefill_slots: { from: 'authored' },
        args: { from: 'eventchip' },
      },
    ]);
    expect(chips).toEqual([
      {
        bindingId: 'b-1',
        label: 'Authored label',
        prefillSlots: { from: 'authored' },
        requiresAttachments: false,
      },
    ]);
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
  it('retired "delta" wire chunks are IGNORED (post-cutover: no per-field vocabulary on the bus)', async () => {
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
    // The retired chunks bridge to NOTHING; only the terminal lifecycle runs.
    expect(events.map(e => e.event.type)).toEqual(['streaming_started', 'streaming_complete']);
    expect(events[1].event).toMatchObject({ completionStatus: 'complete' });
    // every event carries the same channel
    expect(events.every(e => e.channel === 'workspace')).toBe(true);
  });

  it('terminal complete-with-result bridges section pairs per top-level key before completion', async () => {
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
      'section_started',
      'section_completed',
      'section_started',
      'section_completed',
      'streaming_complete',
    ]);
    // String value → finalContent; declaration-order sectionIndex.
    expect(events[1].event).toMatchObject({ sectionName: 'tldr', sectionIndex: 0, streamId: 's-2' });
    expect(events[2].event).toMatchObject({ sectionName: 'tldr', finalContent: 'Short version' });
    // Array value → finalStructuredData (typed section rendering downstream).
    expect(events[3].event).toMatchObject({ sectionName: 'keywords', sectionIndex: 1 });
    expect(events[4].event).toMatchObject({ sectionName: 'keywords', finalStructuredData: ['a', 'b'] });
    // Skipped keys never become sections.
    const sectionNames = events
      .filter(e => e.event.type === 'section_started')
      .map(e => (e.event as DispatchWorkspaceEvent).sectionName);
    expect(sectionNames).toEqual(['tldr', 'keywords']);
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
    // Only a retired/ignored chunk arrives — the stream ends with no terminal.
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

// ---------------------------------------------------------------------------
// G-P1 UAT round-1 Defect-1 fix (2026-07-05): terminal result + next-step
// chips are captured from the stream and returned to the host so a Click
// dispatch can render its output in the conversation and re-arm the strip.
// ---------------------------------------------------------------------------

describe('dispatchConsumer result + chips capture (G-P1 Defect 1)', () => {
  it('returns the terminal complete chunk result and the parsed chips chunk', async () => {
    const { publish } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        '{"type":"complete","done":true,"result":{"tldr":"T.","summary":"S."}}',
        '{"type":"chips","done":false,"chips":[{"targetBindingId":"b-again","label":"Summarize again","args":{"fileIds":["f1"]},"requiresAttachments":true}]}',
      ])
    );

    const result = await dispatchConsumer(BINDING_ID);

    expect(result.status).toBe('complete');
    expect(result.result).toEqual({ tldr: 'T.', summary: 'S.' });
    expect(result.chips).toEqual([
      {
        bindingId: 'b-again',
        label: 'Summarize again',
        prefillSlots: { fileIds: ['f1'] },
        requiresAttachments: true,
      },
    ]);
  });

  it('falls back to the legacy summary string when the complete chunk has no structured result', async () => {
    const { publish } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"complete","done":true,"summary":"Plain text summary."}']));

    const result = await dispatchConsumer(BINDING_ID);

    expect(result.result).toBe('Plain text summary.');
    expect(result.chips).toBeUndefined();
  });

  it('ignores empty/malformed chips chunks (tolerant — never a throwing parse)', async () => {
    const { publish } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        '{"type":"chips","done":false,"chips":[]}',
        '{"type":"chips","done":false,"chips":"not-an-array"}',
        '{"type":"complete","done":true}',
      ])
    );

    const result = await dispatchConsumer(BINDING_ID);

    expect(result.status).toBe('complete');
    expect(result.chips).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Surface-launch routing decision (spaarkeai-assistant-enhancements-r1 task 013a):
// the terminal `complete` chunk carries the SERVER's create-flow routing decision
// (`disposition` + `consumerType`, additive). The helper surfaces them verbatim so
// the host can branch `surface_launch` → the pre-seeded surface launch (ADR-039 —
// the client re-derives nothing).
// ---------------------------------------------------------------------------

describe('dispatchConsumer disposition + consumerType surfacing (task 013a)', () => {
  it('surfaces disposition + consumerType from the terminal complete chunk', async () => {
    const { publish } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        JSON.stringify({
          type: 'complete',
          done: true,
          disposition: 'surface_launch',
          consumerType: 'create-matter',
          result: { matter_name: 'Acme v. Beta', matter_description: 'Contract dispute.' },
        }),
      ])
    );

    const result = await dispatchConsumer(BINDING_ID);

    expect(result.status).toBe('complete');
    expect(result.disposition).toBe('surface_launch');
    expect(result.consumerType).toBe('create-matter');
    expect(result.result).toEqual({ matter_name: 'Acme v. Beta', matter_description: 'Contract dispute.' });
  });

  it('leaves disposition + consumerType undefined on a non-create dispatch (absent by default)', async () => {
    const { publish } = makePublishSpy();
    const dispatchConsumer = makeDispatcher(publish);
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"complete","done":true,"result":{"tldr":"T."}}']));

    const result = await dispatchConsumer(BINDING_ID);

    expect(result.status).toBe('complete');
    expect(result.disposition).toBeUndefined();
    expect(result.consumerType).toBeUndefined();
  });
});

// ---------------------------------------------------------------------------
// Progressive section reveal pacing (task 039 / D-F5, FR-A1-10)
//
// D-F5 wants long dispatched outputs to render progressively (≥2 visible
// steps) rather than as one terminal blob, WITHOUT weakening ADR-040
// store-before-render. `chunk.result` here is the terminal, already-STORED
// payload the BFF only emits after IOutputRouter.RouteAsync's ledger write
// (asserted server-side by ProgressiveRenderGuard.EnsureStored) — these
// tests prove the client reveals it in real, perceptible steps rather than
// one synchronous batch, using genuine (short) delays rather than fake
// timers to avoid coupling to Jest's timer-mock internals.
// ---------------------------------------------------------------------------

describe('dispatchConsumer progressive section reveal pacing (task 039 / D-F5)', () => {
  it('reveals sections with a real pacing gap — the second section is NOT visible before the delay elapses', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = createConsumerDispatcher({
      bffBaseUrl: BFF_BASE,
      getSessionId: () => SESSION_ID,
      getAccessToken: async () => 'test-token',
      publishPaneEvent: publish,
      sectionRevealDelayMs: 150,
    });
    mockFetch.mockResolvedValueOnce(
      sseResponse([JSON.stringify({ type: 'complete', done: true, result: { tldr: 'A', keywords: ['x'] } })])
    );

    const dispatchPromise = dispatchConsumer(BINDING_ID, { streamId: 'pace-1' });

    // Well before the 150ms inter-section gap: only the FIRST section's pair
    // (plus streaming_started) should have been published.
    await new Promise(resolve => setTimeout(resolve, 30));
    expect(events.map(e => e.event.type)).toEqual(['streaming_started', 'section_started', 'section_completed']);

    const result = await dispatchPromise;

    // After the reveal (and its trailing streaming_complete) finishes, both
    // sections are present, in order, and the dispatch has resolved.
    expect(events.map(e => e.event.type)).toEqual([
      'streaming_started',
      'section_started',
      'section_completed',
      'section_started',
      'section_completed',
      'streaming_complete',
    ]);
    expect(result.status).toBe('complete');
  });

  it('never reveals a section for a non-"complete" chunk — pacing cannot introduce a render-ahead-of-store event', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = createConsumerDispatcher({
      bffBaseUrl: BFF_BASE,
      getSessionId: () => SESSION_ID,
      getAccessToken: async () => 'test-token',
      publishPaneEvent: publish,
      sectionRevealDelayMs: 0,
    });
    // A malformed/adversarial "text" chunk carrying a result-shaped payload —
    // section reveal must only ever be driven by a "complete" chunk (the ONE
    // chunk type the BFF emits after the ADR-040 ledger write).
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        JSON.stringify({ type: 'text', content: 'not a completion', result: { tldr: 'should never render' } }),
        '{"type":"complete","done":true}',
      ])
    );

    await dispatchConsumer(BINDING_ID, { streamId: 'pace-2' });

    expect(events.map(e => e.event.type)).toEqual(['streaming_started', 'streaming_complete']);
    expect(events.some(e => e.event.type === 'section_started' || e.event.type === 'section_completed')).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// suppressWorkspaceSectionBridge (spaarkeai-compose-r2 task 112, UAT-R3 defect
// #3c): the Compose surface has no `workspace`-channel section-renderer
// subscriber — publishing section events there was dead output, and awaiting
// the paced reveal needlessly delayed the dispatch Promise for a renderer
// nobody mounts. Additive + default-false: unset behaves exactly like every
// pre-existing test above (their dispatchers never set the flag).
// ---------------------------------------------------------------------------

describe('dispatchConsumer suppressWorkspaceSectionBridge (task 112, Compose-surface scoping)', () => {
  it('publishes ZERO workspace events for an object-shaped complete result when suppressed', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = createConsumerDispatcher({
      bffBaseUrl: BFF_BASE,
      getSessionId: () => SESSION_ID,
      getAccessToken: async () => 'test-token',
      publishPaneEvent: publish,
      sectionRevealDelayMs: 0,
      suppressWorkspaceSectionBridge: true,
    });
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        JSON.stringify({
          type: 'complete',
          done: true,
          result: { explanation: 'This clause caps liability.', keyConcepts: ['liability', 'cap'] },
        }),
      ])
    );

    const result = await dispatchConsumer(BINDING_ID, { streamId: 'compose-1' });

    // The dispatch still resolves with the terminal result (the injection seam
    // depends on this) — only the workspace-channel side effects are suppressed.
    expect(result.status).toBe('complete');
    expect(result.result).toEqual({ explanation: 'This clause caps liability.', keyConcepts: ['liability', 'cap'] });
    expect(events).toHaveLength(0);
  });

  it('does NOT publish widget_load / streaming_started for a workspaceTarget dispatch when suppressed', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = createConsumerDispatcher({
      bffBaseUrl: BFF_BASE,
      getSessionId: () => SESSION_ID,
      getAccessToken: async () => 'test-token',
      publishPaneEvent: publish,
      suppressWorkspaceSectionBridge: true,
    });
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"complete","done":true}']));

    await dispatchConsumer(BINDING_ID, {
      streamId: 'compose-2',
      workspaceTarget: { widgetType: 'structured-output-stream' },
    });

    expect(events).toHaveLength(0);
  });

  it('publishes ZERO workspace events on an error/declined terminal when suppressed', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = createConsumerDispatcher({
      bffBaseUrl: BFF_BASE,
      getSessionId: () => SESSION_ID,
      getAccessToken: async () => 'test-token',
      publishPaneEvent: publish,
      suppressWorkspaceSectionBridge: true,
    });
    mockFetch.mockResolvedValueOnce(sseResponse(['{"type":"error","error":"boom","done":true}']));

    await expect(dispatchConsumer(BINDING_ID, { streamId: 'compose-3' })).rejects.toThrow(/stream reported an error/);
    expect(events).toHaveLength(0);
  });

  it('regression: leaving the flag unset preserves the pre-task-112 section-bridge behavior', async () => {
    const { publish, events } = makePublishSpy();
    const dispatchConsumer = createConsumerDispatcher({
      bffBaseUrl: BFF_BASE,
      getSessionId: () => SESSION_ID,
      getAccessToken: async () => 'test-token',
      publishPaneEvent: publish,
      sectionRevealDelayMs: 0,
      // suppressWorkspaceSectionBridge intentionally omitted — default false.
    });
    mockFetch.mockResolvedValueOnce(
      sseResponse([
        JSON.stringify({ type: 'complete', done: true, result: { tldr: 'Short version', keywords: ['a'] } }),
      ])
    );

    await dispatchConsumer(BINDING_ID, { streamId: 'chip-1' });

    expect(events.map(e => e.event.type)).toEqual([
      'streaming_started',
      'section_started',
      'section_completed',
      'section_started',
      'section_completed',
      'streaming_complete',
    ]);
  });
});
