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
/**
 * Callback for notifying parent (e.g., grid) of updates
 */
export type RecordUpdateCallback = (recordId: string, updatedFields: Record<string, unknown>) => void;
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
    handleSuccess: (savedFields: Record<string, unknown>, currentValues: Record<string, unknown>) => void;
    /** Handle failed save */
    handleError: (errorMessage: string, failedFields: Record<string, unknown>, currentValues: Record<string, unknown>) => void;
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
/**
 * Optimistic save hook with rollback support.
 *
 * @param recordId - The record ID being edited (for callback notifications)
 * @returns Save lifecycle management functions
 */
export declare function useOptimisticSave(recordId: string | undefined): UseOptimisticSaveResult;
//# sourceMappingURL=useOptimisticSave.d.ts.map