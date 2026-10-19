/**
 * closeSecureProject — the failure path of the injected `authenticatedFetch`.
 *
 * `@spaarke/auth`'s `authenticatedFetch` never RETURNS a non-2xx: it throws `ApiError` (message = the
 * ProblemDetails detail/title) or `AuthError` (401 after retries). The service reports either as
 * `{ success: false, errorMessage }` and never rejects.
 */
import {
  CLOSURE_CONTAINER_NOT_CLEARED_REASON_CODE,
  CLOSURE_ENUMERATION_FAILED_REASON_CODE,
  CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE,
  CLOSURE_PARTIAL_REVOCATION_REASON_CODE,
  CLOSURE_PERMISSION_AMBIGUOUS_REASON_CODE,
  CLOSURE_TOO_MANY_GRANTS_REASON_CODE,
  NON_RETRYABLE_CLOSURE_REASON_CODES,
  closeSecureProject,
} from '../closureService';
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
      .mockRejectedValue(
        apiErrorFor(409, { title: 'Conflict', status: 409, detail: 'The project is already closed.' })
      );

    const result = await closeSecureProject(REQUEST, fetchMock, 'https://bff.example');

    expect(result).toEqual({
      success: false,
      errorMessage: 'Project closure failed: The project is already closed.',
      detail: 'The project is already closed.',
    });
    expect(fetchMock).toHaveBeenCalledWith(
      'https://bff.example/api/v1/external-access/close-project',
      expect.objectContaining({ method: 'POST' })
    );
  });

  it('#2104: surfaces the ProblemDetails reasonCode and the unprefixed detail, so a caller can tell a retry from none', async () => {
    const fetchMock = jest.fn().mockRejectedValue(
      apiErrorFor(500, {
        title: 'Project closure incomplete',
        status: 500,
        detail: 'An administrator must remove those members’ permissions.',
        reasonCode: CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE,
      })
    );

    const result = await closeSecureProject(REQUEST, fetchMock, 'https://bff.example');

    expect(result.success).toBe(false);
    expect(result.reasonCode).toBe('sdap.closure.incomplete.organization_too_large');
    expect(result.detail).toBe('An administrator must remove those members’ permissions.');
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

describe('closure reason codes (#2104 round 2)', () => {
  it('mirror ProjectClosureEndpoint literally, and only the three unfixable leaves are non-retryable', () => {
    expect(CLOSURE_ENUMERATION_FAILED_REASON_CODE).toBe('sdap.closure.incomplete.enumeration_failed');
    expect(CLOSURE_PARTIAL_REVOCATION_REASON_CODE).toBe('sdap.closure.incomplete.partial_revocation');
    expect(CLOSURE_CONTAINER_NOT_CLEARED_REASON_CODE).toBe('sdap.closure.incomplete.container_not_cleared');
    expect(CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE).toBe('sdap.closure.incomplete.organization_too_large');
    expect(CLOSURE_TOO_MANY_GRANTS_REASON_CODE).toBe('sdap.closure.incomplete.too_many_grants');
    // #2104 round 3
    expect(CLOSURE_PERMISSION_AMBIGUOUS_REASON_CODE).toBe('sdap.closure.incomplete.permission_ambiguous');
    expect([...NON_RETRYABLE_CLOSURE_REASON_CODES].sort()).toEqual(
      [
        CLOSURE_ORGANIZATION_TOO_LARGE_REASON_CODE,
        CLOSURE_TOO_MANY_GRANTS_REASON_CODE,
        CLOSURE_PERMISSION_AMBIGUOUS_REASON_CODE,
      ].sort()
    );
  });
});
