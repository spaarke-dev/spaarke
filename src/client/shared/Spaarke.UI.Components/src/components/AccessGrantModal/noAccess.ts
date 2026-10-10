/**
 * Manage Access — the read-only No Access List and the row states it drives (unified-access-control-r2 task 067,
 * owner round 59 item 3; task 066 folded in).
 *
 * Pure functions, kept out of the component so the rules are pinned by tests without rendering (the same split as
 * `accessPermissionState.ts`):
 *  - {@link parseNoAccessResponse} reads task 064's `GET /api/v1/records/{table}/{id}/no-access` answer. The contract
 *    is frozen in `projects/unified-access-control-r2/notes/phase4-access-report-contract.md`; every answer it cannot
 *    trust (a non-200, an unparseable body, a body about another record, `entriesState: unavailable`) is an ERROR
 *    state, never "no entries".
 *  - {@link vetoFor} decides whether a Current Access row is walled off. Only an entry with `inForce: true` vetoes.
 *  - {@link suppressionFor} decides whether a row is cancelled by the record's Secure / Limited / Restricted policy,
 *    styled from the props the host already passes (`accessPermissionState`, `isSecureRecord`), as owner round 59
 *    folded 066 into this task. The server enforces both; these only stop the dialog reading as if the access works.
 *
 * Read-only by design: authoring stays in task 154's No Access Entries form, the ONE place walls are written.
 */

import { cleanGuid } from '../../utils/guid';
import type { AccessPermissionState, ExternalGrantRootType, IAccessGrantRecord, IRecordNoAccessEntry } from './types';

/**
 * The table segment of 064's route for each root type (a BFF route contract, like the fixed access-level values, not a
 * Dataverse lookup the modal performs). 064 maps exactly these three; any other type has no route.
 */
const NO_ACCESS_ROUTE_TABLE: Readonly<Record<ExternalGrantRootType, string>> = {
  project: 'sprk_project',
  matter: 'sprk_matter',
  workassignment: 'sprk_workassignment',
};

/** The relative BFF path of the record's No Access read. The id is canonicalized (ADR-044) before it enters the URL. */
export function buildNoAccessPath(recordType: ExternalGrantRootType, recordId: string): string {
  return `/api/v1/records/${NO_ACCESS_ROUTE_TABLE[recordType]}/${encodeURIComponent(cleanGuid(recordId))}/no-access`;
}

/** What the No Access List section shows. */
export type NoAccessSectionState =
  | { kind: 'loading' }
  /** 064 answered `notShown` (the caller lacks Write, owner O2): the section is not rendered. */
  | { kind: 'hidden' }
  /** The list could not be read or trusted. Never shown as an empty list. */
  | { kind: 'error' }
  /** `complete` or `truncated` (the first 100, `NoAccessShareEnforcer.MaxEntriesPerRecord`). */
  | { kind: 'list'; entries: IRecordNoAccessEntry[]; truncated: boolean };

const SUBJECT_KINDS = new Set(['contact', 'organization', 'systemuser']);
const OBJECT_KINDS = new Set(['record', 'organization']);

function optString(v: unknown): string | null | undefined {
  if (v === null || v === undefined) return v as null | undefined;
  return typeof v === 'string' ? v : undefined;
}

