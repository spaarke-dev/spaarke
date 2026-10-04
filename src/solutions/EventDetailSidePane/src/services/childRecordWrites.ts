/**
 * Event side pane — child-record creates and the event's re-file go through the BFF.
 *
 * unified-access-control-r2 task 147 r1 (owner round 28 item 1, "E1 = A1, the existing G5 pattern"). A to-do or memo
 * created in the browser through `Xrm.WebApi` is owned by the user, in the user's own business unit — one regarding an
 * event of a secure project would be readable by everyone in that unit. The owner is a server invariant (ADR-002 WP-1):
 *
 * - creates go to `POST /api/v1/child-records/{table}` — the BFF checks the caller's rights and creates the row owned by
 *   the team the ownership rule names (the Secure Record Owners team under a secure record);
 * - a change to the event's LOOKUPS (what it is filed under) goes to `PATCH /api/v1/events/{id}/filing` — the caller's own
 *   update, with the owner re-derived and assigned (round 28: "re-files go through the existing families").
 *
 * The pane did not bootstrap MSAL before; it does so lazily, on the first such write (`initAuth` coalesces a duplicate
 * init for the same client id).
 */

import { authenticatedFetch, initAuth, resolveRuntimeConfig } from '@spaarke/auth';
import { createChildRecordViaBff, updateChildRecordViaBff } from '@spaarke/ui-components';

let bffBaseUrlPromise: Promise<string> | null = null;

function ensureBffBaseUrl(): Promise<string> {
  if (!bffBaseUrlPromise) {
    bffBaseUrlPromise = (async () => {
      const config = await resolveRuntimeConfig();
      await initAuth({
        clientId: config.msalClientId,
        bffBaseUrl: config.bffBaseUrl,
        bffApiScope: config.bffOAuthScope,
        tenantId: config.tenantId || undefined,
        proactiveRefresh: true,
      });
      return config.bffBaseUrl;
    })();
    bffBaseUrlPromise.catch(() => {
      bffBaseUrlPromise = null;
    });
  }
  return bffBaseUrlPromise;
}

/** Creates a child record (to-do, memo) through the BFF (G5) and returns its id. Rejects with the server's message. */
export async function createChildThroughBff(table: string, payload: Record<string, unknown>): Promise<string> {
  const bffBaseUrl = await ensureBffBaseUrl();
  return createChildRecordViaBff(authenticatedFetch, bffBaseUrl, table, payload);
}

/** Re-files the event (its `@odata.bind` lookups) through the BFF. Rejects with the server's message. */
export async function refileEventThroughBff(eventId: string, payload: Record<string, unknown>): Promise<void> {
  const bffBaseUrl = await ensureBffBaseUrl();
  await updateChildRecordViaBff(authenticatedFetch, bffBaseUrl, 'sprk_event', eventId, payload);
}
