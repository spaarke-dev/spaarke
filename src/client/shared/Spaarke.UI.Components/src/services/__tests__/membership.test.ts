/**
 * Unit tests for createMembershipResolver (spaarkeai-assistant-enhancements-r1
 * task 050 feature — the client resolver behind behavior.membershipFilter).
 *
 * Coverage:
 *  - happy path: GET /api/users/me/memberships/{entityType} → body.ids
 *  - query params: roles / identityTypes CSV, includeRelated, limit
 *  - entityType URL-encoded
 *  - empty membership → [] (NOT null — distinct from failure)
 *  - fail-soft: non-2xx → null; thrown fetch → null; missing ids → []
 *  - blank entityType → null (no request)
 */

import { createMembershipResolver } from '../membership';

function jsonResponse(status: number, body: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
  } as unknown as Response;
}

describe('createMembershipResolver', () => {
  it('resolves ids from the membership endpoint', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(200, { ids: ['a', 'b'], count: 2 }));
    const resolver = createMembershipResolver(fetchMock);

    const ids = await resolver('sprk_event');

    expect(ids).toEqual(['a', 'b']);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [path, init] = fetchMock.mock.calls[0];
    expect(path).toBe('/api/users/me/memberships/sprk_event');
    expect(init).toEqual({ method: 'GET' });
  });

  it('forwards roles / identityTypes / includeRelated / limit as query params', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(200, { ids: [] }));
    const resolver = createMembershipResolver(fetchMock);

    await resolver('sprk_event', {
      roles: ['owner', 'assignedTo'],
      identityTypes: ['systemuser', 'contact'],
      includeRelated: true,
      limit: 50,
    });

    const url = new URL('https://host' + fetchMock.mock.calls[0][0]);
    expect(url.pathname).toBe('/api/users/me/memberships/sprk_event');
    expect(url.searchParams.get('roles')).toBe('owner,assignedTo');
    expect(url.searchParams.get('identityTypes')).toBe('systemuser,contact');
    expect(url.searchParams.get('includeRelated')).toBe('true');
    expect(url.searchParams.get('limit')).toBe('50');
  });

  it('URL-encodes the entityType', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(200, { ids: [] }));
    const resolver = createMembershipResolver(fetchMock);
    await resolver('weird/type name');
    expect(fetchMock.mock.calls[0][0]).toBe('/api/users/me/memberships/weird%2Ftype%20name');
  });

  it('returns [] (not null) for an empty membership', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(200, { ids: [], count: 0 }));
    const resolver = createMembershipResolver(fetchMock);
    await expect(resolver('sprk_event')).resolves.toEqual([]);
  });

  it('returns [] when the body omits ids', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(200, { count: 0 }));
    const resolver = createMembershipResolver(fetchMock);
    await expect(resolver('sprk_event')).resolves.toEqual([]);
  });

  it('fails soft to null on a non-2xx response', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(500, { error: 'boom' }));
    const resolver = createMembershipResolver(fetchMock);
    await expect(resolver('sprk_event')).resolves.toBeNull();
  });

  it('fails soft to null when the fetch throws', async () => {
    const fetchMock = jest.fn().mockRejectedValue(new Error('network down'));
    const resolver = createMembershipResolver(fetchMock);
    await expect(resolver('sprk_event')).resolves.toBeNull();
  });

  it('returns null for a blank entityType without calling fetch', async () => {
    const fetchMock = jest.fn();
    const resolver = createMembershipResolver(fetchMock);
    await expect(resolver('   ')).resolves.toBeNull();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('honors a custom basePath', async () => {
    const fetchMock = jest.fn().mockResolvedValue(jsonResponse(200, { ids: ['x'] }));
    const resolver = createMembershipResolver(fetchMock, { basePath: '/api/v2/memberships' });
    await resolver('sprk_event');
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v2/memberships/sprk_event');
  });
});
