/**
 * Notepad — the memo CREATE goes through the BFF.
 *
 * unified-access-control-r2 task 147 r1 (owner round 28 item 1, "E1 = A1, the existing G5 pattern"). A memo created in
 * the browser through `Xrm.WebApi` is owned by the user, in the user's own business unit — a memo on a secure project (or
 * on one of its events) would be readable by everyone in that unit. The owner is a server invariant (ADR-002 WP-1), so the
 * create is sent to `POST /api/v1/child-records/sprk_memo`: the BFF checks the caller's rights and creates the memo owned
 * by the team the ownership rule names (the Secure Record Owners team under a secure record), recording the caller as its
 * creator person.
 *
 * This supersedes, for the memo CREATE only, record-header-and-notepad-r1's NFR-05 ("zero @spaarke/auth") and NFR-07
 * ("zero BFF calls") — a CLAUDE.md §6.5 path-B amendment decided by UAC-r2 owner round 28 (main session, under the
 * owner's standing directive). Reads and the body/name saves stay on `Xrm.WebApi` as the user.
 *
 * MSAL is initialised lazily, on the first create (`initAuth` coalesces a duplicate init for the same client id).
 */

import { authenticatedFetch, initAuth, resolveRuntimeConfig } from '@spaarke/auth';
import { createChildRecordViaBff } from '@spaarke/ui-components/utils/adapters/bffChildWriteAdapter';

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

/** Creates a `sprk_memo` through the BFF (G5) and returns its id. Rejects with the server's message. */
export async function createMemoThroughBff(payload: Record<string, unknown>): Promise<string> {
  const bffBaseUrl = await ensureBffBaseUrl();
  return createChildRecordViaBff(authenticatedFetch, bffBaseUrl, 'sprk_memo', payload);
}
