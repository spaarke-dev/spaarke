/**
 * Unit tests for contextService — Phase 2 multi-playbook features
 *
 * Tests two exported functions:
 *   1. resolveContextMapping() — API-driven playbook resolution with sessionStorage cache (5-min TTL)
 *   2. detectPageType() — Page type detection from Xrm or URL heuristics
 *
 * Mock strategy:
 *   - sessionStorage: jsdom provides a real sessionStorage (no mock needed)
 *   - fetch: global.fetch is assigned a jest.fn() mock per-test
 *   - Xrm: window.Xrm is set/cleared per-test for detectPageType
 *   - window.location.href: overridden via delete + reassign pattern (jsdom-compatible)
 *
 * @see services/contextService.ts
 */

import { resolveContextMapping, detectPageType } from '../../services/contextService';
import type { ContextMappingResponse } from '../../services/contextService';

// ---------------------------------------------------------------------------
// Constants (must match contextService.ts values)
// ---------------------------------------------------------------------------

const CONTEXT_MAPPING_CACHE_PREFIX = 'sprkchat-context-';
const CONTEXT_MAPPING_CACHE_TTL_MS = 5 * 60 * 1000; // 5 minutes

// ---------------------------------------------------------------------------
// Test fixtures
// ---------------------------------------------------------------------------

const TEST_API_BASE = 'https://spe-api-dev.azurewebsites.net';
const TEST_TOKEN = 'test-bearer-token-abc123';
const TEST_ENTITY_TYPE = 'sprk_matter';
const TEST_PAGE_TYPE = 'entityrecord';

const MOCK_MAPPING_RESPONSE: ContextMappingResponse = {
  defaultPlaybook: {
    id: 'pb-001',
    name: 'Matter Analysis',
    description: 'Analyze legal matters',
  },
  availablePlaybooks: [
    { id: 'pb-001', name: 'Matter Analysis', description: 'Analyze legal matters' },
    { id: 'pb-002', name: 'Document Review', description: 'Review documents' },
    { id: 'pb-003', name: 'General Chat' },
  ],
};

const EMPTY_MAPPING_RESPONSE: ContextMappingResponse = {
  defaultPlaybook: null,
  availablePlaybooks: [],
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Build the cache key matching the format used by contextService.ts */
function buildCacheKey(entityType: string, pageType: string): string {
  return `${CONTEXT_MAPPING_CACHE_PREFIX}${entityType}-${pageType}`;
}

/** Store a cache entry in sessionStorage matching the format used by contextService.ts */
function seedCache(entityType: string, pageType: string, data: ContextMappingResponse, timestamp: number): void {
  const key = buildCacheKey(entityType, pageType);
  sessionStorage.setItem(key, JSON.stringify({ data, timestamp }));
}

/** Create a mock fetch Response */
function mockFetchResponse(data: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? 'OK' : 'Internal Server Error',
    json: () => Promise.resolve(data),
    headers: new Headers(),
    redirected: false,
    type: 'basic' as ResponseType,
    url: '',
    clone: () => mockFetchResponse(data, status),
    body: null,
    bodyUsed: false,
    arrayBuffer: () => Promise.resolve(new ArrayBuffer(0)),
    blob: () => Promise.resolve(new Blob()),
    formData: () => Promise.resolve(new FormData()),
    text: () => Promise.resolve(JSON.stringify(data)),
    bytes: () => Promise.resolve(new Uint8Array()),
  } as Response;
}

/**
 * Override window.location.href in jsdom-compatible way.
 *
 * Uses a custom jest environment (jest-environment-jsdom-configurable-location.js)
 * that exposes a global __setWindowLocationHref() function using jsdom's
 * reconfigure API -- the only reliable way to change window.location in Jest 30.
 */
declare global {
  // eslint-disable-next-line no-var
  var __setWindowLocationHref: (url: string) => void;
}

function setLocationHref(href: string): void {
  globalThis.__setWindowLocationHref(href);
}

// ---------------------------------------------------------------------------
// resolveContextMapping() tests
// ---------------------------------------------------------------------------

