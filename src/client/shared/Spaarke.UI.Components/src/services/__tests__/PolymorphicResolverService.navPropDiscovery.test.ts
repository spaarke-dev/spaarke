/**
 * discoverNavProps — the ONE client implementation of the Dataverse nav-prop discovery query (Pattern D, issues
 * #2803 P-3 / #2804 P-4). Three private copies (ConnectionsWriteHandler, ResolverWriteHandler, useInlineTodoCreate)
 * were folded into it, which is why it now carries their SRFR-050 behaviour:
 *
 *   - the logical name is LOWERCASED for the URL and for the cache key — Dataverse answers a wrong-case name with
 *     HTTP 200 and an empty list, not a 404, which silently breaks every downstream write;
 *   - a zero-entry result is warned about, naming the entity;
 *   - it never throws, and an injected `fetchImpl` is used instead of the global.
 */

import { discoverNavProps, _resetNavPropCacheForTests } from '../PolymorphicResolverService';

function makeFetch(value: unknown[] = []) {
  return jest.fn().mockResolvedValue({ ok: true, status: 200, json: async () => ({ value }) });
}

const ROW = {
  ReferencingAttribute: 'sprk_regardingmatter',
  ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
  ReferencedEntity: 'sprk_matter',
};

describe('discoverNavProps — shared nav-prop discovery', () => {
  let warn: jest.SpyInstance;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    warn.mockRestore();
  });

  it('requests the LOWERCASED logical name (a wrong-case name answers 200 with an empty list)', async () => {
    const fetchImpl = makeFetch([ROW]);

    const entries = await discoverNavProps('sprk_Communication', fetchImpl as unknown as typeof fetch);

    expect(fetchImpl).toHaveBeenCalledTimes(1);
    const [url] = fetchImpl.mock.calls[0];
    expect(url).toContain("EntityDefinitions(LogicalName='sprk_communication')/ManyToOneRelationships");
    expect(url).not.toContain('sprk_Communication');
    expect(entries).toEqual([
      { columnName: 'sprk_regardingmatter', navPropName: 'sprk_RegardingMatter', referencedEntity: 'sprk_matter' },
    ]);
  });

  it('shares ONE cache across spellings of the same entity (the cache key is the lowercased name)', async () => {
    const fetchImpl = makeFetch([ROW]);

    const a = await discoverNavProps('sprk_Todo', fetchImpl as unknown as typeof fetch);
    const b = await discoverNavProps('sprk_todo', fetchImpl as unknown as typeof fetch);
    const c = await discoverNavProps('SPRK_TODO', fetchImpl as unknown as typeof fetch);

    expect(fetchImpl).toHaveBeenCalledTimes(1);
    expect(b).toBe(a);
    expect(c).toBe(a);
  });

  it('_resetNavPropCacheForTests(name) clears the entry whatever the spelling', async () => {
    const fetchImpl = makeFetch([ROW]);
    await discoverNavProps('sprk_todo', fetchImpl as unknown as typeof fetch);

    _resetNavPropCacheForTests('sprk_Todo');
    await discoverNavProps('sprk_todo', fetchImpl as unknown as typeof fetch);

    expect(fetchImpl).toHaveBeenCalledTimes(2);
  });

  it('warns, naming the entity, when discovery returns zero entries', async () => {
    const fetchImpl = makeFetch([]);

    const entries = await discoverNavProps('sprk_typo', fetchImpl as unknown as typeof fetch);

    expect(entries).toEqual([]);
    expect(warn).toHaveBeenCalledTimes(1);
    expect(String(warn.mock.calls[0][0])).toContain('sprk_typo');
    expect(String(warn.mock.calls[0][0])).toMatch(/zero entries/);
  });

  it('does not warn about zero entries when the entity has relationships', async () => {
    await discoverNavProps('sprk_todo', makeFetch([ROW]) as unknown as typeof fetch);

    expect(warn).not.toHaveBeenCalled();
  });

  it('never throws: returns [] on a non-OK response and on a rejected fetch', async () => {
    const notOk = jest.fn().mockResolvedValue({ ok: false, status: 500, json: async () => ({}) });
    const boom = jest.fn().mockRejectedValue(new Error('network down'));

    await expect(discoverNavProps('sprk_a', notOk as unknown as typeof fetch)).resolves.toEqual([]);
    await expect(discoverNavProps('sprk_b', boom as unknown as typeof fetch)).resolves.toEqual([]);
  });
});
