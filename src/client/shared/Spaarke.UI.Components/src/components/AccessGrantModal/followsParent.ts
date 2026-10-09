/**
 * Manage Access — a child record whose access follows its parent (unified-access-control-r2 task 175, owner round 84:
 * "a child's access always follows its parent, both ways, and is locked while it has a parent").
 *
 * Pure functions, kept out of the component so the rules are pinned by tests and reused by the host (the same split as
 * `accessPermissionState.ts` and `noAccess.ts`). Entity-agnostic (ADR-012): the BFF names the parent as `'matter'` or
 * `'project'`; the host maps that to its own table when it opens the parent.
 */

import type { IAccessGrantRecord, IFollowsParent } from './types';

/** The BFF's refusal of an access write on a record that follows its parent (HTTP 409 ProblemDetails). */
export const ACCESS_FOLLOWS_PARENT_REASON_CODE = 'sdap.access.access_follows_parent';

const PARENT_TYPES = new Set(['matter', 'project']);

/** One parent from the wire, or `null` when it is not a matter or project with an id. */
export function parseFollowsParent(raw: unknown): IFollowsParent | null {
  if (!raw || typeof raw !== 'object') return null;
  const p = raw as Record<string, unknown>;
  if (typeof p.recordType !== 'string' || !PARENT_TYPES.has(p.recordType)) return null;
  if (typeof p.recordId !== 'string' || !p.recordId) return null;
  return {
    recordType: p.recordType as IFollowsParent['recordType'],
    recordId: p.recordId,
    name: typeof p.name === 'string' && p.name ? p.name : null,
  };
}

/**
 * Reads `followsParents` from a `can-manage-access` 200 body. Absent or not an array (an older BFF): `[]`, so the record
 * is not locked here; the server still refuses a write on a child with a parent, and the modal then shows the lock. An
 * entry that is not a matter or project with an id is skipped.
 */
export function parseFollowsParents(raw: unknown): IFollowsParent[] {
  if (!Array.isArray(raw)) return [];
  const parents: IFollowsParent[] = [];
  for (const item of raw) {
    const parent = parseFollowsParent(item);
    if (parent) parents.push(parent);
  }
  return parents;
}

/** `'matter'` / `'project'` (also accepts the table logical names); anything else reads as "record". */
export function parentTypeLabel(recordType: string | null | undefined): string {
  switch (recordType) {
    case 'matter':
    case 'sprk_matter':
      return 'matter';
    case 'project':
    case 'sprk_project':
      return 'project';
    default:
      return 'record';
  }
}

/** The designed sentence for a 409 `access_follows_parent` that carried no `detail` of its own. */
export function followsParentFallbackMessage(recordType: string | null | undefined): string {
  return `Access to this record follows the ${parentTypeLabel(recordType)} it is filed under. Change it there.`;
}

/** " and 1 other" / " and N others" after the first parent's name; empty for a single parent. */
export function otherParentsSuffix(parentCount: number): string {
  const others = parentCount - 1;
  if (others <= 0) return '';
  return others === 1 ? ' and 1 other' : ` and ${others} others`;
}

/** The Current Access label of a user share a secure parent passed on to the record (`provenance: 'inherited'`). */
export function describeInheritedShare(grant: IAccessGrantRecord): string {
  const label = parentTypeLabel(grant.inheritedFrom?.recordType);
  return label === 'record' ? 'Inherited from a parent record' : `Inherited from the ${label}`;
}
