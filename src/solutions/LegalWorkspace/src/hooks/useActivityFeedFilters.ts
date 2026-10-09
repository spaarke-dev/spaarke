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
import { isFeedEventOverdue } from '../components/ActivityFeed/feedDueAccent';

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
   * HighPriority mirrors the server OData predicate in queryHelpers.ts; Overdue
   * uses the feed's local-day rule (see `isOverdue`), not the server's UTC filter.
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
 * Compute whether a single event matches the Overdue category: the feed's
 * shared rule (`isFeedEventOverdue` — due before today, calendar days, LOCAL
 * time), so the badge count, the client filter and the card accent agree
 * (task 081 round 4; this parsed a DateOnly due date as UTC midnight).
 *
 * It does NOT fully mirror the server OData filter `buildEventCategoryFilter(Overdue)`
 * in `services/queryHelpers.ts` (`sprk_duedate lt <local today yyyy-MM-dd> and
 * statuscode eq 1`): that one uses the same local-day boundary (task 106) but also
 * requires an open status. This client rule counts every loaded event by due date only.
 */
function isOverdue(event: IEvent): boolean {
  return isFeedEventOverdue(event.sprk_duedate);
}

/**
 * Compute per-category counts from the full event list. HighPriority mirrors
 * the OData filter in queryHelpers.ts; Overdue uses the feed's local-day rule
 * (see `isOverdue` above for how that differs from the server filter).
 *
 * @internal exported for the cross-surface overdue test.
 */
export function computeCategoryCounts(events: IEvent[]): CategoryCounts {
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
