/**
 * useDocumentPreview Hook
 *
 * Custom hook for fetching document preview URL and managing iframe load state.
 * Includes 15-second timeout for iframe loading.
 */

import { useState, useEffect, useCallback, useRef } from 'react';
import { BffClient } from '../BffClient';
import {
    FilePreviewResponse,
    DocumentInfo,
    CheckoutStatus
} from '../types';

// Iframe load timeout in milliseconds (15 seconds per task 021)
const IFRAME_LOAD_TIMEOUT_MS = 15000;

export interface UseDocumentPreviewState {
    /** Preview URL for embed.aspx */
    previewUrl: string | null;
    /** Document metadata */
    documentInfo: DocumentInfo | null;
    /** Current checkout status */
    checkoutStatus: CheckoutStatus | null;
    /** Loading state for API call */
    isLoading: boolean;
    /** Loading state for iframe */
    isIframeLoading: boolean;
    /** Error message if any */
    error: string | null;
    /** Whether iframe timed out */
    isIframeTimedOut: boolean;
}

export interface UseDocumentPreviewActions {
    /** Reload document preview */
    refresh: () => void;
    /** Called when iframe loads successfully */
    onIframeLoad: () => void;
    /** Called when iframe fails to load */
    onIframeError: () => void;
    /** Reset iframe loading state (for retry) */
    resetIframeState: () => void;
}

export interface UseDocumentPreviewResult extends UseDocumentPreviewState, UseDocumentPreviewActions {}

/**
 * Hook for managing document preview state
 *
 * @param documentId - The Dataverse document ID
 * @param bffApiUrl - Base URL for BFF API
 * @param accessToken - Bearer token for API calls
 * @param correlationId - Correlation ID for request tracing
 */
export function useDocumentPreview(
    documentId: string | null | undefined,
    bffApiUrl: string,
    accessToken: string,
    correlationId: string
): UseDocumentPreviewResult {
    const [state, setState] = useState<UseDocumentPreviewState>({
        previewUrl: null,
        documentInfo: null,
        checkoutStatus: null,
        isLoading: true,
        isIframeLoading: true,
        error: null,
        isIframeTimedOut: false
    });

    const bffClient = useRef(new BffClient(bffApiUrl));
    const iframeTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
    const isMounted = useRef(true);

    // Update client if URL changes
    useEffect(() => {
        bffClient.current = new BffClient(bffApiUrl);
    }, [bffApiUrl]);

    // Track mount state
    useEffect(() => {
        isMounted.current = true;
        return () => {
            isMounted.current = false;
        };
    }, []);

    /**
     * Clear iframe timeout
     */
    const clearIframeTimeout = useCallback(() => {
        if (iframeTimeoutRef.current) {
            clearTimeout(iframeTimeoutRef.current);
            iframeTimeoutRef.current = null;
        }
    }, []);

    /**
     * Start iframe load timeout
     */
    const startIframeTimeout = useCallback(() => {
        clearIframeTimeout();

        iframeTimeoutRef.current = setTimeout(() => {
            if (isMounted.current) {
                console.warn('[useDocumentPreview] Iframe load timeout after 15 seconds');
                setState(prev => ({
                    ...prev,
                    isIframeLoading: false,
                    isIframeTimedOut: true,
                    error: 'Document preview timed out. The document may be too large or the connection is slow. Please try again.'
                }));
            }
        }, IFRAME_LOAD_TIMEOUT_MS);
    }, [clearIframeTimeout]);

    /**
     * Fetch document preview URL
     */
    const loadDocument = useCallback(async () => {
        if (!documentId) {
            setState(prev => ({
                ...prev,
                isLoading: false,
                isIframeLoading: false,
                error: null,
                previewUrl: null,
                documentInfo: null,
                checkoutStatus: null
            }));
            return;
        }

        // Reset state before loading
        setState(prev => ({
            ...prev,
            isLoading: true,
            isIframeLoading: true,
            isIframeTimedOut: false,
            error: null
        }));

        try {
            console.log(`[useDocumentPreview] Fetching view URL for document: ${documentId}`);

            const response: FilePreviewResponse = await bffClient.current.getViewUrl(
                documentId,
                accessToken,
                correlationId
            );

            if (!isMounted.current) return;

            console.log('[useDocumentPreview] View URL received (real-time):', {
                hasUrl: !!response.previewUrl,
                documentName: response.documentInfo?.name,
                isCheckedOut: response.checkoutStatus?.isCheckedOut
            });

            setState(prev => ({
                ...prev,
                isLoading: false,
                previewUrl: response.previewUrl,
                documentInfo: response.documentInfo ?? null,
                checkoutStatus: response.checkoutStatus ?? null,
                error: null
            }));

            // Start iframe timeout now that we have a URL
            if (response.previewUrl) {
                startIframeTimeout();
            }

        } catch (error) {
            if (!isMounted.current) return;

            console.error('[useDocumentPreview] Failed to load preview:', error);

            setState(prev => ({
                ...prev,
                isLoading: false,
                isIframeLoading: false,
                error: error instanceof Error ? error.message : 'Failed to load document preview'
            }));
        }
    }, [documentId, accessToken, correlationId, startIframeTimeout]);

    /**
     * Handle iframe load success
     */
    const onIframeLoad = useCallback(() => {
        console.log('[useDocumentPreview] Iframe loaded successfully');
        clearIframeTimeout();

        if (isMounted.current) {
            setState(prev => ({
                ...prev,
                isIframeLoading: false,
                isIframeTimedOut: false
            }));
        }
    }, [clearIframeTimeout]);

    /**
     * Handle iframe load error
     */
    const onIframeError = useCallback(() => {
        console.error('[useDocumentPreview] Iframe failed to load');
        clearIframeTimeout();

        if (isMounted.current) {
            setState(prev => ({
                ...prev,
                isIframeLoading: false,
                error: 'Failed to load document preview. Please try again.'
            }));
        }
    }, [clearIframeTimeout]);

    /**
     * Reset iframe state for retry
     */
    const resetIframeState = useCallback(() => {
        clearIframeTimeout();
        setState(prev => ({
            ...prev,
            isIframeLoading: true,
            isIframeTimedOut: false,
            error: null
        }));
        startIframeTimeout();
    }, [clearIframeTimeout, startIframeTimeout]);

    /**
     * Refresh document preview
     */
    const refresh = useCallback(() => {
        clearIframeTimeout();
        loadDocument();
    }, [clearIframeTimeout, loadDocument]);

    // Load on mount and when document changes
    useEffect(() => {
        loadDocument();

        return () => {
            clearIframeTimeout();
        };
    }, [loadDocument, clearIframeTimeout]);

    return {
        ...state,
        refresh,
        onIframeLoad,
        onIframeError,
        resetIframeState
    };
}

export default useDocumentPreview;
