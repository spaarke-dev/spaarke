/**
 * GitHub #1571 review, fix round 3 (spaarkeai-word-add-in-r1 task 136): the save-progress stream (`SseClient`)
 * - passes the token the refused connection SENT to `authService.clearCache`, so only that token is recorded as
 *   refused;
 * - stops reconnecting once the token is empty (nothing can be had silently), raising "sign-in required" once, instead
 *   of spending its remaining retries on requests that carry no token.
 */

jest.mock('@shared/services/AuthService', () => ({
  authService: {
    clearCache: jest.fn(),
    notifySignInRequired: jest.fn(),
  },
}));

import { authService } from '@shared/services/AuthService';
import { createSseConnection } from '../SseClient';

const clearCache = authService.clearCache as jest.Mock;
const notifySignInRequired = authService.notifySignInRequired as jest.Mock;

function unauthorized(): Response {
  return {
    ok: false,
    status: 401,
    statusText: 'Unauthorized',
    body: { cancel: () => Promise.resolve() },
    text: async () => 'Unauthorized',
  } as unknown as Response;
}

const fetchMock = jest.fn();
const originalFetch = global.fetch;

beforeEach(() => {
  jest.clearAllMocks();
  fetchMock.mockReset();
  global.fetch = fetchMock as unknown as typeof fetch;
});

afterAll(() => {
  global.fetch = originalFetch;
});

describe('SseClient — the refused token, and no retry without a token (#1571 fix round 3)', () => {
  it('a 401 records the token the connection sent; an empty token then ends the retries with one signal', async () => {
    const tokens = ['sent-token', ''];
    fetchMock.mockResolvedValue(unauthorized());

    const error = await new Promise<Error | null>(resolve => {
      createSseConnection('https://bff/api/office/jobs/1/stream', {
        getAccessToken: async () => tokens.shift() ?? '',
        onEvent: () => undefined,
        onError: err => resolve(err),
        onClose: () => resolve(null),
        maxAuthRetries: 3,
      });
    });

    expect(clearCache).toHaveBeenCalledTimes(1);
    expect(clearCache).toHaveBeenCalledWith('sent-token');
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(notifySignInRequired).toHaveBeenCalledTimes(1);
    expect(error?.message).toMatch(/401/);
  });
});
