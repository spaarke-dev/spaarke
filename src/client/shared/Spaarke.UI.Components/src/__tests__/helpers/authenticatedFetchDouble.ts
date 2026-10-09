/**
 * Test doubles for the fetch hosts inject as `authenticatedFetch`, in its PRODUCTION shape.
 *
 * `@spaarke/auth`'s `authenticatedFetch` (every Dataverse code page, PCF and wizard) never returns
 * a non-2xx `Response`. It throws instead:
 *   - 401 (after its retries)  -> `AuthError('Authentication failed after all retry attempts', 'auth_exhausted')`
 *   - any other non-2xx        -> `ApiError(detail ?? title ?? 'HTTP {status}', status, problemDetails)`
 *     where `problemDetails` is the JSON body when it has a `title` or `status`, else `null`.
 *
 * A mock that resolves `{ ok: false }` tests a path that fetch never takes. These doubles throw the
 * REAL classes from `@spaarke/auth`'s source, built the way `authenticatedFetch.ts` builds them, so a
 * test fails on code that only handles the returned-response shape.
 */
import { ApiError, AuthError } from '../../../../Spaarke.Auth/src/errors';

export { ApiError, AuthError };

/** RFC 7807 body, extension members allowed. */
export type ProblemBody = { title?: string; status?: number; detail?: string; [key: string]: unknown };

/** The ApiError `authenticatedFetch` throws for `status` with this ProblemDetails body. */
export function apiErrorFor(status: number, problem: ProblemBody | null = null): ApiError {
  const pd = problem && ('title' in problem || 'status' in problem) ? problem : null;
  return new ApiError(pd?.detail ?? pd?.title ?? `HTTP ${status}`, status, pd);
}

/** The AuthError `authenticatedFetch` throws once its 401 retries are spent. */
export function authExhausted(): AuthError {
  return new AuthError('Authentication failed after all retry attempts', 'auth_exhausted');
}

type Respond = (url: string, init?: RequestInit) => Response | Promise<Response>;

/**
 * Wrap a response-producing function so it behaves like `@spaarke/auth.authenticatedFetch`: a 2xx is
 * returned, anything else is converted to the error that fetch throws. Lets a suite keep writing
 * "the server answers 422 with this body" while the code under test sees what production sees.
 *
 * Test `Response` stand-ins often omit `headers`; a missing `headers` is read as a JSON body. When
 * `headers` is present the real content-type rule applies.
 */
export function throwingAuthenticatedFetch(respond: Respond): jest.Mock<Promise<Response>, [string, RequestInit?]> {
  return jest.fn(async (url: string, init?: RequestInit) => {
    const res = await respond(url, init);
    if (res.ok) return res;
    if (res.status === 401) throw authExhausted();

    let problem: ProblemBody | null = null;
    try {
      const contentType = (res as { headers?: Headers }).headers?.get?.('content-type');
      const isJson =
        contentType == null || contentType.includes('application/json') || contentType.includes('application/problem+json');
      if (isJson) {
        const body = (await res.json()) as unknown;
        if (body && typeof body === 'object') problem = body as ProblemBody;
      }
    } catch {
      problem = null;
    }
    throw apiErrorFor(res.status, problem);
  });
}
