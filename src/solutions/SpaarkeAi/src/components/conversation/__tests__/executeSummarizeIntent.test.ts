/**
 * executeSummarizeIntent.test.ts — R5 task 036 / P2-CLOSEOUT-05.
 *
 * Covers:
 *   - Happy 1-file path
 *   - Happy 2-file path (multi-file promotion + summarize)
 *   - /documents 400 → abort, /summarize NOT called
 *   - /summarize 500 (pre-stream) → emits declined + throws
 *   - SSE parse error (malformed JSON line) → tolerated
 *   - Abort signal propagates
 *   - context.files_staged dispatched AFTER promotion success
 */

import {
  executeSummarizeIntent,
  type HeldFile,
} from '../executeSummarizeIntent';
import type { PaneChannel, PaneChannelEventMap } from '@spaarke/ai-widgets';

// jsdom (used by the SpaarkeAi jest preset for React tests) does NOT polyfill
// TextEncoder / TextDecoder by default in older versions. Use Node's
// util.TextEncoder / TextDecoder as drop-in replacements so the SSE wire-format
// helper below + the production code's decoder both work in tests.
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

// ---------------------------------------------------------------------------
// Test helpers
// ---------------------------------------------------------------------------

interface PublishedEvent<C extends PaneChannel = PaneChannel> {
  channel: C;
  event: PaneChannelEventMap[C];
}

function makePublishSpy() {
  const events: PublishedEvent[] = [];
  const publish = <C extends PaneChannel>(
    channel: C,
    event: PaneChannelEventMap[C]
  ): void => {
    events.push({ channel, event } as PublishedEvent);
  };
  return { publish, events };
}

function makeFile(name: string, content = 'hello', mime = 'application/pdf'): File {
  // Polyfill File when not available (e.g. jsdom < 14). Jest's jsdom should
  // provide File natively; fall back to a minimal Blob wrapper if not.
  if (typeof File !== 'undefined') {
    return new File([content], name, { type: mime });
  }
  const blob = new Blob([content], { type: mime }) as Blob & { name?: string };
  blob.name = name;
  return blob as unknown as File;
}

function makeHeldFile(id: string, name: string): HeldFile {
  return { id, file: makeFile(name) };
}

/**
 * Build a Response-like for a JSON body (one-shot fetch). Provides `ok`,
 * `status`, `json()`. The default `headers`, `body`, etc. are stubbed.
 */
function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    headers: new Headers(),
    body: null,
  } as unknown as Response;
}

/**
 * Build a Response-like for an SSE stream. Accepts an array of payload
 * lines (each will be wrapped as `data: <line>\n\n`).
 *
 * The returned Response has a `body` with a `getReader()` that yields the
 * full payload in one chunk then closes.
 */
