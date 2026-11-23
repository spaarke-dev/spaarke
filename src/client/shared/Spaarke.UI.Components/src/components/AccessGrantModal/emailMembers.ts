/**
 * "Email members" recipients (unified-access-control-r2 task 196, owner decision 2026-11-12).
 *
 * The TrackingFieldTrio host's email icon pre-fills the canonical `SendEmailDialog` with the people on the record. Until
 * task 196 that list was the record's `sprk_assigned*` role contacts alone, so a record whose people had access through a
 * grant or a share read as "no members". The owner's rule: ONE list, de-duplicated by email (case-insensitive), of
 *  a. the role contacts (the host's `fetchCandidates` read) with an email;
 *  b. the contacts holding an ACTIVE grant on the record (the host's `fetchExistingGrants`, the same
 *     `GET /record-grants` read Current Access uses) — expired grants are left out;
 *  c. the internal users the record is shared with (`GET /user-shares`, through `userShares.ts`, the reader Current
 *     Access uses), by their primary email.
 * Anyone walled off by the record's No Access list is left out, by the SAME rules that mark Current Access rows
 * (`noAccess.ts`: `buildVetoIndex` / `vetoFor`, organization walls through the host's membership read), and a grant or
 * share the record's policy cancels (`suppressionFor`, an external user's share on a Restricted record) is left out as
 * Current Access marks it "No effect". So "who has access" and "who gets emailed" agree.
 *
 * Standing-grant members are not read separately: the host's standing rows are its role contacts whose global flag is
 * set (`fetchStandingContacts` filters `fetchCandidates`), so they are already in (a).
 *
 * FAIL VISIBLE, NEVER SILENTLY NARROWER. The grants and shares reads are gated server-side on Write + Share, and any read
 * can fail. A failed source never breaks the button: the result carries whatever the other sources gave, plus a notice
 * naming what could not be read, which the host shows in the dialog. `empty` (the "no members" state) is returned only
 * when every source and check succeeded and produced nobody; `failed` only when every source failed. The notice tells
 * apart "someone may be missing" (a source, an email, a capped list) from "someone listed may be walled off" (the No
 * Access check could not be completed, so people were kept). An email row the caller cannot read is reported, never
 * taken for "no email" ({@link EmailReader}).
 *
 * Pure orchestration: every Dataverse and BFF read is injected by the host (ADR-012: the shared code knows no table or
 * column names), so the rules are pinned by tests without rendering.
 */

import type { AuthenticatedFetchFn } from '@spaarke/auth';
import { cleanGuid } from '../../utils/guid';
import {
  buildNoAccessPath,
  buildVetoIndex,
  classifyCurrentAccessRow,
  effectiveAccessState,
  parseEffectiveAccess,
  parseNoAccessResponse,
  suppressionFor,
  vetoFor,
} from './noAccess';
import type { CurrentAccessRowKind, INoAccessVetoIndex } from './noAccess';
import { buildUserSharesPath, mapUserShares } from './userShares';
import type {
  AccessPermissionState,
  ExternalGrantRootType,
  IAccessGrantCandidate,
  IAccessGrantRecord,
  IContactOrganizationMembership,
  IExistingGrantsRead,
} from './types';

/** The reads that exist only on a grant root (project, matter, work assignment). */
export interface IEmailMembersRootSources {
  recordType: ExternalGrantRootType;
  recordId: string;
  /** The host's `authenticatedFetch` (ADR-028), for the `/user-shares` and No Access reads. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** The record's grants — the host's `fetchExistingGrants`. Rejects on failure. */
  readGrants: () => Promise<IAccessGrantRecord[] | IExistingGrantsRead>;
  /** System user id → primary email, for the given users ({@link EmailReader}). */
  readUserEmails: EmailReader;
  /** Whether the caller may manage this record's access (the host's gate). The grants and shares reads are gated
   * server-side on the same rights, so when they fail for a caller who may not, the notice says the caller cannot see
   * who else has access rather than that a read failed. Default `true`. Wording only: the reads are always tried. */
  callerManagesAccess?: boolean;
  /** Which of the contacts belong to which of the walled organizations — the host's Current Access read. */
  readContactOrganizationMemberships?: (
    contactIds: string[],
    organizationIds: string[]
  ) => Promise<IContactOrganizationMembership[]>;
  /** The record's own Access Permission state and Secure flag, as the host passes them to the modal. */
  accessPermissionState: AccessPermissionState;
  isSecureRecord: boolean;
}

