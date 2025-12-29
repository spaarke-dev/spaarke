/**
 * useCheckoutFlow Hook
 *
 * Manages the document checkout/checkin state machine:
 * - Preview -> Checkout -> Edit -> CheckIn -> Preview
 * - Edit -> Discard -> Preview
 *
 * Handles API calls, loading states, and error handling.
 */

import { useState, useCallback, useRef } from 'react';
import { BffClient, DocumentLockedException } from '../BffClient';
import { ViewMode, CheckoutStatus, CheckoutResponse } from '../types';

export interface UseCheckoutFlowState {
    /** Current view mode */
    viewMode: ViewMode;
    /** Office Online edit URL (when in edit mode) */
    editUrl: string | null;
    /** Loading state for checkout operation */
    isCheckoutLoading: boolean;
    /** Loading state for check-in operation */
    isCheckInLoading: boolean;
    /** Loading state for discard operation */
    isDiscardLoading: boolean;
    /** Error message from last operation */
    error: string | null;
    /** Document locked error details (for 409 conflicts) */
    lockError: {
        lockedByName: string;
        lockedAt: string | null;
    } | null;
}

export interface UseCheckoutFlowActions {
    /** Initiate checkout and transition to edit mode */
    checkout: () => Promise<CheckoutResponse | null>;
    /** Check in document with optional comment */
    checkIn: (comment?: string) => Promise<boolean>;
    /** Discard changes and return to preview mode */
    discard: () => Promise<boolean>;
    /** Clear any error state */
    clearError: () => void;
    /** Reset to preview mode (e.g., after external refresh) */
    resetToPreview: () => void;
    /** Update checkout status (from preview hook) */
    updateCheckoutStatus: (status: CheckoutStatus | null) => void;
}

export interface UseCheckoutFlowResult extends UseCheckoutFlowState, UseCheckoutFlowActions {}

export interface UseCheckoutFlowOptions {
    documentId: string | null | undefined;
    bffApiUrl: string;
    accessToken: string;
    correlationId: string;
    /** Callback when checkout completes successfully */
    onCheckoutSuccess?: (response: CheckoutResponse) => void;
    /** Callback when check-in completes successfully */
    onCheckInSuccess?: () => void;
    /** Callback when discard completes successfully */
    onDiscardSuccess?: () => void;
}

/**
 * Hook for managing checkout/checkin workflow
 */
