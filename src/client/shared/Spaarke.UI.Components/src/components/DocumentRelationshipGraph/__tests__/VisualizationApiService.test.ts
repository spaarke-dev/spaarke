/**
 * VisualizationApiService — shared by the DocumentRelationshipViewer code page and PCF (#3093).
 *
 * 1. What `@spaarke/auth`'s authenticatedFetch THROWS becomes VisualizationApiError, so the viewer's
 *    404 (empty graph, "no relationship data yet") and 401/403 ("You don't have permission…") handling
 *    runs (moved here from the PCF, which ran it against both former copies).
 * 2. The behaviours the two copies disagreed on, settled on the code page's (the canonical viewer):
 *    every relationship type is kept per node with "semantic" as the primary, and the request carries
 *    no `credentials: 'include'` (bearer auth only; the PCF copy never sent it).
 */
jest.mock('@spaarke/auth', () => ({
  ...jest.requireActual('@spaarke/auth'),
  authenticatedFetch: jest.fn(),
}));

import { ApiError, AuthError, authenticatedFetch } from '@spaarke/auth';
import { VisualizationApiService, VisualizationApiError, mapToGraphData } from '../VisualizationApiService';
import { formatVisualizationError } from '../useVisualizationApi';
import type { DocumentGraphResponse } from '../types';

const mockedFetch = authenticatedFetch as jest.MockedFunction<typeof authenticatedFetch>;

// No tenantId: the BFF resolves the tenant from the token's `tid` (#1453).
const params = {};

describe('VisualizationApiService.getRelatedDocuments — thrown failures', () => {
  const failWith = async (err: unknown) => {
    mockedFetch.mockReset();
    mockedFetch.mockRejectedValue(err);
    try {
      await new VisualizationApiService('https://api.example.com').getRelatedDocuments('doc-1', params);
    } catch (thrown) {
      return thrown;
    }
    throw new Error('getRelatedDocuments resolved');
  };

  it('a 404 becomes a not-found VisualizationApiError carrying the ProblemDetails detail', async () => {
    const problem = { title: 'Not Found', status: 404, detail: 'Document has no embedding yet.' };
    const err = (await failWith(new ApiError(problem.detail, 404, problem))) as VisualizationApiError;
    expect(err).toBeInstanceOf(VisualizationApiError);
    expect(err.isNotFound()).toBe(true);
    expect(err.message).toBe('Document has no embedding yet.');
  });

  it('a 403 becomes an unauthorized VisualizationApiError', async () => {
    const err = (await failWith(
      new ApiError('Forbidden', 403, { title: 'Forbidden', status: 403 })
    )) as VisualizationApiError;
    expect(err).toBeInstanceOf(VisualizationApiError);
    expect(err.isUnauthorized()).toBe(true);
  });

  it('an exhausted 401 (AuthError) becomes an unauthorized VisualizationApiError', async () => {
    const err = (await failWith(
      new AuthError('Authentication failed after all retry attempts', 'auth_exhausted')
    )) as VisualizationApiError;
    expect(err).toBeInstanceOf(VisualizationApiError);
    expect(err.statusCode).toBe(401);
  });

  it('a 500 without ProblemDetails reads "API error: 500"', async () => {
    const err = (await failWith(new ApiError('HTTP 500', 500, null))) as Error;
    expect(err).toBeInstanceOf(VisualizationApiError);
    expect(err.message).toBe('API error: 500');
  });

  it('a network failure is passed through unchanged', async () => {
    const network = new TypeError('Failed to fetch');
    expect(await failWith(network)).toBe(network);
  });

  it('viewer text: 404 → "no relationship data yet"; 403 → permission sentence', async () => {
    mockedFetch.mockReset();
    const service = new VisualizationApiService('https://api.example.com');

    mockedFetch.mockRejectedValueOnce(new ApiError('HTTP 404', 404, null));
    const notFound = await service.getRelatedDocuments('doc-1', params).catch((e: Error) => e);
    expect(formatVisualizationError(notFound as Error)).toBe(
      'Document not found or has no relationship data yet. The document may still be processing.'
    );

    mockedFetch.mockRejectedValueOnce(new ApiError('HTTP 403', 403, null));
    const forbidden = await service.getRelatedDocuments('doc-1', params).catch((e: Error) => e);
    expect(formatVisualizationError(forbidden as Error)).toBe(
      "You don't have permission to view relationships for this document."
    );
  });
});

