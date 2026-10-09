/**
 * TrackingFieldTrio PCF Control
 *
 * Compact three-field editor: Monitor (toggle) + High Priority (toggle) +
 * Access Permission (segmented 3-value picker). Designed to fit inside a 33%
 * form column where the standard Dataverse field controls waste too much
 * horizontal space.
 *
 * v1.0.1 — added showTitle / showVersion PCF properties, alignment fix
 * (explicit 2-row grid), pale color scheme, option-set color binding.
 *
 * v1.0.6 (task 023, FR-14/FR-18) — the rendered component now lives in
 * `@spaarke/ui-components` (entity-agnostic `TrackingFieldTrio`; options
 * injected via props). This `index.ts` is the ONLY place in the tree that
 * knows the bound record's Access Permission choice values —
 * `getAccessPermissionOptions()` supplies the real Dataverse OptionSet
 * metadata (value + label + color) when available, falling back to the
 * hardcoded Standard/Limited/Restricted triple (no color — the shared
 * core's position-based default palette applies) when metadata is
 * unavailable (e.g., harness/test environments), preserving the PRE-LIFT
 * behavior (NFR-04 — zero regression).
 *
 * v1.0.8 (task 040, teams-app-r1) — wires the shared core's governance
 * toolbar (person + email icons). `onOpenGrantModal` / `onOpenEmailMembers`
 * are STUB handlers here (console-logged, no dialog) — task 041 replaces
 * the grant-modal stub, task 042 replaces the email-members stub, with no
 * further changes required to the shared `TrackingFieldTrio` core.
 * `canGrantAccess` defaults to `true` (fail-open) — task 041 wires the real
 * privilege check once the access-grant flow's authorization data source
 * exists; building that check here would be premature (no consumer yet).
 *
 * v1.0.9 (task 041, teams-app-r1) — replaces the grant-modal stub with the
 * real `AccessGrantModal` (`@spaarke/ui-components`). This file is the ONLY
 * place that knows the `sprk_project` entity + its `sprk_assigned*` field
 * names (R1 scope per design.md §5 — `sprk_externalrecordaccess` is
 * `sprk_project`-scoped) — the shared modal itself stays entity-agnostic
 * (ADR-012), receiving candidates/grants/search/classification via callback
 * props backed by `context.webAPI` (host-context Dataverse reads, per
 * `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`). BFF writes (grant /
 * invite-and-grant / revoke) go through `@spaarke/auth`'s `authenticatedFetch`
 * (bootstrapped in `init()` via `authInit.ts`, same pattern as
 * `SemanticSearchControl`).
 *
 * Historical note (v1.0.9 through v1.0.30): `canGrantAccess` reflected a
 * `context.utils.hasEntityPrivilege('sprk_externalrecordaccess', Create,
 * Global)` check that failed OPEN, and this comment asserted that
 * `AccessGrantModal` applied "a second, defense-in-depth gate on the same
 * prop". Both halves were wrong — see `evaluateGrantGate()` below, which
 * replaced them in v1.0.31.
 *
 * v1.0.10 (task 042, teams-app-r1) — replaces the email-members stub with the
 * canonical `SendEmailDialog` (`EmailComposer` engine, `@spaarke/ui-components`,
 * ADR-045). Clicking the email icon reuses the SAME `fetchCandidates()`
 * membership-contact data source task 041 wired for the grant modal — no
 * separate recipient-derivation rule (per this task's `<constraint
 * source="project">`). Candidates without a populated email are dropped; a
 * record with NO emailable membership contacts shows a small empty-state
 * alert instead of opening the dialog with zero recipients (send flows
 * through the composer's own `sendCommunication()` call — no custom send
 * logic is added here).
 *
 * v1.0.11 (task 043, teams-app-r1) — wires `AccessGrantModal`'s new
 * `accessPermissionState` prop (spec FR-14 Option A sharing gate) to this
 * record's bound `sprk_project` Access Permission field. This file is the
 * ONLY place that maps the raw `ACCESS_PERMISSION_STANDARD` /
 * `_LIMITED` / `_RESTRICTED` OptionSet integers to the shared modal's
 * entity-agnostic `AccessPermissionState` vocabulary — the modal itself
 * never sees the raw Dataverse values (ADR-012). Distinct from, and does
 * not touch, the per-grant `sprk_accesslevel` wiring below.
 *
 * v1.0.30 (task 065, unified-access-control-r2, FR-29) — wires two new
 * `AccessGrantModal` props: `pickUser` opens the host's NATIVE advanced-lookup
 * side pane for a `systemuser` (mirrors `pickContact`/`pickOrganization`
 * exactly — `INavigationService.openLookup` already accepts any Dataverse
 * entity type, so no new navigation-adapter code was needed) for the "+ User"
 * internal system-user share picker; `fetchSecureOwnerInfo` reads the bound
 * record's `ownerid`/`owningbusinessunit`/`sprk_issecure` (host-context,
 * single-entity) and returns the owner + business-unit display names for a
 * secure record, or `null` otherwise — design.md §5.1a's correction applies
 * (`ownerid` resolves to the `Secure Project` OWNER TEAM, not a service
 * account; this file reads whatever Dataverse's polymorphic Owner field
 * resolves to, generically). Both props are entity-agnostic reads/writes at
 * the modal boundary — this file remains the ONLY place that knows the raw
 * Dataverse field names (ADR-012), same discipline as every prior wiring pass.
 *
 * v1.0.31 (task 118, unified-access-control-r2, FR-07 / owner decision D-1
 * option C) — the Manage Access gate now asks THE SERVER'S QUESTION with THE
 * SERVER'S FAIL DIRECTION. Until now the UI and the BFF asked different
 * questions about the same action: the BFF asks "do you hold Write on THIS
 * record?" and denies what it cannot evaluate (`DelegationRuleFilter`); this
 * control asked "may you create rows in the `sprk_externalrecordaccess` TABLE,
 * anywhere?" and ALLOWED what it could not evaluate. Two consequences, both
 * real: a user with the table privilege but no Write on a confidential matter
 * was offered the button and then refused by the server, and whenever
 * `hasEntityPrivilege` was unavailable the affordance appeared for everyone.
 * `evaluateGrantGate()` replaces it with `GET /api/v1/external-access/
 * can-manage-access`, whose answer IS the filter's own verdict, and the gate
 * now fails CLOSED: anything other than a 200 naming this record disables the
 * affordance. The answer is asynchronous while `updateView` is synchronous, so
 * it resolves into control state and re-renders (the `authInit` pattern this
 * file already uses) rather than blocking a render on a network call.
 *
 * v1.0.32 (task 138, unified-access-control-r2 — the Access Permission levels
 * made real; owner round 2 item 3 + Q6 + O1 FINAL):
 * - The modal's state now folds in the record's SECURE flag through the shared
 *   pure `resolveAccessPermissionState`: Restricted → 'restricted'; Limited, or
 *   Standard on a secure record, or Standard while `sprk_issecure` is unreadable
 *   → 'limited' (fail CLOSED); Standard on a record read as not secure →
 *   'standard'. The secure read used for this GATING is `ensureSecureFlag()`;
 *   the owner/BU DISPLAY read (`fetchSecureOwnerInfo`) stays fail-soft.
 * - The pill honours a read-only form: `context.mode.isControlDisabled`
 *   disables all three controls, and the bound column's `security.editable ===
 *   false` disables the pill alone (re-read in init and in every updateView).
 * - On a secure record the closed pill reads "Secure" in red (owner O1 FINAL);
 *   its menu is the unchanged Standard / Limited / Restricted list, so the
 *   secure display never rewrites the stored value.
 * - `accessPermission` is now an OPTIONAL bound property, so the control can sit
 *   on a form whose table has no such column; unbound → no pill. (Corrected for
 *   task 173, owner round 81: `sprk_communication.sprk_accesspermission` is NOT
 *   retired - on a child table the column is a display copy of the parent's
 *   value, written by the BFF and locked on the form while the record has a
 *   parent; no TrackingFieldTrio is placed on Communication.)
 * - The dead `onSetStandingGrant` wiring is removed (the modal has had no
 *   standing-grant control since task 073 UAT v1.0.24 #5).
 *
 * v1.0.41 (task 153, unified-access-control-r2 — owner round 83 item 11, O1 "BOTH"): an access-status indicator in the
 *   header row. The host reads task 064's per-record route (`GET /api/v1/records/{table}/{id}/no-access`, Read-gated,
 *   the same answer the form banner `sprk_accessstatus_banner.js` reads) with `evaluateGrantGate`'s rules — fail
 *   closed to "Access status unavailable", the answer must name THIS record, a late answer for a record the control has
 *   left is dropped — and passes it as `accessStatus`. Clicking it (only when the server says the caller may manage
 *   access) opens Manage Access, at the No Access List when a No Access restriction applies (`initialSection`).
 *   Only the three root tables are asked; on any other host table no indicator is drawn.
 *
 * v1.0.40 (task 067, unified-access-control-r2 — owner round 59 item 3; task 066 folded in): the bundled
 *   `AccessGrantModal` shows a read-only No Access List (task 064's `GET /api/v1/records/{table}/{id}/no-access`, Write
 *   holders only), marks Current Access rows an in-force wall overrides ("No Access") and rows the record's Secure /
 *   Limited / Restricted state cancels ("No effect"). New host callback `fetchContactOrganizationMemberships` reads
 *   `sprk_contactorganization` so a contact in a walled organization is marked too. Walls are still authored only in
 *   No Access Entries (task 154).
 *
 * v1.0.39 (task 114, unified-access-control-r2 — owner test round 3, 2026-10-07): dark mode still light on 1.0.38 —
 *   a STANDARD control's `fluentDesignLanguage.isDarkTheme` reads false in Spaarke dark mode, so the theme no longer
 *   reads the PCF context (user choice → dark-mode URL flag → navbar; the shared resolver also gained the URL step).
 *   While a lookup is open the lookup pane now opens ON TOP of the Manage Access modal where it can be layered above
 *   it (SprkModal `sidePaneLayering`), else the modal docks left as before.
 *
 * v1.0.38 (task 114, unified-access-control-r2 — owner test round 2, 2026-10-07): dark mode. The control and the
 *   Manage Access modal (which renders inside this control's FluentProvider) hard-coded `webLightTheme`; they now use
 *   `resolveThemeWithUserPreference` and re-render on a theme change (`setupThemeListener`), per ADR-021. While a
 *   lookup is open the modal now dims without turning see-through.
 *
 * v1.0.37 (task 114, unified-access-control-r2 — owner test feedback 2026-10-07):
 * - `pickUser` honours the modal's `excludeExternal` (set on a Restricted record): the "+ User" lookup leaves out
 *   users flagged `sprk_isexternal = true` (blank counts as internal), and the pick carries the user's email so the
 *   modal can tell same-named users apart and name the person in a refusal.
 * - The bundled `AccessGrantModal` keeps itself visible (docked left of the lookup pane, dimmed) while a lookup is
 *   open instead of hiding — hiding read as the modal closing — and names the person in `/share-user`'s refusals.
 *
 * v1.0.36 (task 114, unified-access-control-r2 — owner round 67 amendment 4(c)): no change in this file's logic; the
 * bundled `AccessGrantModal` labels a user share the BFF marks `externalNoAccess` (a Restricted record, a user flagged
 * `sprk_isexternal = true`) as "External user — no access" until the server removes it.
 *
 * v1.0.35 (task 140, unified-access-control-r2 — contact-side Grant Access, owner C4 / Q2):
 * `fetchExistingGrants` also reads `_sprk_grantedbycontact_value` (the new contact-typed issuer lookup), and the
 * bundled `AccessGrantModal` shows a contact-issued grant as "Granted by {contact} (external contact)". Revoking it
 * from Current Access is unchanged (`/revoke`, Write on the record).
 *
 * v1.0.34 (task 142, unified-access-control-r2 — Assigned-To auto-grants, owner Q5 / A3 / A2):
 * no change in this file's logic; the bundled `AccessGrantModal` now reads the record's Assigned-To ledger from the
 * BFF (`GET /api/v1/external-access/assigned-access`) and shows SERVER-derived suggestions on a secure record
 * ("Suggested from Assigned Paralegal 1", Grant / Dismiss — owner A3 = prompt), names the field behind an automatic
 * grant in Current Access, and — owner A2 reversed (standing and organization access stay) — warns before and after
 * removing an automatic grant whose contact still reaches the record through a standing or organization term.
 * `CANDIDATE_ROLE_FIELDS` stays for the email-members feature; a candidate the server already suggests is offered
 * once, in Suggested Access.
 *
 * v1.0.33 (task 139, unified-access-control-r2 — the grant model, owner C4 + Q1):
 * no change in this file's logic; the bundled `AccessGrantModal` now reports a
 * `/grant` or `/invite-and-grant` that the server capped at the caller's own
 * level (the narrowed notice), and shows the server's own sentence for the new
 * refusals — would-lower-existing (409), grantee on the No Access list (422),
 * caller cannot grant (403) — and for `/unshare-user`'s refusal to remove the
 * last person who can open a secure record.
 *
 * @remarks
 * - Uses React 16 APIs per ADR-022 (ReactDOM.render, not createRoot)
 * - Uses Fluent UI v9 per ADR-021 (via platform libraries)
 */

