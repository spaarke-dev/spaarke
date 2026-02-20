/**
 * useDirtyFields - Generic hook for tracking field changes and building PATCH payloads
 *
 * Compares current field values against original values to determine which
 * fields have been modified. Returns only the dirty fields for efficient
 * PATCH-style updates via Xrm.WebApi.
 *
 * @see ADR-012 - Shared Component Library
 * @see Task 109 - Extract useDirtyFields to shared hooks
 *
 * @example
 * ```tsx
 * const { dirtyFields, isDirty, resetOriginal } = useDirtyFields(
 *   originalRecord,
 *   currentValues,
 *   ["sprk_eventname", "sprk_duedate", "sprk_priority"]
 * );
 *
 * if (isDirty) {
 *   await webApi.updateRecord(entityName, recordId, dirtyFields);
 *   resetOriginal(currentValues);
 * }
 * ```
 */

import * as React from "react";

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Map of field names to their changed values
 */
export type DirtyFieldMap = Record<string, unknown>;

/**
 * Result of the useDirtyFields hook
 */
export interface UseDirtyFieldsResult {
  /** Map of dirty field names to their new values */
  dirtyFields: DirtyFieldMap;
  /** Whether any fields have been changed */
  isDirty: boolean;
  /** Count of dirty fields */
  dirtyCount: number;
  /** List of dirty field names */
  dirtyFieldNames: string[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Utility Functions
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Compare original and current values to find changed fields.
 * Handles null/undefined equivalence (treats both as "no value").
 *
 * @param original - Original record values (from server)
 * @param current - Current edited values
 * @param editableFields - List of field names to track
 * @returns Map of dirty fields with their new values
 */
export function computeDirtyFields(
  original: Record<string, unknown> | null,
  current: Record<string, unknown>,
  editableFields: string[]
): DirtyFieldMap {
  if (!original) return {};

  const dirty: DirtyFieldMap = {};

  for (const field of editableFields) {
    if (field in current) {
      const originalValue = original[field];
      const currentValue = current[field];

      // Normalize null/undefined to undefined for comparison
      const origNorm = originalValue === null ? undefined : originalValue;
      const currNorm = currentValue === null ? undefined : currentValue;

      if (origNorm !== currNorm) {
        dirty[field] = currentValue;
      }
    }
  }

  return dirty;
}

/**
 * Check if a dirty field map has any entries
 */
export function hasDirtyFields(dirtyFields: DirtyFieldMap): boolean {
  return Object.keys(dirtyFields).length > 0;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook Implementation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Track field changes between original and current values.
 *
 * @param original - Original record from server (null while loading)
 * @param current - Current edited values
 * @param editableFields - Field names to track for changes
 * @returns Dirty field state and helpers
 */
export function useDirtyFields(
  original: Record<string, unknown> | null,
  current: Record<string, unknown>,
  editableFields: string[]
): UseDirtyFieldsResult {
  const dirtyFields = React.useMemo(
    () => computeDirtyFields(original, current, editableFields),
    [original, current, editableFields]
  );

  const isDirty = React.useMemo(
    () => hasDirtyFields(dirtyFields),
    [dirtyFields]
  );

  const dirtyFieldNames = React.useMemo(
    () => Object.keys(dirtyFields),
    [dirtyFields]
  );

  return {
    dirtyFields,
    isDirty,
    dirtyCount: dirtyFieldNames.length,
    dirtyFieldNames,
  };
}
