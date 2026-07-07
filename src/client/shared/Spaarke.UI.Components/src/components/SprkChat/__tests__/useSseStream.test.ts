/**
 * useSseStream Hook Tests
 *
 * Tests the SSE stream parsing and state management.
 * Covers: parseSseEvent utility, stream lifecycle, cancellation, error handling.
 * Task 045 (FR-P3-06): readSseStream FormData + fetchImpl extension behavior.
 *
 * @see ADR-022 - React 16 APIs only
 */

import { parseSseEvent, readSseStream } from '../hooks/useSseStream';

// jsdom does not polyfill TextEncoder / TextDecoder in all versions — use
// Node's implementations (same pattern as the dispatchConsumer tests).
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

// ---------------------------------------------------------------------------
// parseSseEvent unit tests
// ---------------------------------------------------------------------------

describe('parseSseEvent', () => {
  it('should parse a valid token event', () => {
    const result = parseSseEvent('data: {"type":"token","content":"Hello"}');
    expect(result).toEqual({ type: 'token', content: 'Hello' });
  });

  it('should parse a done event', () => {
    const result = parseSseEvent('data: {"type":"done","content":null}');
    expect(result).toEqual({ type: 'done', content: null });
  });

  it('should parse an error event', () => {
    const result = parseSseEvent('data: {"type":"error","content":"Something went wrong"}');
    expect(result).toEqual({ type: 'error', content: 'Something went wrong' });
  });

  it('should return null for lines without data prefix', () => {
    expect(parseSseEvent('id: 123')).toBeNull();
    expect(parseSseEvent('event: message')).toBeNull();
    expect(parseSseEvent(': comment')).toBeNull();
    expect(parseSseEvent('random text')).toBeNull();
  });

  it('should return null for empty data payload', () => {
    expect(parseSseEvent('data: ')).toBeNull();
    expect(parseSseEvent('data:')).toBeNull();
  });

  it('should return null for invalid JSON', () => {
    expect(parseSseEvent('data: not-json')).toBeNull();
    expect(parseSseEvent('data: {broken')).toBeNull();
  });

  it('should return null for empty strings', () => {
    expect(parseSseEvent('')).toBeNull();
    expect(parseSseEvent('   ')).toBeNull();
  });

  it('should handle whitespace around the line', () => {
    const result = parseSseEvent('  data: {"type":"token","content":"Hi"}  ');
    expect(result).toEqual({ type: 'token', content: 'Hi' });
  });

  it('should return null for objects missing type field', () => {
    expect(parseSseEvent('data: {"content":"test"}')).toBeNull();
  });

  it('should handle token events with empty content', () => {
    const result = parseSseEvent('data: {"type":"token","content":""}');
    expect(result).toEqual({ type: 'token', content: '' });
  });

  it('should handle token events with special characters in content', () => {
    const result = parseSseEvent('data: {"type":"token","content":"Hello\\nWorld"}');
    expect(result).toEqual({ type: 'token', content: 'Hello\nWorld' });
  });

  it('should handle done event without content field', () => {
    const result = parseSseEvent('data: {"type":"done"}');
    expect(result).not.toBeNull();
    expect(result!.type).toBe('done');
  });
});

// ---------------------------------------------------------------------------
// readSseStream — task 045 (FR-P3-06, NFR-08) FormData + fetchImpl extension
// ---------------------------------------------------------------------------

