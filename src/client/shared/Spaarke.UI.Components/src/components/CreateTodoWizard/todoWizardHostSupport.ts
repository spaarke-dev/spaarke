/**
 * todoWizardHostSupport.ts — the two host-side pieces every Create To Do host wires around
 * `TodoWizardDialog`, shared by the `sprk_createtodowizard` code page (navigateTo / ribbon path)
 * and the in-app `InAppWizardHost` (spaarke-ontology-platform-r1 task 112). Moved verbatim from
 * `src/solutions/CreateTodoWizard/src/main.tsx` so the two hosts cannot drift.
 *
 * 1. `withTodoCreatedBroadcast` — R4 task 100 (W-2) post-create refetch contract. After a
 *    successful `sprk_todo` create, post `{ type: 'sprk_todo:created' }` on the
 *    `sprk_todo:lifecycle` BroadcastChannel. The LegalWorkspace To Do widget shim
 *    (`src/solutions/LegalWorkspace/src/sections/todo.registration.ts`) subscribes and refetches.
 *    The constants MUST stay in lockstep with that shim's. BroadcastChannel delivers to every
 *    other channel object of the same name in the origin — another window/iframe (the code page)
 *    or the same window (the in-app host) alike.
 *
 * 2. `resolveCurrentUserContact` — smart-todo-r5 UAT 2026-08-17 (item #1): default "Assigned To"
 *    to the current user's CONTACT (`contact.sprk_systemuser` = the user).
 */

import type { IDataService } from '../../types/serviceInterfaces';
import { getXrm } from '../../utils/xrmContext';

const SPRK_TODO_ENTITY = 'sprk_todo';
/** BroadcastChannel name — lockstep with todo.registration.ts. */
export const SPRK_TODO_CHANNEL_NAME = 'sprk_todo:lifecycle';
/** Message type posted after a successful create — lockstep with todo.registration.ts. */
export const SPRK_TODO_CREATED = 'sprk_todo:created';

/**
 * Wrap an `IDataService` so successful `sprk_todo` creates broadcast `sprk_todo:created`. All other
 * operations pass through unmodified. No-op wrapper where BroadcastChannel is unavailable (the create
 * still succeeds; only the cross-surface refetch is missed).
 */
export function withTodoCreatedBroadcast(inner: IDataService): IDataService {
  if (typeof BroadcastChannel === 'undefined') return inner;

  // UAC-r2 task 147 r1c: the wrapper INHERITS from `inner` (Object.create) rather than spreading it, so a BFF-routed
  // `inner` (withBffChildWrites) stays recognised as routed — `TodoService` then calls THIS createRecord instead
  // of re-wrapping `inner` and sending the create to the BFF past the broadcast (which lost the refetch).
  return Object.assign(Object.create(inner) as IDataService, {
    createRecord: async (entityName: string, data: Record<string, unknown>) => {
      const result = await inner.createRecord(entityName, data);
      if (entityName === SPRK_TODO_ENTITY && result) {
        try {
          const channel = new BroadcastChannel(SPRK_TODO_CHANNEL_NAME);
          try {
            channel.postMessage({ type: SPRK_TODO_CREATED, todoId: result });
          } finally {
            channel.close();
          }
        } catch (err) {
          // Non-fatal — the create succeeded; only the refetch signal failed.
          console.warn(
            '[CreateTodoWizard] Failed to broadcast sprk_todo:created — widget will need manual refresh',
            err
          );
        }
      }
      return result;
    },
  });
}

/**
 * Resolve the current user's CONTACT (`sprk_todo.sprk_assignedto` targets `contact`, linked to the
 * user by `contact.sprk_systemuser`). `undefined` on any failure — the assignee then opens blank.
 */
export async function resolveCurrentUserContact(
  dataService: IDataService
): Promise<{ contactId: string; contactName?: string } | undefined> {
  try {
    // Shared cross-frame walker (task 081 / C-8).
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm: any = getXrm('utility');
    const rawUserId: string | undefined = xrm?.Utility?.getGlobalContext?.().userSettings?.userId;
    const userId = rawUserId ? rawUserId.replace(/[{}]/g, '') : '';
    if (!userId) return undefined;
    const res = await dataService.retrieveMultipleRecords(
      'contact',
      `?$select=contactid,fullname&$filter=_sprk_systemuser_value eq ${userId}&$top=2`
    );
    const first = (res.entities ?? [])[0] as { contactid?: string; fullname?: string } | undefined;
    return first?.contactid ? { contactId: first.contactid, contactName: first.fullname } : undefined;
  } catch (err) {
    console.warn('[CreateTodoWizard] current-user contact resolve failed:', err);
    return undefined;
  }
}
