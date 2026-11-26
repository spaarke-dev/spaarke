/**
 * useSmartTodoBridge — the SmartTodo code page's data for the shared Smart To Do board.
 *
 * #2907 / #2972: the code page no longer keeps its own fork of the `SmartToDo` component or of the data layer
 * (DataverseService, useTodoItems, useUserPreferences). It renders `@spaarke/smart-todo-components`' `SmartToDo` and
 * this hook supplies the page-specific pieces, the way LegalWorkspace's `useSmartToDoBridge` does for the workspace:
 *   - the list scope: to-dos assigned to the current user's contact (R4 task 031 / OD-2 "Assigned to Me"), narrowed
 *     to one parent record when the page was launched with `action=openTodos` (R4 FR-34 / DEF-11 Part 2);
 *   - to-do creates through the BFF (`createTodoThroughBff`, UAC-r2 task 147 r1);
 *   - ONE preferences instance for the whole page (the Header's orientation and the board's thresholds and column
 *     order share it — two instances overwrote each other's fields).
 */

import * as React from 'react';
import {
  TodoDataverseService,
  useCurrentContactId,
  useTodoItems,
  useTodoKanbanPreferences,
  type ITodoWebApi,
  type ITodoKanbanPreferences,
  type ITodoMutationResult,
  type ISmartToDoProps,
  type TodoListScope,
} from '@spaarke/smart-todo-components';
import { createTodoThroughBff } from '../services/childRecordWrites';
import { useLaunchContext, LAUNCH_ACTION_OPEN_TODOS } from './useLaunchContext';
import type { ITodo } from '../types/entities';

/** The contact id used before the current user's contact resolves, or when they have none: matches no to-dos. */
const NO_CONTACT_ID = '00000000-0000-0000-0000-000000000000';

export interface IUseSmartTodoBridgeOptions {
  /** Xrm.WebApi (null outside Dataverse — the reads then fail and the board shows the error). */
  webApi: unknown;
  /** The current user's systemuser GUID. */
  userId: string;
}

/** The board props this hook supplies, plus what the page's own chrome needs. */
export type ISmartTodoBridgeResult = Pick<
  ISmartToDoProps,
  | 'items'
  | 'isLoading'
  | 'error'
  | 'onRefetch'
  | 'preferences'
  | 'onUpdatePreferences'
  | 'prefsLoading'
  | 'onCreateTodo'
  | 'onDismissTodo'
  | 'onRestoreTodo'
  | 'dataverseService'
> & {
  /** The same items, typed for the page's toolbar actions. */
  todos: ITodo[];
  /** Persists a preference change and resolves when saved (the Header's orientation toggle). */
  updatePreferences: (update: Partial<ITodoKanbanPreferences>) => Promise<void>;
};

function toMutationResult(
  result: { success: true; data?: unknown } | { success: false; error: { message: string } }
): ITodoMutationResult {
  return result.success
    ? { success: true, id: typeof result.data === 'string' ? result.data : undefined }
    : { success: false, error: { message: result.error.message } };
}

export function useSmartTodoBridge(options: IUseSmartTodoBridgeOptions): ISmartTodoBridgeResult {
  const { webApi, userId } = options;

  const service = React.useMemo(
    () => new TodoDataverseService(webApi as ITodoWebApi, createTodoThroughBff),
    [webApi]
  );

  // `sprk_todo.sprk_assignedto` is a contact lookup — resolve the user's contact (UAT 2026-06-19).
  const { contactId } = useCurrentContactId({
    webApi: webApi as Parameters<typeof useCurrentContactId>[0]['webApi'],
    userId,
  });

  // openTodos launch → narrow to the parent record. Other launches keep "Assigned to Me".
  const launchContext = useLaunchContext();
  const regardingEntityType =
    launchContext?.action === LAUNCH_ACTION_OPEN_TODOS ? launchContext.regardingFilter.entityType : undefined;
  const regardingRecordId =
    launchContext?.action === LAUNCH_ACTION_OPEN_TODOS ? launchContext.regardingFilter.recordId : undefined;

  const scope = React.useMemo<TodoListScope>(
    () => ({
      kind: 'assignedTo',
      contactId: contactId ?? NO_CONTACT_ID,
      regardingFilter:
        regardingEntityType && regardingRecordId
          ? { entityType: regardingEntityType, recordId: regardingRecordId }
          : undefined,
    }),
    [contactId, regardingEntityType, regardingRecordId]
  );

  const { items, isLoading, error, refetch } = useTodoItems({ service, scope });
  const { preferences, updatePreferences, isLoading: prefsLoading } = useTodoKanbanPreferences({ service, userId });

  const onUpdatePreferences = React.useCallback(
    (update: Partial<ITodoKanbanPreferences>) => {
      void updatePreferences(update);
    },
    [updatePreferences]
  );

  const onCreateTodo = React.useCallback(
    async (title: string) => toMutationResult(await service.createTodo(title)),
    [service]
  );
  const onDismissTodo = React.useCallback(
    async (todoId: string) => toMutationResult(await service.dismissTodo(todoId)),
    [service]
  );
  const onRestoreTodo = React.useCallback(
    async (todoId: string) => toMutationResult(await service.updateTodoStatus(todoId, 'Open')),
    [service]
  );

  return {
    items,
    todos: items as unknown as ITodo[],
    isLoading,
    error,
    onRefetch: refetch,
    preferences,
    onUpdatePreferences,
    updatePreferences,
    prefsLoading,
    onCreateTodo,
    onDismissTodo,
    onRestoreTodo,
    dataverseService: service,
  };
}
