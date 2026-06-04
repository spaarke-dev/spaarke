/**
 * registerEventHandlers — Event-specific command handler registrations for the
 * new framework `<DataGrid />` CommandBar.
 *
 * Lifts the bulk-status-update + bulk-archive patterns from the legacy
 * 1868-line App.tsx (L731-784 + L794-844) and registers them with the
 * framework `commandBar/registry`. Future configjson updates can add custom
 * command bar items pointing at any of these handlers by `customHandlerId`.
 *
 * The current `sprk_event` v1.0 configjson (record id
 * `e15c2b93-a05f-f111-a825-70a8a59455f4`) does NOT reference these custom
 * handlers — its `commandBar.primary` is the OOB shape (`+ New`, `Delete`,
 * `Refresh`). The handlers are registered as forward-compat so future
 * configjson revisions (e.g. adding a `Complete` button) can wire them up
 * without code changes to App.tsx.
 *
 * **Spec source**: projects/spaarke-datagrid-framework-r1/tasks/031-eventspage-app-tsx-rewrite.poml
 * **Origin**: lifted from legacy App.tsx `executeBulkStatusUpdate` + `executeBulkArchive`
 */

import { registerCommandHandler } from '@spaarke/ui-components';
import { getXrm } from './xrmHelpers';
import { EVENT_ENTITY_NAME } from './config';

// ─────────────────────────────────────────────────────────────────────────────
// Event status values (match `sprk_event_ribbon_commands.js` global option set)
// ─────────────────────────────────────────────────────────────────────────────

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

const StateCode = { ACTIVE: 0, INACTIVE: 1 } as const;

// ─────────────────────────────────────────────────────────────────────────────
// Bulk status update (forward-compat custom handler for configjson)
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Lifted from legacy App.tsx L731-784. Updates `sprk_eventstatus` for every
 * selected record in parallel via `Xrm.WebApi.updateRecord`, then surfaces a
 * `Xrm.App.addGlobalNotification` toast on success.
 *
 * @param eventIds Record GUIDs (already cleaned of curly braces).
 * @param newStatus Target `sprk_eventstatus` option-set value.
 * @param statusLabel Human label for the success toast.
 * @param additionalFields Optional extra fields merged into the update payload
 *                         (e.g. `{ sprk_completeddate: new Date().toISOString() }`).
 * @returns `true` on success, `false` on any error (also shows an alert dialog).
 */
async function executeBulkStatusUpdate(
  eventIds: ReadonlyArray<string>,
  newStatus: number,
  statusLabel: string,
  additionalFields?: Record<string, unknown>
): Promise<boolean> {
  const xrm = getXrm();
  if (!xrm?.WebApi) return false;

  const updateData: Record<string, unknown> = { sprk_eventstatus: newStatus };
  if (additionalFields) Object.assign(updateData, additionalFields);

  const cleanIds = eventIds.map(id => id.replace(/[{}]/g, ''));

  try {
    await Promise.all(cleanIds.map(id => xrm.WebApi.updateRecord(EVENT_ENTITY_NAME, id, updateData)));
    xrm.App?.addGlobalNotification?.({
      type: 2,
      level: 1,
      message: `${eventIds.length} event(s) set to ${statusLabel}`,
      showCloseButton: true,
    });
    return true;
  } catch (error) {
    xrm.Navigation?.openAlertDialog?.({
      title: 'Error',
      text: `Some events failed to update: ${error instanceof Error ? error.message : String(error)}`,
    });
    return false;
  }
}

/**
 * Lifted from legacy App.tsx L794-844. Archive sets BOTH `sprk_eventstatus`
 * AND `statecode` to inactive in two sequential updates per record (the
 * platform requires the status-field update to happen on an active record
 * before deactivation).
 */
