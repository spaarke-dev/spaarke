/**
 * resolveSource host rule (spaarke-ontology-platform-r1 task 054 follow-up; unified-access-control-r2 task 157).
 * On the external host a savedquery-set source is refused without listing the entity's saved queries; the one
 * configured savedquery id and an inline source resolve on both hosts; the internal host is unchanged.
 */
import { resolveSource } from '../resolveGridSource';
import type { IDataverseClient } from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const VIEW = { entityName: 'sprk_communication', fetchXml: '<fetch/>', layoutXml: '<grid/>', name: 'v' };

function makeClient() {
  return {
    retrieveSavedQueriesForEntity: jest.fn().mockResolvedValue([{ id: 'def', isDefault: true }]),
    retrieveSavedQuery: jest.fn().mockResolvedValue(VIEW),
  } as unknown as jest.Mocked<IDataverseClient>;
}

const SET: DataGridConfiguration = { _version: '1.0', source: { type: 'savedquery-set', entityLogicalName: 'sprk_communication' } };
const ONE: DataGridConfiguration = { _version: '1.0', source: { type: 'savedquery', savedQueryId: 'configured' } };
const INLINE: DataGridConfiguration = {
  _version: '1.0',
  source: { type: 'inline', fetchXml: '<fetch><entity name="sprk_communication"/></fetch>', layoutXml: '<grid/>' },
};

describe('resolveSource host rule', () => {
  it('external: refuses savedquery-set and never lists or fetches a view', async () => {
    const client = makeClient();
    await expect(resolveSource(client, SET, undefined, 'external')).resolves.toBeNull();
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
    expect(client.retrieveSavedQuery).not.toHaveBeenCalled();
  });

  it('internal: savedquery-set still lists and resolves the default view (unchanged)', async () => {
    const client = makeClient();
    await expect(resolveSource(client, SET, undefined, 'internal')).resolves.toBe(VIEW);
    expect(client.retrieveSavedQueriesForEntity).toHaveBeenCalledWith('sprk_communication');
    expect(client.retrieveSavedQuery).toHaveBeenCalledWith('def');
  });

  it.each(['internal', 'external'] as const)('%s: the one configured savedquery id resolves, with no list', async host => {
    const client = makeClient();
    await expect(resolveSource(client, ONE, undefined, host)).resolves.toBe(VIEW);
    expect(client.retrieveSavedQuery).toHaveBeenCalledWith('configured');
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
  });

  it.each(['internal', 'external'] as const)('%s: an inline source resolves, with no retrieval', async host => {
    const client = makeClient();
    const r = await resolveSource(client, INLINE, undefined, host);
    expect(r?.entityName).toBe('sprk_communication');
    expect(client.retrieveSavedQuery).not.toHaveBeenCalled();
    expect(client.retrieveSavedQueriesForEntity).not.toHaveBeenCalled();
  });
});
