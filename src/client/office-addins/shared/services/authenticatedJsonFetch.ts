import { authService } from './AuthService';

/**
 * Shared single-retry-on-401 fetch wrapper for JSON BFF calls (task 040 / FR-B0
 * realignment).
 *
 * The office-addins package cannot call `@spaarke/auth`'s own `authenticatedFetch`
 * free function directly: that function resolves its token via the module-level
 * `getAuthProvider()` singleton, which is only populated by `initAuth()`. This
 * package's `AuthService.ts` deliberately does NOT call `initAuth()` — it
 * constructs its own `SpaarkeAuthProvider` + `OfficeNaaStrategy` instance instead
 * (see that file's header comment for why `initAuth()`'s convenience wrapper
 * doesn't support a `strategy` override). `getAuthProvider()` would therefore
 * throw `AuthError('not_initialized')` if called from here.
 *
 * This helper mirrors `authenticatedFetch`'s 401-retry SHAPE (see
 * `src/client/shared/Spaarke.Auth/src/authenticatedFetch.ts`) for this package's
 * call sites instead: on a 401 response, it re-acquires a token (optionally
 * invalidating a cache first) and retries the request EXACTLY ONCE. A second 401
 * — or any other non-2xx status — is returned to the caller as a normal
 * `Response`; callers already parse RFC 7807 ProblemDetails / throw their own
 * typed errors from a non-ok response, so this helper does not surface a second
 * auth path or an unhandled error itself.
 */

export interface AuthenticatedFetchRetry {
  /** Re-acquires a token for the retry attempt. Callers pass their existing per-call `getAccessToken` getter. */
  getRetryToken: () => Promise<string>;
  /** Optional: invalidate any token cache before re-acquiring (mirrors `@spaarke/auth`'s `provider.clearCache()` step). */
  onBeforeRetry?: () => void;
}

/**
 * Performs `fetch(url, init)` with a Bearer `Authorization` header built from
 * `token`. On a 401 response, retries EXACTLY ONCE with a freshly acquired
 * token per `retry.getRetryToken()`.
 *
 * @param url Full request URL.
 * @param init Standard fetch RequestInit (method/body/other headers/signal/etc). `Authorization` is added/overwritten by this helper.
 * @param token The already-acquired access token for the first attempt.
 * @param retry Retry configuration for the single 401 retry.
 */
export async function authenticatedJsonFetch(
  url: string,
  init: RequestInit,
  token: string,
  retry: AuthenticatedFetchRetry
): Promise<Response> {
  const withAuth = (accessToken: string): RequestInit => ({
    ...init,
    headers: {
      ...init.headers,
      Authorization: `Bearer ${accessToken}`,
    },
  });

  const response = await fetch(url, withAuth(token));

  if (response.status !== 401) {
    return response;
  }

  // #1571 review F1: the token just refused is the one the auth service would hand back from its caches, so the
  // re-acquire asks for a new one (silently) whatever the caller's retry config does. The token this request sent is
  // passed on, so a late 401 for an older token never marks a newer, good one as refused (fix round 3).
  authService.clearCache(token);
  retry.onBeforeRetry?.();
  const freshToken = await retry.getRetryToken();
  if (!freshToken) {
    // No token silently (fix round 3): the retry would be refused too, so it is not sent. The caller gets the
    // original 401, and the pane its sign-in prompt.
    authService.notifySignInRequired?.();
    return response;
  }
  const retried = await fetch(url, withAuth(freshToken));
  if (retried.status === 401) {
    // #1571 review F2: the session cannot be renewed silently. One signal for every caller (Save, Find, Invoice,
    // Email…): the pane shows its sign-in prompt, whose click is the only interactive sign-in.
    authService.notifySignInRequired?.();
  }
  return retried;
}
