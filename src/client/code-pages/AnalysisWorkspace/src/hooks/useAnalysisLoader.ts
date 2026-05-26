/**
 * useAnalysisLoader - Data loading hook for AnalysisWorkspace
 *
 * Loads the analysis record directly from Dataverse Web API (same-origin)
 * and document metadata from the BFF API in parallel.
 * Dataverse is the source of truth for analysis content.
 *
 * Spaarke Auth v2 (task 026): consumes `authenticatedFetch` instead of a
 * snapshotted token. The hook re-fires when `isAuthenticated` flips true so
 * the document metadata load runs once the provider is ready.
 *
 * @see ADR-007 - Document access through BFF API (SpeFileStore facade)
 */

import { useCallback, useEffect, useState, useRef } from 'react';
import type { AuthenticatedFetchFn } from '@spaarke/auth';
import { fetchAnalysis, fetchDocumentMetadata, getDocumentViewUrl } from '../services/analysisApi';
import type { AnalysisRecord, DocumentMetadata, AnalysisError } from '../types';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface UseAnalysisLoaderOptions {
  /** GUID of the analysis record to load */
  analysisId: string;
  /** GUID of the source document to load metadata for */
  documentId: string;
  /** Whether the user is authenticated (gate to defer BFF metadata load). */
  isAuthenticated: boolean;
  /** Authenticated fetch from useAuth() — wraps Bearer + 401 retry. */
  authenticatedFetch: AuthenticatedFetchFn;
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
  /** Reload only the analysis record (no document refetch) */
  reloadAnalysis: () => void;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Data loading hook that fetches analysis (from Dataverse) and
 * document metadata (from BFF) in parallel.
 *
 * Analysis loading uses Dataverse Web API directly (same-origin, no token needed).
 * Document metadata loading uses BFF API via authenticatedFetch.
 */
export function useAnalysisLoader(options: UseAnalysisLoaderOptions): UseAnalysisLoaderResult {
  const { analysisId, documentId, isAuthenticated, authenticatedFetch } = options;

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
   * Load the analysis record from Dataverse Web API (same-origin).
   */
  const loadAnalysis = useCallback(async () => {
    if (!analysisId) return;

    setIsAnalysisLoading(true);
    setAnalysisError(null);

    try {
      const result = await fetchAnalysis(analysisId);
      if (mountedRef.current) {
        setAnalysis(result);
      }
    } catch (err) {
      if (mountedRef.current) {
        const analysisErr = isAnalysisError(err)
          ? err
          : {
              errorCode: 'LOAD_FAILED',
              message: err instanceof Error ? err.message : 'Failed to load analysis',
            };
        setAnalysisError(analysisErr);
      }
    } finally {
      if (mountedRef.current) {
        setIsAnalysisLoading(false);
      }
    }
  }, [analysisId]);

  /**
   * Load document metadata and preview URL from the BFF API via authenticatedFetch.
   * Fetches metadata first, then preview URL, and merges the viewUrl into metadata.
   */
  const loadDocument = useCallback(async () => {
    if (!documentId || !isAuthenticated) return;

    setIsDocumentLoading(true);
    setDocumentError(null);

    try {
      const result = await fetchDocumentMetadata(documentId, authenticatedFetch);

      // If metadata loaded but has no viewUrl, fetch the preview URL separately
      if (!result.viewUrl) {
        try {
          const previewUrl = await getDocumentViewUrl(documentId, authenticatedFetch);
          if (previewUrl) {
            result.viewUrl = previewUrl;
          }
        } catch (previewErr) {
          // Non-fatal: metadata is still useful without preview
          console.warn('[useAnalysisLoader] Preview URL fetch failed:', previewErr);
        }
      }

      if (mountedRef.current) {
        setDocument(result);
      }
    } catch (err) {
      if (mountedRef.current) {
        const docErr = isAnalysisError(err)
          ? err
          : {
              errorCode: 'LOAD_FAILED',
              message: err instanceof Error ? err.message : 'Failed to load document metadata',
            };
        setDocumentError(docErr);
      }
    } finally {
      if (mountedRef.current) {
        setIsDocumentLoading(false);
      }
    }
  }, [documentId, isAuthenticated, authenticatedFetch]);

  /**
   * Load both resources in parallel once authenticated.
   */
  useEffect(() => {
    if (!isAuthenticated) return;

    loadAnalysis();
    loadDocument();
  }, [isAuthenticated, loadAnalysis, loadDocument]);

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
    reloadAnalysis: loadAnalysis,
  };
}

// ---------------------------------------------------------------------------
// Type Guard
// ---------------------------------------------------------------------------

/**
 * Check if an unknown error value matches the AnalysisError shape.
 */
function isAnalysisError(err: unknown): err is AnalysisError {
  return typeof err === 'object' && err !== null && 'errorCode' in err && 'message' in err;
}