/** Response stand-in carrying an SSE body (same pattern as dispatchConsumer tests). */
function sseResponse(wire: string, status = 200): Response {
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

describe('readSseStream — auth-mode validation (task 045)', () => {
  it('throws a clear error when NEITHER getAccessToken nor fetchImpl is provided', async () => {
    await expect(readSseStream({ url: 'https://bff.test/stream', body: {}, onLine: () => undefined })).rejects.toThrow(
      /exactly one of getAccessToken or fetchImpl.*neither/
    );
  });

  it('throws a clear error when BOTH getAccessToken and fetchImpl are provided', async () => {
    await expect(
      readSseStream({
        url: 'https://bff.test/stream',
        body: {},
        getAccessToken: async () => 'token',
        fetchImpl: async () => sseResponse(''),
        onLine: () => undefined,
      })
    ).rejects.toThrow(/exactly one of getAccessToken or fetchImpl.*both/);
  });
});

describe('readSseStream — fetchImpl mode (task 045)', () => {
  it('calls fetchImpl with POST/json headers/stringified body/signal and does NOT attach Authorization', async () => {
    const fetchImpl = jest.fn().mockResolvedValue(sseResponse('data: {"type":"token","content":"Hi"}\n\n'));
    const controller = new AbortController();
    const lines: string[] = [];

    await readSseStream({
      url: 'https://bff.test/stream',
      body: { message: 'hello' },
      fetchImpl,
      signal: controller.signal,
      onLine: line => lines.push(line),
    });

    expect(fetchImpl).toHaveBeenCalledTimes(1);
    const [url, init] = fetchImpl.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('https://bff.test/stream');
    expect(init.method).toBe('POST');
    expect(init.body).toBe(JSON.stringify({ message: 'hello' }));
    expect(init.signal).toBe(controller.signal);
    const headers = init.headers as Record<string, string>;
    expect(headers['Content-Type']).toBe('application/json');
    // ADR-028: the caller's authenticatedFetch owns auth — no Authorization /
    // X-Tenant-Id from the primitive in fetchImpl mode.
    expect(headers.Authorization).toBeUndefined();
    expect(headers['X-Tenant-Id']).toBeUndefined();

    expect(lines).toContain('data: {"type":"token","content":"Hi"}');
  });

  it('passes FormData through verbatim WITHOUT a Content-Type header (browser sets the boundary)', async () => {
    const fetchImpl = jest.fn().mockResolvedValue(sseResponse('data: {"type":"progress","step":"analyzing"}\n\n'));
    const form = new FormData();
    form.append('files', new Blob(['abc']), 'a.txt');
    const events: Array<ReturnType<typeof parseSseEvent>> = [];

    await readSseStream({
      url: 'https://bff.test/summarize',
      body: form,
      fetchImpl,
      onLine: line => {
        const evt = parseSseEvent(line);
        if (evt) events.push(evt);
      },
    });

    const [, init] = fetchImpl.mock.calls[0] as [string, RequestInit];
    expect(init.body).toBe(form); // pass-through, not stringified
    const headers = init.headers as Record<string, string>;
    expect(headers['Content-Type']).toBeUndefined();
    expect(events).toEqual([{ type: 'progress', step: 'analyzing' }]);
  });

  it('throws the mapHttpError result on non-OK responses in fetchImpl mode', async () => {
    const fetchImpl = jest.fn().mockResolvedValue(sseResponse('nope', 500));

    await expect(
      readSseStream({
        url: 'https://bff.test/stream',
        body: {},
        fetchImpl,
        mapHttpError: async response => new Error(`custom failure (${response.status})`),
        onLine: () => undefined,
      })
    ).rejects.toThrow('custom failure (500)');
  });

  it('delivers the trailing remainder buffer (final event without a closing blank line)', async () => {
    const fetchImpl = jest
      .fn()
      .mockResolvedValue(sseResponse('data: {"type":"token","content":"a"}\n\ndata: {"type":"done"}'));
    const types: string[] = [];

    await readSseStream({
      url: 'https://bff.test/stream',
      body: {},
      fetchImpl,
      onLine: line => {
        const evt = parseSseEvent(line);
        if (evt) types.push(evt.type);
      },
    });

    expect(types).toEqual(['token', 'done']);
  });
});

describe('readSseStream — getAccessToken mode unchanged (task 045 regression)', () => {
  const originalFetch = global.fetch;
  afterEach(() => {
    (global as unknown as { fetch: typeof fetch }).fetch = originalFetch;
  });

  it('attaches a fresh Bearer token + derived X-Tenant-Id + json Content-Type', async () => {
    const fetchMock = jest.fn().mockResolvedValue(sseResponse('data: {"type":"done"}\n\n'));
    (global as unknown as { fetch: typeof fetch }).fetch = fetchMock as unknown as typeof fetch;

    // Minimal JWT-shaped token whose payload carries a `tid` claim.
    const payload = Buffer.from(JSON.stringify({ tid: 'tenant-123' })).toString('base64');
    const token = `header.${payload}.sig`;
    const getAccessToken = jest.fn().mockResolvedValue(token);

    await readSseStream({
      url: 'https://bff.test/stream',
      body: { message: 'hi' },
      getAccessToken,
      onLine: () => undefined,
    });

    expect(getAccessToken).toHaveBeenCalledTimes(1);
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = init.headers as Record<string, string>;
    expect(headers.Authorization).toBe(`Bearer ${token}`);
    expect(headers['X-Tenant-Id']).toBe('tenant-123');
    expect(headers['Content-Type']).toBe('application/json');
    expect(init.body).toBe(JSON.stringify({ message: 'hi' }));
  });
});
