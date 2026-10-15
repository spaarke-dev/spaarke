/**
 * closeSecureProject — the failure path of the injected `authenticatedFetch`.
 *
 * `@spaarke/auth`'s `authenticatedFetch` never RETURNS a non-2xx: it throws `ApiError` (message = the
 * ProblemDetails detail/title) or `AuthError` (401 after retries). The service reports either as
 * `{ success: false, errorMessage }` and never rejects.
 */
import { closeSecureProject } from '../closureService';
import { apiErrorFor, authExhausted } from '../../../__tests__/helpers/authenticatedFetchDouble';

const REQUEST = { projectId: 'p-1', closeMessage: 'done' } as unknown as Parameters<typeof closeSecureProject>[0];

describe('closeSecureProject — thrown failures', () => {
  beforeEach(() => {
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });
  afterEach(() => jest.restoreAllMocks());

  it('reports the server detail when the fetch throws ApiError(409)', async () => {
    const fetchMock = jest
      .fn()
      .mockRejectedValue(apiErrorFor(409, { title: 'Conflict', status: 409, detail: 'The project is already closed.' }));

    const result = await closeSecureProject(REQUEST, fetchMock, 'https://bff.example');

    expect(result).toEqual({ success: false, errorMessage: 'Project closure failed: The project is already closed.' });
    expect(fetchMock).toHaveBeenCalledWith(
      'https://bff.example/api/v1/external-access/close-project',
      expect.objectContaining({ method: 'POST' })
    );
  });

  it('falls back to "HTTP n" when the error body was not ProblemDetails', async () => {
    const fetchMock = jest.fn().mockRejectedValue(apiErrorFor(500));
    const result = await closeSecureProject(REQUEST, fetchMock, 'https://bff.example');
    expect(result).toEqual({ success: false, errorMessage: 'Project closure failed: HTTP 500' });
  });

  it('reports an exhausted sign-in (AuthError) instead of rejecting', async () => {
    const fetchMock = jest.fn().mockRejectedValue(authExhausted());
    const result = await closeSecureProject(REQUEST, fetchMock, 'https://bff.example');
    expect(result.success).toBe(false);
    expect(result.errorMessage).toBe('Project closure failed: Authentication failed after all retry attempts');
  });
});
