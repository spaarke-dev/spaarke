/**
 * CommunicationConnections — the BFF re-file of v1.7.0 (unified-access-control-r2 task 147 r1, owner round 28 item 1).
 *
 * A communication's regarding writes (file under a record, unlink, clear the primary) move it into or out of a record,
 * so its owner follows: they go to the communications family's `PATCH /api/communications/{id}/filing` — the caller's
 * own update, the owner re-derived and assigned (F3 on a move out of a secure record). The association status and the
 * override reason stay on `context.webAPI` (plain columns of the communication).
 *
 * MSAL is bootstrapped lazily through `@spaarke/auth` (config from the Dataverse environment variables) on the first
 * re-file; `initAuth` coalesces a duplicate init for the same client id. A failed bootstrap is not cached, so the next
 * re-file tries again.
 */

import { authenticatedFetch, initAuth, resolveRuntimeConfig } from '@spaarke/auth';
import { bffRefile } from '@spaarke/communication-components/logic/connections';

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

/** Re-files the host communication through the BFF. Rejects with the server's message (nothing written). */
export async function refileThroughBff(
  hostEntity: string,
  hostRecordId: string,
  payload: Record<string, unknown>
): Promise<void> {
  const bffBaseUrl = await ensureBffBaseUrl();
  await bffRefile(authenticatedFetch, bffBaseUrl)(hostEntity, hostRecordId, payload);
}
