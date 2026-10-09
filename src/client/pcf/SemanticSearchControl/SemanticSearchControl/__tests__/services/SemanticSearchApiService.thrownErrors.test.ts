/**
 * What the control shows when `@spaarke/auth`'s `authenticatedFetch` THROWS.
 *
 * In production that fetch never returns a non-OK Response: it throws `AuthError` once its 401
 * retries are spent and `ApiError(message, status, problemDetails)` for anything else. The service's
 * status-specific copy (`handleHttpError`) and the bulk-download 413/404 toasts were written for a
 * returned response, so every failure fell through to the generic "unexpected error" + Retry. These
 * tests throw the real classes.
 */

const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  // The real guards (structural, like production) — only the fetch is replaced.
  ...jest.requireActual('../../../../../shared/Spaarke.Auth/src/errorGuards'),
  authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
}));

import { ApiError, AuthError } from '../../../../../shared/Spaarke.Auth/src/errors';
import { SemanticSearchApiService } from '../../services/SemanticSearchApiService';
import { bulkDownloadFailureForStatus, bulkDownloadFailureForThrown } from '../../services/bulkDownloadFailure';
import type { SearchRequest } from '../../types';

const request: SearchRequest = {
  query: 'indemnification',
  scope: 'all',
  scopeId: null,
  filters: {
    documentTypes: [],
    matterTypes: [],
    dateRange: null,
    fileTypes: [],
    threshold: 0,
    searchMode: 'hybrid',
  },
  options: { limit: 25, offset: 0, includeHighlights: true },
};

const failWith = async (err: unknown) => {
  mockAuthenticatedFetch.mockReset();
  mockAuthenticatedFetch.mockRejectedValue(err);
  try {
    await new SemanticSearchApiService('https://api.example.com').search(request);
  } catch (thrown) {
    return thrown as { message: string; code: string; retryable: boolean };
  }
  throw new Error('search() resolved');
};

describe('SemanticSearchApiService.search() — errors authenticatedFetch throws', () => {
  it('400 → "Invalid search request", not retryable', async () => {
    const problem = { title: 'Bad Request', status: 400, detail: 'INDEX_NOT_ALLOWED' };
    expect(await failWith(new ApiError(problem.detail, 400, problem))).toEqual({
      message: 'Invalid search request. Please modify your query.',
      code: 'HTTP_400',
      retryable: false,
    });
  });

  it('carries a ProblemDetails `code` extension as the error code', async () => {
    const problem = { title: 'Bad Request', status: 400, code: 'INDEX_NOT_ALLOWED' };
    expect((await failWith(new ApiError('Bad Request', 400, problem))).code).toBe('INDEX_NOT_ALLOWED');
  });

  it('exhausted 401 (AuthError) → session expired, AUTH_EXPIRED', async () => {
    expect(await failWith(new AuthError('Authentication failed after all retry attempts', 'auth_exhausted'))).toEqual({
      message: 'Your session has expired. Please sign in again.',
      code: 'AUTH_EXPIRED',
      retryable: true,
    });
  });

  it('403 → no permission, not retryable', async () => {
    expect(await failWith(new ApiError('Forbidden', 403, { title: 'Forbidden', status: 403 }))).toEqual({
      message: 'You do not have permission to perform this search.',
      code: 'FORBIDDEN',
      retryable: false,
    });
  });

  it('429 → rate limited, retryable', async () => {
    const err = await failWith(new ApiError('HTTP 429', 429, null));
    expect(err).toEqual({
      message: 'Too many requests. Please wait a moment and try again.',
      code: 'RATE_LIMITED',
      retryable: true,
    });
  });

  it('503 → temporarily unavailable, retryable', async () => {
    expect(await failWith(new ApiError('HTTP 503', 503, null))).toEqual({
      message: 'The search service is temporarily unavailable. Please try again.',
      code: 'HTTP_503',
      retryable: true,
    });
  });

  it('a non-HTTP failure keeps the generic message', async () => {
    expect((await failWith(new Error('boom'))).code).toBe('UNKNOWN_ERROR');
  });
});

describe('bulk download failure toast', () => {
  it('a thrown 413 is "Too many documents" with no Retry', () => {
    expect(bulkDownloadFailureForThrown(new ApiError('HTTP 413', 413, null))).toEqual({
      title: 'Too many documents',
      body: 'Maximum 500 documents per bulk download.',
      retry: false,
    });
  });

  it('a thrown 404 is "No accessible documents" with Retry', () => {
    expect(bulkDownloadFailureForThrown(new ApiError('Not Found', 404, { title: 'Not Found', status: 404 }))).toEqual({
      title: 'Download failed',
      body: 'No accessible documents in the current selection.',
      retry: true,
    });
  });

  it('another thrown status names it', () => {
    expect(bulkDownloadFailureForThrown(new ApiError('HTTP 500', 500, null))?.body).toBe('Download failed (500).');
  });

  it('a non-HTTP failure is left to the generic handling', () => {
    expect(bulkDownloadFailureForThrown(new TypeError('Failed to fetch'))).toBeNull();
    expect(bulkDownloadFailureForThrown(new AuthError('expired', 'auth_exhausted'))).toBeNull();
  });

  it('a returned non-OK response maps the same way', () => {
    expect(bulkDownloadFailureForStatus(413).title).toBe('Too many documents');
  });
});
