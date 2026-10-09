/**
 * AccessGrantModal — injected-props contract (context-agnostic, ADR-012).
 *
 * Built for teams-app-r1 task 041 (design.md §5.1 / spec FR-11/FR-12). The modal
 * writes the ONE built table, `sprk_externalrecordaccess`, exclusively through the
 * BFF's already-built external-access endpoints (`/api/v1/external-access/grant`,
 * `/invite-and-grant`, `/revoke`) — it never writes to that table directly, and it
 * never re-implements the BFF's grant/invite core. The shared core has NO hard
 * dependency on `Xrm` or any Dataverse-entity-specific field names: the caller
 * (the `TrackingFieldTrio` PCF's `index.ts`, which alone knows the `sprk_project`
 * schema per FR-14's entity-agnostic discipline) supplies the candidate list,
 * existing-grants list, contact search, and internal/external contact
 * classification via callback props.
 *
 * `AuthenticatedFetchFn` is imported from `@spaarke/auth` — already an existing
 * dependency of `@spaarke/ui-components` (see `services/EmailComposer`,
 * `ConversationView`, etc.) — per ADR-028: the caller passes `authenticatedFetch`
 * as a function dependency, never a raw token.
 */

import type { AuthenticatedFetchFn } from '@spaarke/auth';
import type { ILookupItem } from '../../types/LookupTypes';

/** A single access-conferring membership candidate for the current record —
 * sourced from the contact-anchored role-allowlist (task 021's convention-based
 * discovery), read by the caller for the current bound record. NOT yet granted
 * (the caller SHOULD exclude any contact who already has an active grant — the
 * modal also defensively excludes any candidate whose `contactId` matches an
 * `existingGrants` row). */
export interface IAccessGrantCandidate {
  contactId: string;
  fullName: string;
  email?: string;
  /** Display role the candidate holds on the record (e.g., "Assigned Attorney 1"). */
  role: string;
}

/** An existing (active) `sprk_externalrecordaccess` row for the current record,
 * as read by the caller (Xrm.WebApi, host-context, single-entity — per
 * `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`). */
export interface IAccessGrantRecord {
  /** `sprk_externalrecordaccessid` — required by `/revoke`. ABSENT for a
   * standing-grant row (`provenance: 'standing'`), which confers ongoing
   * membership via the contact's `sprk_standinggrant` flag and has NO
   * per-record `sprk_externalrecordaccess` row to revoke here (task 073 UAT
   * #2). Rows without it render as non-revocable in Current Access. */
  accessRecordId?: string;
  contactId: string;
  fullName: string;
  email?: string;
  accessLevel: number;
  grantedByName?: string;
  /**
   * The CONTACT who issued this grant from the external SPA (`sprk_grantedbycontact`, unified-access-control-r2
   * task 140 — contact-side Grant Access). Set only on a contact-issued row; `grantedByName` (the systemuser
   * `sprk_grantedby`) is empty on such a row. Rendered as "Granted by {name} (external contact)"; the row stays
   * revocable here exactly like any other grant.
   */
  grantedByContactName?: string;
  /** ISO 8601 grant date (`sprk_granteddate`). */
  grantedDate?: string;
  /** Display-only provenance label — NOT sent to the BFF (the endpoint has no
   * provenance field; this is a client-side annotation of HOW the grant was
   * created, inferred by the caller or defaulted by this modal's own writes).
   * `'share'` (task 065, FR-29) is an internal system-user POA share, read
   * from `GET /api/v1/external-access/user-shares` — NOT a
   * `sprk_externalrecordaccess` row, so it carries no `accessRecordId`; see
   * {@link IAccessGrantModalProps.pickUser}. Task 066 (folded into 067 by
   * owner round 59) adds no provenance of its own: a row the record's policy
   * cancels, or a No Access entry walls off, is marked from this value plus
   * the record's state (`noAccess.ts`). */
  provenance?: 'membership-approved' | 'named' | 'standing' | 'organization' | 'unknown' | 'share';
  /**
   * Share rows only (unified-access-control-r2 task 114, owner round 67 amendment 4(c)): the record is Restricted and
   * this user is flagged external (`externalNoAccess` from `/user-shares`). Rendered as "External user — no access"
   * until the server removes the share (the record's next save, or its 5-minute job). Still revocable here.
   */
  externalNoAccess?: boolean;
}

/**
 * One No Access entry covering the record, as task 064's `GET /api/v1/records/{table}/{id}/no-access` returns it to a
 * caller with Write (unified-access-control-r2 task 067). The contract is frozen in
 * `projects/unified-access-control-r2/notes/phase4-access-report-contract.md`. Read-only here: walls are authored in
 * No Access Entries (task 154), never from this modal. The entry's Reason is never returned.
 */