import { IInputs, IOutputs } from './generated/ManifestTypes';
import * as React from 'react';
import * as ReactDOM from 'react-dom';
import {
  FluentProvider,
  Dialog,
  DialogSurface,
  DialogBody,
  DialogTitle,
  DialogContent,
  DialogActions,
  Button,
} from '@fluentui/react-components';
import { authenticatedFetch } from '@spaarke/auth';
// Aliased on import — the PCF control class below MUST be named
// `TrackingFieldTrio` to match `constructor="TrackingFieldTrio"` in
// ControlManifest.Input.xml, so the shared component is imported under a
// distinct local name.
import {
  TrackingFieldTrio as SharedTrackingFieldTrio,
  type ITrackingFieldTrioProps,
  type IAccessPermissionOption,
  type ITrackingAccessStatus,
  type GrantModalSection,
  readAccessStatus,
} from '@spaarke/ui-components/dist/components/TrackingFieldTrio';
import {
  AccessGrantModal,
  type IAccessGrantCandidate,
  type IAccessGrantRecord,
  type IContactSearchResult,
  type IOrganizationPick,
  type IUserPick,
  type IUserPickOptions,
  type ISecureOwnerInfo,
  type IContactOrganizationMembership,
  type ExternalGrantRootType,
  type AccessPermissionState,
  resolveAccessPermissionState,
} from '@spaarke/ui-components/dist/components/AccessGrantModal';
// Spaarke theme resolution (ADR-021 dark mode): the user's Spaarke theme choice, then the MDA's own theme — the same
// helpers the Communication PCFs use.
import { resolveThemeWithUserPreference, setupThemeListener } from '@spaarke/ui-components/dist/utils/themeStorage';
// Shared side-pane Advanced Lookup (task 071) — adopted as-is per §11: the PCF
// host wires INavigationService.openLookup (→ Xrm.Utility.lookupObjects) and
// passes plain pickContact/pickOrganization callbacks into the Xrm-free modal.
import { createXrmNavigationService } from '@spaarke/ui-components/dist/utils/adapters';
import type { INavigationService } from '@spaarke/ui-components/dist/types/serviceInterfaces';
import type { ILookupItem } from '@spaarke/ui-components/dist/types/LookupTypes';
// Canonical `SendEmailDialog` (task 042, ADR-045) — the `EmailComposer`
// wrapper that owns the Dialog chrome + `sendCommunication()` send flow.
// This is the ONLY email-send surface this control is allowed to open (no
// forked dialog, no ad-hoc fetch to the send endpoint).
import { SendEmailDialog } from '@spaarke/ui-components/dist/components/EmailComposer';
// Shared Xrm-backed compose-lookup + upload factory (task 073 UAT v1.0.24 #10/#11).
// Builds `onLookupRecipients` (To/Cc native people picker) + `onUploadLocalAttachment`
// (local file → SPE → governed sprk_document, so it rides the send payload instead of
// being dropped) from auth + BFF URL. The SAME factory EmailWorkspaceWidget uses.
import { createXrmEmailComposeHandlers } from '@spaarke/ui-components/dist/components/EmailComposer';
// Shared per-widget error boundary (task 073 UAT #1) — wraps the dialog subtree
// so a render error inside a shared dialog degrades to a small inline card
// instead of blanking the whole PCF (defense-in-depth alongside the
// react/jsx-runtime dedupe in ../webpack.config.js).
import { WidgetErrorBoundary } from '@spaarke/ui-components/dist/components/WidgetErrorBoundary';
import { cleanGuid } from '@spaarke/ui-components/dist/services/PolymorphicResolverService';
import { getXrm } from '@spaarke/ui-components/dist/utils/xrmContext';
import { initializeAuth } from './authInit';
// Dataverse Environment Variable resolution (task 073 UAT fix) — the SAME mechanism
// SemanticSearchControl uses so the grant modal's BFF auth needs NO per-control form config: the MSAL
// client id / BFF app id / BFF base url come from sprk_MsalClientId / sprk_BffApiAppId / sprk_BffApiBaseUrl.
import { getEnvironmentVariable, getApiBaseUrl } from '../shared/utils/environmentVariables';

// Access Permission choice values of the three grant ROOTS — sprk_project,
// sprk_matter and sprk_workassignment carry the identical option set (verified
// live 2026-09-04 and 2026-09-30; the BFF's ExternalParticipationService uses the
// same integers). Entity-specific: lives ONLY here (the PCF caller), never in the
// shared `TrackingFieldTrio` core (FR-14). The same global choice backs the
// column on To Do, Event, Communication and Document, where it is a display copy
// of the parent's value that enforcement never reads (task 173, owner round 81;
// it was to be retired by task 138). No TrackingFieldTrio is placed on Communication.
const ACCESS_PERMISSION_STANDARD = 100000000;
const ACCESS_PERMISSION_LIMITED = 100000001;
const ACCESS_PERMISSION_RESTRICTED = 100000002;

// Owner O1 FINAL (2026-10-01): on a SECURE record the closed pill reads "Secure"
// (red) for both secure and secure + Restricted. That is a closed-LABEL change
// only: the menu keeps the record's own Standard / Limited / Restricted options,
// so Standard stays selectable and no item writes a value other than its own
// (a two-item "Secure" → Limited menu was not part of O1 FINAL — task 138 r1).
const SECURE_PILL_LABEL = 'Secure';

// Fallback segments (no per-option color) used when the bound OptionSet's
// field metadata isn't available (e.g., harness/test environments). The
// shared core's position-based default palette (green/yellow/red, by index)
// then supplies the same colors the pre-lift hardcoded fallback did.
const FALLBACK_ACCESS_PERMISSION_OPTIONS: IAccessPermissionOption[] = [
  { value: ACCESS_PERMISSION_STANDARD, label: 'Standard' },
  { value: ACCESS_PERMISSION_LIMITED, label: 'Limited' },
  { value: ACCESS_PERMISSION_RESTRICTED, label: 'Restricted' },
];

// v1.0.12 (task 071, external-access-r2) — the access-grant surface is now
// POLYMORPHIC across the three external-grant roots (Project / Matter / Work
// Assignment), the UI companion to tasks 028 (read) + 070 (write). The host
// entity is derived from the bound record at runtime (`context.page.entityTypeName`)
// instead of the R1 `sprk_project` hardcode, and the modal's contact picker is
// the SHARED side-pane Advanced Lookup (INavigationService.openLookup) rather
// than the inline Combobox. This file remains the ONLY place that knows the
// concrete Dataverse entity names + their `sprk_assigned*` role fields — the
// shared `AccessGrantModal` stays entity-agnostic (ADR-012 / FR-14).
const EXTERNAL_ACCESS_ENTITY = 'sprk_externalrecordaccess';

// The polymorphic external-grant root config, keyed by the host form's entity
// (task 070/071). `recordType` is the semantic root type the modal sends as
// `{recordType, recordId}`; `rootValueField` is the `sprk_externalrecordaccess`
// lookup filter for reading the record's active grants (fixes the R1
// `_sprk_projectid_value` bug — the verified lookup name is `_sprk_project_value`,
// per task 070's live $metadata verification). Matter + Work Assignment carry
// the SAME `sprk_assigned*` role fields as Project (verified), so a single
// shared CANDIDATE_ROLE_FIELDS set below serves all three.
interface IGrantRootConfig {
  recordType: ExternalGrantRootType;
  rootValueField: string;
}
const GRANT_ROOT_BY_ENTITY: Readonly<Record<string, IGrantRootConfig>> = {
  sprk_project: { recordType: 'project', rootValueField: '_sprk_project_value' },
  sprk_matter: { recordType: 'matter', rootValueField: '_sprk_matter_value' },
  sprk_workassignment: { recordType: 'workassignment', rootValueField: '_sprk_workassignment_value' },
};
// Back-compat default when the host entity is unknown/unavailable (e.g. a
// harness environment where `context.page` isn't populated) — preserves the
// R1 project behavior.
const DEFAULT_GRANT_ROOT: IGrantRootConfig = GRANT_ROOT_BY_ENTITY['sprk_project'];