const graphResponse: DocumentGraphResponse = {
  nodes: [
    node('src', 'source', 'Source.pdf'),
    node('doc-a', 'related', 'Contract.docx', 0.82),
    node('matter-1', 'matter', 'Acme v Beta'),
  ],
  edges: [
    edge('e1', 'src', 'doc-a', 'semantic', 0.82),
    edge('e2', 'doc-a', 'matter-1', 'same_matter', 1),
    edge('e3', 'src', 'doc-a', 'same_email', 1),
  ],
  metadata: {
    sourceDocumentId: 'src',
    tenantId: 't',
    totalResults: 1,
    threshold: 0.65,
    depth: 1,
    maxDepthReached: 1,
    nodesPerLevel: [1, 1],
    searchLatencyMs: 5,
    cacheHit: false,
  },
};

describe('mapToGraphData', () => {
  it('keeps every relationship type on a node, once each, and makes "semantic" the primary', () => {
    const { nodes } = mapToGraphData(graphResponse);
    const docA = nodes.find(n => n.id === 'doc-a')!;

    expect(docA.data.relationshipTypes?.map(r => r.type)).toEqual(['semantic', 'same_matter', 'same_email']);
    // The PCF copy kept only the highest-priority one (same_email) as the badge.
    expect(docA.data.relationshipType).toBe('semantic');
    expect(docA.data.relationshipLabel).toBe('Semantic');
    expect(docA.data.similarity).toBe(0.82);
  });

  it('maps hub and source nodes: no documentId for hubs, documentId for documents', () => {
    const { nodes } = mapToGraphData(graphResponse);
    const hub = nodes.find(n => n.id === 'matter-1')!;
    const source = nodes.find(n => n.id === 'src')!;

    expect(hub.data.documentId).toBeUndefined();
    expect(hub.data.fileType).toBe('matter');
    expect(source.data.isSource).toBe(true);
    expect(source.data.documentId).toBe('src');
    expect(source.data.fileType).toBe('pdf');
    expect(nodes.every(n => n.type === 'document')).toBe(true);
  });
});

describe('the request', () => {
  it('is a bearer GET with the filters as query parameters and no cookie credentials', async () => {
    mockedFetch.mockReset();
    mockedFetch.mockResolvedValue({ ok: true, json: async () => graphResponse } as unknown as Awaited<
      ReturnType<typeof authenticatedFetch>
    >);

    await new VisualizationApiService('https://api.example.com/').getRelatedDocuments('doc-1', {
      threshold: 0.7,
      documentTypes: ['Contract', 'Email'],
    });

    const [url, init] = mockedFetch.mock.calls[0];
    expect(url).toBe(
      'https://api.example.com/api/ai/visualization/related/doc-1?threshold=0.7&documentTypes=Contract&documentTypes=Email'
    );
    expect(init).toEqual({ method: 'GET', headers: { 'Content-Type': 'application/json' } });
    expect((init as RequestInit).credentials).toBeUndefined();
  });
});

function node(id: string, type: DocumentGraphResponse['nodes'][number]['type'], label: string, similarity?: number) {
  return {
    id,
    type,
    depth: type === 'source' ? 0 : 1,
    data: {
      label,
      documentType: type === 'matter' ? 'Matter' : 'Contract',
      similarity,
      extractedKeywords: [],
      createdOn: '2026-01-01T00:00:00Z',
      modifiedOn: '2026-01-02T00:00:00Z',
      recordUrl: '',
      fileUrl: '',
    },
  };
}

function edge(id: string, source: string, target: string, relationshipType: string, similarity: number) {
  return {
    id,
    source,
    target,
    data: { similarity, sharedKeywords: [], relationshipType, relationshipLabel: relationshipType },
  };
}