export interface IRecordNoAccessEntry {
  entryId: string;
  name?: string | null;
  /** Who is walled off; `null` when the entry is malformed. */
  subjectKind: 'contact' | 'organization' | 'systemuser' | null;
  subjectId?: string | null;
  subjectName?: string | null;
  /** `record`: the entry names a record; `organization`: a wall over an organization the record references. */
  objectKind: 'record' | 'organization' | null;
  objectOrganizationId?: string | null;
  objectOrganizationName?: string | null;
  /** The record the entry covers this one THROUGH (this record, or a secure parent), as a table logical name + id. */
  coveredRecordType: string;
  coveredRecordId: string;
  viaSecureParent: boolean;
  alsoViaSecureParent: boolean;
  malformed: boolean;
  /** true: walls someone off this record; false: listed but inert here; null: undecided. Decided by the server only. */
  inForce: boolean | null;
  notInForceReason?: string | null;
  modifiedById?: string | null;
  modifiedByName?: string | null;
  modifiedOn?: string | null;
}

/** An ACTIVE membership of a contact in an organization (`sprk_contactorganization`), as the host reads it. */
export interface IContactOrganizationMembership {
  contactId: string;
  organizationId: string;
}

/** A single Dataverse Contact search result (named-contact person-picker). */
export interface IContactSearchResult {
  contactId: string;
  fullName: string;
  email?: string;
}

/** A grantee firm/organization picked via the side-pane Advanced Lookup
 * (task 071) — a `sprk_organization` record. `id` is the `sprk_organizationid`
 * GUID (already `cleanGuid`-normalized by the navigation adapter). Sent to the
 * BFF as `organizationId` (the grant's `sprk_Organization` firm-scoping lookup,
 * task 070). The shared modal never reads `sprk_organization` itself — the host
 * supplies this via the injected {@link IAccessGrantModalProps.pickOrganization}
 * callback, keeping the modal Xrm-free (ADR-012). */
export interface IOrganizationPick {
  id: string;
  name: string;
}

/** An internal system user picked via the side-pane Advanced Lookup (task 065,
 * FR-29) — a Dataverse `systemuser` record. `id` is the `systemuserid` GUID
 * (already `cleanGuid`-normalized by the navigation adapter). Sent to the BFF
 * as `systemUserId` on `POST /api/v1/external-access/share-user`. The shared
 * modal never reads `systemuser` itself — the host supplies this via the
 * injected {@link IAccessGrantModalProps.pickUser} callback, mirroring
 * {@link IOrganizationPick} / {@link pickOrganization} (ADR-012). */
export interface IUserPick {
  id: string;
  name: string;
  /** The user's primary email, when the host could read it. Shown beside the name because several users can share
   * one (owner test feedback 2026-10-07). */
  email?: string;
}

/** What the modal asks of the "+ User" lookup (task 114). */
export interface IUserPickOptions {
  /** Leave out users flagged external (`sprk_isexternal = true`): set on a Restricted record, where they cannot
   * receive a share (owner round 67). Blank counts as internal. */
  excludeExternal?: boolean;
}

/** The secure-project owner + business-unit alignment read-only display (task
 * 065, design.md §6). Per design §5.1a a secure record is owned by an OWNER
 * TEAM, not a service account — `ownerName` is whatever Dataverse's polymorphic
 * `ownerid` resolves to (team or user display name), and `businessUnitName` is
 * `owningbusinessunit`'s display name. The host resolves both via a
 * host-context read (`ownerid`/`owningbusinessunit`, entity-agnostic column
 * names on every one of the three grant-root entities) and returns `null` when
 * the record is not secure (`sprk_issecure` is not `true`) or the read failed —
 * the modal renders nothing in that case (fail-soft, never a hard error). */
export interface ISecureOwnerInfo {
  ownerName: string;
  businessUnitName: string;
}

/** The polymorphic root type a grant is held at (task 070/071). Mirrors the
 * BFF's `ExternalGrantRoot` types; the caller (the PCF host) derives it from the
 * bound record's entity and passes it as {@link IAccessGrantModalProps.recordType}
 * so the modal sends `{recordType, recordId}` grant bodies without knowing any
 * Dataverse entity name itself (ADR-012). */
export type ExternalGrantRootType = 'project' | 'matter' | 'workassignment';

