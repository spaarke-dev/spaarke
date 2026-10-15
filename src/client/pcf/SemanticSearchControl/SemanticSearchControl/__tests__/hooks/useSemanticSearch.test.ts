/**
 * Unit tests for useSemanticSearch hook
 *
 * @see useSemanticSearch.ts for implementation
 */
import { renderHook, act } from '@testing-library/react-hooks';
import { useSemanticSearch } from '../../hooks/useSemanticSearch';
import { SemanticSearchApiService } from '../../services';
import { SearchFilters, SearchResponse } from '../../types';

// Mock the API service
const mockSearch = jest.fn();
const mockApiService = {
  search: mockSearch,
} as unknown as SemanticSearchApiService;

// Helper to create mock response
const createMockResponse = (results: number, total: number, startIndex = 0): SearchResponse => ({
  results: Array.from({ length: results }, (_, k) => k + startIndex).map(i => ({
    documentId: `doc-${i}`,
    name: `Document ${i}`,
    fileType: 'pdf',
    documentType: 'contract',
    matterName: null,
    matterId: null,
    createdAt: '2026-01-01',
    combinedScore: 0.95 - i * 0.01,
    highlights: ['test highlight'],
    fileUrl: `https://example.com/doc-${i}`,
    recordUrl: `https://crm.dynamics.com/doc-${i}`,
    createdBy: null,
    modifiedAt: null,
    modifiedBy: null,
    summary: null,
    tldr: null,
  })),
  totalCount: total,
  metadata: {
    searchTimeMs: 100,
    query: 'test query',
  },
});

// Default empty filters
const emptyFilters: SearchFilters = {
  documentTypes: [],
  matterTypes: [],
  dateRange: null,
  fileTypes: [],
  threshold: 0,
  searchMode: 'hybrid',
};

