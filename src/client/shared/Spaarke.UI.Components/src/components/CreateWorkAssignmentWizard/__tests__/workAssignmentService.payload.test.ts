/**
 * WorkAssignmentService — the server write path (field-mapping-server-write-path-r1, task 026, FR-16)
 *
 * The Create Work Assignment wizard no longer maps fields on the client. It sends ONLY what the user entered or picked
 * plus the filing / regarding keys to `POST /api/v1/child-records/sprk_workassignment`; the follow-on Event goes to
 * `POST /api/v1/child-records/sprk_event`. The server applies the mapping (fill-only, FR-02), the owner (FR-09) and the
 * search index (FR-38).
 *
 * Proven here:
 *   (a) `applyFieldMappings` is never imported or called, and no field-mapping profile is read;
 *   (b) the work assignment goes to the route with the related record as the explicit mapping source in the query string
 *       (sprk_workassignment is not in the server's InferMappingSourceTables), and with no query string when it has none;
 *   (c) the follow-on event goes to the route with the NEW work assignment as its mapping source, after the work
 *       assignment, never through `dataService.createRecord` (Xrm.WebApi); a route warning reaches the caller;
 *   (d) both bodies hold exactly the user-entered keys plus the filing / regarding keys (exact key sets): no owner, owning
 *       team, impersonation, container, search-index or mapped key;
 *   (e) each Assign Work lookup binds its FULL logical column even when a look-alike column is listed first
 *       (`findNavProp` binds the first column CONTAINING a fragment).
 */

import * as fs from 'fs';
import * as path from 'path';

import type { IDataService } from '../../../types/serviceInterfaces';
import { WorkAssignmentService } from '../workAssignmentService';
import { EMPTY_ASSIGN_WORK_STATE, EMPTY_FOLLOW_ON_EVENT_STATE, EMPTY_WORK_ASSIGNMENT_FORM } from '../formTypes';
import type { ICreateWorkAssignmentFormState } from '../formTypes';
import { _resetNavPropCacheForTests } from '../../../services/PolymorphicResolverService';
import { applyFieldMappings } from '../../../services/FieldMappingService';

jest.mock('../../../services/FieldMappingService', () => ({
  applyFieldMappings: jest.fn(),
}));

const BFF_BASE = 'https://bff.test';
const WA_ROUTE = `${BFF_BASE}/api/v1/child-records/sprk_workassignment`;
const EVENT_ROUTE = `${BFF_BASE}/api/v1/child-records/sprk_event`;
const NEW_WA_ID = '33333333-3333-3333-3333-333333333333';
const NEW_EVENT_ID = '44444444-4444-4444-4444-444444444444';
const RT_MATTER_GUID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID_RAW = '{39CDE3E3-9D15-4B29-9A1E-1234567890AB}';
const MATTER_ID_CLEAN = '39cde3e3-9d15-4b29-9a1e-1234567890ab';
const MATTER_TYPE_ID = '55555555-5555-5555-5555-555555555555';
const PRACTICE_AREA_ID = '66666666-6666-6666-6666-666666666666';
const ATTORNEY_ID = '71111111-1111-1111-1111-111111111111';
const PARALEGAL_ID = '72222222-2222-2222-2222-222222222222';
const LAW_FIRM_ID = '73333333-3333-3333-3333-333333333333';
const LAW_FIRM_ATTORNEY_ID = '74444444-4444-4444-4444-444444444444';
const ASSIGNED_TO_ID = '88888888-8888-8888-8888-888888888888';

function rel(column: string, navProp: string, referenced: string) {
  return {
    ReferencingAttribute: column,
    ReferencingEntityNavigationPropertyName: navProp,
    ReferencedEntity: referenced,
  };
}

/**
 * sprk_workassignment ManyToOne relationships (docs/data-model/sprk_workassignment.md), the look-alike contact and
 * organization lookups DELIBERATELY listed before the primary slots the wizard means: a substring hint binds the first.
 */
