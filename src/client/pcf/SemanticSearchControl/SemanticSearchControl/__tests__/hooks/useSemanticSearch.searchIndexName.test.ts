/**
 * Hook-level unit tests for the FR-PCF-02 (Wave 9/10) `searchIndexName` wiring
 * in `useSemanticSearch`.
 *
 * Focus: verify the new optional 4th argument flows correctly into:
 *   - `apiService.search(...)` on the initial `search(...)` call, and
 *   - `apiService.search(...)` on the paginated `loadMore(...)` call.
 *
 * `searchUnion` was retired from the hook (multi-container-multi-index-r1 post-Phase D
 * UAT: one source of truth, the AI Search index), so both calls route through
 * `apiService.search`; a test asserts `searchUnion` is never called.
 *
 * Behaviour contract (per the hook's JSDoc + tasks 031/032):
 *   - When `searchIndexName` is a non-empty string, it is forwarded verbatim
 *     to BOTH the initial `search` and the subsequent `search` (loadMore) call.
 *   - When `searchIndexName` is `null` / `undefined` (the omit-on-empty
 *     contract), the hook forwards `undefined` to the service, which then
 *     omits the field from the BFF request body (covered separately by
 *     `services/SemanticSearchApiService.test.ts`).
 *
 * This file intentionally does NOT duplicate the broader behavioural tests
 * already present in `useSemanticSearch.test.ts`. The tests here are tightly
 * scoped to the Wave-10 wiring per task 034.
 *
 * @see ../../hooks/useSemanticSearch.ts
 * @see ../../services/SemanticSearchApiService.ts
 * @see projects/spaarke-multi-container-multi-index-r1/spec.md FR-PCF-02
 */

import { renderHook, act } from '@testing-library/react-hooks';
import { useSemanticSearch } from '../../hooks/useSemanticSearch';
import { SemanticSearchApiService } from '../../services';
import { SearchFilters, SearchResponse } from '../../types';

// -----------------------------------------------------------------------------
// Test scaffolding
// -----------------------------------------------------------------------------

/** Builds an `apiService`-shaped mock exposing only the methods the hook calls. */
function buildMockApiService() {
  const mockSearch = jest.fn();
  const mockSearchUnion = jest.fn();
  const service = {
    search: mockSearch,
    searchUnion: mockSearchUnion,
  } as unknown as SemanticSearchApiService;
  return { service, mockSearch, mockSearchUnion };
}

/** Minimal default filters; matches the defaults the hook initializes with. */
const defaultFilters: SearchFilters = {
  documentTypes: [],
  matterTypes: [],
  dateRange: null,
  fileTypes: [],
  threshold: 50,
  searchMode: 'hybrid',
  associatedOnly: true,
};

/** Build a minimal SearchResponse with N results and a totalCount. */
function buildResponse(count: number, total: number): SearchResponse {
  return {
    results: Array.from({ length: count }, (_, i) => ({
      documentId: `doc-${i}`,
      name: `Document ${i}`,
      fileType: 'pdf',
      documentType: 'contract',
      matterName: null,
      matterId: null,
      createdAt: '2026-01-01',
      combinedScore: 0.95 - i * 0.01,
      highlights: ['hit'],
      fileUrl: `https://example.com/doc-${i}`,
      recordUrl: `https://crm.dynamics.com/doc-${i}`,
      createdBy: null,
      modifiedAt: null,
      modifiedBy: null,
      summary: null,
      tldr: null,
    })),
    totalCount: total,
    metadata: { searchTimeMs: 10, query: 'test' },
  };
}

// -----------------------------------------------------------------------------
// FR-PCF-02 — searchIndexName forwarding through useSemanticSearch
// -----------------------------------------------------------------------------

