/**
 * Integration tests for useAnalysisLoader hook
 *
 * Tests the data loading hook that fetches analysis and document metadata
 * in parallel from the BFF API. Covers:
 *   - Valid load (both resources succeed)
 *   - Invalid / missing analysis ID
 *   - Network failure
 *   - Parallel fetch (both fire concurrently)
 *   - Missing document (analysis loads, document errors)
 *
 * Mock strategy: Mock the analysisApi service functions (fetchAnalysis,
 * fetchDocumentMetadata) rather than fetch() so tests remain focused
 * on hook behavior, not HTTP plumbing.
 *
 * @see hooks/useAnalysisLoader.ts
 * @see services/analysisApi.ts
 */

import { renderHook, act, waitFor } from "@testing-library/react";
import { useAnalysisLoader } from "../hooks/useAnalysisLoader";
import {
    buildAnalysisRecord,
    buildDocumentMetadata,
    buildAnalysisError,
    TEST_ANALYSIS_ID,
    TEST_DOCUMENT_ID,
    TEST_TOKEN,
} from "./mocks/fixtures";

// Mock the analysisApi service module
jest.mock("../services/analysisApi");

import {
    fetchAnalysis,
    fetchDocumentMetadata,
} from "../services/analysisApi";

const mockFetchAnalysis = fetchAnalysis as jest.MockedFunction<typeof fetchAnalysis>;
const mockFetchDocumentMetadata = fetchDocumentMetadata as jest.MockedFunction<
    typeof fetchDocumentMetadata
>;

// ---------------------------------------------------------------------------
// Test Suite
// ---------------------------------------------------------------------------

