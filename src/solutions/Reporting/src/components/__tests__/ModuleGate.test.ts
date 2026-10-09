/**
 * ModuleGate.probeModuleStatus — the BFF probe's answer mapped to a gate state.
 *
 * `@spaarke/auth`'s authenticatedFetch (re-exported by services/authInit) THROWS for every non-2xx —
 * ApiError(status) or, once its 401 retries are spent, AuthError — so the gate's designed answers
 * (404 = module disabled, 401/403 = no access) only exist as thrown errors. These tests throw them the
 * way the real fetch does.
 */
import { ApiError, AuthError } from '@spaarke/auth';

const mockFetch = jest.fn<Promise<Response>, [string, RequestInit?]>();

jest.mock('../../services/authInit', () => ({
  authenticatedFetch: (url: string, init?: RequestInit) => mockFetch(url, init),
}));
jest.mock('../../config/runtimeConfig', () => ({
  getBffBaseUrl: () => 'https://bff.example.test',
}));

import { probeModuleStatus } from '../ModuleGate';

describe('probeModuleStatus', () => {
  let warn: jest.SpyInstance;
  let error: jest.SpyInstance;

  beforeEach(() => {
    mockFetch.mockReset();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
    error = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    warn.mockRestore();
    error.mockRestore();
  });

  it('is "ok" for a 2xx', async () => {
    mockFetch.mockResolvedValue({ ok: true, status: 200 } as Response);
    await expect(probeModuleStatus()).resolves.toBe('ok');
  });

  it('is "disabled" when the module gate answers 404 (thrown ApiError)', async () => {
    mockFetch.mockRejectedValue(new ApiError('Not Found', 404, { title: 'Not Found', status: 404 }));
    await expect(probeModuleStatus()).resolves.toBe('disabled');
  });

  it('is "unauthorized" when the user lacks the role: 403 (thrown ApiError)', async () => {
    mockFetch.mockRejectedValue(new ApiError('Forbidden', 403, { title: 'Forbidden', status: 403 }));
    await expect(probeModuleStatus()).resolves.toBe('unauthorized');
  });

  it('is "unauthorized" when sign-in failed: AuthError after the 401 retries', async () => {
    mockFetch.mockRejectedValue(new AuthError('Authentication failed after all retry attempts', 'auth_exhausted'));
    await expect(probeModuleStatus()).resolves.toBe('unauthorized');
  });

  it('is "error" for any other HTTP failure (thrown ApiError 500)', async () => {
    mockFetch.mockRejectedValue(new ApiError('HTTP 500', 500));
    await expect(probeModuleStatus()).resolves.toBe('error');
  });

  it('is "error" for a network failure', async () => {
    mockFetch.mockRejectedValue(new TypeError('Failed to fetch'));
    await expect(probeModuleStatus()).resolves.toBe('error');
  });

  it('maps a RETURNED non-2xx the same way (a fetch that does not throw)', async () => {
    mockFetch.mockResolvedValueOnce({ ok: false, status: 404 } as Response);
    await expect(probeModuleStatus()).resolves.toBe('disabled');
    mockFetch.mockResolvedValueOnce({ ok: false, status: 403 } as Response);
    await expect(probeModuleStatus()).resolves.toBe('unauthorized');
  });
});
