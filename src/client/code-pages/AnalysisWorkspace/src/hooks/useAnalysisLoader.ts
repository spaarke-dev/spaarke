/**
 * useAnalysisLoader - Data loading hook for AnalysisWorkspace
 *
 * Fetches the analysis record and document metadata in parallel from the BFF API
 * on mount. Manages loading, loaded, and error states for both resources independently.
 * Provides a retry function for recovering from transient failures.
 *
 * @see ADR-007 - Document access through BFF API (SpeFileStore facade)
 * @see ADR-019 - ProblemDetails error handling
 */

import { useCallback, useEffect, useState, useRef } from "react";
import { fetchAnalysis, fetchDocumentMetadata } from "../services/analysisApi";
import type { AnalysisRecord, DocumentMetadata, AnalysisError } from "../types";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseAnalysisLoaderOptions {
    /** GUID of the analysis record to load */
    analysisId: string;
    /** GUID of the source document to load metadata for */
    documentId: string;
    /** Bearer auth token for BFF API calls */
    token: string | null;
}

export interface UseAnalysisLoaderResult {
    /** Loaded analysis record (null while loading or on error) */
    analysis: AnalysisRecord | null;
    /** Loaded document metadata (null while loading or on error) */
    document: DocumentMetadata | null;
    /** Whether the analysis record is currently loading */
    isAnalysisLoading: boolean;
    /** Whether the document metadata is currently loading */
    isDocumentLoading: boolean;
    /** Whether any resource is currently loading */
    isLoading: boolean;
    /** Error from analysis loading (null on success) */
    analysisError: AnalysisError | null;
    /** Error from document loading (null on success) */
    documentError: AnalysisError | null;
    /** Retry loading all failed resources */
    retry: () => void;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Data loading hook that fetches analysis and document metadata in parallel.
 *
 * The hook fires when `token` becomes non-null and `analysisId` / `documentId`
 * are provided. This ensures data is loaded only after authentication completes.
 *
 * @example
 * ```tsx
 * const { analysis, document, isLoading, analysisError, retry } = useAnalysisLoader({
 *     analysisId: "abc-123",
 *     documentId: "doc-456",
 *     token: authToken,
 * });
 *
 * if (isLoading) return <Spinner label="Loading analysis..." />;
 * if (analysisError) return <ErrorState error={analysisError} onRetry={retry} />;
 * ```
 */
export function useAnalysisLoader(options: UseAnalysisLoaderOptions): UseAnalysisLoaderResult {
    const { analysisId, documentId, token } = options;

    // Analysis state
    const [analysis, setAnalysis] = useState<AnalysisRecord | null>(null);
    const [isAnalysisLoading, setIsAnalysisLoading] = useState(false);
    const [analysisError, setAnalysisError] = useState<AnalysisError | null>(null);

    // Document state
    const [document, setDocument] = useState<DocumentMetadata | null>(null);
    const [isDocumentLoading, setIsDocumentLoading] = useState(false);
    const [documentError, setDocumentError] = useState<AnalysisError | null>(null);

    // Ref to track whether the component is still mounted (avoid state updates after unmount)
    const mountedRef = useRef(true);

    useEffect(() => {
        mountedRef.current = true;
        return () => {
            mountedRef.current = false;
        };
    }, []);

    /**
     * Load the analysis record from the BFF API.
     */
    const loadAnalysis = useCallback(async () => {
        if (!analysisId || !token) return;

        setIsAnalysisLoading(true);
        setAnalysisError(null);

        try {
            const result = await fetchAnalysis(analysisId, token, documentId);
            if (mountedRef.current) {
                setAnalysis(result);
            }
        } catch (err) {
            if (mountedRef.current) {
                const analysisErr = isAnalysisError(err)
                    ? err
                    : {
                          errorCode: "LOAD_FAILED",
                          message:
                              err instanceof Error
                                  ? err.message
                                  : "Failed to load analysis",
                      };
                setAnalysisError(analysisErr);
            }
        } finally {
            if (mountedRef.current) {
                setIsAnalysisLoading(false);
            }
        }
    }, [analysisId, documentId, token]);

    /**
     * Load document metadata from the BFF API.
     */
    const loadDocument = useCallback(async () => {
        if (!documentId || !token) return;

        setIsDocumentLoading(true);
        setDocumentError(null);

        try {
            const result = await fetchDocumentMetadata(documentId, token);
            if (mountedRef.current) {
                setDocument(result);
            }
        } catch (err) {
            if (mountedRef.current) {
                const docErr = isAnalysisError(err)
                    ? err
                    : {
                          errorCode: "LOAD_FAILED",
                          message:
                              err instanceof Error
                                  ? err.message
                                  : "Failed to load document metadata",
                      };
                setDocumentError(docErr);
            }
        } finally {
            if (mountedRef.current) {
                setIsDocumentLoading(false);
            }
        }
    }, [documentId, token]);

    /**
     * Load both resources in parallel when token becomes available.
     */
    useEffect(() => {
        if (!token) return;

        // Fetch in parallel
        loadAnalysis();
        loadDocument();
    }, [token, loadAnalysis, loadDocument]);

    /**
     * Retry loading all failed resources.
     */
    const retry = useCallback(() => {
        if (analysisError) {
            loadAnalysis();
        }
        if (documentError) {
            loadDocument();
        }
        // If both are null (no errors), reload everything
        if (!analysisError && !documentError) {
            loadAnalysis();
            loadDocument();
        }
    }, [analysisError, documentError, loadAnalysis, loadDocument]);

    return {
        analysis,
        document,
        isAnalysisLoading,
        isDocumentLoading,
        isLoading: isAnalysisLoading || isDocumentLoading,
        analysisError,
        documentError,
        retry,
    };
}

// ---------------------------------------------------------------------------
// Type Guard
// ---------------------------------------------------------------------------

/**
 * Check if an unknown error value matches the AnalysisError shape.
 */
function isAnalysisError(err: unknown): err is AnalysisError {
    return (
        typeof err === "object" &&
        err !== null &&
        "errorCode" in err &&
        "message" in err
    );
}
