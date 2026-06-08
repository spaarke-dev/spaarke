/**
 * useTodoItems — React hook for fetching and managing Smart To Do list items.
 *
 * Currently queries sprk_event records (legacy event-based todo path); this
 * hook will be repointed at `sprk_todo` in a follow-up that mirrors task 020
 * (which already migrated the standalone SmartTodo Code Page). Until then it
 * subscribes to FeedTodoSyncContext for cross-block lifecycle updates using
 * the new R3 todoId-based payload (FR-14 / OS-1): the listener receives
 * `(todoId, isActive)` and the hook treats the `todoId` opaquely — it
 * triggers a refetch when relevant todos become active/inactive.
 *
 * Sort order (FR-07):
 *   1. sprk_priorityscore DESC (highest priority first)
 *   2. sprk_duedate ASC (earliest due date first within same priority)
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
  /** Record scope. */
  scope?: 'my' | 'all';
  /** Business unit ID. */
  businessUnitId?: string;
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
      .getActiveTodos(userId, { scope: options.scope, businessUnitId: options.businessUnitId })
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
  }, [userId, mockItems, fetchKey, options.scope, options.businessUnitId]);

  // -------------------------------------------------------------------------
  // FeedTodoSyncContext subscription — react to cross-block todo lifecycle
  // changes (R3 FR-14 payload contract: `(todoId, isActive)`).
  //
  // This LegalWorkspace block still loads its data from `sprk_event` (the
  // sprk_todo repoint here lands in a later task), so we cannot resolve a
  // todoId to a specific row in our list directly. The robust behaviour is
  // to trigger a refetch on any cross-block notification — the next render
  // sees a fresh authoritative list. The cost is one extra query per
  // notification, acceptable for the user-driven mutation cadence.
  // -------------------------------------------------------------------------

  const { subscribe } = useFeedTodoSync();

  useEffect(() => {
    const unsubscribe = subscribe((_todoId: string, _isActive: boolean) => {
      // Mark suppressed-args to satisfy strict no-unused — these are part of
      // the public listener contract and used by other consumers.
      void _todoId;
      void _isActive;
      // Trigger refetch. Safe to call multiple times in quick succession —
      // useEffect coalesces via the fetchKey state bump.
      setFetchKey((k) => k + 1);
    });

    return unsubscribe;
  }, [subscribe]);

  // Memoize the returned items so that consumers receive a stable reference
  // when neither the list contents nor the loading/error state has changed.
  // This prevents cascading re-renders in SmartToDo when parent components
  // update for unrelated reasons (NFR-01: page load < 3s).
  const memoizedItems = useMemo(() => items, [items]);

  return { items: memoizedItems, isLoading, error, refetch };
}
