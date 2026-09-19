/**
 * spaarkeai-word-add-in-r1 task 038, owner decision 2026-09-13: cache `GET
 * /api/office/search/matter-types` results in the pane so repeat opens don't call the BFF. Only a
 * successful, NON-EMPTY result is cached (a failure or an empty list is never cached, so the
 * coordinator's Retry fix still re-fetches); the cache has a ~24h TTL, `localStorage` faults degrade
 * to a live fetch, and a quick-create "matter type not found" warning clears the cache.
 *
 * Each test calls `jest.resetModules()` + `require()`s the service fresh — this mirrors a REAL pane
 * reopen more faithfully than a manual reset export would: the module's in-memory cache variable is
 * gone (a fresh page load), but jsdom's `window.localStorage` — like a real browser's — persists
 * across the reset, so "does localStorage alone satisfy a fresh load" is exercised honestly rather
 * than assumed.
 */
import type * as MatterTypeLookupServiceModule from '../matterTypeLookupService';

type ServiceModule = typeof MatterTypeLookupServiceModule;

function freshService(): ServiceModule {
  jest.resetModules();
  // eslint-disable-next-line @typescript-eslint/no-require-imports -- deliberate fresh module load; see file header
  return require('../matterTypeLookupService') as ServiceModule;
}

const RESPONSE_BODY = {
  results: [
    { id: '11aed095-30da-f011-8406-7ced8d1dc988', name: 'Litigation', code: 'LITG' },
    { id: '46c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Patent', code: 'PAT' },
  ],
};

const CACHE_KEY = 'spaarke.officeAddin.matterTypes.v1';
const TOKEN_GETTER = jest.fn().mockResolvedValue('token');

function mockFetchOnce(ok: boolean, status: number, body: unknown): jest.Mock {
  const fn = jest.fn().mockResolvedValue({ ok, status, json: async () => body });
  global.fetch = fn as unknown as typeof fetch;
  return fn;
}

beforeEach(() => {
  window.localStorage.clear();
  jest.restoreAllMocks();
});

describe('matterTypeLookupService — caching (owner decision 2026-09-13)', () => {
  it('a cached entry is used on the second load, with NO second fetch, within the TTL', async () => {
    const svc = freshService();
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);

    const first = await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(first).toHaveLength(2);

    const second = await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock).toHaveBeenCalledTimes(1); // no second network call
    expect(second).toEqual(first);
  });

  it('a fresh module load (a real pane reopen) still avoids a fetch via the localStorage entry', async () => {
    const svc1 = freshService();
    const fetchMock1 = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc1.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock1).toHaveBeenCalledTimes(1);
    expect(window.localStorage.getItem(CACHE_KEY)).not.toBeNull();

    // Simulate the pane reopening: a fresh module instance (in-memory cache gone), same localStorage.
    const svc2 = freshService();
    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    const result = await svc2.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock2).not.toHaveBeenCalled();
    expect(result).toHaveLength(2);
  });

  it('an expired entry triggers a fresh fetch', async () => {
    const svc = freshService();
    const nowSpy = jest.spyOn(Date, 'now');
    nowSpy.mockReturnValue(1_000_000);
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);

    await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    // Past the ~24h TTL.
    nowSpy.mockReturnValue(1_000_000 + 25 * 60 * 60 * 1000);
    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock2).toHaveBeenCalledTimes(1); // a SECOND live call, not served from the stale entry
  });

  it('localStorage throwing on every access still results in a working fetch', async () => {
    const svc = freshService();
    jest.spyOn(window.localStorage.__proto__, 'getItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });
    jest.spyOn(window.localStorage.__proto__, 'setItem').mockImplementation(() => {
      throw new Error('storage disabled');
    });
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);

    const result = await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result).toHaveLength(2);
  });

  it('a malformed localStorage value is ignored, falling back to a fetch', async () => {
    window.localStorage.setItem(CACHE_KEY, '{ not: valid json');
    const svc = freshService();
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);

    const result = await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result).toHaveLength(2);
  });

  it('a failed fetch is not cached — the next call fetches again', async () => {
    const svc = freshService();
    mockFetchOnce(false, 500, {});

    await expect(svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER)).rejects.toThrow();
    expect(window.localStorage.getItem(CACHE_KEY)).toBeNull();

    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    const result = await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock2).toHaveBeenCalledTimes(1);
    expect(result).toHaveLength(2);
  });

  it('a successful but EMPTY result is not cached — the next call fetches again', async () => {
    const svc = freshService();
    mockFetchOnce(true, 200, { results: [] });

    const empty = await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);
    expect(empty).toEqual([]);
    expect(window.localStorage.getItem(CACHE_KEY)).toBeNull();

    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock2).toHaveBeenCalledTimes(1);
  });

  it('clearMatterTypesCache empties both the memory and localStorage cache, so the next call re-fetches', async () => {
    const svc = freshService();
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(window.localStorage.getItem(CACHE_KEY)).not.toBeNull();

    svc.clearMatterTypesCache();
    expect(window.localStorage.getItem(CACHE_KEY)).toBeNull();

    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchMatterTypes('https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock2).toHaveBeenCalledTimes(1);
  });

  describe('warningsIndicateMatterTypeNotFound', () => {
    it('matches a "matter type ... not found" warning, case-insensitively', () => {
      const svc = freshService();
      expect(
        svc.warningsIndicateMatterTypeNotFound(['The selected Matter Type was NOT FOUND; created without it.'])
      ).toBe(true);
    });

    it('does not match a transient "could not be checked" warning', () => {
      const svc = freshService();
      expect(
        svc.warningsIndicateMatterTypeNotFound(['The selected matter type could not be checked; created without it.'])
      ).toBe(false);
    });

    it('does not match when there are no warnings', () => {
      const svc = freshService();
      expect(svc.warningsIndicateMatterTypeNotFound(undefined)).toBe(false);
      expect(svc.warningsIndicateMatterTypeNotFound([])).toBe(false);
    });
  });
});
