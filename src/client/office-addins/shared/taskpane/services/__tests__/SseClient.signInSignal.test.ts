/**
 * GitHub #1571 review, fix round 1 (spaarkeai-word-add-in-r1 task 136): the save-progress stream (`SseClient`) recovers
 * from a refused sign-in the same way every other pane request does:
 * - before reconnecting after a 401 it clears the auth service's token cache, so the next token is a NEW one rather
 *   than MSAL's cached copy of the refused one (F1);
 * - when the 401s outlast its retries it raises the auth service's "sign-in required" signal, so the pane shows its
 *   sign-in prompt (F2). A stream that opens raises nothing.
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

function emptyStream(): Response {
  return {
    ok: true,
    status: 200,
    body: { getReader: () => ({ read: async () => ({ done: true, value: undefined }) }) },
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

function open(maxAuthRetries: number): Promise<Error | null> {
  return new Promise(resolve => {
    createSseConnection('https://bff/api/office/jobs/1/stream', {
      getAccessToken: async () => 'token',
      onEvent: () => undefined,
      onError: err => resolve(err),
      onClose: () => resolve(null),
      maxAuthRetries,
    });
  });
}

describe('SseClient — fresh token on reconnect, one sign-in signal (#1571 review)', () => {
  it('a 401 clears the token cache before reconnecting; a stream that then opens raises nothing', async () => {
    fetchMock.mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(emptyStream());

    const error = await open(3);

    expect(error).toBeNull();
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(clearCache).toHaveBeenCalledTimes(1);
    expect(notifySignInRequired).not.toHaveBeenCalled();
  });

  it('401s that outlast the retries raise "sign-in required" and still report the error', async () => {
    fetchMock.mockResolvedValue(unauthorized());

    const error = await open(2);

    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(notifySignInRequired).toHaveBeenCalledTimes(1);
    expect(error?.message).toMatch(/401/);
  });
});
