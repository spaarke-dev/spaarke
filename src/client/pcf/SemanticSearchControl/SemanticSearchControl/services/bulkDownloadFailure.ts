/**
 * The toast for a failed POST /api/documents/bulk-download.
 *
 * The control calls the route through `@spaarke/auth`'s `authenticatedFetch`, which THROWS
 * `ApiError(status, problemDetails)` for every non-OK response instead of returning it, so the copy the
 * control wrote for `response.status === 413` / `!response.ok` has to be reached from the `catch` as
 * well. One mapping serves both.
 */
import { isApiError } from '@spaarke/auth';

export interface BulkDownloadFailureToast {
  title: string;
  body: string;
  /** Offer the Retry action (a smaller selection is the only fix for 413, so it gets none). */
  retry: boolean;
}

/** The toast for an HTTP status the bulk-download route answered with. */
export function bulkDownloadFailureForStatus(status: number): BulkDownloadFailureToast {
  if (status === 413) {
    return { title: 'Too many documents', body: 'Maximum 500 documents per bulk download.', retry: false };
  }
  return {
    title: 'Download failed',
    // Surface BFF ProblemDetails 4xx (404 "no accessible documents", 403, 401).
    body: status === 404 ? 'No accessible documents in the current selection.' : `Download failed (${status}).`,
    retry: true,
  };
}

/**
 * The toast for an error the fetch threw, or `null` when it is not an HTTP failure (network error,
 * expired sign-in) — the caller keeps its generic handling for those.
 */
export function bulkDownloadFailureForThrown(err: unknown): BulkDownloadFailureToast | null {
  return isApiError(err) ? bulkDownloadFailureForStatus(err.status) : null;
}
