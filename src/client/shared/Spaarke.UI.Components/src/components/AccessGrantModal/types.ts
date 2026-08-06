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
  /** `sprk_externalrecordaccessid` — required by `/revoke`. */
  accessRecordId: string;
  contactId: string;
  fullName: string;
  email?: string;
  accessLevel: number;
  grantedByName?: string;
  /** ISO 8601 grant date (`sprk_granteddate`). */
  grantedDate?: string;
  /** Display-only provenance label — NOT sent to the BFF (the endpoint has no
   * provenance field; this is a client-side annotation of HOW the grant was
   * created, inferred by the caller or defaulted by this modal's own writes). */
  provenance?: 'membership-approved' | 'named' | 'standing' | 'unknown';
}

/** A single Dataverse Contact search result (named-contact person-picker). */
export interface IContactSearchResult {
  contactId: string;
  fullName: string;
  email?: string;
}

/**
 * The record-level Access-Permission sharing-gate state (spec FR-14, Option
 * A — teams-app-r1 task 043). This is a project-wide SEMANTIC vocabulary
 * ("Standard"/"Limited"/"Restricted"), NOT a raw Dataverse OptionSet value —
 * the caller maps its own entity's Access-Permission OptionSet integer to
 * one of these three states (see `TrackingFieldTrio`'s PCF `index.ts`, the
 * ONLY place that knows the real `sprk_project` OptionSet values), keeping
 * this shared modal entity-agnostic per ADR-012.
 *
 * - `'restricted'` — external access is off for this record: the modal
 *   blocks ALL external-grant actions (approve-candidate + add-named-contact).
 * - `'limited'` — named/approved grants remain available, but the
 *   standing-grant option is unavailable (no auto-approval across future
 *   records).
 * - `'standard'` — every grant type is available, including standing
 *   grants. This is the DEFAULT applied when the prop is omitted, matching
 *   task 041's baseline behavior for any caller that hasn't wired the
 *   record's Access-Permission value (zero-regression default).
 *
 * DISTINCT from the per-grant `sprk_accesslevel` field (`accessLevelOptions`
 * / `defaultAccessLevel` below) — this state governs WHICH grant types the
 * modal permits, never WHAT access level an individual grant carries. R1
 * exposes no per-grant access-level selector in this modal's UI (see
 * `defaultAccessLevel`'s doc comment), so there is no shared UI surface for
 * this gate to affect; the independence is structural — the gate only
 * touches candidate/named-contact/standing-grant availability, never
 * `accessLevelOptions` or `defaultAccessLevel`.
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
  /** The current record's id (`sprk_projectid` — R1 scope per design.md §5). */
  recordId: string;
  /** Gates the modal's functional UI. Mirrors `TrackingFieldTrio`'s
   * `canGrantAccess` gate on the person icon (task 040) — this is a SECOND,
   * defense-in-depth check inside the modal itself, so a direct component
   * mount (bypassing the icon's disabled state) still cannot grant/revoke.
   * When `false`, the modal renders an explanatory not-authorized state
   * instead of the candidate list / picker / grants list. Default `true`
   * (fail-open only when the caller hasn't wired an access decision at all —
   * matches `TrackingFieldTrio`'s own default). */
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
  /** Searches Dataverse Contacts by free-text query (named-contact picker).
   * Debounced by the modal; the host implements the actual Contact query
   * (Xrm.WebApi, host-context). */
  searchContacts: (query: string) => Promise<IContactSearchResult[]>;
  /** Classifies a contact as internal-workforce (has a linked `systemuser`)
   * vs external. Drives the notify branch: an external contact with a known
   * email is granted via the built `/invite-and-grant` (onboard + grant + CIAM
   * email, atomic); an internal contact is granted via the built `/grant`
   * only (no CIAM onboarding is appropriate for an internal workforce
   * person) — see the modal's own doc comment for the escalated internal
   * deep-link notify gap. */
  isInternalContact: (contactId: string) => Promise<boolean>;
  /** Sets/clears the contact's subject-level standing-grant flag
   * (`contact.sprk_standinggrant`, FR-12) — a single-field Contact write, NOT
   * a `sprk_externalrecordaccess` write, so it is intentionally OUTSIDE the
   * "reuse the grant endpoint" constraint. The host implements this via
   * Xrm.WebApi (host-context, single-entity, single-field — per
   * DATA-ACCESS-DECISION-CRITERIA.md). Best-effort: a failure here does NOT
   * roll back the grant that was already written (NFR-06 principle applied
   * to the standing-grant option specifically). Omit to hide the standing-
   * grant option entirely (e.g., a host that hasn't wired task 050's field
   * yet). */
  onSetStandingGrant?: (contactId: string, standingGrant: boolean) => Promise<void>;
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
   * Option A — task 043). Governs which grant types the modal permits:
   * `'restricted'` blocks all external grants (approve-candidate +
   * add-named-contact disabled, with an explanatory banner); `'limited'`
   * allows named/approved grants but hides the standing-grant option;
   * `'standard'` (default, when omitted) allows every grant type — task
   * 041's unmodified baseline. See {@link AccessPermissionState} for the
   * full mapping and the `sprk_accesslevel` independence guarantee. */
  accessPermissionState?: AccessPermissionState;
}

/** BFF's fixed `ExternalAccessLevel` enum values (Infrastructure/ExternalAccess/
 * ExternalCallerContext.cs) — a stable API contract, not Dataverse schema. */
export const DEFAULT_ACCESS_LEVEL_OPTIONS: IAccessLevelOption[] = [
  { value: 100000000, label: 'View Only' },
  { value: 100000001, label: 'Collaborate' },
  { value: 100000002, label: 'Full Access' },
];