export function useCheckoutFlow({
    documentId,
    bffApiUrl,
    accessToken,
    correlationId,
    onCheckoutSuccess,
    onCheckInSuccess,
    onDiscardSuccess
}: UseCheckoutFlowOptions): UseCheckoutFlowResult {

    // State
    const [viewMode, setViewMode] = useState<ViewMode>(ViewMode.Preview);
    const [editUrl, setEditUrl] = useState<string | null>(null);
    const [isCheckoutLoading, setIsCheckoutLoading] = useState(false);
    const [isCheckInLoading, setIsCheckInLoading] = useState(false);
    const [isDiscardLoading, setIsDiscardLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [lockError, setLockError] = useState<{
        lockedByName: string;
        lockedAt: string | null;
    } | null>(null);

    // BFF client reference
    const bffClient = useRef(new BffClient(bffApiUrl));

    // Update client if URL changes
    if (bffClient.current['baseUrl'] !== bffApiUrl.replace(/\/$/, '')) {
        bffClient.current = new BffClient(bffApiUrl);
    }

    /**
     * Checkout document and get edit URL
     */
    const checkout = useCallback(async (): Promise<CheckoutResponse | null> => {
        if (!documentId) {
            setError('No document ID provided');
            return null;
        }

        setIsCheckoutLoading(true);
        setError(null);
        setLockError(null);

        console.log('[useCheckoutFlow] Starting checkout for document:', documentId);

        try {
            const response = await bffClient.current.checkout(
                documentId,
                accessToken,
                correlationId
            );

            console.log('[useCheckoutFlow] Checkout successful:', response);

            // Update state
            setEditUrl(response.editUrl);
            setViewMode(ViewMode.Edit);

            // Callback
            onCheckoutSuccess?.(response);

            return response;

        } catch (err) {
            console.error('[useCheckoutFlow] Checkout failed:', err);

            if (err instanceof DocumentLockedException) {
                // Handle 409 Conflict
                const lockedBy = err.lockedError.checkedOutBy;
                setLockError({
                    lockedByName: lockedBy.name,
                    lockedAt: err.lockedError.checkedOutAt
                });
                setError(`Document is locked by ${lockedBy.name}`);
            } else if (err instanceof Error) {
                setError(err.message);
            } else {
                setError('Checkout failed');
            }

            return null;

        } finally {
            setIsCheckoutLoading(false);
        }
    }, [documentId, accessToken, correlationId, onCheckoutSuccess]);

    /**
     * Check in document with optional comment
     */
    const checkIn = useCallback(async (comment?: string): Promise<boolean> => {
        if (!documentId) {
            setError('No document ID provided');
            return false;
        }

        setIsCheckInLoading(true);
        setViewMode(ViewMode.Processing);
        setError(null);

        console.log('[useCheckoutFlow] Starting check-in for document:', documentId, 'comment:', comment);

        try {
            const response = await bffClient.current.checkIn(
                documentId,
                accessToken,
                correlationId,
                comment
            );

            console.log('[useCheckoutFlow] Check-in successful:', response);

            // Reset to preview mode
            setEditUrl(null);
            setViewMode(ViewMode.Preview);

            // Callback
            onCheckInSuccess?.();

            return true;

        } catch (err) {
            console.error('[useCheckoutFlow] Check-in failed:', err);

            // Revert to edit mode on failure
            setViewMode(ViewMode.Edit);

            if (err instanceof Error) {
                setError(err.message);
            } else {
                setError('Check-in failed');
            }

            return false;

        } finally {
            setIsCheckInLoading(false);
        }
    }, [documentId, accessToken, correlationId, onCheckInSuccess]);

    /**
     * Discard changes and release lock
     */
    const discard = useCallback(async (): Promise<boolean> => {
        if (!documentId) {
            setError('No document ID provided');
            return false;
        }

        setIsDiscardLoading(true);
        setError(null);

        console.log('[useCheckoutFlow] Starting discard for document:', documentId);

        try {
            const response = await bffClient.current.discard(
                documentId,
                accessToken,
                correlationId
            );

            console.log('[useCheckoutFlow] Discard successful:', response);

            // Reset to preview mode
            setEditUrl(null);
            setViewMode(ViewMode.Preview);

            // Callback
            onDiscardSuccess?.();

            return true;

        } catch (err) {
            console.error('[useCheckoutFlow] Discard failed:', err);

            if (err instanceof Error) {
                setError(err.message);
            } else {
                setError('Discard failed');
            }

            return false;

        } finally {
            setIsDiscardLoading(false);
        }
    }, [documentId, accessToken, correlationId, onDiscardSuccess]);

    /**
     * Clear error state
     */
    const clearError = useCallback(() => {
        setError(null);
        setLockError(null);
    }, []);

    /**
     * Reset to preview mode
     */
    const resetToPreview = useCallback(() => {
        setViewMode(ViewMode.Preview);
        setEditUrl(null);
        setError(null);
        setLockError(null);
    }, []);

    /**
     * Update checkout status from preview hook
     * (Used to sync state when document status changes externally)
     */
    const updateCheckoutStatus = useCallback((status: CheckoutStatus | null) => {
        // If we're in edit mode but document is no longer checked out by us,
        // transition back to preview
        if (viewMode === ViewMode.Edit && status && !status.isCurrentUser && status.isCheckedOut) {
            console.log('[useCheckoutFlow] Checkout lost to another user, reverting to preview');
            setViewMode(ViewMode.Preview);
            setEditUrl(null);
        }
    }, [viewMode]);

    return {
        // State
        viewMode,
        editUrl,
        isCheckoutLoading,
        isCheckInLoading,
        isDiscardLoading,
        error,
        lockError,
        // Actions
        checkout,
        checkIn,
        discard,
        clearError,
        resetToPreview,
        updateCheckoutStatus
    };
}