// The access-conferring role-field set shared by all three grant roots
// (Project / Matter / Work Assignment carry the SAME `sprk_assigned*` lookups —
// verified in notes/polymorphic-grant-authoring-enhancement.md; task 021's
// convention-based discovery, mirrored client-side). A newly-added `sprk_assigned*` field
// does NOT auto-qualify here the way it does in the server-side metadata
// discovery (task 021/022) — this client-side list is a UI convenience for
// the candidate section, not the security enforcement path (enforcement is
// entirely server-side per design.md §5). If the role-field set changes,
// update this list; nothing security-load-bearing depends on it being
// exhaustive.
const CANDIDATE_ROLE_FIELDS: readonly { attr: string; role: string }[] = [
  { attr: 'sprk_assignedattorney1', role: 'Assigned Attorney 1' },
  { attr: 'sprk_assignedattorney2', role: 'Assigned Attorney 2' },
  { attr: 'sprk_assignedparalegal1', role: 'Assigned Paralegal 1' },
  { attr: 'sprk_assignedparalegal2', role: 'Assigned Paralegal 2' },
  { attr: 'sprk_assignedtoexternal', role: 'Assigned To (External)' },
  { attr: 'sprk_assignedtointernal', role: 'Assigned To (Internal)' },
];

/** One comparable spelling for a Dataverse record id: lowercase, no braces, no surrounding space.
 *
 * Exists because the PCF host and the BFF do not agree on GUID formatting — `Xrm`'s `getId()` yields
 * `{ABC…}` while .NET's default `ToString()` yields `abc…` — and task 118's grant gate compares the id it
 * ASKED about with the id the server ANSWERED about. That comparison fails closed, so a purely cosmetic
 * disagreement would hide the Manage Access affordance rather than merely log something. Normalizing is
 * the cheap end of that trade. */
const normalizeRecordId = cleanGuid;

/** Resolves the Dataverse org URL for the MSAL redirect URI, mirroring the
 * `Xrm.Utility.getGlobalContext().getClientUrl()` pattern used by every other
 * Spaarke PCF's `authInit.ts` (e.g. `RelatedDocumentCount`,
 * `CommunicationActions`). Returns `''` when `Xrm` isn't available (harness). */
function getClientUrl(): string {
  const xrm = (
    window as unknown as { Xrm?: { Utility?: { getGlobalContext?: () => { getClientUrl?: () => string } } } }
  ).Xrm;
  return xrm?.Utility?.getGlobalContext?.()?.getClientUrl?.() ?? '';
}

