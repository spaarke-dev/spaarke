/**
 * assignedAccessSync — ask the BFF to apply the Assigned-To access rule to a record a client writer just created
 * (unified-access-control-r2 task 142; owner Q5 + round 3 R3 "immediate": wizard-created assignees get access at
 * creation, not at the next job tick).
 *
 * The client writers (Create Matter / Create Project / Create Work Assignment wizards) create the record with
 * `Xrm.WebApi` / the host data service, so no BFF writer ran — this one call is their L1 trigger
 * (DATAVERSE-WRITE-PATH-ARCHITECTURE WP-2/WP-3: the client previews, the SERVER owns the invariant). The body carries
 * ONLY the record; the server reads the record's own "Assigned *" columns and never trusts a client-supplied subject.
 *
 * NEVER throws and never fails the wizard: a failed call is logged and the reconciliation job (≤ 5 minutes) is the
 * safety net. ADR-028: the host's `authenticatedFetch` is passed in as a function dependency — never a raw token.
 */

import type { AuthenticatedFetchFn } from './EntityCreationService';

/** The three Assigned-To roots — the BFF's `recordType` tokens. */
export type AssignedAccessRecordType = 'project' | 'matter' | 'workassignment';

/** What the call came to: `ok` only on a 2xx. `reason` is the server's `reasonCode`, or the client-side cause. */
export interface IAssignedAccessSyncResult {
  ok: boolean;
  status?: number;
  reason?: string;
}

/** The route — kept in one place so a test pins it. */
export const ASSIGNED_ACCESS_SYNC_PATH = '/api/v1/external-access/assigned-access/sync';

function stripBraces(id: string): string {
  return id
    .trim()
    .replace(/^\{|\}$/g, '')
    .toLowerCase();
}

/**
 * Calls `POST {bffBaseUrl}/api/v1/external-access/assigned-access/sync` with `{ recordType, recordId }`.
 * Resolves (never rejects) with what happened; the caller does not need to look at it.
 */
export async function syncAssignedAccess(
  authenticatedFetch: AuthenticatedFetchFn | undefined,
  bffBaseUrl: string | undefined,
  recordType: AssignedAccessRecordType,
  recordId: string | undefined
): Promise<IAssignedAccessSyncResult> {
  if (!authenticatedFetch || !bffBaseUrl || !recordId) {
    console.warn(
      '[AssignedAccessSync] Skipped: no authenticated BFF client or record id. The reconciliation job grants access ' +
        'within minutes.'
    );
    return { ok: false, reason: 'not-configured' };
  }

  try {
    const response = await authenticatedFetch(`${bffBaseUrl.replace(/\/+$/, '')}${ASSIGNED_ACCESS_SYNC_PATH}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ recordType, recordId: stripBraces(recordId) }),
    });

    if (response.ok) {
      return { ok: true, status: response.status };
    }

    let reason: string | undefined;
    try {
      const body = (await response.json()) as { reasonCode?: string };
      reason = body?.reasonCode;
    } catch {
      // A non-JSON error body: the status is enough for the log.
    }
    console.warn(
      `[AssignedAccessSync] The BFF answered ${response.status}${reason ? ` (${reason})` : ''} for ${recordType} ` +
        `${recordId}. The record was created; the reconciliation job grants access within minutes.`
    );
    return { ok: false, status: response.status, reason };
  } catch (err) {
    console.warn(
      `[AssignedAccessSync] The call failed for ${recordType} ${recordId}. The record was created; the reconciliation ` +
        'job grants access within minutes.',
      err
    );
    return { ok: false, reason: 'network' };
  }
}
