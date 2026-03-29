/**
 * useTodoItems — React hook for fetching and managing Smart To Do list items.
 *
 * Queries sprk_event records where sprk_todoflag=true AND sprk_todostatus != Dismissed.
 * Integrates with FeedTodoSyncContext so that flag toggles from the Updates Feed
 * (Block 3) are reflected in real time without a page refresh.
 *
 * Sort order (FR-07):
 *   1. sprk_priorityscore DESC (highest priority first)
 *   2. sprk_duedate ASC (earliest due date first within same priority)
 *
 * FeedTodoSyncContext integration:
 *   - On mount, subscribes to flag change notifications.
 *   - When an event is flagged (true), fetches its full details and inserts
 *     it into the local list, then re-sorts.
 *   - When an event is unflagged (false), removes it from the local list.
 *   - This gives instant UI feedback without waiting for a full re-fetch.
 *
 * Usage:
 *   const { items, isLoading, error, refetch } = useTodoItems({ webApi, userId });
 */

import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { DataverseService } from '../services/DataverseService';
import { IEvent } from '../types/entities';
import { useFeedTodoSync } from './useFeedTodoSync';
import { computeTodoScore } from '../utils/todoScoreUtils';
import type { IWebApi } from '../types/xrm';

// ---------------------------------------------------------------------------
// Sort helper
// ---------------------------------------------------------------------------

/**
 * Sort todo items by To Do Score DESC, then due date ASC as tiebreaker.
 *
 * To Do Score combines priority (50%), inverted effort (20%), and due-date
 * urgency (30%) into a single 0-100 composite. Higher scores surface the
 * most important, time-sensitive, and achievable items first.
 *
 * Returns a NEW array — does not mutate the original.
 */
function sortTodoItems(items: IEvent[]): IEvent[] {
  return [...items].sort((a, b) => {
    // Primary: To Do Score DESC (higher is more important)
    const scoreA = computeTodoScore(a).todoScore;
    const scoreB = computeTodoScore(b).todoScore;
    const scoreDiff = scoreB - scoreA;
    if (scoreDiff !== 0) return scoreDiff;

    // Tiebreaker: duedate ASC (earlier is more urgent)
    const dueDateA = a.sprk_duedate ? new Date(a.sprk_duedate).getTime() : Infinity;
    const dueDateB = b.sprk_duedate ? new Date(b.sprk_duedate).getTime() : Infinity;
    return dueDateA - dueDateB;
  });
}

// ---------------------------------------------------------------------------
// Hook options and result types
// ---------------------------------------------------------------------------

export interface IUseTodoItemsOptions {
  /** Xrm.WebApi reference from the PCF framework context */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
  /**
   * Optional mock items for local development / testing.
   * When provided, bypasses Xrm.WebApi.
   */
  mockItems?: IEvent[];
}

export interface IUseTodoItemsResult {
  /** Sorted active to-do items (excludes Dismissed) */
  items: IEvent[];
  /** True while the initial fetch is in progress */
  isLoading: boolean;
  /** User-friendly error message, null when healthy */
  error: string | null;
  /** Trigger a fresh fetch from Dataverse, bypassing any cached state */
  refetch: () => void;
}

// ---------------------------------------------------------------------------
// Hook implementation
// ---------------------------------------------------------------------------

export function useTodoItems(options: IUseTodoItemsOptions): IUseTodoItemsResult {
  const { webApi, userId, mockItems } = options;

  const [items, setItems] = useState<IEvent[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [fetchKey, setFetchKey] = useState<number>(0);

  // Stable service reference — only recreates when webApi changes
  const serviceRef = useRef<DataverseService>(new DataverseService(webApi));
  useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  // Keep items ref for synchronous reads inside the subscribe callback
  const itemsRef = useRef<IEvent[]>([]);
  useEffect(() => {
    itemsRef.current = items;
  }, [items]);

  const refetch = useCallback(() => {
    setFetchKey((k) => k + 1);
  }, []);

  // -------------------------------------------------------------------------
  // Initial / refresh data fetch
  // -------------------------------------------------------------------------

  useEffect(() => {
    // Mock data path for development
    if (mockItems) {
      const sorted = sortTodoItems(mockItems);
      setItems(sorted);
      itemsRef.current = sorted;
      setIsLoading(false);
      setError(null);
      return;
    }

    // Guard: userId must be present
    if (!userId) {
      setIsLoading(false);
      setError(null);
      setItems([]);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);

    serviceRef.current
      .getActiveTodos(userId)
      .then((result) => {
        if (cancelled) return;

        if (!result.success) {
          setError(
            result.error?.message ?? 'Unable to load to-do items. Please try again.'
          );
          setIsLoading(false);
          return;
        }

        const sorted = sortTodoItems(result.data);
        setItems(sorted);
        itemsRef.current = sorted;
        setIsLoading(false);
        setError(null);
      })
      .catch((err: Error) => {
        if (cancelled) return;
        setError(err.message ?? 'An unexpected error occurred loading to-do items.');
        setIsLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [userId, mockItems, fetchKey]);

  // -------------------------------------------------------------------------
  // FeedTodoSyncContext subscription — react to cross-block flag changes
  // -------------------------------------------------------------------------

  const { subscribe } = useFeedTodoSync();

  useEffect(() => {
    const unsubscribe = subscribe(async (eventId: string, flagged: boolean) => {
      if (flagged) {
        // Event was flagged in the Updates Feed.
        // Check if it already exists in our list to avoid duplicates.
        const alreadyInList = itemsRef.current.some(
          (item) => item.sprk_eventid === eventId
        );
        if (alreadyInList) return;

        // Fetch full event details and add to list
        if (!userId) return;

        try {
          // We don't have a single-record helper, so fetch with an eventId filter.
          // Using retrieveMultipleRecords with a filter is the cleanest approach
          // with Xrm.WebApi (no retrieveRecord equivalent in ComponentFramework.WebApi
          // without the entity type — and we have it: sprk_event).
          const result = await serviceRef.current.getActiveTodos(userId);
          if (!result.success) return;

          // Find the newly flagged event in the refreshed list
          const newItem = result.data.find((e) => e.sprk_eventid === eventId);
          if (!newItem) return;

          setItems((prev) => {
            // Double-check for race condition: event might have been added already
            if (prev.some((item) => item.sprk_eventid === eventId)) return prev;
            return sortTodoItems([...prev, newItem]);
          });
        } catch {
          // Silently ignore — the item will appear on next manual refetch
        }
      } else {
        // Event was unflagged — remove from the to-do list immediately
        setItems((prev) => {
          const filtered = prev.filter((item) => item.sprk_eventid !== eventId);
          // Only update if something actually changed
          if (filtered.length === prev.length) return prev;
          return filtered;
        });
      }
    });

    return unsubscribe;
  }, [subscribe, userId]);

  // Memoize the returned items so that consumers receive a stable reference
  // when neither the list contents nor the loading/error state has changed.
  // This prevents cascading re-renders in SmartToDo when parent components
  // update for unrelated reasons (NFR-01: page load < 3s).
  const memoizedItems = useMemo(() => items, [items]);

  return { items: memoizedItems, isLoading, error, refetch };
}
