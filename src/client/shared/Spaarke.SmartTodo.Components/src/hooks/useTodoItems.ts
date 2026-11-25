/**
 * useTodoItems — loads the Smart To Do list for a scope; one copy for every host (#2972).
 *
 * Before #2972 the LegalWorkspace and SmartTodo code pages each had their own copy of this hook (and of the service
 * under it). The host now passes the service and the scope in.
 *
 * Sort (FR-07): To Do Score descending, then due date ascending.
 *
 * Cross-surface sync (R3 FR-14): when the host passes `feedSync`, the hook listens for `(todoId, isActive)` changes
 * made elsewhere — an inactive to-do leaves the list at once; a newly active one is fetched and inserted.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ITodo } from '../types/entities';
import type { IFeedSyncBridge } from '../types/todo';
import type { TodoDataverseService } from '../services/TodoDataverseService';
import { todoListScopeKey, type TodoListScope } from '../services/todoQueries';
import { computeTodoScore } from '../utils/todoScoring';

/** Sorts by To Do Score descending, then due date ascending (no due date last). Returns a new array. */
export function sortTodoItems<T extends ITodo>(items: readonly T[]): T[] {
  return [...items].sort((a, b) => {
    const scoreDiff = computeTodoScore(b).todoScore - computeTodoScore(a).todoScore;
    if (scoreDiff !== 0) return scoreDiff;
    const dueA = a.sprk_duedate ? new Date(a.sprk_duedate).getTime() : Infinity;
    const dueB = b.sprk_duedate ? new Date(b.sprk_duedate).getTime() : Infinity;
    return dueA - dueB;
  });
}

export interface IUseTodoItemsOptions {
  /** The data service (host-constructed). The latest one is used; a new identity does not refetch. */
  service: Pick<TodoDataverseService, 'getActiveTodos'>;
  /** Which to-dos to load. `null` loads nothing and reports not-loading (e.g. no user yet). */
  scope: TodoListScope | null;
  /** Items for local development / tests — when set, the service is not called. */
  mockItems?: ITodo[];
  /** Optional cross-surface lifecycle bus (only `subscribe` is used here). */
  feedSync?: Pick<IFeedSyncBridge, 'subscribe'>;
}

export interface IUseTodoItemsResult {
  /** Active to-dos, sorted. */
  items: ITodo[];
  /** True while a fetch is in progress. */
  isLoading: boolean;
  /** User-facing error message, or null. */
  error: string | null;
  /** Fetches the list again. */
  refetch: () => void;
}

export function useTodoItems(options: IUseTodoItemsOptions): IUseTodoItemsResult {
  const { service, scope, mockItems, feedSync } = options;

  const [items, setItems] = useState<ITodo[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [fetchKey, setFetchKey] = useState<number>(0);

  // The scope object is usually a new identity every render; effects key on its string form.
  const scopeKey = todoListScopeKey(scope);
  const scopeRef = useRef(scope);
  scopeRef.current = scope;
  const serviceRef = useRef(service);
  serviceRef.current = service;
  const itemsRef = useRef<ITodo[]>([]);
  itemsRef.current = items;

  const refetch = useCallback(() => setFetchKey(k => k + 1), []);

  useEffect(() => {
    if (mockItems) {
      setItems(sortTodoItems(mockItems));
      setIsLoading(false);
      setError(null);
      return;
    }
    const currentScope = scopeRef.current;
    if (!currentScope) {
      setItems([]);
      setIsLoading(false);
      setError(null);
      return;
    }

    let cancelled = false;
    setIsLoading(true);
    setError(null);
    serviceRef.current
      .getActiveTodos(currentScope)
      .then(result => {
        if (cancelled) return;
        if (!result.success) {
          setError(result.error?.message ?? 'Unable to load to-do items. Please try again.');
          setIsLoading(false);
          return;
        }
        setItems(sortTodoItems(result.data));
        setIsLoading(false);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setError(
          err instanceof Error && err.message ? err.message : 'An unexpected error occurred loading to-do items.'
        );
        setIsLoading(false);
      });
    return () => {
      cancelled = true;
    };
    // `service` is read through a ref: a host that does not memoise it must not cause a fetch loop.
  }, [scopeKey, mockItems, fetchKey]);

  const subscribe = feedSync?.subscribe;
  useEffect(() => {
    if (!subscribe) return undefined;
    return subscribe(async (todoId: string, isActive: boolean) => {
      if (!isActive) {
        setItems(prev => {
          const filtered = prev.filter(item => item.sprk_todoid !== todoId);
          return filtered.length === prev.length ? prev : filtered;
        });
        return;
      }
      if (itemsRef.current.some(item => item.sprk_todoid === todoId)) return;
      const currentScope = scopeRef.current;
      if (!currentScope) return;
      try {
        const result = await serviceRef.current.getActiveTodos(currentScope);
        if (!result.success) return;
        const newItem = result.data.find(t => t.sprk_todoid === todoId);
        if (!newItem) return;
        setItems(prev => (prev.some(item => item.sprk_todoid === todoId) ? prev : sortTodoItems([...prev, newItem])));
      } catch {
        // The to-do appears on the next refetch.
      }
    });
  }, [subscribe, scopeKey]);

  const memoizedItems = useMemo(() => items, [items]);
  return { items: memoizedItems, isLoading, error, refetch };
}
