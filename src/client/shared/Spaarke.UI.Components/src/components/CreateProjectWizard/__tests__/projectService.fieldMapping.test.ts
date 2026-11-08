/**
 * ProjectService — the server write path (field-mapping-server-write-path-r1, task 022, FR-16, FR-15)
 *
 * Replaces the task-020 "client mapping engine wiring" tests. The Project wizard no longer maps fields or writes a BU
 * default: it sends ONLY what the user entered or picked to `POST /api/v1/child-records/sprk_project`, and the server
 * applies the mapping (fill-only, FR-02), the owner (FR-09), the search-index default (FR-38/FR-40) and the autonumber.
 * A secure project is asked for with the route's `secure=true` query parameter (task 049 fix round 2), for every host.
 *
 * Proven here:
 *   - the payload holds exactly the user-entered keys: no owner / owning-team / impersonation value, no search-index,
 *     container or secure-flag value (even when the host resolved BU defaults), and nothing a mapping profile would fill;
 *   - the create goes to the BFF route, never `dataService.createRecord('sprk_project', ...)` (Xrm.WebApi), whether or not
 *     the data service is BFF-decorated; with no BFF connection the create is refused and nothing is written;
 *   - the AssociateToStep selection travels as the explicit mapping source in the QUERY STRING, and the body is the same
 *     with or without it; the secure intent travels there too;
 *   - `applyFieldMappings` is never imported and never called, and no field-mapping profile is read;
 *   - no client access sync after the create;
 *   - the route's warnings reach the result; a refusal is a failed create with the root-create wording.
 */

import * as fs from 'fs';
import * as path from 'path';

import { ProjectService } from '../projectService';
import type { ICreateProjectFormState } from '../projectFormTypes';
import type { IDataService } from '../../../types/serviceInterfaces';
import type { AssociationResult } from '../../AssociateToStep/types';
import type { IUserBuCascadeDefaults } from '../../../services/EntityCreationService';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import { applyFieldMappings } from '../../../services/FieldMappingService';
import { ASSIGNED_ACCESS_SYNC_PATH } from '../../../services/assignedAccessSync';
import { withBffChildWrites } from '../../../utils/adapters/bffChildWriteAdapter';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';

jest.mock('../../../services/FieldMappingService', () => ({
  applyFieldMappings: jest.fn(),
}));

const BFF_BASE = 'https://example.test';
const ROUTE = `${BFF_BASE}/api/v1/child-records/sprk_project`;
const NEW_ID = '0c0c0c0c-0000-0000-0000-000000000022';
const MATTER_ID = '22222222-2222-2222-2222-222222222222';

