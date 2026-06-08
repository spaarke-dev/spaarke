/**
 * FR-PARITY-02 — Unit-level shape-parity slice between code-page and PCF.
 *
 * The full FR-PARITY-02 acceptance (PCF and code-page MUST show identical
 * result sets for the same time-point) is fundamentally a live UAT concern —
 * scheduled in task 073. The PCF and code-page intentionally use DIFFERENT
 * request-body SHAPES (PCF transforms scope/entityType/entityId/associatedOnly
 * into a flat shape; the code-page emits the BFF's already-flat shape) so a
 * literal body-equality test would be over-specified and wrong.
 *
 * However, there is ONE invariant that MUST hold across both surfaces at the
 * unit level for FR-PARITY-02 to be reachable in UAT: the `searchIndexName`
 * envelope field MUST be forwarded with IDENTICAL semantics. If the two
 * surfaces disagree on when to send / omit / trim the index, they will route
 * to different Azure AI Search indexes and FR-PARITY-02 will fail in UAT.
 *
 * This test verifies that contract — the request bodies emitted by both
 * surfaces, for matched inputs, contain `searchIndexName` (or not) under the
 * SAME rules.
 *
 * Rule (per FR-PCF-02 + FR-CP-04, both citing FR-BFF-04):
 *   - non-empty trimmed string → forward as the trimmed value
 *   - null / undefined / empty / whitespace-only → omit the key entirely
 *
 * @see useSemanticSearch.ts buildSearchIndexNameFragment
 * @see src/client/pcf/SemanticSearchControl/.../SemanticSearchApiService.ts transformRequest
 * @see projects/spaarke-multi-container-multi-index-r1/spec.md — FR-PARITY-02, FR-CP-04
 * @see projects/spaarke-multi-container-multi-index-r1/tasks/073-uat-r-checklist.poml — full UAT
 */

import { renderHook, act } from '@testing-library/react';
import type { DocumentSearchResponse, SearchFilters } from '../../types';

// ---------------------------------------------------------------------------
// Mocks
// ---------------------------------------------------------------------------

const mockSearch = jest.fn<Promise<DocumentSearchResponse>, [unknown]>();

jest.mock('../../services/SemanticSearchApiService', () => ({
  search: (...args: unknown[]) => mockSearch(...args),
}));

import { useSemanticSearch } from '../../hooks/useSemanticSearch';

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

const baseFilters: SearchFilters = {
  documentTypes: [],
  fileTypes: [],
  matterTypes: [],
  dateRange: { from: null, to: null },
  threshold: 0.5,
  searchMode: 'hybrid',
};

const emptyResponse: DocumentSearchResponse = {
  results: [],
  metadata: {
    totalResults: 0,
    returnedResults: 0,
    searchDurationMs: 0,
    embeddingDurationMs: 0,
  },
};

/**
 * Mirror of the PCF SemanticSearchApiService.transformRequest() conditional
 * for `searchIndexName`. Reproduced inline (NOT imported) because the PCF lives
 * outside this code-page's tsconfig rootDir. The PCF rule is:
 *
 *   const trimmedIndex = typeof searchIndexName === 'string'
 *     ? searchIndexName.trim() : '';
 *   const indexField = trimmedIndex.length > 0
 *     ? { searchIndexName: trimmedIndex } : {};
 *
 * If this helper drifts from the PCF source, the parity invariant is broken.
 * Source: src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/SemanticSearchApiService.ts
 * lines ~565-566 (`trimmedIndex` calculation).
 */
function pcfBuildSearchIndexNameFragment(searchIndexName?: string | null): { searchIndexName?: string } {
  const trimmed = typeof searchIndexName === 'string' ? searchIndexName.trim() : '';
  return trimmed.length > 0 ? { searchIndexName: trimmed } : {};
}

// ---------------------------------------------------------------------------
// Tests — parity invariant
// ---------------------------------------------------------------------------