/**
 * The record-level Access-Permission sharing-gate state (spec FR-14, Option
 * A — teams-app-r1 task 043). This is a project-wide SEMANTIC vocabulary
 * ("Standard"/"Limited"/"Restricted"), NOT a raw Dataverse OptionSet value —
 * the caller maps its own entity's Access-Permission OptionSet integer to
 * one of these three states (see `TrackingFieldTrio`'s PCF `index.ts`, the
 * ONLY place that knows the real `sprk_project` OptionSet values), keeping
 * this shared modal entity-agnostic per ADR-012.
 *
 * The owner's model (unified-access-control-r2 task 138, round 2 item 3,
 * binding) — the server enforces exactly this at read AND write time:
 * - `'restricted'` — no contact-based access at all; internal users are
 *   unaffected. The modal does not offer "+ Contact", "+ Organization" or the
 *   role-based candidates; "+ User" (an internal share), its level dropdown,
 *   Add and Revoke stay available.
 * - `'limited'` — contacts get access ONLY through named, direct grants:
 *   organization-wide grants, standing-grant membership and organization
 *   expansion confer nothing. The modal does not offer "+ Organization".
 *   A SECURE record (not Restricted) is passed as `'limited'` too — Secure
 *   implies Limited for contacts — with {@link IAccessGrantModalProps.isSecureRecord}
 *   set so the banner can say "Secure".
 * - `'standard'` — every grant type is available. This is the DEFAULT applied
 *   when the prop is omitted (task 041's baseline).
 *
 * The host maps raw values to this state and MUST fail closed: an unreadable
 * secure flag maps to `'limited'`, never `'standard'`.
 *
 * DISTINCT from the per-grant `sprk_accesslevel` field (`accessLevelOptions`
 * / `defaultAccessLevel` below) — this state governs WHICH grantee kinds the
 * modal offers, never WHAT access level an individual grant carries.
 */
export type AccessPermissionState = 'standard' | 'limited' | 'restricted';

/** A single access-level choice offered by the modal. Defaults mirror the
 * BFF's fixed `ExternalAccessLevel` enum (ViewOnly=100000000,
 * Collaborate=100000001, FullAccess=100000002) — this is a BFF API contract
 * value, not a per-installation Dataverse OptionSet, so a sensible default is
 * provided; callers MAY override via `accessLevelOptions`. */
export interface IAccessLevelOption {
  value: number;
  label: string;
}

