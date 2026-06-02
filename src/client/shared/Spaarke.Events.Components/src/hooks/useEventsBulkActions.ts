/**
 * useEventsBulkActions — shared hook for Events bulk-action surfaces.
 *
 * Single source of truth for the five canonical Events bulk operations:
 * Complete, Close, Cancel, Put-On-Hold, Archive — plus the two low-level
 * primitives (status update, archive) they compose.
 *
 * Consumers (R4 task 063, B-7):
 *  - `src/solutions/EventsPage/src/App.tsx`  (standalone code page)
 *  - `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`
 *
 * ADR-012 (shared-component context-agnostic): no host imports, no global
 *   Xrm access — the host injects `getXrm` (which already handles cross-frame
 *   fallback in both consumers).
 * ADR-021 (Fluent v9 tokens): non-UI hook; no styling concerns.
 * ADR-022 (React 19): compatible — only `useCallback` used.
 * ADR-028 (function-based auth): N/A — bulk operations go through `Xrm.WebApi`
 *   (Dataverse Web API direct call), explicitly allowed by ADR-028 D-AUTH-7.
 *   No `authenticatedFetch`, no BFF token snapshots.
 *
 * Behavior parity with the pre-extraction EventsPage inline implementation
 * (canonical). Three small wording divergences in the Calendar widget were
 * resolved in favor of EventsPage strings — documented in
 * `projects/spaarke-ai-platform-unification-r4/notes/b7-hook-api-sketch.md`.
 *
 * @see projects/spaarke-ai-platform-unification-r4/notes/b7-hook-api-sketch.md
 */

import * as React from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// Constants (re-exported — consumers drop their inline copies)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Event Status values (sprk_eventstatus custom field).
 * Mirrors Spaarke.Event.EventStatus in sprk_event_ribbon_commands.js.
 */
export const EventStatus = {
  DRAFT: 0,
  OPEN: 1,
  COMPLETED: 2,
  CLOSED: 3,
  ON_HOLD: 4,
  CANCELLED: 5,
  REASSIGNED: 6,
  ARCHIVED: 7,
} as const;

export type EventStatusValue = (typeof EventStatus)[keyof typeof EventStatus];

/**
 * OOB Dataverse state codes (only used when archiving).
 */
export const StateCode = {
  ACTIVE: 0,
  INACTIVE: 1,
} as const;

/**
 * Default Dataverse logical name for the Event entity. Override via deps.
 */
export const EVENT_ENTITY_NAME = 'sprk_event';

// ─────────────────────────────────────────────────────────────────────────────
// Minimal Xrm typing — host-injected via deps.getXrm
// ─────────────────────────────────────────────────────────────────────────────

/* eslint-disable @typescript-eslint/no-explicit-any */
/**
 * Structural minimum the hook needs from the Xrm global. Hosts that pass
 * a richer `Xrm` (with side-pane / form / navigation surfaces) get them
 * for free via the `any` widening, but the hook itself only touches:
 *   - `WebApi.updateRecord`
 *   - `App.addGlobalNotification` (optional)
 *   - `Navigation.openConfirmDialog` (optional — falls back to window.confirm)
 *   - `Navigation.openAlertDialog` (optional — error surfacing)
 */
export interface XrmLike {
  WebApi: {
    updateRecord(entityName: string, id: string, data: Record<string, unknown>): Promise<unknown>;
  };
  App?: {
    addGlobalNotification?: (params: {
      type: number;
      level: number;
      message: string;
      showCloseButton?: boolean;
    }) => void;
  };
  Navigation?: {
    openConfirmDialog?: (params: {
      title: string;
      text: string;
      confirmButtonLabel?: string;
      cancelButtonLabel?: string;
    }) => Promise<{ confirmed: boolean }>;
    openAlertDialog?: (params: { title: string; text: string }) => Promise<unknown>;
  };
}
/* eslint-enable @typescript-eslint/no-explicit-any */

// ─────────────────────────────────────────────────────────────────────────────
// Deps (injected by host — ADR-012 context-agnostic)
// ─────────────────────────────────────────────────────────────────────────────