/** One entry from the wire, or `null` when it does not match the contract (the whole answer is then not trusted). */
function parseEntry(raw: unknown): IRecordNoAccessEntry | null {
  if (!raw || typeof raw !== 'object') return null;
  const e = raw as Record<string, unknown>;
  if (typeof e.entryId !== 'string' || !e.entryId) return null;
  if (typeof e.coveredRecordType !== 'string' || typeof e.coveredRecordId !== 'string') return null;
  if (!(e.inForce === true || e.inForce === false || e.inForce === null)) return null;
  const subjectKind = e.subjectKind === null || e.subjectKind === undefined ? null : e.subjectKind;
  if (subjectKind !== null && (typeof subjectKind !== 'string' || !SUBJECT_KINDS.has(subjectKind))) return null;
  const objectKind = e.objectKind === null || e.objectKind === undefined ? null : e.objectKind;
  if (objectKind !== null && (typeof objectKind !== 'string' || !OBJECT_KINDS.has(objectKind))) return null;
  return {
    entryId: e.entryId,
    name: optString(e.name),
    subjectKind: subjectKind as IRecordNoAccessEntry['subjectKind'],
    subjectId: optString(e.subjectId),
    subjectName: optString(e.subjectName),
    objectKind: objectKind as IRecordNoAccessEntry['objectKind'],
    objectOrganizationId: optString(e.objectOrganizationId),
    objectOrganizationName: optString(e.objectOrganizationName),
    coveredRecordType: e.coveredRecordType,
    coveredRecordId: e.coveredRecordId,
    viaSecureParent: e.viaSecureParent === true,
    alsoViaSecureParent: e.alsoViaSecureParent === true,
    malformed: e.malformed === true,
    inForce: e.inForce as boolean | null,
    notInForceReason: optString(e.notInForceReason),
    modifiedById: optString(e.modifiedById),
    modifiedByName: optString(e.modifiedByName),
    modifiedOn: optString(e.modifiedOn),
  };
}

/**
 * Reads a 200 body of 064's route for the record the dialog is about. Fails to `error` on anything it cannot trust:
 * a body that is not an object, a `recordId` that does not name THIS record (compared canonically, ADR-044 — the host
 * may hold a braced id while the server echoes the bare one), an unknown `entriesState`, `unavailable`, or a listed
 * state whose entries are missing or off-contract.
 */
export function parseNoAccessResponse(body: unknown, recordId: string): NoAccessSectionState {
  if (!body || typeof body !== 'object') return { kind: 'error' };
  const b = body as Record<string, unknown>;
  if (typeof b.recordId !== 'string' || cleanGuid(b.recordId) !== cleanGuid(recordId)) return { kind: 'error' };
  switch (b.entriesState) {
    case 'notShown':
      return { kind: 'hidden' };
    case 'complete':
    case 'truncated': {
      if (!Array.isArray(b.entries)) return { kind: 'error' };
      const entries: IRecordNoAccessEntry[] = [];
      for (const raw of b.entries) {
        const parsed = parseEntry(raw);
        if (!parsed) return { kind: 'error' };
        entries.push(parsed);
      }
      return { kind: 'list', entries, truncated: b.entriesState === 'truncated' };
    }
    default:
      // 'unavailable', or a value this client does not know.
      return { kind: 'error' };
  }
}

/**
 * Task 174 (owner round 84; task 067's amendment): the record's EFFECTIVE access as 064 reports it — a work assignment or
 * project filed under a secure, Limited or Restricted parent is enforced as its parent is, whatever its own stored values
 * read (until task 175's cascade writes them). `accessPermission` is `undefined` when the server did not report one (an
 * older BFF): nothing is then folded in for it.
 */
export interface IEffectiveRecordAccess {
  /** `secure`: `true` (applies), `false` (does not apply), `null` (unknown). */
  isSecure: boolean | null;
  /** The effective Access Permission; `null` when the server could not establish it; `undefined` when not reported. */
  accessPermission: AccessPermissionState | null | undefined;
  /** The record the effective values are inherited from, when an ancestor makes them stricter than the record's own. */
  inheritedFrom: { recordType: string; recordId: string; name: string | null } | null;
}

const ACCESS_PERMISSION_STATES = new Set<AccessPermissionState>(['standard', 'limited', 'restricted']);

/**
 * Reads the effective-access fields of a 200 body of 064's route (task 174). `null` when the body cannot be trusted (not
 * an object, about another record, or no recognisable `secure` signal) — the dialog then keeps the host's values, as
 * before this task.
 */
