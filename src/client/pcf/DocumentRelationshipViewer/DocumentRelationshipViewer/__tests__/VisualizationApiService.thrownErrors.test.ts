/**
 * VisualizationApiService turns what `@spaarke/auth`'s authenticatedFetch THROWS into
 * VisualizationApiError, so the viewer's 404 (empty graph, "no relationship data yet") and 401/403
 * ("You don't have permission…") handling runs. The fetch never returns a non-OK Response, so the
 * service's `!response.ok` branch alone never built that error.
 *
 * Runs against BOTH copies of the service: this PCF's and the DocumentRelationshipViewer code page's
 * ("identical logic to PCF version"). The code page has no test runner of its own.
 */
const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  ...jest.requireActual('../../../../shared/Spaarke.Auth/src/errorGuards'),
  authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
}));

import { ApiError, AuthError } from '../../../../shared/Spaarke.Auth/src/errors';
import * as pcf from '../services/VisualizationApiService';
import * as codePage from '../../../../code-pages/DocumentRelationshipViewer/src/services/VisualizationApiService';
import { formatVisualizationError } from '../hooks/useVisualizationApi';

// No tenantId: the BFF resolves the tenant from the token's `tid` and the query params no longer carry it (#1453).
const params = {};

describe.each([
  ['PCF', pcf],
  ['code page', codePage],
] as const)('%s VisualizationApiService.getRelatedDocuments — thrown failures', (_name, mod) => {
  const failWith = async (err: unknown) => {
    mockAuthenticatedFetch.mockReset();
    mockAuthenticatedFetch.mockRejectedValue(err);
    try {
      await new mod.VisualizationApiService('https://api.example.com').getRelatedDocuments('doc-1', params);
    } catch (thrown) {
      return thrown;
    }
    throw new Error('getRelatedDocuments resolved');
  };

  it('a 404 becomes a not-found VisualizationApiError carrying the ProblemDetails detail', async () => {
    const problem = { title: 'Not Found', status: 404, detail: 'Document has no embedding yet.' };
    const err = (await failWith(new ApiError(problem.detail, 404, problem))) as InstanceType<
      typeof mod.VisualizationApiError
    >;
    expect(err).toBeInstanceOf(mod.VisualizationApiError);
    expect(err.isNotFound()).toBe(true);
    expect(err.message).toBe('Document has no embedding yet.');
  });

  it('a 403 becomes an unauthorized VisualizationApiError', async () => {
    const err = (await failWith(new ApiError('Forbidden', 403, { title: 'Forbidden', status: 403 }))) as InstanceType<
      typeof mod.VisualizationApiError
    >;
    expect(err).toBeInstanceOf(mod.VisualizationApiError);
    expect(err.isUnauthorized()).toBe(true);
  });

  it('an exhausted 401 (AuthError) becomes an unauthorized VisualizationApiError', async () => {
    const err = (await failWith(
      new AuthError('Authentication failed after all retry attempts', 'auth_exhausted')
    )) as InstanceType<typeof mod.VisualizationApiError>;
    expect(err).toBeInstanceOf(mod.VisualizationApiError);
    expect(err.statusCode).toBe(401);
  });

  it('a 500 without ProblemDetails reads "API error: 500"', async () => {
    const err = (await failWith(new ApiError('HTTP 500', 500, null))) as Error;
    expect(err).toBeInstanceOf(mod.VisualizationApiError);
    expect(err.message).toBe('API error: 500');
  });

  it('a network failure is passed through unchanged', async () => {
    const network = new TypeError('Failed to fetch');
    expect(await failWith(network)).toBe(network);
  });
});

describe('PCF viewer text for thrown failures', () => {
  it('404 → "no relationship data yet"; 403 → permission sentence', async () => {
    mockAuthenticatedFetch.mockReset();
    const service = new pcf.VisualizationApiService('https://api.example.com');

    mockAuthenticatedFetch.mockRejectedValueOnce(new ApiError('HTTP 404', 404, null));
    const notFound = await service.getRelatedDocuments('doc-1', params).catch((e: Error) => e);
    expect(formatVisualizationError(notFound as Error)).toBe(
      'Document not found or has no relationship data yet. The document may still be processing.'
    );

    mockAuthenticatedFetch.mockRejectedValueOnce(new ApiError('HTTP 403', 403, null));
    const forbidden = await service.getRelatedDocuments('doc-1', params).catch((e: Error) => e);
    expect(formatVisualizationError(forbidden as Error)).toBe(
      "You don't have permission to view relationships for this document."
    );
  });
});