export interface UseEventsBulkActionsDeps {
  /**
   * Resolver for the Xrm global. Hosts handle cross-frame fallback
   * (current window → parent → top). Returns null when Xrm is
   * unreachable (rare; bulk actions then no-op + return false).
   */
  getXrm: () => XrmLike | null;
  /**
   * Override for the Event entity logical name. Defaults to "sprk_event".
   * Provided for hypothetical multi-tenant or test scenarios.
   */
  entityName?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook return shape
// ─────────────────────────────────────────────────────────────────────────────

export interface UseEventsBulkActionsApi {
  /**
   * Low-level: writes `sprk_eventstatus=newStatus` (+ optional extra fields)
   * for each id. Surfaces a success notification on completion. Returns
   * true if all updates succeeded.
   */
  updateStatus: (
    eventIds: string[],
    newStatus: number,
    statusLabel: string,
    additionalFields?: Record<string, unknown>
  ) => Promise<boolean>;
  /**
   * Low-level: archives — sets `sprk_eventstatus=ARCHIVED` then
   * `statecode=INACTIVE, statuscode=2` for each id in two sequential
   * calls per id (matches pre-extraction behavior). Surfaces a success
   * notification on completion.
   */
  archiveEvents: (eventIds: string[]) => Promise<boolean>;

