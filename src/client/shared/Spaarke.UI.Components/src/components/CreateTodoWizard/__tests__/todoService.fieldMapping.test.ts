/**
 * TodoService — the server write path (field-mapping-server-write-path-r1, task 024, FR-16)
 *
 * The Create To Do wizard no longer maps fields on the client. It sends ONLY what the user entered or picked, the regarding
 * (filing) keys and the initial state to `POST /api/v1/child-records/sprk_todo`; the server applies the mapping (fill-only,
 * FR-02) and the owner (FR-09).
 *
 * Proven here:
 *   - the payload sent to the route holds exactly the user-entered keys, the filing keys and the initial state: no owner /
 *     owning-team / impersonation value, no container or search-index value, and nothing a mapping profile would fill;
 *   - the create goes to the BFF route, never `dataService.createRecord('sprk_todo', ...)` (Xrm.WebApi);
 *   - the regarding parent travels as the explicit mapping source in the QUERY STRING, and the body is the same with or
 *     without it; no parent, no query string;
 *   - no client mapping engine is imported or called, and no field-mapping profile is read;
 *   - the route's warnings reach the result; a refusal is a hard error; a service with no BFF connection creates nothing.
 */

import * as fs from 'fs';
import * as path from 'path';

import type { IDataService } from '../../../types/serviceInterfaces';
import { TodoService, _resetTodoServiceNavPropCacheForTests } from '../todoService';
import { EMPTY_TODO_FORM, type ICreateTodoFormState } from '../formTypes';
import {
  _resetNavPropCacheForTests,
  _resetRecordNumberFieldCacheForTests,
  _resetDisplayNameFieldCacheForTests,
} from '../../../services/PolymorphicResolverService';
import { apiErrorFor } from '../../../__tests__/helpers/authenticatedFetchDouble';

const BFF_BASE = 'https://example.test';
const ROUTE = `${BFF_BASE}/api/v1/child-records/sprk_todo`;
const NEW_ID = '0b0b0b0b-0000-0000-0000-000000000024';
const RT_MATTER_GUID = 'aaaaaaaa-1111-2222-3333-444444444444';
const MATTER_ID_RAW = '{39CDE3E3-9D15-4B29-9A1E-1234567890AB}';
const MATTER_ID_CLEAN = '39cde3e3-9d15-4b29-9a1e-1234567890ab';
const ASSIGNEE_ID = '88888888-8888-8888-8888-888888888888';

const MATTER = { entityType: 'sprk_matter', recordId: MATTER_ID_RAW, recordName: 'Smith v. Jones' };

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

/** Nav-prop metadata for sprk_todo (global fetch -- the discoverNavProps / applyResolverFields channel). */
function stubGlobalFetchForNavProps(): jest.Mock {
  const mock = jest.fn(async () => ({
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
          ReferencingAttribute: 'sprk_assignedto',
          ReferencingEntityNavigationPropertyName: 'sprk_AssignedTo',
          ReferencedEntity: 'contact',
        },
      ],
    }),
  }));
  (global as unknown as { fetch: unknown }).fetch = mock as unknown as typeof fetch;
  return mock;
}

