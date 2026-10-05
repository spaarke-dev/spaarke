/**
 * spaarkeai-word-add-in-r1 task 038 (owner decision 2026-09-13), generalized by task 100: the create form's reference
 * lists (`GET /api/office/search/{list}`) are cached in the pane so repeat opens don't call the BFF. Only a
 * successful, NON-EMPTY result is cached (a failure or an empty list is never cached, so Retry still re-fetches); the
 * cache has a ~24h TTL, `localStorage` faults degrade to a live fetch, and a quick-create "… not found" warning clears
 * that list's cache. Formerly `matterTypeLookupService.cache.test.ts` — the same cases, now per list.
 *
 * Each test calls `jest.resetModules()` + `require()`s the service fresh — this mirrors a REAL pane reopen more
 * faithfully than a manual reset export would: the module's in-memory cache is gone (a fresh page load), but jsdom's
 * `window.localStorage` — like a real browser's — persists across the reset.
 */
import type * as ReferenceListServiceModule from '../referenceListService';

type ServiceModule = typeof ReferenceListServiceModule;

function freshService(): ServiceModule {
  jest.resetModules();
  // eslint-disable-next-line @typescript-eslint/no-var-requires -- deliberate fresh module load; see file header
  return require('../referenceListService') as ServiceModule;
}

const RESPONSE_BODY = {
  results: [
    { id: '11aed095-30da-f011-8406-7ced8d1dc988', name: 'Litigation', code: 'LITG' },
    { id: '46c35aa2-30da-f011-8406-7ced8d1dc988', name: 'Patent', code: 'PAT' },
  ],
};

/** The key task 038 shipped — kept, so a pane already holding a cached matter-type list keeps it. */
const MATTER_TYPES_CACHE_KEY = 'spaarke.officeAddin.matterTypes.v1';
const PRACTICE_AREAS_CACHE_KEY = 'spaarke.officeAddin.referenceList.practice-areas.v1';
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

describe('referenceListService — caching (owner decision 2026-09-13)', () => {
  it('calls the list route by name', async () => {
    const svc = freshService();
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);

    await svc.fetchReferenceList('practice-areas', 'https://bff', 'token', TOKEN_GETTER);

    expect(String(fetchMock.mock.calls[0][0])).toBe('https://bff/api/office/search/practice-areas');
  });

  it('a cached entry is used on the second load, with NO second fetch, within the TTL', async () => {
    const svc = freshService();
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);

    const first = await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(first).toHaveLength(2);

    const second = await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock).toHaveBeenCalledTimes(1); // no second network call
    expect(second).toEqual(first);
  });

  it('each list has its own cache — a cached matter-type list never answers for practice areas', async () => {
    const svc = freshService();
    mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);

    const fetchMock2 = mockFetchOnce(true, 200, { results: [{ id: 'pa-1', name: 'Appellate' }] });
    const areas = await svc.fetchReferenceList('practice-areas', 'https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock2).toHaveBeenCalledTimes(1);
    expect(areas).toEqual([{ id: 'pa-1', name: 'Appellate' }]);
    expect(window.localStorage.getItem(MATTER_TYPES_CACHE_KEY)).not.toBeNull();
    expect(window.localStorage.getItem(PRACTICE_AREAS_CACHE_KEY)).not.toBeNull();
  });

  it('a fresh module load (a real pane reopen) still avoids a fetch via the localStorage entry', async () => {
    const svc1 = freshService();
    const fetchMock1 = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc1.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock1).toHaveBeenCalledTimes(1);
    expect(window.localStorage.getItem(MATTER_TYPES_CACHE_KEY)).not.toBeNull();

    // Simulate the pane reopening: a fresh module instance (in-memory cache gone), same localStorage.
    const svc2 = freshService();
    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    const result = await svc2.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock2).not.toHaveBeenCalled();
    expect(result).toHaveLength(2);
  });

  it('an expired entry triggers a fresh fetch', async () => {
    const svc = freshService();
    const nowSpy = jest.spyOn(Date, 'now');
    nowSpy.mockReturnValue(1_000_000);
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);

    await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock).toHaveBeenCalledTimes(1);

    // Past the ~24h TTL.
    nowSpy.mockReturnValue(1_000_000 + 25 * 60 * 60 * 1000);
    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);

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

    const result = await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result).toHaveLength(2);
  });

  it('a malformed localStorage value is ignored, falling back to a fetch', async () => {
    window.localStorage.setItem(MATTER_TYPES_CACHE_KEY, '{ not: valid json');
    const svc = freshService();
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);

    const result = await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(result).toHaveLength(2);
  });

  it('a failed fetch is not cached — the next call fetches again', async () => {
    const svc = freshService();
    mockFetchOnce(false, 500, {});

    await expect(svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER)).rejects.toThrow();
    expect(window.localStorage.getItem(MATTER_TYPES_CACHE_KEY)).toBeNull();

    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    const result = await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);

    expect(fetchMock2).toHaveBeenCalledTimes(1);
    expect(result).toHaveLength(2);
  });

  it('a successful but EMPTY result is not cached — the next call fetches again', async () => {
    const svc = freshService();
    mockFetchOnce(true, 200, { results: [] });

    const empty = await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);
    expect(empty).toEqual([]);
    expect(window.localStorage.getItem(MATTER_TYPES_CACHE_KEY)).toBeNull();

    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock2).toHaveBeenCalledTimes(1);
  });

  it('clearReferenceListCache empties that list (memory + localStorage), so its next call re-fetches', async () => {
    const svc = freshService();
    const fetchMock = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(window.localStorage.getItem(MATTER_TYPES_CACHE_KEY)).not.toBeNull();

    svc.clearReferenceListCache('matter-types');
    expect(window.localStorage.getItem(MATTER_TYPES_CACHE_KEY)).toBeNull();

    const fetchMock2 = mockFetchOnce(true, 200, RESPONSE_BODY);
    await svc.fetchReferenceList('matter-types', 'https://bff', 'token', TOKEN_GETTER);
    expect(fetchMock2).toHaveBeenCalledTimes(1);
  });

  describe('warningsIndicateReferenceNotFound', () => {
    it('matches a "matter type ... not found" warning, case-insensitively', () => {
      const svc = freshService();
      expect(
        svc.warningsIndicateReferenceNotFound('matter-types', [
          'The selected Matter Type was NOT FOUND; created without it.',
        ])
      ).toBe(true);
    });

    it('matches the server`s practice-area and project-type warnings, each for its own list only', () => {
      const svc = freshService();
      const practiceArea = [
        'The selected practice area was not found, so the matter was created without a practice area.',
      ];
      expect(svc.warningsIndicateReferenceNotFound('practice-areas', practiceArea)).toBe(true);
      expect(svc.warningsIndicateReferenceNotFound('matter-types', practiceArea)).toBe(false);
      expect(
        svc.warningsIndicateReferenceNotFound('project-types', [
          'The selected project type was not found, so the project was created without a project type.',
        ])
      ).toBe(true);
    });

    it('does not match a transient "could not be checked" warning', () => {
      const svc = freshService();
      expect(
        svc.warningsIndicateReferenceNotFound('matter-types', [
          'The selected matter type could not be checked; created without it.',
        ])
      ).toBe(false);
    });

    it('does not match when there are no warnings', () => {
      const svc = freshService();
      expect(svc.warningsIndicateReferenceNotFound('matter-types', undefined)).toBe(false);
      expect(svc.warningsIndicateReferenceNotFound('matter-types', [])).toBe(false);
    });
  });
});
