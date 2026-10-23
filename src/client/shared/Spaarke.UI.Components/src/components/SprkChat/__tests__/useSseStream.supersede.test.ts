/**
 * useSseStream - superseded stream must not write state (#1906 review, F1)
 *
 * The server keeps the response open briefly after `done` while it persists
 * history. A send in that window starts stream #2, which aborts #1; #1's
 * AbortError catch then ran setIsStreaming(false) AFTER #2 set it true, so #2
 * streamed with isStreaming=false (Stop hidden, composer enabled).
 */

import { renderHook, act, waitFor } from '@testing-library/react';
import { useSseStream } from '../hooks/useSseStream';

import { TextEncoder, TextDecoder } from 'util';
(global as any).TextEncoder = TextEncoder;
(global as any).TextDecoder = TextDecoder;

const mockFetch = jest.fn();
(global as any).fetch = mockFetch;

/**
 * A response that delivers `events` in one read, then stays open until the
 * request's AbortSignal fires (like a server still closing after `done`).
 */
function openResponse(events: Array<Record<string, unknown>>, signal: AbortSignal): Response {
  const chunk = new TextEncoder().encode(events.map(e => `data: ${JSON.stringify(e)}\n\n`).join(''));
  let delivered = false;
  return {
    ok: true,
    status: 200,
    body: {
      getReader: () => ({
        read: () => {
          if (!delivered) {
            delivered = true;
            return Promise.resolve({ done: false, value: chunk });
          }
          return new Promise((_, reject) => {
            signal.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
          });
        },
        releaseLock: () => {},
      }),
    },
  } as unknown as Response;
}

const getToken = () => Promise.resolve('header.e30.sig');

describe('useSseStream - superseded stream', () => {
  beforeEach(() => mockFetch.mockReset());

  it('abortedPreviousStream_DoesNotClearNewStreamIsStreaming', async () => {
    mockFetch
      .mockImplementationOnce((_url: string, init: RequestInit) =>
        Promise.resolve(
          openResponse(
            [
              { type: 'token', content: 'First answer.' },
              { type: 'done', content: null },
            ],
            init.signal as AbortSignal
          )
        )
      )
      .mockImplementationOnce((_url: string, init: RequestInit) =>
        Promise.resolve(openResponse([{ type: 'token', content: 'Second' }], init.signal as AbortSignal))
      );

    const { result } = renderHook(() => useSseStream());

    act(() => {
      result.current.startStream('https://api/x', { message: 'one' }, getToken);
    });
    await waitFor(() => expect(result.current.isDone).toBe(true));
    expect(result.current.isStreaming).toBe(false);

    // Send again while stream #1's response is still open.
    act(() => {
      result.current.startStream('https://api/x', { message: 'two' }, getToken);
    });
    await waitFor(() => expect(result.current.content).toBe('Second'));
    // Let stream #1's AbortError catch run.
    await act(async () => {
      await new Promise(r => setTimeout(r, 0));
    });

    expect(result.current.isStreaming).toBe(true);
    expect(result.current.error).toBeNull();

    // Stream #2 never closes on its own.
    act(() => result.current.cancelStream());
  });
});
