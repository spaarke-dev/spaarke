/**
 * useActivityFeedFilters — filter state management and per-category count logic
 * for the Updates Feed (Block 3) filter bar.
 *
 * Responsibilities:
 *   1. Track the currently active EventFilterCategory pill.
 *   2. Compute per-category counts from the full (All) event list so every pill
 *      badge shows a live count without requiring separate Dataverse queries.
 *   3. Expose setFilter so FilterBar can update state.
 *
 * The count computation runs client-side on the full All-filter result set.
 * This avoids 8 separate Dataverse round-trips for the badge counts — the
 * parent fetches All events once, and we derive badge numbers from that list.
 *
 * Usage:
 *   const { activeFilter, setFilter, categoryCounts } = useActivityFeedFilters({
 *     allEvents: eventsFromUseEvents,
 *   });
 */

import { useState, useMemo, useCallback } from 'react';
import { IEvent } from '../types/entities';
import { EventFilterCategory } from '../types/enums';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/** Per-category item count displayed in each pill badge */
export type CategoryCounts = Record<EventFilterCategory, number>;

export interface IUseActivityFeedFiltersOptions {
  /**
   * The complete unfiltered event list (fetched with EventFilterCategory.All).
   * Counts for all 8 categories are derived from this list client-side.
   */
  allEvents: IEvent[];
}

export interface IUseActivityFeedFiltersResult {
  /** Currently active filter pill */
  activeFilter: EventFilterCategory;
  /** Change the active filter */
  setFilter: (filter: EventFilterCategory) => void;
  /**
   * Per-category counts derived from allEvents.
   * These mirror the server-side OData predicates in queryHelpers.ts.
   */
  categoryCounts: CategoryCounts;
}

// ---------------------------------------------------------------------------
// Count computation helpers
// ---------------------------------------------------------------------------

/**
 * Compute whether a single event matches the HighPriority category.
 * Mirrors: buildEventCategoryFilter(HighPriority) → priorityscore gt 70
 */
function isHighPriority(event: IEvent): boolean {
  return (event.sprk_priorityscore ?? 0) > 70;
}

/**
 * Compute whether a single event matches the Overdue category.
 * Mirrors: buildEventCategoryFilter(Overdue) → duedate lt today
 */
function isOverdue(event: IEvent): boolean {
  if (!event.sprk_duedate) return false;
  const dueDate = new Date(event.sprk_duedate);
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  return dueDate < today;
}

/**
 * Compute per-category counts from the full event list.
 * Predicates intentionally mirror the OData filters in queryHelpers.ts so
 * badge counts match what the server would return for each filter.
 */
function computeCategoryCounts(events: IEvent[]): CategoryCounts {
  const counts: CategoryCounts = {
    [EventFilterCategory.All]: events.length,
    [EventFilterCategory.HighPriority]: 0,
    [EventFilterCategory.Overdue]: 0,
    [EventFilterCategory.Alerts]: 0,
    [EventFilterCategory.Emails]: 0,
    [EventFilterCategory.Documents]: 0,
    [EventFilterCategory.Invoices]: 0,
    [EventFilterCategory.Tasks]: 0,
  };

  for (const event of events) {
    const type = (event.eventTypeName ?? '').toLowerCase();

    if (isHighPriority(event)) counts[EventFilterCategory.HighPriority]++;
    if (isOverdue(event)) counts[EventFilterCategory.Overdue]++;

    // Type-based categories — match sprk_eventtype_ref display names (lowercased)
    if (type === 'notification' || type === 'status change' || type === 'reminder') {
      counts[EventFilterCategory.Alerts]++;
    }
    if (type === 'communication') {
      counts[EventFilterCategory.Emails]++;
    }
    if (type === 'filing') {
      counts[EventFilterCategory.Documents]++;
    }
    if (type === 'approval') {
      counts[EventFilterCategory.Invoices]++;
    }
    if (type === 'task' || type === 'to do' || type === 'action' || type === 'deadline') {
      counts[EventFilterCategory.Tasks]++;
    }
  }

  return counts;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useActivityFeedFilters(
  options: IUseActivityFeedFiltersOptions
): IUseActivityFeedFiltersResult {
  const { allEvents } = options;

  const [activeFilter, setActiveFilterState] = useState<EventFilterCategory>(
    EventFilterCategory.All
  );

  const setFilter = useCallback((filter: EventFilterCategory) => {
    setActiveFilterState(filter);
  }, []);

  // Memoize so counts only recompute when the event list changes
  const categoryCounts = useMemo(
    () => computeCategoryCounts(allEvents),
    [allEvents]
  );

  return {
    activeFilter,
    setFilter,
    categoryCounts,
  };
}
