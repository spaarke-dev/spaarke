/**
 * Compile-only fixture for `okResponse.types.test.ts` — never executed.
 *
 * Every `@ts-expect-error TSnnnn` line below MUST produce that error on the next line (the test
 * strips the directives and checks each code fires there). Every other line MUST compile clean
 * (the test compiles this file as written and expects zero diagnostics, so a directive that stops
 * firing is reported as TS2578 "Unused '@ts-expect-error' directive").
 */
import { authenticatedFetch } from '../../src/authenticatedFetch';
import { createCodePageAuthInitializer } from '../../src/createCodePageAuthInitializer';
import type { AuthenticatedFetchFn, OkResponse, ResponseFetchFn } from '../../src/types';

declare const throwingFetch: AuthenticatedFetchFn;
declare const returningFetch: ResponseFetchFn;
/** What `jest.fn<Promise<Response>, …>()` and the external SPA / Office fetches look like. */
declare const plainResponseFetch: (url: string, init?: RequestInit) => Promise<Response>;

// ---------------------------------------------------------------------------
// MUST FIRE — a failure check after a throwing fetch is dead code
// ---------------------------------------------------------------------------

export async function deadStatusCheck(): Promise<unknown> {
  const res = await throwingFetch('/api/x');
  // @ts-expect-error TS2367 — a throwing fetch never resolves with a 404
  if (res.status === 404) return null;
  return res.json();
}

export async function deadSwitchCase(): Promise<unknown> {
  const res = await throwingFetch('/api/x');
  switch (res.status) {
    // @ts-expect-error TS2678 — a throwing fetch never resolves with a 500
    case 500:
      return null;
    default:
      return res.json();
  }
}

export async function deadStatusCheckOnTheRealFunction(): Promise<unknown> {
  const res = await authenticatedFetch('/api/x');
  // @ts-expect-error TS2367 — `authenticatedFetch` throws ApiError(404) instead
  if (res.status === 404) return null;
  return res.json();
}

export async function deadStatusCheckOnTheCodePageInitializer(): Promise<unknown> {
  const { authenticatedFetch: pageFetch } = createCodePageAuthInitializer({
    clientId: 'c',
    bffBaseUrl: 'https://bff.example',
    bffApiScope: 'api://x/user_impersonation',
    logLabel: 'fixture',
  });
  const res = await pageFetch('/api/x');
  // @ts-expect-error TS2367 — the code-page wrapper throws like `authenticatedFetch`
  if (res.status === 403) return null;
  return res.json();
}

// ---------------------------------------------------------------------------
// MUST FIRE — a fetch that RETURNS failures cannot stand in for a throwing one
// ---------------------------------------------------------------------------

// @ts-expect-error TS2322 — a ResponseFetchFn may resolve `{ ok: false }`
export const returningAsThrowing: AuthenticatedFetchFn = returningFetch;

// @ts-expect-error TS2322 — a mock typed `Promise<Response>` is the returning shape
export const plainMockAsThrowing: AuthenticatedFetchFn = plainResponseFetch;

// ---------------------------------------------------------------------------
// MUST NOT FIRE
// ---------------------------------------------------------------------------

/** A returning fetch is a ResponseFetchFn. */
export const returningAsResponseFetch: ResponseFetchFn = plainResponseFetch;

/** A throwing fetch is also a ResponseFetchFn (helpers that handle both shapes accept it). */
export const throwingAsResponseFetch: ResponseFetchFn = throwingFetch;
export const realFunctionAsResponseFetch: ResponseFetchFn = authenticatedFetch;
export const realFunctionAsThrowing: AuthenticatedFetchFn = authenticatedFetch;

/** After a returning fetch, a failure check is live code. */
export async function dualShapeHelper(): Promise<unknown> {
  const res = await returningFetch('/api/x');
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json();
}

/** Success statuses still compare after a throwing fetch. */
export async function successStatusCheck(): Promise<unknown> {
  const res = await throwingFetch('/api/x');
  if (res.status === 204) return null;
  return res.json();
}

/** An OkResponse is a Response. */
export const okResponseIsAResponse = (res: OkResponse): Response => res;
