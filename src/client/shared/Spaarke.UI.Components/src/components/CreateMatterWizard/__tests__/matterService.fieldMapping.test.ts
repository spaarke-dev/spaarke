/**
 * MatterService — the server write path (field-mapping-server-write-path-r1, task 021, FR-16)
 *
 * Replaces the task-020 "client mapping engine wiring" tests. The Matter wizard no longer maps fields, generates a
 * matter number or writes a BU default: it sends ONLY what the user entered or picked to
 * `POST /api/v1/child-records/sprk_matter`, and the server applies the mapping (fill-only, FR-02), the owner (FR-09),
 * the search-index default (FR-38/FR-40) and the autonumber (I-11).
 *
 * Proven here:
 *   - the payload sent to the route holds exactly the user-entered keys: no `sprk_matternumber`, no owner / owning-team
 *     / impersonation value, no search-index or container value (even when the host resolved BU defaults), and nothing
 *     a mapping profile would have filled;
 *   - the create goes to the BFF route, never `dataService.createRecord('sprk_matter', ...)` (Xrm.WebApi);
 *   - the AssociateToStep selection travels as the explicit mapping source in the QUERY STRING, and the body is the same
 *     with or without it;
 *   - `applyFieldMappings` is never imported and never called, and no field-mapping profile is read;
 *   - the route's warnings reach the result; a refusal is a hard error and creates nothing else.
 */

import * as fs from 'fs';
import * as path from 'path';

import { MatterService } from '../matterService';
import type { ICreateMatterFormState } from '../formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';
import type { IUserBuCascadeDefaults } from '../../../services/EntityCreationService';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import { applyFieldMappings } from '../../../services/FieldMappingService';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';

jest.mock('../../../services/FieldMappingService', () => ({
  applyFieldMappings: jest.fn(),
}));

