/**
 * Unit tests for SemanticSearchApiService.search() request body shape.
 *
 * Focus: FR-PCF-02 — `searchIndexName` is included in the BFF request body
 * when non-empty, and OMITTED when null/undefined/empty/whitespace-only.
 *
 * NFR-09 / ADR-028 — verifies the call still routes through `authenticatedFetch`
 * from `@spaarke/auth` (no Bearer literals, no direct PublicClientApplication).
 *
 * @see services/SemanticSearchApiService.ts
 * @see projects/spaarke-multi-container-multi-index-r1/spec.md FR-PCF-02
 */

// Mock `@spaarke/auth` BEFORE importing the service so the service's
// `import { authenticatedFetch }` resolves to the mock. `jest.mock` calls
// are hoisted above imports by ts-jest, so the SUT picks up the mock at
// module-load time.
const mockAuthenticatedFetch = jest.fn();
jest.mock('@spaarke/auth', () => ({
  authenticatedFetch: (...args: unknown[]) => mockAuthenticatedFetch(...args),
}));

import { SemanticSearchApiService } from '../../services/SemanticSearchApiService';
import { SearchRequest, SearchFilters } from '../../types';

/**
 * Build a minimal valid SearchRequest. Keep fields static so each test only
 * varies the bits it cares about.
 */
const baseFilters: SearchFilters = {
  documentTypes: [],
  matterTypes: [],
  dateRange: null,
  fileTypes: [],
  threshold: 0,
  searchMode: 'hybrid',
};

const baseRequest: SearchRequest = {
  query: 'contracts about indemnification',
  scope: 'matter',
  scopeId: 'aaaa-bbbb-cccc-dddd',
  filters: baseFilters,
  options: {
    limit: 25,
    offset: 0,
    includeHighlights: true,
  },
};

/**
 * Helper — read the body that was POSTed to `authenticatedFetch` and parse it
 * back to a JSON object. Asserts a body was provided.
 */
function readSentBody(): Record<string, unknown> {
  expect(mockAuthenticatedFetch).toHaveBeenCalledTimes(1);
  const [, init] = mockAuthenticatedFetch.mock.calls[0] as [string, RequestInit];
  expect(init).toBeDefined();
  expect(init.method).toBe('POST');
  expect(typeof init.body).toBe('string');
  return JSON.parse(init.body as string) as Record<string, unknown>;
}

/**
 * Helper — give the mocked fetch a happy-path JSON 200 response so `search()`
 * can return without exercising the error paths. Body shape matches the
 * minimal SearchResponse contract `validateResponse` accepts.
 */
function mockSuccessResponse(): void {
  mockAuthenticatedFetch.mockResolvedValueOnce({
    ok: true,
    status: 200,
    json: async () => ({
      results: [],
      totalCount: 0,
      metadata: { searchTimeMs: 1, query: baseRequest.query },
    }),
  });
}

