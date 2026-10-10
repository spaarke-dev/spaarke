/**
 * The failure `@spaarke/auth`'s `authenticatedFetch` THROWS for a non-2xx response.
 *
 * `authenticatedFetch` never RETURNS a non-OK Response: it throws `ApiError(message, status, problemDetails)`
 * (and `AuthError` once its 401 retries are spent). A suite that fakes the network as `{ ok: false, status }`
 * drives a branch production cannot reach — fixtures that answer "the BFF has nothing for this URL" must
 * `throw httpError(404)` (or `mockRejectedValue(httpError(404))`).
 *
 * `ApiError` is imported from `@spaarke/auth` on purpose: ComposeWorkspace tests it with `err instanceof ApiError`,
 * so the thrown instance must be of whichever class the suite's `jest.mock('@spaarke/auth', ...)` exposes.
 */
import { ApiError } from '@spaarke/auth';

/** RFC 7807 body, extension members allowed. */
export type ProblemBody = { title?: string; status?: number; detail?: string; [key: string]: unknown };

/** The ApiError `authenticatedFetch` throws for `status` (with this ProblemDetails body, if any). */
export function httpError(status: number, problem: ProblemBody | null = null): InstanceType<typeof ApiError> {
  const pd = problem && ('title' in problem || 'status' in problem) ? problem : null;
  return new (ApiError as new (message: string, status: number, problem?: unknown) => InstanceType<typeof ApiError>)(
    pd?.detail ?? pd?.title ?? `HTTP ${status}`,
    status,
    pd
  );
}
