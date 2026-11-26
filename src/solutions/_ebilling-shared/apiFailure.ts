/**
 * The failure shape of an e-billing page's BFF call, and the mapping from what `authenticatedFetch` threw (#2965).
 *
 * One copy for the pages whose failures need only these four facts (Invoice Files, Record Procedures). `authenticatedFetch` THROWS on
 * any non-2xx (`ApiError` carrying the parsed ProblemDetails) and on an exhausted 401 (`AuthError`); a page's API client catches that and
 * returns `{ ok: false, failure: toFailure(err) }`, so every outcome is decided in one place through the `@spaarke/auth` guards.
 *
 * The AP Export and Invoice Review pages carry their own, wider `ApiFailure` (refusal lists, a failed step, committed line ids) and their
 * own `toFailure` with different rules for `errorCode` and `detail`; they are deliberately not folded into this one (changing which text
 * a user sees is a behaviour change).
 */

import { isApiError, isAuthFailure, problemOf } from '@spaarke/auth';

/** Everything the page needs of a failed call, whatever raised it. */
export interface ApiFailure {
  /** HTTP status; 0 when no response arrived (network, or a token that could not be acquired). */
  status: number;
  /** The stable `errorCode` of ProblemDetails (ADR-019), when there is one. */
  errorCode: string | null;
  detail: string | null;
  /** True when sign-in failed (an exhausted 401 or no token). */
  signInFailed: boolean;
}

export type ApiResult<T> = { ok: true; value: T } | { ok: false; failure: ApiFailure };

function str(value: unknown): string | null {
  return typeof value === 'string' && value.length > 0 ? value : null;
}

/** Turn whatever `authenticatedFetch` threw into an {@link ApiFailure}. Exported for the tests. */
export function toFailure(err: unknown): ApiFailure {
  if (isApiError(err)) {
    const p = problemOf(err) as Record<string, unknown> | null;
    return {
      status: err.status,
      errorCode: str(p?.errorCode),
      detail: str(p?.detail) ?? str(err.message),
      signInFailed: isAuthFailure(err),
    };
  }
  return {
    status: 0,
    errorCode: null,
    detail: err instanceof Error ? err.message : null,
    signInFailed: isAuthFailure(err),
  };
}
