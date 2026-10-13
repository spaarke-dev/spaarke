/**
 * The error line for a failed "Save to SharePoint" (POST /communications/{id}/archive).
 *
 * `@spaarke/auth`'s `authenticatedFetch` never returns a non-2xx Response: it throws `ApiError`
 * (status + ProblemDetails) or, once its 401 retries are spent, `AuthError`. So the framed
 * "Save to SharePoint failed (n)." the button was written to show has to be built from the thrown
 * error — otherwise the user reads the bare `ApiError` message ("HTTP 500").
 */
import { isApiError, isAuthFailure, problemOf } from '@spaarke/auth';

/** "Save to SharePoint failed (n): <server detail or title>" — or "(n)." when the server sent no reason. */
export function archiveFailureMessage(err: unknown): string {
  if (isApiError(err)) {
    const problem = problemOf(err);
    const detail = typeof problem?.detail === 'string' ? problem.detail.trim() : '';
    const title = typeof problem?.title === 'string' ? problem.title.trim() : '';
    const reason = detail || title;
    return reason
      ? `Save to SharePoint failed (${err.status}): ${reason}`
      : `Save to SharePoint failed (${err.status}).`;
  }
  if (isAuthFailure(err)) {
    return 'Save to SharePoint failed: your sign-in has expired. Refresh the page to sign in again.';
  }
  // `fetch` rejects with a TypeError when the request never got an answer (offline, DNS, CORS). Any other
  // non-HTTP error (e.g. an unreadable success body) is not a connectivity problem; neither shows the raw
  // browser message ("Failed to fetch").
  if (err instanceof TypeError) {
    return 'Save to SharePoint failed: the Spaarke server could not be reached. Check your connection.';
  }
  return 'Save to SharePoint failed.';
}
