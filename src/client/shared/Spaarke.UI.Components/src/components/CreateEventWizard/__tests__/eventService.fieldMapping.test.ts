/**
 * EventService — the server write path (field-mapping-server-write-path-r1, task 023, FR-16)
 *
 * Replaces the task-020 "client mapping engine wiring" tests. The Event wizard no longer maps fields or writes a BU
 * search-index default: it sends ONLY what the user entered or picked, plus the regarding keys the route files the event
 * by, to `POST /api/v1/child-records/sprk_event`; the server applies the mapping (fill-only, FR-02), the owner (FR-09),
 * the search-index default (FR-38/FR-40) and the autonumber (I-11).
 *
 * Proven here:
 *   - the payload sent to the route holds exactly the user-entered keys plus the regarding keys: no `sprk_eventnumber`, no
 *     owner / owning-team / impersonation value, no search-index or container value (even with a resolvable BU), and
 *     nothing a mapping profile would have filled;
 *   - the create goes to the BFF route, never `dataService.createRecord('sprk_event', ...)` (Xrm.WebApi);
 *   - the regarding parent travels as the explicit mapping source in the QUERY STRING, and the body is the same with or
 *     without it; no parent, no query string;
 *   - `applyFieldMappings` is never imported and never called, and no field-mapping profile is read;
 *   - the route's warnings reach the result; a refusal is a hard error; a service with no BFF connection creates nothing.
 */

import * as fs from 'fs';
import * as path from 'path';