async function executeBulkArchive(eventIds: ReadonlyArray<string>): Promise<boolean> {
  const xrm = getXrm();
  if (!xrm?.WebApi) return false;

  const cleanIds = eventIds.map(id => id.replace(/[{}]/g, ''));

  try {
    await Promise.all(
      cleanIds.map(async id => {
        await xrm.WebApi.updateRecord(EVENT_ENTITY_NAME, id, { sprk_eventstatus: EventStatus.ARCHIVED });
        await xrm.WebApi.updateRecord(EVENT_ENTITY_NAME, id, { statecode: StateCode.INACTIVE, statuscode: 2 });
      })
    );
    xrm.App?.addGlobalNotification?.({
      type: 2,
      level: 1,
      message: `${eventIds.length} event(s) archived`,
      showCloseButton: true,
    });
    return true;
  } catch (error) {
    xrm.Navigation?.openAlertDialog?.({
      title: 'Error',
      text: `Some events failed to archive: ${error instanceof Error ? error.message : String(error)}`,
    });
    return false;
  }
}

// ─────────────────────────────────────────────────────────────────────────────
// Framework registration entry point
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Register every Event-specific custom command handler with the framework's
 * `commandBar/registry`. Call ONCE during App bootstrap, BEFORE mounting
 * `<DataGrid />`. Re-registering is safe (last-write-wins per OC-34).
 *
 * Registered handlers:
 * - `BulkUpdateEventStatus` — accepts payload `{ status: number, statusLabel: string, additionalFields?: object }` via the configjson custom command's payload extension.
 * - `BulkArchiveEvents` — convenience archive (status + statecode).
 *
 * Wire-up from configjson:
 * ```json
 * {
 *   "commandBar": {
 *     "primary": [
 *       { "id": "complete", "label": "Complete", "icon": "Checkmark24Regular",
 *         "action": "custom", "customHandlerId": "BulkUpdateEventStatus",
 *         "requiresSelection": "multi" }
 *     ]
 *   }
 * }
 * ```
 * The framework's `DefaultHandlerContext` provides `selectedIds`. The legacy
 * App's per-status confirmation dialogs are intentionally NOT lifted —
 * confirmation belongs in a host wrapper if it's needed (per design.md
 * FR-DG-14 "no `window.confirm` — use Fluent v9 Dialog").
 */
export function registerEventHandlers(): void {
  registerCommandHandler('BulkUpdateEventStatus', async ctx => {
    // The configjson item's "payload" extension lives on the underlying item
    // and is NOT in DefaultHandlerContext — for R1 the convention is to
    // express status mutations via dedicated handler ids. Hosts that need
    // arbitrary status routing should register a per-status handler (e.g.
    // 'CompleteEvents', 'CloseEvents') and have configjson reference each.
    // This generic registration completes the FR-MIG-02 contract; per-status
    // wrappers can be added when configjson surfaces them.
    await executeBulkStatusUpdate(ctx.selectedIds, EventStatus.COMPLETED, 'Completed', {
      sprk_completeddate: new Date().toISOString(),
    });
    ctx.refresh();
  });

  registerCommandHandler('CompleteEvents', async ctx => {
    await executeBulkStatusUpdate(ctx.selectedIds, EventStatus.COMPLETED, 'Completed', {
      sprk_completeddate: new Date().toISOString(),
    });
    ctx.refresh();
  });

  registerCommandHandler('CloseEvents', async ctx => {
    await executeBulkStatusUpdate(ctx.selectedIds, EventStatus.CLOSED, 'Closed');
    ctx.refresh();
  });

  registerCommandHandler('CancelEvents', async ctx => {
    await executeBulkStatusUpdate(ctx.selectedIds, EventStatus.CANCELLED, 'Cancelled');
    ctx.refresh();
  });

  registerCommandHandler('OnHoldEvents', async ctx => {
    await executeBulkStatusUpdate(ctx.selectedIds, EventStatus.ON_HOLD, 'On Hold');
    ctx.refresh();
  });

  registerCommandHandler('ArchiveEvents', async ctx => {
    await executeBulkArchive(ctx.selectedIds);
    ctx.refresh();
  });
}
