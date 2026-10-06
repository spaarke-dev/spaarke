/**
 * SmartTodo — child-record writes through the BFF.
 *
 * unified-access-control-r2 task 147 r1 (owner round 28 item 1, "E1 = A1, the existing G5 pattern"). A to-do created in
 * the browser through `Xrm.WebApi` is owned by the user, in the user's own business unit — a to-do regarding a secure
 * project would be readable by everyone in that unit. The owner is a server invariant (ADR-002 WP-1), so this code page's
 * to-do creates go to the BFF (`POST /api/v1/child-records/sprk_todo`), which checks the caller's rights and creates the
 * row owned by the team the ownership rule names. The client never sets the owner.
 *
 * The Kanban does not bootstrap MSAL on a normal load (`SmartTodoApp`'s launch host initialises it lazily); a create
 * initialises it on first use — `initAuth` coalesces a duplicate init for the same client id.
 */

import { authenticatedFetch, initAuth, resolveRuntimeConfig } from '@spaarke/auth';
import { createChildRecordViaBff } from '@spaarke/ui-components';

let bffBaseUrlPromise: Promise<string> | null = null;

/** The BFF base URL, initialising `@spaarke/auth` once. A failed init is retried on the next call. */
export function ensureBffBaseUrl(): Promise<string> {
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

/** Creates a `sprk_todo` through the BFF (G5) and returns its id. Rejects with the server's message. */
export async function createTodoThroughBff(payload: Record<string, unknown>): Promise<string> {
  const bffBaseUrl = await ensureBffBaseUrl();
  return createChildRecordViaBff(authenticatedFetch, bffBaseUrl, 'sprk_todo', payload);
}
