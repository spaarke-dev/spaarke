/**
 * Unit tests for `aiSearchIndexService.ts`.
 *
 * Coverage:
 *   - URL contains entity-set name, $select clause, $filter on statecode,
 *     $orderby on displayorder then displayname.
 *   - Headers include the Prefer annotation header (REQUIRED for the
 *     FormattedValue annotation that drives §6.5 normalize step).
 *   - `credentials: 'include'` (session cookie auth — not BFF MSAL).
 *   - FormattedValue annotation maps into `sprk_targetentitytypeLabel`.
 *   - Defensive defaults: missing displayorder → 999; missing isdefault → false.
 *   - Error paths: non-OK response → []; network error → []; unexpected
 *     response shape → [].
 */

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

// Mock the Xrm global so getOrgUrl returns a deterministic URL.
const ORG_URL = 'https://test-org.crm.dynamics.com';
beforeAll(() => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).Xrm = {
    Utility: {
      getGlobalContext: () => ({
        getClientUrl: () => ORG_URL,
      }),
    },
  };
});

afterAll(() => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  delete (window as any).Xrm;
});

// Mock global fetch
const mockFetch = jest.fn<Promise<Response>, [RequestInfo | URL, RequestInit?]>();
global.fetch = mockFetch as typeof global.fetch;

// Mock console.error / console.warn to silence expected error-path logs.
let errorSpy: jest.SpyInstance;
let warnSpy: jest.SpyInstance;
beforeEach(() => {
  errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
  warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});
  jest.clearAllMocks();
});
afterEach(() => {
  errorSpy.mockRestore();
  warnSpy.mockRestore();
});

import { listActiveSearchIndexes } from '../../services/aiSearchIndexService';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function createSuccessResponse(body: unknown): Response {
  return {
    ok: true,
    status: 200,
    statusText: 'OK',
    json: jest.fn().mockResolvedValue(body),
  } as unknown as Response;
}