/**
 * Row id → primary email, for the given rows. The map holds EVERY row the read returned, with `''` for a row that has
 * no email; an id missing from the map is a row the caller could not read (a host-context read leaves out rows the
 * caller's Dataverse privileges do not reach), which is reported, never taken for "no email". Rejects on failure.
 */
export type EmailReader = (ids: string[]) => Promise<ReadonlyMap<string, string>>;

export interface IEmailMembersSources {
  /** The record's role contacts (with emails) — the host's `fetchCandidates` read. */
  readRoleContacts: () => Promise<IAccessGrantCandidate[]>;
  /** Contact id → primary email ({@link EmailReader}), for granted contacts and for role contacts that came without an
   * email (to tell "no email" from "row not readable"). Without it a role contact's email is taken as given, and a
   * granted contact's email cannot be read (reported). */
  readContactEmails?: EmailReader;
  /** Present only when the record is a grant root; otherwise only the role contacts apply (and no note is shown). */
  root?: IEmailMembersRootSources;
}

export type EmailMembersResolution =
  /** Open the dialog with these recipients; `notice` (when set) must be shown in it. */
  | { kind: 'recipients'; recipients: string[]; notice: string | null }
  /** Every source and check succeeded and produced nobody with an email. */
  | { kind: 'empty' }
  /** Every source failed: nothing is known about who is on the record. */
  | { kind: 'failed' };

/** The empty state's sentence (task 196: "members", not "membership contacts" — grants and shares count too). */
export const EMAIL_MEMBERS_EMPTY_MESSAGE =
  'This record has no members with an email address yet. Grant access or assign a role first.';
/** The all-sources-failed sentence. */
export const EMAIL_MEMBERS_FAILED_MESSAGE =
  'The people on this record could not be loaded, so no email was started. Close this and try again.';

type Settled<T> = { ok: true; value: T } | { ok: false };

async function settle<T>(read: () => Promise<T>): Promise<Settled<T>> {
  try {
    return { ok: true, value: await read() };
  } catch {
    return { ok: false };
  }
}

/** A GET through the host fetch that resolves only with a 2xx body; anything else rejects (thrown or returned). */
async function getOkJson(fetchFn: AuthenticatedFetchFn, path: string): Promise<unknown> {
  // A host fetch may RETURN a non-2xx instead of throwing (the modal's `requestOk` handles both shapes the same way).
  const res: Response = await fetchFn(path, { method: 'GET' });
  if (!res.ok) throw new Error(`${path} answered ${res.status}`);
  return (await res.json()) as unknown;
}

/** One person who may be emailed, with the Current Access row kind the No Access rules read. */
interface IMember {
  /** `role` and `grant` ids are contact ids; `share` ids are system user ids. */
  source: 'role' | 'grant' | 'share';
  row: IAccessGrantRecord;
  kind: CurrentAccessRowKind;
}

