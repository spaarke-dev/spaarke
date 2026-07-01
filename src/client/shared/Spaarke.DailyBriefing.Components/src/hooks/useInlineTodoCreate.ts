/**
 * useInlineTodoCreate — hook for creating `sprk_todo` records directly from
 * notification items in the Daily Briefing.
 *
 * Per smart-todo-decoupling-r3 FR-29 / OS-1 (task 014, 2026-06-08):
 *   - Creates first-class `sprk_todo` Dataverse records.
 *   - Legacy `sprk_event { sprk_todoflag=true, sprk_todostatus, sprk_todosource }`
 *     model is retired across the repo. This hook was the final functional
 *     consumer outside the BFF.
 *   - No backward-compat shim — `sprk_eventtodo` is not written under any
 *     circumstances.
 *
 * Regarding (multi-entity resolution per ADR-024):
 *   - When the notification item carries `regardingEntityType` + `regardingId`,
 *     the hook resolves the catalog entry from `TODO_REGARDING_CATALOG` and
 *     calls `applyResolverFields` to atomically populate the entity-specific
 *     lookup + four resolver fields (sprk_regardingrecordtype,
 *     sprk_regardingrecordid, sprk_regardingrecordname, sprk_regardingrecordurl).
 *   - If the regarding entity type is not in the catalog, the regarding step is
 *     skipped and the todo is created without an association (the resolver
 *     fields stay null).
 *
 * Tracks per-notification creation state so the UI can show pending/created/error.
 *
 * Usage:
 *   const { createTodo, isCreated, isPending, getError } = useInlineTodoCreate(webApi);
 *
 *   <Button onClick={() => createTodo(notificationItem)} disabled={isPending(item.id)}>
 *     {isCreated(item.id) ? "Created" : "Add To Do"}
 *   </Button>
 *
 * Hoist note (R2 task 013 / FR-05):
 *   Originally lived at `src/solutions/DailyBriefing/src/hooks/useInlineTodoCreate.ts`.
 *   Hoisted verbatim to `@spaarke/daily-briefing-components/hooks` per ADR-012
 *   (Pattern D dual-use shape, Calendar + Smart Todo precedent).
 *   ADR-024 binding: `TODO_REGARDING_CATALOG` + `applyResolverFields` usage is
 *   preserved byte-identical from the original — no behavior change.
 *   The `types/notifications` import is a relative back-pointer to the standalone
 *   solution's types file until task 014 hoists the types alongside the
 *   `useNotificationData` decomposition (mirrors task 012's `briefingService` →
 *   `notifications.ts` back-pointer pattern). Original location becomes a
 *   re-export shim (cleaned up in task 017/018).
 */

import { useState, useRef, useCallback } from 'react';
import {
  applyResolverFields,
  TODO_REGARDING_CATALOG,
  type INavPropEntry,
  type IPolymorphicWebApi,
} from '@spaarke/ui-components/services';
import type { IWebApi, NotificationItem, NotificationPriority } from '../types/notifications';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

type TodoCreateStatus = 'pending' | 'created' | 'error';

export interface UseInlineTodoCreateResult {
  /** Create a new `sprk_todo` from a notification item. */
  createTodo: (item: NotificationItem) => Promise<void>;
  /** Returns true if a To Do was successfully created for this notification. */
  isCreated: (itemId: string) => boolean;
  /** Returns true if a To Do creation is in-flight for this notification. */
  isPending: (itemId: string) => boolean;
  /** Returns the error message for a failed creation, or undefined. */
  getError: (itemId: string) => string | undefined;
  /**
   * Returns the sprk_todoid of the created record for this notification, or undefined
   * if creation hasn't succeeded yet. Used by the widget to wire "Open To Do" toast
   * actions (R7 W12 feedback item 8).
   */
  getCreatedId: (itemId: string) => string | undefined;
}

// ---------------------------------------------------------------------------
// sprk_todo lifecycle constants (mirrors SmartTodo/DataverseService.ts task 020)
// ---------------------------------------------------------------------------

/** sprk_todo statecode: 0 = Active. */
const STATECODE_ACTIVE = 0;
/** sprk_todo statuscode: 1 = Open (statecode 0). */
const STATUSCODE_OPEN = 1;

/**
 * R2.2 Item 3 — Default due-date strategy for new To Dos created from
 * Daily Briefing notifications:
 *   1. If the source notification carries `item.dueDate` (task notifications
 *      from the R2.2 plumbing change), use it verbatim — preserves the actual
 *      task due date so the To Do inherits the original deadline.
 *   2. Otherwise default to **+3 calendar days from now, end of day (17:00 local)**
 *      — gives the user a reasonable working window without being too aggressive.
 *      Notifications without a real due date (documents, emails, events) get
 *      this default; the user can edit later in the To Do app.
 */
const DEFAULT_DUE_OFFSET_DAYS = 3;
const DEFAULT_DUE_HOUR_LOCAL = 17;