function sseResponse(chunks: string[], status = 200): Response {
  const wire = chunks.map((c) => `data: ${c}\n\n`).join('');
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
    body: { getReader: () => reader },
    headers: new Headers(),
  } as unknown as Response;
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('executeSummarizeIntent — happy paths', () => {
  beforeEach(() => {
    // Provide a global fetch that the SSE consumer uses.
    // Each test overrides this via jest.spyOn.
    (globalThis.fetch as unknown) = undefined;
  });

  test('1 file: promotes via /documents, dispatches files_staged, streams /summarize', async () => {
    const auth = jest.fn(async (_url: string, _init?: RequestInit) =>
      jsonResponse({ documentId: 'doc-1', filename: 'a.pdf', status: 'ready' }, 202)
    );
    const getToken = jest.fn(async () => 'tok-abc');
    const { publish, events } = makePublishSpy();

    const sseChunks = [
      JSON.stringify({ type: 'delta', delta: { path: 'tldr', content: 'Hi', sequence: 1 } }),
      JSON.stringify({ type: 'complete', done: true }),
    ];
    global.fetch = jest.fn(async () => sseResponse(sseChunks)) as unknown as typeof fetch;

    const result = await executeSummarizeIntent({
      bffBaseUrl: BFF_BASE,
      sessionId: SESSION_ID,
      heldFiles: [makeHeldFile('chip-1', 'a.pdf')],
      authenticatedFetch: auth,
      getAccessToken: getToken,
      publishPaneEvent: publish,
    });

    expect(auth).toHaveBeenCalledTimes(1);
    expect(auth.mock.calls[0][0]).toBe(`${BFF_BASE}/api/ai/chat/sessions/${SESSION_ID}/documents`);
    expect((auth.mock.calls[0][1] as RequestInit).method).toBe('POST');

    expect(result.documentIds).toEqual(['doc-1']);
    expect(result.filenames).toEqual(['a.pdf']);

    // context.files_staged emitted after promotion + BEFORE SSE
    const stagedIdx = events.findIndex(
      (e) => e.channel === 'context' && (e.event as { type: string }).type === 'files_staged'
    );
    expect(stagedIdx).toBeGreaterThanOrEqual(0);
    expect((events[stagedIdx].event as { stagedFileIds?: string[] }).stagedFileIds).toEqual([
      'doc-1',
    ]);

    // workspace.streaming_started precedes field_delta precedes streaming_complete
    const workspaceEvents = events
      .filter((e) => e.channel === 'workspace')
      .map((e) => (e.event as { type: string }).type);
    expect(workspaceEvents).toEqual(['streaming_started', 'field_delta', 'streaming_complete']);

    // getAccessToken invoked exactly once for the stream open
    expect(getToken).toHaveBeenCalledTimes(1);
  });

  test('2 files: promotes both, single files_staged with both ids, streams /summarize', async () => {
    let docCallCount = 0;
    const auth = jest.fn(async () => {
      docCallCount += 1;
      return jsonResponse(
        { documentId: `doc-${docCallCount}`, filename: `f${docCallCount}.pdf`, status: 'ready' },
        202
      );
    });
    const getToken = jest.fn(async () => 'tok-abc');
    const { publish, events } = makePublishSpy();

    const sseChunks = [
      JSON.stringify({
        type: 'delta',
        delta: { path: 'summary', content: 'token', sequence: 1 },
      }),
      JSON.stringify({ type: 'complete', done: true }),
    ];
    global.fetch = jest.fn(async () => sseResponse(sseChunks)) as unknown as typeof fetch;

    const result = await executeSummarizeIntent({
      bffBaseUrl: BFF_BASE,
      sessionId: SESSION_ID,
      heldFiles: [makeHeldFile('chip-1', 'a.pdf'), makeHeldFile('chip-2', 'b.pdf')],
      authenticatedFetch: auth,
      getAccessToken: getToken,
      publishPaneEvent: publish,
    });

    expect(auth).toHaveBeenCalledTimes(2);
    expect(result.documentIds).toEqual(['doc-1', 'doc-2']);

    const staged = events.find(
      (e) => e.channel === 'context' && (e.event as { type: string }).type === 'files_staged'
    );
    expect((staged?.event as { stagedFileIds?: string[] }).stagedFileIds).toEqual([
      'doc-1',
      'doc-2',
    ]);
  });

  test('propagates styleHint into /summarize body', async () => {
    const auth = jest.fn(async () =>
      jsonResponse({ documentId: 'doc-1', filename: 'a.pdf', status: 'ready' }, 202)
    );
    const getToken = jest.fn(async () => 'tok-xyz');
    const { publish } = makePublishSpy();

    const fetchSpy = jest.fn(async (_url: string, _init?: RequestInit) =>
      sseResponse([JSON.stringify({ type: 'complete', done: true })])
    );
    global.fetch = fetchSpy as unknown as typeof fetch;

    await executeSummarizeIntent({
      bffBaseUrl: BFF_BASE,
      sessionId: SESSION_ID,
      heldFiles: [makeHeldFile('chip-1', 'a.pdf')],
      authenticatedFetch: auth,
      getAccessToken: getToken,
      publishPaneEvent: publish,
      styleHint: 'concise',
    });

    const init = fetchSpy.mock.calls[0][1] as RequestInit;
    const body = JSON.parse(init.body as string) as { fileIds: string[]; style?: string };
    expect(body.fileIds).toEqual(['doc-1']);
    expect(body.style).toBe('concise');
  });
});

