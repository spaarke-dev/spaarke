/**
 * Shared HTTP-failure copy for SDAP client operations.
 *
 * Extracted so the user-facing wording lives in ONE place. It was previously private to
 * `@spaarke/ui-components`'s parallel upload client (`services/document-upload/SdapApiClient.ts`),
 * which is being retired onto this package — porting the copy first means the retirement does not
 * silently downgrade every error message from a sentence to `HTTP 502`.
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