function createErrorResponse(status: number, statusText = 'Error'): Response {
  return {
    ok: false,
    status,
    statusText,
    json: jest.fn().mockResolvedValue({}),
  } as unknown as Response;
}

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const SAMPLE_RESPONSE = {
  value: [
    {
      sprk_aisearchindexid: '00000000-0000-0000-0000-000000000001',
      sprk_displayname: 'Development Files 2',
      sprk_searchindexname: 'spaarke-file-index',
      sprk_targetentitytype: 100000004,
      'sprk_targetentitytype@OData.Community.Display.V1.FormattedValue': 'Document',
      sprk_isdefault: true,
      sprk_displayorder: 20,
    },
    {
      sprk_aisearchindexid: '00000000-0000-0000-0000-000000000002',
      sprk_displayname: 'Matters',
      sprk_searchindexname: 'spaarke-records-index',
      sprk_targetentitytype: 100000001,
      'sprk_targetentitytype@OData.Community.Display.V1.FormattedValue': 'Matter',
      sprk_isdefault: false,
      sprk_displayorder: 30,
    },
    {
      sprk_aisearchindexid: '00000000-0000-0000-0000-000000000003',
      sprk_displayname: 'All Records',
      sprk_searchindexname: 'spaarke-records-index',
      sprk_targetentitytype: 100000000,
      'sprk_targetentitytype@OData.Community.Display.V1.FormattedValue': 'All',
      sprk_isdefault: false,
      sprk_displayorder: 80,
    },
  ],
};

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('aiSearchIndexService.listActiveSearchIndexes', () => {
  describe('request construction', () => {
    it('GETs the sprk_aisearchindexes entity set on the v9.2 OData API', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      await listActiveSearchIndexes();

      expect(mockFetch).toHaveBeenCalledTimes(1);
      const [url] = mockFetch.mock.calls[0];
      expect(String(url)).toContain(`${ORG_URL}/api/data/v9.2/sprk_aisearchindexes`);
    });

    it('includes a $select clause listing the required columns', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      await listActiveSearchIndexes();

      const [url] = mockFetch.mock.calls[0];
      const urlStr = String(url);
      expect(urlStr).toContain('$select=');
      expect(urlStr).toContain('sprk_aisearchindexid');
      expect(urlStr).toContain('sprk_displayname');
      expect(urlStr).toContain('sprk_searchindexname');
      expect(urlStr).toContain('sprk_targetentitytype');
      expect(urlStr).toContain('sprk_isdefault');
      expect(urlStr).toContain('sprk_displayorder');
    });

    it('includes a $filter on statecode eq 0', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      await listActiveSearchIndexes();

      const [url] = mockFetch.mock.calls[0];
      // URL is built as a plain string (not URL-encoded) — the OData server
      // accepts both encoded and unencoded forms. We assert the unencoded
      // form because that's what the implementation produces.
      expect(String(url)).toContain('$filter=statecode eq 0');
    });

    it('includes a $orderby on displayorder,displayname', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      await listActiveSearchIndexes();

      const [url] = mockFetch.mock.calls[0];
      const urlStr = String(url);
      expect(urlStr).toContain('$orderby=sprk_displayorder');
      expect(urlStr).toContain('sprk_displayname');
    });

    it('sends Prefer header for FormattedValue annotations (REQUIRED for §6.5)', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      await listActiveSearchIndexes();

      const [, init] = mockFetch.mock.calls[0];
      const headers = init?.headers as Record<string, string>;
      expect(headers).toBeDefined();
      expect(headers['Prefer']).toBe('odata.include-annotations="OData.Community.Display.V1.FormattedValue"');
    });

    it('sends Accept: application/json header', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      await listActiveSearchIndexes();

      const [, init] = mockFetch.mock.calls[0];
      const headers = init?.headers as Record<string, string>;
      expect(headers['Accept']).toBe('application/json');
    });

    it('sends OData-Version: 4.0 header', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      await listActiveSearchIndexes();

      const [, init] = mockFetch.mock.calls[0];
      const headers = init?.headers as Record<string, string>;
      expect(headers['OData-Version']).toBe('4.0');
    });

    it('uses credentials: include (session cookie auth, NOT BFF MSAL)', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      await listActiveSearchIndexes();

      const [, init] = mockFetch.mock.calls[0];
      expect(init?.credentials).toBe('include');
    });
  });

  describe('response mapping', () => {
    it('returns the rows in response order (server-side sorted)', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      const result = await listActiveSearchIndexes();

      expect(result).toHaveLength(3);
      expect(result[0].sprk_displayname).toBe('Development Files 2');
      expect(result[1].sprk_displayname).toBe('Matters');
      expect(result[2].sprk_displayname).toBe('All Records');
    });

    it('maps the FormattedValue annotation into sprk_targetentitytypeLabel', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      const result = await listActiveSearchIndexes();

      expect(result[0].sprk_targetentitytypeLabel).toBe('Document');
      expect(result[1].sprk_targetentitytypeLabel).toBe('Matter');
      expect(result[2].sprk_targetentitytypeLabel).toBe('All');
    });

    it('preserves the Choice integer (sprk_targetentitytype) for completeness', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      const result = await listActiveSearchIndexes();

      expect(result[0].sprk_targetentitytype).toBe(100000004);
      expect(result[1].sprk_targetentitytype).toBe(100000001);
      expect(result[2].sprk_targetentitytype).toBe(100000000);
    });

    it('maps sprk_isdefault correctly (only exact true → true)', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse(SAMPLE_RESPONSE));

      const result = await listActiveSearchIndexes();

      expect(result[0].sprk_isdefault).toBe(true);
      expect(result[1].sprk_isdefault).toBe(false);
      expect(result[2].sprk_isdefault).toBe(false);
    });

    it('defaults displayorder to 999 when missing', async () => {
      const responseWithMissing = {
        value: [
          {
            sprk_aisearchindexid: '00000000-0000-0000-0000-000000000099',
            sprk_displayname: 'No Order',
            sprk_searchindexname: 'spaarke-test',
            sprk_targetentitytype: 100000000,
            'sprk_targetentitytype@OData.Community.Display.V1.FormattedValue': 'All',
            sprk_isdefault: false,
            // sprk_displayorder absent
          },
        ],
      };
      mockFetch.mockResolvedValue(createSuccessResponse(responseWithMissing));

      const result = await listActiveSearchIndexes();

      expect(result[0].sprk_displayorder).toBe(999);
    });

    it('defaults FormattedValue label to empty string when annotation missing', async () => {
      const responseWithoutAnnotation = {
        value: [
          {
            sprk_aisearchindexid: '00000000-0000-0000-0000-000000000099',
            sprk_displayname: 'No Annotation',
            sprk_searchindexname: 'spaarke-test',
            sprk_targetentitytype: 100000000,
            // FormattedValue annotation absent
            sprk_isdefault: false,
            sprk_displayorder: 50,
          },
        ],
      };
      mockFetch.mockResolvedValue(createSuccessResponse(responseWithoutAnnotation));

      const result = await listActiveSearchIndexes();

      expect(result[0].sprk_targetentitytypeLabel).toBe('');
    });

    it('defaults sprk_isdefault to false for non-boolean values', async () => {
      const responseWithStringBool = {
        value: [
          {
            sprk_aisearchindexid: '00000000-0000-0000-0000-000000000099',
            sprk_displayname: 'Test',
            sprk_searchindexname: 'spaarke-test',
            sprk_targetentitytype: 100000000,
            'sprk_targetentitytype@OData.Community.Display.V1.FormattedValue': 'All',
            sprk_isdefault: 'true', // string, not boolean
            sprk_displayorder: 50,
          },
        ],
      };
      mockFetch.mockResolvedValue(createSuccessResponse(responseWithStringBool));

      const result = await listActiveSearchIndexes();

      expect(result[0].sprk_isdefault).toBe(false);
    });
  });

  describe('error paths', () => {
    it('returns [] when HTTP response is non-OK', async () => {
      mockFetch.mockResolvedValue(createErrorResponse(403, 'Forbidden'));

      const result = await listActiveSearchIndexes();

      expect(result).toEqual([]);
    });

    it('returns [] when fetch rejects with network error', async () => {
      mockFetch.mockRejectedValue(new TypeError('Failed to fetch'));

      const result = await listActiveSearchIndexes();

      expect(result).toEqual([]);
    });

    it('returns [] when response has no `value` array', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse({}));

      const result = await listActiveSearchIndexes();

      expect(result).toEqual([]);
    });

    it('returns [] when response.value is not an array', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse({ value: 'not-an-array' }));

      const result = await listActiveSearchIndexes();

      expect(result).toEqual([]);
    });

    it('returns [] gracefully for an empty value array', async () => {
      mockFetch.mockResolvedValue(createSuccessResponse({ value: [] }));

      const result = await listActiveSearchIndexes();

      expect(result).toEqual([]);
    });
  });
});
