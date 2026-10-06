/**
 * RegardingResolver — the BFF writes of v1.6.0 (unified-access-control-r2 task 147 r1, owner round 28).
 *
 * - `refileThroughBff`: a SAVED child host's re-file (set or clear of its regarding) goes to the BFF — the caller's own
 *   update, with the owner re-derived (the Secure Record Owners team under a secure record) and F3 on a move out of one.
 *   `PATCH /api/v1/child-records/{table}/{id}`, `/api/v1/events/{id}/filing` or `/api/communications/{id}/filing`.
 * - `isSecureRoot`: whether a project / matter / work assignment is secure, read as the user through `context.webAPI`.
 *
 * MSAL is bootstrapped lazily through `@spaarke/auth` (config from the Dataverse environment variables), on the first
 * re-file; `initAuth` coalesces a duplicate init for the same client id.
 */

import { authenticatedFetch, initAuth, resolveRuntimeConfig } from '@spaarke/auth';
import { updateChildRecordViaBff } from '@spaarke/ui-components/dist/utils/adapters/bffChildWriteAdapter';

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

/** Re-files a saved child host through the BFF. Rejects with the server's message (nothing written). */
export async function refileThroughBff(
  hostEntity: string,
  hostRecordId: string,
  payload: Record<string, unknown>
): Promise<void> {
  const bffBaseUrl = await ensureBffBaseUrl();
  await updateChildRecordViaBff(authenticatedFetch, bffBaseUrl, hostEntity, hostRecordId, payload);
}

/** The WebApi surface `isSecureRootReader` needs (context.webAPI). */
export interface ISecureFlagReader {
  retrieveRecord: (entityLogicalName: string, id: string, options?: string) => Promise<Record<string, unknown>>;
}

/**
 * Builds the `isSecureRoot` check over the host's WebApi: `true` / `false` from `sprk_issecure`, `null` when it cannot be
 * read or is empty (the caller treats `null` as "do not file before saving" — fail closed).
 */
export function isSecureRootReader(webApi: ISecureFlagReader) {
  return async (rootEntity: string, rootId: string): Promise<boolean | null> => {
    try {
      const row = await webApi.retrieveRecord(rootEntity, rootId.replace(/[{}]/g, ''), '?$select=sprk_issecure');
      const flag = row['sprk_issecure'];
      return typeof flag === 'boolean' ? flag : null;
    } catch {
      return null;
    }
  };
}
