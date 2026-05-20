/**
 * SSE Client using fetch + ReadableStream
 *
 * Native EventSource does not support custom headers (like Authorization).
 * This implementation uses fetch with ReadableStream to parse SSE format
 * while supporting bearer token authentication.
 *
 * Per spec.md: MUST use fetch + ReadableStream for SSE (not EventSource)
 *
 * Auth v2 (D-AUTH-7): Accepts a `getAccessToken` getter rather than a token string.
 * The getter is invoked ONCE per stream open (and again on 401 mid-stream retry),
 * immediately before the fetch is issued — so the token is always fresh for THIS
 * connection. The token is NEVER snapshotted at construction time and NEVER reused
 * across reconnects. This eliminates the class of bug where a token captured at
 * mount time would expire mid-session, producing silent 401 failures on streams
 * that have no token-refresh path (mirrors the SprkChat `useSseStream.ts`
 * Wave 1 fix from task AUTHV2-023).
 *
 * NOTE: `authenticatedFetch` cannot be used here because SSE requires streaming
 * the ReadableStream body, which the wrapper function does not expose. The
 * raw `Authorization: Bearer ${token}` header below is an intentional
 * D-AUTH-7 exception site.
 */

/**
 * Function that resolves to a fresh BFF access token.
 *
 * Implementations MUST acquire a fresh token per invocation (typically via MSAL's
 * own cache, which handles silent refresh) and MUST NOT cache the resolved string
 * outside MSAL.
 */
export type AccessTokenGetter = () => Promise<string>;

export interface SseEvent {
  /** Event type (e.g., 'stage-update', 'job-complete') */
  event?: string;
  /** Event data (JSON parsed) */
  data: unknown;
  /** Event ID for Last-Event-ID reconnection */
  id?: string;
  /** Retry interval hint from server (ms) */
  retry?: number;
}

export interface SseClientOptions {
  /**
   * Function returning a fresh BFF access token. Invoked immediately before each
   * fetch (initial connect and any 401 reconnect) so the token is always fresh
   * for THIS stream open. Never snapshotted.
   */
  getAccessToken: AccessTokenGetter;
  /** Called when an event is received */
  onEvent: (event: SseEvent) => void;
  /** Called when an error occurs */
  onError: (error: Error) => void;
  /** Called when the connection is closed */
  onClose?: () => void;
  /** Called when the connection is established */
  onOpen?: () => void;
  /** Last event ID for reconnection */
  lastEventId?: string;
  /** Request timeout in ms (default: 30000) */
  timeout?: number;
  /** Maximum 401 reconnect attempts (default: 3) */
  maxAuthRetries?: number;
}

export interface SseConnection {
  /** Close the SSE connection */
  close: () => void;
  /** Whether the connection is currently open */
  isConnected: () => boolean;
}

/**
 * Creates an SSE connection using fetch + ReadableStream.
 *
 * Token freshness contract:
 *   - `options.getAccessToken()` is invoked immediately before each `fetch` call
 *   - On 401 from the server, the connection auto-reconnects with a freshly
 *     acquired token (up to `maxAuthRetries` times, default 3)
 *   - The token string is never stored on the closure beyond the lifetime of
 *     a single fetch call
 *
 * @param url The SSE endpoint URL
 * @param options Connection options including auth-token getter and callbacks
 * @returns Connection object with close method
 *
 * @example
 * ```typescript
 * const connection = createSseConnection('/office/jobs/123/stream', {
 *   getAccessToken: () => authService.getAccessToken(['user_impersonation']),
 *   onEvent: (event) => console.log('Event:', event),
 *   onError: (error) => console.error('Error:', error),
 * });
 *
 * // Later, to close:
 * connection.close();
 * ```
 */
