/**
 * useTodoItems — the workspace's Smart To Do list.
 *
 * #2972: a thin adapter over the shared `useTodoItems` in `@spaarke/smart-todo-components` (the SmartTodo code page
 * uses the same hook). This file only supplies the workspace's pieces: its `DataverseService`, the owner scope
 * (`my` / `all` + business unit) and the FeedTodoSyncContext bus (R3 FR-14).
 *
 * Usage:
 *   const { items, isLoading, error, refetch } = useTodoItems({ webApi, userId });
 */

import { useMemo } from 'react';
import { useTodoItems as useSharedTodoItems, type TodoListScope } from '@spaarke/smart-todo-components';
import { DataverseService } from '../services/DataverseService';
import type { ITodo } from '../types/entities';
import type { IWebApi } from '../types/xrm';
import { useFeedTodoSync } from './useFeedTodoSync';

export interface IUseTodoItemsOptions {
  /** Xrm.WebApi reference from the PCF framework context */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId) */
  userId: string;
  /**
   * Optional mock items for local development / testing.
   * When provided, bypasses Xrm.WebApi.
   */
  mockItems?: ITodo[];
  /** Workspace record scope. */
  scope?: 'my' | 'all';
  /** Workspace business unit ID. */
  businessUnitId?: string;
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

export function useTodoItems(options: IUseTodoItemsOptions): IUseTodoItemsResult {
  const { webApi, userId, mockItems, scope, businessUnitId } = options;
  const service = useMemo(() => new DataverseService(webApi), [webApi]);
  const listScope = useMemo<TodoListScope | null>(
    () => (userId ? { kind: 'owner', userId, scope, businessUnitId } : null),
    [userId, scope, businessUnitId]
  );
  const feedSync = useFeedTodoSync();
  // The shared ITodo makes createdon/modifiedon optional; the rows always carry them.
  return useSharedTodoItems({ service, scope: listScope, mockItems, feedSync }) as IUseTodoItemsResult;
}