describe('executeSummarizeIntent — error paths', () => {
  test('/documents 400: aborts, /summarize NOT called, throws', async () => {
    const auth = jest.fn(async () =>
      jsonResponse({ errorCode: 'summarize.too-many-files', title: 'Bad Request' }, 400)
    );
    const getToken = jest.fn(async () => 'tok');
    const summarizeFetch = jest.fn(async () =>
      sseResponse([JSON.stringify({ type: 'complete', done: true })])
    );
    global.fetch = summarizeFetch as unknown as typeof fetch;
    const { publish, events } = makePublishSpy();

    await expect(
      executeSummarizeIntent({
        bffBaseUrl: BFF_BASE,
        sessionId: SESSION_ID,
        heldFiles: [makeHeldFile('chip-1', 'a.pdf')],
        authenticatedFetch: auth,
        getAccessToken: getToken,
        publishPaneEvent: publish,
      })
    ).rejects.toThrow(/documents POST failed/);

    expect(auth).toHaveBeenCalledTimes(1);
    expect(summarizeFetch).not.toHaveBeenCalled();

    // No context.files_staged should be dispatched.
    const staged = events.find(
      (e) => e.channel === 'context' && (e.event as { type: string }).type === 'files_staged'
    );
    expect(staged).toBeUndefined();
  });

  test('/documents partial failure (2nd of 3) aborts before /summarize', async () => {
    let callCount = 0;
    const auth = jest.fn(async () => {
      callCount += 1;
      if (callCount === 2) {
        return jsonResponse({ errorCode: 'documents.upload-failed' }, 500);
      }
      return jsonResponse(
        { documentId: `doc-${callCount}`, filename: `f${callCount}.pdf`, status: 'ready' },
        202
      );
    });
    const getToken = jest.fn(async () => 'tok');
    const summarizeFetch = jest.fn();
    global.fetch = summarizeFetch as unknown as typeof fetch;
    const { publish } = makePublishSpy();

    await expect(
      executeSummarizeIntent({
        bffBaseUrl: BFF_BASE,
        sessionId: SESSION_ID,
        heldFiles: [
          makeHeldFile('chip-1', 'a.pdf'),
          makeHeldFile('chip-2', 'b.pdf'),
          makeHeldFile('chip-3', 'c.pdf'),
        ],
        authenticatedFetch: auth,
        getAccessToken: getToken,
        publishPaneEvent: publish,
      })
    ).rejects.toThrow(/documents POST failed/);

    // Should have stopped at call 2 — call 3 never happens.
    expect(auth).toHaveBeenCalledTimes(2);
    expect(summarizeFetch).not.toHaveBeenCalled();
  });

  test('/summarize 500: emits declined and throws', async () => {
    const auth = jest.fn(async () =>
      jsonResponse({ documentId: 'doc-1', filename: 'a.pdf', status: 'ready' }, 202)
    );
    const getToken = jest.fn(async () => 'tok');
    global.fetch = jest.fn(async () =>
      jsonResponse({ errorCode: 'summarize.internal-error' }, 500)
    ) as unknown as typeof fetch;
    const { publish, events } = makePublishSpy();

    await expect(
      executeSummarizeIntent({
        bffBaseUrl: BFF_BASE,
        sessionId: SESSION_ID,
        heldFiles: [makeHeldFile('chip-1', 'a.pdf')],
        authenticatedFetch: auth,
        getAccessToken: getToken,
        publishPaneEvent: publish,
      })
    ).rejects.toThrow(/summarize POST failed/);

    // context.files_staged still dispatched (promotion succeeded).
    expect(
      events.find((e) => e.channel === 'context' && (e.event as { type: string }).type === 'files_staged')
    ).toBeDefined();
    // streaming_complete with declined emitted on the workspace channel.
    const completeEvt = events.find(
      (e) => e.channel === 'workspace' && (e.event as { type: string }).type === 'streaming_complete'
    );
    expect(completeEvt).toBeDefined();
    expect((completeEvt?.event as { completionStatus?: string }).completionStatus).toBe('declined');
  });

  test('SSE malformed JSON line is skipped, stream continues', async () => {
    const auth = jest.fn(async () =>
      jsonResponse({ documentId: 'doc-1', filename: 'a.pdf', status: 'ready' }, 202)
    );
    const getToken = jest.fn(async () => 'tok');
    const sseChunks = [
      '{this is not json',
      JSON.stringify({ type: 'delta', delta: { path: 'tldr', content: 'OK', sequence: 1 } }),
      JSON.stringify({ type: 'complete', done: true }),
    ];
    global.fetch = jest.fn(async () => sseResponse(sseChunks)) as unknown as typeof fetch;
    const { publish, events } = makePublishSpy();

    // Should NOT throw; the malformed event is skipped.
    await executeSummarizeIntent({
      bffBaseUrl: BFF_BASE,
      sessionId: SESSION_ID,
      heldFiles: [makeHeldFile('chip-1', 'a.pdf')],
      authenticatedFetch: auth,
      getAccessToken: getToken,
      publishPaneEvent: publish,
    });

    const workspaceTypes = events
      .filter((e) => e.channel === 'workspace')
      .map((e) => (e.event as { type: string }).type);
    // streaming_started + field_delta + streaming_complete (3 events)
    expect(workspaceTypes).toEqual(['streaming_started', 'field_delta', 'streaming_complete']);
  });

  test('AbortSignal propagates to fetch calls', async () => {
    const auth = jest.fn(async (_url: string, init?: RequestInit) => {
      // Echo the signal back so we can assert it was passed.
      if (init?.signal?.aborted) {
        const err = new Error('aborted');
        err.name = 'AbortError';
        throw err;
      }
      return jsonResponse({ documentId: 'doc-1', filename: 'a.pdf', status: 'ready' }, 202);
    });
    const getToken = jest.fn(async () => 'tok');
    global.fetch = jest.fn(async () =>
      sseResponse([JSON.stringify({ type: 'complete', done: true })])
    ) as unknown as typeof fetch;
    const { publish } = makePublishSpy();

    const controller = new AbortController();
    controller.abort();

    await expect(
      executeSummarizeIntent({
        bffBaseUrl: BFF_BASE,
        sessionId: SESSION_ID,
        heldFiles: [makeHeldFile('chip-1', 'a.pdf')],
        authenticatedFetch: auth,
        getAccessToken: getToken,
        publishPaneEvent: publish,
        signal: controller.signal,
      })
    ).rejects.toThrow();
  });

  test('throws on empty heldFiles array', async () => {
    const auth = jest.fn();
    const getToken = jest.fn();
    const { publish } = makePublishSpy();

    await expect(
      executeSummarizeIntent({
        bffBaseUrl: BFF_BASE,
        sessionId: SESSION_ID,
        heldFiles: [],
        authenticatedFetch: auth,
        getAccessToken: getToken,
        publishPaneEvent: publish,
      })
    ).rejects.toThrow(/heldFiles must be non-empty/);

    expect(auth).not.toHaveBeenCalled();
  });

  test('throws on missing sessionId', async () => {
    const { publish } = makePublishSpy();
    await expect(
      executeSummarizeIntent({
        bffBaseUrl: BFF_BASE,
        sessionId: '',
        heldFiles: [makeHeldFile('chip-1', 'a.pdf')],
        authenticatedFetch: jest.fn(),
        getAccessToken: jest.fn(),
        publishPaneEvent: publish,
      })
    ).rejects.toThrow(/sessionId is required/);
  });
});
