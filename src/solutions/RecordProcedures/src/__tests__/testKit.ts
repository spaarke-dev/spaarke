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

/**
 * A fake `authenticatedFetch`: routes `.../{core}/{id}/procedures` and `.../rules-in-force` to the two handlers. A handler returns the
 * body, or an Error to be thrown (as `authenticatedFetch` does for a non-2xx). Every call is recorded.
 */
export function serverFake(handlers: { procedures: Handler; rules: Handler }) {
  const calls: Call[] = [];
  const fetchFn = jest.fn(async (url: string, init?: RequestInit) => {
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
  return { fetchFn: fetchFn as never, calls, mock: fetchFn };
}

export { ApiError, AuthError };