export interface IAccessGrantModalProps {
  /** Whether the modal is open. */
  open: boolean;
  /** Close callback — wired to the × and the footer Close button. */
  onClose: () => void;
  /** The current record's id — the GUID of the polymorphic root identified by
   * {@link recordType} (a `sprk_project`, `sprk_matter`, or `sprk_workassignment`
   * id). Sent to the BFF as `recordId`. */
  recordId: string;
  /** The polymorphic root type this grant is held at (task 070/071). The modal
   * sends `{recordType, recordId}` in every grant/invite-and-grant body so the
   * BFF binds the correct typed root lookup. Defaults to `'project'` — the
   * pre-071 baseline — so a caller that has not yet wired the host entity keeps
   * writing project grants unchanged (the BFF also accepts the legacy `projectId`
   * shorthand, but this modal always sends the explicit `recordType`). */
  recordType?: ExternalGrantRootType;
  /** Gates the modal's functional UI. When not `true`, the modal renders an
   * explanatory not-authorized state instead of the candidate list / picker /
   * grants list.
   *
   * 🔴 WHAT THIS IS NOT (corrected in task 118, unified-access-control-r2).
   * This doc used to call the check "a SECOND, defense-in-depth check inside
   * the modal itself". It is not, and describing it that way is what let a
   * fail-open default survive review: this check and `TrackingFieldTrio`'s
   * person-icon check read THE SAME VALUE, from the same host, so they cannot
   * disagree and neither can catch the other being wrong. Two gates reading
   * one value are one gate. What it DOES buy is narrower and worth stating
   * honestly: a direct component mount that bypasses the icon's disabled state
   * still renders the not-authorized surface rather than the grant UI.
   *
   * THE REAL BACKSTOP IS THE SERVER. `DelegationRuleFilter`, group-level on
   * `/api/v1/external-access`, refuses every grant, revoke, share and expiry
   * change from a caller without Write on the target record — evaluated as the
   * caller over OBO, and denying what it cannot evaluate. No client-side value
   * can bypass it.
   *
   * Default `false` — INVERTED in task 118. It was `true`, so a host that had
   * not wired an access decision got the full grant UI. An unanswered access
   * question is now a denial, matching both `TrackingFieldTrio`'s default and
   * the server's fail direction. */
  canGrantAccess?: boolean;
  /** Host-supplied `authenticatedFetch` (ADR-028 function-dependency contract
   * — never a raw token). Used for the three built BFF calls: `/grant`,
   * `/invite-and-grant`, `/revoke`. */
  authenticatedFetch: AuthenticatedFetchFn;
  /** Loads the record's membership candidates (task 021 role-allowlist
   * source). Called once when the modal opens. */
  fetchCandidates: () => Promise<IAccessGrantCandidate[]>;
  /** Loads the record's existing active grants. Called when the modal opens
   * and re-called after every successful grant/revoke to refresh the list. */
  fetchExistingGrants: () => Promise<IAccessGrantRecord[]>;
  /** Loads the record's STANDING-grant members — contacts whose global
   * `sprk_standinggrant` flag is set AND who hold an access-conferring role on
   * THIS record (the host intersects the standing flag with the record's
   * role-members so the list mirrors the server-side union in
   * `AccessibleRecordSetService` — a standing contact with no role on this
   * record confers no access to it, task 073 UAT #2). Each returned row MUST
   * carry `provenance: 'standing'` and NO `accessRecordId` (there is no
   * per-record grant to revoke here). Merged into "Current Access", deduped by
   * `contactId` against {@link fetchExistingGrants} (an explicit per-record
   * grant wins). Omit → no standing rows (a host that hasn't wired the
   * `sprk_standinggrant` field yet). NOTE: `sprk_standinggrant` is
   * field-level-secured — a caller without FLS read silently gets none. */
  fetchStandingContacts?: () => Promise<IAccessGrantRecord[]>;
  /** Searches Dataverse Contacts by free-text query (named-contact picker).
   * Debounced by the modal; the host implements the actual Contact query
   * (Xrm.WebApi, host-context). Used ONLY as the fallback inline picker when
   * {@link pickContact} is not supplied (e.g. an SPA host with no side-pane
   * lookup); when `pickContact` is provided the modal uses the side-pane
   * Advanced Lookup instead and this is not called. */
  searchContacts: (query: string) => Promise<IContactSearchResult[]>;
  /** Searches `sprk_organization` firms/orgs by free-text query for the in-app
   * "Add organization" inline picker (task 073 UAT #4). Host implements the
   * actual query (Xrm.WebApi, host-context) and returns `{ id, name }[]`.
   * When supplied, section 2 shows an inline org `LookupField` that stacks
   * inside the modal (no side-pane, no modal-hide). Omit → no org picker. */
  searchOrganizations?: (query: string) => Promise<ILookupItem[]>;
  /** Opens the host's NATIVE Dataverse advanced-lookup side pane for a single
   * Contact (task 073 UAT v1.0.24 — `Xrm.Utility.lookupObjects`), returning the
   * pick (or `null` on cancel). This is the PRIMARY "+ Contact" mechanism — same
   * advanced-find surface the wizards use (search/views/filters/recent). Works
   * because the modal renders `nonBlocking` (no backdrop covering the lookup
   * pane). When supplied, the "+ Contact" button opens this; {@link searchContacts}
   * is only the fallback for hosts (e.g. an SPA) that can't open the native pane.
   * (Earlier v1.0.23 deprecated this in favor of an in-modal box; v1.0.24
   * restored it per owner UAT — the native lookup is preferred.) */
  pickContact?: () => Promise<IContactSearchResult | null>;
  /** Opens the host's NATIVE advanced-lookup side pane for a single
   * `sprk_organization` (task 073 UAT v1.0.24) — the PRIMARY "+ Organization"
   * mechanism, mirroring {@link pickContact}. Omit → the "+ Organization" button
   * is hidden. */
  pickOrganization?: () => Promise<IOrganizationPick | null>;
  /** Opens the host's NATIVE advanced-lookup side pane for a single
   * `systemuser` (task 065, FR-29) — the PRIMARY "+ User" mechanism, mirroring
   * {@link pickContact} / {@link pickOrganization}. The picked user is staged
   * into the SAME "Add Access Permissions" list (its own per-row access-level
   * dropdown) and committed by `Add (N)` via `POST
   * /api/v1/external-access/share-user` — an internal Dataverse POA share, NOT
   * a `sprk_externalrecordaccess` row (distinct write path from
   * pickContact/pickOrganization's `/grant` | `/invite-and-grant`). Omit → the
   * "+ User" button is hidden. */
  pickUser?: (options?: IUserPickOptions) => Promise<IUserPick | null>;
  /** Opens the Contact record (task 073 UAT v1.0.24 #6) — wired by the host to
   * `Xrm.Navigation.navigateTo` (entityrecord, modal target) so a user with write
   * access to the Contact can view/edit it. When supplied, each contact name in
   * the Available + Current Access lists renders as a link. Omit → names render
   * as plain text. */
  onOpenContact?: (contactId: string) => void;
  /** Classifies a contact as internal-workforce (has a linked `systemuser`)
   * vs external. Drives the notify branch: an external contact with a known
   * email is granted via the built `/invite-and-grant` (onboard + grant + CIAM
   * email, atomic); an internal contact is granted via the built `/grant`
   * only (no CIAM onboarding is appropriate for an internal workforce
   * person) — see the modal's own doc comment for the escalated internal
   * deep-link notify gap. */
  isInternalContact: (contactId: string) => Promise<boolean>;
  // `onSetStandingGrant` was REMOVED by task 138: the modal has had no
  // standing-grant control since task 073 UAT v1.0.24 #5 (the standing grant is
  // set on the Contact record itself), so the prop was dead wiring.
  /** Header title override (default `"Manage Access"`). */
  title?: string;
  /** Access-level choices offered for every grant (default: the BFF's fixed
   * ViewOnly/Collaborate/FullAccess triple). */
  accessLevelOptions?: IAccessLevelOption[];
  /** The access level applied to every grant written by this modal (default:
   * the first `accessLevelOptions` entry — ViewOnly). R1 does not expose a
   * per-grant access-level picker in the UI (out of this task's scope per
   * design.md §5.1 — the modal's job is WHO gets access, not WHAT level);
   * callers needing a different default may override. */
  defaultAccessLevel?: number;
  /** The record's current Access-Permission sharing-gate state (spec FR-14,
   * Option A — task 043; made real by task 138). Governs which grantee kinds
   * the modal offers: `'restricted'` hides "+ Contact", "+ Organization" and
   * the candidates (keeping "+ User"); `'limited'` hides "+ Organization";
   * `'standard'` (default, when omitted) offers everything. See
   * {@link AccessPermissionState} for the full mapping. */
  accessPermissionState?: AccessPermissionState;
  /** Whether the record is SECURE (task 138; owner O1 FINAL, 2026-10-01) — a
   * semantic flag, not a Dataverse column name. Only changes the banner's copy
   * ("Secure", or "Secure – Restricted" when {@link accessPermissionState} is
   * `'restricted'`); the gating itself comes from `accessPermissionState`, to
   * which the host already folds Secure as `'limited'`. Default `false`. */
  isSecureRecord?: boolean;
  /** Resolves the current record's secure-project owner + business-unit
   * alignment (task 065, design.md §6) for read-only display. Called once
   * when the modal opens, alongside the other loaders. Returns `null` for a
   * non-secure record, or when the host cannot resolve it — the modal simply
   * renders no owner/BU row in that case. Omit → the row never renders (a
   * host that hasn't wired the read yet; zero-regression default). */
  fetchSecureOwnerInfo?: () => Promise<ISecureOwnerInfo | null>;
  /**
   * Resolves which of the given contacts hold an ACTIVE membership in which of the given organizations
   * (`sprk_contactorganization`, the same state-only bound the server's wall uses: `statecode` active or blank, no
   * dates). Task 067: an organization on the record's No Access list walls off its people, so a Current Access row for
   * such a contact is marked walled off. Called only when an organization wall is IN FORCE on the record and Current
   * Access lists contacts. Omit, or reject, and the modal says those rows could not be checked (never "not walled").
   */
  fetchContactOrganizationMemberships?: (
    contactIds: string[],
    organizationIds: string[]
  ) => Promise<IContactOrganizationMembership[]>;
  /**
   * The section to bring into view when the modal opens (task 153): `'noAccess'` scrolls to and focuses the No Access
   * List once this open's load has finished (the TrackingFieldTrio access-status indicator asks for it when a No
   * Access restriction applies). Omit for the top (Current Access). Ignored when the section is not shown (the caller
   * lacks Write, `notShown`).
   */
  initialSection?: 'noAccess';
}

/** BFF's fixed `ExternalAccessLevel` enum values (Infrastructure/ExternalAccess/
 * ExternalCallerContext.cs) — a stable API contract, not Dataverse schema. */
export const DEFAULT_ACCESS_LEVEL_OPTIONS: IAccessLevelOption[] = [
  { value: 100000000, label: 'View Only' },
  { value: 100000001, label: 'Collaborate' },
  { value: 100000002, label: 'Full Access' },
];
