/**
 * useOptimisticSave - Generic hook for optimistic UI save with rollback
 *
 * Manages the save lifecycle for side pane editing:
 * 1. Store original record snapshot
 * 2. On save success: update original to match current values
 * 3. On save error: preserve user edits, offer Retry or Discard
 * 4. On discard: roll back to original values
 *
 * @see ADR-012 - Shared Component Library
 * @see Task 109 - Extract useOptimisticSave to shared hooks
 *
 * @example
 * ```tsx
 * const save = useOptimisticSave();
 *
 * // On record load
 * save.setOriginal(loadedRecord);
 *
 * // On successful save
 * save.handleSuccess(dirtyFields, currentValues);
 *
 * // On failed save
 * save.handleError("Permission denied", dirtyFields, currentValues);
 *
 * // On discard
 * const restoredValues = save.rollback();
 * ```
 */
import * as React from 'react';
// ─────────────────────────────────────────────────────────────────────────────
// Hook Implementation
// ─────────────────────────────────────────────────────────────────────────────
/**
 * Optimistic save hook with rollback support.
 *
 * @param recordId - The record ID being edited (for callback notifications)
 * @returns Save lifecycle management functions
 */
export function useOptimisticSave(recordId) {
    const [originalRecord, setOriginalRecord] = React.useState(null);
    const [errorState, setErrorState] = React.useState(null);
    const updateCallbackRef = React.useRef(null);
    const setOriginal = React.useCallback((record) => {
        setOriginalRecord({ ...record });
        setErrorState(null);
    }, []);
    const handleSuccess = React.useCallback((savedFields, currentValues) => {
        // Update original to reflect saved state
        setOriginalRecord(prev => {
            if (!prev)
                return prev;
            return { ...prev, ...savedFields };
        });
        setErrorState(null);
        // Notify parent (e.g., grid row update)
        if (updateCallbackRef.current && recordId) {
            updateCallbackRef.current(recordId, currentValues);
        }
    }, [recordId]);
    const handleError = React.useCallback((errorMessage, failedFields, currentValues) => {
        setErrorState({
            message: errorMessage,
            failedFields,
            valuesAtFailure: { ...currentValues },
        });
    }, []);
    const rollback = React.useCallback(() => {
        setErrorState(null);
        return originalRecord ? { ...originalRecord } : {};
    }, [originalRecord]);
    const retryFields = React.useCallback(() => {
        if (!errorState)
            return {};
        setErrorState(null);
        return { ...errorState.failedFields };
    }, [errorState]);
    const dismissError = React.useCallback(() => {
        setErrorState(null);
    }, []);
    const registerUpdateCallback = React.useCallback((callback) => {
        updateCallbackRef.current = callback;
    }, []);
    return {
        originalRecord,
        setOriginal,
        handleSuccess,
        handleError,
        rollback,
        retryFields,
        dismissError,
        errorState,
        registerUpdateCallback,
    };
}
//# sourceMappingURL=useOptimisticSave.js.map