/**
 * MatterService — the BU cascade is no longer written onto the matter (task 021, FR-16)
 *
 * History: spaarke-multi-container-multi-index-r1 (FR-WIZ-01 / FR-WIZ-08) cascaded `sprk_searchindexname` (and the
 * `sprk_ai_search_index` bind) from the user's owning Business Unit onto the create payload, and task 076 (2026-09-03)
 * deleted the `sprk_containerid` writes. Task 041 (FR-40) made both index columns the SERVER's on a matter: the index
 * follows the content type, and the create path drops whatever a caller sends for either. Task 021 moves the wizard onto
 * `POST /api/v1/child-records/sprk_matter` with the user's own values only, so the last client-side cascade write goes.
 *
 * Contract: `matterService.createMatter` still accepts `cascadeDefaults?: IUserBuCascadeDefaults` (the host resolves it
 * from `Xrm.Utility.getGlobalContext().userSettings.userId`), but only to name the index that uploaded FILES are indexed
 * into. It is never a source for the matter's own columns.
 *
 * Covered (all negative on the create body, plus the graceful-degradation cases the old suite held):
 *   - `sprk_searchindexname`, `sprk_ai_search_index` (the bind, whichever nav-prop casing) and `sprk_containerid` are
 *     NEVER in the body -- whatever the host resolved and however the service was constructed;
 *   - omitted / partial cascade defaults still create the matter, and no BU lookup is made on the client.
 */

import { MatterService } from '../matterService';
import type { ICreateMatterFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import { EntityCreationService } from '../../../services/EntityCreationService';
import type { IUserBuCascadeDefaults } from '../../../services/EntityCreationService';
import type { IUploadedFile } from '../wizardTypes';

const MOCK_BU_ID = 'bu-guid-xyz';
const MOCK_CONTAINER_ID = 'spe-container-from-host';
const MOCK_BU_SEARCH_INDEX = 'spaarke-knowledge-index-v2';
const MOCK_BU_SEARCH_INDEX_ID = 'index-guid-from-bu';
const BFF_BASE = 'https://example.test';
const ROUTE = `${BFF_BASE}/api/v1/child-records/sprk_matter`;

function makeForm(overrides?: Partial<ICreateMatterFormState>): ICreateMatterFormState {
  return {
    matterTypeId: '',
    matterTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    matterName: 'Test Matter',
    assignedAttorneyId: '',
    assignedAttorneyName: '',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedOutsideCounselId: '',
    assignedOutsideCounselName: '',
    summary: '',
    ...overrides,
  };
}

function makeDataService(): IDataService {
  return {
    createRecord: jest.fn().mockResolvedValue('xrm-created-guid'),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

/** Answers the create route with an id and every other call (the access sync) with an empty 200. */
function makeFetch(): jest.Mock {
  return jest.fn(async (url: string) =>
    String(url).startsWith(ROUTE)
      ? ({
          ok: true,
          status: 201,
          json: async () => ({ id: 'created-record-guid' }),
          text: async () => '',
        } as unknown as Response)
      : ({ ok: true, status: 200, json: async () => ({}), text: async () => '' } as unknown as Response)
  );
}

/** The body POSTed to the create route. */
function createBody(fetchMock: jest.Mock): Record<string, unknown> {
  const call = fetchMock.mock.calls.find(([url]) => String(url).startsWith(ROUTE));
  expect(call).toBeDefined();
  return JSON.parse((call![1] as RequestInit).body as string);
}

function expectNoCascadeColumns(body: Record<string, unknown>): void {
  expect(body).not.toHaveProperty('sprk_searchindexname');
  expect(body).not.toHaveProperty('sprk_containerid');
  expect(Object.keys(body).filter(k => /ai_search_index/i.test(k))).toEqual([]);
}

describe('MatterService — BU cascade defaults are not written onto the matter (task 021)', () => {
  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
    jest.clearAllMocks();
  });

  it('writes none of the cascade columns when the host resolved the full BU defaults (name, index id, container)', async () => {
    const fetchMock = makeFetch();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: MOCK_CONTAINER_ID,
      searchIndexName: MOCK_BU_SEARCH_INDEX,
      searchIndexId: MOCK_BU_SEARCH_INDEX_ID,
    };

    const service = new MatterService(makeDataService(), fetchMock, BFF_BASE, MOCK_CONTAINER_ID);
    const result = await service.createMatter(makeForm(), [], {}, cascadeDefaults);

    expect(result.status).toBe('success');
    const body = createBody(fetchMock);
    expectNoCascadeColumns(body);
    expect(JSON.stringify(body)).not.toContain(MOCK_BU_SEARCH_INDEX);
    expect(JSON.stringify(body)).not.toContain(MOCK_BU_SEARCH_INDEX_ID);
    expect(JSON.stringify(body)).not.toContain(MOCK_CONTAINER_ID);
  });

  it('writes no container from EITHER source (the host-injected container, the BU cascade container)', async () => {
    const fetchMock = makeFetch();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: 'cascade-container-should-be-ignored',
      searchIndexName: MOCK_BU_SEARCH_INDEX,
    };

    // No host container: the only container in play is the cascade's.
    await new MatterService(makeDataService(), fetchMock, BFF_BASE, undefined).createMatter(
      makeForm(),
      [],
      {},
      cascadeDefaults
    );
    expectNoCascadeColumns(createBody(fetchMock));

    // Both: still neither.
    const second = makeFetch();
    await new MatterService(makeDataService(), second, BFF_BASE, MOCK_CONTAINER_ID).createMatter(
      makeForm(),
      [],
      {},
      cascadeDefaults
    );
    const body = createBody(second);
    expectNoCascadeColumns(body);
    expect(JSON.stringify(body)).not.toContain('cascade-container-should-be-ignored');
  });

  it('creates the matter when cascadeDefaults.searchIndexName is undefined (Phase A.5 ordering scenario)', async () => {
    const fetchMock = makeFetch();
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: MOCK_CONTAINER_ID,
      searchIndexName: undefined,
    };

    const result = await new MatterService(makeDataService(), fetchMock, BFF_BASE, MOCK_CONTAINER_ID).createMatter(
      makeForm(),
      [],
      {},
      cascadeDefaults
    );

    expect(result.status).toBe('success');
    expectNoCascadeColumns(createBody(fetchMock));
  });

  it('creates the matter when cascadeDefaults is omitted entirely, and makes no BU lookup on the client', async () => {
    const fetchMock = makeFetch();
    const dataService = makeDataService();

    const result = await new MatterService(dataService, fetchMock, BFF_BASE, MOCK_CONTAINER_ID).createMatter(
      makeForm(),
      [],
      {}
    );

    expect(result.status).toBe('success');
    expectNoCascadeColumns(createBody(fetchMock));
    expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('businessunit', expect.anything(), expect.anything());
    expect(dataService.retrieveRecord).not.toHaveBeenCalledWith('systemuser', expect.anything(), expect.anything());
  });
});

