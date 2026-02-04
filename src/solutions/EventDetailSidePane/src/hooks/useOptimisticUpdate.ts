/**
 * useOptimisticUpdate - Optimistic UI updates with error rollback
 *
 * Manages optimistic updates for the EventDetailSidePane:
 * - Stores original values before editing
 * - On save success, notifies grid to update the row
 * - On save error, offers rollback to original values
 * - Preserves user's unsaved changes during rollback decision
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/041-add-optimistic-ui.poml
 * @see ADR-021 - Fluent UI v9, dark mode support
 */

import * as React from "react";
import { IEventRecord } from "../types/EventRecord";
import { DirtyFields } from "../services/eventService";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Callback type for notifying the grid to update a row
 * The grid can use this to refresh its display without a full reload
 */
export type GridUpdateCallback = (
  eventId: string,
  updatedFields: Partial<IEventRecord>
) => void;

/**
 * Error state with rollback options
 */
export interface OptimisticErrorState {
  /** Error message from failed save */
  error: string;
  /** Fields that failed to save (for retry) */
  failedFields: DirtyFields;
  /** Original values before the failed changes (for rollback) */
  rollbackValues: Partial<IEventRecord>;
  /** Current values at time of error (user's changes) */
  currentValuesAtError: Partial<IEventRecord>;
}

/**
 * Return type for useOptimisticUpdate hook
 */
export interface UseOptimisticUpdateResult {
  /** Original event loaded from server (snapshot before edits) */
  originalEvent: IEventRecord | null;
  /** Set original event when loaded from server */
  setOriginalEvent: (event: IEventRecord) => void;
  /** Current error state if save failed (null if no error) */
  errorState: OptimisticErrorState | null;
  /** Handle save success - updates original and notifies grid */
  handleSaveSuccess: (
    savedFields: DirtyFields,
    currentValues: Partial<IEventRecord>
  ) => void;
  /** Handle save error - stores state for rollback decision */
  handleSaveError: (
    error: string,
    failedFields: DirtyFields,
    currentValues: Partial<IEventRecord>
  ) => void;
  /** Rollback to original values (discard changes) */
  rollbackToOriginal: () => Partial<IEventRecord>;
  /** Retry with current values (keep changes) */
  retryWithCurrentValues: () => DirtyFields;
  /** Dismiss error without action */
  dismissError: () => void;
  /** Register grid update callback */
  registerGridCallback: (callback: GridUpdateCallback | null) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Hook for managing optimistic UI updates with error rollback
 *
 * @param eventId - Current event ID for grid notifications
 * @returns OptimisticUpdate state and handlers
 */
export function useOptimisticUpdate(
  eventId: string | undefined
): UseOptimisticUpdateResult {
  // Original event values (snapshot when loaded)
  const [originalEvent, setOriginalEventInternal] =
    React.useState<IEventRecord | null>(null);

  // Error state for rollback decision
  const [errorState, setErrorState] =
    React.useState<OptimisticErrorState | null>(null);

  // Grid update callback (registered by parent)
  const gridCallbackRef = React.useRef<GridUpdateCallback | null>(null);

  /**
   * Set original event and clear any existing error state
   */
  const setOriginalEvent = React.useCallback((event: IEventRecord) => {
    setOriginalEventInternal(event);
    setErrorState(null);
  }, []);

  /**
   * Register a callback for grid updates
   * Called when save succeeds to notify the parent grid
   */
  const registerGridCallback = React.useCallback(
    (callback: GridUpdateCallback | null) => {
      gridCallbackRef.current = callback;
    },
    []
  );

  /**
   * Handle successful save
   * - Updates original event with saved values
   * - Notifies grid to update the row
   */
  const handleSaveSuccess = React.useCallback(
    (savedFields: DirtyFields, currentValues: Partial<IEventRecord>) => {
      // Update original event with saved values (clears dirty state)
      setOriginalEventInternal((prev) => {
        if (!prev) return null;
        const updated = { ...prev };
        for (const [field, value] of Object.entries(savedFields)) {
          (updated as Record<string, unknown>)[field] = value;
        }
        return updated;
      });

      // Clear any existing error state
      setErrorState(null);

      // Notify grid to update the row (optimistic UI)
      if (gridCallbackRef.current && eventId) {
        gridCallbackRef.current(eventId, currentValues);
        console.log("[OptimisticUpdate] Grid notified of update:", eventId);
      }
    },
    [eventId]
  );

  /**
   * Handle save error
   * - Stores error state for rollback decision
   * - Preserves user's current changes
   */
  const handleSaveError = React.useCallback(
    (
      error: string,
      failedFields: DirtyFields,
      currentValues: Partial<IEventRecord>
    ) => {
      // Build rollback values from original event
      const rollbackValues: Partial<IEventRecord> = {};
      if (originalEvent) {
        for (const field of Object.keys(failedFields)) {
          rollbackValues[field as keyof IEventRecord] =
            originalEvent[field as keyof IEventRecord];
        }
      }

      setErrorState({
        error,
        failedFields,
        rollbackValues,
        currentValuesAtError: { ...currentValues },
      });

      console.log("[OptimisticUpdate] Save failed, rollback available:", error);
    },
    [originalEvent]
  );

  /**
   * Rollback to original values (discard user changes)
   * Returns the original values to restore in the form
   */
  const rollbackToOriginal = React.useCallback((): Partial<IEventRecord> => {
    if (!errorState) {
      console.warn("[OptimisticUpdate] No error state to rollback from");
      return {};
    }

    const valuesToRestore = errorState.rollbackValues;

    // Clear error state
    setErrorState(null);

    console.log("[OptimisticUpdate] Rolling back to original values");
    return valuesToRestore;
  }, [errorState]);

  /**
   * Retry with current values (keep user changes)
   * Returns the dirty fields to retry saving
   */
  const retryWithCurrentValues = React.useCallback((): DirtyFields => {
    if (!errorState) {
      console.warn("[OptimisticUpdate] No error state to retry from");
      return {};
    }

    const fieldsToRetry = errorState.failedFields;

    // Clear error state (will be set again if retry fails)
    setErrorState(null);

    console.log("[OptimisticUpdate] Retrying with current values");
    return fieldsToRetry;
  }, [errorState]);

  /**
   * Dismiss error without action
   * User can continue editing and try save again manually
   */
  const dismissError = React.useCallback(() => {
    setErrorState(null);
    console.log("[OptimisticUpdate] Error dismissed, preserving current values");
  }, []);

  return {
    originalEvent,
    setOriginalEvent,
    errorState,
    handleSaveSuccess,
    handleSaveError,
    rollbackToOriginal,
    retryWithCurrentValues,
    dismissError,
    registerGridCallback,
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Helper Types for App Integration
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Props for components that can receive grid update notifications
 * Used by parent components (e.g., grid) that open the side pane
 */
export interface OptimisticGridProps {
  /** Callback to update a row in the grid when side pane saves */
  onRowUpdated?: GridUpdateCallback;
}