const BFF_BASE = 'https://example.test';
const ROUTE = `${BFF_BASE}/api/v1/child-records/sprk_matter`;
const NEW_ID = '0b0b0b0b-0000-0000-0000-000000000021';
const PROJECT_ID = '11111111-1111-1111-1111-111111111111';

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
    retrieveRecord: jest.fn().mockResolvedValue({ sprk_mattertypecode: 'LIT' }),
    retrieveMultipleRecords: jest.fn().mockResolvedValue({ entities: [] }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

function created(body: Record<string, unknown> = { id: NEW_ID }): Response {
  return { ok: true, status: 201, json: async () => body, text: async () => '' } as unknown as Response;
}

/** An authenticated fetch that answers the create route with `answer` and every other call (the access sync) with {}. */
function makeFetch(answer: () => Response | Promise<Response> = () => created()): jest.Mock {
  return jest.fn(async (url: string) =>
    url.startsWith(ROUTE)
      ? answer()
      : ({ ok: true, status: 200, json: async () => ({}), text: async () => '' } as unknown as Response)
  );
}

/** The requests made to the create route: [full url, parsed body]. */
function createRequests(fetchMock: jest.Mock): Array<{ url: string; body: Record<string, unknown> }> {
  return fetchMock.mock.calls
    .filter(([url]) => String(url).startsWith(ROUTE))
    .map(([url, init]) => ({ url: String(url), body: JSON.parse((init as RequestInit).body as string) }));
}

const PROJECT_ASSOCIATION: AssociationResult = {
  entityType: 'sprk_project',
  recordId: `{${PROJECT_ID.toUpperCase()}}`,
  recordName: 'Alpha Project',
};

describe('MatterService — creates through the server write path (task 021, FR-16)', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    // Nav-prop discovery reads through the global fetch: offline, so the lookup keys fall back to the column names.
    globalThis.fetch = jest.fn(
      async () => ({ ok: false, status: 404, json: async () => ({ value: [] }) }) as unknown as Response
    ) as unknown as typeof fetch;
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    _resetNavPropCacheForTests();
    jest.restoreAllMocks();
    jest.clearAllMocks();
  });

  it('sends ONLY the keys the user entered or picked -- nothing a default, a mapping or the server owns', async () => {
    const dataService = makeDataService();
    const fetchMock = makeFetch();
    // The host resolved the BU defaults (index name, index id, container) -- none of them may reach the body.
    const cascade: IUserBuCascadeDefaults = {
      businessUnitId: 'bu-1',
      containerId: 'bu-container',
      searchIndexName: 'bu-search-index',
      searchIndexId: 'bu-index-guid',
    };

    const service = new MatterService(dataService, fetchMock, BFF_BASE, 'host-container');
    const result = await service.createMatter(
      makeForm({
        matterName: '  Smith v. Jones  ',
        summary: ' A dispute ',
        matterTypeId: 'mt-guid-1',
        practiceAreaId: 'pa-guid-1',
        assignedAttorneyId: 'att-guid-1',
        assignedParalegalId: 'para-guid-1',
        assignedOutsideCounselId: '{OC-GUID-1}',
      }),
      [],
      {},
      cascade
    );

    expect(result.status).toBe('success');
    expect(result.matterId).toBe(NEW_ID);

    const [request] = createRequests(fetchMock);
    expect(Object.keys(request.body).sort()).toEqual(
      [
        'sprk_mattername',
        'sprk_matterdescription',
        'sprk_mattertype@odata.bind',
        'sprk_practicearea@odata.bind',
        'sprk_assignedattorney1@odata.bind',
        'sprk_assignedparalegal1@odata.bind',
        'sprk_assignedlawfirm1@odata.bind',
      ].sort()
    );
    expect(request.body['sprk_mattername']).toBe('Smith v. Jones');
    expect(request.body['sprk_matterdescription']).toBe('A dispute');
    expect(request.body['sprk_assignedlawfirm1@odata.bind']).toBe('/sprk_organizations(oc-guid-1)');
  });

  it('never sends a matter number, however the Matter Type is chosen (the platform autonumber issues it, I-11)', async () => {
    const dataService = makeDataService();
    const fetchMock = makeFetch();

    const service = new MatterService(dataService, fetchMock, BFF_BASE);
    await service.createMatter(makeForm({ matterTypeId: 'mt-guid-1' }), [], {});

    const [request] = createRequests(fetchMock);
    expect(request.body).not.toHaveProperty('sprk_matternumber');
    // The Matter Type code lookup that fed the old number is gone: nothing reads the type record.
    expect(dataService.retrieveRecord).not.toHaveBeenCalled();
  });

  it('sends no owner, owning team, impersonation, container or search-index value', async () => {
    const fetchMock = makeFetch();
    const service = new MatterService(makeDataService(), fetchMock, BFF_BASE, 'host-container');
    await service.createMatter(
      makeForm({ matterTypeId: 'mt-guid-1' }),
      [],
      {},
      {
        businessUnitId: 'bu-1',
        containerId: 'bu-container',
        searchIndexName: 'bu-search-index',
        searchIndexId: 'bu-index-guid',
      }
    );

    const [request] = createRequests(fetchMock);
    const keys = Object.keys(request.body);
    expect(keys.filter(k => /owner|owning|impersonat|createdby|createdonbehalf/i.test(k))).toEqual([]);
    expect(keys.filter(k => /container|searchindex|search_index/i.test(k))).toEqual([]);
    const sent = JSON.stringify(request.body);
    expect(sent).not.toContain('bu-search-index');
    expect(sent).not.toContain('bu-index-guid');
    expect(sent).not.toContain('bu-container');
    expect(sent).not.toContain('host-container');
  });

  it('creates through the BFF child-records route, never Xrm.WebApi (dataService.createRecord is not called)', async () => {
    const dataService = makeDataService();
    const fetchMock = makeFetch();

    const service = new MatterService(dataService, fetchMock, BFF_BASE);
    const result = await service.createMatter(makeForm(), [], {});

    expect(result.status).toBe('success');
    expect(dataService.createRecord).not.toHaveBeenCalled();
    expect(createRequests(fetchMock)).toHaveLength(1);
    const call = fetchMock.mock.calls.find(([url]) => String(url).startsWith(ROUTE));
    expect(call?.[0]).toBe(ROUTE); // no association: no mapping source in the query string
    expect((call?.[1] as RequestInit).method).toBe('POST');
  });

  it('names the association as the explicit mapping source in the QUERY STRING; the body is the same with or without it', async () => {
    const withoutFetch = makeFetch();
    await new MatterService(makeDataService(), withoutFetch, BFF_BASE).createMatter(
      makeForm({ summary: 'The user typed this' }),
      [],
      {}
    );

    const withFetch = makeFetch();
    const result = await new MatterService(makeDataService(), withFetch, BFF_BASE).createMatter(
      makeForm({ summary: 'The user typed this' }),
      [],
      {},
      undefined,
      undefined,
      PROJECT_ASSOCIATION
    );

    expect(result.status).toBe('success');
    const [without] = createRequests(withoutFetch);
    const [withSource] = createRequests(withFetch);
    // Braces stripped, lower-case, source entity lower-case.
    expect(withSource.url).toBe(`${ROUTE}?mappingSourceEntity=sprk_project&mappingSourceId=${PROJECT_ID}`);
    // The body carries nothing of the association, and nothing a mapping profile would fill: the user's value only.
    expect(withSource.body).toEqual(without.body);
    expect(withSource.body).toEqual({ sprk_mattername: 'Test Matter', sprk_matterdescription: 'The user typed this' });
    expect(JSON.stringify(withSource.body)).not.toContain(PROJECT_ID);
  });

  it('does not map on the client: applyFieldMappings is never called and no field-mapping profile is read', async () => {
    const fetchMock = makeFetch();
    const service = new MatterService(makeDataService(), fetchMock, BFF_BASE);
    await service.createMatter(makeForm(), [], {}, undefined, undefined, PROJECT_ASSOCIATION);

    expect(applyFieldMappings).not.toHaveBeenCalled();
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/field-mappings/'))).toEqual([]);
  });

  it('matterService.ts neither imports nor mentions applyFieldMappings or FieldMappingService', () => {
    const source = fs.readFileSync(path.resolve(__dirname, '../matterService.ts'), 'utf8');
    expect(source).not.toMatch(/applyFieldMappings/);
    expect(source).not.toMatch(/FieldMappingService/);
    expect(source).not.toMatch(/sprk_matternumber['"\]]\s*\]?\s*=/);
    expect(source).not.toMatch(/random6|Math\.random/);
  });

  it("surfaces the route's warnings on the result, as a partial success", async () => {
    const fetchMock = makeFetch(() =>
      created({ id: NEW_ID, warnings: ["The value you chose for 'sprk_practicearea' was not written."] })
    );

    const result = await new MatterService(makeDataService(), fetchMock, BFF_BASE).createMatter(
      makeForm(),
      [],
      {},
      undefined,
      undefined,
      PROJECT_ASSOCIATION
    );

    expect(result.status).toBe('partial');
    expect(result.matterId).toBe(NEW_ID);
    expect(result.warnings).toEqual(["The value you chose for 'sprk_practicearea' was not written."]);
  });

  it('a refusal by the route is a hard error: no record id, no access sync, no follow-on', async () => {
    const fetchMock = jest.fn(async (url: string): Promise<Response> => {
      if (url.startsWith(ROUTE)) {
        throw apiErrorFor(404, { title: 'Not found', status: 404, detail: 'The record was not found.' });
      }
      return created({});
    });
    silenceErrors();

    const result = await new MatterService(makeDataService(), fetchMock, BFF_BASE).createMatter(makeForm(), [], {
      sendEmail: { to: 'a@b.test', subject: 's', body: 'b' },
    });

    expect(result.status).toBe('error');
    expect(result.matterId).toBeUndefined();
    expect(result.errorMessage).toContain('Failed to create matter record');
    // Only the create was attempted: no assigned-access sync, no email.
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

/** The service logs the full error with console.error; keep the run quiet. */
function silenceErrors(): void {
  jest.spyOn(console, 'error').mockImplementation(() => undefined);
}