function makeForm(overrides?: Partial<ICreateTodoFormState>): ICreateTodoFormState {
  return { ...EMPTY_TODO_FORM, title: 'Review discovery motion', ...overrides };
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

/** The requests made to the create route. */
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

describe('TodoService — creates through the server write path (task 024, FR-16)', () => {
  beforeEach(() => {
    _resetTodoServiceNavPropCacheForTests();
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

  it('sends ONLY the keys the user entered or picked, the filing keys and the initial state -- nothing a default, a mapping or the server owns', async () => {
    const fetchMock = makeFetch();
    const service = new TodoService(makeDataService(), fetchMock, BFF_BASE);

    const result = await service.createTodo(
      makeForm({
        title: '  Review discovery motion  ',
        notes: ' Discuss with co-counsel ',
        dueDate: '2026-12-01',
        priorityScore: 75,
        effortScore: 30,
        assignedToId: `{${ASSIGNEE_ID.toUpperCase()}}`,
        assignedToName: 'Jane Doe',
      }),
      MATTER
    );

    expect(result.success).toBe(true);
    expect(result.todoId).toBe(NEW_ID);
    expect(result.warnings).toEqual([]);

    const [request] = createRequests(fetchMock);
    expect(Object.keys(request.body).sort()).toEqual(
      [
        // user-entered / user-picked
        'sprk_name',
        'sprk_notes',
        'sprk_duedate',
        'sprk_priorityscore',
        'sprk_effortscore',
        'sprk_AssignedTo@odata.bind',
        // initial state (see task-024-notes.md)
        'statecode',
        'statuscode',
        // filing keys
        ...REGARDING_KEYS,
      ].sort()
    );
    expect(request.body['sprk_name']).toBe('Review discovery motion');
    expect(request.body['sprk_notes']).toBe('Discuss with co-counsel');
    expect(request.body['sprk_duedate']).toBe('2026-12-01');
    expect(request.body['sprk_priorityscore']).toBe(75);
    expect(request.body['sprk_effortscore']).toBe(30);
    expect(request.body['statecode']).toBe(0);
    expect(request.body['statuscode']).toBe(1);
    expect(request.body['sprk_AssignedTo@odata.bind']).toBe(`/contacts(${ASSIGNEE_ID.toUpperCase()})`.toLowerCase());
    // The regarding keys the route files the to do by: the typed lookup and the pair, normalised.
    expect(request.body['sprk_RegardingMatter@odata.bind']).toBe(`/sprk_matters(${MATTER_ID_CLEAN})`);
    expect(request.body['sprk_regardingrecordid']).toBe(MATTER_ID_CLEAN);
    // The record URL names the record the pair names (the server refuses one that does not, task 045f).
    expect(request.body['sprk_regardingrecordurl']).toContain('etn=sprk_matter');
    expect(request.body['sprk_regardingrecordurl']).toContain(`id=${MATTER_ID_CLEAN}`);
  });

  it('a minimal to do sends only its name, the two scores and the initial state', async () => {
    const fetchMock = makeFetch();
    await new TodoService(makeDataService(), fetchMock, BFF_BASE).createTodo(makeForm());

    const [request] = createRequests(fetchMock);
    expect(request.body).toEqual({
      sprk_name: 'Review discovery motion',
      sprk_priorityscore: EMPTY_TODO_FORM.priorityScore,
      sprk_effortscore: EMPTY_TODO_FORM.effortScore,
      statecode: 0,
      statuscode: 1,
    });
  });

  it('sends no owner, owning team, impersonation, container or search-index key', async () => {
    const fetchMock = makeFetch();
    await new TodoService(makeDataService(), fetchMock, BFF_BASE).createTodo(
      makeForm({ assignedToId: ASSIGNEE_ID }),
      MATTER
    );

    const [request] = createRequests(fetchMock);
    const keys = Object.keys(request.body);
    expect(keys.filter(k => /owner|owning|impersonat|createdby|createdonbehalf/i.test(k))).toEqual([]);
    expect(keys.filter(k => /container|searchindex|search_index|aisearchindex/i.test(k))).toEqual([]);
  });

  it('creates through the BFF child-records route, never Xrm.WebApi (dataService.createRecord is not called)', async () => {
    const dataService = makeDataService();
    const fetchMock = makeFetch();

    const result = await new TodoService(dataService, fetchMock, BFF_BASE).createTodo(makeForm());

    expect(result.success).toBe(true);
    expect(dataService.createRecord).not.toHaveBeenCalled();
    const requests = createRequests(fetchMock);
    expect(requests).toHaveLength(1);
    expect(requests[0].url).toBe(ROUTE); // no parent: no mapping source in the query string
    expect(requests[0].method).toBe('POST');
  });

  it('accepts an empty base URL (hosts whose fetch resolves relative paths) and posts to the relative route', async () => {
    const fetchMock = jest.fn(async () => created());

    const result = await new TodoService(makeDataService(), fetchMock, '').createTodo(makeForm());

    expect(result.success).toBe(true);
    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/child-records/sprk_todo');
  });

  it('names the regarding parent as the explicit mapping source in the QUERY STRING; the body holds nothing a mapping would fill', async () => {
    const fetchMock = makeFetch();
    const result = await new TodoService(makeDataService(), fetchMock, BFF_BASE).createTodo(
      makeForm({ notes: 'The user typed this' }),
      MATTER
    );

    expect(result.success).toBe(true);
    const [request] = createRequests(fetchMock);
    // Braces stripped, lower-case, source entity lower-case.
    expect(request.url).toBe(`${ROUTE}?mappingSourceEntity=sprk_matter&mappingSourceId=${MATTER_ID_CLEAN}`);
    // The user's own value is in the body; no key outside the user's, the initial state and the regarding keys.
    expect(request.body['sprk_notes']).toBe('The user typed this');
    expect(
      Object.keys(request.body)
        .filter(k => !REGARDING_KEYS.includes(k))
        .sort()
    ).toEqual(['sprk_effortscore', 'sprk_name', 'sprk_notes', 'sprk_priorityscore', 'statecode', 'statuscode']);
    // The source travels in the query string only, never as a body key.
    expect(Object.keys(request.body).filter(k => /mappingsource/i.test(k))).toEqual([]);
  });

  it('names no mapping source when the to do has no regarding parent (skipped or null)', async () => {
    for (const regarding of [undefined, null]) {
      const fetchMock = makeFetch();
      await new TodoService(makeDataService(), fetchMock, BFF_BASE).createTodo(makeForm(), regarding);
      const [request] = createRequests(fetchMock);
      expect(request.url).toBe(ROUTE);
      expect(Object.keys(request.body).filter(k => /regarding/i.test(k))).toEqual([]);
    }
  });

  it('does not map on the client: no field-mapping profile is read', async () => {
    const fetchMock = makeFetch();
    await new TodoService(makeDataService(), fetchMock, BFF_BASE).createTodo(makeForm(), MATTER);

    expect(fetchMock.mock.calls.filter(([url]) => String(url).includes('/field-mappings/'))).toEqual([]);
  });

  it('todoService.ts neither imports nor mentions FieldMappingService, and does not use withBffChildWrites', () => {
    const source = fs.readFileSync(path.resolve(__dirname, '../todoService.ts'), 'utf8');
    expect(source).not.toMatch(/FieldMappingService/);
    expect(source).not.toMatch(/withBffChildWrites/);
  });

  it("surfaces the route's warnings on the result (the server dropped a mapped value) and keeps the to do created", async () => {
    const fetchMock = makeFetch(() =>
      created({ id: NEW_ID, warnings: ["The value mapped to 'sprk_description' was not written."] })
    );

    const result = await new TodoService(makeDataService(), fetchMock, BFF_BASE).createTodo(makeForm(), MATTER);

    expect(result.success).toBe(true);
    expect(result.todoId).toBe(NEW_ID);
    expect(result.warnings).toEqual(["The value mapped to 'sprk_description' was not written."]);
  });

  it('a refusal by the route is a hard error: no to do id, nothing else attempted', async () => {
    const fetchMock = jest.fn(async (url: string): Promise<Response> => {
      if (url.startsWith(ROUTE)) {
        throw apiErrorFor(404, { title: 'Not found', status: 404, detail: 'The record was not found.' });
      }
      return created({});
    });
    jest.spyOn(console, 'error').mockImplementation(() => undefined);

    const result = await new TodoService(makeDataService(), fetchMock, BFF_BASE).createTodo(makeForm());

    expect(result.success).toBe(false);
    expect(result.todoId).toBeUndefined();
    expect(result.errorMessage).toBeDefined();
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  describe("the route's uniform not-found", () => {
    const ROOT_SENTENCE =
      "One of the values you selected was not found, or you don't have permission to link it to this to do. Ask your administrator.";
    const FILED_UNDER_DETAIL = 'A record this to do is filed under was not found. The to do was not saved.';
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
      const result = await new TodoService(makeDataService(), notFound(), BFF_BASE).createTodo(makeForm());

      expect(result.success).toBe(false);
      expect(result.errorMessage).toBe(ROOT_SENTENCE);
    });

    it('with a regarding parent keeps the filed-under reading, not the root-create sentence', async () => {
      const result = await new TodoService(makeDataService(), notFound(), BFF_BASE).createTodo(makeForm(), MATTER);

      expect(result.success).toBe(false);
      expect(result.errorMessage).not.toBe(ROOT_SENTENCE);
      expect(result.errorMessage).not.toContain('One of the values you selected');
    });
  });

  it('a service built with no BFF connection creates nothing (fails closed, no Xrm fallback)', async () => {
    const dataService = makeDataService();
    jest.spyOn(console, 'error').mockImplementation(() => undefined);

    const result = await new TodoService(dataService).createTodo(makeForm());

    expect(result.success).toBe(false);
    expect(result.errorMessage).toMatch(/not connected to the Spaarke service/i);
    expect(dataService.createRecord).not.toHaveBeenCalled();
  });

  it('a service built with a BFF-routed data service but no fetch of its own still creates nothing', async () => {
    // The old path re-used a routed data service; the create now needs the fetch, and never falls back to Xrm.
    const dataService = makeDataService();
    jest.spyOn(console, 'error').mockImplementation(() => undefined);

    const result = await new TodoService(dataService, undefined, BFF_BASE).createTodo(makeForm());

    expect(result.success).toBe(false);
    expect(dataService.createRecord).not.toHaveBeenCalled();
  });
});
