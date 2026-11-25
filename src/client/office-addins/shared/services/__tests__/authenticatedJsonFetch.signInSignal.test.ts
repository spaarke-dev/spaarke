/**
 * GitHub #1571 review, fix round 1 (spaarkeai-word-add-in-r1 task 136): `authenticatedJsonFetch`, the single 401 retry
 * every pane request goes through (ApiClient, Save, Find, Invoice, Email, To Do):
 * - clears the auth service's token cache before the retry, whatever the caller's retry config, so the re-acquire asks
 *   Entra for a NEW token rather than MSAL's cached copy of the refused one (F1);
 * - raises the auth service's "sign-in required" signal when the retry is refused too, so every caller reaches the
 *   same sign-in prompt (F2). A success, a 403 or a 5xx raises nothing.
 */

jest.mock('../AuthService', () => ({
  authService: {
    clearCache: jest.fn(),
    notifySignInRequired: jest.fn(),
  },
}));

import { authService } from '../AuthService';
import { authenticatedJsonFetch } from '../authenticatedJsonFetch';

const clearCache = authService.clearCache as jest.Mock;
const notifySignInRequired = authService.notifySignInRequired as jest.Mock;

function respond(status: number): Response {
  return { ok: status >= 200 && status < 300, status } as unknown as Response;
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

describe('authenticatedJsonFetch — fresh token on retry, one sign-in signal (#1571 review)', () => {
  it('a 401 clears the token cache BEFORE the retry token is read, even with no onBeforeRetry', async () => {
    const order: string[] = [];
    clearCache.mockImplementation(() => order.push('clear'));
    fetchMock.mockResolvedValueOnce(respond(401)).mockResolvedValueOnce(respond(200));

    await authenticatedJsonFetch('https://bff/api/x', {}, 'old', {
      getRetryToken: async () => {
        order.push('retry-token');
        return 'new';
      },
    });

    expect(order).toEqual(['clear', 'retry-token']);
    expect(notifySignInRequired).not.toHaveBeenCalled();
  });

  it('a 401 that survives the retry raises "sign-in required" and returns the 401', async () => {
    fetchMock.mockResolvedValue(respond(401));

    const res = await authenticatedJsonFetch('https://bff/api/x', {}, 'old', { getRetryToken: async () => 'new' });

    expect(res.status).toBe(401);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(notifySignInRequired).toHaveBeenCalledTimes(1);
  });

  it.each([200, 403, 500])('NEGATIVE: a %i raises nothing and is not retried', async status => {
    fetchMock.mockResolvedValue(respond(status));

    await authenticatedJsonFetch('https://bff/api/x', {}, 'old', { getRetryToken: async () => 'new' });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(clearCache).not.toHaveBeenCalled();
    expect(notifySignInRequired).not.toHaveBeenCalled();
  });

  it('NEGATIVE: a 401 whose retry succeeds raises nothing', async () => {
    fetchMock.mockResolvedValueOnce(respond(401)).mockResolvedValueOnce(respond(201));

    const res = await authenticatedJsonFetch('https://bff/api/x', {}, 'old', { getRetryToken: async () => 'new' });

    expect(res.status).toBe(201);
    expect(notifySignInRequired).not.toHaveBeenCalled();
  });
});
