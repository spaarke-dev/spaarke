// ts-jest does not transform node_modules (Spaarke.Auth's dist/ is compiled ESM), so the mock
// factory below defines its own minimal ApiError stand-in rather than requireActual-ing the
// real @spaarke/auth package. negotiate.ts imports ApiError from the SAME mocked module, so
// `instanceof ApiError` checks inside negotiate.ts still work correctly against this stand-in.
jest.mock('@spaarke/auth', () => {
  class ApiError extends Error {
    status: number;
    problemDetails: unknown;
    constructor(message: string, status: number, problemDetails: unknown = null) {
      super(message);
      this.name = 'ApiError';
      this.status = status;
      this.problemDetails = problemDetails;
      Object.setPrototypeOf(this, ApiError.prototype);
    }
  }
  class AuthError extends Error {
    code: string;
    constructor(message: string, code = 'auth_failed') {
      super(message);
      this.name = 'AuthError';
      this.code = code;
      Object.setPrototypeOf(this, AuthError.prototype);
    }
  }
  return {
    authenticatedFetch: jest.fn(),
    ApiError,
    AuthError,
  };
});

import { ApiError } from '@spaarke/auth';

// eslint-disable-next-line @typescript-eslint/no-var-requires
const { authenticatedFetch } = jest.requireMock('@spaarke/auth') as { authenticatedFetch: jest.Mock };

import { negotiate, SignalRUnavailableError } from '../src/negotiate';

describe('negotiate', () => {
  beforeEach(() => {
    authenticatedFetch.mockReset();
  });

  it('POSTs to /notifications/negotiate via authenticatedFetch and returns url/accessToken', async () => {
    authenticatedFetch.mockResolvedValueOnce({
      json: async () => ({ url: 'https://signalr.example/client', accessToken: 'tok-123' }),
    });

    const result = await negotiate();

    expect(authenticatedFetch).toHaveBeenCalledWith('/notifications/negotiate', { method: 'POST' });
    expect(result).toEqual({ url: 'https://signalr.example/client', accessToken: 'tok-123' });
  });

  it('throws SignalRUnavailableError when the server returns 503 (SignalR disabled)', async () => {
    authenticatedFetch.mockRejectedValueOnce(new ApiError('Feature disabled', 503, null));

    await expect(negotiate()).rejects.toBeInstanceOf(SignalRUnavailableError);
  });

  it('re-throws a non-503 ApiError as-is (typed error, not a silent no-op)', async () => {
    const err = new ApiError('Forbidden', 403, null);
    authenticatedFetch.mockRejectedValueOnce(err);

    await expect(negotiate()).rejects.toBe(err);
  });

  it('throws when the response is missing url or accessToken', async () => {
    authenticatedFetch.mockResolvedValueOnce({ json: async () => ({ url: 'https://signalr.example/client' }) });

    await expect(negotiate()).rejects.toThrow(/missing url\/accessToken/);
  });
});