const WA_RELATIONSHIPS = [
  rel('createdby', 'createdby', 'systemuser'),
  rel('sprk_assignedlawfirmattorney1', 'sprk_AssignedLawFirmAttorney1', 'contact'),
  rel('sprk_assignedattorney2', 'sprk_AssignedAttorney2', 'contact'),
  rel('sprk_assignedparalegal2', 'sprk_AssignedParalegal2', 'contact'),
  rel('sprk_assignedlawfirm2', 'sprk_AssignedLawFirm2', 'sprk_organization'),
  rel('sprk_assignedtointernal', 'sprk_AssignedToInternal', 'contact'),
  rel('sprk_assignedto', 'sprk_AssignedTo', 'contact'),
  rel('sprk_assignedattorney1', 'sprk_AssignedAttorney1', 'contact'),
  rel('sprk_assignedparalegal1', 'sprk_AssignedParalegal1', 'contact'),
  rel('sprk_assignedlawfirm1', 'sprk_AssignedLawFirm1', 'sprk_organization'),
  rel('sprk_mattertype', 'sprk_MatterType', 'sprk_mattertype_ref'),
  rel('sprk_practicearea', 'sprk_PracticeArea', 'sprk_practicearea_ref'),
  rel('sprk_regardingmatter', 'sprk_RegardingMatter', 'sprk_matter'),
  rel('sprk_regardingrecordtype', 'sprk_RegardingRecordType', 'sprk_recordtype_ref'),
];

/** sprk_event relationships: a look-alike work-assignment column first, the assignee look-alikes before sprk_assignedto. */
const EVENT_RELATIONSHIPS = [
  rel('sprk_relatedworkassignment', 'sprk_RelatedWorkAssignment', 'sprk_workassignment'),
  rel('sprk_regardingworkassignment', 'sprk_RegardingWorkAssignment', 'sprk_workassignment'),
  rel('sprk_assignedtoexternal', 'sprk_AssignedToExternal', 'contact'),
  rel('sprk_assignedto1', 'sprk_AssignedTo1', 'contact'),
  rel('sprk_assignedto', 'sprk_AssignedTo', 'contact'),
];

function stubNavPropFetch() {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async (url: string) => ({
    ok: true,
    status: 200,
    json: async () => ({
      value: url.includes("LogicalName='sprk_workassignment'")
        ? WA_RELATIONSHIPS
        : url.includes("LogicalName='sprk_event'")
          ? EVENT_RELATIONSHIPS
          : [],
    }),
  }));
}

/** The user's data service: catalog + parent reads for the resolver; any create or BU read would be a regression. */
function makeDataService(): IDataService {
  return {
    createRecord: jest.fn().mockResolvedValue('xrm-created-guid'),
    retrieveRecord: jest.fn().mockResolvedValue({}),
    retrieveMultipleRecords: jest.fn(async (entityName: string, options?: string) => {
      const query = options ?? '';
      if (entityName === 'sprk_recordtype_ref') {
        if (query.includes('sprk_recordtype_refid')) {
          return { entities: [{ sprk_recordtype_refid: RT_MATTER_GUID, sprk_recorddisplayname: 'Matter' }] };
        }
        if (query.includes('sprk_regardingrecordnumberfield')) {
          return { entities: [{ sprk_regardingrecordnumberfield: 'sprk_matternumber' }] };
        }
        if (query.includes('sprk_recorddisplaynamefield')) {
          return { entities: [{ sprk_recorddisplaynamefield: 'sprk_mattername' }] };
        }
        return { entities: [] };
      }
      if (entityName === 'sprk_matter') {
        return { entities: [{ sprk_matternumber: 'MAT-2026-0042', sprk_mattername: 'Smith v. Jones' }] };
      }
      return { entities: [] };
    }),
    updateRecord: jest.fn().mockResolvedValue(undefined),
    deleteRecord: jest.fn().mockResolvedValue(undefined),
  };
}