export class TrackingFieldTrio implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  /** Removes the theme-change listeners added in `init` (dark mode, task 114 owner test 2026-10-07). */
  private themeListenerCleanup?: () => void;
  private container: HTMLDivElement;
  private notifyOutputChanged: () => void;
  private context: ComponentFramework.Context<IInputs>;

  // Local state that mirrors the bound fields. We keep them here so the
  // control can render immediately when the user clicks a segment/toggle,
  // then flush the change via notifyOutputChanged() → getOutputs().
  private monitorValue = false;
  private highPriorityValue = false;
  private accessPermissionValue: number | null = null;

  // Access-grant modal state (task 041). `authInitPromise` gates every
  // `authenticatedFetch` call the modal makes so a click before MSAL
  // bootstrap completes still succeeds (awaits, doesn't fail) rather than
  // racing `@spaarke/auth`'s "not initialized" guard.
  private isGrantModalOpen = false;

  /** Whether the CALLER may change who can access the bound record, per the server
   * (`GET /api/v1/external-access/can-manage-access`, task 118).
   *
   * Starts `false` and STAYS `false` until the server says otherwise. That is the
   * fail-closed default the whole task turns on: before v1.0.31 this field started
   * `true`, so every unanswerable access question resolved to "offer the button".
   * An affordance that appears while the answer is unknown is a promise the server
   * may refuse — and when the answer is unknown because authorization is degraded,
   * it is a promise made in exactly the conditions that warrant caution. */
  private canGrantAccessValue = false;

  /** The record id the gate has been ASKED about — not the one it has been answered for.
   *
   * Three distinct states, and all three are load-bearing:
   * `undefined` = never asked (the initial state, so the first `updateView` still asks);
   * `null`      = asked while no record was bound (a harness, or an unsaved form) — re-asked
   *               the moment an id appears;
   * a string    = asked about that record; a different id means the form rebound and the
   *               previous record's verdict must not be carried forward.
   *
   * It is set BEFORE the request goes out, which is what keeps `updateView` — called on every
   * field write and form refresh — from issuing one OBO exchange plus two Dataverse calls per
   * refresh while an answer is already in flight. */
  private grantGateRequestedFor: string | null | undefined = undefined;

  /** The bound record's `sprk_issecure`, read for GATING (task 138): `true` / `false` from a successful
   * read, `null` while unread or when the read fails or the value is hidden (field-level security). The
   * modal state treats `null` as Limited — an unknown Secure flag must never widen what the dialog offers. */
  private isSecureValue: boolean | null = null;

  /** The record id the secure read was ASKED about — the same three-state discipline as
   * {@link grantGateRequestedFor}, so `updateView` does not re-read on every refresh. */
  private secureFlagRequestedFor: string | null | undefined = undefined;

  /** The Manage Access section the current open was asked for (task 153); reset on close. */
  private grantModalSection: GrantModalSection | undefined = undefined;

  /** The record's access status from task 064's route (task 153). `undefined` while not asked, in flight, or on a
   * host table that has no such route (no indicator is drawn); the shared ACCESS_STATUS_UNAVAILABLE on every failure to
   * obtain an answer this client can trust. */
  private accessStatusValue: ITrackingAccessStatus | undefined = undefined;

  /** The record id the status was ASKED about — the same three-state discipline as {@link grantGateRequestedFor}. */
  private accessStatusRequestedFor: string | null | undefined = undefined;

  private authInitPromise: Promise<void> = Promise.resolve();

  // Email-members state (task 042). `apiBaseUrl` mirrors the value passed to
  // `initializeAuth()` — reused as `SendEmailDialog`'s `bffBaseUrl` prop (same
  // pattern as every other Spaarke PCF that hosts the canonical dialog, e.g.
  // `CommunicationActionsApp.tsx`).
  private apiBaseUrl = '';
  private isSendEmailDialogOpen = false;
  private isEmailEmptyStateOpen = false;
  private emailRecipients: string[] = [];

  public init(
    context: ComponentFramework.Context<IInputs>,
    notifyOutputChanged: () => void,
    _state: ComponentFramework.Dictionary,
    container: HTMLDivElement
  ): void {
    this.container = container;
    this.notifyOutputChanged = notifyOutputChanged;
    this.context = context;

    this.monitorValue = context.parameters.monitor?.raw ?? false;
    this.highPriorityValue = context.parameters.highPriority?.raw ?? false;
    this.accessPermissionValue = context.parameters.accessPermission?.raw ?? null;

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const params = context.parameters as any;
    const webApi = context.webAPI;
    // task 073 UAT fix: resolve MSAL config from the manifest input properties FIRST, then fall back to
    // the Dataverse ENVIRONMENT VARIABLES (sprk_MsalClientId / sprk_BffApiAppId / sprk_BffApiBaseUrl) —
    // the SAME mechanism SemanticSearchControl uses. The prior code only read the (empty) manifest props,
    // so on an environment configured purely via env vars it threw "MSAL Client ID not configured".
    // getApiBaseUrl() normalizes the URL (strips any trailing /api) so the modal's `/api/...` paths resolve.
    this.authInitPromise = (async () => {
      const clientAppId =
        (params.clientAppId?.raw as string) || (await getEnvironmentVariable(webApi, 'sprk_MsalClientId')) || '';
      const bffAppId =
        (params.bffAppId?.raw as string) || (await getEnvironmentVariable(webApi, 'sprk_BffApiAppId')) || '';
      this.apiBaseUrl = (params.apiBaseUrl?.raw as string) || (await getApiBaseUrl(webApi));
      const tenantId = (await getEnvironmentVariable(webApi, 'sprk_TenantId')) || '';
      await initializeAuth(clientAppId, bffAppId, this.apiBaseUrl, getClientUrl(), tenantId);
      // Re-render so surfaces that read this.apiBaseUrl directly (e.g. SendEmailDialog's bffBaseUrl) pick
      // up the resolved value now that auth init has completed.
      this.renderControl();
    })().catch(err => {
      console.error(
        "[TrackingFieldTrio] Auth initialization failed — the access-grant modal's BFF calls will fail until the page is reloaded.",
        err
      );
    });

    // Ask the server whether this caller may manage access (task 118). Deliberately NOT awaited: the
    // control renders immediately with the affordance DISABLED and enables it only if the answer says so,
    // rather than delaying first paint on a network call. Nothing is swallowed — every failure path
    // inside `evaluateGrantGate` resolves the gate to `false` and logs.
    this.ensureGrantGate();
    // Read the record's Secure flag for the Access Permission gate (task 138) — also not awaited; until it
    // answers, the modal state is the fail-closed Limited.
    this.ensureSecureFlag();
    // The access-status indicator (task 153) — not awaited; nothing is drawn until it answers.
    this.ensureAccessStatus();

    // Re-render when the user switches the Spaarke theme (same tab or another tab).
    this.themeListenerCleanup = setupThemeListener(() => this.renderControl());

    this.renderControl();
  }

  public updateView(context: ComponentFramework.Context<IInputs>): void {
    this.context = context;

    // Framework-driven update (e.g., form refresh, another script wrote to
    // the field). Sync local state to the framework's raw values.
    this.monitorValue = context.parameters.monitor?.raw ?? false;
    this.highPriorityValue = context.parameters.highPriority?.raw ?? false;
    this.accessPermissionValue = context.parameters.accessPermission?.raw ?? null;

    // Re-ask the server if — and only if — this control is now bound to a different record (task 118).
    this.ensureGrantGate();
    // Same for the record's Secure flag (task 138).
    this.ensureSecureFlag();
    // And for the access-status indicator (task 153).
    this.ensureAccessStatus();

    this.renderControl();
  }

  /**
   * Reads the bound record's access status for the indicator (task 153), unless it is already asked or answered for
   * this record. Only the three root tables have task 064's route: on any other host table nothing is asked and no
   * indicator is drawn (the project fallback of `resolveGrantRoot` would ask about the wrong table).
   */
  private ensureAccessStatus(): void {
    const recordId = this.getRecordId();
    if (recordId === this.accessStatusRequestedFor) {
      return;
    }

    // A different record: the previous record's status must not be shown for even one render.
    this.accessStatusRequestedFor = recordId;
    this.accessStatusValue = undefined;
    const root = GRANT_ROOT_BY_ENTITY[this.getHostEntity()];
    if (!recordId || !root) {
      return;
    }

    void this.evaluateAccessStatus(recordId, root.recordType);
  }

  /**
   * Asks task 064's per-record route whether the record is Secure and under a No Access restriction, with
   * `evaluateGrantGate`'s rules, through the shared `readAccessStatus`: every non-200 (the route's uniform 404
   * included), a thrown call (auth not initialised, network), an unparseable body or an answer that does not name THIS
   * record is "unavailable" — shown as "Access status unavailable", never as "not restricted"; an unknown or missing
   * signal is `unknown`. Entries a Write caller also receives are never read: the indicator shows no count, name or
   * reason. Here only the staleness rule is applied.
   */
  private async evaluateAccessStatus(recordId: string, recordType: ExternalGrantRootType): Promise<void> {
    // Never rejects; every failure is ACCESS_STATUS_UNAVAILABLE (the shared helper's tests pin each case).
    const status = await readAccessStatus(this.authenticatedFetchGated, recordType, recordId);

    // Drop a late answer for a record the control has since left.
    if (this.accessStatusRequestedFor !== recordId) {
      return;
    }
    this.accessStatusValue = status;
    this.renderControl();
  }

  /**
   * Reads the bound record's `sprk_issecure` for the Access Permission GATE (task 138), unless that read is
   * already asked or answered for this record. FAIL CLOSED: a failed read, a missing record id or a hidden
   * value leaves {@link isSecureValue} `null`, which `resolveAccessPermissionState` maps to Limited. Distinct
   * from the fail-soft owner/BU DISPLAY read in `fetchSecureOwnerInfo`, which is unchanged.
   */
  private ensureSecureFlag(): void {
    const recordId = this.getRecordId();
    if (recordId === this.secureFlagRequestedFor) {
      return;
    }

    this.secureFlagRequestedFor = recordId;
    this.isSecureValue = null;
    if (!recordId) {
      return;
    }

    void (async () => {
      let value: boolean | null = null;
      try {
        const record = (await this.context.webAPI.retrieveRecord(
          this.getHostEntity(),
          recordId,
          '?$select=sprk_issecure'
        )) as unknown as Record<string, unknown>;
        const raw = record['sprk_issecure'];
        value = raw === true ? true : raw === false ? false : null;
      } catch (err) {
        console.warn(
          '[TrackingFieldTrio] Could not read whether this record is secure; Manage Access offers only named grants.',
          err
        );
      }
      // Drop a late answer for a record the form has since left.
      if (this.secureFlagRequestedFor !== recordId) {
        return;
      }
      this.isSecureValue = value;
      this.renderControl();
    })();
  }

  /** Whether the `accessPermission` property is bound to a column on this form (task 138 — the property
   * is optional so the control can sit on a table without the column). A bound OptionSet parameter
   * carries its attribute metadata (`attributes.LogicalName`); an unbound optional one does not.
   * A numeric `raw` value is also proof of a binding (an unbound property never holds one), so a host
   * that omits the metadata — the case `getAccessPermissionOptions()` falls back for — still shows and
   * writes back the pill whenever the record has a value. Live gate 16(d) re-checks the three root
   * forms under v1.0.32 (task 138 r1, verifier finding 7). */
  private isAccessPermissionBound(): boolean {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const param = this.context.parameters.accessPermission as any;
    const logicalName = param?.attributes?.LogicalName;
    if (typeof logicalName === 'string' && logicalName.length > 0) return true;
    return typeof param?.raw === 'number';
  }

  /** Whether the bound access-permission column is editable for this user — `false` only when the
   * platform says so explicitly (`security.editable === false`, e.g. field-level security). */
  private isAccessPermissionEditable(): boolean {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const security = (this.context.parameters.accessPermission as any)?.security;
    return security?.editable !== false;
  }

  /**
   * Asks the server about the CURRENTLY bound record, unless that question is already asked or answered.
   *
   * Called from both `init` and `updateView`, which is why the guard lives here rather than at either
   * call site: `updateView` fires on every field write and every form refresh, and each evaluation costs
   * an OBO exchange plus two Dataverse calls.
   *
   * When the record HAS changed, the previous record's verdict is revoked FIRST. Carrying it forward for
   * even one render would offer Manage Access on a record for which nobody has been authorized — and the
   * case where a form rebinds from a record you can write to one you cannot is exactly the case that
   * matters.
   */
  private ensureGrantGate(): void {
    const recordId = this.getRecordId();
    if (recordId === this.grantGateRequestedFor) {
      return;
    }

    this.grantGateRequestedFor = recordId;
    this.canGrantAccessValue = false;
    void this.evaluateGrantGate(recordId);
  }

  /**
   * Extract per-option value/label/color from the bound OptionSet's field
   * metadata so the segmented picker can honor the choice column's real
   * Dataverse values, labels, and colors. Falls back to the hardcoded
   * Standard/Limited/Restricted triple (no color) when metadata isn't
   * available, so the control always renders 3 segments (NFR-04).
   */
  private getAccessPermissionOptions(): IAccessPermissionOption[] {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const attrs = (this.context.parameters.accessPermission as any)?.attributes;
    const options = attrs?.Options as { Value: number; Label: string; Color?: string }[] | undefined;
    if (!options || options.length === 0) return FALLBACK_ACCESS_PERMISSION_OPTIONS;
    return options.map(o => ({
      value: o.Value,
      label: o.Label,
      color: o.Color,
    }));
  }

  /**
   * Resolve a bound field's Dataverse display name from the PCF context.
   * Falls back to the provided default when metadata isn't available (e.g.,
   * harness/test environments).
   */
  private getFieldLabel(param: ComponentFramework.PropertyTypes.Property | undefined, fallback: string): string {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const displayName = (param as any)?.attributes?.DisplayName as string | undefined;
    return displayName || fallback;
  }

  // =========================================================================
  // Access-grant modal wiring (task 041, teams-app-r1). Localized to this
  // file per the task's "no baked-in entity/field values in the shared core"
  // discipline (FR-14) — AccessGrantModal receives everything below via
  // callback props and stays entity-agnostic.
  // =========================================================================

  /** The current record's id, via the standard PCF `context.page.entityId`
   * surface (same pattern as `RelatedDocumentCount`'s index.ts). `null` in a
   * harness/test host where `page` isn't populated. */
  private getRecordId(): string | null {
    const page = (this.context as unknown as { page?: { entityId?: string } }).page;
    return page?.entityId || null;
  }

  /** The bound record's entity logical name, via `context.page.entityTypeName`
   * (task 071 — replaces the R1 `sprk_project` hardcode). Falls back to
   * `sprk_project` in a harness/test host where `page` isn't populated. */
  private getHostEntity(): string {
    const page = (this.context as unknown as { page?: { entityTypeName?: string } }).page;
    return page?.entityTypeName || 'sprk_project';
  }

  /** Resolves the polymorphic external-grant root config for the bound host
   * entity (task 070/071). Unknown entities fall back to the project root. */
  private resolveGrantRoot(): IGrantRootConfig {
    return GRANT_ROOT_BY_ENTITY[this.getHostEntity()] ?? DEFAULT_GRANT_ROOT;
  }

  /** Lazily-constructed shared navigation service backing the side-pane Advanced
   * Lookup (task 071). Cached so the contact + org pickers reuse one instance. */
  private navService?: INavigationService;
  private getNavService(): INavigationService {
    if (!this.navService) {
      this.navService = createXrmNavigationService();
    }
    return this.navService;
  }

  /** Opens the Contact record as an OOB modal (task 073 UAT v1.0.24 #6) via
   * `Xrm.Navigation.navigateTo` (entityrecord, `target: 2` = dialog) — per
   * MODAL-DECISION-CRITERIA, opening a record uses the OOB navigator, not a
   * proprietary Fluent dialog. A user with write access can then edit the Contact.
   *
   * v1.0.24 UAT #1: the Manage Access modal is a Fluent portal (top-window
   * z-index) that renders ABOVE the platform Contact dialog, so the Contact
   * dialog appeared BEHIND it. Fix: HIDE Manage Access while the Contact dialog
   * is open (so the Contact dialog is unambiguously in front and fully covers it)
   * and RESTORE it when the Contact dialog closes. The dialog opens large (70% ×
   * 80%) so it covers the Manage Access footprint. */
  private openContactRecord = (contactId: string): void => {
    // Shared cross-frame walker (task 081 / C-8).
    const xrm = getXrm('navigation');
    const wasGrantOpen = this.isGrantModalOpen;
    const restore = (): void => {
      if (wasGrantOpen && !this.isGrantModalOpen) {
        this.isGrantModalOpen = true;
        this.renderControl();
      }
    };
    try {
      if (wasGrantOpen) {
        this.isGrantModalOpen = false;
        this.renderControl();
      }
      const p = xrm?.Navigation?.navigateTo?.(
        { pageType: 'entityrecord', entityName: 'contact', entityId: contactId },
        { target: 2, position: 1, width: { value: 70, unit: '%' }, height: { value: 80, unit: '%' } }
      );
      // navigateTo resolves/rejects when the dialog closes — restore Manage Access
      // either way. If a host returns no thenable (legacy), restore immediately.
      if (p && typeof p.then === 'function') {
        p.then(restore, restore);
      } else {
        restore();
      }
    } catch (err) {
      console.warn('[TrackingFieldTrio] open Contact record failed.', err);
      restore();
    }
  };

  /** The bound record's primary-name value (e.g. the matter number) for the email
   * "Related to" chip (task 073 UAT v1.0.24 #9). Read from the form entity's
   * primary attribute — entity-agnostic, no metadata call. Undefined outside an
   * MDA host (harness) or when unset (chip then shows the humanized entity type). */
  private getRecordDisplayName(): string | undefined {
    // Shared cross-frame walker (task 081 / C-8). `any` view: typed XrmContext
    // does not declare Page.data.
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const xrm = getXrm('page') as any;
    try {
      const v = xrm?.Page?.data?.entity?.getPrimaryAttributeValue?.();
      return typeof v === 'string' && v.length > 0 ? v : undefined;
    } catch {
      return undefined;
    }
  }

  /** Lazily-built shared Xrm email compose handlers (task 073 UAT v1.0.24 #10/#11):
   * `onLookupRecipients` (native To/Cc people picker) + `onUploadLocalAttachment`
   * (local file → SPE → governed `sprk_document`, so it rides the send payload
   * instead of being dropped). Rebuilt if `apiBaseUrl` changes (it is resolved
   * asynchronously after auth init, so the first build must happen post-init —
   * which the email dialog only ever opens after). */
  private emailHandlers?: { baseUrl: string; handlers: ReturnType<typeof createXrmEmailComposeHandlers> };
  private getEmailHandlers(): ReturnType<typeof createXrmEmailComposeHandlers> {
    if (!this.emailHandlers || this.emailHandlers.baseUrl !== this.apiBaseUrl) {
      this.emailHandlers = {
        baseUrl: this.apiBaseUrl,
        handlers: createXrmEmailComposeHandlers({
          clientUrl: getClientUrl(),
          authenticatedFetch: this.authenticatedFetchGated,
          bffBaseUrl: this.apiBaseUrl,
        }),
      };
    }
    return this.emailHandlers.handlers;
  }

  /**
   * Asks the SERVER whether this caller may change who can access the bound record, and resolves the
   * Manage Access affordance from the answer (task 118, spec FR-07 / owner decision D-1 option C).
   *
   * WHAT IT ASKS. `GET /api/v1/external-access/can-manage-access?recordType=&recordId=`. That route sits
   * on the `/api/v1/external-access` group behind `DelegationRuleFilter`, so its 200 is not a second
   * opinion about the rule — it is the OUTCOME of the rule, produced by the one implementation of it
   * (`CallerRecordAccessProbe` over OBO: Write on THIS record, evaluated as the caller). A client gate
   * built on a re-implementation of a server rule drifts; this one cannot, because it never re-implements
   * anything.
   *
   * WHAT IT REPLACED, AND WHY. Through v1.0.30 this was a synchronous
   * `context.utils.hasEntityPrivilege('sprk_externalrecordaccess', Create, Global)` that returned `true`
   * on every failure. That asked a DIFFERENT question — table-level Create, anywhere, versus per-record
   * Write on this record — with the OPPOSITE fail direction. So a caller with the table privilege but no
   * Write on a confidential matter was offered the affordance and then refused by the server, and a
   * caller whose privilege could not be read at all was offered it unconditionally.
   *
   * 🔴 IT FAILS CLOSED, AND THAT IS THE POINT OF THE CHANGE. Every path that does not produce a 200
   * naming THIS record leaves the gate `false`: no record bound, auth not initialised, the fetch
   * throwing, a non-200 (including the filter's own 403), an unparseable body, `canManageAccess` not
   * exactly `true`, or an answer about a record we are no longer bound to. Each logs, so a disabled
   * affordance is diagnosable rather than mysterious — the discipline `CallerRecordAccessProbe` already
   * applies server-side, where it denies on every degraded path rather than degrading to Read.
   *
   * HARNESS AND TEST HOSTS ARE INCLUDED IN THAT, DELIBERATELY. A PCF harness has no `Xrm`, no MSAL
   * bootstrap and no BFF, so the gate stays `false` and the icon renders disabled. That does NOT make the
   * component untestable: the rendered UI lives in `@spaarke/ui-components`' `TrackingFieldTrio`, which
   * takes `canGrantAccess` as an explicit prop, and its tests pass the value they mean rather than
   * relying on a default. The only thing a harness loses is an ENABLED grant icon — an icon that, in a
   * host with no BFF, could not have completed a grant anyway.
   */
  private async evaluateGrantGate(recordId: string | null): Promise<void> {
    if (!recordId) {
      // No bound record (a harness, or a form that has not saved yet). There is no record to be
      // authorized ON, so there is nothing to allow. `ensureGrantGate` re-asks if an id appears later.
      this.setGrantGate(recordId, false);
      return;
    }

    const recordType = this.resolveGrantRoot().recordType;

    try {
      const query = `recordType=${encodeURIComponent(recordType)}&recordId=${encodeURIComponent(recordId)}`;
      // authenticatedFetchGated awaits authInitPromise first, so a gate evaluated before MSAL bootstrap
      // completes waits rather than racing the "not initialized" guard. If auth init FAILED, this rejects
      // and the catch below denies — which is the correct direction for an unprovable identity.
      const res = await this.authenticatedFetchGated(`/api/v1/external-access/can-manage-access?${query}`);

      if (!res.ok) {
        // The expected denial is the delegation filter's 403 — an answer, not an error. Logged at info so
        // a legitimately read-only user does not fill the console with warnings on every form load.
        console.info(
          `[TrackingFieldTrio] Manage Access is disabled for ${recordType} ${recordId}: the server answered ${res.status}.`
        );
        this.setGrantGate(recordId, false);
        return;
      }

      const body = (await res.json()) as { recordId?: string; canManageAccess?: boolean } | null;

      // `canManageAccess === true` exactly — not truthy. A body that omits the field, or carries a
      // truthy-but-wrong value, is an answer this client does not understand, and an answer it does not
      // understand is not a licence.
      const answeredYes = body?.canManageAccess === true;

      // The answer must name the record we asked about. Two independent hazards it closes: an in-flight
      // answer arriving after the form rebound to another record, and a proxy or cache returning some
      // other record's response.
      //
      // Compared through `normalizeRecordId` rather than raw, because the two sides do not agree on GUID
      // FORMATTING and this check fails CLOSED. The host may hand us a braced id (`Xrm`'s
      // `getId()` shape) while the server always echoes .NET's "D" format, lowercase and unbraced. Raw
      // string equality would then be false for the right record, the affordance would vanish, and the
      // symptom — "Manage Access is gone for everyone" — looks exactly like a broken gate rather than a
      // formatting mismatch. Fail-closed makes false negatives expensive, so the comparison absorbs the
      // difference instead of trusting both ends to spell a GUID the same way.
      const answersThisRecord =
        typeof body?.recordId === 'string' && normalizeRecordId(body.recordId) === normalizeRecordId(recordId);

      if (answeredYes && !answersThisRecord) {
        console.warn(
          `[TrackingFieldTrio] Discarding a can-manage-access answer for '${body?.recordId}' while bound to '${recordId}'.`
        );
      }

      this.setGrantGate(recordId, answeredYes && answersThisRecord);
    } catch (err) {
      console.warn(
        `[TrackingFieldTrio] Could not establish whether you may manage access on ${recordType} ${recordId}; ` +
          'Manage Access is disabled. An unanswerable access question is a denial, not a default.',
        err
      );
      this.setGrantGate(recordId, false);
    }
  }

  /**
   * Applies the gate's verdict and re-renders — but only if the control is still asking about
   * `answeredFor`.
   *
   * Two reasons this is a method rather than two assignments at each exit. First, the answer arrives
   * after `updateView` has returned, so every exit path in {@link evaluateGrantGate} must repaint; one
   * that assigned without repainting would leave the toolbar showing the previous verdict. Second, the
   * staleness check belongs in ONE place: a slow answer for a record the form has since left must be
   * dropped, and dropping it in four separate places is three chances to forget.
   */
  private setGrantGate(answeredFor: string | null, canGrant: boolean): void {
    if (this.grantGateRequestedFor !== answeredFor) {
      return;
    }

    this.canGrantAccessValue = canGrant;
    this.renderControl();
  }

  /** Wraps `authenticatedFetch` so a click that races MSAL bootstrap still
   * succeeds (awaits `authInitPromise` first) instead of hitting
   * `@spaarke/auth`'s "not initialized" guard. */
  private authenticatedFetchGated = async (url: string, init?: RequestInit): Promise<Response> => {
    await this.authInitPromise;
    return authenticatedFetch(url, init);
  };

  /**
   * Maps the bound record's raw `sprk_accesspermission` value (a root's own; on a To Do or Event the display copy of
   * its parent's, task 173) AND its `sprk_issecure` flag (task 043, spec
   * FR-14 Option A; task 138) to `AccessGrantModal`'s entity-agnostic `AccessPermissionState`. This is the
   * ONLY place that knows the real `ACCESS_PERMISSION_*` integers and the secure column — the shared modal
   * receives only the semantic 'standard' | 'limited' | 'restricted' vocabulary (ADR-012). The rules
   * themselves (Restricted wins; Secure implies Limited; an unreadable Secure flag is Limited) live in the
   * shared pure `resolveAccessPermissionState`, where tests pin them.
   */
  private mapAccessPermissionToState(value: number | null): AccessPermissionState {
    return resolveAccessPermissionState(value, this.isSecureValue, {
      limited: ACCESS_PERMISSION_LIMITED,
      restricted: ACCESS_PERMISSION_RESTRICTED,
    });
  }

  /** Reads the current `sprk_project` record's `sprk_assigned*` contact
   * lookups (host-context, single-entity, one `$expand` read — per
   * `DATA-ACCESS-DECISION-CRITERIA.md`) and returns the populated ones as
   * membership candidates, de-duplicated by contact (a contact assigned to
   * two role fields appears once, tagged with the first role encountered). */
  private fetchCandidates = async (): Promise<IAccessGrantCandidate[]> => {
    const recordId = this.getRecordId();
    if (!recordId) return [];

    // task 073 UAT fix: read the role lookups by their FK VALUE fields (`_sprk_X_value`), NOT by the
    // lookup logical name. You cannot `$select` a lookup by its logical name (Dataverse 400 "Could not
    // find a property named 'sprk_assignedattorney1'"), and `$expand` would depend on the PascalCase
    // navigation-property name. The `_X_value` form is stable and carries the contact's display name via
    // the FormattedValue annotation — no nav-property-casing dependency. Emails (which drive the
    // external-vs-internal grant routing) are batch-fetched separately below.
    const valueFields = CANDIDATE_ROLE_FIELDS.map(f => `_${f.attr}_value`).join(',');
    let record: Record<string, unknown>;
    try {
      record = (await this.context.webAPI.retrieveRecord(
        this.getHostEntity(),
        recordId,
        `?$select=${valueFields}`
      )) as unknown as Record<string, unknown>;
    } catch {
      // A host entity may not carry every role field — candidates are a convenience (the named-contact
      // picker still works), so fail SOFT to an empty list rather than breaking the whole modal load.
      return [];
    }

    const FORMATTED = '@OData.Community.Display.V1.FormattedValue';
    const seen = new Set<string>();
    const byId = new Map<string, { role: string; fullName: string }>();
    for (const field of CANDIDATE_ROLE_FIELDS) {
      const contactId = record[`_${field.attr}_value`] as string | undefined;
      if (contactId && !seen.has(contactId)) {
        seen.add(contactId);
        byId.set(contactId, {
          role: field.role,
          fullName: (record[`_${field.attr}_value${FORMATTED}`] as string) ?? '(no name)',
        });
      }
    }
    if (byId.size === 0) return [];

    const emailById = await this.fetchContactEmails([...byId.keys()]);
    return [...byId.entries()].map(([contactId, meta]) => ({
      contactId,
      fullName: meta.fullName,
      email: emailById.get(contactId),
      role: meta.role,
    }));
  };

  /** Batch-fetches emails for a set of contacts (host-context). Email is best-effort — it drives the
   * modal's external-vs-internal grant routing but a miss simply routes as grant-only. */
  private fetchContactEmails = async (contactIds: string[]): Promise<Map<string, string>> => {
    const map = new Map<string, string>();
    if (contactIds.length === 0) return map;
    const filter = contactIds.map(id => `contactid eq ${id}`).join(' or ');
    try {
      const res = await this.context.webAPI.retrieveMultipleRecords(
        'contact',
        `?$select=contactid,emailaddress1&$filter=${filter}`
      );
      for (const e of res.entities) {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        const row = e as any;
        if (row.contactid && row.emailaddress1) map.set(row.contactid as string, row.emailaddress1 as string);
      }
    } catch {
      /* email is best-effort — routing falls back to grant-only when unknown */
    }
    return map;
  };

  /** Reads the current record's active `sprk_externalrecordaccess` grants
   * (host-context, single-entity, one `$expand` read). */
  private fetchExistingGrants = async (): Promise<IAccessGrantRecord[]> => {
    const recordId = this.getRecordId();
    if (!recordId) return [];

    // Filter by the bound root's lookup value field (task 070/071 polymorphic
    // read) — e.g. `_sprk_matter_value` on a Matter form. Replaces the R1
    // `_sprk_projectid_value` (invalid field name → matched zero rows).
    // task 073 UAT fix: read the contact + grantedby by their FK VALUE fields + FormattedValue
    // annotations (the contact/systemuser display names) instead of `$expand=sprk_contactid,sprk_grantedby`
    // — those lowercase names are NOT the navigation properties (Dataverse 400 "Could not find a property
    // named 'sprk_contactid'"; the real nav props are PascalCase per task 070). The `_X_value` form is
    // stable and needs no nav-property-casing.
    const FORMATTED = '@OData.Community.Display.V1.FormattedValue';
    const rootValueField = this.resolveGrantRoot().rootValueField;
    const options =
      `?$filter=${rootValueField} eq ${recordId} and statecode eq 0` +
      // v1.0.35 (task 140): + _sprk_grantedbycontact_value — the CONTACT who issued the grant from the external SPA.
      // ⚠️ Deploy order: the column (scripts/Deploy-ExternalRecordAccessContactGrantor.ps1) must exist first, or this
      // read 400s and Current Access shows nothing.
      `&$select=_sprk_contact_value,_sprk_organization_value,_sprk_grantedby_value,_sprk_grantedbycontact_value,sprk_accesslevel,sprk_granteddate`;

    let result: ComponentFramework.WebApi.RetrieveMultipleResponse;
    try {
      result = await this.context.webAPI.retrieveMultipleRecords(EXTERNAL_ACCESS_ENTITY, options);
    } catch {
      return [];
    }

    return result.entities.map(e => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const row = e as any;
      const contactId = (row['_sprk_contact_value'] as string) ?? '';
      const organizationId = (row['_sprk_organization_value'] as string) ?? '';
      // An ORGANIZATION grant (task 073 #7) is a row with NO contact + an organization set — it grants
      // access to all contacts at that organization, so it renders as "All contacts at {org}" (revocable)
      // instead of a person. A normal per-contact grant keeps the contact's name.
      const isOrgGrant = !contactId && !!organizationId;
      const orgName = (row[`_sprk_organization_value${FORMATTED}`] as string) ?? 'this organization';
      return {
        accessRecordId: row.sprk_externalrecordaccessid as string,
        // Org rows have no contact — use the org id so the row still has a stable, non-empty key.
        contactId: isOrgGrant ? organizationId : contactId,
        fullName: isOrgGrant
          ? `All contacts at ${orgName}`
          : ((row[`_sprk_contact_value${FORMATTED}`] as string) ?? '(unknown contact)'),
        email: undefined,
        accessLevel: row.sprk_accesslevel as number,
        grantedByName: (row[`_sprk_grantedby_value${FORMATTED}`] as string) ?? undefined,
        grantedByContactName: (row[`_sprk_grantedbycontact_value${FORMATTED}`] as string) ?? undefined,
        grantedDate: row.sprk_granteddate ?? undefined,
        provenance: isOrgGrant ? ('organization' as const) : undefined,
      };
    });
  };

  /** Named-contact person-picker search (host-context, single-entity, capped
   * at 10 results). */
  private searchContacts = async (query: string): Promise<IContactSearchResult[]> => {
    const escaped = query.replace(/'/g, "''");
    const options =
      `?$filter=contains(fullname,'${escaped}') or contains(emailaddress1,'${escaped}')` +
      `&$select=fullname,emailaddress1&$top=10`;
    const result = await this.context.webAPI.retrieveMultipleRecords('contact', options);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return result.entities.map((e: any) => ({
      contactId: e.contactid as string,
      fullName: e.fullname as string,
      email: e.emailaddress1 as string | undefined,
    }));
  };

  /** In-app `sprk_organization` search for the modal's inline org LookupField
   * (task 073 UAT #4) — host-context, single-entity, capped at 10 — returns
   * `{ id, name }` items. Replaces the side-pane org Advanced Lookup so the
   * modal never has to hide to pick a firm/org. */
  private searchOrganizations = async (query: string): Promise<ILookupItem[]> => {
    const escaped = query.replace(/'/g, "''");
    const options =
      `?$filter=contains(sprk_organizationname,'${escaped}')` +
      `&$select=sprk_organizationid,sprk_organizationname&$top=10`;
    const result = await this.context.webAPI.retrieveMultipleRecords('sprk_organization', options);
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return result.entities.map((e: any) => ({
      id: e.sprk_organizationid as string,
      name: (e.sprk_organizationname as string) ?? '(no name)',
    }));
  };

  /** Opens the SHARED side-pane Advanced Lookup for a single Contact (task 071 —
   * INavigationService.openLookup → Xrm.Utility.lookupObjects) and enriches the
   * pick with the contact's email via a host-context read, so the modal's
   * external-vs-internal grant routing (which keys off email) stays correct.
   * Returns `null` when the user cancels. The GUID is already `cleanGuid`-
   * normalized by the adapter, so it is safe to send in an `@odata.bind`. */
  private pickContact = async (): Promise<IContactSearchResult | null> => {
    const results = await this.getNavService().openLookup({
      entityType: 'contact',
      entityTypes: ['contact'],
      allowMultiSelect: false,
    });
    const picked = results[0];
    if (!picked) return null;
    try {
      const rec = (await this.context.webAPI.retrieveRecord(
        'contact',
        picked.id,
        '?$select=fullname,emailaddress1'
      )) as unknown as { fullname?: string; emailaddress1?: string };
      return {
        contactId: picked.id,
        fullName: rec?.fullname ?? picked.name,
        email: rec?.emailaddress1 ?? undefined,
      };
    } catch {
      // Email enrichment failed — still return the pick (routes as grant-only).
      return { contactId: picked.id, fullName: picked.name };
    }
  };

  /** Opens the SHARED side-pane Advanced Lookup for a single `sprk_organization`
   * (task 071) — the optional grantee firm/org sent to the BFF as
   * `organizationId` (the grant's `sprk_Organization` firm-scoping lookup,
   * task 070). Returns `null` when the user cancels. */
  private pickOrganization = async (): Promise<IOrganizationPick | null> => {
    const results = await this.getNavService().openLookup({
      entityType: 'sprk_organization',
      entityTypes: ['sprk_organization'],
      allowMultiSelect: false,
    });
    const picked = results[0];
    return picked ? { id: picked.id, name: picked.name } : null;
  };

  /** Opens the SHARED side-pane Advanced Lookup for a single `systemuser`
   * (task 065, FR-29) — the "+ User" internal system-user share picker,
   * mirroring {@link pickContact}/{@link pickOrganization} exactly.
   * `INavigationService.openLookup` (task 071) wraps `Xrm.Utility.lookupObjects`
   * generically by `entityType`/`entityTypes` with no per-entity allow-list, so
   * `systemuser` works with no adapter change — verified against
   * `xrmNavigationServiceAdapter.ts`'s `openLookup`, which passes
   * `entityTypes` straight through. Returns `null` when the user cancels.
   *
   * Task 114 (owner test feedback 2026-10-07): with `excludeExternal` (a Restricted record) the lookup leaves out
   * users flagged `sprk_isexternal = true` — blank counts as internal, hence the `null` branch (FetchXML `ne` drops
   * nulls). The lookup's "recent records" list may not apply the filter, so `/share-user` still refuses such a user
   * and the modal names them. The pick is enriched with the user's email, like {@link pickContact}. */
  private pickUser = async (options?: IUserPickOptions): Promise<IUserPick | null> => {
    const results = await this.getNavService().openLookup({
      entityType: 'systemuser',
      entityTypes: ['systemuser'],
      allowMultiSelect: false,
      filters: options?.excludeExternal
        ? [
            {
              entityLogicalName: 'systemuser',
              filterXml:
                '<filter type="or"><condition attribute="sprk_isexternal" operator="ne" value="1" />' +
                '<condition attribute="sprk_isexternal" operator="null" /></filter>',
            },
          ]
        : undefined,
    });
    const picked = results[0];
    if (!picked) return null;
    try {
      const rec = (await this.context.webAPI.retrieveRecord(
        'systemuser',
        picked.id,
        '?$select=fullname,internalemailaddress'
      )) as unknown as { fullname?: string; internalemailaddress?: string };
      return {
        id: picked.id,
        name: rec?.fullname ?? picked.name,
        email: rec?.internalemailaddress ?? undefined,
      };
    } catch {
      // Email enrichment failed — still return the pick, named as the lookup named it.
      return { id: picked.id, name: picked.name };
    }
  };

  /** Task 067: which of the given contacts hold an ACTIVE membership in which of the given (walled) organizations —
   * `sprk_contactorganization`, bounded by its own state only (`statecode` active or blank, no dates), the same predicate
   * the server's wall uses (`ExternalParticipationService.WallMembershipStateClause`). The modal calls this only when an
   * organization wall is in force on the record, and marks those contacts' Current Access rows walled off. Errors
   * propagate: the modal then says the rows could not be checked, never that they are not walled. Contacts are asked in
   * chunks so a long Current Access list cannot exceed the URL limit. */
  private fetchContactOrganizationMemberships = async (
    contactIds: string[],
    organizationIds: string[]
  ): Promise<IContactOrganizationMembership[]> => {
    if (contactIds.length === 0 || organizationIds.length === 0) return [];
    // Only canonical GUIDs enter the OData filter. Anything else is refused (the modal then says the rows could not be
    // checked) rather than dropped, so a bad id can neither alter the query nor silently unmark a row.
    const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
    const contacts = contactIds.map(cleanGuid);
    const organizations = organizationIds.map(cleanGuid);
    if (![...contacts, ...organizations].every(id => GUID.test(id))) {
      throw new Error('fetchContactOrganizationMemberships: an id is not a GUID');
    }
    const orgClause = organizations.map(id => `_sprk_organization_value eq ${id}`).join(' or ');
    const memberships: IContactOrganizationMembership[] = [];
    const CHUNK = 40;
    for (let i = 0; i < contacts.length; i += CHUNK) {
      const contactClause = contacts
        .slice(i, i + CHUNK)
        .map(id => `_sprk_contact_value eq ${id}`)
        .join(' or ');
      const result = await this.context.webAPI.retrieveMultipleRecords(
        'sprk_contactorganization',
        `?$select=_sprk_contact_value,_sprk_organization_value` +
          `&$filter=(${contactClause}) and (${orgClause}) and (statecode eq 0 or statecode eq null)`
      );
      for (const e of result.entities) {
        const row = e as unknown as { _sprk_contact_value?: string; _sprk_organization_value?: string };
        if (row._sprk_contact_value && row._sprk_organization_value) {
          memberships.push({ contactId: row._sprk_contact_value, organizationId: row._sprk_organization_value });
        }
      }
    }
    return memberships;
  };

  /** Reads the bound record's secure-project owner + business-unit alignment
   * (task 065, design.md §6) for the modal's read-only display. Host-context,
   * single-entity read of `ownerid`/`owningbusinessunit`/`sprk_issecure` —
   * the SAME three standard Dataverse columns on all three grant-root
   * entities (project/matter/workassignment; verified against task 046's
   * live metadata pass, `notes/task-046-secure-project-owner-role.md` §7b).
   * Returns `null` for a non-secure record (`sprk_issecure` is not `true`)
   * or when the read fails — this is a read-only convenience display, never
   * a blocking concern, so it fails soft rather than surfacing an error. Per
   * design §5.1a's correction, `ownerid` is whatever Dataverse's polymorphic
   * Owner field resolves to (the `Secure Project` OWNER TEAM today, not a
   * service account) — this method reads the display name generically and
   * does not assume which principal kind it is. */
  private fetchSecureOwnerInfo = async (): Promise<ISecureOwnerInfo | null> => {
    const recordId = this.getRecordId();
    if (!recordId) return null;
    const FORMATTED = '@OData.Community.Display.V1.FormattedValue';
    try {
      const record = (await this.context.webAPI.retrieveRecord(
        this.getHostEntity(),
        recordId,
        '?$select=sprk_issecure,_ownerid_value,_owningbusinessunit_value'
      )) as unknown as Record<string, unknown>;
      if (record['sprk_issecure'] !== true) return null;
      return {
        ownerName: (record[`_ownerid_value${FORMATTED}`] as string) ?? '(unknown owner)',
        businessUnitName: (record[`_owningbusinessunit_value${FORMATTED}`] as string) ?? '(unknown business unit)',
      };
    } catch {
      return null;
    }
  };

  /** Classifies a contact internal-workforce (has a linked `systemuser` via
   * `sprk_primarycontact`) vs external — drives `AccessGrantModal`'s
   * invite-and-grant vs grant-only routing decision. */
  private isInternalContact = async (contactId: string): Promise<boolean> => {
    const options = `?$filter=_sprk_primarycontact_value eq ${contactId}&$select=systemuserid&$top=1`;
    const result = await this.context.webAPI.retrieveMultipleRecords('systemuser', options);
    return result.entities.length > 0;
  };

  /** Reads the record's STANDING-grant members (task 073 UAT #2): the record's
   * role-member candidates (`fetchCandidates()`) whose global
   * `contact.sprk_standinggrant` flag is set. The intersection with THIS
   * record's role-members mirrors the server-side union in
   * `AccessibleRecordSetService.ComposeForContactAsync` — a standing contact
   * with no access-conferring role on this record confers no access TO it, so
   * it is intentionally NOT listed here (Eyal Iffergan shows only if he holds a
   * `sprk_assigned*` role on the bound record). Returned rows carry
   * `provenance: 'standing'` and NO `accessRecordId` (there is no per-record
   * `sprk_externalrecordaccess` row to revoke) — the modal renders them
   * non-revocable. `sprk_standinggrant` is field-level-secured: a signed-in user
   * without FLS read on it silently gets an empty list (the query returns no
   * matches), same fail-soft as the server-side reader. */
  private fetchStandingContacts = async (): Promise<IAccessGrantRecord[]> => {
    const candidates = await this.fetchCandidates();
    if (candidates.length === 0) return [];
    const filter = candidates.map(c => `contactid eq ${c.contactId}`).join(' or ');
    let result: ComponentFramework.WebApi.RetrieveMultipleResponse;
    try {
      result = await this.context.webAPI.retrieveMultipleRecords(
        'contact',
        `?$select=contactid&$filter=sprk_standinggrant eq true and (${filter})`
      );
    } catch {
      // FLS denial or query error — standing rows are additive; fail soft so the
      // rest of the Current Access list still loads.
      return [];
    }
    const standingIds = new Set<string>();
    for (const e of result.entities) {
      const row = e as unknown as { contactid?: string };
      if (row.contactid) standingIds.add(row.contactid);
    }
    return candidates
      .filter(c => standingIds.has(c.contactId))
      .map(c => ({
        // No accessRecordId — a standing grant has no per-record row to revoke.
        contactId: c.contactId,
        fullName: c.fullName,
        email: c.email,
        // Placeholder — standing rows render a "Standing" badge, not an
        // access level (the effective level is role-derived server-side). Value
        // is unused by the standing-row render path; 100000000 = ViewOnly.
        accessLevel: 100000000,
        provenance: 'standing' as const,
      }));
  };

  // =========================================================================
  // Email-members wiring (task 042, teams-app-r1). Reuses `fetchCandidates()`
  // above verbatim — the SAME allowlist-filtered `sprk_assigned*` membership-
  // contact data source the grant modal (task 041) uses — per this task's
  // "MUST NOT invent a separate recipient-derivation rule" constraint.
  // =========================================================================

  /** Resolves the email-members recipient list: the current record's
   * membership contacts, deduplicated by email, dropping any candidate with
   * no populated email address (it cannot be pre-filled as a To recipient).
   * An empty result means "no emailable membership contacts" — the caller
   * (the click handler below) shows an empty state instead of opening
   * `SendEmailDialog` with zero recipients. */
  private resolveEmailMembersRecipients = async (): Promise<string[]> => {
    const candidates = await this.fetchCandidates();
    const emails = new Set<string>();
    for (const candidate of candidates) {
      if (candidate.email && candidate.email.trim().length > 0) {
        emails.add(candidate.email.trim());
      }
    }
    return Array.from(emails);
  };

  private closeSendEmailDialog = (): void => {
    this.isSendEmailDialogOpen = false;
    this.renderControl();
  };

  private closeEmailEmptyState = (): void => {
    this.isEmailEmptyStateOpen = false;
    this.renderControl();
  };

  private renderControl(): void {
    // `!== false` treats unset / null / undefined as "not explicitly false"
    // so the manifest default (showTitle=true) wins. Same pattern as
    // VisualHost's showToolbar/showVersion reads.
    const showTitle = this.context.parameters.showTitle?.raw !== false;
    const showVersion = this.context.parameters.showVersion?.raw === true;
    // Task 138: honour a read-only form. Re-read on every render (init and every updateView), so a form
    // that becomes read-only — or a column that becomes non-editable — takes effect immediately.
    const controlDisabled = this.context.mode?.isControlDisabled === true;
    const accessPermissionBound = this.isAccessPermissionBound();

    const props: ITrackingFieldTrioProps = {
      monitor: this.monitorValue,
      highPriority: this.highPriorityValue,
      accessPermission: this.accessPermissionValue,
      // Optional control header title (task 073 UAT #3) — when set, the shared
      // core renders a 32px header row (title left, grant/email icons right).
      title: (this.context.parameters.title?.raw as string) || undefined,
      showTitle,
      showVersion,
      versionText: 'v1.0.42 • Built 2026-10-08',
      accessPermissionOptions: this.getAccessPermissionOptions(),
      // Labels pulled from each bound field's Dataverse metadata so they
      // reflect the actual field display name (localizable, and stays in
      // sync if the field is renamed).
      monitorLabel: this.getFieldLabel(this.context.parameters.monitor, 'Monitor'),
      highPriorityLabel: this.getFieldLabel(this.context.parameters.highPriority, 'High Priority'),
      accessPermissionLabel: this.getFieldLabel(this.context.parameters.accessPermission, 'Access Permission'),
      onMonitorChange: v => {
        this.monitorValue = v;
        this.notifyOutputChanged();
      },
      onHighPriorityChange: v => {
        this.highPriorityValue = v;
        this.notifyOutputChanged();
      },
      onAccessPermissionChange: v => {
        this.accessPermissionValue = v;
        this.notifyOutputChanged();
      },
      // Governance toolbar — person icon opens the real access-grant modal
      // (task 041); email icon opens the canonical SendEmailDialog (task 042).
      // Task 153: the access-status indicator passes 'noAccess' to open at the No Access List; the person icon passes
      // nothing (the top).
      onOpenGrantModal: (section?: GrantModalSection) => {
        this.grantModalSection = section;
        this.isGrantModalOpen = true;
        this.renderControl();
      },
      // Email icon (task 042) — resolves the record's membership contacts
      // (reusing fetchCandidates(), same as the grant modal) then either
      // opens the canonical SendEmailDialog pre-populated with those emails,
      // or — if none are emailable — shows the empty-state alert instead of
      // opening a dialog with zero recipients.
      onOpenEmailMembers: () => {
        void (async () => {
          const recipients = await this.resolveEmailMembersRecipients();
          if (recipients.length === 0) {
            this.isEmailEmptyStateOpen = true;
          } else {
            this.emailRecipients = recipients;
            this.isSendEmailDialogOpen = true;
          }
          this.renderControl();
        })();
      },
      // The SERVER's answer to "may this caller change who can access this record" (task 118) — see
      // evaluateGrantGate(). `false` until the server says otherwise, including while the answer is in
      // flight and on every failure to obtain one.
      canGrantAccess: this.canGrantAccessValue,
      // Task 138 — read-only form / non-editable column / unbound column / secure display (O1 FINAL).
      disabled: controlDisabled,
      accessPermissionDisabled: !this.isAccessPermissionEditable(),
      showAccessPermission: accessPermissionBound,
      secureAccessPermission: this.isSecureValue === true ? { label: SECURE_PILL_LABEL } : undefined,
      // Task 153: the access-status indicator (undefined while unasked or in flight → nothing drawn).
      accessStatus: this.accessStatusValue,
    };

    const recordId = this.getRecordId();

    // React 16 API per ADR-022 - use ReactDOM.render, NOT createRoot
    ReactDOM.render(
      React.createElement(
        FluentProvider,
        // No PCF context: in a STANDARD control `fluentDesignLanguage.isDarkTheme` reads false in Spaarke dark mode
        // (owner test 2026-10-07), so the theme comes from the user's choice, the dark-mode URL flag, then the navbar.
        { theme: resolveThemeWithUserPreference(), style: { width: '100%' } },
        React.createElement(
          React.Fragment,
          null,
          React.createElement(SharedTrackingFieldTrio, props),
          // task 073 UAT #1 — wrap the dialog subtree in the shared
          // WidgetErrorBoundary so a render error inside a shared dialog (e.g.
          // the EmailComposer engine) degrades to a small inline card instead of
          // blanking the entire control. Defense-in-depth: the root cause (a
          // duplicate React 19 bundled via Lexical's react/jsx-runtime subpath)
          // is fixed at the build layer in ../webpack.config.js.
          //
          // no-children-prop is intentionally suppressed here: WidgetErrorBoundaryProps
          // types `children` as REQUIRED, which the createElement rest-args overload does
          // not satisfy (verified: TS2769). Casting the props to bypass the requirement
          // would erase type-checking, so the fully-typed `children` prop is the type-safe
          // form. See the inline note on the `children:` line below.
          // eslint-disable-next-line react/no-children-prop
          React.createElement(WidgetErrorBoundary, {
            widgetType: 'tracking-field-trio-dialogs',
            displayName: 'Access & Email',
            surface: 'TrackingFieldTrio',
            // Children via the `children` prop (not createElement rest-args):
            // WidgetErrorBoundaryProps types `children` as required, which the
            // rest-args overload of React.createElement does not satisfy (same
            // reason as the empty-state Dialog below). React.Fragment DOES
            // accept rest-args, so wrap the dialog subtree in one.
            children: React.createElement(
              React.Fragment,
              null,
              // Access-grant modal (task 041) — always mounted so AccessGrantModal's
              // own `open`-driven effect controls data loading; `recordId` is only
              // resolvable once the control is bound to a real record (harness
              // environments render the toolbar but the modal has nothing to open).
              recordId
                ? React.createElement(AccessGrantModal, {
                    open: this.isGrantModalOpen,
                    onClose: () => {
                      this.isGrantModalOpen = false;
                      this.grantModalSection = undefined;
                      this.renderControl();
                    },
                    // Task 153: open at the No Access List when the access-status indicator asked for it.
                    initialSection: this.grantModalSection,
                    recordId,
                    // Polymorphic root type derived from the bound host entity
                    // (task 071) — the modal sends {recordType, recordId}.
                    recordType: this.resolveGrantRoot().recordType,
                    canGrantAccess: this.canGrantAccessValue,
                    authenticatedFetch: this.authenticatedFetchGated,
                    fetchCandidates: this.fetchCandidates,
                    fetchExistingGrants: this.fetchExistingGrants,
                    // Standing-grant members (task 073 UAT #2) — role-members whose
                    // global sprk_standinggrant flag is set, merged into Current Access.
                    fetchStandingContacts: this.fetchStandingContacts,
                    searchContacts: this.searchContacts,
                    searchOrganizations: this.searchOrganizations,
                    // NATIVE advanced-lookup pickers (task 073 UAT v1.0.24 #1/#4) —
                    // "+ Contact"/"+ Organization" open Xrm.Utility.lookupObjects (the
                    // same advanced-find surface the wizards use). The modal is
                    // nonBlocking so the lookup pane isn't covered by a backdrop.
                    pickContact: this.pickContact,
                    pickOrganization: this.pickOrganization,
                    // "+ User" native picker (task 065, FR-29) — mirrors pickContact/pickOrganization.
                    pickUser: this.pickUser,
                    // Contact-name link → open the Contact record (task 073 UAT v1.0.24 #6).
                    onOpenContact: this.openContactRecord,
                    isInternalContact: this.isInternalContact,
                    // Access-Permission sharing gate (task 043, FR-14 Option A; task 138) —
                    // the bound field's raw value folded with the record's Secure flag,
                    // failing closed; see mapAccessPermissionToState()'s doc comment.
                    accessPermissionState: this.mapAccessPermissionToState(this.accessPermissionValue),
                    // Banner copy only ("Secure" / "Secure – Restricted", owner O1 FINAL).
                    isSecureRecord: this.isSecureValue === true,
                    // Secure-record owner/BU read-only display (task 065, design.md §6).
                    fetchSecureOwnerInfo: this.fetchSecureOwnerInfo,
                    // Task 067: contacts in a walled organization are marked walled off in Current Access.
                    fetchContactOrganizationMemberships: this.fetchContactOrganizationMemberships,
                  })
                : null,
              // Canonical SendEmailDialog (task 042) — pre-populated with the
              // record's membership-contact emails (resolveEmailMembersRecipients()
              // above). Send flows through the engine's OWN sendCommunication()
              // call (ADR-045) — no custom send logic here. Gated on `recordId`
              // for the same reason as the grant modal above.
              recordId
                ? React.createElement(SendEmailDialog, {
                    open: this.isSendEmailDialogOpen,
                    onClose: this.closeSendEmailDialog,
                    initialTo: this.emailRecipients,
                    authenticatedFetch: this.authenticatedFetchGated,
                    bffBaseUrl: this.apiBaseUrl,
                    titleOverride: 'Email Members',
                    // Non-blocking so the native To/Cc people picker + "link a record"
                    // advanced-lookup panes render ON TOP instead of behind the modal
                    // backdrop (owner UAT v1.0.26 — same fix as Manage Access).
                    nonBlocking: true,
                    // "Related to" chip shows the record's number/name, not the bare
                    // entity type (task 073 UAT v1.0.24 #9).
                    regarding: {
                      entityType: this.getHostEntity(),
                      id: recordId,
                      name: this.getRecordDisplayName(),
                    },
                    // To/Cc native people picker (#10) + local-file → SPE upload so
                    // attachments ride the send payload instead of being dropped (#11).
                    onLookupRecipients: this.getEmailHandlers().onLookupRecipients,
                    onUploadLocalAttachment: this.getEmailHandlers().onUploadLocalAttachment,
                    recordLookupCatalog: this.getEmailHandlers().recordLookupCatalog,
                    onLookupRecord: this.getEmailHandlers().onLookupRecord,
                    onAddRelationship: this.getEmailHandlers().onAddRelationship,
                    onResolveShareLink: this.getEmailHandlers().onResolveShareLink,
                    onSent: () => {
                      this.isSendEmailDialogOpen = false;
                      this.renderControl();
                    },
                    onError: (err: Error) => {
                      console.error('[TrackingFieldTrio] Email-members send failed.', err);
                    },
                  })
                : null,
              // Empty-state alert (task 042) — shown INSTEAD of SendEmailDialog
              // when the record has no membership contacts with a populated
              // email, so the dialog never opens with zero recipients.
              //
              // no-children-prop is intentionally suppressed here: Fluent v9 `DialogProps`
              // types `children` as REQUIRED, which the createElement rest-args overload
              // does not satisfy (verified: TS2769) and casting to bypass it would erase
              // type-checking of the props, so the fully-typed `children` prop is retained.
              // eslint-disable-next-line react/no-children-prop
              React.createElement(Dialog, {
                open: this.isEmailEmptyStateOpen,
                onOpenChange: (_event: unknown, data: { open: boolean }) => {
                  if (!data.open) this.closeEmailEmptyState();
                },
                // Passed via the `children` prop (not createElement rest-args) —
                // Fluent v9's `Dialog` types `children` as required on `DialogProps`,
                // which the rest-args overload of `React.createElement` does not
                // satisfy.
                children: React.createElement(
                  DialogSurface,
                  null,
                  React.createElement(
                    DialogBody,
                    null,
                    React.createElement(DialogTitle, null, 'Email members'),
                    React.createElement(
                      DialogContent,
                      null,
                      'This record has no membership contacts with an email address yet. Grant access or assign a role first.'
                    ),
                    React.createElement(
                      DialogActions,
                      null,
                      React.createElement(Button, { appearance: 'primary', onClick: this.closeEmailEmptyState }, 'OK')
                    )
                  )
                ),
              })
            ),
          })
        )
      ),
      this.container
    );
  }

  public getOutputs(): IOutputs {
    return {
      monitor: this.monitorValue,
      highPriority: this.highPriorityValue,
      // Task 138: an unbound (optional) property is never written back.
      accessPermission: this.isAccessPermissionBound() ? (this.accessPermissionValue ?? undefined) : undefined,
    };
  }

  public destroy(): void {
    this.themeListenerCleanup?.();
    // React 16 API per ADR-022 - use unmountComponentAtNode, NOT root.unmount()
    ReactDOM.unmountComponentAtNode(this.container);
  }
}
