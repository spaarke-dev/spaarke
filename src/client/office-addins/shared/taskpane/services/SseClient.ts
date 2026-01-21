/**
 * SSE Client using fetch + ReadableStream
 *
 * Native EventSource does not support custom headers (like Authorization).
 * This implementation uses fetch with ReadableStream to parse SSE format
 * while supporting bearer token authentication.
 *
 * Per spec.md: MUST use fetch + ReadableStream for SSE (not EventSource)
 */

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
  /** Access token for Authorization header */
  accessToken: string;
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
 * @param url The SSE endpoint URL
 * @param options Connection options including auth token and callbacks
 * @returns Connection object with close method
 *
 * @example
 * ```typescript
 * const connection = createSseConnection('/office/jobs/123/stream', {
 *   accessToken: 'Bearer token',
 *   onEvent: (event) => console.log('Event:', event),
 *   onError: (error) => console.error('Error:', error),
 * });
 *
 * // Later, to close:
 * connection.close();
 * ```
 */
export function createSseConnection(
  url: string,
  options: SseClientOptions
): SseConnection {
  const {
    accessToken,
    onEvent,
    onError,
    onClose,
    onOpen,
    lastEventId,
    timeout = 30000,
  } = options;

  let abortController: AbortController | null = new AbortController();
  let isConnected = false;

  const connect = async (): Promise<void> => {
    if (!abortController) {
      return;
    }

    const headers: HeadersInit = {
      Accept: 'text/event-stream',
      Authorization: `Bearer ${accessToken}`,
      'Cache-Control': 'no-cache',
    };

    if (lastEventId) {
      headers['Last-Event-ID'] = lastEventId;
    }

    try {
      const response = await fetch(url, {
        method: 'GET',
        headers,
        signal: abortController.signal,
        cache: 'no-store',
      });

      if (!response.ok) {
        const errorText = await response.text().catch(() => response.statusText);
        throw new Error(`SSE connection failed: ${response.status} - ${errorText}`);
      }

      if (!response.body) {
        throw new Error('SSE response has no body');
      }

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
              parsedData =
                typeof currentEvent.data === 'string'
                  ? JSON.parse(currentEvent.data)
                  : currentEvent.data;
            } catch {
              parsedData = currentEvent.data;
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
          value = line.charAt(colonIndex + 1) === ' '
            ? line.slice(colonIndex + 2)
            : line.slice(colonIndex + 1);
        }

        switch (field) {
          case 'event':
            currentEvent.event = value;
            break;
          case 'data':
            // Data can be multi-line, append with newline
            currentEvent.data = currentEvent.data
              ? `${currentEvent.data}\n${value}`
              : value;
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
