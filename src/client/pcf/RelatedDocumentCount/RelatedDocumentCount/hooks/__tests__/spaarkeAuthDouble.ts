/**
 * `@spaarke/auth` for these hook tests, in its PRODUCTION shape.
 *
 * `authenticatedFetch` wraps `global.fetch` (which each test stubs) the way the real one behaves: a 2xx
 * is returned; a 401 throws `AuthError` (retries spent); any other non-2xx throws
 * `ApiError(detail ?? title ?? 'HTTP {status}', status, problemDetails)`. It never returns a non-OK
 * Response — so a test that stubs `fetch` with a 404 exercises the path production takes.
 *
 * Everything else (`buildBffApiUrl`, the error classes and guards) is the real shared source. The
 * package's compiled `dist/` is ESM, which this PCF's ts-jest does not transform; that is why the
 * module is replaced rather than required.
 *
 * Use: `jest.mock('@spaarke/auth', () => jest.requireActual('./spaarkeAuthDouble'));`
 */
import { ApiError, AuthError } from '../../../../../shared/Spaarke.Auth/src/errors';
import type { IProblemDetails } from '../../../../../shared/Spaarke.Auth/src/types';

export { ApiError, AuthError };
export { isApiError, isAuthFailure, problemOf } from '../../../../../shared/Spaarke.Auth/src/errorGuards';
export { buildBffApiUrl } from '../../../../../shared/Spaarke.Auth/src/buildBffApiUrl';

type Problem = IProblemDetails;

export async function authenticatedFetch(url: string, init?: RequestInit): Promise<Response> {
  const res = await fetch(url, init);
  if (res.ok) return res;
  if (res.status === 401) throw new AuthError('Authentication failed after all retry attempts', 'auth_exhausted');
  let problem: Problem | null = null;
  try {
    const body = (await res.json()) as unknown;
    if (body && typeof body === 'object' && ('title' in body || 'status' in body)) problem = body as Problem;
  } catch {
    problem = null;
  }
  throw new ApiError(problem?.detail ?? problem?.title ?? `HTTP ${res.status}`, res.status, problem);
}