describe('resolveContextMapping', () => {
  let mockFetch: jest.Mock;
  const originalFetch = global.fetch;

  beforeEach(() => {
    // Clear all sessionStorage entries
    sessionStorage.clear();
    // Mock global fetch by direct assignment (jsdom may not have fetch as own property)
    mockFetch = jest.fn().mockResolvedValue(mockFetchResponse(MOCK_MAPPING_RESPONSE));
    global.fetch = mockFetch;
    // Suppress console output during tests
    jest.spyOn(console, 'debug').mockImplementation();
    jest.spyOn(console, 'info').mockImplementation();
    jest.spyOn(console, 'warn').mockImplementation();
  });

  afterEach(() => {
    global.fetch = originalFetch;
    jest.restoreAllMocks();
  });

  // -----------------------------------------------------------------------
  // 1. Cache hit returns cached data without API call
  // -----------------------------------------------------------------------

  it('resolveContextMapping_CacheHitWithinTTL_ReturnsCachedDataWithoutFetch', async () => {
    // Arrange: seed cache with fresh data (timestamp = now)
    const now = Date.now();
    seedCache(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, MOCK_MAPPING_RESPONSE, now);

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: returns cached data, no fetch call
    expect(result).toEqual(MOCK_MAPPING_RESPONSE);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  it('resolveContextMapping_CacheHitJustBeforeExpiry_ReturnsCachedData', async () => {
    // Arrange: seed cache with data at 4 min 59 sec ago (just before TTL)
    const almostExpired = Date.now() - (CONTEXT_MAPPING_CACHE_TTL_MS - 1000);
    seedCache(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, MOCK_MAPPING_RESPONSE, almostExpired);

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: still a cache hit
    expect(result).toEqual(MOCK_MAPPING_RESPONSE);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  // -----------------------------------------------------------------------
  // 2. Cache miss calls API and caches result
  // -----------------------------------------------------------------------

  it('resolveContextMapping_NoCacheEntry_CallsApiAndCachesResult', async () => {
    // Arrange: sessionStorage is empty (from beforeEach)

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: returns API data
    expect(result).toEqual(MOCK_MAPPING_RESPONSE);

    // Assert: fetch was called with correct URL and auth header
    expect(mockFetch).toHaveBeenCalledTimes(1);
    const fetchUrl = mockFetch.mock.calls[0][0] as string;
    expect(fetchUrl).toContain(`${TEST_API_BASE}/api/ai/chat/context-mappings`);
    expect(fetchUrl).toContain(`entityType=${encodeURIComponent(TEST_ENTITY_TYPE)}`);
    expect(fetchUrl).toContain(`pageType=${encodeURIComponent(TEST_PAGE_TYPE)}`);

    const fetchOptions = mockFetch.mock.calls[0][1] as RequestInit;
    expect(fetchOptions.headers).toEqual({ Authorization: `Bearer ${TEST_TOKEN}` });

    // Assert: result was cached in sessionStorage
    const cacheKey = buildCacheKey(TEST_ENTITY_TYPE, TEST_PAGE_TYPE);
    const cached = sessionStorage.getItem(cacheKey);
    expect(cached).not.toBeNull();
    const parsed = JSON.parse(cached!);
    expect(parsed.data).toEqual(MOCK_MAPPING_RESPONSE);
    expect(typeof parsed.timestamp).toBe('number');
  });

  // -----------------------------------------------------------------------
  // 3. Double /api/ prefix regression — UAT E-13
  // -----------------------------------------------------------------------

  it('resolveContextMapping_ApiBaseUrlWithApiSuffix_NormalizesUrlToSingleApiPrefix', async () => {
    // Arrange: apiBaseUrl already ends with /api (as stored by SprkChat PCF control)
    const apiBaseWithApiSuffix = 'https://spe-api-dev.azurewebsites.net/api';

    // Act
    await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, apiBaseWithApiSuffix, TEST_TOKEN);

    // Assert: fetch URL uses single /api prefix (not /api/api/)
    expect(mockFetch).toHaveBeenCalledTimes(1);
    const fetchUrl = mockFetch.mock.calls[0][0] as string;
    expect(fetchUrl).not.toContain('/api/api/');
    expect(fetchUrl).toContain('https://spe-api-dev.azurewebsites.net/api/ai/chat/context-mappings');
  });

  // -----------------------------------------------------------------------
  // 4. Expired cache calls API again
  // -----------------------------------------------------------------------

  it('resolveContextMapping_ExpiredCache_CallsApiAndRefreshesCache', async () => {
    // Arrange: seed cache with data older than 5 minutes
    const expiredTimestamp = Date.now() - (CONTEXT_MAPPING_CACHE_TTL_MS + 1000);
    seedCache(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, MOCK_MAPPING_RESPONSE, expiredTimestamp);

    const updatedResponse: ContextMappingResponse = {
      defaultPlaybook: { id: 'pb-updated', name: 'Updated Playbook' },
      availablePlaybooks: [{ id: 'pb-updated', name: 'Updated Playbook' }],
    };
    mockFetch.mockResolvedValue(mockFetchResponse(updatedResponse));

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: returns fresh API data, not stale cache
    expect(result).toEqual(updatedResponse);
    expect(mockFetch).toHaveBeenCalledTimes(1);

    // Assert: cache was updated
    const cacheKey = buildCacheKey(TEST_ENTITY_TYPE, TEST_PAGE_TYPE);
    const cached = JSON.parse(sessionStorage.getItem(cacheKey)!);
    expect(cached.data).toEqual(updatedResponse);
  });

  // -----------------------------------------------------------------------
  // 4. API error returns empty defaults
  // -----------------------------------------------------------------------

  it('resolveContextMapping_ApiReturns500_ReturnsEmptyDefaults', async () => {
    // Arrange: API returns server error
    mockFetch.mockResolvedValue(mockFetchResponse(null, 500));

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: returns empty defaults
    expect(result).toEqual(EMPTY_MAPPING_RESPONSE);
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it('resolveContextMapping_ApiReturns401_ReturnsEmptyDefaults', async () => {
    // Arrange: API returns unauthorized
    mockFetch.mockResolvedValue(mockFetchResponse(null, 401));

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: returns empty defaults (graceful degradation)
    expect(result).toEqual(EMPTY_MAPPING_RESPONSE);
  });

  it('resolveContextMapping_NetworkError_ReturnsEmptyDefaults', async () => {
    // Arrange: fetch throws (network failure)
    mockFetch.mockRejectedValue(new TypeError('Failed to fetch'));

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: returns empty defaults
    expect(result).toEqual(EMPTY_MAPPING_RESPONSE);
  });

  // -----------------------------------------------------------------------
  // 5. Cache key includes entityType and pageType
  // -----------------------------------------------------------------------

  it('resolveContextMapping_DifferentEntityTypes_UseSeparateCacheKeys', async () => {
    // Arrange: seed cache for sprk_matter
    seedCache('sprk_matter', TEST_PAGE_TYPE, MOCK_MAPPING_RESPONSE, Date.now());

    const projectResponse: ContextMappingResponse = {
      defaultPlaybook: { id: 'pb-project', name: 'Project Playbook' },
      availablePlaybooks: [{ id: 'pb-project', name: 'Project Playbook' }],
    };
    mockFetch.mockResolvedValue(mockFetchResponse(projectResponse));

    // Act: request for a different entity type (sprk_project)
    const result = await resolveContextMapping('sprk_project', TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: cache miss for sprk_project, API called
    expect(result).toEqual(projectResponse);
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it('resolveContextMapping_DifferentPageTypes_UseSeparateCacheKeys', async () => {
    // Arrange: seed cache for entityrecord page type
    seedCache(TEST_ENTITY_TYPE, 'entityrecord', MOCK_MAPPING_RESPONSE, Date.now());

    const listResponse: ContextMappingResponse = {
      defaultPlaybook: { id: 'pb-list', name: 'List Playbook' },
      availablePlaybooks: [{ id: 'pb-list', name: 'List Playbook' }],
    };
    mockFetch.mockResolvedValue(mockFetchResponse(listResponse));

    // Act: request for entitylist page type
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, 'entitylist', TEST_API_BASE, TEST_TOKEN);

    // Assert: cache miss for entitylist, API called
    expect(result).toEqual(listResponse);
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });

  it('resolveContextMapping_SameEntityAndPageType_CacheHit', async () => {
    // Arrange: seed cache for exact match
    seedCache(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, MOCK_MAPPING_RESPONSE, Date.now());

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: cache hit, no API call
    expect(result).toEqual(MOCK_MAPPING_RESPONSE);
    expect(mockFetch).not.toHaveBeenCalled();
  });

  // -----------------------------------------------------------------------
  // 6. Corrupted cache entry — handled gracefully
  // -----------------------------------------------------------------------

  it('resolveContextMapping_CorruptedCacheEntry_FallsBackToApi', async () => {
    // Arrange: put invalid JSON in sessionStorage
    const cacheKey = buildCacheKey(TEST_ENTITY_TYPE, TEST_PAGE_TYPE);
    sessionStorage.setItem(cacheKey, 'not-valid-json{{{');

    // Act
    const result = await resolveContextMapping(TEST_ENTITY_TYPE, TEST_PAGE_TYPE, TEST_API_BASE, TEST_TOKEN);

    // Assert: falls back to API call
    expect(result).toEqual(MOCK_MAPPING_RESPONSE);
    expect(mockFetch).toHaveBeenCalledTimes(1);
  });
});

// ---------------------------------------------------------------------------
// detectPageType() tests
// ---------------------------------------------------------------------------

describe('detectPageType', () => {
  beforeEach(() => {
    // Clear Xrm from window
    delete (window as any).Xrm;
    jest.spyOn(console, 'debug').mockImplementation();
    jest.spyOn(console, 'info').mockImplementation();
    jest.spyOn(console, 'warn').mockImplementation();
  });

  afterEach(() => {
    // Reset URL to avoid leaking between tests
    setLocationHref('about:blank');
    jest.restoreAllMocks();
  });

  /**
   * Helper to set up a mock Xrm namespace with getPageContext returning the given pageType.
   */
  function setXrmPageType(pageType: string | undefined): void {
    (window as any).Xrm = {
      Utility: {
        getGlobalContext: () => ({
          getClientUrl: () => 'https://test.crm.dynamics.com',
          getUserId: () => 'user-123',
          getUserName: () => 'Test User',
        }),
        getPageContext: () => ({
          input: pageType ? { pageType } : undefined,
        }),
      },
    };
  }

  /**
   * Helper to set up Xrm with getPageContext returning undefined (no page context available).
   */
  function setXrmNoPageContext(): void {
    (window as any).Xrm = {
      Utility: {
        getGlobalContext: () => ({
          getClientUrl: () => 'https://test.crm.dynamics.com',
          getUserId: () => 'user-123',
          getUserName: () => 'Test User',
        }),
        getPageContext: () => undefined,
      },
    };
  }

  // -----------------------------------------------------------------------
  // 1. Returns 'entityrecord' when Xrm says entityrecord
  // -----------------------------------------------------------------------

  it('detectPageType_XrmReturnsEntityrecord_ReturnsEntityrecord', () => {
    setXrmPageType('entityrecord');
    expect(detectPageType()).toBe('entityrecord');
  });

  // -----------------------------------------------------------------------
  // 2. Returns 'entitylist' when Xrm says entitylist
  // -----------------------------------------------------------------------

  it('detectPageType_XrmReturnsEntitylist_ReturnsEntitylist', () => {
    setXrmPageType('entitylist');
    expect(detectPageType()).toBe('entitylist');
  });

  // -----------------------------------------------------------------------
  // 3. Returns other native Dataverse page types
  // -----------------------------------------------------------------------

  it('detectPageType_XrmReturnsDashboard_ReturnsDashboard', () => {
    setXrmPageType('dashboard');
    expect(detectPageType()).toBe('dashboard');
  });

  it('detectPageType_XrmReturnsWebresource_ReturnsWebresource', () => {
    setXrmPageType('webresource');
    expect(detectPageType()).toBe('webresource');
  });

  it('detectPageType_XrmReturnsCustom_ReturnsCustom', () => {
    setXrmPageType('custom');
    expect(detectPageType()).toBe('custom');
  });

  // -----------------------------------------------------------------------
  // 4. Returns 'webresource' for workspace allowlist URLs
  // -----------------------------------------------------------------------

  it('detectPageType_UrlContainsCorporateWorkspace_ReturnsWebresource', () => {
    // No Xrm page context, but URL contains a workspace name
    setXrmNoPageContext();
    setLocationHref('https://org.crm.dynamics.com/WebResources/sprk_corporateworkspace');

    expect(detectPageType()).toBe('webresource');
  });

  it('detectPageType_UrlContainsLegalWorkspace_ReturnsWebresource', () => {
    setXrmNoPageContext();
    setLocationHref('https://org.crm.dynamics.com/WebResources/sprk_legalworkspace?data=something');

    expect(detectPageType()).toBe('webresource');
  });

  it('detectPageType_UrlContainsAnalysisWorkspace_ReturnsWebresource', () => {
    setXrmNoPageContext();
    setLocationHref('https://org.crm.dynamics.com/WebResources/sprk_analysisworkspace');

    expect(detectPageType()).toBe('webresource');
  });

  // -----------------------------------------------------------------------
  // 5. Returns 'unknown' when no Xrm available
  // -----------------------------------------------------------------------

  it('detectPageType_NoXrmAvailable_ReturnsUnknown', () => {
    // No Xrm set (deleted in beforeEach), no matching URL
    setLocationHref('https://localhost:3000/sprkchatpane.html');

    expect(detectPageType()).toBe('unknown');
  });

  it('detectPageType_XrmExistsButNoPageContext_FallsBackToUrlHeuristic', () => {
    // Xrm exists but getPageContext returns undefined
    setXrmNoPageContext();
    setLocationHref('https://org.crm.dynamics.com/main.aspx?pagetype=entityrecord&etn=account');

    // URL contains 'entityrecord' so fallback heuristic should catch it
    expect(detectPageType()).toBe('entityrecord');
  });

  it('detectPageType_XrmExistsButNoPageContext_EntitylistInUrl_ReturnsEntitylist', () => {
    setXrmNoPageContext();
    setLocationHref('https://org.crm.dynamics.com/main.aspx?pagetype=entitylist&etn=sprk_matter');

    expect(detectPageType()).toBe('entitylist');
  });

  it('detectPageType_XrmExistsButNoPageContext_DashboardInUrl_ReturnsDashboard', () => {
    setXrmNoPageContext();
    setLocationHref('https://org.crm.dynamics.com/main.aspx?pagetype=dashboard&id=abc');

    expect(detectPageType()).toBe('dashboard');
  });

  // -----------------------------------------------------------------------
  // 6. Xrm page type takes priority over URL allowlist
  // -----------------------------------------------------------------------

  it('detectPageType_XrmPageTypePresent_TakesPriorityOverUrlMatch', () => {
    // Xrm says entityrecord, but URL also contains a workspace name
    setXrmPageType('entityrecord');
    setLocationHref('https://org.crm.dynamics.com/WebResources/sprk_corporateworkspace');

    // Xrm should take priority
    expect(detectPageType()).toBe('entityrecord');
  });

  // -----------------------------------------------------------------------
  // 7. Unknown Xrm page type falls through to URL detection
  // -----------------------------------------------------------------------

  it('detectPageType_XrmReturnsUnrecognizedPageType_FallsBackToUrlDetection', () => {
    // Xrm returns a page type not in the known set
    setXrmPageType('somethingNew');
    setLocationHref('https://org.crm.dynamics.com/WebResources/sprk_corporateworkspace');

    // Falls through to workspace allowlist
    expect(detectPageType()).toBe('webresource');
  });

  // -----------------------------------------------------------------------
  // 8. Error handling — never throws
  // -----------------------------------------------------------------------

  it('detectPageType_XrmThrowsError_ReturnsUnknown', () => {
    // Xrm exists but getPageContext throws
    (window as any).Xrm = {
      Utility: {
        getGlobalContext: () => ({
          getClientUrl: () => 'https://test.crm.dynamics.com',
          getUserId: () => 'user-123',
          getUserName: () => 'Test User',
        }),
        getPageContext: () => {
          throw new Error('Cross-origin access denied');
        },
      },
    };
    setLocationHref('https://localhost:3000/');

    // Should never throw, returns 'unknown'
    expect(detectPageType()).toBe('unknown');
  });
});
