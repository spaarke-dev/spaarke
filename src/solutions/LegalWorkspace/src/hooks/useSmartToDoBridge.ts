/**
 * useSmartToDoBridge — LegalWorkspace-side injection adapter for the hoisted
 * `@spaarke/smart-todo-components` rich `SmartToDo` / `SmartToDoDialog`.
 *
 * R5 FR-01 / task 003 (thin-shim conversion). Task 002 redesigned the hoisted
 * `SmartToDo` container to be fully prop-injected (host-agnostic, ADR-012 /
 * NFR-05 — zero `src/solutions/...` reach-in). This hook is the ONE place
 * that fulfils the injected contract documented in
 * `projects/smart-todo-r5/notes/task-002-hoist.md` ("Contract handed to
 * task 003"), so both LegalWorkspace call sites (`components/SmartToDo/SmartToDo.tsx`
 * and `components/SmartToDo/SmartToDoDialog.tsx`) share ONE implementation
 * instead of duplicating the wiring:
 *
 *   - `useTodoItems`         → `items` / `isLoading` / `error` / `onRefetch`
 *   - `useUserPreferences`   → `preferences` / `onUpdatePreferences` / `prefsLoading`
 *   - `DataverseService.createTodo/dismissTodo/updateTodoStatus`
 *                            → `onCreateTodo` / `onDismissTodo` / `onRestoreTodo`
 *                              (adapted from `IResult<T>` to `ITodoMutationResult`)
 *   - `DataverseService.updateEventColumn/updateEventPinned/batchUpdateEventColumns`
 *                            → `dataverseService: IKanbanDataverseService`
 *                              (structurally compatible — passed directly, no
 *                              adapter class needed; see Component Justification
 *                              note below)
 *   - `useFeedTodoSync`      → `feedSync: IFeedSyncBridge`
 *   - `Xrm.Navigation`       → `onOpenTodo` (ported verbatim from the pre-hoist
 *                              `SmartToDo.tsx#handleOpenSmartTodo` — opens the
 *                              `sprk_smarttodo` Code Page webresource; this is a
 *                              DIFFERENT "open" behavior from the Pattern D
 *                              `SmartTodoWidget` shim in `sections/todo.registration.ts`,
 *                              which opens the OOB `sprk_todo` entity FORM. Both
 *                              are preserved verbatim per call site — no behavior
 *                              change per POML constraint.)
 *
 * Component Justification (root CLAUDE.md §11) for NOT writing a separate
 * `IKanbanDataverseService` adapter class:
 *   - Existing: `DataverseService.updateEventColumn/updateEventPinned/
 *     batchUpdateEventColumns` already have the exact parameter shapes and
 *     `{ success: boolean, ... }`-shaped (`IResult<T>`) return types the
 *     package's `IKanbanDataverseService` interface requires.
 *   - Extension: passing the existing `DataverseService` instance directly
 *     satisfies the interface structurally (extra methods on the class are
 *     harmless — TS interface conformance doesn't require an exact shape for
 *     a non-literal value).
 *   - Cost-of-doing-nothing: writing a wrapper class here would be pure
 *     indirection with no behavior difference — scope creep.
 */

import * as React from 'react';
import type {
  IFeedSyncBridge,
  ISmartToDoProps as IPackageSmartToDoProps,
  ITodoMutationResult,
} from '@spaarke/smart-todo-components';
import { OOB_MODAL_SIZES } from '@spaarke/ui-components';
import { DataverseService } from '../services/DataverseService';
import type { ITodo } from '../types/entities';
import type { IWebApi } from '../types/xrm';
import { useTodoItems } from './useTodoItems';
import { useUserPreferences } from './useUserPreferences';
import { useFeedTodoSync } from './useFeedTodoSync';

// ---------------------------------------------------------------------------
// Options / result types
// ---------------------------------------------------------------------------

export interface IUseSmartToDoBridgeOptions {
  /** Xrm.WebApi reference from the PCF framework context. */
  webApi: IWebApi;
  /** GUID of the current user (context.userSettings.userId). */
  userId: string;
  /**
   * Optional mock items for local development / testing. When provided,
   * bypasses Xrm.WebApi (forwarded to `useTodoItems`).
   */
  mockItems?: ITodo[];
  /** Record scope. */
  scope?: 'my' | 'all';
  /** Business unit ID. */
  businessUnitId?: string;
}

/**
 * The subset of the hoisted package's `ISmartToDoProps` this hook is
 * responsible for (data + mutations + services + navigation). The remaining
 * presentational props (`embedded`, `onCountChange`, `onRefetchReady`,
 * `onShowMore`, `disableSidePane`) are call-site-specific and supplied by
 * each thin shim component directly.
 */
