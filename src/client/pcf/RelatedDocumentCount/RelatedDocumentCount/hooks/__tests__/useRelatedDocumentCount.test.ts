/**
 * Unit tests for useRelatedDocumentCount hook.
 *
 * Tests API fetching, loading/error states, race conditions,
 * and response parsing for the related document count hook.
 *
 * Uses @testing-library/react-hooks for React 16 compatibility (ADR-022).
 */
import { renderHook, act } from '@testing-library/react-hooks';
import { useRelatedDocumentCount } from '../useRelatedDocumentCount';

// ── Helpers ──────────────────────────────────────────────────────────────────

const API_BASE = 'https://spe-api-dev.azurewebsites.net';
const DOC_ID = 'abc-123-def-456';
const TENANT_ID = 'tenant-001';

/** Build a mock Response with JSON body. */
function mockJsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: jest.fn().mockResolvedValue(body),
    headers: new Headers(),
    redirected: false,
    statusText: 'OK',
    type: 'basic' as ResponseType,
    url: '',
    clone: jest.fn(),
    body: null,
    bodyUsed: false,
    arrayBuffer: jest.fn(),
    blob: jest.fn(),
    formData: jest.fn(),
    text: jest.fn(),
  } as unknown as Response;
}

/** Standard countOnly API response. */
function countOnlyResponse(totalResults: number) {
  return {
    nodes: [],
    edges: [],
    metadata: { totalResults },
  };
}

// ── Setup / Teardown ─────────────────────────────────────────────────────────

let fetchMock: jest.Mock;

beforeEach(() => {
  fetchMock = jest.fn();
  global.fetch = fetchMock;
});

afterEach(() => {
  jest.restoreAllMocks();
});

// ── Tests ────────────────────────────────────────────────────────────────────