describe("useAnalysisLoader", () => {
    beforeEach(() => {
        jest.clearAllMocks();
    });

    // -----------------------------------------------------------------------
    // 1. Valid Load
    // -----------------------------------------------------------------------

    it("loadAnalysisAndDocument_ValidIds_BothLoadedSuccessfully", async () => {
        // Arrange
        const analysisFixture = buildAnalysisRecord({ id: TEST_ANALYSIS_ID });
        const documentFixture = buildDocumentMetadata({ id: TEST_DOCUMENT_ID });

        mockFetchAnalysis.mockResolvedValue(analysisFixture);
        mockFetchDocumentMetadata.mockResolvedValue(documentFixture);

        // Act
        const { result } = renderHook(() =>
            useAnalysisLoader({
                analysisId: TEST_ANALYSIS_ID,
                documentId: TEST_DOCUMENT_ID,
                token: TEST_TOKEN,
            })
        );

        // Assert: initially loading
        expect(result.current.isLoading).toBe(true);

        // Wait for loading to complete
        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        // Assert: both resources loaded
        expect(result.current.analysis).toEqual(analysisFixture);
        expect(result.current.document).toEqual(documentFixture);
        expect(result.current.analysisError).toBeNull();
        expect(result.current.documentError).toBeNull();
    });

    // -----------------------------------------------------------------------
    // 2. Invalid ID
    // -----------------------------------------------------------------------

    it("loadAnalysis_InvalidId_ReturnsAnalysisError", async () => {
        // Arrange
        const apiError = buildAnalysisError({
            errorCode: "ANALYSIS_NOT_FOUND",
            message: "Analysis not found",
            status: 404,
        });

        mockFetchAnalysis.mockRejectedValue(apiError);
        mockFetchDocumentMetadata.mockResolvedValue(buildDocumentMetadata());

        // Act
        const { result } = renderHook(() =>
            useAnalysisLoader({
                analysisId: "non-existent-id",
                documentId: TEST_DOCUMENT_ID,
                token: TEST_TOKEN,
            })
        );

        await waitFor(() => {
            expect(result.current.isAnalysisLoading).toBe(false);
        });

        // Assert: analysis has error, document loaded fine
        expect(result.current.analysis).toBeNull();
        expect(result.current.analysisError).toEqual(
            expect.objectContaining({
                errorCode: "ANALYSIS_NOT_FOUND",
                message: "Analysis not found",
            })
        );
        expect(result.current.document).not.toBeNull();
        expect(result.current.documentError).toBeNull();
    });

    // -----------------------------------------------------------------------
    // 3. Network Failure
    // -----------------------------------------------------------------------

    it("loadAnalysis_NetworkFailure_ReturnsGenericError", async () => {
        // Arrange: simulate a TypeError (fetch network failure)
        mockFetchAnalysis.mockRejectedValue(new TypeError("Failed to fetch"));
        mockFetchDocumentMetadata.mockRejectedValue(new TypeError("Failed to fetch"));

        // Act
        const { result } = renderHook(() =>
            useAnalysisLoader({
                analysisId: TEST_ANALYSIS_ID,
                documentId: TEST_DOCUMENT_ID,
                token: TEST_TOKEN,
            })
        );

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        // Assert: both have errors with LOAD_FAILED code (non-AnalysisError objects
        // are wrapped by the hook's isAnalysisError type guard)
        expect(result.current.analysisError).toEqual(
            expect.objectContaining({
                errorCode: "LOAD_FAILED",
                message: "Failed to fetch",
            })
        );
        expect(result.current.documentError).toEqual(
            expect.objectContaining({
                errorCode: "LOAD_FAILED",
                message: "Failed to fetch",
            })
        );
    });

    // -----------------------------------------------------------------------
    // 4. Parallel Fetch
    // -----------------------------------------------------------------------

    it("loadBothResources_WithToken_FetchesBothInParallel", async () => {
        // Arrange: use delayed promises to verify parallel execution
        let analysisResolve: ((value: ReturnType<typeof buildAnalysisRecord>) => void) | undefined;
        let documentResolve: ((value: ReturnType<typeof buildDocumentMetadata>) => void) | undefined;

        mockFetchAnalysis.mockImplementation(
            () =>
                new Promise((resolve) => {
                    analysisResolve = resolve;
                })
        );
        mockFetchDocumentMetadata.mockImplementation(
            () =>
                new Promise((resolve) => {
                    documentResolve = resolve;
                })
        );

        // Act
        const { result } = renderHook(() =>
            useAnalysisLoader({
                analysisId: TEST_ANALYSIS_ID,
                documentId: TEST_DOCUMENT_ID,
                token: TEST_TOKEN,
            })
        );

        // Assert: both fetches were initiated (both are loading)
        expect(result.current.isAnalysisLoading).toBe(true);
        expect(result.current.isDocumentLoading).toBe(true);

        // Both API functions were called (parallel, not sequential)
        expect(mockFetchAnalysis).toHaveBeenCalledTimes(1);
        expect(mockFetchDocumentMetadata).toHaveBeenCalledTimes(1);

        // Resolve analysis first, then document
        await act(async () => {
            analysisResolve!(buildAnalysisRecord());
        });

        await waitFor(() => {
            expect(result.current.isAnalysisLoading).toBe(false);
        });

        // Document is still loading while analysis is done
        expect(result.current.analysis).not.toBeNull();
        expect(result.current.isDocumentLoading).toBe(true);

        // Now resolve document
        await act(async () => {
            documentResolve!(buildDocumentMetadata());
        });

        await waitFor(() => {
            expect(result.current.isDocumentLoading).toBe(false);
        });

        expect(result.current.document).not.toBeNull();
        expect(result.current.isLoading).toBe(false);
    });

    // -----------------------------------------------------------------------
    // 5. Missing Document
    // -----------------------------------------------------------------------

    it("loadDocument_MissingDocument_AnalysisLoadsDocumentErrors", async () => {
        // Arrange
        const analysisFixture = buildAnalysisRecord();
        const docError = buildAnalysisError({
            errorCode: "DOCUMENT_NOT_FOUND",
            message: "Document not found",
            status: 404,
        });

        mockFetchAnalysis.mockResolvedValue(analysisFixture);
        mockFetchDocumentMetadata.mockRejectedValue(docError);

        // Act
        const { result } = renderHook(() =>
            useAnalysisLoader({
                analysisId: TEST_ANALYSIS_ID,
                documentId: "missing-doc-id",
                token: TEST_TOKEN,
            })
        );

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        // Assert: analysis loaded, document has error
        expect(result.current.analysis).toEqual(analysisFixture);
        expect(result.current.analysisError).toBeNull();
        expect(result.current.document).toBeNull();
        expect(result.current.documentError).toEqual(
            expect.objectContaining({
                errorCode: "DOCUMENT_NOT_FOUND",
            })
        );
    });

    // -----------------------------------------------------------------------
    // 6. Token null (gated loading)
    // -----------------------------------------------------------------------

    it("loadResources_TokenNull_DoesNotFetchUntilTokenAvailable", async () => {
        // Arrange
        mockFetchAnalysis.mockResolvedValue(buildAnalysisRecord());
        mockFetchDocumentMetadata.mockResolvedValue(buildDocumentMetadata());

        // Act: render with null token
        const { result, rerender } = renderHook(
            (props: { token: string | null }) =>
                useAnalysisLoader({
                    analysisId: TEST_ANALYSIS_ID,
                    documentId: TEST_DOCUMENT_ID,
                    token: props.token,
                }),
            { initialProps: { token: null as string | null } }
        );

        // Assert: nothing fetched
        expect(mockFetchAnalysis).not.toHaveBeenCalled();
        expect(mockFetchDocumentMetadata).not.toHaveBeenCalled();
        expect(result.current.isLoading).toBe(false);

        // Act: provide token
        rerender({ token: TEST_TOKEN });

        await waitFor(() => {
            expect(result.current.analysis).not.toBeNull();
        });

        // Assert: now fetched
        expect(mockFetchAnalysis).toHaveBeenCalledWith(TEST_ANALYSIS_ID, TEST_TOKEN);
        expect(mockFetchDocumentMetadata).toHaveBeenCalledWith(TEST_DOCUMENT_ID, TEST_TOKEN);
    });

    // -----------------------------------------------------------------------
    // 7. Retry
    // -----------------------------------------------------------------------

    it("retry_AfterError_ReloadsFailedResources", async () => {
        // Arrange: first call fails, second succeeds
        const apiError = buildAnalysisError({ errorCode: "SERVER_ERROR", message: "Internal error" });
        mockFetchAnalysis
            .mockRejectedValueOnce(apiError)
            .mockResolvedValueOnce(buildAnalysisRecord());
        mockFetchDocumentMetadata.mockResolvedValue(buildDocumentMetadata());

        // Act
        const { result } = renderHook(() =>
            useAnalysisLoader({
                analysisId: TEST_ANALYSIS_ID,
                documentId: TEST_DOCUMENT_ID,
                token: TEST_TOKEN,
            })
        );

        await waitFor(() => {
            expect(result.current.isLoading).toBe(false);
        });

        // Assert: analysis errored
        expect(result.current.analysisError).not.toBeNull();

        // Act: retry
        await act(async () => {
            result.current.retry();
        });

        await waitFor(() => {
            expect(result.current.analysis).not.toBeNull();
        });

        // Assert: second attempt succeeded
        expect(result.current.analysisError).toBeNull();
        expect(result.current.analysis?.id).toBe(TEST_ANALYSIS_ID);
    });
});
