/**
 * Shared builders for the Record Procedures page's tests (ebilling-r1 task 084a).
 */

import { ApiError, AuthError } from '@spaarke/auth';

export const BASE = 'https://bff.example.com/';

/** An `ApiError` as `authenticatedFetch` throws it, with a ProblemDetails body. */
export function problem(status: number, errorCode: string | null, extra: Record<string, unknown> = {}): ApiError {
  return new ApiError(`HTTP ${status}`, status, { status, title: 'x', errorCode, ...extra } as never);
}

export function jsonResponse(body: unknown) {
  return { json: async () => body } as never;
}

export interface Query {
  core: string;
  recordId: string;
  firmId: string | null;
  date: string;
}

export type Handler = (q: Query) => unknown;

export interface Call {
  url: string;
  method: string | undefined;
  leaf: 'procedures' | 'rules-in-force' | string;
  query: Query;
}

/** One call to a route of task 089 (or task 081's read of a Procedure), as the wizards make it. `body` is the parsed JSON body (undefined for GET and DELETE). */
export interface WriteCall {
  route: 'assign' | 'exclude' | 'preview' | 'remove' | 'procedure' | 'other';
  method: string | undefined;
  url: string;
  pathname: string;
  core: string;
  recordId: string;
  overrideId: string | null;
  procedureId: string | null;
  body: unknown;
  headers: unknown;
}

export type WriteHandler = (call: WriteCall) => unknown | Promise<unknown>;

/** Which route a URL is, or null for the two list reads. */
function classify(url: string, init?: RequestInit): WriteCall | null {
  const u = new URL(url);
  const parts = u.pathname.split('/').filter(Boolean); // api, finance, ...
  const base = { method: init?.method, url, pathname: u.pathname, body: init?.body ? (JSON.parse(String(init.body)) as unknown) : undefined, headers: init?.headers };
  const none = { core: '', recordId: '', overrideId: null, procedureId: null };
  if (parts[2] === 'procedures' && parts.length === 4) return { ...base, ...none, route: 'procedure', procedureId: parts[3] };
  const [core, recordId, a, b, c] = parts.slice(2);
  const rec = { ...none, core, recordId };
  if (a === 'procedure-assignments' && parts.length === 5) return { ...base, ...rec, route: 'assign' };
  if (a === 'rule-overrides' && b === 'exclude') return { ...base, ...rec, route: 'exclude' };
  if (a === 'overrides' && c === 'removal-preview') return { ...base, ...rec, route: 'preview', overrideId: b };
  if (a === 'overrides' && parts.length === 6) return { ...base, ...rec, route: 'remove', overrideId: b };
  if (a === 'procedures' || a === 'rules-in-force') return null;
  return { ...base, ...rec, route: 'other' };
}

/**
 * A fake `authenticatedFetch`: routes `.../{core}/{id}/procedures` and `.../rules-in-force` to the two handlers. A handler returns the
 * body, or an Error to be thrown (as `authenticatedFetch` does for a non-2xx). Every call is recorded.
 */
export function serverFake(handlers: { procedures: Handler; rules: Handler; writes?: WriteHandler }) {
  const calls: Call[] = [];
  const writeCalls: WriteCall[] = [];
  const fetchFn = jest.fn(async (url: string, init?: RequestInit) => {
    const write = classify(url, init);
    if (write) {
      writeCalls.push(write);
      if (!handlers.writes) throw new Error(`Unexpected route ${write.pathname}`);
      const out = await handlers.writes(write);
      if (out instanceof Error) throw out;
      return jsonResponse(out);
    }
    const u = new URL(url);
    const parts = u.pathname.split('/').filter(Boolean); // api, finance, {core}, {id}, {leaf}
    const query: Query = { core: parts[2], recordId: parts[3], firmId: u.searchParams.get('firmId'), date: u.searchParams.get('date') ?? '' };
    const leaf = parts[4];
    calls.push({ url, method: init?.method, leaf, query });
    const handler = leaf === 'procedures' ? handlers.procedures : leaf === 'rules-in-force' ? handlers.rules : null;
    if (!handler) throw new Error(`Unexpected route ${u.pathname}`);
    const out = handler(query);
    if (out instanceof Error) throw out;
    return jsonResponse(out);
  });
  return { fetchFn: fetchFn as never, calls, writeCalls, mock: fetchFn };
}

export { ApiError, AuthError };
