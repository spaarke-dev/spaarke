/**
 * useKanbanColumns — Core data-management hook for the Smart To Do Kanban board.
 *
 * Takes items from useTodoItems and thresholds from useUserPreferences, then:
 *   1. Assigns each item to a column (Today / Tomorrow / Future) by To Do Score.
 *   2. Respects pinned items — pinned items keep their sprk_todocolumn.
 *   3. Provides moveItem() — moves an item to a target column + auto-pins + writes Dataverse.
 *   4. Provides togglePin() — flips pin state + writes Dataverse.
 *   5. Provides recalculate() — reassigns unpinned items by current scores + batch writes.
 *
 * All mutations use optimistic UI with rollback on Dataverse write failure.
 *
 * Usage:
 *   const { columns, moveItem, togglePin, recalculate, isRecalculating } =
 *     useKanbanColumns({ items, todayThreshold: 60, tomorrowThreshold: 30, webApi, userId });
 */

import { useState, useMemo, useCallback, useEffect, useRef } from 'react';
import { tokens } from '@fluentui/react-components';
import { DataverseService } from '../services/DataverseService';
import { computeTodoScore } from '../utils/todoScoreUtils';
import type { IEvent } from '../types/entities';
import type { TodoColumn } from '../types/enums';
import type { IKanbanColumn } from '../components/shared/KanbanBoard';
import type { IWebApi } from '../types/xrm';

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

/** Map TodoColumn string to Dataverse choice value. */
const COLUMN_TO_CHOICE: Record<TodoColumn, number> = {
  Today: 0,
  Tomorrow: 1,
  Future: 2,
};

/** Map Dataverse choice value to TodoColumn string. */
const CHOICE_TO_COLUMN: Record<number, TodoColumn> = {
  0: 'Today',
  1: 'Tomorrow',
  2: 'Future',
};

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface IUseKanbanColumnsOptions {
  /** Sorted todo items from useTodoItems. */
  items: IEvent[];
  /** Score threshold: items scoring at or above this go to "Today". */
  todayThreshold: number;
  /** Score threshold: items scoring at or above this (but below todayThreshold) go to "Tomorrow". */
  tomorrowThreshold: number;
  /** Xrm.WebApi reference. */
  webApi: IWebApi;
  /** Current user ID (for service calls). */
  userId: string;
}

export interface IUseKanbanColumnsResult {
  /** Three columns (Today, Tomorrow, Future) with items assigned. */
  columns: IKanbanColumn<IEvent>[];
  /** Move an item to a target column. Auto-pins and persists to Dataverse. */
  moveItem: (eventId: string, targetColumn: TodoColumn) => void;
  /** Toggle pin state for an item. Persists to Dataverse. */
  togglePin: (eventId: string) => void;
  /** Reassign all unpinned items by current scores. Batch-writes to Dataverse. */
  recalculate: () => void;
  /** True while a recalculate batch write is in progress. */
  isRecalculating: boolean;
}

// ---------------------------------------------------------------------------
// Pure helpers
// ---------------------------------------------------------------------------

/**
 * Determine which column an unpinned item belongs to based on its To Do Score.
 */
function assignColumnByScore(
  event: IEvent,
  todayThreshold: number,
  tomorrowThreshold: number
): TodoColumn {
  const { todoScore } = computeTodoScore(event);
  if (todoScore >= todayThreshold) return 'Today';
  if (todoScore >= tomorrowThreshold) return 'Tomorrow';
  return 'Future';
}

/**
 * Resolve the effective column for an item, respecting pin state.
 * - Pinned items: use their stored sprk_todocolumn (with fallback to score-based).
 * - Unpinned items: compute from To Do Score + thresholds.
 */
function resolveColumn(
  event: IEvent,
  todayThreshold: number,
  tomorrowThreshold: number
): TodoColumn {
  if (event.sprk_todopinned && event.sprk_todocolumn != null) {
    return CHOICE_TO_COLUMN[event.sprk_todocolumn] ?? assignColumnByScore(event, todayThreshold, tomorrowThreshold);
  }
  return assignColumnByScore(event, todayThreshold, tomorrowThreshold);
}

/**
 * Build the three-column structure from a flat item list.
 * Preserves item order (items arrive pre-sorted by To Do Score from useTodoItems).
 */