function json(status: number, body: Record<string, unknown>): Response {
  return {
    ok: status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
  } as unknown as Response;
}

/** An authenticated fetch answering both create routes; every other call gets {}. */
function makeFetch(opts?: { eventWarnings?: string[] }): jest.Mock {
  return jest.fn(async (url: string) => {
    if (url.startsWith(WA_ROUTE)) return json(201, { id: NEW_WA_ID });
    if (url.startsWith(EVENT_ROUTE)) return json(201, { id: NEW_EVENT_ID, warnings: opts?.eventWarnings });
    return json(200, {});
  });
}

interface IRouteCall {
  url: string;
  method: string;
  body: Record<string, unknown>;
}

function callsTo(fetchMock: jest.Mock, route: string): IRouteCall[] {
  return fetchMock.mock.calls
    .filter(([url]) => String(url).startsWith(route))
    .map(([url, init]) => ({
      url: String(url),
      method: String((init as RequestInit).method),
      body: JSON.parse((init as RequestInit).body as string),
    }));
}

function makeForm(over: Partial<ICreateWorkAssignmentFormState> = {}): ICreateWorkAssignmentFormState {
  return { ...EMPTY_WORK_ASSIGNMENT_FORM, name: 'Review the lease', ...over };
}

const MATTER_FILING = { recordType: 'matter' as const, recordId: MATTER_ID_RAW, recordName: 'Smith v. Jones' };

/** The filing / regarding keys a matter parent writes (ADR-024: the typed lookup + the resolver fields). */
const REGARDING_KEYS = [
  'sprk_RegardingMatter@odata.bind',
  'sprk_RegardingRecordType@odata.bind',
  'sprk_regardingrecordid',
  'sprk_regardingrecordname',
  'sprk_regardingrecordnumber',
  'sprk_regardingrecordurl',
];

/** Keys that may never be sent: the server owns them (owner, owning team, impersonation, container, search index). */
const SERVER_OWNED =
  /^(ownerid|owningteam|owninguser|owningbusinessunit|sprk_containerid|sprk_searchindexname|sprk_ai_search_index|sprk_issecure|sprk_createdbyperson|createdby|impersonat)/i;

