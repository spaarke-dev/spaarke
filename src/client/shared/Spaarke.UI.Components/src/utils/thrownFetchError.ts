/**
 * Reading what an injected `authenticatedFetch` THROWS.
 *
 * The fetch every Dataverse host injects is `@spaarke/auth`'s `authenticatedFetch`, and it never
 * returns a non-2xx `Response`: it throws `AuthError` (no `status`) once its 401 retries are spent
 * and `ApiError` (`status`, parsed RFC 7807 `problemDetails`) for every other failure. Code here that
 * checks `res.ok` / `res.status` after the call is dead under that fetch; the outcome it was written
 * for has to be reached from the `catch`. (The external SPA and the Outlook pane inject a fetch that
 * DOES return non-2xx responses, so callers keep their `!res.ok` branch as well.)
 *
 * These are this package's copies of `@spaarke/auth`'s `isApiError` / `problemOf` / `isAuthFailure`,
 * with the same contract (pinned by `__tests__/thrownFetchError.test.ts`, which runs both against
 * the real error classes). They are copies, not imports, because this package does not import
 * `@spaarke/auth` at runtime: the fetch is injected precisely so it need not (see
 * `communicationApi.ts`; `EmailComposer.smoketest.test.tsx` enforces it for the composer), and
 * consumer test suites map `@spaarke/auth` to stubs that would not carry these guards. Same reason
 * `@spaarke/sdap-client`'s `httpStatusOfThrown` is structural.
 *
 * Structural (by `name` + fields), never `instanceof`: a class from a second bundled copy of
 * `@spaarke/auth` fails `instanceof` silently.
 */

/** RFC 7807 problem details as `ApiError.problemDetails` carries them, extension members included. */
export interface IThrownProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [key: string]: unknown;
}

/** The fields of `@spaarke/auth`'s `ApiError` this package reads. */
export interface IThrownApiError extends Error {
  readonly status: number;
  readonly problemDetails: IThrownProblemDetails | null;
}

function hasName(err: unknown, name: string): boolean {
  return typeof err === 'object' && err !== null && (err as { name?: unknown }).name === name;
}

/** True for an `ApiError` — any status when `statuses` is empty, otherwise one of them. */
export function isApiError(err: unknown, ...statuses: number[]): err is IThrownApiError {
  if (!hasName(err, 'ApiError')) return false;
  const status = (err as { status?: unknown }).status;
  if (typeof status !== 'number') return false;
  return statuses.length === 0 || statuses.includes(status);
}

/** The parsed problem details of an `ApiError`, or `null` (not an `ApiError`, or its body was not ProblemDetails). */
export function problemOf(err: unknown): IThrownProblemDetails | null {
  if (!isApiError(err)) return null;
  const problem = (err as { problemDetails?: unknown }).problemDetails;
  return typeof problem === 'object' && problem !== null ? (problem as IThrownProblemDetails) : null;
}

/** True for `AuthError` (the exhausted-401 / no-token path) or an `ApiError` with status 401. */
export function isAuthFailure(err: unknown): boolean {
  return hasName(err, 'AuthError') || isApiError(err, 401);
}