export function parseEffectiveAccess(body: unknown, recordId: string): IEffectiveRecordAccess | null {
  if (!body || typeof body !== 'object') return null;
  const b = body as Record<string, unknown>;
  if (typeof b.recordId !== 'string' || cleanGuid(b.recordId) !== cleanGuid(recordId)) return null;
  let isSecure: boolean | null;
  switch (b.secure) {
    case 'applies':
      isSecure = true;
      break;
    case 'doesNotApply':
      isSecure = false;
      break;
    case 'unknown':
      isSecure = null;
      break;
    default:
      return null;
  }
  let accessPermission: AccessPermissionState | null | undefined;
  if (b.accessPermission === undefined) {
    accessPermission = undefined;
  } else if (
    typeof b.accessPermission === 'string' &&
    ACCESS_PERMISSION_STATES.has(b.accessPermission as AccessPermissionState)
  ) {
    accessPermission = b.accessPermission as AccessPermissionState;
  } else {
    // 'unknown', or a value this client does not know: not established (folded in fail-closed below).
    accessPermission = null;
  }
  let inheritedFrom: IEffectiveRecordAccess['inheritedFrom'] = null;
  const from = b.inheritedFrom as Record<string, unknown> | null | undefined;
  if (from && typeof from === 'object' && typeof from.recordType === 'string' && typeof from.recordId === 'string') {
    inheritedFrom = {
      recordType: from.recordType,
      recordId: from.recordId,
      name: typeof from.name === 'string' ? from.name : null,
    };
  }
  return { isSecure, accessPermission, inheritedFrom };
}

const STATE_RANK: Readonly<Record<AccessPermissionState, number>> = { standard: 0, limited: 1, restricted: 2 };

/**
 * The state the dialog gates and marks rows by (task 174): the STRICTER of the host's (the record's own stored values) and
 * the server's effective answer — never less strict than the host. Secure implies Limited for contacts; an effective
 * Access Permission or Secure flag the server could not establish folds in as Limited (the host's own fail-closed rule for
 * an unreadable flag). No server answer (`null`) keeps the host's values unchanged.
 */
export function effectiveAccessState(
  hostState: AccessPermissionState,
  hostIsSecure: boolean,
  server: IEffectiveRecordAccess | null
): { state: AccessPermissionState; isSecure: boolean } {
  if (!server) return { state: hostState, isSecure: hostIsSecure };
  const isSecure = hostIsSecure || server.isSecure === true;
  let state = hostState;
  const raise = (to: AccessPermissionState) => {
    if (STATE_RANK[to] > STATE_RANK[state]) state = to;
  };
  if (server.accessPermission) raise(server.accessPermission);
  if (server.accessPermission === null || server.isSecure !== false) raise('limited');
  return { state, isSecure };
}

/** Task 174: where the effective values come from, as a sentence for the banner; `null` when the record's own govern. */
export function describeInheritedFrom(server: IEffectiveRecordAccess | null): string | null {
  const from = server?.inheritedFrom;
  if (!from) return null;
  const label = tableLabel(from.recordType);
  return from.name
    ? `It follows the ${label} it is filed under: ${from.name}.`
    : `It follows the ${label} it is filed under.`;
}

/** A table logical name as a person reads it. */
function tableLabel(logicalName: string): string {
  switch (logicalName) {
    case 'sprk_project':
      return 'project';
    case 'sprk_matter':
      return 'matter';
    case 'sprk_workassignment':
      return 'work assignment';
    default:
      return 'record';
  }
}

/** Who the entry walls off, as a person reads it. */
export function describeSubjectKind(entry: IRecordNoAccessEntry): string {
  switch (entry.subjectKind) {
    case 'contact':
      return 'Contact';
    case 'organization':
      return 'Organization (all its people)';
    case 'systemuser':
      return 'User';
    default:
      return 'Incomplete entry';
  }
}

