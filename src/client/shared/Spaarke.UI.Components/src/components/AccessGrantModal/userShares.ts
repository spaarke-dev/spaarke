/**
 * The record's internal system-user shares (unified-access-control-r2 task 063/065, FR-29): the ONE reader of
 * `GET /api/v1/external-access/user-shares`, shared by the Manage Access modal's Current Access and the TrackingFieldTrio
 * host's "Email members" recipients (task 196), so "who has access" and "who gets emailed" read the same list the same
 * way. Pure: the caller makes the request (the modal through its error-mapping `getJson`, the email resolver through the
 * host's `authenticatedFetch`).
 */

import type { ExternalGrantRootType, IAccessGrantRecord } from './types';

/** The relative BFF path of the record's user-share read. */
export function buildUserSharesPath(recordType: ExternalGrantRootType, recordId: string): string {
  return `/api/v1/external-access/user-shares?recordType=${encodeURIComponent(recordType)}&recordId=${encodeURIComponent(recordId)}`;
}

/** One item of the `/user-shares` answer. Untrusted server JSON: every field is checked before use. */
interface IUserShareDto {
  systemUserId: string;
  fullName?: string | null;
  accessLevel?: number | null;
  modifiedOn?: string;
  externalNoAccess?: boolean;
  // Task 175: the secure parent that passed this share on (task 158's provenance); absent/null = a direct share.
  inheritedFrom?: unknown;
  // Task 193 G2: the workforce-app grant paired with this share (`kind: 'grant'`, its accessRecordId).
  pairedWith?: { id?: unknown; kind?: unknown } | null;
}

/**
 * Maps a `/user-shares` body to Current Access rows with `provenance: 'share'` (or `'inherited'`). The row's `contactId`
 * carries the systemUserId for row-keying — the convention the modal uses for organization-grant rows — since a share
 * has no contact at all. A body without `shares` reads as an empty list.
 */
export function mapUserShares(data: { shares?: IUserShareDto[] } | null | undefined): IAccessGrantRecord[] {
  return (data?.shares ?? []).map(s => {
    const raw = s.inheritedFrom;
    const from =
      raw !== null && typeof raw === 'object' ? (raw as { recordType?: unknown; recordId?: unknown }) : undefined;
    // Any inheritedFrom object makes the row read-only (inherited), even one whose fields are off-contract: a share
    // the parent owns is never offered for revoke here.
    const inheritedFrom = from
      ? {
          recordType: typeof from.recordType === 'string' ? from.recordType : '',
          recordId: typeof from.recordId === 'string' ? from.recordId : '',
        }
      : undefined;
    const row: IAccessGrantRecord = {
      contactId: s.systemUserId,
      fullName: s.fullName ?? '(unknown user)',
      // Unmapped mask (a share holding rights outside the three levels) reads
      // as `null` server-side; 0 is a safe sentinel — it matches none of the
      // fixed ExternalAccessLevel option values, so the row falls through to
      // the "Custom" display rather than rendering a raw `null`.
      accessLevel: s.accessLevel ?? 0,
      grantedDate: s.modifiedOn,
      provenance: inheritedFrom ? 'inherited' : 'share',
      // Task 114: Restricted record + user flagged external — shown as "External user — no access" until removed.
      externalNoAccess: s.externalNoAccess === true,
    };
    if (inheritedFrom) row.inheritedFrom = inheritedFrom;
    if (s.pairedWith?.kind === 'grant' && typeof s.pairedWith.id === 'string' && s.pairedWith.id) {
      row.pairedWith = { id: s.pairedWith.id, kind: 'grant' };
    }
    return row;
  });
}