function makeForm(overrides?: Partial<ICreateProjectFormState>): ICreateProjectFormState {
  return {
    projectTypeId: '',
    projectTypeName: '',
    practiceAreaId: '',
    practiceAreaName: '',
    projectName: 'Test Project',
    assignedAttorneyId: '',
    assignedAttorneyName: '',
    assignedParalegalId: '',
    assignedParalegalName: '',
    assignedOutsideCounselId: '',
    assignedOutsideCounselName: '',
    description: '',
    isSecure: false,
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

function created(body: Record<string, unknown> = { id: NEW_ID }): Response {
  return { ok: true, status: 201, json: async () => body, text: async () => '' } as unknown as Response;
}

/** An authenticated fetch that answers the create route with `answer` and every other call with {}. */
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

const MATTER_ASSOCIATION: AssociationResult = {
  entityType: 'sprk_matter',
  recordId: `{${MATTER_ID.toUpperCase()}}`,
  recordName: 'Smith v. Jones',
};

const BU_DEFAULTS: IUserBuCascadeDefaults = {
  businessUnitId: 'bu-1',
  containerId: 'bu-container',
  searchIndexName: 'bu-search-index',
  searchIndexId: 'bu-index-guid',
};

/** Nav-prop metadata for sprk_project, so every user-picked lookup binds under its own navigation property. */
const PROJECT_NAV_PROPS = [
  {
    ReferencingAttribute: 'sprk_projecttype_ref',
    ReferencingEntityNavigationPropertyName: 'sprk_ProjectType_Ref',
    ReferencedEntity: 'sprk_projecttype_ref',
  },
  {
    ReferencingAttribute: 'sprk_practicearea',
    ReferencingEntityNavigationPropertyName: 'sprk_PracticeArea',
    ReferencedEntity: 'sprk_practicearea_ref',
  },
  {
    ReferencingAttribute: 'sprk_assignedattorney',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedAttorney',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedparalegal',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedParalegal',
    ReferencedEntity: 'contact',
  },
  {
    ReferencingAttribute: 'sprk_assignedoutsidecounsel',
    ReferencingEntityNavigationPropertyName: 'sprk_AssignedOutsideCounsel',
    ReferencedEntity: 'sprk_organization',
  },
  {
    ReferencingAttribute: 'sprk_ai_search_index',
    ReferencingEntityNavigationPropertyName: 'sprk_AI_Search_Index',
    ReferencedEntity: 'sprk_aisearchindex',
  },
];

describe('ProjectService — creates through the server write path (task 022, FR-16)', () => {
  const originalFetch = globalThis.fetch;

  beforeEach(() => {
    _resetNavPropCacheForTests();
    // Nav-prop discovery reads through the global fetch.
    globalThis.fetch = jest.fn(
      async () => ({ ok: true, status: 200, json: async () => ({ value: PROJECT_NAV_PROPS }) }) as unknown as Response
    ) as unknown as typeof fetch;
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
  });

  afterEach(() => {
    globalThis.fetch = originalFetch;
    _resetNavPropCacheForTests();
    jest.restoreAllMocks();
    jest.clearAllMocks();
  });

  it('sends ONLY the keys the user entered or picked -- nothing a default, a mapping or the server owns', async () => {
    const fetchMock = makeFetch();

    const result = await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
      makeForm({
        projectName: '  Alpha Project  ',
        description: ' A review ',
        projectTypeId: 'pt-guid-1',
        practiceAreaId: 'pa-guid-1',
        assignedAttorneyId: 'att-guid-1',
        assignedParalegalId: 'para-guid-1',
        assignedOutsideCounselId: '{OC-GUID-1}',
      }),
      // The host resolved the BU defaults (index name, index id, container) -- none of them may reach the body.
      BU_DEFAULTS
    );

    expect(result.success).toBe(true);
    expect(result.projectId).toBe(NEW_ID);

    const [request] = createRequests(fetchMock);
    expect(Object.keys(request.body).sort()).toEqual(
      [
        'sprk_projectname',
        'sprk_projectdescription',
        'sprk_ProjectType_Ref@odata.bind',
        'sprk_PracticeArea@odata.bind',
        'sprk_AssignedAttorney@odata.bind',
        'sprk_AssignedParalegal@odata.bind',
        'sprk_AssignedOutsideCounsel@odata.bind',
      ].sort()
    );
    expect(request.body['sprk_projectname']).toBe('Alpha Project');
    expect(request.body['sprk_projectdescription']).toBe('A review');
    expect(request.body['sprk_AssignedOutsideCounsel@odata.bind']).toBe('/sprk_organizations(oc-guid-1)');
  });

  it.each([true, false])(
    'sends no owner, owning team, impersonation, container, search-index or secure-flag value (secure requested: %s)',
    async isSecure => {
      const fetchMock = makeFetch();
      await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
        makeForm({ projectTypeId: 'pt-guid-1', isSecure }),
        BU_DEFAULTS
      );

      const [request] = createRequests(fetchMock);
      const keys = Object.keys(request.body);
      expect(keys.filter(k => /owner|owning|impersonat|createdby|createdonbehalf/i.test(k))).toEqual([]);
      expect(keys.filter(k => /container|searchindex|search_index|issecure/i.test(k))).toEqual([]);
      const sent = JSON.stringify(request.body);
      expect(sent).not.toContain('bu-search-index');
      expect(sent).not.toContain('bu-index-guid');
      expect(sent).not.toContain('bu-container');
    }
  );

  it('creates through the BFF child-records route, never Xrm.WebApi (dataService.createRecord is not called)', async () => {
    const dataService = makeDataService();
    const fetchMock = makeFetch();

    const result = await new ProjectService(dataService, fetchMock, BFF_BASE).createProject(makeForm());

    expect(result.success).toBe(true);
    expect(dataService.createRecord).not.toHaveBeenCalled();
    expect(createRequests(fetchMock)).toHaveLength(1);
    const call = fetchMock.mock.calls.find(([url]) => String(url).startsWith(ROUTE));
    expect(call?.[0]).toBe(ROUTE); // no association, not secure: no query string
    expect((call?.[1] as RequestInit).method).toBe('POST');
  });

  it('a secure project on an UNDECORATED data service (the standalone code page) is created with secure=true', async () => {
    const dataService = makeDataService();
    const fetchMock = makeFetch();

    const result = await new ProjectService(dataService, fetchMock, BFF_BASE).createProject(
      makeForm({ isSecure: true })
    );

    expect(result.success).toBe(true);
    expect(result.securedAtCreate).toBe(true);
    expect(dataService.createRecord).not.toHaveBeenCalled();
    expect(createRequests(fetchMock).map(r => r.url)).toEqual([`${ROUTE}?secure=true`]);
  });

  it('a decorated data service (InAppWizardHost) gives the same single route call', async () => {
    const inner = makeDataService();
    const fetchMock = makeFetch();

    const result = await new ProjectService(
      withBffChildWrites(inner, fetchMock, BFF_BASE),
      fetchMock,
      BFF_BASE
    ).createProject(makeForm({ isSecure: true }));

    expect(result.securedAtCreate).toBe(true);
    expect(inner.createRecord).not.toHaveBeenCalled();
    expect(createRequests(fetchMock).map(r => r.url)).toEqual([`${ROUTE}?secure=true`]);
  });

  it('names the association as the explicit mapping source in the QUERY STRING; the body is the same with or without it', async () => {
    const withoutFetch = makeFetch();
    await new ProjectService(makeDataService(), withoutFetch, BFF_BASE).createProject(
      makeForm({ description: 'The user typed this' })
    );

    const withFetch = makeFetch();
    const result = await new ProjectService(makeDataService(), withFetch, BFF_BASE).createProject(
      makeForm({ description: 'The user typed this' }),
      undefined,
      MATTER_ASSOCIATION
    );

    expect(result.success).toBe(true);
    const [without] = createRequests(withoutFetch);
    const [withSource] = createRequests(withFetch);
    // Braces stripped, lower-case, source entity lower-case.
    expect(withSource.url).toBe(`${ROUTE}?mappingSourceEntity=sprk_matter&mappingSourceId=${MATTER_ID}`);
    // The body carries nothing of the association, and nothing a mapping profile would fill: the user's value only
    // (the server's fill-only mapping keeps it over a mapped value, FR-02).
    expect(withSource.body).toEqual(without.body);
    expect(withSource.body).toEqual({
      sprk_projectname: 'Test Project',
      sprk_projectdescription: 'The user typed this',
    });
    expect(JSON.stringify(withSource.body)).not.toContain(MATTER_ID);
    expect(JSON.stringify(withSource.body).toLowerCase()).not.toContain('regarding');
  });

  it('a secure project under a matter sends both the mapping source and the secure intent in the query', async () => {
    const fetchMock = makeFetch();
    await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
      makeForm({ isSecure: true }),
      undefined,
      MATTER_ASSOCIATION
    );

    expect(createRequests(fetchMock).map(r => r.url)).toEqual([
      `${ROUTE}?mappingSourceEntity=sprk_matter&mappingSourceId=${MATTER_ID}&secure=true`,
    ]);
  });

  it('does not map on the client: applyFieldMappings is never called and no field-mapping profile is read', async () => {
    const fetchMock = makeFetch();
    await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
      makeForm(),
      undefined,
      MATTER_ASSOCIATION
    );

    expect(applyFieldMappings).not.toHaveBeenCalled();
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/field-mappings/'))).toEqual([]);
  });

  it('makes no client access sync after the create (the server grants the Assigned * access)', async () => {
    for (const isSecure of [false, true]) {
      const fetchMock = makeFetch();
      await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
        makeForm({ isSecure, assignedAttorneyId: 'att-guid-1' })
      );
      expect(fetchMock.mock.calls.filter(([url]) => String(url).endsWith(ASSIGNED_ACCESS_SYNC_PATH))).toEqual([]);
      expect(fetchMock).toHaveBeenCalledTimes(1);
    }
  });

  it('projectService.ts neither imports nor calls applyFieldMappings, FieldMappingService, the BU cascade or the access sync', () => {
    const source = fs.readFileSync(path.resolve(__dirname, '../projectService.ts'), 'utf8');
    expect(source).not.toMatch(/applyFieldMappings/);
    expect(source).not.toMatch(/FieldMappingService/);
    expect(source).not.toMatch(/applyUserBuDefaults/);
    expect(source).not.toMatch(/syncAssignedAccess/);
    expect(source).not.toMatch(/_dataService\.createRecord/);
  });

  it("surfaces the route's warnings on the result", async () => {
    const fetchMock = makeFetch(() =>
      created({ id: NEW_ID, warnings: ["The value you chose for 'sprk_practicearea' was not written."] })
    );

    const result = await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
      makeForm(),
      undefined,
      MATTER_ASSOCIATION
    );

    expect(result.success).toBe(true);
    expect(result.projectId).toBe(NEW_ID);
    expect(result.warnings).toEqual(["The value you chose for 'sprk_practicearea' was not written."]);
  });

  it('a refusal by the route is a failed create: no record id, no secure claim, nothing else asked', async () => {
    const fetchMock = jest.fn(async (url: string): Promise<Response> => {
      if (url.startsWith(ROUTE)) {
        throw apiErrorFor(400, {
          title: 'Bad',
          status: 400,
          detail: 'Name is required.',
          reasonCode: 'child_record.invalid',
        });
      }
      return created({});
    });

    const result = await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
      makeForm({ isSecure: true })
    );

    expect(result.success).toBe(false);
    expect(result.projectId).toBeUndefined();
    expect(result.securedAtCreate).toBeUndefined();
    expect(result.errorMessage).toBe('Failed to create project record: Name is required.');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('the route\'s uniform not-found is the root-create sentence, never "filed under" (the create names no parent)', async () => {
    const fetchMock = jest.fn(async (): Promise<Response> => {
      throw apiErrorFor(404, {
        title: 'Not found',
        status: 404,
        detail: 'A record this project is filed under was not found. The project was not saved.',
        reasonCode: 'child_record.not_found',
      });
    });

    const result = await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
      makeForm({ projectTypeId: 'pt-guid-1' })
    );

    expect(result.success).toBe(false);
    expect(result.errorMessage).toBe(
      "One of the values you selected was not found, or you don't have permission to link it to this project. Ask your administrator."
    );
  });

  it.each([
    [
      'sdap.access.deny.delegation_share_required',
      "You don't have the access needed to create a secure project. You can create it without Secure, or ask an administrator.",
    ],
    [
      'sdap.access.deny.delegation_check_failed',
      'Whether you can create a secure project could not be checked. Try again in a moment, or create it without Secure.',
    ],
  ])(
    'a secure create refused by the delegation rule (%s) shows its own sentence, unprefixed',
    async (reasonCode, sentence) => {
      const fetchMock = jest.fn(async (): Promise<Response> => {
        throw apiErrorFor(403, { title: 'Forbidden', status: 403, detail: 'operator text', reasonCode });
      });

      const result = await new ProjectService(makeDataService(), fetchMock, BFF_BASE).createProject(
        makeForm({ isSecure: true })
      );

      expect(result.success).toBe(false);
      expect(result.errorMessage).toBe(sentence);
    }
  );

  it.each([
    ['without authenticatedFetch', undefined, BFF_BASE],
    ['without a BFF base URL', 'fetch', undefined],
    ['with an empty BFF base URL', 'fetch', ''],
  ])('fails closed %s: nothing is created, through the route or Xrm.WebApi', async (_label, fetchKind, base) => {
    const dataService = makeDataService();
    const fetchMock = makeFetch();

    const result = await new ProjectService(
      dataService,
      fetchKind ? fetchMock : undefined,
      base as string | undefined
    ).createProject(makeForm({ isSecure: true }), undefined, MATTER_ASSOCIATION);

    expect(result.success).toBe(false);
    expect(result.errorMessage).toMatch(/not connected to the Spaarke service/);
    expect(result.securedAtCreate).toBeUndefined();
    expect(dataService.createRecord).not.toHaveBeenCalled();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
