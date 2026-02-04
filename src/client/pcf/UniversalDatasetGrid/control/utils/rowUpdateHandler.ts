/**
 * Row Update Handler - Optimistic Update Utility (Task 015)
 *
 * Provides optimistic row update functionality for the UniversalDatasetGrid.
 * Allows Side Pane to update individual grid rows without full dataset refresh.
 *
 * Features:
 * - Single row update without full grid refresh
 * - Preserves scroll position
 * - Preserves selection state
 * - Automatic rollback on error
 */

import {
    OptimisticRowUpdateRequest,
    OptimisticUpdateResult,
    RowFieldUpdate
} from '../types';
import { logger } from './logger';

/**
 * Stored row state for rollback functionality.
 * Captures field values before optimistic update.
 */
interface RowSnapshot {
    recordId: string;
    previousValues: RowFieldUpdate[];
    timestamp: number;
}

/**
 * In-memory store for row snapshots.
 * Enables rollback after failed saves.
 */
const rowSnapshots: Map<string, RowSnapshot> = new Map();

/**
 * Maximum age for snapshots (5 minutes).
 * Prevents memory leaks from orphaned snapshots.
 */
const SNAPSHOT_MAX_AGE_MS = 5 * 60 * 1000;

/**
 * Clean up old snapshots to prevent memory leaks.
 */
function cleanupOldSnapshots(): void {
    const now = Date.now();
    for (const [recordId, snapshot] of rowSnapshots.entries()) {
        if (now - snapshot.timestamp > SNAPSHOT_MAX_AGE_MS) {
            rowSnapshots.delete(recordId);
            logger.debug('RowUpdateHandler', `Cleaned up old snapshot for record ${recordId}`);
        }
    }
}

/**
 * Create a snapshot of the row's current state before updating.
 *
 * @param recordId - Record ID to snapshot
 * @param currentValues - Current field values to preserve
 */
export function createRowSnapshot(
    recordId: string,
    currentValues: RowFieldUpdate[]
): void {
    // Clean up any old snapshots first
    cleanupOldSnapshots();

    rowSnapshots.set(recordId, {
        recordId,
        previousValues: currentValues,
        timestamp: Date.now()
    });

    logger.debug('RowUpdateHandler', `Created snapshot for record ${recordId}`, {
        fieldCount: currentValues.length
    });
}

/**
 * Get the snapshot for a record (for rollback).
 *
 * @param recordId - Record ID to get snapshot for
 * @returns Snapshot if exists, undefined otherwise
 */
export function getRowSnapshot(recordId: string): RowSnapshot | undefined {
    return rowSnapshots.get(recordId);
}

/**
 * Clear the snapshot for a record (after successful commit).
 *
 * @param recordId - Record ID to clear snapshot for
 */
export function clearRowSnapshot(recordId: string): void {
    rowSnapshots.delete(recordId);
    logger.debug('RowUpdateHandler', `Cleared snapshot for record ${recordId}`);
}

/**
 * Validate an optimistic update request.
 *
 * @param request - The update request to validate
 * @returns Error message if invalid, null if valid
 */
export function validateUpdateRequest(
    request: OptimisticRowUpdateRequest
): string | null {
    if (!request) {
        return 'Update request is required';
    }

    if (!request.recordId || typeof request.recordId !== 'string') {
        return 'recordId is required and must be a string';
    }

    // Validate GUID format (loose check)
    const guidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    if (!guidRegex.test(request.recordId)) {
        return `Invalid recordId format: ${request.recordId}`;
    }

    if (!request.updates || !Array.isArray(request.updates)) {
        return 'updates array is required';
    }

    if (request.updates.length === 0) {
        return 'updates array must not be empty';
    }

    for (const update of request.updates) {
        if (!update.fieldName || typeof update.fieldName !== 'string') {
            return 'Each update must have a fieldName string';
        }

        if (typeof update.formattedValue !== 'string') {
            return `formattedValue for ${update.fieldName} must be a string`;
        }
    }

    return null;
}

/**
 * Create a no-op rollback function.
 * Used when creating rollback is not possible.
 */
export function createNoOpRollback(): () => void {
    return () => {
        logger.warn('RowUpdateHandler', 'No-op rollback called - no previous state available');
    };
}

/**
 * Create an error result for optimistic updates.
 *
 * @param error - Error message
 * @returns OptimisticUpdateResult with error
 */
export function createErrorResult(error: string): OptimisticUpdateResult {
    return {
        success: false,
        error,
        rollback: createNoOpRollback()
    };
}

/**
 * Create a success result for optimistic updates.
 *
 * @param rollbackFn - Function to rollback the update
 * @returns OptimisticUpdateResult indicating success
 */
export function createSuccessResult(rollbackFn: () => void): OptimisticUpdateResult {
    return {
        success: true,
        rollback: rollbackFn
    };
}

/**
 * Apply field updates to a row object.
 * Returns the previous values for rollback.
 *
 * @param row - Row object to update
 * @param updates - Field updates to apply
 * @returns Previous values before update
 */
export function applyFieldUpdates(
    row: Record<string, unknown>,
    updates: RowFieldUpdate[]
): RowFieldUpdate[] {
    const previousValues: RowFieldUpdate[] = [];

    for (const update of updates) {
        // Capture previous value
        const previousValue = row[update.fieldName];
        previousValues.push({
            fieldName: update.fieldName,
            formattedValue: typeof previousValue === 'string' ? previousValue : String(previousValue ?? ''),
            rawValue: previousValue
        });

        // Apply new value
        row[update.fieldName] = update.formattedValue;

        // If rawValue provided and this is a lookup field, update the _lookupIds too
        if (update.rawValue !== undefined && row._lookupIds) {
            const lookupIds = row._lookupIds as Record<string, string | undefined>;
            if (typeof update.rawValue === 'string') {
                lookupIds[update.fieldName] = update.rawValue;
            }
        }
    }

    return previousValues;
}

/**
 * Log an optimistic update for debugging.
 *
 * @param recordId - Record ID being updated
 * @param updates - Updates being applied
 */
export function logOptimisticUpdate(
    recordId: string,
    updates: RowFieldUpdate[]
): void {
    logger.info('RowUpdateHandler', `Optimistic update for record ${recordId}`, {
        fieldCount: updates.length,
        fields: updates.map(u => u.fieldName)
    });
}
