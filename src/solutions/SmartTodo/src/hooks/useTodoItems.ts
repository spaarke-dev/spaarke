/**
 * useTodoItems — React hook for fetching and managing Smart To Do list items.
 *
 * Per R3 FR-09 / FR-11, queries `sprk_todo` records (statecode=0, statuscode
 * in (Open, In Progress)). The legacy `sprk_event` + `sprk_todoflag` path is
 * removed (OS-1: no compat shims).
 *
 * Integrates with FeedTodoSyncContext so todo lifecycle notifications from
 * elsewhere (e.g. Updates Feed in LegalWorkspace) are reflected in real time
 * without a page refresh. In standalone SmartTodo the sync context is a no-op
 * (see useFeedTodoSync.ts).
 *
 * Sort order (FR-07):
 *   1. computed To Do Score DESC (priority/effort/urgency composite)
 *   2. sprk_duedate ASC (earliest due date first within same score)
 *
 * Usage:
 *   const { items, isLoading, error, refetch } = useTodoItems({ webApi, userId });
 */

import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { DataverseService } from '../services/DataverseService';
import { ITodo } from '../types/entities';
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
function sortTodoItems(items: ITodo[]): ITodo[] {
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
  /**
   * UAT 2026-06-19: param semantic changed from systemuser GUID to
   * sprk_contact GUID. Caller must resolve via useCurrentContactId.
   * The field name stays `userId` for back-compat with legacy callers
   * (they pass whatever value they have; field renaming is deferred).
   */
  userId: string;
  /**
   * Optional mock items for local development / testing.
   * When provided, bypasses Xrm.WebApi.
   */
  mockItems?: ITodo[];
}

export interface IUseTodoItemsResult {
  /** Sorted active to-do items (excludes Dismissed) */
  items: ITodo[];
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

  const [items, setItems] = useState<ITodo[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [fetchKey, setFetchKey] = useState<number>(0);

  // Stable service reference — only recreates when webApi changes
  const serviceRef = useRef<DataverseService>(new DataverseService(webApi));
  useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  // Keep items ref for synchronous reads inside the subscribe callback
  const itemsRef = useRef<ITodo[]>([]);
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
  // FeedTodoSyncContext subscription — react to cross-block todo lifecycle
  // changes (R3 FR-14 / task 023 contract).
  // -------------------------------------------------------------------------

  const { subscribe } = useFeedTodoSync();

  useEffect(() => {
    // The listener payload is `(todoId, isActive)` per R3 FR-14:
    //   - todoId is a sprk_todoid GUID
    //   - isActive=true  → todo became Open / In Progress
    //   - isActive=false → todo became Completed / Dismissed / deleted
    // In the standalone SmartTodo Code Page the bus is a no-op stub
    // (see useFeedTodoSync.ts) so this callback only fires when the hook is
    // re-hosted inside LegalWorkspace where a FeedTodoSyncProvider is mounted.
    const unsubscribe = subscribe(async (todoId: string, isActive: boolean) => {
      if (isActive) {
        // Todo became active externally — check for duplicates and re-fetch.
        const alreadyInList = itemsRef.current.some(
          (item) => item.sprk_todoid === todoId
        );
        if (alreadyInList) return;

        if (!userId) return;

        try {
          const result = await serviceRef.current.getActiveTodos(userId);
          if (!result.success) return;

          // Find the newly active todo in the refreshed list
          const newItem = result.data.find((t) => t.sprk_todoid === todoId);
          if (!newItem) return;

          setItems((prev) => {
            // Double-check for race condition: item might have been added already
            if (prev.some((item) => item.sprk_todoid === todoId)) return prev;
            return sortTodoItems([...prev, newItem]);
          });
        } catch {
          // Silently ignore — the item will appear on next manual refetch
        }
      } else {
        // Todo became inactive (dismissed/deleted) — remove from the list immediately
        setItems((prev) => {
          const filtered = prev.filter((item) => item.sprk_todoid !== todoId);
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