/** Resolves the "Email members" recipients. Never rejects. */
export async function resolveEmailMembersRecipients(sources: IEmailMembersSources): Promise<EmailMembersResolution> {
  const root = sources.root;
  // Two kinds of note, worded apart: someone may be MISSING from the list, or someone listed may be WALLED OFF (the No
  // Access check could not be completed, so people were kept rather than silently dropped).
  const notes: string[] = [];
  const unchecked: string[] = [];

  const [roles, grants, shares, noAccess] = await Promise.all([
    settle(sources.readRoleContacts),
    root ? settle(root.readGrants) : Promise.resolve(null),
    root
      ? settle(async () =>
          mapUserShares(
            (await getOkJson(
              root.authenticatedFetch,
              buildUserSharesPath(root.recordType, root.recordId)
            )) as Parameters<typeof mapUserShares>[0]
          )
        )
      : Promise.resolve(null),
    root
      ? settle(() => getOkJson(root.authenticatedFetch, buildNoAccessPath(root.recordType, root.recordId)))
      : Promise.resolve(null),
  ]);

  const sourcesOk = root ? [roles.ok, grants?.ok === true, shares?.ok === true] : [roles.ok];
  if (sourcesOk.every(ok => !ok)) return { kind: 'failed' };

  if (!roles.ok) notes.push("the record's assigned people could not be read");
  if (root?.callerManagesAccess === false && grants?.ok !== true && shares?.ok !== true) {
    // The grants and shares reads are for people who manage the record's access; for anyone else they are refused.
    notes.push('you cannot see who else has access to this record (only people who manage its access can)');
  } else {
    if (grants && !grants.ok) notes.push('the people granted access could not be read');
    if (shares && !shares.ok) notes.push('the users the record is shared with could not be read');
  }

  // The record's grants (a capped read says so — NFR-03).
  let grantList: IAccessGrantRecord[] = [];
  if (grants?.ok) {
    const read = grants.value;
    grantList = Array.isArray(read) ? read : read.grants;
    if (!Array.isArray(read) && read.truncated) {
      notes.push(`only the first ${read.grants.length} grants could be read`);
    }
  }

  // The No Access list and the record's effective policy, read and applied as Current Access does.
  let vetoIndex: INoAccessVetoIndex = buildVetoIndex([]);
  let policy: { state: AccessPermissionState; isSecure: boolean } | null = null;
  if (root) {
    const section = noAccess?.ok ? parseNoAccessResponse(noAccess.value, root.recordId) : ({ kind: 'error' } as const);
    if (section.kind === 'list') {
      vetoIndex = buildVetoIndex(section.entries);
      if (section.truncated) unchecked.push('only part of the No Access list could be checked');
    } else {
      unchecked.push('the No Access list could not be checked');
    }
    policy = effectiveAccessState(
      root.accessPermissionState,
      root.isSecureRecord,
      noAccess?.ok ? parseEffectiveAccess(noAccess.value, root.recordId) : null
    );
  }

  // Candidates, in the order their emails are listed: role contacts, then granted contacts, then shared users.
  const members: IMember[] = [];
  if (roles.ok) {
    for (const c of roles.value) {
      members.push({
        source: 'role',
        row: { contactId: c.contactId, fullName: c.fullName, email: c.email, accessLevel: 0 },
        kind: 'contact',
      });
    }
  }
  let organizationGrantLeftOut = false;
  for (const g of grantList) {
    if (g.isExpired === true) continue;
    const kind = classifyCurrentAccessRow(g);
    if (kind === 'organization') {
      // An organization-wide grant names no person: its people are not expanded here, and the notice says so.
      if (!vetoFor(g, kind, vetoIndex, new Map()) && !(policy && suppressionFor(kind, policy.state, policy.isSecure))) {
        organizationGrantLeftOut = true;
      }
      continue;
    }
    if (g.contactId) members.push({ source: 'grant', row: g, kind });
  }
  if (shares?.ok) {
    for (const s of shares.value) {
      // Task 114: an external user's share on a Restricted record gives no access ("External user — no access").
      if (s.externalNoAccess === true || !s.contactId) continue;
      members.push({ source: 'share', row: s, kind: classifyCurrentAccessRow(s) });
    }
  }
  if (organizationGrantLeftOut) notes.push('people with access only through an organization-wide grant are not added');

  // Organization walls: contacts that belong to a walled organization (Current Access's own check).
  const contactWalledOrgs = new Map<string, string>();
  if (root && vetoIndex.organizations.size > 0) {
    const contactIds = Array.from(
      new Set(members.filter(m => m.source !== 'share').map(m => cleanGuid(m.row.contactId)))
    );
    if (contactIds.length > 0) {
      const memberships = root.readContactOrganizationMemberships
        ? await settle(() =>
            root.readContactOrganizationMemberships!(contactIds, Array.from(vetoIndex.organizations.keys()))
          )
        : ({ ok: false } as const);
      if (memberships.ok) {
        for (const m of memberships.value) {
          const orgName = vetoIndex.organizations.get(cleanGuid(m.organizationId));
          if (orgName) contactWalledOrgs.set(cleanGuid(m.contactId), orgName);
        }
      } else {
        unchecked.push('whether anyone belongs to a walled-off organization could not be checked');
      }
    }
  }

  // Left out: walled off by the No Access list, or cancelled by the record's policy (never role contacts — the owner's
  // list names them whatever their grants say; they are still left out when walled off).
  const walled = members.filter(m => vetoFor(m.row, m.kind, vetoIndex, contactWalledOrgs));
  const kept = members.filter(m => {
    if (walled.includes(m)) return false;
    if (m.source === 'role') return true;
    return !(policy && suppressionFor(m.kind, policy.state, policy.isSecure));
  });

  // Emails the sources did not carry: granted contacts (the grants read names no email), shared users, and role
  // contacts that came without one (when the host gives a contact reader, so "no email" and "not readable" differ).
  // Walled-off people are read too: their addresses are kept off the list whichever way they would arrive (a walled
  // contact's paired user share, say), since the No Access rules match people by id and the list is by email.
  const lookedUp = [...kept, ...walled];
  const needsLookup = (m: IMember): boolean =>
    !hasEmail(m.row.email) && (m.source !== 'role' || sources.readContactEmails !== undefined);
  const contactEmails = await readEmails(
    lookedUp.filter(m => m.source !== 'share' && needsLookup(m)).map(m => m.row.contactId),
    sources.readContactEmails
  );
  const userEmails = await readEmails(
    lookedUp.filter(m => m.source === 'share' && needsLookup(m)).map(m => m.row.contactId),
    root?.readUserEmails
  );
  /** The member's email; `null` when it should have been read and was not (the read failed or left the row out). */
  const emailOf = (m: IMember): string | undefined | null => {
    if (hasEmail(m.row.email)) return m.row.email.trim();
    if (!needsLookup(m)) return undefined;
    const map = (m.source === 'share' ? userEmails : contactEmails).map;
    const id = cleanGuid(m.row.contactId);
    if (!map.has(id)) return null;
    return map.get(id)?.trim() || undefined;
  };

  const walledEmails = new Set<string>();
  let walledUnread = false;
  for (const m of walled) {
    const email = emailOf(m);
    if (email === null) walledUnread = true;
    else if (email) walledEmails.add(email.toLowerCase());
  }
  if (walledUnread) unchecked.push('some email addresses of people on the No Access list could not be read');

  const seen = new Set<string>();
  const recipients: string[] = [];
  let unread = false;
  for (const m of kept) {
    const email = emailOf(m);
    if (email === null) unread = true;
    if (!email) continue;
    const key = email.toLowerCase();
    if (seen.has(key) || walledEmails.has(key)) continue;
    seen.add(key);
    recipients.push(email);
  }
  if (unread) notes.push('some email addresses could not be read');

  if (recipients.length === 0 && notes.length === 0 && unchecked.length === 0) return { kind: 'empty' };
  const parts: string[] = [];
  if (notes.length > 0) parts.push(`Some people may be missing from the recipients: ${notes.join('; ')}.`);
  if (unchecked.length > 0) {
    parts.push(`Someone listed may be walled off from this record: ${unchecked.join('; ')}.`);
  }
  return {
    kind: 'recipients',
    recipients,
    notice: parts.length === 0 ? null : `${parts.join(' ')} Check the list before sending.`,
  };
}

function hasEmail(email: string | undefined): email is string {
  return typeof email === 'string' && email.trim().length > 0;
}

/** Reads emails for the ids (canonical keys; see {@link EmailReader}). No ids needs no read. A missing reader or a failed
 * read gives an empty map, so every id reads as "not read". */
async function readEmails(ids: string[], reader: EmailReader | undefined): Promise<{ map: Map<string, string> }> {
  const unique = Array.from(new Set(ids.map(cleanGuid)));
  const map = new Map<string, string>();
  if (unique.length === 0 || !reader) return { map };
  const read = await settle(() => reader(unique));
  if (read.ok) for (const [id, email] of read.value) map.set(cleanGuid(id), email);
  return { map };
}