  /** Confirm + complete: status=COMPLETED, sprk_completeddate=now. */
  completeEvents: (eventIds: string[]) => Promise<boolean>;
  /** Confirm + close: status=CLOSED. */
  closeEvents: (eventIds: string[]) => Promise<boolean>;
  /** Confirm + cancel: status=CANCELLED. */
  cancelEvents: (eventIds: string[]) => Promise<boolean>;
  /** Confirm + put-on-hold: status=ON_HOLD. */
  holdEvents: (eventIds: string[]) => Promise<boolean>;
  /** Confirm + archive (deactivate). */
  archiveSelectedEvents: (eventIds: string[]) => Promise<boolean>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal: confirm-dialog helper (Xrm.Navigation.openConfirmDialog | window.confirm)
// ─────────────────────────────────────────────────────────────────────────────

async function confirmAction(
  xrm: XrmLike | null,
  title: string,
  text: string,
  confirmButtonLabel: string,
  cancelButtonLabel: string
): Promise<boolean> {
  if (xrm?.Navigation?.openConfirmDialog) {
    try {
      const result = await xrm.Navigation.openConfirmDialog({
        title,
        text,
        confirmButtonLabel,
        cancelButtonLabel,
      });
      return !!result?.confirmed;
    } catch {
      // Fall through to window.confirm.
    }
  }
  return typeof window !== 'undefined' && typeof window.confirm === 'function' ? window.confirm(text) : false;
}

// ─────────────────────────────────────────────────────────────────────────────
// Hook
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Returns a stable API of bulk-action handlers for the Events entity.
 *
 * @example
 * ```tsx
 * const { completeEvents, archiveSelectedEvents } = useEventsBulkActions({
 *   getXrm: () => getXrmFromCurrentOrParentFrame(),
 * });
 *
 * const onCompleteClick = React.useCallback(async () => {
 *   const ok = await completeEvents(selectedIds);
 *   if (ok) refreshGrid();
 * }, [completeEvents, selectedIds, refreshGrid]);
 * ```
 */
export function useEventsBulkActions(deps: UseEventsBulkActionsDeps): UseEventsBulkActionsApi {
  const { getXrm } = deps;
  const entityName = deps.entityName ?? EVENT_ENTITY_NAME;

  // ── Low-level primitives ──────────────────────────────────────────────────

  const updateStatus = React.useCallback<UseEventsBulkActionsApi['updateStatus']>(
    async (eventIds, newStatus, statusLabel, additionalFields) => {
      const xrm = getXrm();
      if (!xrm?.WebApi) {
        console.warn('[useEventsBulkActions] Xrm.WebApi not available. Cannot update status.');
        return false;
      }
      const cleanIds = eventIds.map(id => id.replace(/[{}]/g, ''));
      const payload: Record<string, unknown> = { sprk_eventstatus: newStatus };
      if (additionalFields) {
        Object.assign(payload, additionalFields);
      }
      try {
        await Promise.all(cleanIds.map(id => xrm.WebApi.updateRecord(entityName, id, payload)));
        xrm.App?.addGlobalNotification?.({
          type: 2,
          level: 1,
          message: `${eventIds.length} event(s) set to ${statusLabel}`,
          showCloseButton: true,
        });
        console.log(`[useEventsBulkActions] Successfully updated ${eventIds.length} events to ${statusLabel}`);
        return true;
      } catch (error) {
        console.error('[useEventsBulkActions] Bulk status update failed:', error);
        xrm.Navigation?.openAlertDialog?.({
          title: 'Error',
          text: `Some events failed to update: ${error instanceof Error ? error.message : String(error)}`,
        });
        return false;
      }
    },
    [getXrm, entityName]
  );

  const archiveEvents = React.useCallback<UseEventsBulkActionsApi['archiveEvents']>(
    async eventIds => {
      const xrm = getXrm();
      if (!xrm?.WebApi) {
        console.warn('[useEventsBulkActions] Xrm.WebApi not available. Cannot archive.');
        return false;
      }
      const cleanIds = eventIds.map(id => id.replace(/[{}]/g, ''));
      try {
        await Promise.all(
          cleanIds.map(async id => {
            // Two-step sequence (preserved verbatim from EventsPage inline impl).
            // 1) Set custom status to ARCHIVED.
            await xrm.WebApi.updateRecord(entityName, id, {
              sprk_eventstatus: EventStatus.ARCHIVED,
            });
            // 2) Deactivate the record.
            await xrm.WebApi.updateRecord(entityName, id, {
              statecode: StateCode.INACTIVE,
              statuscode: 2,
            });
          })
        );
        xrm.App?.addGlobalNotification?.({
          type: 2,
          level: 1,
          message: `${eventIds.length} event(s) archived`,
          showCloseButton: true,
        });
        console.log(`[useEventsBulkActions] Successfully archived ${eventIds.length} events`);
        return true;
      } catch (error) {
        console.error('[useEventsBulkActions] Bulk archive failed:', error);
        xrm.Navigation?.openAlertDialog?.({
          title: 'Error',
          text: `Some events failed to archive: ${error instanceof Error ? error.message : String(error)}`,
        });
        return false;
      }
    },
    [getXrm, entityName]
  );

  // ── High-level confirm + action handlers ──────────────────────────────────
  // Confirm-dialog text + button labels: EventsPage canonical strings.

  const completeEvents = React.useCallback<UseEventsBulkActionsApi['completeEvents']>(
    async eventIds => {
      if (eventIds.length === 0) return false;
      const xrm = getXrm();
      const confirmed = await confirmAction(
        xrm,
        'Complete Events',
        `Mark ${eventIds.length} event(s) as complete?`,
        'Complete',
        'Cancel'
      );
      if (!confirmed) return false;
      return updateStatus(eventIds, EventStatus.COMPLETED, 'Completed', {
        sprk_completeddate: new Date().toISOString(),
      });
    },
    [getXrm, updateStatus]
  );

  const closeEvents = React.useCallback<UseEventsBulkActionsApi['closeEvents']>(
    async eventIds => {
      if (eventIds.length === 0) return false;
      const xrm = getXrm();
      const confirmed = await confirmAction(
        xrm,
        'Close Events',
        `Close ${eventIds.length} event(s) without action?`,
        'Close',
        'Cancel'
      );
      if (!confirmed) return false;
      return updateStatus(eventIds, EventStatus.CLOSED, 'Closed');
    },
    [getXrm, updateStatus]
  );

  const cancelEvents = React.useCallback<UseEventsBulkActionsApi['cancelEvents']>(
    async eventIds => {
      if (eventIds.length === 0) return false;
      const xrm = getXrm();
      const confirmed = await confirmAction(
        xrm,
        'Cancel Events',
        `Cancel ${eventIds.length} event(s)?`,
        'Cancel Events',
        'Keep Active'
      );
      if (!confirmed) return false;
      return updateStatus(eventIds, EventStatus.CANCELLED, 'Cancelled');
    },
    [getXrm, updateStatus]
  );

  const holdEvents = React.useCallback<UseEventsBulkActionsApi['holdEvents']>(
    async eventIds => {
      if (eventIds.length === 0) return false;
      const xrm = getXrm();
      const confirmed = await confirmAction(
        xrm,
        'Put Events On Hold',
        `Put ${eventIds.length} event(s) on hold?`,
        'Put On Hold',
        'Cancel'
      );
      if (!confirmed) return false;
      return updateStatus(eventIds, EventStatus.ON_HOLD, 'On Hold');
    },
    [getXrm, updateStatus]
  );

  const archiveSelectedEvents = React.useCallback<UseEventsBulkActionsApi['archiveSelectedEvents']>(
    async eventIds => {
      if (eventIds.length === 0) return false;
      const xrm = getXrm();
      const confirmed = await confirmAction(
        xrm,
        'Archive Events',
        `Archive ${eventIds.length} event(s)?\n\nThis will hide them from active views.`,
        'Archive',
        'Cancel'
      );
      if (!confirmed) return false;
      return archiveEvents(eventIds);
    },
    [getXrm, archiveEvents]
  );

  return {
    updateStatus,
    archiveEvents,
    completeEvents,
    closeEvents,
    cancelEvents,
    holdEvents,
    archiveSelectedEvents,
  };
}
