/**
 * reportingApi — the failure arm of every call is what the user reads.
 *
 * ExportButton / NewReportButton / DeleteReportButton / ModeToggle show `<prefix>: ${result.error}`.
 * `@spaarke/auth`'s authenticatedFetch (re-exported by services/authInit) THROWS for every non-2xx —
 * ApiError(status, ProblemDetails), or AuthError once its 401 retries are spent — so these tests throw
 * them the way the real fetch does. Before the fix every failure became `String(err)`
 * ("ApiError: HTTP 500", "AuthError: Authentication failed after all retry attempts") with no status.
 */
import { ApiError, AuthError } from '@spaarke/auth';

const mockFetch = jest.fn<Promise<Response>, [string, RequestInit?]>();

jest.mock('../authInit', () => ({
  authenticatedFetch: (url: string, init?: RequestInit) => mockFetch(url, init),
}));
jest.mock('../../config/runtimeConfig', () => ({
  getBffBaseUrl: () => 'https://bff.example.test',
}));

import { createReport, deleteReport, exportReport, fetchEmbedToken, fetchReports } from '../reportingApi';

describe('reportingApi failures', () => {
  let error: jest.SpyInstance;

  beforeEach(() => {
    mockFetch.mockReset();
    error = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    error.mockRestore();
  });

  it("export: a thrown ApiError carries the server's detail and status", async () => {
    const detail = 'Power BI could not export the report: the export limit was reached.';
    mockFetch.mockRejectedValue(new ApiError(detail, 502, { title: 'Power BI Service Error', status: 502, detail }));

    const result = await exportReport('r1', 'PDF');

    expect(result).toEqual({ ok: false, error: detail, status: 502 });
  });

  it('export: a thrown bare 500 reads "temporarily unavailable", never the class name', async () => {
    mockFetch.mockRejectedValue(new ApiError('HTTP 500', 500));

    const result = await exportReport('r1', 'PPTX');

    expect(result).toEqual({
      ok: false,
      error: 'The reporting service is temporarily unavailable. Try again in a few minutes.',
      status: 500,
    });
  });

  it('create: an expired sign-in (thrown AuthError) says so', async () => {
    mockFetch.mockRejectedValue(new AuthError('Authentication failed after all retry attempts', 'auth_exhausted'));

    const result = await createReport({ name: 'Q3', sourceReportId: 'r1' });

    expect(result).toEqual({
      ok: false,
      error: 'Your sign-in has expired. Refresh the page to sign in again.',
      status: 401,
    });
  });

  it('delete: a thrown 403 with only a title reads as a permission sentence', async () => {
    mockFetch.mockRejectedValue(new ApiError('Forbidden', 403, { title: 'Forbidden', status: 403 }));

    const result = await deleteReport('r1');

    expect(result).toEqual({ ok: false, error: 'You do not have permission to do this.', status: 403 });
  });

  it('catalog: a thrown 429 asks the user to wait', async () => {
    mockFetch.mockRejectedValue(new ApiError('HTTP 429', 429));

    const result = await fetchReports();

    expect(result).toEqual({ ok: false, error: 'Too many requests. Wait a moment and try again.', status: 429 });
  });

  it('a network failure gives a sentence, not "TypeError: Failed to fetch"', async () => {
    mockFetch.mockRejectedValue(new TypeError('Failed to fetch'));

    const result = await fetchEmbedToken('r1');

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.error).toBe('The reporting service could not be reached. Check your connection and try again.');
    expect(result.error).not.toMatch(/TypeError|ApiError|AuthError/);
  });

  it('a RETURNED non-2xx (a fetch that does not throw) maps the same way', async () => {
    const detail = 'The report was not found or you do not have access to it.';
    mockFetch.mockResolvedValue({
      ok: false,
      status: 404,
      statusText: 'Not Found',
      json: () => Promise.resolve({ title: 'Report Not Found', status: 404, detail }),
    } as unknown as Response);

    const result = await deleteReport('r1');

    expect(result).toEqual({ ok: false, error: detail, status: 404 });
  });
});