describe('useSemanticSearch — FR-PCF-02 searchIndexName forwarding', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  // React 16 does not batch the hook's several setState calls after an await, so
  // `waitForNextUpdate` can resolve on the FIRST of them. Settle on the final state.
  async function settle(
    result: { current: ReturnType<typeof useSemanticSearch> },
    waitFor: (cb: () => void) => Promise<void>
  ) {
    await waitFor(() => expect(result.current.state).toBe('success'));
  }

  // ---------------------------------------------------------------------------
  // search(): forwards into apiService.search()
  // ---------------------------------------------------------------------------
  it('forwards a non-empty searchIndexName into apiService.search on initial search', async () => {
    const { service, mockSearch, mockSearchUnion } = buildMockApiService();
    mockSearch.mockResolvedValueOnce(buildResponse(3, 3));

    const { result, waitFor } = renderHook(() =>
      useSemanticSearch(service, 'matter', 'matter-001', 'spaarke-file-index')
    );

    act(() => {
      void result.current.search('contracts', defaultFilters);
    });
    await settle(result, waitFor);

    expect(mockSearch).toHaveBeenCalledTimes(1);
    // 2nd positional arg is the searchIndexName forwarded by the hook.
    expect(mockSearch.mock.calls[0][1]).toBe('spaarke-file-index');
    // The union path is retired: the hook must not route through searchUnion any more.
    expect(mockSearchUnion).not.toHaveBeenCalled();
  });

  it('forwards `undefined` (NOT the literal null) when searchIndexName is null', async () => {
    // FR-PCF-02: hook normalizes null -> undefined via `searchIndexName ?? undefined`.
    // The service-level test then verifies undefined -> omit-on-empty in body.
    const { service, mockSearch } = buildMockApiService();
    mockSearch.mockResolvedValueOnce(buildResponse(0, 0));

    const { result, waitFor } = renderHook(() => useSemanticSearch(service, 'matter', 'matter-001', null));

    act(() => {
      void result.current.search('contracts', defaultFilters);
    });
    await settle(result, waitFor);

    expect(mockSearch).toHaveBeenCalledTimes(1);
    expect(mockSearch.mock.calls[0][1]).toBeUndefined();
  });

  it('forwards `undefined` when searchIndexName is not provided (default arg)', async () => {
    const { service, mockSearch } = buildMockApiService();
    mockSearch.mockResolvedValueOnce(buildResponse(0, 0));

    const { result, waitFor } = renderHook(() =>
      // 4th arg omitted entirely
      useSemanticSearch(service, 'all', null)
    );

    act(() => {
      void result.current.search('contracts', defaultFilters);
    });
    await settle(result, waitFor);

    expect(mockSearch).toHaveBeenCalledTimes(1);
    expect(mockSearch.mock.calls[0][1]).toBeUndefined();
  });

  // ---------------------------------------------------------------------------
  // loadMore(): re-uses the same searchIndexName on the paginated search() call
  // ---------------------------------------------------------------------------
  it('loadMore reuses the same searchIndexName on the paginated apiService.search call', async () => {
    const { service, mockSearch } = buildMockApiService();
    // Initial search returns 3 of 10 -> hasMore=true; loadMore returns the next page.
    mockSearch.mockResolvedValueOnce(buildResponse(3, 10));
    mockSearch.mockResolvedValueOnce(buildResponse(3, 10));

    const { result, waitFor } = renderHook(() =>
      useSemanticSearch(service, 'matter', 'matter-001', 'spaarke-file-index')
    );

    // Step 1: initial search
    act(() => {
      void result.current.search('contracts', defaultFilters);
    });
    await waitFor(() => expect(result.current.hasMore).toBe(true));
    await settle(result, waitFor);

    expect(mockSearch).toHaveBeenCalledTimes(1);
    expect(mockSearch.mock.calls[0][1]).toBe('spaarke-file-index');

    // Step 2: loadMore
    act(() => {
      void result.current.loadMore();
    });
    await waitFor(() => expect(mockSearch).toHaveBeenCalledTimes(2));

    // The hook MUST forward the same index name on the paginated call so the
    // BFF routes subsequent pages to the same index (FR-PCF-02 + spec
    // multi-container-multi-index-r1 invariants).
    expect(mockSearch.mock.calls[1][1]).toBe('spaarke-file-index');
    // ...and it is a genuine second page, not a repeat of page one.
    expect(mockSearch.mock.calls[1][0].options.offset).toBe(3);
  });

  it('loadMore forwards `undefined` when the hook was constructed without an index name', async () => {
    const { service, mockSearch } = buildMockApiService();
    mockSearch.mockResolvedValueOnce(buildResponse(3, 10));
    mockSearch.mockResolvedValueOnce(buildResponse(3, 10));

    const { result, waitFor } = renderHook(
      () => useSemanticSearch(service, 'all', null) // no 4th arg
    );

    act(() => {
      void result.current.search('contracts', defaultFilters);
    });
    await waitFor(() => expect(result.current.hasMore).toBe(true));
    await settle(result, waitFor);

    act(() => {
      void result.current.loadMore();
    });
    await waitFor(() => expect(mockSearch).toHaveBeenCalledTimes(2));

    expect(mockSearch.mock.calls[1][1]).toBeUndefined();
  });

  // ---------------------------------------------------------------------------
  // Re-render contract: changing searchIndexName mid-session picks up the new
  // value on the NEXT search call (closures recreated via useCallback dep).
  // ---------------------------------------------------------------------------
  it('picks up a new searchIndexName when the hook re-renders with a different value', async () => {
    const { service, mockSearch } = buildMockApiService();
    mockSearch.mockResolvedValue(buildResponse(0, 0));

    const { result, rerender, waitFor } = renderHook(
      ({ idx }: { idx: string | null }) => useSemanticSearch(service, 'matter', 'matter-001', idx),
      { initialProps: { idx: 'index-A' } }
    );

    // First search uses index-A
    act(() => {
      void result.current.search('q1', defaultFilters);
    });
    await settle(result, waitFor);
    expect(mockSearch.mock.calls[0][1]).toBe('index-A');

    // Caller swaps to index-B (mirrors the PCF user navigating between scope
    // records with different sprk_searchindexname values without remounting).
    rerender({ idx: 'index-B' });

    // Next search uses index-B
    act(() => {
      void result.current.search('q2', defaultFilters);
    });
    await waitFor(() => expect(mockSearch).toHaveBeenCalledTimes(2));
    expect(mockSearch.mock.calls[1][1]).toBe('index-B');
  });
});