/** A record this one is DIRECTLY filed under, with its name when known (task 175: `followsParents`; task 174:
 * `inheritedFrom`). Only a direct parent is ever named — a record further up may not be visible to the reader (064's
 * contract, verifier F1-d). */
export interface IKnownDirectParent {
  recordId: string;
  name: string | null;
}

/**
 * Where the entry reaches this record from: this record itself, an organization it references, a secure parent (064's
 * contract: `coveredRecordType`/`coveredRecordId` is the record it covers this one THROUGH — this record, or any secure
 * record it is filed under, up the chain). The covering record is named only when it is one of `directParents` (task
 * 175): a direct parent is already on the record's own lookup; a record further up is labelled by its type alone.
 */
export function describeCoverage(
  entry: IRecordNoAccessEntry,
  directParents: readonly IKnownDirectParent[] = []
): string {
  let scope: string;
  const coveredId = cleanGuid(entry.coveredRecordId);
  const directName = directParents.find(p => p.name && cleanGuid(p.recordId) === coveredId)?.name;
  const parentName = directName ? `: ${directName}` : '';
  if (entry.objectKind === 'organization') {
    const org = entry.objectOrganizationName ?? 'an organization';
    scope = entry.viaSecureParent
      ? `Every record referencing ${org}, reaching this one through the secure ${tableLabel(entry.coveredRecordType)} it is filed under${parentName}`
      : `Every record referencing ${org}`;
  } else if (entry.objectKind === 'record') {
    scope = entry.viaSecureParent
      ? `Through the secure ${tableLabel(entry.coveredRecordType)} this record is filed under${parentName}`
      : 'This record';
  } else {
    scope = 'Scope not set';
  }
  if (entry.alsoViaSecureParent) scope += ', and again through a secure parent';
  return scope;
}

/**
 * Why a listed entry walls nobody off here, or `null` for an entry in force. Uses the server's reason only — the
 * client never decides whether an entry is in force.
 */
export function describeNotInForce(entry: IRecordNoAccessEntry): string | null {
  if (entry.inForce === true) return null;
  switch (entry.notInForceReason) {
    case 'malformed':
      return 'Incomplete entry: it walls nobody off. An access administrator should correct it in No Access Entries.';
    case 'userWallOnNonSecureRecord':
      return 'Not in force here: user walls apply only to secure records.';
    case 'secureStateUnknown':
      return 'Undetermined: whether this record is secure could not be read, so whether this user wall applies is unknown.';
    default:
      return entry.inForce === null ? 'Undetermined on this record.' : 'Not in force on this record.';
  }
}

/** The in-force walls, keyed by canonical subject id. Only `inForce: true` drives a veto marker (contract). */
export interface INoAccessVetoIndex {
  contacts: Set<string>;
  users: Set<string>;
  /** organization id → its display name. */
  organizations: Map<string, string>;
}

export function buildVetoIndex(entries: readonly IRecordNoAccessEntry[]): INoAccessVetoIndex {
  const index: INoAccessVetoIndex = { contacts: new Set(), users: new Set(), organizations: new Map() };
  for (const e of entries) {
    if (e.inForce !== true || !e.subjectId) continue;
    const id = cleanGuid(e.subjectId);
    if (e.subjectKind === 'contact') index.contacts.add(id);
    else if (e.subjectKind === 'systemuser') index.users.add(id);
    else if (e.subjectKind === 'organization') index.organizations.set(id, e.subjectName ?? 'An organization');
  }
  return index;
}

/**
 * The five kinds of Current Access row (the component classifies; these functions only read the kind). `'inherited'`
 * (task 175) is a user share a secure parent passed on to the record: read-only here, and otherwise marked exactly as a
 * user share (`'share'`) is.
 */
export type CurrentAccessRowKind = 'share' | 'inherited' | 'organization' | 'standing' | 'contact';

