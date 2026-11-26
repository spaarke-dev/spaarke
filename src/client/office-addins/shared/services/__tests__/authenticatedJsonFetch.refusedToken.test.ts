/**
 * GitHub #1571 review, fix round 3 (spaarkeai-word-add-in-r1 task 136): `authenticatedJsonFetch`
 * - passes the token the refused request SENT to `authService.clearCache`, so the strategy records that token as
 *   refused and never a newer, good one (a late 401 for an older token);
 * - does not send the retry when no token can be had silently: the retry would be refused too. The caller gets the
 *   original 401 and the "sign-in required" signal fires exactly once.
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

describe('authenticatedJsonFetch — the refused token, and no retry without a token (#1571 fix round 3)', () => {
  it('a 401 passes the token the request sent to clearCache', async () => {
    fetchMock.mockResolvedValueOnce(respond(401)).mockResolvedValueOnce(respond(200));

    await authenticatedJsonFetch('https://bff/api/x', {}, 'sent-token', { getRetryToken: async () => 'new-token' });

    expect(clearCache).toHaveBeenCalledWith('sent-token');
  });

  it('no retry token: the retry is not sent, the original 401 is returned, and the signal fires once', async () => {
    const original = respond(401);
    fetchMock.mockResolvedValueOnce(original);

    const res = await authenticatedJsonFetch('https://bff/api/x', {}, 'sent-token', { getRetryToken: async () => '' });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(res).toBe(original);
    expect(notifySignInRequired).toHaveBeenCalledTimes(1);
  });
});