describe('useSemanticSearch', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('should initialize with idle state', () => {
    const { result } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    expect(result.current.results).toEqual([]);
    expect(result.current.totalCount).toBe(0);
    expect(result.current.state).toBe('idle');
    expect(result.current.isLoading).toBe(false);
    expect(result.current.isLoadingMore).toBe(false);
    expect(result.current.error).toBeNull();
    expect(result.current.hasMore).toBe(false);
    expect(result.current.query).toBe('');
  });

  it('should execute search successfully', async () => {
    mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    act(() => {
      result.current.search('test query', emptyFilters);
    });

    expect(result.current.isLoading).toBe(true);
    expect(result.current.state).toBe('loading');

    await waitFor(() => expect(['success', 'error']).toContain(result.current.state));

    expect(result.current.results).toHaveLength(10);
    expect(result.current.totalCount).toBe(50);
    expect(result.current.state).toBe('success');
    expect(result.current.isLoading).toBe(false);
    expect(result.current.hasMore).toBe(true);
    expect(result.current.query).toBe('test query');
  });

  it('should still search for an empty query (returns all documents in scope)', async () => {
    // Contract change: the hook's search() documents "Empty query is supported - returns
    // all documents in scope ordered by date", so an empty query MUST reach the API.
    mockSearch.mockResolvedValueOnce(createMockResponse(3, 3));
    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    await act(async () => {
      await result.current.search('', emptyFilters);
    });
    await waitFor(() => expect(result.current.state).toBe('success'));

    expect(mockSearch).toHaveBeenCalledTimes(1);
    expect(mockSearch.mock.calls[0][0]).toEqual(expect.objectContaining({ query: '' }));
    expect(result.current.results).toHaveLength(3);
  });

  it('should forward a whitespace-only query verbatim (trimming is the service/BFF concern)', async () => {
    mockSearch.mockResolvedValueOnce(createMockResponse(0, 0));
    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    await act(async () => {
      await result.current.search('   ', emptyFilters);
    });
    await waitFor(() => expect(result.current.state).toBe('success'));

    expect(mockSearch).toHaveBeenCalledTimes(1);
    expect(mockSearch.mock.calls[0][0]).toEqual(expect.objectContaining({ query: '   ' }));
  });

  it('should handle search error', async () => {
    const mockError = { message: 'Network error', retryable: true };
    mockSearch.mockRejectedValueOnce(mockError);

    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    act(() => {
      result.current.search('test query', emptyFilters);
    });

    await waitFor(() => expect(['success', 'error']).toContain(result.current.state));

    expect(result.current.state).toBe('error');
    expect(result.current.error).toEqual(mockError);
    expect(result.current.results).toEqual([]);
  });

  it('should load more results', async () => {
    // First search returns 10 of 50
    mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    // Initial search
    act(() => {
      result.current.search('test query', emptyFilters);
    });
    await waitFor(() => expect(['success', 'error']).toContain(result.current.state));

    expect(result.current.results).toHaveLength(10);

    // Load more returns the NEXT 10 (distinct documentIds - the hook dedupes by documentId)
    mockSearch.mockResolvedValueOnce(createMockResponse(10, 50, 10));

    act(() => {
      result.current.loadMore();
    });

    expect(result.current.isLoadingMore).toBe(true);
    expect(result.current.state).toBe('loadingMore');

    await waitFor(() => expect(['success', 'error']).toContain(result.current.state));

    expect(result.current.results).toHaveLength(20);
    expect(result.current.state).toBe('success');
  });

  it('should drop already-seen documentIds when a later page overlaps (dedupe)', async () => {
    mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));
    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    act(() => {
      result.current.search('test query', emptyFilters);
    });
    await waitFor(() => expect(result.current.state).toBe('success'));

    // Page 2 repeats doc-8/doc-9 and adds doc-10/doc-11.
    mockSearch.mockResolvedValueOnce(createMockResponse(4, 50, 8));
    act(() => {
      result.current.loadMore();
    });
    await waitFor(() => expect(result.current.state).toBe('success'));

    expect(result.current.results.map(r => r.documentId)).toEqual(
      Array.from({ length: 12 }, (_, i) => `doc-${i}`)
    );
  });

  it('should not load more when already loading', async () => {
    mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    // Start initial search (state -> 'loading')
    act(() => {
      result.current.search('test query', emptyFilters);
    });
    expect(result.current.state).toBe('loading');

    // Try to load more while loading
    await act(async () => {
      await result.current.loadMore();
    });
    await waitFor(() => expect(result.current.state).toBe('success'));

    // Should only have called search once (the initial one)
    expect(mockSearch).toHaveBeenCalledTimes(1);
  });

  it('should not load more when no more results', async () => {
    // Search returns all 10 results
    mockSearch.mockResolvedValueOnce(createMockResponse(10, 10));

    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    act(() => {
      result.current.search('test query', emptyFilters);
    });
    await waitFor(() => expect(['success', 'error']).toContain(result.current.state));

    expect(result.current.hasMore).toBe(false);

    // Try to load more
    await act(async () => {
      await result.current.loadMore();
    });

    // Should only have called search once (initial)
    expect(mockSearch).toHaveBeenCalledTimes(1);
  });

  it('should reset state', async () => {
    mockSearch.mockResolvedValueOnce(createMockResponse(10, 50));

    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    // Execute search
    act(() => {
      result.current.search('test query', emptyFilters);
    });
    await waitFor(() => expect(['success', 'error']).toContain(result.current.state));

    expect(result.current.results).toHaveLength(10);

    // Reset
    act(() => {
      result.current.reset();
    });

    expect(result.current.results).toEqual([]);
    expect(result.current.totalCount).toBe(0);
    expect(result.current.state).toBe('idle');
    expect(result.current.query).toBe('');
  });

  it('should pass scope and scopeId to API', async () => {
    mockSearch.mockResolvedValueOnce(createMockResponse(5, 5));

    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'matter', 'matter-123'));

    act(() => {
      result.current.search('test', emptyFilters);
    });
    await waitFor(() => expect(['success', 'error']).toContain(result.current.state));

    expect(mockSearch).toHaveBeenCalledWith(
      expect.objectContaining({
        scope: 'matter',
        scopeId: 'matter-123',
      }),
      undefined // searchIndexName not supplied -> forwarded as undefined
    );
  });

  it('should pass filters to API', async () => {
    mockSearch.mockResolvedValueOnce(createMockResponse(5, 5));

    const { result, waitFor } = renderHook(() => useSemanticSearch(mockApiService, 'all', null));

    const filters: SearchFilters = {
      documentTypes: ['contract'],
      matterTypes: [],
      dateRange: { from: '2026-01-01', to: '2026-01-31' },
      fileTypes: ['pdf'],
      threshold: 0,
      searchMode: 'hybrid',
    };

    act(() => {
      result.current.search('test', filters);
    });
    await waitFor(() => expect(['success', 'error']).toContain(result.current.state));

    expect(mockSearch).toHaveBeenCalledWith(
      expect.objectContaining({
        filters,
      }),
      undefined // searchIndexName not supplied -> forwarded as undefined
    );
  });
});