export function classifyCurrentAccessRow(grant: IAccessGrantRecord): CurrentAccessRowKind {
  // Checked before the standing branch: like a direct share, an inherited share carries no accessRecordId.
  if (grant.provenance === 'inherited') return 'inherited';
  if (grant.provenance === 'share') return 'share';
  if (grant.provenance === 'standing' || !grant.accessRecordId) return 'standing';
  if (grant.provenance === 'organization') return 'organization';
  return 'contact';
}

/**
 * Whether a Current Access row is walled off, and why, or `null`. Matches the entry's subject (contract, "Consumer
 * rules"): a contact row by its contact, a user share by its system user, an organization grant by its organization,
 * and a contact row ALSO by the contact's organizations (`contactWalledOrgs`: contact id → the walled organization's
 * name, resolved by the host for contacts that belong to a walled organization).
 */
export function vetoFor(
  grant: IAccessGrantRecord,
  kind: CurrentAccessRowKind,
  index: INoAccessVetoIndex,
  contactWalledOrgs: ReadonlyMap<string, string>
): string | null {
  const id = cleanGuid(grant.contactId);
  // Task 175: an inherited share is a user share for the No Access list (matched by its system user).
  if (kind === 'share' || kind === 'inherited') {
    // A Dataverse share still opens the record in the model-driven app until it is removed (143 removes it; owner N2
    // keeps team/role-held access), so the sentence does not claim the share gives no access anywhere.
    return index.users.has(id)
      ? 'On the No Access list: blocked in Teams and the Spaarke apps. If this share stays, revoke it so the model-driven app blocks them too.'
      : null;
  }
  if (kind === 'organization') {
    return index.organizations.has(id)
      ? 'This organization is on the No Access list: its people get no access through this grant.'
      : null;
  }
  if (index.contacts.has(id)) return 'On the No Access list: this grant gives no access.';
  const walledOrg = contactWalledOrgs.get(id);
  return walledOrg
    ? `${walledOrg} is on the No Access list and this contact belongs to it: this grant gives no access.`
    : null;
}

/**
 * Whether the record's own policy cancels a Current Access row, and why, or `null` (task 066's suppressed rows, owner
 * round 2 item 3 — the rule the server applies at read time):
 *  - Restricted: no contact-based access at all, so every contact, organization and standing row is cancelled.
 *    Internal user shares are unaffected (an external-flagged user's share has its own label, task 114).
 *  - Limited, and Secure (which the host folds into Limited, as it does an unreadable Secure flag): contacts get access
 *    only through grants made to them by name, so organization-wide and standing rows are cancelled.
 */
export function suppressionFor(
  kind: CurrentAccessRowKind,
  state: AccessPermissionState,
  isSecureRecord: boolean
): string | null {
  // Internal user shares, direct or inherited from a parent (task 175), are not cancelled by the record's policy.
  if (kind === 'share' || kind === 'inherited') return null;
  if (state === 'restricted') {
    return isSecureRecord
      ? 'No effect: this record is Secure – Restricted, so contacts get no access.'
      : 'No effect: this record is Restricted, so contacts get no access.';
  }
  if (state === 'limited' && (kind === 'organization' || kind === 'standing')) {
    const what = kind === 'organization' ? 'organization-wide grants' : 'standing grants';
    return isSecureRecord
      ? `No effect: this record is secure, so ${what} give no access.`
      : `No effect: this record is Limited, so ${what} give no access.`;
  }
  return null;
}

/** The contacts (canonical ids) among Current Access rows whose organizations must be checked against org walls. */
export function contactIdsToCheck(grants: readonly IAccessGrantRecord[]): string[] {
  const ids = new Set<string>();
  for (const g of grants) {
    const kind = classifyCurrentAccessRow(g);
    if ((kind === 'contact' || kind === 'standing') && g.contactId) ids.add(cleanGuid(g.contactId));
  }
  return Array.from(ids);
}
