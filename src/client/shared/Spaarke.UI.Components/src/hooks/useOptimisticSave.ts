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

import * as React from "react";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Callback for notifying parent (e.g., grid) of updates
 */
export type RecordUpdateCallback = (
  recordId: string,
  updatedFields: Record<string, unknown>
) => void;

/**
 * Error state for save failure
 */
export interface SaveErrorState {
  /** Error message */
  message: string;
  /** Fields that failed to save */
  failedFields: Record<string, unknown>;
  /** Values at time of failure (for retry) */
  valuesAtFailure: Record<string, unknown>;
}

/**
 * Result of the useOptimisticSave hook
 */
export interface UseOptimisticSaveResult {
  /** Original record loaded from server (null before load) */
  originalRecord: Record<string, unknown> | null;
  /** Set the original record (called on initial load) */
  setOriginal: (record: Record<string, unknown>) => void;
  /** Handle successful save */
  handleSuccess: (
    savedFields: Record<string, unknown>,
    currentValues: Record<string, unknown>
  ) => void;
  /** Handle failed save */
  handleError: (
    errorMessage: string,
    failedFields: Record<string, unknown>,
    currentValues: Record<string, unknown>
  ) => void;
  /** Roll back to original values (returns the restored values) */
  rollback: () => Record<string, unknown>;
  /** Retry save with current values (returns the fields to retry) */
  retryFields: () => Record<string, unknown>;
  /** Dismiss error state */
  dismissError: () => void;
  /** Current error state (null if no error) */
  errorState: SaveErrorState | null;
  /** Register a callback for parent notifications */
  registerUpdateCallback: (callback: RecordUpdateCallback | null) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Optimistic save hook with rollback support.
 *
 * @param recordId - The record ID being edited (for callback notifications)
 * @returns Save lifecycle management functions
 */
export function useOptimisticSave(
  recordId: string | undefined
): UseOptimisticSaveResult {
  const [originalRecord, setOriginalRecord] = React.useState<Record<string, unknown> | null>(null);
  const [errorState, setErrorState] = React.useState<SaveErrorState | null>(null);
  const updateCallbackRef = React.useRef<RecordUpdateCallback | null>(null);

  const setOriginal = React.useCallback((record: Record<string, unknown>) => {
    setOriginalRecord({ ...record });
    setErrorState(null);
  }, []);

  const handleSuccess = React.useCallback(
    (
      savedFields: Record<string, unknown>,
      currentValues: Record<string, unknown>
    ) => {
      // Update original to reflect saved state
      setOriginalRecord((prev) => {
        if (!prev) return prev;
        return { ...prev, ...savedFields };
      });
      setErrorState(null);

      // Notify parent (e.g., grid row update)
      if (updateCallbackRef.current && recordId) {
        updateCallbackRef.current(recordId, currentValues);
      }
    },
    [recordId]
  );

  const handleError = React.useCallback(
    (
      errorMessage: string,
      failedFields: Record<string, unknown>,
      currentValues: Record<string, unknown>
    ) => {
      setErrorState({
        message: errorMessage,
        failedFields,
        valuesAtFailure: { ...currentValues },
      });
    },
    []
  );

  const rollback = React.useCallback((): Record<string, unknown> => {
    setErrorState(null);
    return originalRecord ? { ...originalRecord } : {};
  }, [originalRecord]);

  const retryFields = React.useCallback((): Record<string, unknown> => {
    if (!errorState) return {};
    setErrorState(null);
    return { ...errorState.failedFields };
  }, [errorState]);

  const dismissError = React.useCallback(() => {
    setErrorState(null);
  }, []);

  const registerUpdateCallback = React.useCallback(
    (callback: RecordUpdateCallback | null) => {
      updateCallbackRef.current = callback;
    },
    []
  );

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