function buildColumns(
  items: IEvent[],
  todayThreshold: number,
  tomorrowThreshold: number
): IKanbanColumn<IEvent>[] {
  const today: IEvent[] = [];
  const tomorrow: IEvent[] = [];
  const future: IEvent[] = [];

  for (const item of items) {
    const col = resolveColumn(item, todayThreshold, tomorrowThreshold);
    if (col === 'Today') today.push(item);
    else if (col === 'Tomorrow') tomorrow.push(item);
    else future.push(item);
  }

  return [
    { id: 'Today', title: 'Today', items: today, accentColor: tokens.colorPaletteRedBorder2 },
    { id: 'Tomorrow', title: 'Tomorrow', items: tomorrow, accentColor: tokens.colorPaletteDarkOrangeBorder2 },
    { id: 'Future', title: 'Future', items: future, accentColor: tokens.colorPaletteGreenBorder2 },
  ];
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useKanbanColumns(options: IUseKanbanColumnsOptions): IUseKanbanColumnsResult {
  const { items, todayThreshold, tomorrowThreshold, webApi, userId } = options;

  const [isRecalculating, setIsRecalculating] = useState(false);

  // Local overrides for optimistic column/pin mutations.
  // Key: eventId, Value: { column, pinned } overrides.
  const [overrides, setOverrides] = useState<
    Record<string, { column?: number; pinned?: boolean }>
  >({});

  // Stable service reference
  const serviceRef = useRef<DataverseService>(new DataverseService(webApi));
  useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  // Clear overrides when the underlying items change (e.g., refetch) so
  // we pick up the persisted Dataverse values.
  const prevItemsRef = useRef(items);
  useEffect(() => {
    if (prevItemsRef.current !== items) {
      prevItemsRef.current = items;
      setOverrides({});
    }
  }, [items]);

  // -------------------------------------------------------------------------
  // Apply overrides to items for column computation
  // -------------------------------------------------------------------------

  const effectiveItems = useMemo(() => {
    if (Object.keys(overrides).length === 0) return items;

    return items.map((item) => {
      const ov = overrides[item.sprk_eventid];
      if (!ov) return item;
      return {
        ...item,
        ...(ov.column != null ? { sprk_todocolumn: ov.column } : {}),
        ...(ov.pinned != null ? { sprk_todopinned: ov.pinned } : {}),
      };
    });
  }, [items, overrides]);

  // -------------------------------------------------------------------------
  // Derive columns
  // -------------------------------------------------------------------------

  const columns = useMemo(
    () => buildColumns(effectiveItems, todayThreshold, tomorrowThreshold),
    [effectiveItems, todayThreshold, tomorrowThreshold]
  );

  // -------------------------------------------------------------------------
  // moveItem — optimistic column change + auto-pin + Dataverse write
  // -------------------------------------------------------------------------

  const moveItem = useCallback(
    (eventId: string, targetColumn: TodoColumn) => {
      const choiceValue = COLUMN_TO_CHOICE[targetColumn];

      // Optimistic: set column + pin
      setOverrides((prev) => ({
        ...prev,
        [eventId]: { column: choiceValue, pinned: true },
      }));

      // Persist both column and pin in parallel
      const service = serviceRef.current;
      Promise.all([
        service.updateEventColumn(eventId, choiceValue),
        service.updateEventPinned(eventId, true),
      ]).then(([colResult, pinResult]) => {
        if (!colResult.success || !pinResult.success) {
          console.error('[useKanbanColumns] moveItem write failed, rolling back', {
            colResult,
            pinResult,
          });
          // Rollback: remove overrides so item returns to its data-driven position
          setOverrides((prev) => {
            const next = { ...prev };
            delete next[eventId];
            return next;
          });
        }
      });
    },
    []
  );

  // -------------------------------------------------------------------------
  // togglePin — optimistic pin flip + Dataverse write with rollback
  // -------------------------------------------------------------------------

  const togglePin = useCallback(
    (eventId: string) => {
      // Find current effective pin state
      const item = effectiveItems.find((i) => i.sprk_eventid === eventId);
      if (!item) return;

      const currentPinned = item.sprk_todopinned ?? false;
      const newPinned = !currentPinned;

      // Optimistic: flip pin (keep existing column override if any)
      setOverrides((prev) => {
        const existing = prev[eventId] ?? {};
        return {
          ...prev,
          [eventId]: { ...existing, pinned: newPinned },
        };
      });

      // Persist
      serviceRef.current.updateEventPinned(eventId, newPinned).then((result) => {
        if (!result.success) {
          console.error('[useKanbanColumns] togglePin write failed, rolling back', result);
          // Rollback pin to previous state
          setOverrides((prev) => {
            const existing = prev[eventId];
            if (!existing) return prev;
            // Remove the pin override; keep column if set
            const { pinned: _removed, ...rest } = existing;
            if (Object.keys(rest).length === 0) {
              const next = { ...prev };
              delete next[eventId];
              return next;
            }
            return { ...prev, [eventId]: rest };
          });
        }
      });
    },
    [effectiveItems]
  );

  // -------------------------------------------------------------------------
  // recalculate — reassign all unpinned items + batch Dataverse write
  // -------------------------------------------------------------------------

  const recalculate = useCallback(() => {
    setIsRecalculating(true);

    // Build batch of column updates for unpinned items whose computed column
    // differs from their current stored column.
    const updates: Array<{ eventId: string; column: number }> = [];

    for (const item of effectiveItems) {
      // Skip pinned items
      if (item.sprk_todopinned) continue;

      const computedColumn = assignColumnByScore(item, todayThreshold, tomorrowThreshold);
      const computedChoice = COLUMN_TO_CHOICE[computedColumn];
      const currentChoice = item.sprk_todocolumn;

      if (currentChoice !== computedChoice) {
        updates.push({ eventId: item.sprk_eventid, column: computedChoice });
      }
    }

    if (updates.length === 0) {
      setIsRecalculating(false);
      return;
    }

    // Optimistic: apply all column changes locally
    setOverrides((prev) => {
      const next = { ...prev };
      for (const u of updates) {
        const existing = next[u.eventId] ?? {};
        next[u.eventId] = { ...existing, column: u.column };
      }
      return next;
    });

    // Batch write to Dataverse
    serviceRef.current.batchUpdateEventColumns(updates).then((result) => {
      if (!result.success) {
        console.error('[useKanbanColumns] recalculate batch write failed, rolling back', result);
        // Rollback all column overrides from this batch
        setOverrides((prev) => {
          const next = { ...prev };
          for (const u of updates) {
            const existing = next[u.eventId];
            if (!existing) continue;
            const { column: _removed, ...rest } = existing;
            if (Object.keys(rest).length === 0) {
              delete next[u.eventId];
            } else {
              next[u.eventId] = rest;
            }
          }
          return next;
        });
      }
      setIsRecalculating(false);
    });
  }, [effectiveItems, todayThreshold, tomorrowThreshold]);

  return { columns, moveItem, togglePin, recalculate, isRecalculating };
}
