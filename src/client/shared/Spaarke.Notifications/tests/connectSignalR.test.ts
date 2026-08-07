/**
 * connectSignalR — accessTokenFactory freshness (spaarkeai-assistant-enhancements-r2
 * Phase 0 FIX 3, 2026-08-07).
 *
 * Regression coverage for the owner-reported "Assistant sometimes fails to load on
 * the FIRST mount after a hard reset" symptom (console: negotiate 401 + "Failed to
 * start the connection"). Prior to this fix, `accessTokenFactory` closed over a
 * SINGLE `negotiate()` response taken once at connect time and returned that same
 * snapshot forever — including on every `withAutomaticReconnect` attempt, well past
 * the point that short-lived SignalR-service token could have expired. The SDK
 * invokes `accessTokenFactory` again before each (re)connect attempt specifically so
 * callers can hand back a fresh token; this test verifies `connectSignalR` now
 * re-negotiates (a real `authenticatedFetch` call, itself already 401-retry-with-
 * backoff resilient — see negotiate.test.ts / authenticatedFetch.ts) on every
 * `accessTokenFactory` invocation instead of reusing the snapshot, and degrades
 * gracefully (falls back to the last-known token, never throws out of the SDK's
 * token-fetch path) if that re-negotiate fails.
 *
 * Mocks `@microsoft/signalr` entirely (no real transport) and `@spaarke/auth`'s
 * `authenticatedFetch` (same pattern as negotiate.test.ts).
 */

// jest.mock factories may only reference outer identifiers prefixed `mock*`
// (babel-plugin-jest-hoist allowlist) — see the two mocks below.
const mockWithUrl = jest.fn();
const mockStart = jest.fn().mockResolvedValue(undefined);
const mockOn = jest.fn();

jest.mock('@microsoft/signalr', () => {
  class FakeHubConnectionBuilder {
    withUrl(url: string, options: unknown): FakeHubConnectionBuilder {
      mockWithUrl(url, options);
      return this;
    }
    withAutomaticReconnect(): FakeHubConnectionBuilder {
      return this;
    }
    configureLogging(): FakeHubConnectionBuilder {
      return this;
    }
    build() {
      return {
        on: mockOn,
        start: mockStart,
        onclose: jest.fn(),
        onreconnecting: jest.fn(),
        onreconnected: jest.fn(),
      };
    }
  }
  return {
    HubConnectionBuilder: FakeHubConnectionBuilder,
    LogLevel: { Warning: 2 },
  };
});

jest.mock('@spaarke/auth', () => {
  class ApiError extends Error {
    status: number;
    constructor(message: string, status: number) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
      Object.setPrototypeOf(this, ApiError.prototype);
    }
  }
  return {
    authenticatedFetch: jest.fn(),
    ApiError,
  };
});

import { authenticatedFetch } from '@spaarke/auth';
import { connectSignalR } from '../src/negotiate';

const mockedAuthenticatedFetch = authenticatedFetch as jest.Mock;

function jsonResponse(body: unknown) {
  return { json: async () => body };
}

describe('connectSignalR — accessTokenFactory freshness', () => {
  beforeEach(() => {
    mockWithUrl.mockClear();
    mockStart.mockClear();
    mockOn.mockClear();
    mockedAuthenticatedFetch.mockReset();
  });

  it('passes an accessTokenFactory that re-negotiates (fresh authenticatedFetch call) on every invocation', async () => {
    mockedAuthenticatedFetch
      .mockResolvedValueOnce(jsonResponse({ url: 'https://signalr.example/client', accessToken: 'tok-1' }))
      .mockResolvedValueOnce(jsonResponse({ url: 'https://signalr.example/client', accessToken: 'tok-2' }))
      .mockResolvedValueOnce(jsonResponse({ url: 'https://signalr.example/client', accessToken: 'tok-3' }));

    await connectSignalR(() => undefined);

    expect(mockedAuthenticatedFetch).toHaveBeenCalledTimes(1); // the initial negotiate() inside connectSignalR
    expect(mockWithUrl).toHaveBeenCalledTimes(1);
    const [url, options] = mockWithUrl.mock.calls[0] as [string, { accessTokenFactory: () => Promise<string> }];
    expect(url).toBe('https://signalr.example/client');
    expect(typeof options.accessTokenFactory).toBe('function');

    // First invocation (simulating the SDK's initial connect) re-negotiates — a SECOND
    // authenticatedFetch call, distinct from the one `connectSignalR` itself made above.
    await expect(options.accessTokenFactory()).resolves.toBe('tok-2');
    expect(mockedAuthenticatedFetch).toHaveBeenCalledTimes(2);

    // A later invocation (simulating a `withAutomaticReconnect` attempt) re-negotiates
    // AGAIN — proving the factory is not a one-time snapshot.
    await expect(options.accessTokenFactory()).resolves.toBe('tok-3');
    expect(mockedAuthenticatedFetch).toHaveBeenCalledTimes(3);
  });

  it('falls back to the last-known token (never throws) when a re-negotiate call fails', async () => {
    mockedAuthenticatedFetch
      .mockResolvedValueOnce(jsonResponse({ url: 'https://signalr.example/client', accessToken: 'tok-initial' }))
      .mockRejectedValueOnce(new Error('network blip'));

    await connectSignalR(() => undefined);
    const options = mockWithUrl.mock.calls[0][1] as { accessTokenFactory: () => Promise<string> };

    await expect(options.accessTokenFactory()).resolves.toBe('tok-initial');
  });
});
