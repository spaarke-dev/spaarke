/**
 * useAiSummary terminal-error tests (ADR-019: a stream that fails after it started ends in ONE terminal `error`
 * event; the consumer must end in the error state and show it, never stop silently or keep a truncated result).
 *
 * Drives the REAL hook and the REAL readSseStream / parseSseEvent over a scripted `fetch`: the playbook lookup
 * answers 200, then `/api/ai/analysis/execute` streams a chunk and fails the way the BFF now reports it - one `error`
 * chunk carrying the fixed message, the stable `errorCode` and the `correlationId`.
 */

import { act, renderHook, waitFor } from '@testing-library/react';
import { TextDecoder, TextEncoder } from 'util';
import { useAiSummary } from '../useAiSummary';

// jsdom has no TextDecoder; readSseStream decodes the response body with one.
if (typeof globalThis.TextDecoder === 'undefined') {
  (globalThis as unknown as { TextDecoder: typeof TextDecoder }).TextDecoder = TextDecoder;
}

const BASE_URL = 'https://spe-api-test.example.com';

const DOC = {
  documentId: 'doc-1111',
  driveId: 'drive-aaaa',
  itemId: 'item-bbbb',
  fileName: 'contract.pdf',
};

const SAFE_MESSAGE = 'The playbook run failed. See the server log for details.';

function jsonResponse(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    headers: new Headers({ 'content-type': 'application/json' }),
    json: jest.fn().mockResolvedValue(body),
    text: jest.fn().mockResolvedValue(JSON.stringify(body)),
    body: null,
  } as unknown as Response;
}

/** An SSE response whose body delivers each frame as its own read, then ends. */
function sseResponse(frames: string[]): Response {
  const encoder = new TextEncoder();
  const queue = frames.map(f => encoder.encode(`${f}\n\n`));
  return {
    ok: true,
    status: 200,
    headers: new Headers({ 'content-type': 'text/event-stream' }),
    body: {
      getReader: () => ({
        read: async () => (queue.length ? { done: false, value: queue.shift() } : { done: true, value: undefined }),
        releaseLock: () => undefined,
      }),
    },
  } as unknown as Response;
}

const data = (chunk: unknown) => `data: ${JSON.stringify(chunk)}`;

describe('useAiSummary - terminal error event', () => {
  const originalFetch = global.fetch;
  let fetchMock: jest.Mock;

  beforeEach(() => {
    jest.spyOn(console, 'log').mockImplementation(() => undefined);
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    fetchMock = jest.fn();
    (global as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    (global as unknown as { fetch: typeof fetch }).fetch = originalFetch;
    jest.restoreAllMocks();
  });

  it('ends in the error state and shows the server message and correlation id when the stream fails after it started', async () => {
    fetchMock.mockResolvedValueOnce(jsonResponse({ playbookId: 'pb-1' })).mockResolvedValueOnce(
      sseResponse([
        data({ type: 'metadata', content: null, done: false, analysisId: 'a-1' }),
        data({ type: 'chunk', content: 'Partial summary. ', done: false }),
        data({
          type: 'error',
          content: null,
          done: true,
          error: SAFE_MESSAGE,
          errorCode: 'playbook.run-failed',
          correlationId: '0HNABC123:00000001',
        }),
      ])
    );

    const { result } = renderHook(() => useAiSummary({ apiBaseUrl: BASE_URL, autoStart: false }));
    act(() => {
      result.current.addDocuments([DOC]);
    });
    act(() => {
      result.current.startAll();
    });

    await waitFor(() => expect(result.current.documents[0]?.status).toBe('error'));

    const doc = result.current.documents[0];
    expect(doc.error).toContain(SAFE_MESSAGE);
    expect(doc.error).toContain('0HNABC123:00000001');
    expect(doc.status).not.toBe('complete');
  });

  it('still shows the plain message for an error event that carries no correlation id', async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ playbookId: 'pb-1' }))
      .mockResolvedValueOnce(
        sseResponse([data({ type: 'error', content: null, done: true, error: 'Document has no extractable text.' })])
      );

    const { result } = renderHook(() => useAiSummary({ apiBaseUrl: BASE_URL, autoStart: false }));
    act(() => {
      result.current.addDocuments([DOC]);
    });
    act(() => {
      result.current.startAll();
    });

    await waitFor(() => expect(result.current.documents[0]?.status).toBe('error'));
    expect(result.current.documents[0].error).toBe('Document has no extractable text.');
  });
});

describe('useAiSummary - progress chunks', () => {
  const originalFetch = global.fetch;
  let fetchMock: jest.Mock;

  beforeEach(() => {
    jest.spyOn(console, 'log').mockImplementation(() => undefined);
    fetchMock = jest.fn();
    (global as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;
  });

  afterEach(() => {
    (global as unknown as { fetch: typeof fetch }).fetch = originalFetch;
    jest.restoreAllMocks();
  });

  it('keeps progress status lines out of the summary text', async () => {
    fetchMock
      .mockResolvedValueOnce(jsonResponse({ playbookId: 'pb-1' }))
      .mockResolvedValueOnce(
        sseResponse([
          data({ type: 'progress', content: 'Extracting document text…', done: false, step: 'extracting_text' }),
          data({ type: 'chunk', content: 'The summary.', done: false }),
          data({ type: 'progress', content: 'Updating document record…', done: false, step: 'updating_record' }),
          data({ type: 'done', content: null, done: true, analysisId: 'a-1' }),
        ])
      );

    const { result } = renderHook(() => useAiSummary({ apiBaseUrl: BASE_URL, autoStart: false }));
    act(() => {
      result.current.addDocuments([DOC]);
    });
    act(() => {
      result.current.startAll();
    });

    await waitFor(() => expect(result.current.documents[0]?.status).toBe('complete'));
    expect(result.current.documents[0].summary).toBe('The summary.');
  });
});