describe('useRelatedDocumentCount', () => {
  describe('successful fetch', () => {
    it('returns the count from metadata.totalResults', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(7)));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      // Initially loading
      expect(result.current.isLoading).toBe(true);

      await waitForNextUpdate();

      expect(result.current.count).toBe(7);
      expect(result.current.isLoading).toBe(false);
      expect(result.current.error).toBeNull();
      expect(result.current.lastUpdated).toBeInstanceOf(Date);
    });

    it('builds the correct URL with countOnly=true', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(3)));

      const { waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();

      const calledUrl = fetchMock.mock.calls[0][0] as string;
      expect(calledUrl).toContain(`/api/ai/visualization/related/${DOC_ID}`);
      expect(calledUrl).toContain('countOnly=true');
      expect(calledUrl).toContain(`tenantId=${TENANT_ID}`);
    });

    it('omits tenantId param when undefined', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(2)));

      const { waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, undefined, API_BASE));

      await waitForNextUpdate();

      const calledUrl = fetchMock.mock.calls[0][0] as string;
      expect(calledUrl).not.toContain('tenantId');
    });

    it('strips trailing slash from apiBaseUrl', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(1)));

      const { waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, `${API_BASE}/`));

      await waitForNextUpdate();

      const calledUrl = fetchMock.mock.calls[0][0] as string;
      // Should not have double slash before /api
      expect(calledUrl).not.toContain('//api');
    });

    it('defaults count to 0 when metadata.totalResults is missing', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse({ nodes: [], edges: [], metadata: {} }));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();

      expect(result.current.count).toBe(0);
      expect(result.current.error).toBeNull();
    });
  });

  describe('loading state', () => {
    it('sets isLoading=true while fetch is in progress', async () => {
      let resolvePromise!: (value: Response) => void;
      fetchMock.mockReturnValueOnce(
        new Promise<Response>(resolve => {
          resolvePromise = resolve;
        })
      );

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      expect(result.current.isLoading).toBe(true);
      expect(result.current.count).toBe(0);

      await act(async () => {
        resolvePromise(mockJsonResponse(countOnlyResponse(5)));
      });

      expect(result.current.isLoading).toBe(false);
      expect(result.current.count).toBe(5);
    });
  });

  describe('error handling', () => {
    it('sets error on network failure', async () => {
      fetchMock.mockRejectedValueOnce(new Error('Network error'));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();

      expect(result.current.error).toBe('Unable to load related documents. Please try again.');
      expect(result.current.isLoading).toBe(false);
      expect(result.current.count).toBe(0);
    });

    it("sets auth-specific error when error message contains 'auth'", async () => {
      fetchMock.mockRejectedValueOnce(new Error('auth token expired'));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();

      expect(result.current.error).toBe('Authentication error. Please refresh the page.');
    });

    it('sets error on HTTP 500', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse({}, 500));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();

      expect(result.current.error).toBe('Failed to load related document count.');
      expect(result.current.isLoading).toBe(false);
    });

    it('sets permission error on HTTP 401', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse({}, 401));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();

      expect(result.current.error).toBe("You don't have permission to view related documents.");
    });

    it('sets permission error on HTTP 403', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse({}, 403));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();

      expect(result.current.error).toBe("You don't have permission to view related documents.");
    });
  });

  describe('404 handling', () => {
    it('returns count=0 for 404 response without error', async () => {
      fetchMock.mockResolvedValueOnce(mockJsonResponse({}, 404));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();

      expect(result.current.count).toBe(0);
      expect(result.current.error).toBeNull();
      expect(result.current.lastUpdated).toBeInstanceOf(Date);
    });
  });

  describe('missing / empty parameters', () => {
    it('returns count=0 when documentId is empty string', () => {
      const { result } = renderHook(() => useRelatedDocumentCount('', TENANT_ID, API_BASE));

      expect(result.current.count).toBe(0);
      expect(result.current.isLoading).toBe(false);
      expect(result.current.error).toBeNull();
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it('returns count=0 when documentId is whitespace', () => {
      const { result } = renderHook(() => useRelatedDocumentCount('   ', TENANT_ID, API_BASE));

      expect(result.current.count).toBe(0);
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it('returns count=0 when apiBaseUrl is undefined', () => {
      const { result } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, undefined));

      expect(result.current.count).toBe(0);
      expect(fetchMock).not.toHaveBeenCalled();
    });
  });

  describe('re-fetch on documentId change', () => {
    it('fetches again when documentId changes', async () => {
      fetchMock
        .mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(3)))
        .mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(8)));

      const { result, waitForNextUpdate, rerender } = renderHook(
        ({ docId }: { docId: string }) => useRelatedDocumentCount(docId, TENANT_ID, API_BASE),
        { initialProps: { docId: 'doc-1' } }
      );

      await waitForNextUpdate();
      expect(result.current.count).toBe(3);

      rerender({ docId: 'doc-2' });

      await waitForNextUpdate();
      expect(result.current.count).toBe(8);
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });
  });

  describe('race condition handling', () => {
    it('discards stale responses when documentId changes rapidly', async () => {
      // First fetch resolves slowly
      let resolveFirst!: (value: Response) => void;
      const firstPromise = new Promise<Response>(resolve => {
        resolveFirst = resolve;
      });

      // Second fetch resolves quickly
      fetchMock.mockReturnValueOnce(firstPromise).mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(99)));

      const { result, waitForNextUpdate, rerender } = renderHook(
        ({ docId }: { docId: string }) => useRelatedDocumentCount(docId, TENANT_ID, API_BASE),
        { initialProps: { docId: 'doc-slow' } }
      );

      // Quickly change documentId before first resolves
      rerender({ docId: 'doc-fast' });

      await waitForNextUpdate();

      // Second (fast) request should win
      expect(result.current.count).toBe(99);

      // Now resolve the first (stale) response
      await act(async () => {
        resolveFirst(mockJsonResponse(countOnlyResponse(1)));
      });

      // Count should still be 99, not 1 (stale response discarded)
      expect(result.current.count).toBe(99);
    });
  });

  describe('refetch', () => {
    it('manually triggers a re-fetch', async () => {
      fetchMock
        .mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(5)))
        .mockResolvedValueOnce(mockJsonResponse(countOnlyResponse(10)));

      const { result, waitForNextUpdate } = renderHook(() => useRelatedDocumentCount(DOC_ID, TENANT_ID, API_BASE));

      await waitForNextUpdate();
      expect(result.current.count).toBe(5);

      await act(async () => {
        result.current.refetch();
      });

      expect(result.current.count).toBe(10);
    });
  });
});
