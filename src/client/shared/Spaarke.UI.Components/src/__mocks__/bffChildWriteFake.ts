/**
 * A fake BFF for the child-record write routes (unified-access-control-r2 task 147 r1), for unit tests of the writers
 * that now create and re-file child records through the BFF instead of `Xrm.WebApi`.
 *
 * `bffChildWriteFetch(dataService, inner?)` returns an `authenticatedFetch` that answers
 * - `POST  /api/v1/child-records/{table}`        → `dataService.createRecord(table, body)` → 201 `{ id }`
 * - `PATCH /api/v1/child-records/{table}/{id}`   → `dataService.updateRecord(table, id, body)` → 204
 * - `PATCH /api/v1/events/{id}/filing`           → `dataService.updateRecord('sprk_event', id, body)` → 204
 * - `PATCH /api/communications/{id}/filing`      → `dataService.updateRecord('sprk_communication', id, body)` → 204
 *
 * so a suite's existing payload assertions on the mock data service keep describing exactly what reaches the server,
 * while the suite also proves the write left through the BFF (`fetch` was called; see {@link childWriteCalls}). A
 * rejection of the data service becomes a 500 ProblemDetails carrying its message — the shape the BFF answers with.
 * Any other URL goes to `inner` (the suite's own fetch stub for field mapping, uploads, …) or answers 404.
 */

import type { IDataService } from '../types/serviceInterfaces';

/** The minimal Response surface the writers read. */
export function fakeResponse(status: number, body?: unknown): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body ?? {},
    text: async () => (body === undefined ? '' : JSON.stringify(body)),
  } as unknown as Response;
}

type Fetch = (url: string, init?: RequestInit) => Promise<Response>;

const CREATE = /\/api\/v1\/child-records\/([a-z_]+)$/i;
const REFILE = /\/api\/v1\/child-records\/([a-z_]+)\/([^/?]+)$/i;
const EVENT_FILING = /\/api\/v1\/events\/([^/?]+)\/filing$/i;
const COMMUNICATION_FILING = /\/api\/communications\/([^/?]+)\/filing$/i;

export const FAKE_BFF_BASE_URL = 'https://bff.test';

/** The data-service surface the fake writes through (an `IDataService` or an `Xrm.WebApi`-shaped stub). */
export interface IFakeChildWriteTarget {
  createRecord: (entityName: string, data: Record<string, unknown>) => Promise<unknown>;
  updateRecord?: (entityName: string, id: string, data: Record<string, unknown>) => Promise<unknown>;
}

export function bffChildWriteFetch(
  dataService: IFakeChildWriteTarget | Pick<IDataService, 'createRecord' | 'updateRecord'>,
  inner?: Fetch
): jest.Mock<Promise<Response>, [string, RequestInit?]> {
  return jest.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase();
    // Only the child-record routes carry JSON; any other request (an SPE upload's file body, …) goes to `inner` as is.
    const isChildRoute = [CREATE, REFILE, EVENT_FILING, COMMUNICATION_FILING].some(r => r.test(url));
    if (!isChildRoute) {
      return inner ? inner(url, init) : fakeResponse(404, { detail: `No fake route for ${method} ${url}` });
    }
    const body = typeof init?.body === 'string' ? (JSON.parse(init.body) as Record<string, unknown>) : {};
    try {
      let m = CREATE.exec(url);
      if (m && method === 'POST') {
        const created = (await (dataService as IFakeChildWriteTarget).createRecord(m[1], body)) as unknown;
        const id = typeof created === 'string' ? created : (created as { id?: string } | undefined)?.id;
        return fakeResponse(201, { id });
      }
      m = REFILE.exec(url);
      if (m && method === 'PATCH') {
        await dataService.updateRecord?.(m[1], m[2], body);
        return fakeResponse(204);
      }
      m = EVENT_FILING.exec(url);
      if (m && method === 'PATCH') {
        await dataService.updateRecord?.('sprk_event', m[1], body);
        return fakeResponse(204);
      }
      m = COMMUNICATION_FILING.exec(url);
      if (m && method === 'PATCH') {
        await dataService.updateRecord?.('sprk_communication', m[1], body);
        return fakeResponse(204);
      }
    } catch (err) {
      return fakeResponse(500, { detail: err instanceof Error ? err.message : String(err) });
    }
    return inner ? inner(url, init) : fakeResponse(404, { detail: `No fake route for ${method} ${url}` });
  });
}

/** The child-record write calls a {@link bffChildWriteFetch} received, as `[method, path]`. */
export function childWriteCalls(fetchMock: jest.Mock): Array<[string, string]> {
  return fetchMock.mock.calls
    .map(
      ([url, init]) =>
        [String((init as RequestInit | undefined)?.method ?? 'GET').toUpperCase(), String(url)] as [string, string]
    )
    .filter(
      ([, url]) => CREATE.test(url) || REFILE.test(url) || EVENT_FILING.test(url) || COMMUNICATION_FILING.test(url)
    )
    .map(([method, url]) => [method, url.replace(FAKE_BFF_BASE_URL, '')]);
}
