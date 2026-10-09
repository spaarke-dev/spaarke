/**
 * Manage Access and the Access Permission pill on a record filed under a matter or project (unified-access-control-r2
 * task 175; owner round 87, which refined round 84): the parent sets a FLOOR. A filed work assignment or project inherits
 * Secure and Access Permission (the most restrictive across its parents and their chain) and may never be looser than
 * that floor; a user MAY make it stricter by hand, and remove that extra strictness again down to the floor. Grants and
 * shares on the child are unaffected.
 *
 * Pure functions, kept out of the component so the rules are pinned by tests and reused by the host (the same split as
 * `accessPermissionState.ts` and `noAccess.ts`). Entity-agnostic (ADR-012): the BFF names the parent as `'matter'` or
 * `'project'` and the floor in the semantic vocabulary; the host supplies its own option integers
 * ({@link IAccessPermissionValues}) and maps the parent to its own table when it opens it.
 */

import type { IAccessPermissionValues } from './accessPermissionState';
import type { AccessPermissionState, IAccessFloor, IAccessGrantRecord, IFollowsParent } from './types';

/** The BFF's refusal of a change that would make a record looser than its parent's floor (HTTP 409 ProblemDetails). */
export const ACCESS_FOLLOWS_PARENT_REASON_CODE = 'sdap.access.access_follows_parent';

const PARENT_TYPES = new Set(['matter', 'project']);
const ACCESS_PERMISSION_STATES = new Set<AccessPermissionState>(['standard', 'limited', 'restricted']);
const STATE_RANK: Readonly<Record<AccessPermissionState, number>> = { standard: 0, limited: 1, restricted: 2 };
const STATE_LABEL: Readonly<Record<AccessPermissionState, string>> = {
  standard: 'Standard',
  limited: 'Limited',
  restricted: 'Restricted',
};

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
 * Reads `followsParents` from a `can-manage-access` 200 body. Absent or not an array (an older BFF): `[]`. An entry that
 * is not a matter or project with an id is skipped.
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

/**
 * Reads the floor fields of a `can-manage-access` 200 body (`floorSecure`, `floorAccessPermission`,
 * `parentUnverifiable`). A field that is absent (an older BFF) or off-contract reads as `null` (no floor);
 * `parentUnverifiable` is `true` only when the body says exactly `true`.
 */
