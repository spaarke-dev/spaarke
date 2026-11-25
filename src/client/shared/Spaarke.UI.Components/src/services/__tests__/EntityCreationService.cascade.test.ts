/**
 * EntityCreationService business-unit defaults and index-file call - Unit Tests
 *
 * Scope: what the client reads from the business unit and what it sends when it indexes an uploaded file
 * (field-mapping-server-write-path-r1, FR-40, task 035).
 *
 * Covered:
 *   - resolveUserBuDefaults: systemuser -> BU chain; returns the container and the BU id; normalizes empty strings
 *     to undefined; leaves a field undefined when the BU does not set it (no throw); and selects NO search-index
 *     column from the business unit, whatever the BU holds (the server owns the index)
 *   - indexUploadedFiles: the `POST /api/ai/rag/index-file` body carries no `searchIndexName` key
 *
 * Removed with the helpers they covered: `applyDefaultSearchIndexName` and `applyUserBuDefaults` (task 035, FR-40: no
 * client stamps a search index onto a create payload) and, earlier, `applyDefaultContainerId` (task 076: a client may
 * not choose a record's storage location). Their tests had no subject; do not re-add them.
 */

import { EntityCreationService } from '../EntityCreationService';
import type { IWebApiLike, IWebApiWithCreate } from '../../types/WebApiLike';

// ---------------------------------------------------------------------------
// resolveUserBuDefaults
// ---------------------------------------------------------------------------

describe('EntityCreationService.resolveUserBuDefaults', () => {
  function makeWebApi(userBuId: string | null, buFields: Record<string, unknown>): IWebApiLike {
    return {
      retrieveRecord: jest.fn().mockImplementation(async (entityType: string, _id: string, _options?: string) => {
        if (entityType === 'systemuser') {
          return { _businessunitid_value: userBuId };
        }
        if (entityType === 'businessunit') {
          return buFields;
        }
        throw new Error(`Unexpected entityType: ${entityType}`);
      }),
      retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    };
  }

  it('chains systemuser -> businessunit and returns the container and the BU id', async () => {
    const webApi = makeWebApi('bu-guid-1', { sprk_containerid: 'bu-container-abc' });
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');

    expect(result).toEqual({ businessUnitId: 'bu-guid-1', containerId: 'bu-container-abc' });
    expect(webApi.retrieveRecord).toHaveBeenCalledWith('systemuser', 'user-guid-1', '?$select=_businessunitid_value');
    expect(webApi.retrieveRecord).toHaveBeenCalledWith('businessunit', 'bu-guid-1', '?$select=sprk_containerid');
  });

  it('selects no search-index column from the business unit and surfaces none, even when the BU holds one (FR-40)', async () => {
    const webApi = makeWebApi('bu-guid-1', {
      sprk_containerid: 'bu-container-abc',
      sprk_searchindexname: 'spaarke-knowledge-index-v2',
      _sprk_ai_search_index_value: '11111111-1111-1111-1111-111111111111',
    });
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');

    const buCall = (webApi.retrieveRecord as jest.Mock).mock.calls.find(([entity]) => entity === 'businessunit');
    expect(buCall?.[2]).not.toMatch(/searchindex|ai_search_index/i);
    expect(Object.keys(result).sort()).toEqual(['businessUnitId', 'containerId']);
  });

  it('strips braces from userId before the lookup', async () => {
    const webApi = makeWebApi('bu-guid-1', { sprk_containerid: 'c' });
    await EntityCreationService.resolveUserBuDefaults(webApi, '{user-guid-1}');
    expect(webApi.retrieveRecord).toHaveBeenCalledWith('systemuser', 'user-guid-1', '?$select=_businessunitid_value');
  });

  it('returns all-undefined when user has no business unit', async () => {
    const webApi = makeWebApi(null, {});
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');
    expect(result).toEqual({ businessUnitId: undefined, containerId: undefined });
    // BU lookup must be skipped when there is no BU id.
    expect(webApi.retrieveRecord).toHaveBeenCalledTimes(1);
  });

  it('normalizes a NULL container to undefined (a BU the operator has not set up yet)', async () => {
    const webApi = makeWebApi('bu-guid-1', { sprk_containerid: null });
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');
    expect(result).toEqual({ businessUnitId: 'bu-guid-1', containerId: undefined });
  });

  it('normalizes an empty-string container to undefined', async () => {
    const webApi = makeWebApi('bu-guid-1', { sprk_containerid: '   ' });
    const result = await EntityCreationService.resolveUserBuDefaults(webApi, 'user-guid-1');
    expect(result).toEqual({ businessUnitId: 'bu-guid-1', containerId: undefined });
  });
});

// ---------------------------------------------------------------------------
// indexUploadedFiles
// ---------------------------------------------------------------------------

describe('EntityCreationService.indexUploadedFiles', () => {
  it('sends an index-file body with no searchIndexName key (the index follows the content type, FR-40)', async () => {
    const bodies: Array<Record<string, unknown>> = [];
    const fetchMock = jest.fn().mockImplementation(async (_url: string, init?: RequestInit) => {
      bodies.push(JSON.parse(String(init?.body)));
      return { ok: true, status: 200, json: async () => ({ success: true, chunksIndexed: 3 }) } as unknown as Response;
    });
    const service = new EntityCreationService({} as IWebApiWithCreate, fetchMock, 'https://bff.example.test');

    const warnings = await service.indexUploadedFiles(
      [{ id: '01ITEM', name: 'a.docx', size: 10, driveId: 'b!drive' }],
      'tenant-1',
      { entityType: 'sprk_matter', entityId: 'matter-1', entityName: 'M' },
      ['doc-1']
    );

    expect(warnings).toEqual([]);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe('https://bff.example.test/api/ai/rag/index-file');
    expect(Object.keys(bodies[0]).sort()).toEqual(
      ['documentId', 'driveId', 'fileName', 'itemId', 'parentEntity', 'tenantId'].sort()
    );
    expect(bodies[0]).not.toHaveProperty('searchIndexName');
  });
});