describe('SemanticSearchApiService.search() — FR-PCF-02 searchIndexName forwarding', () => {
  beforeEach(() => {
    mockAuthenticatedFetch.mockReset();
  });

  it('includes searchIndexName in the body when given a non-empty string', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest, 'spaarke-file-index');

    const body = readSentBody();
    expect(body.searchIndexName).toBe('spaarke-file-index');
  });

  it('omits searchIndexName entirely when given an empty string', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest, '');

    const body = readSentBody();
    expect(Object.prototype.hasOwnProperty.call(body, 'searchIndexName')).toBe(false);
  });

  it('omits searchIndexName entirely when given undefined (default)', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest);

    const body = readSentBody();
    expect(Object.prototype.hasOwnProperty.call(body, 'searchIndexName')).toBe(false);
  });

  it('omits searchIndexName entirely when given null', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest, null);

    const body = readSentBody();
    expect(Object.prototype.hasOwnProperty.call(body, 'searchIndexName')).toBe(false);
  });

  it('omits searchIndexName entirely when given whitespace only', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest, '   ');

    const body = readSentBody();
    expect(Object.prototype.hasOwnProperty.call(body, 'searchIndexName')).toBe(false);
  });

  it('omits searchIndexName entirely when given tab/newline whitespace', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest, '\t\n  \r');

    const body = readSentBody();
    expect(Object.prototype.hasOwnProperty.call(body, 'searchIndexName')).toBe(false);
  });

  it('trims surrounding whitespace from a non-empty searchIndexName', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest, '  spaarke-file-index  ');

    const body = readSentBody();
    expect(body.searchIndexName).toBe('spaarke-file-index');
  });

  it('uses authenticatedFetch from @spaarke/auth (no Bearer literal, no raw fetch)', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest, 'spaarke-file-index');

    // The service MUST route through the mocked authenticatedFetch — no raw fetch.
    expect(mockAuthenticatedFetch).toHaveBeenCalledTimes(1);
    const [url, init] = mockAuthenticatedFetch.mock.calls[0] as [string, RequestInit];

    // Endpoint hits the BFF /api/ai/search path.
    expect(url).toBe('https://api.example.com/api/ai/search');

    // No Authorization header is injected by the caller — that's the contract:
    // @spaarke/auth's authenticatedFetch handles tokens internally. The PCF
    // surface MUST NOT supply its own Bearer header (NFR-09).
    const headers = (init.headers ?? {}) as Record<string, string>;
    const headerKeys = Object.keys(headers).map(k => k.toLowerCase());
    expect(headerKeys).not.toContain('authorization');
  });

  it('hits POST https://{base}/api/ai/search regardless of trailing slash on base URL', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com///');

    await service.search(baseRequest, 'spaarke-file-index');

    const [url, init] = mockAuthenticatedFetch.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('https://api.example.com/api/ai/search');
    expect(init.method).toBe('POST');
  });

  it('preserves the rest of the request body shape when searchIndexName is included', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest, 'spaarke-file-index');

    const body = readSentBody();
    // Spot-check that pre-existing fields are still present alongside the
    // newly-added field — i.e. we didn't accidentally drop anything.
    expect(body.query).toBe('contracts about indemnification');
    expect(body.scope).toBe('entity');
    expect(body.entityType).toBe('matter');
    expect(body.entityId).toBe('aaaa-bbbb-cccc-dddd');
    expect(body.searchIndexName).toBe('spaarke-file-index');
    expect(body.associatedOnly).toBe(false);
  });

  it('preserves the rest of the request body shape when searchIndexName is omitted', async () => {
    mockSuccessResponse();
    const service = new SemanticSearchApiService('https://api.example.com');

    await service.search(baseRequest);

    const body = readSentBody();
    expect(body.query).toBe('contracts about indemnification');
    expect(body.scope).toBe('entity');
    expect(body.entityType).toBe('matter');
    expect(body.entityId).toBe('aaaa-bbbb-cccc-dddd');
    expect(body.associatedOnly).toBe(false);
    expect('searchIndexName' in body).toBe(false);
  });
});

describe('SemanticSearchApiService.searchUnion() — FR-PCF-02 forwarding to sub-paths', () => {
  beforeEach(() => {
    mockAuthenticatedFetch.mockReset();
  });

  it('forwards searchIndexName into BOTH parallel sub-requests on entity scope (associatedOnly=false)', async () => {
    // searchUnion fires TWO parallel `search()` calls — one for the semantic
    // path, one for the associated-only path. Both must carry the index name
    // so the semantic path routes correctly and the associated path stays
    // body-consistent (BFF ignores it on that branch).
    mockSuccessResponse();
    mockSuccessResponse();

    const service = new SemanticSearchApiService('https://api.example.com');

    await service.searchUnion(baseRequest, 'spaarke-file-index');

    expect(mockAuthenticatedFetch).toHaveBeenCalledTimes(2);
    const bodies = mockAuthenticatedFetch.mock.calls.map(([, init]) =>
      JSON.parse((init as RequestInit).body as string)
    );
    for (const body of bodies) {
      expect(body.searchIndexName).toBe('spaarke-file-index');
    }
  });

  it('omits searchIndexName from BOTH sub-requests when it is empty', async () => {
    mockSuccessResponse();
    mockSuccessResponse();

    const service = new SemanticSearchApiService('https://api.example.com');

    await service.searchUnion(baseRequest, '');

    expect(mockAuthenticatedFetch).toHaveBeenCalledTimes(2);
    const bodies = mockAuthenticatedFetch.mock.calls.map(([, init]) =>
      JSON.parse((init as RequestInit).body as string)
    );
    for (const body of bodies) {
      expect('searchIndexName' in body).toBe(false);
    }
  });

  it('forwards searchIndexName when union falls back to plain search (non-entity scope)', async () => {
    // scope='all' is not entity-scoped, so searchUnion delegates to a single
    // search() call. The index name still must propagate.
    mockSuccessResponse();

    const service = new SemanticSearchApiService('https://api.example.com');
    const allScopeRequest: SearchRequest = { ...baseRequest, scope: 'all', scopeId: null };

    await service.searchUnion(allScopeRequest, 'spaarke-file-index');

    expect(mockAuthenticatedFetch).toHaveBeenCalledTimes(1);
    const [, init] = mockAuthenticatedFetch.mock.calls[0] as [string, RequestInit];
    const body = JSON.parse(init.body as string);
    expect(body.searchIndexName).toBe('spaarke-file-index');
  });
});