import type { IDataService } from '../../../types/serviceInterfaces';
import { EventService } from '../eventService';
import type { ICreateEventFormState } from '../formTypes';
import {
  _resetNavPropCacheForTests,
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../../../services/PolymorphicResolverService';
import { applyFieldMappings } from '../../../services/FieldMappingService';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';

jest.mock('../../../services/FieldMappingService', () => ({
  applyFieldMappings: jest.fn(),
}));

const BFF_BASE = 'https://example.test';
const ROUTE = `${BFF_BASE}/api/v1/child-records/sprk_event`;
const NEW_ID = '0b0b0b0b-0000-0000-0000-000000000023';
const RT_MATTER_GUID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID_RAW = '{39CDE3E3-9D15-4B29-9A1E-1234567890AB}';
const MATTER_ID_CLEAN = '39cde3e3-9d15-4b29-9a1e-1234567890ab';
const EVENT_TYPE_ID = '77777777-7777-7777-7777-777777777777';
const ASSIGNEE_ID = '88888888-8888-8888-8888-888888888888';

/** Catalog + parent-record data the resolver reads (the user's data service reads, not the route). */
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

/** Nav-prop metadata for sprk_event (global fetch -- the discoverNavProps / applyResolverFields channel). */
function stubGlobalFetchForNavProps(): void {
  (global as unknown as { fetch: unknown }).fetch = jest.fn(async () => ({
    ok: true,
    status: 200,
    json: async () => ({
      value: [
        {
          ReferencingAttribute: 'sprk_regardingmatter',
          ReferencingEntityNavigationPropertyName: 'sprk_RegardingMatter',
          ReferencedEntity: 'sprk_matter',
        },
        {
          ReferencingAttribute: 'sprk_regardingrecordtype',
          ReferencingEntityNavigationPropertyName: 'sprk_RegardingRecordType',
          ReferencedEntity: 'sprk_recordtype_ref',
        },
        {
          ReferencingAttribute: 'sprk_eventtype',
          ReferencingEntityNavigationPropertyName: 'sprk_EventType',
          ReferencedEntity: 'sprk_eventtype_ref',
        },
        {
          ReferencingAttribute: 'sprk_assignedto',
          ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo',
          ReferencedEntity: 'contact',
        },
      ],
    }),
  })) as unknown as typeof fetch;
}

function makeForm(overrides?: Partial<ICreateEventFormState>): ICreateEventFormState {
  return {
    eventName: 'Kickoff meeting',
    eventTypeId: '',
    eventTypeName: '',
    dueDate: '',
    priority: 100000001,
    description: '',
    regardingRecordId: '',
    regardingRecordName: '',
    assignedToId: '',
    assignedToName: '',
    ...overrides,
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
function createRequests(fetchMock: jest.Mock): Array<{ url: string; method: string; body: Record<string, unknown> }> {
  return fetchMock.mock.calls
    .filter(([url]) => String(url).startsWith(ROUTE))
    .map(([url, init]) => ({
      url: String(url),
      method: String((init as RequestInit).method),
      body: JSON.parse((init as RequestInit).body as string),
    }));
}

/** The keys the regarding association writes (ADR-024: the typed lookup + the five resolver fields). */
const REGARDING_KEYS = [
  'sprk_RegardingMatter@odata.bind',
  'sprk_RegardingRecordType@odata.bind',
  'sprk_regardingrecordid',
  'sprk_regardingrecordname',
  'sprk_regardingrecordnumber',
  'sprk_regardingrecordurl',
];

describe('EventService — creates through the server write path (task 023, FR-16)', () => {
  beforeEach(() => {
    _resetNavPropCacheForTests();
    _resetRecordNumberFieldCacheForTests();
    _resetDisplayNameFieldCacheForTests();
    stubGlobalFetchForNavProps();
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
    jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });

  afterEach(() => {
    jest.restoreAllMocks();
    jest.clearAllMocks();
  });

  it('sends ONLY the keys the user entered or picked plus the regarding keys -- nothing a default, a mapping or the server owns', async () => {
    const fetchMock = makeFetch();
    const service = new EventService(makeDataService(), fetchMock, BFF_BASE);

    const result = await service.createEvent(
      makeForm({
        eventName: '  Kickoff meeting  ',
        description: ' Agenda ',
        dueDate: '2026-12-01',
        eventTypeId: `{${EVENT_TYPE_ID.toUpperCase()}}`,
        assignedToId: ASSIGNEE_ID,
        regardingRecordId: MATTER_ID_RAW,
        regardingRecordName: 'picker-provided fallback',
      }),
      'sprk_matter'
    );

    expect(result.success).toBe(true);
    expect(result.eventId).toBe(NEW_ID);
    expect(result.warnings).toEqual([]);

    const [request] = createRequests(fetchMock);
    expect(Object.keys(request.body).sort()).toEqual(
      [
        'sprk_eventname',
        'sprk_priority',
        'sprk_description',
        'sprk_duedate',
        'sprk_EventType@odata.bind',
        'sprk_AssignedTo@odata.bind',
        ...REGARDING_KEYS,
      ].sort()
    );
    expect(request.body['sprk_eventname']).toBe('Kickoff meeting');
    expect(request.body['sprk_description']).toBe('Agenda');
    expect(request.body['sprk_EventType@odata.bind']).toBe(`/sprk_eventtype_refs(${EVENT_TYPE_ID})`);
    expect(request.body['sprk_AssignedTo@odata.bind']).toBe(`/contacts(${ASSIGNEE_ID})`);
    // The regarding keys the route files the event by: the typed lookup and the pair, normalised.
    expect(request.body['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID_CLEAN})`);
    expect(request.body['sprk_regardingrecordid']).toBe(MATTER_ID_CLEAN);
    // The record URL names the record the pair names (the server refuses one that does not, task 045f).
    expect(request.body['sprk_regardingrecordurl']).toContain('etn=sprk_matter');
    expect(request.body['sprk_regardingrecordurl']).toContain(`id=${MATTER_ID_CLEAN}`);
  });

  it('a minimal event sends only its name and priority', async () => {
    const fetchMock = makeFetch();
    await new EventService(makeDataService(), fetchMock, BFF_BASE).createEvent(makeForm());

    const [request] = createRequests(fetchMock);
    expect(request.body).toEqual({ sprk_eventname: 'Kickoff meeting', sprk_priority: 100000001 });
  });

  it('sends no event number, owner, owning team, impersonation, container or search-index value, even when the BU has them', async () => {
    // A user and a BU that WOULD have fed the old cascade: nothing reads them any more.
    (window as unknown as { Xrm: unknown }).Xrm = {
      WebApi: {},
      Utility: { getGlobalContext: () => ({ userSettings: { userId: '11111111-1111-1111-1111-111111111111' } }) },
    };
    try {
      const dataService = makeDataService();
      (dataService.retrieveRecord as jest.Mock).mockImplementation(async (entityType: string) =>
        entityType === 'systemuser'
          ? { _businessunitid_value: '22222222-2222-2222-2222-222222222222' }
          : {
              sprk_containerid: 'bu-container',
              sprk_searchindexname: 'bu-search-index',
              sprk_ai_search_index: { sprk_aisearchindexid: 'bu-index-guid' },
            }
      );
      const fetchMock = makeFetch();
      await new EventService(dataService, fetchMock, BFF_BASE).createEvent(
        makeForm({ regardingRecordId: MATTER_ID_RAW }),
        'sprk_matter'
      );

      const [request] = createRequests(fetchMock);
      const keys = Object.keys(request.body);
      expect(keys.filter(k => /owner|owning|impersonat|createdby|createdonbehalf/i.test(k))).toEqual([]);
      expect(keys.filter(k => /container|searchindex|search_index|aisearchindex/i.test(k))).toEqual([]);
      expect(keys).not.toContain('sprk_eventnumber');
      const sent = JSON.stringify(request.body);
      expect(sent).not.toContain('bu-search-index');
      expect(sent).not.toContain('bu-index-guid');
      expect(sent).not.toContain('bu-container');
      // The BU is not read at all.
      expect(dataService.retrieveRecord).not.toHaveBeenCalled();
    } finally {
      delete (window as unknown as { Xrm?: unknown }).Xrm;
    }
  });

  it('creates through the BFF child-records route, never Xrm.WebApi (dataService.createRecord is not called)', async () => {
    const dataService = makeDataService();
    const fetchMock = makeFetch();

    const result = await new EventService(dataService, fetchMock, BFF_BASE).createEvent(makeForm());

    expect(result.success).toBe(true);
    expect(dataService.createRecord).not.toHaveBeenCalled();
    const requests = createRequests(fetchMock);
    expect(requests).toHaveLength(1);
    expect(requests[0].url).toBe(ROUTE); // no parent: no mapping source in the query string
    expect(requests[0].method).toBe('POST');
  });

  it('names the regarding parent as the explicit mapping source in the QUERY STRING; the body holds nothing a mapping would fill', async () => {
    const fetchMock = makeFetch();
    const result = await new EventService(makeDataService(), fetchMock, BFF_BASE).createEvent(
      makeForm({ description: 'The user typed this', regardingRecordId: MATTER_ID_RAW, regardingRecordName: 'M' }),
      'sprk_matter'
    );

    expect(result.success).toBe(true);
    const [request] = createRequests(fetchMock);
    // Braces stripped, lower-case, source entity lower-case.
    expect(request.url).toBe(`${ROUTE}?mappingSourceEntity=sprk_matter&mappingSourceId=${MATTER_ID_CLEAN}`);
    // The user's own value is in the body; no key outside the user's and the regarding keys.
    expect(request.body['sprk_description']).toBe('The user typed this');
    expect(Object.keys(request.body).filter(k => !REGARDING_KEYS.includes(k))).toEqual([
      'sprk_eventname',
      'sprk_priority',
      'sprk_description',
    ]);
    // The source travels in the query string only, never as a body key.
    expect(Object.keys(request.body).filter(k => /mappingsource/i.test(k))).toEqual([]);
  });

  it('does not map on the client: applyFieldMappings is never called and no field-mapping profile is read', async () => {
    const fetchMock = makeFetch();
    await new EventService(makeDataService(), fetchMock, BFF_BASE).createEvent(
      makeForm({ regardingRecordId: MATTER_ID_RAW }),
      'sprk_matter'
    );

    expect(applyFieldMappings).not.toHaveBeenCalled();
    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/field-mappings/'))).toEqual([]);
  });

  it('eventService.ts neither imports nor mentions applyFieldMappings, FieldMappingService or the BU cascade writes', () => {
    const source = fs.readFileSync(path.resolve(__dirname, '../eventService.ts'), 'utf8');
    expect(source).not.toMatch(/applyFieldMappings/);
    expect(source).not.toMatch(/FieldMappingService/);
    expect(source).not.toMatch(/applyUserBuDefaults|resolveUserBuDefaults/);
    expect(source).not.toMatch(/\['sprk_searchindexname'\]\s*=|aisearchindex/);
    expect(source).not.toMatch(/withBffChildWrites/);
  });

  it("surfaces the route's warnings on the result (the server dropped a mapped value) and keeps the event created", async () => {
    const fetchMock = makeFetch(() =>
      created({ id: NEW_ID, warnings: ["The value mapped to 'sprk_priorityreason' was not written."] })
    );

    const result = await new EventService(makeDataService(), fetchMock, BFF_BASE).createEvent(
      makeForm({ regardingRecordId: MATTER_ID_RAW }),
      'sprk_matter'
    );

    expect(result.success).toBe(true);
    expect(result.eventId).toBe(NEW_ID);
    expect(result.warnings).toEqual(["The value mapped to 'sprk_priorityreason' was not written."]);
  });

  it('a refusal by the route is a hard error: no event id, nothing else attempted', async () => {
    const fetchMock = jest.fn(async (url: string): Promise<Response> => {
      if (url.startsWith(ROUTE)) {
        throw apiErrorFor(404, { title: 'Not found', status: 404, detail: 'The record was not found.' });
      }
      return created({});
    });
    jest.spyOn(console, 'error').mockImplementation(() => undefined);

    const result = await new EventService(makeDataService(), fetchMock, BFF_BASE).createEvent(makeForm());

    expect(result.success).toBe(false);
    expect(result.eventId).toBeUndefined();
    expect(result.errorMessage).toBeDefined();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  describe("the route's uniform not-found", () => {
    const ROOT_SENTENCE =
      "One of the values you selected was not found, or you don't have permission to link it to this event. Ask your administrator.";
    const FILED_UNDER_DETAIL = 'A record this event is filed under was not found. The event was not saved.';
    const notFound = () =>
      jest.fn(async (): Promise<Response> => {
        throw apiErrorFor(404, {
          title: 'Not found',
          status: 404,
          detail: FILED_UNDER_DETAIL,
          reasonCode: 'child_record.not_found',
        });
      });

    beforeEach(() => {
      jest.spyOn(console, 'error').mockImplementation(() => undefined);
    });

    it('with no regarding parent is the root-create sentence, never "filed under"', async () => {
      const result = await new EventService(makeDataService(), notFound(), BFF_BASE).createEvent(makeForm());

      expect(result.success).toBe(false);
      expect(result.errorMessage).toBe(ROOT_SENTENCE);
    });

    it('with a regarding parent keeps the filed-under reading, not the root-create sentence', async () => {
      const result = await new EventService(makeDataService(), notFound(), BFF_BASE).createEvent(
        makeForm({ regardingRecordId: MATTER_ID_RAW }),
        'sprk_matter'
      );

      expect(result.success).toBe(false);
      expect(result.errorMessage).not.toBe(ROOT_SENTENCE);
      expect(result.errorMessage).not.toContain('One of the values you selected');
    });
  });

  it('a service built with no BFF connection creates nothing (fails closed, no Xrm fallback)', async () => {
    const dataService = makeDataService();
    jest.spyOn(console, 'error').mockImplementation(() => undefined);

    const result = await new EventService(dataService).createEvent(makeForm());

    expect(result.success).toBe(false);
    expect(dataService.createRecord).not.toHaveBeenCalled();
  });
});