describe('WorkAssignmentService — the server write path (task 026, FR-16)', () => {
  beforeEach(() => {
    _resetNavPropCacheForTests();
    stubNavPropFetch();
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    (applyFieldMappings as jest.Mock).mockReset();
  });

  afterEach(() => {
    jest.restoreAllMocks();
  });

  // (d) -------------------------------------------------------------------------------------------------------------
  it('the work assignment body holds exactly the user-entered keys plus the filing / regarding keys', async () => {
    const fetchMock = makeFetch();
    const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);

    const result = await service.createWorkAssignment(
      makeForm({
        ...MATTER_FILING,
        description: '  Please review  ',
        responseDueDate: '2026-12-01',
        priority: 100000002,
        matterTypeId: MATTER_TYPE_ID,
        practiceAreaId: PRACTICE_AREA_ID,
        assignedToId: `{${ASSIGNED_TO_ID.toUpperCase()}}`,
      }),
      [],
      [],
      {
        ...EMPTY_ASSIGN_WORK_STATE,
        assignedAttorneyId: ATTORNEY_ID,
        assignedParalegalId: PARALEGAL_ID,
        assignedLawFirmId: LAW_FIRM_ID,
        assignedLawFirmAttorneyId: LAW_FIRM_ATTORNEY_ID,
      }
    );

    expect(result.status).toBe('success');
    const [call] = callsTo(fetchMock, WA_ROUTE);
    expect(Object.keys(call.body).sort()).toEqual(
      [
        'sprk_name',
        'sprk_priority',
        'sprk_description',
        'sprk_responseduedate',
        'sprk_MatterType@odata.bind',
        'sprk_PracticeArea@odata.bind',
        'sprk_AssignedTo@odata.bind',
        'sprk_AssignedAttorney1@odata.bind',
        'sprk_AssignedParalegal1@odata.bind',
        'sprk_AssignedLawFirm1@odata.bind',
        'sprk_AssignedLawFirmAttorney1@odata.bind',
        ...REGARDING_KEYS,
      ].sort()
    );
    expect(call.body['sprk_name']).toBe('Review the lease');
    expect(call.body['sprk_priority']).toBe(100000002);
    expect(call.body['sprk_description']).toBe('Please review');
    expect(call.body['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID_CLEAN})`);
    expect(Object.keys(call.body).filter(k => SERVER_OWNED.test(k))).toEqual([]);
  });

  it('a minimal work assignment sends the name and the picked priority only', async () => {
    const fetchMock = makeFetch();
    const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);

    await service.createWorkAssignment(makeForm(), [], []);

    expect(callsTo(fetchMock, WA_ROUTE)[0].body).toEqual({ sprk_name: 'Review the lease', sprk_priority: 100000001 });
  });

  it('never reads the Business Unit and never creates through the data service (no Xrm.WebApi create)', async () => {
    const dataService = makeDataService();
    const service = new WorkAssignmentService(dataService, makeFetch(), BFF_BASE, undefined);

    await service.createWorkAssignment(makeForm(MATTER_FILING), [], []);
    await service.createFollowOnEvent(NEW_WA_ID, { ...EMPTY_FOLLOW_ON_EVENT_STATE });

    expect(dataService.retrieveRecord).not.toHaveBeenCalled();
    expect(dataService.createRecord).not.toHaveBeenCalled();
  });

  // (a) -------------------------------------------------------------------------------------------------------------
  it('does not use the client field-mapping engine: applyFieldMappings is never called and no profile is read', async () => {
    const fetchMock = makeFetch();
    const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);

    await service.createWorkAssignment(makeForm(MATTER_FILING), [], []);
    await service.createFollowOnEvent(NEW_WA_ID, { ...EMPTY_FOLLOW_ON_EVENT_STATE });

    expect(applyFieldMappings).not.toHaveBeenCalled();
    expect(fetchMock.mock.calls.some(([url]) => String(url).includes('/field-mappings/'))).toBe(false);
  });

  it('source scan: workAssignmentService.ts has no applyFieldMappings or FieldMappingService', () => {
    const source = fs.readFileSync(path.resolve(__dirname, '../workAssignmentService.ts'), 'utf8');
    expect(source).not.toMatch(/applyFieldMappings/);
    expect(source).not.toMatch(/FieldMappingService/);
  });

  // (b) -------------------------------------------------------------------------------------------------------------
  it('names the related record as the mapping source, in the query string only', async () => {
    const fetchMock = makeFetch();
    const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);

    await service.createWorkAssignment(makeForm(MATTER_FILING), [], []);

    const calls = callsTo(fetchMock, WA_ROUTE);
    expect(calls).toHaveLength(1);
    expect(calls[0].method).toBe('POST');
    expect(calls[0].url).toBe(`${WA_ROUTE}?mappingSourceEntity=sprk_matter&mappingSourceId=${MATTER_ID_CLEAN}`);
    expect(Object.keys(calls[0].body).filter(k => /mappingsource/i.test(k))).toEqual([]);
  });

  it.each([
    ['project', 'sprk_project'],
    ['invoice', 'sprk_invoice'],
    ['event', 'sprk_event'],
  ] as const)('a %s related record is the mapping source (%s)', async (recordType, entity) => {
    const fetchMock = makeFetch();
    const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);

    await service.createWorkAssignment(makeForm({ recordType, recordId: MATTER_ID_RAW, recordName: 'X' }), [], []);

    expect(callsTo(fetchMock, WA_ROUTE)[0].url).toBe(
      `${WA_ROUTE}?mappingSourceEntity=${entity}&mappingSourceId=${MATTER_ID_CLEAN}`
    );
  });

  it('with no related record there is no query string and no mapping source', async () => {
    const fetchMock = makeFetch();
    const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);

    await service.createWorkAssignment(makeForm({ assignWithoutRecord: true }), [], []);

    const calls = callsTo(fetchMock, WA_ROUTE);
    expect(calls).toHaveLength(1);
    expect(calls[0].url).toBe(WA_ROUTE);
    expect(Object.keys(calls[0].body).some(k => /regarding/i.test(k))).toBe(false);
  });

  // (c) + (d) -------------------------------------------------------------------------------------------------------
  it('the follow-on event posts to the route with the new work assignment as its mapping source', async () => {
    const fetchMock = makeFetch();
    const dataService = makeDataService();
    const service = new WorkAssignmentService(dataService, fetchMock, BFF_BASE, undefined);

    const result = await service.createFollowOnEvent(NEW_WA_ID, {
      eventName: '  Assign Work  ',
      eventDescription: ' Please act ',
      eventPriority: 100000002,
      eventDueDate: '2026-12-02',
      eventFinalDueDate: '2026-12-09',
      assignedToId: `{${ASSIGNED_TO_ID.toUpperCase()}}`,
      assignedToName: 'Contact',
    });

    expect(result).toEqual({ success: true, warnings: [] });
    const calls = callsTo(fetchMock, EVENT_ROUTE);
    expect(calls).toHaveLength(1);
    expect(calls[0].method).toBe('POST');
    expect(calls[0].url).toBe(`${EVENT_ROUTE}?mappingSourceEntity=sprk_workassignment&mappingSourceId=${NEW_WA_ID}`);
    expect(calls[0].body).toEqual({
      sprk_eventname: 'Assign Work',
      sprk_priority: 100000002,
      sprk_description: 'Please act',
      sprk_duedate: '2026-12-02',
      sprk_finalduedate: '2026-12-09',
      'sprk_RegardingWorkAssignment@odata.bind': `/sprk_workassignments(${NEW_WA_ID})`,
      'sprk_AssignedTo@odata.bind': `/contacts(${ASSIGNED_TO_ID})`,
    });
    expect(dataService.createRecord).not.toHaveBeenCalled();
  });

  it('a minimal follow-on event sends the name, the priority and the filing key only', async () => {
    const fetchMock = makeFetch();
    const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);

    await service.createFollowOnEvent(NEW_WA_ID, { ...EMPTY_FOLLOW_ON_EVENT_STATE });

    const [call] = callsTo(fetchMock, EVENT_ROUTE);
    expect(call.body).toEqual({
      sprk_eventname: 'Assign Work',
      sprk_priority: 100000001,
      'sprk_RegardingWorkAssignment@odata.bind': `/sprk_workassignments(${NEW_WA_ID})`,
    });
    expect(Object.keys(call.body).filter(k => SERVER_OWNED.test(k))).toEqual([]);
  });

  it("the event route's warnings reach the caller; a refusal is the existing 'create manually' warning", async () => {
    const warned = new WorkAssignmentService(
      makeDataService(),
      makeFetch({ eventWarnings: ['A mapped value was not written.'] }),
      BFF_BASE,
      undefined
    );
    expect(await warned.createFollowOnEvent(NEW_WA_ID, { ...EMPTY_FOLLOW_ON_EVENT_STATE })).toEqual({
      success: true,
      warnings: ['A mapped value was not written.'],
    });

    const refusing = new WorkAssignmentService(
      makeDataService(),
      jest.fn(async () => json(403, { title: 'Forbidden', status: 403, detail: 'No access.' })),
      BFF_BASE,
      undefined
    );
    const refused = await refusing.createFollowOnEvent(NEW_WA_ID, { ...EMPTY_FOLLOW_ON_EVENT_STATE });
    expect(refused.success).toBe(false);
    expect(refused.warning).toContain('Could not create follow-on event');
    expect(refused.warning).toContain('Please create manually.');
  });

  it('the event is created after the work assignment (completion order unchanged)', async () => {
    const fetchMock = makeFetch();
    const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);

    const wa = await service.createWorkAssignment(makeForm(MATTER_FILING), [], []);
    await service.createFollowOnEvent(wa.workAssignmentId!, { ...EMPTY_FOLLOW_ON_EVENT_STATE });

    const routes = fetchMock.mock.calls
      .map(([url]) => String(url))
      .filter(u => u.startsWith(WA_ROUTE) || u.startsWith(EVENT_ROUTE))
      .map(u => (u.startsWith(WA_ROUTE) ? 'workassignment' : 'event'));
    expect(routes).toEqual(['workassignment', 'event']);
  });

  // (e) -------------------------------------------------------------------------------------------------------------
  describe('lookups bind their FULL logical column, even when a look-alike is listed first', () => {
    async function createWith(assignWork: Partial<typeof EMPTY_ASSIGN_WORK_STATE>) {
      const fetchMock = makeFetch();
      const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);
      await service.createWorkAssignment(makeForm(), [], [], { ...EMPTY_ASSIGN_WORK_STATE, ...assignWork });
      return callsTo(fetchMock, WA_ROUTE)[0].body;
    }

    it('Assigned Attorney -> sprk_AssignedAttorney1 (not the law-firm attorney or attorney 2)', async () => {
      const body = await createWith({ assignedAttorneyId: ATTORNEY_ID });
      expect(body).toEqual(
        expect.objectContaining({ 'sprk_AssignedAttorney1@odata.bind': `/contacts(${ATTORNEY_ID})` })
      );
      expect(Object.keys(body).filter(k => /attorney/i.test(k))).toEqual(['sprk_AssignedAttorney1@odata.bind']);
    });

    it('Assigned Paralegal -> sprk_AssignedParalegal1 (not paralegal 2)', async () => {
      const body = await createWith({ assignedParalegalId: PARALEGAL_ID });
      expect(Object.keys(body).filter(k => /paralegal/i.test(k))).toEqual(['sprk_AssignedParalegal1@odata.bind']);
      expect(body['sprk_AssignedParalegal1@odata.bind']).toBe(`/contacts(${PARALEGAL_ID})`);
    });

    it('Assigned Law Firm -> sprk_AssignedLawFirm1 (not law firm 2)', async () => {
      const body = await createWith({ assignedLawFirmId: LAW_FIRM_ID });
      expect(Object.keys(body).filter(k => /lawfirm/i.test(k))).toEqual(['sprk_AssignedLawFirm1@odata.bind']);
      expect(body['sprk_AssignedLawFirm1@odata.bind']).toBe(`/sprk_organizations(${LAW_FIRM_ID})`);
    });

    it('Assigned Law Firm Attorney -> sprk_AssignedLawFirmAttorney1', async () => {
      const body = await createWith({ assignedLawFirmAttorneyId: LAW_FIRM_ATTORNEY_ID });
      expect(Object.keys(body).filter(k => /attorney/i.test(k))).toEqual(['sprk_AssignedLawFirmAttorney1@odata.bind']);
    });

    it('the follow-on event files under sprk_regardingworkassignment, not a look-alike work-assignment column', async () => {
      const fetchMock = makeFetch();
      const service = new WorkAssignmentService(makeDataService(), fetchMock, BFF_BASE, undefined);
      await service.createFollowOnEvent(NEW_WA_ID, { ...EMPTY_FOLLOW_ON_EVENT_STATE });
      const body = callsTo(fetchMock, EVENT_ROUTE)[0].body;
      expect(Object.keys(body).filter(k => /workassignment/i.test(k))).toEqual([
        'sprk_RegardingWorkAssignment@odata.bind',
      ]);
    });
  });
});