// Fix round 1 (task 021, finding 4): the server's FileIndexRequest has no searchIndexName (FR-40: the index follows the
// content type), so the wizard no longer names an index when it indexes the uploaded files.
describe('MatterService — file indexing names no search index (task 021 fix round 1)', () => {
  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('calls indexUploadedFiles without a search index name, even when the host resolved one', async () => {
    const file = { id: 'item-1', name: 'a.pdf', size: 10, webUrl: 'https://x/a.pdf', driveId: 'b!drive' };
    jest.spyOn(EntityCreationService.prototype, 'uploadFilesToSpe').mockResolvedValue({
      success: true,
      successCount: 1,
      failureCount: 0,
      uploadedFiles: [file],
      errors: [],
    } as never);
    jest
      .spyOn(EntityCreationService.prototype, 'createDocumentRecords')
      .mockResolvedValue({ success: true, linkedCount: 1, createdDocumentIds: ['doc-1'], warnings: [] });
    const index = jest.spyOn(EntityCreationService.prototype, 'indexUploadedFiles').mockResolvedValue([]);
    const cascadeDefaults: IUserBuCascadeDefaults = {
      businessUnitId: MOCK_BU_ID,
      containerId: MOCK_CONTAINER_ID,
      searchIndexName: MOCK_BU_SEARCH_INDEX,
    };

    const result = await new MatterService(
      makeDataService(),
      makeFetch(),
      BFF_BASE,
      MOCK_CONTAINER_ID,
      'tenant-1'
    ).createMatter(makeForm(), [{ id: 'u1', name: 'a.pdf' } as unknown as IUploadedFile], {}, cascadeDefaults);

    expect(result.status).not.toBe('error');
    expect(index).toHaveBeenCalledTimes(1);
    const args = index.mock.calls[0];
    expect(args).toHaveLength(4);
    expect(JSON.stringify(args)).not.toContain(MOCK_BU_SEARCH_INDEX);
  });
});
