/**
 * The failure `@spaarke/auth`'s `authenticatedFetch` THROWS for a non-2xx response.
 *
 * `authenticatedFetch` never RETURNS a non-OK Response: it throws `ApiError(message, status, problemDetails)`
 * (and `AuthError` once its 401 retries are spent). A suite that fakes the network as `{ ok: false, status }`
 * therefore drives a branch production cannot reach. Fixtures that answer "the BFF has nothing for this URL" must
 * `throw httpError(404)` instead of returning a failed Response.
 *
 * Imports the REAL class from the auth source (same file `src/__mocks__/@spaarke/auth.ts` re-exports), so it
 * survives suites that replace `@spaarke/auth` with their own factory.
 */
import { ApiError } from '../../../../../client/shared/Spaarke.Auth/src/errors';

/** RFC 7807 body, extension members allowed. */
export type ProblemBody = { title?: string; status?: number; detail?: string; [key: string]: unknown };

/** The ApiError `authenticatedFetch` throws for `status` (with this ProblemDetails body, if any). */
export function httpError(status: number, problem: ProblemBody | null = null): ApiError {
  const pd = problem && ('title' in problem || 'status' in problem) ? problem : null;
  return new ApiError(pd?.detail ?? pd?.title ?? `HTTP ${status}`, status, pd as never);
}