describe('FR-PARITY-02 — searchIndexName forwarding parity (PCF vs code-page)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockSearch.mockResolvedValue(emptyResponse);
  });

  /**
   * The cases below sweep all five branches of the rule:
   *  - present + clean
   *  - present + whitespace-padded (trim required)
   *  - empty string (must omit)
   *  - whitespace-only (must omit)
   *  - null (must omit)
   *  - undefined (must omit)
   */
  const cases: Array<{
    name: string;
    input: string | null | undefined;
    expectedPresent: boolean;
    expectedValue?: string;
  }> = [
    {
      name: 'non-empty value',
      input: 'spaarke-file-index',
      expectedPresent: true,
      expectedValue: 'spaarke-file-index',
    },
    {
      name: 'padded value (trim)',
      input: '  spaarke-file-index  ',
      expectedPresent: true,
      expectedValue: 'spaarke-file-index',
    },
    { name: 'empty string', input: '', expectedPresent: false },
    { name: 'whitespace-only', input: '   ', expectedPresent: false },
    { name: 'null', input: null, expectedPresent: false },
    { name: 'undefined', input: undefined, expectedPresent: false },
  ];

  cases.forEach(({ name, input, expectedPresent, expectedValue }) => {
    it(`code-page and PCF agree on "${name}" handling`, async () => {
      // ---- code-page side: derive the body via the hook ----
      const { result } = renderHook(() => useSemanticSearch());

      await act(async () => {
        result.current.search('contracts', baseFilters, input);
      });

      const codePageBody = mockSearch.mock.calls[0][0] as Record<string, unknown>;
      const codePageHasIndex = Object.prototype.hasOwnProperty.call(codePageBody, 'searchIndexName');
      const codePageIndexValue = codePageBody.searchIndexName;

      // ---- PCF side: replicate the PCF rule inline ----
      const pcfFragment = pcfBuildSearchIndexNameFragment(input);
      const pcfHasIndex = Object.prototype.hasOwnProperty.call(pcfFragment, 'searchIndexName');
      const pcfIndexValue = pcfFragment.searchIndexName;

      // ---- The invariant ----
      // Both surfaces must agree on whether to include the key.
      expect(codePageHasIndex).toBe(pcfHasIndex);
      // Both surfaces must agree on the expected presence per the rule.
      expect(codePageHasIndex).toBe(expectedPresent);
      // When present, both must emit the same trimmed value.
      if (expectedPresent) {
        expect(codePageIndexValue).toBe(expectedValue);
        expect(pcfIndexValue).toBe(expectedValue);
        expect(codePageIndexValue).toBe(pcfIndexValue);
      }
    });
  });

  it('parity holds across loadMore() — index reused on subsequent pages', async () => {
    // First search captures the index into the hook's ref.
    const page1: DocumentSearchResponse = {
      results: Array.from({ length: 20 }, (_, i) => ({
        documentId: `doc-${i}`,
        name: `Doc ${i}`,
        combinedScore: 0.9,
        documentType: 'Contract',
        fileType: 'pdf',
      })),
      metadata: {
        totalResults: 50,
        returnedResults: 20,
        searchDurationMs: 100,
        embeddingDurationMs: 10,
      },
    };
    mockSearch.mockResolvedValueOnce(page1);

    const { result } = renderHook(() => useSemanticSearch());

    await act(async () => {
      result.current.search('contracts', baseFilters, 'spaarke-file-index');
    });

    mockSearch.mockResolvedValueOnce(emptyResponse);
    await act(async () => {
      result.current.loadMore();
    });

    const secondCall = mockSearch.mock.calls[1][0] as Record<string, unknown>;
    expect(secondCall.searchIndexName).toBe('spaarke-file-index');

    // PCF rule produces the same value for the same input string.
    const pcfFragment = pcfBuildSearchIndexNameFragment('spaarke-file-index');
    expect(secondCall.searchIndexName).toBe(pcfFragment.searchIndexName);
  });
});

// ---------------------------------------------------------------------------
// Documentation — FR-PARITY-02 coverage scope
// ---------------------------------------------------------------------------

/**
 * NOTE on full FR-PARITY-02 coverage:
 *
 * The acceptance test "PCF result set MATCHES code-page result set for the
 * same time-point" requires:
 *   1. A live Azure AI Search index with seeded data
 *   2. A protected matter with associated documents
 *   3. Both surfaces (PCF on the form, code-page launched via "Open in
 *      Semantic Search") executing against the SAME BFF instance at the
 *      same time-point
 *   4. Document-ID + count comparison
 *
 * None of these are reproducible at the unit-test layer. The unit-level
 * slice this file covers — searchIndexName-forwarding parity — is the
 * NECESSARY but NOT SUFFICIENT precondition. The SUFFICIENT condition is
 * verified by the UAT walkthrough in task 073, captured in the project
 * handoff notes alongside task-073's UAT-R checklist.
 */