export function parseAccessFloor(body: unknown): IAccessFloor {
  const b = body && typeof body === 'object' ? (body as Record<string, unknown>) : {};
  const fs = b.floorSecure;
  const fap = b.floorAccessPermission;
  return {
    floorSecure: fs === true || fs === false ? fs : null,
    floorAccessPermission:
      typeof fap === 'string' && ACCESS_PERMISSION_STATES.has(fap as AccessPermissionState)
        ? (fap as AccessPermissionState)
        : null,
    parentUnverifiable: b.parentUnverifiable === true,
  };
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
  return (
    `This change would make this record's access looser than the ${parentTypeLabel(recordType)} it is filed under. ` +
    'A record can be made stricter than its parent, but not looser.'
  );
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

// ── The floor and the Access Permission pill (owner round 87) ─────────────────────────────────────────────────────

/** A raw Access Permission option value as a semantic state (Standard is "anything else", as the host maps it). */
export function accessPermissionStateOf(
  value: number | null | undefined,
  values: IAccessPermissionValues
): AccessPermissionState {
  if (value === values.restricted) return 'restricted';
  if (value === values.limited) return 'limited';
  return 'standard';
}

/** Whether a raw Access Permission value is looser than the floor (`false` when there is no floor). */
export function isLooserThanFloor(
  value: number | null | undefined,
  floor: AccessPermissionState | null | undefined,
  values: IAccessPermissionValues
): boolean {
  if (!floor) return false;
  return STATE_RANK[accessPermissionStateOf(value, values)] < STATE_RANK[floor];
}

/**
 * What the Access Permission pill offers on this record:
 * - `parentUnverifiable`: READ-ONLY (fail closed — what the record is filed under could not be read, so the floor is
 *   unknown), with the options unchanged.
 * - A floor: editable, offering only the options at or above it (Restricted over Limited over Standard). Should the
 *   host's options contain none of those, the pill is read-only rather than offering a looser value.
 * - No floor (a parentless record, or an older BFF): unchanged.
 */
export function resolveAccessPermissionPill<T extends { value: number }>(
  options: readonly T[],
  floor: IAccessFloor | null | undefined,
  values: IAccessPermissionValues
): { options: T[]; readOnly: boolean } {
  if (floor?.parentUnverifiable) return { options: [...options], readOnly: true };
  const min = floor?.floorAccessPermission;
  if (!min) return { options: [...options], readOnly: false };
  const offered = options.filter(o => !isLooserThanFloor(o.value, min, values));
  return offered.length > 0 ? { options: offered, readOnly: false } : { options: [...options], readOnly: true };
}

/** Where a record's effective Secure / Access Permission value comes from. `null`: nothing to say (no floor known). */
export type AccessValueOrigin = { kind: 'inherited'; parent: IFollowsParent | null } | { kind: 'setOnRecord' } | null;

/**
 * Where the record's effective Access Permission comes from (owner round 87):
 * - above the floor → set on this record;
 * - at the floor, and the floor is above Standard → inherited from the first parent;
 * - Standard with a Standard floor, or no floor → `null` (no annotation).
 * A value BELOW the floor (stored before the floor took effect) is enforced as the floor, so it reads as inherited.
 */
export function accessPermissionOrigin(
  value: AccessPermissionState,
  floor: AccessPermissionState | null | undefined,
  parents: readonly IFollowsParent[]
): AccessValueOrigin {
  if (!floor) return null;
  if (STATE_RANK[value] > STATE_RANK[floor]) return { kind: 'setOnRecord' };
  if (floor === 'standard') return null;
  return { kind: 'inherited', parent: parents[0] ?? null };
}

/** Where the record's Secure flag comes from: secure with a secure floor → inherited; secure without → set on this
 * record; not secure, or no floor known → `null`. */
export function secureOrigin(
  isSecure: boolean,
  floorSecure: boolean | null | undefined,
  parents: readonly IFollowsParent[]
): AccessValueOrigin {
  if (!isSecure || floorSecure === null || floorSecure === undefined) return null;
  return floorSecure ? { kind: 'inherited', parent: parents[0] ?? null } : { kind: 'setOnRecord' };
}

/** "inherited from Matter Acme v. Beta" / "inherited from the parent matter" / "set on this record"; `''` for `null`. */
export function describeOrigin(origin: AccessValueOrigin): string {
  if (!origin) return '';
  if (origin.kind === 'setOnRecord') return 'set on this record';
  const parent = origin.parent;
  if (!parent) return 'inherited from the parent record';
  const label = parentTypeLabel(parent.recordType);
  return parent.name
    ? `inherited from ${label.charAt(0).toUpperCase()}${label.slice(1)} ${parent.name}`
    : `inherited from the parent ${label}`;
}

/**
 * The effective values as a person reads them, e.g. `["Access Permission: Restricted (inherited from Matter X)",
 * "Secure (set on this record)"]`. The effective Access Permission is the stricter of the record's own and the floor.
 * `Secure` is listed only for a secure record.
 */
export function describeEffectiveAccess(input: {
  accessPermission: AccessPermissionState;
  isSecure: boolean;
  floor: IAccessFloor | null | undefined;
  parents: readonly IFollowsParent[];
}): string[] {
  const floorAp = input.floor?.floorAccessPermission ?? null;
  const effective =
    floorAp && STATE_RANK[floorAp] > STATE_RANK[input.accessPermission] ? floorAp : input.accessPermission;
  const apOrigin = describeOrigin(accessPermissionOrigin(effective, floorAp, input.parents));
  const lines = [`Access Permission: ${STATE_LABEL[effective]}${apOrigin ? ` (${apOrigin})` : ''}`];
  if (input.isSecure) {
    const sOrigin = describeOrigin(secureOrigin(true, input.floor?.floorSecure, input.parents));
    lines.push(`Secure${sOrigin ? ` (${sOrigin})` : ''}`);
  }
  return lines;
}