export function computeDueDate(item: NotificationItem, now: Date = new Date()): string {
  if (item.dueDate) {
    const parsed = new Date(item.dueDate);
    if (!isNaN(parsed.getTime())) {
      return parsed.toISOString();
    }
  }
  const fallback = new Date(now);
  fallback.setDate(fallback.getDate() + DEFAULT_DUE_OFFSET_DAYS);
  fallback.setHours(DEFAULT_DUE_HOUR_LOCAL, 0, 0, 0);
  return fallback.toISOString();
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Map notification priority string to a 0-100 priority score for
 * `sprk_todo.sprk_priorityscore`. Bands match the implicit kanban
 * banding used elsewhere (low ~20, normal ~50, high ~70, urgent ~90).
 */
function mapPriorityToScore(priority: NotificationPriority): number {
  switch (priority) {
    case 'urgent':
      return 90;
    case 'high':
      return 70;
    case 'normal':
      return 50;
    case 'low':
      return 20;
    default:
      return 50;
  }
}

// ---------------------------------------------------------------------------
// Nav-prop discovery for sprk_todo (mirrors the shared `todoService.ts` pattern)
// ---------------------------------------------------------------------------

/** Page-session cache of discovered ManyToOne nav-props, keyed by entity name. */
const _navPropCache: Record<string, INavPropEntry[]> = {};

async function _discoverNavProps(entityLogicalName: string): Promise<INavPropEntry[]> {
  if (_navPropCache[entityLogicalName]) {
    return _navPropCache[entityLogicalName];
  }

  try {
    const url =
      `/api/data/v9.0/EntityDefinitions(LogicalName='${entityLogicalName}')/ManyToOneRelationships` +
      `?$select=ReferencingAttribute,ReferencingEntityNavigationPropertyName,ReferencedEntity`;

    const resp = await fetch(url, { credentials: 'include' });
    if (!resp.ok) {
      console.warn(`[useInlineTodoCreate] Nav-prop discovery failed for ${entityLogicalName}: HTTP ${resp.status}`);
      return [];
    }

    const json = (await resp.json()) as {
      value?: Array<{
        ReferencingAttribute: string;
        ReferencingEntityNavigationPropertyName: string;
        ReferencedEntity: string;
      }>;
    };

    const entries: INavPropEntry[] = (json.value ?? []).map(r => ({
      columnName: r.ReferencingAttribute,
      navPropName: r.ReferencingEntityNavigationPropertyName,
      referencedEntity: r.ReferencedEntity,
    }));

    _navPropCache[entityLogicalName] = entries;
    return entries;
  } catch (err) {
    console.warn(`[useInlineTodoCreate] Nav-prop discovery error for ${entityLogicalName}:`, err);
    return [];
  }
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

/**
 * Hook for inline To Do creation from notification items.
 *
 * @param webApi - Xrm.WebApi reference (from xrmProvider). Pass null if not yet available.
 * @param userId - Optional current-user systemuserid. When supplied, the hook looks up the
 *                 user's `sprk_primarycontact` and sets it as `sprk_assignedto` on every
 *                 created sprk_todo (R7 W12 feedback item 7). Primary-contact value is
 *                 cached in a ref for the hook's lifetime.
 * @returns Object with createTodo, isCreated, isPending, getError, getCreatedId functions.
 */
export function useInlineTodoCreate(
  webApi: IWebApi | null,
  userId?: string
): UseInlineTodoCreateResult {
  const [statusMap, setStatusMap] = useState<Map<string, TodoCreateStatus>>(() => new Map());
  const errorsRef = useRef<Map<string, string>>(new Map());
  const createdIdRef = useRef<Map<string, string>>(new Map());
  // Cache the current user's sprk_primarycontact value. undefined = not looked up yet;
  // null = looked up + no primary contact configured on the user record.
  const primaryContactRef = useRef<string | null | undefined>(undefined);

  const createTodo = useCallback(
    async (item: NotificationItem): Promise<void> => {
      if (!webApi) {
        console.error('[useInlineTodoCreate] webApi not available');
        return;
      }

      // Optimistic: set to pending
      setStatusMap(prev => {
        const next = new Map(prev);
        next.set(item.id, 'pending');
        return next;
      });

      try {
        // R7 W12 feedback item 7 — resolve the current user's sprk_primarycontact
        // (cached across creates for this hook lifetime). We do the lookup lazily
        // on first createTodo rather than at hook-mount so consumers that never
        // add to To Do don't pay the query cost. Fail-soft: if the field is missing
        // or the query fails, primarycontact stays null and sprk_assignedto is left
        // unset (the existing pre-item-7 behavior).
        if (primaryContactRef.current === undefined && userId) {
          try {
            const user = await webApi.retrieveRecord(
              'systemuser',
              userId,
              '?$select=_sprk_primarycontact_value'
            );
            const rawContact = (user as Record<string, unknown>)['_sprk_primarycontact_value'];
            primaryContactRef.current = typeof rawContact === 'string' && rawContact.length > 0 ? rawContact : null;
          } catch (lookupErr) {
            console.info(
              '[useInlineTodoCreate] sprk_primarycontact lookup failed — sprk_assignedto will be left unset:',
              lookupErr
            );
            primaryContactRef.current = null;
          }
        }

        // 1. Build the core sprk_todo record (per entity-schema.md + SmartTodo
        //    DataverseService.createTodo pattern).
        const record: Record<string, unknown> = {
          sprk_name: item.title,
          statecode: STATECODE_ACTIVE,
          statuscode: STATUSCODE_OPEN,
          sprk_priorityscore: mapPriorityToScore(item.priority),
          // R2.2 Item 3 — auto-default sprk_duedate so the new To Do is
          // immediately actionable. Uses item.dueDate when supplied by the
          // playbook (task notifications), else falls back to +3 calendar days
          // at end-of-day local. See computeDueDate() above.
          sprk_duedate: computeDueDate(item),
        };

        if (item.body) {
          // sprk_notes is the rich-text/long-text body field on sprk_todo
          // (replaces the legacy sprk_event.sprk_description path).
          record['sprk_notes'] = item.body;
        }

        // R7 W12 feedback item 7 — bind sprk_assignedto to the current user's
        // primary contact when known. Uses the @odata.bind syntax expected by
        // Xrm.WebApi.createRecord for lookup fields (target entity set 'contacts').
        if (primaryContactRef.current) {
          record['sprk_assignedto@odata.bind'] = `/contacts(${primaryContactRef.current})`;
        }

        // 2. Apply regarding (multi-entity resolution per ADR-024) — only when
        //    a regarding entity is supplied AND it's in the supported catalog.
        if (item.regardingEntityType && item.regardingId) {
          const catalogEntry = TODO_REGARDING_CATALOG.find(
            (c: { entityType: string }) => c.entityType === item.regardingEntityType
          );

          if (catalogEntry) {
            // Wrap IWebApi.retrieveMultipleRecords into the shape
            // applyResolverFields expects (IPolymorphicWebApi).
            const polyWebApi: IPolymorphicWebApi = {
              retrieveMultipleRecords: (entityLogicalName: string, query: string) =>
                webApi.retrieveMultipleRecords(entityLogicalName, query),
            };

            const navProps = await _discoverNavProps('sprk_todo');

            // Notification carries the parent's display name in `regardingName`.
            await applyResolverFields(
              polyWebApi,
              record,
              navProps,
              catalogEntry.entityType,
              catalogEntry.entitySet,
              item.regardingId,
              item.regardingName ?? '',
              catalogEntry.navPropHint
            );
          } else {
            // Regarding entity not in supported catalog — create the todo
            // without a regarding association rather than failing the whole
            // operation. This handles notifications for entities not in the
            // 11-entity multi-entity resolution scope.
            console.info(
              `[useInlineTodoCreate] Regarding entity "${item.regardingEntityType}" not in TODO_REGARDING_CATALOG — creating todo without association.`
            );
          }
        }

        // 3. Create the sprk_todo record (NEVER sprk_event).
        const createResult = await webApi.createRecord('sprk_todo', record);
        if (createResult?.id) {
          createdIdRef.current.set(item.id, createResult.id);
        }

        // Success
        setStatusMap(prev => {
          const next = new Map(prev);
          next.set(item.id, 'created');
          return next;
        });
      } catch (e: unknown) {
        // Failure
        let message = 'Failed to create To Do. Please try again.';
        if (e && typeof e === 'object') {
          const err = e as Record<string, unknown>;
          if (typeof err['message'] === 'string') {
            message = err['message'];
          }
        } else if (typeof e === 'string') {
          message = e;
        }

        errorsRef.current.set(item.id, message);
        console.error('[useInlineTodoCreate] Failed to create To Do:', message, e);

        setStatusMap(prev => {
          const next = new Map(prev);
          next.set(item.id, 'error');
          return next;
        });
      }
    },
    [webApi, userId]
  );

  const isCreated = useCallback((itemId: string): boolean => statusMap.get(itemId) === 'created', [statusMap]);

  const isPending = useCallback((itemId: string): boolean => statusMap.get(itemId) === 'pending', [statusMap]);

  const getError = useCallback(
    (itemId: string): string | undefined => errorsRef.current.get(itemId),
    // statusMap is included so the callback identity changes when errors are recorded
    // (error status is set in statusMap at the same time as errorsRef is updated)
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [statusMap]
  );

  const getCreatedId = useCallback(
    (itemId: string): string | undefined => createdIdRef.current.get(itemId),
    // Depend on statusMap so consumers re-render when creation completes and
    // pick up the newly-populated id.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [statusMap]
  );

  return { createTodo, isCreated, isPending, getError, getCreatedId };
}
