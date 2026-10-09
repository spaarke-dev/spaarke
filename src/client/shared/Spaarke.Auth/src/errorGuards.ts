import type { IProblemDetails } from './types';
import { ApiError, AuthError } from './errors';

/**
 * Guards for the errors {@link authenticatedFetch} THROWS.
 *
 * `authenticatedFetch` never returns a non-2xx `Response`: it throws `AuthError` (code
 * `auth_exhausted`, no `status`) once its 401 retries are spent, and `ApiError` (`status`, parsed
 * RFC 7807 `problemDetails`) for every other failure. A caller that writes `if (!res.ok)` or
 * `res.status === 404` after it has written a branch that never runs; the outcome it wanted lives in
 * its `catch`. These guards are what that `catch` should test.
 *
 * Each guard accepts an instance of this module's class OR the same shape by `name` + fields.
 * `instanceof` alone fails silently when a host page and a library each bundle their own copy of
 * `@spaarke/auth` (see `ComposeWorkspace.classifySaveFailure`), and a silent fall-through to the
 * generic failure is exactly the defect these exist to prevent. The `name` check keeps other errors
 * that happen to carry a numeric `status` (`SdapHttpError`, `SendCommunicationError`) out.
 */

function hasName(err: unknown, name: string): err is Error {
  return typeof err === 'object' && err !== null && (err as { name?: unknown }).name === name;
}

/**
 * True when `err` is an {@link ApiError} — with any status when `statuses` is empty, otherwise with
 * one of them. `isApiError(err, 404)` is the thrown-error twin of `res.status === 404`.
 */
export function isApiError(err: unknown, ...statuses: number[]): err is ApiError {
  const structural =
    err instanceof ApiError || (hasName(err, 'ApiError') && typeof (err as { status?: unknown }).status === 'number');
  if (!structural) return false;
  return statuses.length === 0 || statuses.includes((err as ApiError).status);
}

/**
 * The parsed RFC 7807 problem details of an {@link ApiError} (including extension members such as
 * `reasonCode`), or `null` for anything else — including an `ApiError` whose body was not
 * ProblemDetails JSON.
 */
export function problemOf(err: unknown): IProblemDetails | null {
  if (!isApiError(err)) return null;
  const problem = (err as { problemDetails?: unknown }).problemDetails;
  return typeof problem === 'object' && problem !== null ? (problem as IProblemDetails) : null;
}

/**
 * True for a sign-in failure: an {@link AuthError} (what a 401 becomes once `authenticatedFetch`'s
 * retries are spent, or a token that could not be acquired) or an {@link ApiError} with status 401.
 */
export function isAuthFailure(err: unknown): boolean {
  return err instanceof AuthError || hasName(err, 'AuthError') || isApiError(err, 401);
}
