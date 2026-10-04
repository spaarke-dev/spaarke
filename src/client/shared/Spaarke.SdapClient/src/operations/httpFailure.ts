/**
 * Shared HTTP-failure copy for SDAP client operations.
 *
 * Extracted so the user-facing wording lives in ONE place. It was previously private to
 * `@spaarke/ui-components`'s parallel upload client (`services/document-upload/SdapApiClient.ts`),
 * which was retired onto this package on 2026-09-03 — porting the copy first meant the retirement
 * did not silently downgrade every error message from a sentence to `HTTP 502`. That file no longer
 * exists; this is now the only definition.
 */

/**
 * Map an HTTP status to something a user can act on.
 *
 * `originalMessage` is returned unchanged for statuses with no better generic phrasing, so a
 * specific server `detail` is never replaced by a vaguer sentence.
 */
export function describeHttpFailure(status: number, originalMessage: string): string {
  switch (status) {
    case 401:
      return 'Authentication failed. Your session may have expired. Please refresh the page and try again.';
    case 403:
      return 'Access denied. You do not have permission to perform this operation. Please contact your administrator.';
    case 404:
      return 'The requested file was not found. It may have been deleted or moved.';
    case 408:
    case 504:
      return 'Request timeout. The server took too long to respond. Please try again.';
    case 429:
      return 'Too many requests. Please wait a moment and try again.';
    case 500:
      return 'Server error occurred. Please try again later. If the problem persists, contact your administrator.';
    case 502:
    case 503:
      return 'The service is temporarily unavailable. Please try again in a few minutes.';
    default:
      return originalMessage;
  }
}

/**
 * Read the most specific message a failed response offers, preferring RFC7807 `detail`/`title`.
 * Never throws — a body that cannot be read yields the status text.
 */
export async function readFailureMessage(response: Response): Promise<string> {
  try {
    const body = await response.json();
    return body?.detail || body?.title || body?.error || response.statusText;
  } catch {
    try {
      const text = await response.text();
      return text || response.statusText;
    } catch {
      return response.statusText;
    }
  }
}

/**
 * The error every operation in this package throws for a non-2xx response.
 *
 * Carries `status` so callers can branch on it instead of matching message text — the mistake that
 * made a 409 name-collision indistinguishable from a real failure in the wizard's upload path.
 */
export class SdapHttpError extends Error {
  public readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'SdapHttpError';
    this.status = status;
    // Required for `instanceof` to survive the ES5 downlevel target some consumers build with.
    Object.setPrototypeOf(this, SdapHttpError.prototype);
  }
}

/**
 * Throw the canonical error for a failed response, with user-facing copy already applied.
 */
export async function throwHttpFailure(response: Response, context: string): Promise<never> {
  const raw = await readFailureMessage(response);
  throw new SdapHttpError(response.status, `${context}: ${describeHttpFailure(response.status, raw)}`);
}

/**
 * Read an HTTP status off an error THROWN by an injected fetch, if it carries one.
 *
 * Deliberately duck-typed on a numeric `status`: this package must not import `@spaarke/auth`
 * (the whole point of injecting the fetch is to avoid that dependency), so it cannot use
 * `instanceof ApiError`. Anything without a numeric `status` — `AuthError`, an `AbortError`, a
 * network failure — returns undefined and is rethrown untouched, because those are not HTTP
 * outcomes and dressing them up as one would lose the real cause.
 */
export function httpStatusOfThrown(error: unknown): number | undefined {
  if (!error || typeof error !== 'object') return undefined;
  const status = (error as { status?: unknown }).status;
  return typeof status === 'number' ? status : undefined;
}

/**
 * Issue a request through the injected fetch and return a SUCCESSFUL response.
 *
 * 🔴 **An injected `authenticatedFetch` has TWO shapes in production, and this package used to
 * handle only one.** Both are real, today:
 *
 *   - `@spaarke/auth.authenticatedFetch` (every code page, every wizard) **THROWS** `ApiError`
 *     on any non-2xx and never returns the response.
 *   - `external-spa`'s `createAuthenticatedFetch()` **RETURNS** the raw response, non-2xx included.
 *
 * Under the first shape every `if (!response.ok)` / `response.status === 409` check in this package
 * was unreachable, so `SdapHttpError`, the {@link describeHttpFailure} copy, and — worst —
 * `UploadNameConflictError` could never be produced. The name-collision dialog depends on that
 * type: without this, repointing the wizard's upload onto this client would have silently turned
 * "a file by that name already exists — keep both or save a new version?" back into an opaque
 * failure, which is the exact regression the collision work fixed. It had no test because the
 * operations had no production callers when they were written.
 *
 * `onStatus` runs BEFORE the generic translation under **both** shapes, so an operation-specific
 * typed outcome (upload's 409) wins over the generic `SdapHttpError` either way. That ordering is
 * load-bearing — do not move the callback after the throw.
 */
export async function requestOrThrow(
  authFetch: (url: string, init?: RequestInit) => Promise<Response>,
  url: string,
  init: RequestInit,
  context: string,
  onStatus?: (status: number) => void
): Promise<Response> {
  let response: Response;

  try {
    response = await authFetch(url, init);
  } catch (error) {
    // Already one of ours (including UploadNameConflictError, which carries no `status`) — leave it.
    if (error instanceof SdapHttpError) throw error;

    const status = httpStatusOfThrown(error);
    if (status === undefined) throw error;

    onStatus?.(status);

    // The throwing shape has already consumed the body, so its message IS the RFC7807
    // detail/title. Passing it through describeHttpFailure gives the same user-facing copy the
    // returned-response path produces.
    const raw = error instanceof Error ? error.message : String(error);
    throw new SdapHttpError(status, `${context}: ${describeHttpFailure(status, raw)}`);
  }

  if (!response.ok) {
    onStatus?.(response.status);
    await throwHttpFailure(response, context);
  }

  return response;
}

/**
 * The single explanation used when an operation needs `authenticatedFetch` and did not get it.
 *
 * Stated as a hard failure rather than a silent unauthenticated request. The previous
 * `TokenProvider` shim returned `''`, and every operation then omitted the `Authorization` header
 * entirely — producing an unauthenticated call to a `RequireAuthorization` BFF (a guaranteed 401)
 * while its comment claimed "authentication handled by browser session". See FAILURE-MODES AP-12.
 */
export function requireAuthenticatedFetch<T>(fetchFn: T | undefined, operation: string): NonNullable<T> {
  if (!fetchFn) {
    throw new Error(
      `SdapApiClient.${operation} requires \`authenticatedFetch\` in the client config. ` +
        'Pass `authenticatedFetch` from `@spaarke/auth` when constructing the client (ADR-028).'
    );
  }
  return fetchFn as NonNullable<T>;
}