export type ISmartToDoBridgeResult = Omit<
  IPackageSmartToDoProps,
  'embedded' | 'onCountChange' | 'onRefetchReady' | 'onShowMore' | 'disableSidePane'
>;

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useSmartToDoBridge(
  options: IUseSmartToDoBridgeOptions
): ISmartToDoBridgeResult {
  const { webApi, userId, mockItems, scope, businessUnitId } = options;

  // Stable DataverseService reference (mirrors the pre-hoist SmartToDo.tsx pattern).
  const serviceRef = React.useRef<DataverseService>(new DataverseService(webApi));
  React.useEffect(() => {
    serviceRef.current = new DataverseService(webApi);
  }, [webApi]);

  // -------------------------------------------------------------------------
  // Data hooks
  // -------------------------------------------------------------------------

  const { items, isLoading, error, refetch } = useTodoItems({
    webApi,
    userId,
    mockItems,
    scope,
    businessUnitId,
  });

  const { preferences, updatePreferences, isLoading: prefsLoading } =
    useUserPreferences({ webApi, userId });

  const onUpdatePreferences = React.useCallback(
    (prefs: Partial<{ todayThreshold: number; tomorrowThreshold: number }>) => {
      void updatePreferences(prefs);
    },
    [updatePreferences]
  );

  // -------------------------------------------------------------------------
  // Mutation callbacks — adapted from LW's `IResult<T>` to the package's
  // `ITodoMutationResult`.
  // -------------------------------------------------------------------------

  const onCreateTodo = React.useCallback(
    async (title: string): Promise<ITodoMutationResult> => {
      const result = await serviceRef.current.createTodo(title, userId);
      return result.success
        ? { success: true, id: result.data }
        : { success: false, error: { message: result.error?.message } };
    },
    [userId]
  );

  const onDismissTodo = React.useCallback(
    async (todoId: string): Promise<ITodoMutationResult> => {
      const result = await serviceRef.current.dismissTodo(todoId);
      return result.success
        ? { success: true }
        : { success: false, error: { message: result.error?.message } };
    },
    []
  );

  const onRestoreTodo = React.useCallback(
    async (todoId: string): Promise<ITodoMutationResult> => {
      const result = await serviceRef.current.updateTodoStatus(todoId, 'Open');
      return result.success
        ? { success: true }
        : { success: false, error: { message: result.error?.message } };
    },
    []
  );

  // -------------------------------------------------------------------------
  // FeedTodoSyncContext bridge (R3 FR-14 cross-block notification).
  // -------------------------------------------------------------------------

  const { notifyTodoChange, subscribe } = useFeedTodoSync();
  const feedSync = React.useMemo<IFeedSyncBridge>(
    () => ({ notifyChange: notifyTodoChange, subscribe }),
    [notifyTodoChange, subscribe]
  );

  // -------------------------------------------------------------------------
  // Open handler — ported verbatim from the pre-hoist
  // `SmartToDo.tsx#handleOpenSmartTodo` (opens the `sprk_smarttodo` Code Page
  // webresource). Distinct from the Pattern D widget's `onOpenTodo` in
  // `sections/todo.registration.ts` (which opens the OOB `sprk_todo` FORM) —
  // both are preserved per their original call sites, no behavior change.
  // -------------------------------------------------------------------------

  const onOpenTodo = React.useCallback((todoId?: string) => {
    try {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const xrm: any = (window as any).Xrm || (window.parent as any)?.Xrm;
      if (xrm?.Navigation?.navigateTo) {
        // Param name kept as `eventId` for SmartTodo Code Page compatibility;
        // the value carried is a `sprk_todoid` GUID post R3 FR-29.
        const data = todoId ? `eventId=${encodeURIComponent(todoId)}` : undefined;
        xrm.Navigation.navigateTo(
          {
            pageType: 'webresource',
            webresourceName: 'sprk_smarttodo',
            ...(data ? { data } : {}),
          },
          {
            target: 2,
            width: OOB_MODAL_SIZES.record.width,
            height: OOB_MODAL_SIZES.record.height,
          }
        );
      }
    } catch (e) {
      console.warn('[useSmartToDoBridge] Could not open SmartTodo Code Page:', e);
    }
  }, []);

  // -------------------------------------------------------------------------
  // Composed result
  // -------------------------------------------------------------------------

  return {
    items,
    isLoading,
    error,
    onRefetch: refetch,
    preferences,
    onUpdatePreferences,
    prefsLoading,
    onCreateTodo,
    onDismissTodo,
    onRestoreTodo,
    dataverseService: serviceRef.current,
    feedSync,
    onOpenTodo,
  };
}
