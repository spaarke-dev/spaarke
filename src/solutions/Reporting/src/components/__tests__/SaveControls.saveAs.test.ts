/**
 * SaveControls — the Save As dialog's error line for a failed server-side copy.
 *
 * createReport (reportingApi) turns what `@spaarke/auth`'s authenticatedFetch THROWS (ApiError with
 * ProblemDetails, or AuthError) into `{ ok: false, error }`. Before the fix the dialog dropped `error` and
 * always showed "The report copy could not be created. Please try again.", so a 403 or 409 reason never
 * reached the user. These tests run the real createReport against a thrown ApiError and pass its result
 * through the dialog's message.
 */
import { ApiError } from '@spaarke/auth';

const mockFetch = jest.fn<Promise<Response>, [string, RequestInit?]>();

jest.mock('../../services/authInit', () => ({
  authenticatedFetch: (url: string, init?: RequestInit) => mockFetch(url, init),
}));
jest.mock('../../config/runtimeConfig', () => ({
  getBffBaseUrl: () => 'https://bff.example.test',
}));

import { createReport } from '../../services/reportingApi';
import { saveAsFailureMessage } from '../SaveControls';

async function saveAsErrorFor(err: unknown): Promise<string> {
  mockFetch.mockRejectedValue(err);
  const result = await createReport({ name: 'Q3 copy', sourceReportId: 'r1' });
  if (result.ok) throw new Error('expected a failure');
  return saveAsFailureMessage(result.error);
}

describe('SaveControls Save As error line', () => {
  let error: jest.SpyInstance;

  beforeEach(() => {
    mockFetch.mockReset();
    error = jest.spyOn(console, 'error').mockImplementation(() => {});
  });

  afterEach(() => {
    error.mockRestore();
  });

  it("shows the server's 403 detail", async () => {
    const detail = 'Failed to register the report. You do not have permission to change the report catalog.';
    const text = await saveAsErrorFor(new ApiError(detail, 403, { title: 'Forbidden', status: 403, detail }));

    expect(text).toBe(`The report copy could not be created: ${detail}`);
  });

  it('shows the 409 sentence when the server sent no detail', async () => {
    const text = await saveAsErrorFor(new ApiError('HTTP 409', 409));

    expect(text).toBe(
      'The report copy could not be created: The report was changed at the same time by someone else. Refresh and try again.'
    );
  });
});