export function createSseConnection(url: string, options: SseClientOptions): SseConnection {
  const {
    getAccessToken,
    onEvent,
    onError,
    onClose,
    onOpen,
    lastEventId: initialLastEventId,
    timeout = 30000,
    maxAuthRetries = 3,
  } = options;

  let abortController: AbortController | null = new AbortController();
  let isConnected = false;
  let authRetries = 0;
  // Track the last-seen event ID across reconnects so the server can resume from
  // the right place. Seeded from caller's `lastEventId`, then updated as events
  // arrive.
  let currentLastEventId = initialLastEventId;

  const connect = async (): Promise<void> => {
    if (!abortController) {
      return;
    }

    // Auth v2 (D-AUTH-7): re-acquire a fresh token for THIS stream open.
    // Never snapshot; never reuse across reconnects.
    const accessToken = await getAccessToken();

    const headers: HeadersInit = {
      Accept: 'text/event-stream',
      // Auth v2 (D-AUTH-7): raw Bearer header is required because SSE streams
      // the ReadableStream body, which `authenticatedFetch` does not expose.
      // The token comes from a fresh `getAccessToken()` call above.
      Authorization: `Bearer ${accessToken}`,
      'Cache-Control': 'no-cache',
    };

    if (currentLastEventId) {
      headers['Last-Event-ID'] = currentLastEventId;
    }

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers,
        signal: abortController.signal,
        cache: 'no-store',
      });

      // Auth v2 (D-AUTH-7): on 401, close current stream, re-fetch fresh token,
      // and reopen. Capped at `maxAuthRetries` to avoid infinite loops if the
      // user's account is genuinely revoked.
      if (response.status === 401 && authRetries < maxAuthRetries && abortController) {
        authRetries += 1;
        // Drain the body to release the underlying connection before reconnecting.
        // (Failing to consume the body of a 401 can leak the socket on some hosts.)
        await response.body?.cancel().catch(() => {
          /* best-effort drain */
        });
        // Re-enter connect — `getAccessToken()` at the top will fetch a new token.
        return connect();
      }

      if (!response.ok) {
        const errorText = await response.text().catch(() => response.statusText);
        throw new Error(`SSE connection failed: ${response.status} - ${errorText}`);
      }

      if (!response.body) {
        throw new Error('SSE response has no body');
      }

      // Stream opened successfully — reset the auth retry budget so a 401
      // *later* in the session can still trigger a fresh reconnect.
      authRetries = 0;

      isConnected = true;
      onOpen?.();

      const reader = response.body.getReader();
      const decoder = new TextDecoder('utf-8');
      let buffer = '';
      let currentEvent: Partial<SseEvent> = {};

      const processLine = (line: string): void => {
        // Empty line marks end of event
        if (line === '') {
          if (currentEvent.data !== undefined) {
            // Parse data as JSON if possible
            let parsedData: unknown;
            try {
              parsedData = typeof currentEvent.data === 'string' ? JSON.parse(currentEvent.data) : currentEvent.data;
            } catch {
              parsedData = currentEvent.data;
            }

            // Track latest event ID for reconnect resumption
            if (currentEvent.id) {
              currentLastEventId = currentEvent.id;
            }

            onEvent({
              event: currentEvent.event,
              data: parsedData,
              id: currentEvent.id,
              retry: currentEvent.retry,
            });
          }
          currentEvent = {};
          return;
        }

        // Comment line (ignore)
        if (line.startsWith(':')) {
          return;
        }

        // Parse field: value
        const colonIndex = line.indexOf(':');
        let field: string;
        let value: string;

        if (colonIndex === -1) {
          field = line;
          value = '';
        } else {
          field = line.slice(0, colonIndex);
          // Skip single space after colon if present
          value = line.charAt(colonIndex + 1) === ' ' ? line.slice(colonIndex + 2) : line.slice(colonIndex + 1);
        }

        switch (field) {
          case 'event':
            currentEvent.event = value;
            break;
          case 'data':
            // Data can be multi-line, append with newline
            currentEvent.data = currentEvent.data ? `${currentEvent.data}\n${value}` : value;
            break;
          case 'id':
            currentEvent.id = value;
            break;
          case 'retry':
            const retryMs = parseInt(value, 10);
            if (!isNaN(retryMs)) {
              currentEvent.retry = retryMs;
            }
            break;
          // Unknown fields are ignored per SSE spec
        }
      };

      // Read stream
      // eslint-disable-next-line no-constant-condition
      while (true) {
        const { done, value } = await reader.read();

        if (done) {
          // Process any remaining buffer
          if (buffer) {
            const lines = buffer.split('\n');
            for (const line of lines) {
              processLine(line);
            }
          }
          break;
        }

        buffer += decoder.decode(value, { stream: true });

        // Process complete lines
        const lines = buffer.split('\n');
        // Keep the last potentially incomplete line in buffer
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          processLine(line);
        }
      }

      isConnected = false;
      onClose?.();
    } catch (error) {
      isConnected = false;
      if (error instanceof Error && error.name === 'AbortError') {
        // Connection was intentionally closed
        onClose?.();
        return;
      }
      onError(error instanceof Error ? error : new Error(String(error)));
    }
  };

  // Start connection
  const timeoutId = setTimeout(() => {
    if (abortController && !isConnected) {
      abortController.abort();
      onError(new Error(`SSE connection timeout after ${timeout}ms`));
    }
  }, timeout);

  connect().finally(() => {
    clearTimeout(timeoutId);
  });

  return {
    close: (): void => {
      if (abortController) {
        abortController.abort();
        abortController = null;
      }
      isConnected = false;
    },
    isConnected: (): boolean => isConnected,
  };
}
