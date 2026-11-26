/**
 * discoverMemoNavProps delegates to the shared discoverNavProps (#2909, ADR-012 reuse).
 *
 * Pinned: the memo host gets the shared rules — the logical name is lowercased for the metadata URL (SRFR-050: a
 * wrong-case name returns HTTP 200 with an empty list) — and the page's one cache, so a discovery another writer on the
 * page already made is reused with no second request. Both fail on the former private copy.
 */
import { discoverMemoNavProps, _resetMemoNavPropCacheForTests } from '../discoverMemoNavProps';
import { discoverNavProps } from '@spaarke/ui-components/services/PolymorphicResolverService';

const MEMO_RELATIONSHIPS = [
  { ReferencingAttribute: 'sprk_matter', ReferencingEntityNavigationPropertyName: 'sprk_Matter', ReferencedEntity: 'sprk_matter' },
];

function ok(value: unknown[]): Response {
  return { ok: true, status: 200, json: async () => ({ value }) } as unknown as Response;
}

describe('discoverMemoNavProps — the shared nav-prop discovery (#2909)', () => {
  const realFetch = global.fetch;
  let warn: jest.SpyInstance;

  beforeEach(() => {
    _resetMemoNavPropCacheForTests();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => {});
  });

  afterEach(() => {
    (global as unknown as { fetch: unknown }).fetch = realFetch;
    warn.mockRestore();
    _resetMemoNavPropCacheForTests();
  });

  it('lowercases a mixed-case logical name for the metadata request', async () => {
    const fetchMock = jest.fn(async () => ok(MEMO_RELATIONSHIPS));
    (global as unknown as { fetch: unknown }).fetch = fetchMock;

    const entries = await discoverMemoNavProps('SPRK_Memo');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(String((fetchMock.mock.calls[0] as unknown[])[0])).toContain("EntityDefinitions(LogicalName='sprk_memo')");
    expect(entries).toEqual([{ columnName: 'sprk_matter', navPropName: 'sprk_Matter', referencedEntity: 'sprk_matter' }]);
  });

  it('reuses a discovery another writer on the page already made: no second request', async () => {
    await discoverNavProps('sprk_memo', jest.fn(async () => ok(MEMO_RELATIONSHIPS)) as unknown as typeof fetch);
    const fetchMock = jest.fn(async () => {
      throw new Error('a second metadata request was sent');
    });
    (global as unknown as { fetch: unknown }).fetch = fetchMock;

    const entries = await discoverMemoNavProps('sprk_memo');

    expect(fetchMock).not.toHaveBeenCalled();
    expect(entries).toHaveLength(1);
  });

  it('warns on an empty relationship list and returns [] without throwing', async () => {
    (global as unknown as { fetch: unknown }).fetch = jest.fn(async () => ok([]));

    await expect(discoverMemoNavProps('sprk_memo')).resolves.toEqual([]);
    expect(warn.mock.calls.some(c => String(c[0]).includes('zero entries'))).toBe(true);
  });
});
