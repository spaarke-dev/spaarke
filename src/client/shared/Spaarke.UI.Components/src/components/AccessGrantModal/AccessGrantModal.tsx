/**
 * AccessGrantModal — the person-icon access-grant modal (teams-app-r1 task 041).
 *
 * Opened from `TrackingFieldTrio`'s `onOpenGrantModal` callback (task 040). Per
 * `docs/standards/MODAL-DECISION-CRITERIA.md` this is a **Family 2** modal
 * (proprietary Fluent v9 dialog — a picker/approve UX, not a full-form edit and
 * not a browse-in-context collection). Per `docs/standards/MODAL-DESIGN-SYSTEM.md`
 * it is built directly on the **`SprkModal` base shell** (NOT one of the six
 * presets): none of `ConfirmModal`/`ChoiceModal`/`FormModal`/`PreviewModal`/
 * `BrowseModal`/`WizardShell` fit — this modal has THREE independent sections
 * (candidate-approve list, named-contact picker, existing-grants+revoke list)
 * each with its OWN per-row action, not a single primary Save/Submit — so a thin
 * `SprkModal` config (size `lg`, `dismiss="explicit"`, a single footer "Close")
 * is the correct fit per the doc's explicit scope note: "does not require
 * adopting every preset ... if a simpler ... preset fits" — here, none of the
 * six presets fit and the base shell itself is the right level.
 *
 * Write path (per the task's binding constraint — MUST reuse the built
 * `sprk_externalrecordaccess` write path, MUST NOT write directly to the table):
 *   - Every contact → `POST /api/v1/external-access/grant` (task 193, #2338: one
 *     "Add person" entry). The modal never classifies a contact; the SERVER
 *     decides. A contact who represents exactly one internal colleague is SHARED
 *     with that user (the answer carries `sharedWithUser` and no grant row is
 *     written; a share has no end date, and `expiryNotApplied` says when the
 *     add's Expiration was not applied). Anyone else is granted, and an unbound
 *     contact with an email is invited by the same call (task 185 slice E).
 *   - Revoke (both) → `POST /api/v1/external-access/revoke` (the built endpoint).
 *
 * INTERNAL NOTIFICATION (unified-access-control-r2 task 181, owner round 89
 * item 3 — replaces teams-app-r1 task 041's "notify pending" notice).
 * The SERVER tells an internal user they were given access: `/grant` when the
 * contact represents an internal user, and `/share-user`, each after its write
 * succeeded and only when it gave access the person did not hold. It is an
 * in-app notification (the model-driven app's bell) linking to the record. It
 * is best effort: a notification that could not be sent never undoes the
 * grant; the route answers `notificationFailed: true` (on `/share-user`'s
 * related-records-pending 500 too), and this modal adds "Some people could not
 * be notified; share the record link with them." to the batch notice. The
 * modal sends nothing itself.
 *
 * ACCESS-PERMISSION SHARING GATE (task 043, spec FR-14 Option A; made real by
 * unified-access-control-r2 task 138, owner round 2 item 3). The record-level
 * Access-Permission state — `'restricted'` / `'limited'` / `'standard'` (see
 * {@link AccessPermissionState} in `types.ts`; a SECURE record arrives as
 * `'limited'`, or `'restricted'` when it is also Restricted) — governs WHICH
 * grant types this modal OFFERS. The server enforces the same rule at write
 * time (`ExternalGrantLifecycle.DecideGrantPolicy`), so this is the matching
 * affordance, not the enforcement:
 *   - Restricted: "+ Organization" and the role-based candidate (contact) rows
 *     are NOT rendered, and "Add person" looks up users only (none flagged
 *     external) — no contact-based access is possible. User shares, user rows,
 *     their level dropdowns and Add stay ENABLED: Restricted never limits
 *     internal access. Revoke of existing access stays available.
 *   - Limited (and Secure): "+ Organization" is not rendered — contacts get
 *     access only through grants made to them by name. "Add person" (contacts
 *     and users) and the candidates stay enabled.
 *   - Standard (the default when the prop is omitted): every option, task
 *     041's baseline.
 * Each non-standard state shows ONE explanatory MessageBar ("Restricted
 * Access", "Secure – Restricted", "Secure", "Limited Access" — owner O1 FINAL,
 * 2026-10-01). There is NO standing-grant control in this modal (the standing
 * grant is set on the Contact record itself, task 073 UAT v1.0.24 #5), so
 * there is nothing standing-related to hide; Current Access rows the state
 * cancels (standing and organization rows on Limited/Secure, every
 * contact-based row on Restricted) are marked "No effect" (task 067, which
 * absorbed task 066). When the server refuses a grant anyway
 * (a stale dialog, a record changed meanwhile), the notice shows the server's
 * own `detail` text. This gate is STRUCTURALLY independent of the per-grant
 * `sprk_accesslevel` (`accessLevelOptions` / `defaultAccessLevel`): it only
 * touches which grantee kinds are offered, never the level a grant carries.
 *
 * INTERNAL SYSTEM-USER SHARES (task 065, unified-access-control-r2, spec
 * FR-29). A FOURTH write path alongside the three above: picking a
 * `systemuser` with "Add person" (native advanced lookup over contacts and
 * users, {@link IAccessGrantModalProps.pickPerson}; task 193)
 * stages it into the SAME "Add Access Permissions" list (its own per-row level
 * dropdown), and `Add (N)` commits it via `POST
 * /api/v1/external-access/share-user` — a Dataverse POA share, NOT a
 * `sprk_externalrecordaccess` row, and NOT the `/grant` core the contact/org
 * flows use (task 063 built a dedicated share/unshare/list surface exactly
 * because a POA share is a different write). Existing shares are read via
 * `GET /api/v1/external-access/user-shares` (called directly by this modal,
 * not host-injected — the endpoint is already entity-agnostic given
 * `{recordType, recordId}`, same as grant/revoke) and merged into Current
 * Access with `provenance: 'share'`; revoke uses `POST
 * /api/v1/external-access/unshare-user`, not `/revoke` (a share carries no
 * `accessRecordId`).
 *
 * DELEGATION GATE (task 008 FR-07 / task 063 — HARD PREREQUISITE for this
 * button, design.md §6): every route this modal calls under
 * `/api/v1/external-access/*` — including the three pre-existing ones — is
 * now behind `DelegationRuleFilter` (group-level on `ExternalAccessEndpoints.
 * MapInternalManagementEndpoints`), which requires the CALLER (OBO, not
 * app-only) to hold Write on the target record. This modal MUST NOT try to
 * PREDICT that outcome client-side — no privilege pre-check, no re-derivation
 * of the rule from Dataverse privileges. Server truth only.
 *
 * Task 118 sharpened that rule rather than relaxing it: the host may now ASK
 * the server the same question ahead of time (`GET /api/v1/external-access/
 * can-manage-access`, whose 200/403 IS this filter's verdict) and pass the
 * answer down as `canGrantAccess`. Asking is server truth; guessing from a
 * table-level `hasEntityPrivilege` — which is what the host did until
 * v1.0.31 — was not. A 403 with a `sdap.access.deny.delegation_*`
 * reason code is rendered as a persistent, dismissable-only-by-reopening
 * banner (`accessDenyState` below) that disables every write action (Add
 * person, +Organization, Add, Revoke) — never a toast, never a raw error.
 * A 401 (expired sign-in) gets its own distinct banner for the same reason.
 *
 * M8 / M2 (task-024 finding, transferred to this task 2026-09-09; owner
 * directive 2026-09-10). `postJson`/`getJson` below now check `res.ok` before
 * parsing the body as success (M8 — previously `(await res.json()) as T` read
 * a failed response's ProblemDetails as if it were the success shape). Revoke
 * renders one of three distinct outcomes: fully revoked, grant revoked but SPE
 * removal could not be confirmed (the person may retain file access — retry/
 * escalate), or nothing was revoked — built by {@link buildRevokeNotice}.
 *
 * M2 landed alongside M8 (binding order — see the task-024 constraint: flipping
 * `/revoke`'s status to 500 without M8's `res.ok` check would have traded one
 * silent wrong answer for a runtime throw). `RevokeExternalAccessEndpoint.cs`
 * now returns 500 + ProblemDetails (`sdap.revoke.incomplete.container_not_cleared`,
 * aligned with `/close-project`'s `ClosureIncomplete`) for the
 * `SpeContainerOutcome.Failed` case ONLY — `NotAttempted`/`PermissionRemoved`/
 * `NoPermissionFound` are unchanged 200s. `AccessGrantModalApiError` carries the
 * ProblemDetails' `deactivatedCount`/`speContainerOutcome` extensions (see
 * {@link AccessGrantModalApiError.fromResponse}), and `confirmRevoke`'s catch
 * block recognizes this ONE reason code and routes it through the SAME
 * {@link buildRevokeNotice} the 200 path uses — so a 500 still produces the
 * owner's three-outcome message, never a generic "please try again" that
 * silently drops `deactivatedCount`.
 *
 * NO ACCESS LIST, WALLED-OFF AND CANCELLED ROWS (unified-access-control-r2 task
 * 067, owner round 59 item 3; task 066 folded in). Read-only, never an
 * authoring surface: walls are written in No Access Entries (task 154), the
 * ONE place to author them.
 *   - The "No Access List" section lists the entries task 064's
 *     `GET /api/v1/records/{table}/{id}/no-access` returns to a Write holder,
 *     each with whether it is in force here and why not (server-decided). A
 *     `notShown` answer hides the section; any answer the modal cannot trust
 *     (non-200, unparseable, another record's, `unavailable`) is an error
 *     state inside the section, never "no entries".
 *   - A Current Access row whose subject an IN-FORCE entry walls off shows a
 *     "No Access" marker over its level (contact, user share, organization
 *     grant, or a contact belonging to a walled organization — resolved by the
 *     host's `fetchContactOrganizationMemberships`).
 *   - A row the record's own policy cancels (Restricted: every contact-based
 *     row; Limited/Secure: organization-wide and standing rows) shows a
 *     "No effect" marker. Veto and cancellation are different reasons and look
 *     different; a walled-off row shows the veto.
 * "No Access" is a veto, never a level (spec FR-23): it is offered by no level
 * dropdown in this modal. Rules: `noAccess.ts`.
 *
 * THE PARENT'S FLOOR (unified-access-control-r2 task 175; owner round 87,
 * refining round 84). A work assignment or project filed under a matter or
 * project inherits Secure and Access Permission from its parents (the most
 * restrictive across them and their chain) as a FLOOR: it can be made stricter
 * by hand, never looser. Grants and shares are unaffected, so every grant
 * affordance stays available on such a record. When the host passes its direct
 * parents (`followsParents`, from `can-manage-access`), the modal shows an info
 * bar naming the first parent (a link with `onOpenParent`) next to the usual
 * Access Permission bar, and, given `accessFloor` and
 * `recordAccessPermission`, whether each effective value is inherited or set
 * on this record. A write the server refuses with 409
 * `sdap.access.access_follows_parent` (it would make the record looser than its
 * parent) is shown as an inline refusal of that action, never as a modal-wide
 * state. A user share a secure parent passed on to the record (`inheritedFrom`
 * on `/user-shares`) is an `'inherited'` row: read-only on any record, and
 * marked for No Access exactly as a direct user share is. Rules:
 * `followsParent.ts`.
 */

import * as React from 'react';
import {
  Button,
  Checkbox,
  Option,
  Dropdown,
  Link,
  Spinner,
  Text,
  Tooltip,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  MessageBarTitle,
  Badge,
  Field,
  Input,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import { PersonRegular, PersonAccountsRegular, DismissCircleRegular, BuildingRegular } from '@fluentui/react-icons';
import { SprkModal } from '../SprkModal';
// Record picker (task 073 v1.0.24) — "Add person" / "+ Organization" open the host's
// NATIVE Dataverse advanced-lookup side pane (Xrm.Utility.lookupObjects, injected as
// pickPerson/pickOrganization) — the same advanced-find surface the wizards use. The
// modal renders `nonBlocking` so that page-level pane is not covered by a backdrop.
// "Add person" (task 193) looks up contacts and system users together.
import type {
  AccessPermissionState,
  IAccessGrantModalProps,
  IAccessGrantCandidate,
  IAccessGrantRecord,
  IContactSearchResult,
  IOrganizationPick,
  IUserPick,
  ISecureOwnerInfo,
  IRecordNoAccessEntry,
  IFollowsParent,
} from './types';
import { DEFAULT_ACCESS_LEVEL_OPTIONS } from './types';
import { isApiError, isAuthFailure, problemOf } from '../../utils/thrownFetchError';
import {
  ACCESS_FOLLOWS_PARENT_REASON_CODE,
  followsParentFallbackMessage,
  otherParentsSuffix,
  parentTypeLabel,
  describeInheritedShare,
  describeEffectiveAccess,
} from './followsParent';
import { cleanGuid } from '../../utils/guid';
import {
  buildNoAccessPath,
  buildVetoIndex,
  classifyCurrentAccessRow,
  contactIdsToCheck,
  describeCoverage,
  describeNotInForce,
  describeSubjectKind,
  describeInheritedFrom,
  effectiveAccessState,
  parseEffectiveAccess,
  parseNoAccessResponse,
  suppressionFor,
  vetoFor,
} from './noAccess';
import type { IEffectiveRecordAccess, IKnownDirectParent, NoAccessSectionState } from './noAccess';

const useStyles = makeStyles({
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalXL,
  },
  // Subsection headers — 20px semibold (task 073 UAT #5).
  sectionTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    color: tokens.colorNeutralForeground1,
  },
  // Subsection header row: title on the left, action buttons (Add person / + Organization) right-aligned.
  sectionHeaderRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
  },
  sectionHeaderActions: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },
  // Task 099c: the record-wide "Expiration" on the "Add Access Permissions" toolbar row (Fluent `Input type="date"`, the
  // library's date idiom — RecordHeader/fields/DateField.tsx).
  shareExpiryField: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
  },
  shareExpiryInput: {
    width: '150px',
  },
  shareExpiryHint: {
    display: 'block',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    marginTop: tokens.spacingVerticalXS,
  },
  // Padding below the "Add Access Permissions" header row, before the list
  // (task 073 UAT v1.0.24 #3).
  listArea: {
    display: 'flex',
    flexDirection: 'column',
    marginTop: tokens.spacingVerticalM,
  },
  // Contact name rendered as a link that opens the Contact record (v1.0.24 #6).
  contactLink: {
    fontSize: tokens.fontSizeBase300,
    cursor: 'pointer',
  },
  levelRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'end',
    columnGap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalS,
  },
  levelField: {
    minWidth: '180px',
  },
  sectionSubtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  row: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    minHeight: '28px',
    paddingBlock: tokens.spacingVerticalXS,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke3}`,
  },
  rowMain: {
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minWidth: 0,
  },
  rowName: {
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  rowMeta: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  rowActions: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    flexShrink: 0,
  },
  emptyState: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
    ...shorthands.padding(tokens.spacingVerticalS, 0),
  },
  // Per-row access-level dropdown (task 073 v1.0.23) — sized so "Pick access level" fits.
  rowLevelDropdown: {
    minWidth: '160px',
  },
  notAuthorized: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  loadingRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  // Secure-record owner/BU read-only row (task 065, design.md §6).
  secureOwnerRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
  // Task 067: the reason a Current Access row is walled off (No Access list) — the danger status colour.
  vetoReason: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorStatusDangerForeground1,
  },
  // Task 067 (066 folded in): the reason the record's own policy cancels a row — neutral and italic, so it never reads
  // as a wall.
  suppressedReason: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    fontStyle: 'italic',
  },
  // The level a walled-off row would carry, struck through: the wall overrides it.
  vetoedLevel: {
    textDecorationLine: 'line-through',
  },
  // A No Access entry's "not in force here" explanation.
  notInForceReason: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
});

/** Formats an ISO date string for display; falls back to the raw value when
 * parsing fails, and to an em-dash when absent. */
function formatGrantDate(iso: string | undefined): string {
  if (!iso) return '—';
  // Task 099a: the BFF's Date Only values (`/record-grants`: grantedDate, expiresDate) arrive as a bare `yyyy-MM-dd`.
  // `new Date('2026-12-10')` is UTC midnight, which shows as the PREVIOUS day anywhere west of UTC — so a bare date is
  // read as the calendar date it names.
  const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  const d = dateOnly ? new Date(Number(dateOnly[1]), Number(dateOnly[2]) - 1, Number(dateOnly[3])) : new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

/**
 * Task 099a: Current Access's error state when the record's grants could not be read and the failure carries no
 * ProblemDetails `detail` of its own (a network failure, a sign-in failure, a host that threw a plain Error).
 */
export const CURRENT_ACCESS_READ_FAILED_MESSAGE =
  'Current Access could not be loaded, so who has access to this record is not shown here. Close and reopen to try again.';

/**
 * Task 099c (FR-33): `POST /api/v1/external-access/set-record-share-expiry` — one expiry on every active external share
 * of the record, all-or-nothing. Its body is `{ recordType, recordId, expiryDate }` and nothing else: the Expiration
 * changes the date only, never the access level (ISS-023).
 */
const SET_RECORD_SHARE_EXPIRY_PATH = '/api/v1/external-access/set-record-share-expiry';

/** The route's 500 when the transaction was attempted and its outcome is unknown (it may have applied). */
const SHARE_EXPIRY_UNCONFIRMED_REASON_CODE = 'sdap.access.share_expiry.write_failed';

/** The server's expiry for a share added without one (`ExternalGrantLifecycle.DefaultExpiryDays`). Display only. */
const DEFAULT_SHARE_EXPIRY_DAYS = 90;

/**
 * The UTC calendar date `days` from today, `yyyy-MM-dd` — the server's "today" for every expiry decision
 * (`ExternalGrantLifecycle.TodayUtc`). The picker's minimum mirrors the route's rule: a date before UTC today is
 * refused (400), today itself is valid, and there is no maximum. The server stays the authority.
 */
function utcDateOnly(days = 0): string {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + days))
    .toISOString()
    .slice(0, 10);
}

/** An add while the Expiration holds no usable date (task 099c): nothing is sent until it is corrected. It names the
 * UTC minimum the same way the field's own error does. */
function addExpiryInvalidMessage(): string {
  return `Choose ${formatGrantDate(utcDateOnly())} or later before adding people.`;
}

/** What the Expiration shows for the record's shares (task 099c). */
type ShareExpirySummary =
  /** The grants read failed (or has not answered): the date is not known, so it cannot be shown or changed. */
  | { kind: 'unknown' }
  /** The record has no external share: the server gives a new share today + 90 days. */
  | { kind: 'noShares' }
  | {
      kind: 'shares';
      /**
       * The record's date: the earliest expiry among the shares still in force (it governs the soonest lapse). When
       * every share has lapsed, the earliest lapsed date (shown, never sent: the route refuses a past date). Null when
       * no share carries a date.
       */
      earliest: string | null;
      /** The shares do not all end on the same date — stated, never silently resolved to one. */
      diverges: boolean;
      /** A share has lapsed: applying a date renews it (the route's "renew them too", owner 2026-09-11). */
      anyLapsed: boolean;
    };

/**
 * Summarizes the record's external shares — the rows the host's grants read returned (each with an `accessRecordId`;
 * standing members and user shares are not shares the route dates) — for the Expiration (task 099c).
 */
function summarizeShareExpiry(grants: IAccessGrantRecord[] | null): ShareExpirySummary {
  if (grants === null) return { kind: 'unknown' };
  const shares = grants.filter(g => !!g.accessRecordId);
  if (shares.length === 0) return { kind: 'noShares' };
  const dates = shares.map(g => (g.expiresDate ? g.expiresDate.slice(0, 10) : null));
  const dated = dates.filter((d): d is string => d !== null).sort();
  const liveDated = shares
    .filter(g => g.isExpired !== true && !!g.expiresDate)
    .map(g => (g.expiresDate as string).slice(0, 10))
    .sort();
  return {
    kind: 'shares',
    // `noUncheckedIndexedAccess` is off, so `[0]` is typed `string` although it is `undefined` on an empty array.
    earliest: liveDated.length > 0 ? liveDated[0] : dated.length > 0 ? dated[0] : null,
    diverges: new Set(dates.map(d => d ?? '')).size > 1,
    anyLapsed: shares.some(g => g.isExpired === true),
  };
}

/** Mirrors the BFF's `SpeContainerRevokeOutcome` enum (camelCase-serialized
 * PascalCase member names via `JsonStringEnumConverter`, no naming policy).
 * Shared by {@link AccessGrantModalApiError} and {@link IRevokeAccessResponseBody}
 * so both read the SAME set of values regardless of which HTTP status carried
 * them (task 065 M2). */
type SpeContainerRevokeOutcome = 'NotAttempted' | 'PermissionRemoved' | 'NoPermissionFound' | 'Failed';

/** M2 (task 024 → task 065): the one reason code `/revoke` uses for its single
 * incomplete shape — the Dataverse grant WAS deactivated, but the SPE
 * container permission could not be confirmed removed. MUST match
 * `RevokeExternalAccessEndpoint.RevokeSpeCleanupIncompleteReason` exactly;
 * deliberately its OWN leaf on the `sdap.*.incomplete.*` family
 * `/close-project` established, not closure's literal code — a single-grant
 * revoke and a project closure must stay distinguishable to a caller
 * switching on the code. */
const REVOKE_SPE_CLEANUP_INCOMPLETE_REASON_CODE = 'sdap.revoke.incomplete.container_not_cleared';

/** #2103 follow-up: the NON-retryable leaf of the same family — an organization grant was revoked, but the
 * organization has more active members than one revoke may sweep, so no member's container permission was removed
 * and a retry cannot change that. The server's `detail` says what an administrator must do. MUST match
 * `RevokeExternalAccessEndpoint.RevokeOrganizationTooLargeReason` exactly. */
const REVOKE_ORGANIZATION_TOO_LARGE_REASON_CODE = 'sdap.revoke.incomplete.organization_too_large';

/** #2104 round 3: the other NON-retryable leaf of the family — the grant was revoked, but a container permission
 * matching the revoked grantee is ambiguous (more than one matches) or its holder cannot be verified, so nothing was
 * removed for them and a retry refuses again. The server's `detail` tells an administrator to resolve the container's
 * permissions in the SPE admin app. MUST match `RevokeExternalAccessEndpoint.RevokePermissionAmbiguousReason` exactly. */
const REVOKE_PERMISSION_AMBIGUOUS_REASON_CODE = 'sdap.revoke.incomplete.permission_ambiguous';

/** The `/revoke` reason codes a retry cannot fix: the grant WAS deactivated, and the server's `detail` says what an
 * administrator must do — shown as a warning, never as "please try again". */
const NON_RETRYABLE_REVOKE_REASON_CODES: ReadonlySet<string> = new Set([
  REVOKE_ORGANIZATION_TOO_LARGE_REASON_CODE,
  REVOKE_PERMISSION_AMBIGUOUS_REASON_CODE,
]);

/**
 * Thrown by {@link postJson}/{@link getJson} for a non-OK BFF response (task-024
 * finding M8, transferred to task 065). Carries the parsed RFC 7807
 * ProblemDetails `reasonCode` (ADR-003/ADR-019) so callers can distinguish a
 * delegation deny from an ordinary failure without re-parsing the body.
 */
class AccessGrantModalApiError extends Error {
  readonly status: number;
  readonly reasonCode?: string;
  readonly detail: string;
  /** M2 (task 024 → task 065): present ONLY when the ProblemDetails carried it
   * (currently: the `/revoke` SPE-cleanup-incomplete 500). Never discarded —
   * owner directive 2026-09-10 — so `confirmRevoke`'s catch block can still
   * build the three-outcome notice via {@link buildRevokeNotice}. */
  readonly deactivatedCount?: number;
  readonly speContainerOutcome?: SpeContainerRevokeOutcome;
  /** The ProblemDetails' own `detail`, when it carried one (`detail` above falls back to the title or the status). */
  readonly problemDetail?: string;
  /** Task 175: a 409 `access_follows_parent`'s `parentRecordType`, for the fallback sentence when it has no `detail`. */
  readonly parentRecordType?: string;
  /** Task 181: `/share-user`'s related-records-pending 500 also says whether the person could not be notified. */
  readonly notificationFailed?: boolean;

  constructor(
    status: number,
    detail: string,
    reasonCode?: string,
    deactivatedCount?: number,
    speContainerOutcome?: SpeContainerRevokeOutcome,
    extras?: { problemDetail?: string; parentRecordType?: string; notificationFailed?: boolean }
  ) {
    super(`AccessGrantModal request failed (${status}): ${detail}`);
    this.name = 'AccessGrantModalApiError';
    this.status = status;
    this.detail = detail;
    this.reasonCode = reasonCode;
    this.deactivatedCount = deactivatedCount;
    this.speContainerOutcome = speContainerOutcome;
    this.problemDetail = extras?.problemDetail;
    this.parentRecordType = extras?.parentRecordType;
    this.notificationFailed = extras?.notificationFailed;
    // Restore the prototype chain (extending built-ins across ES5/ts-jest
    // transpilation targets can otherwise break `instanceof` checks) — same
    // fix as communicationApi.ts's SendCommunicationError.
    Object.setPrototypeOf(this, AccessGrantModalApiError.prototype);
  }

  /** Builds an error from a non-OK {@link Response}. Never throws while parsing. */
  static async fromResponse(response: Response): Promise<AccessGrantModalApiError> {
    const status = response.status;
    try {
      return AccessGrantModalApiError.fromBody(status, await response.json());
    } catch {
      return new AccessGrantModalApiError(status, `HTTP ${status}`);
    }
  }

  /**
   * Builds an error from what `@spaarke/auth`'s `authenticatedFetch` THROWS for a non-OK response —
   * it never returns one, so without this every `instanceof AccessGrantModalApiError` branch in this
   * file is unreachable under the fetch every host injects. An `ApiError` carries the status and the
   * already-parsed ProblemDetails (read exactly as {@link fromResponse} reads a body); an `AuthError`
   * is the 401 whose retries ran out. Returns `null` for anything else (a network failure), which the
   * caller rethrows untouched — it is not a server answer.
   */
  static fromThrown(err: unknown): AccessGrantModalApiError | null {
    if (isApiError(err)) {
      const problem = problemOf(err);
      return problem
        ? AccessGrantModalApiError.fromBody(err.status, problem)
        : new AccessGrantModalApiError(err.status, err.message);
    }
    if (isAuthFailure(err)) {
      return new AccessGrantModalApiError(401, err instanceof Error && err.message ? err.message : 'HTTP 401');
    }
    return null;
  }

  /** The shared ProblemDetails read behind {@link fromResponse} and {@link fromThrown}. */
  private static fromBody(status: number, raw: unknown): AccessGrantModalApiError {
    const body = (raw ?? {}) as {
      reasonCode?: string;
      detail?: string;
      title?: string;
      // M2 (task 024 → task 065): only `/revoke`'s incomplete-SPE-cleanup 500
      // carries these today; every other ProblemDetails leaves them undefined.
      deactivatedCount?: number;
      speContainerOutcome?: string;
      // Task 175: an extension of the 409 `access_follows_parent`.
      parentRecordType?: unknown;
      // Task 181: an extension of `/share-user`'s related-records-pending 500.
      notificationFailed?: unknown;
    };
    const reasonCode = typeof body.reasonCode === 'string' ? body.reasonCode : undefined;
    const detail = body.detail ?? body.title ?? `HTTP ${status}`;
    const deactivatedCount = typeof body.deactivatedCount === 'number' ? body.deactivatedCount : undefined;
    const speContainerOutcome =
      typeof body.speContainerOutcome === 'string'
        ? (body.speContainerOutcome as SpeContainerRevokeOutcome)
        : undefined;
    const problemDetail = typeof body.detail === 'string' && body.detail.trim() ? body.detail : undefined;
    return new AccessGrantModalApiError(status, detail, reasonCode, deactivatedCount, speContainerOutcome, {
      problemDetail,
      parentRecordType: typeof body.parentRecordType === 'string' ? body.parentRecordType : undefined,
      notificationFailed: body.notificationFailed === true,
    });
  }
}

/**
 * Classifies a write/read failure as one of the modal's two designed deny
 * states, or `null` for an ordinary failure the caller should handle its own
 * way (network error, 500, validation 400, etc.). Only
 * {@link AccessGrantModalApiError} instances are ever classified — a thrown
 * `TypeError` (offline, DNS) is never mistaken for a server-issued deny.
 *
 * `'delegation'` covers EVERY `sdap.access.deny.delegation_*` reason code
 * `DelegationRuleFilter` can return (no caller token, unresolvable target, the
 * rights check itself failing, or the ordinary Write-required / Share-required
 * deny) — all mean the same thing to this UI: "you cannot manage access on
 * this record right now," which is the one designed banner state the project
 * constraint requires (not a raw error, not a retry loop). Only the WORDING
 * differs (task 179): the two rights refusals name the rule (Write and the
 * Share privilege); the others say the check could not be made, so an outage
 * never tells an entitled user to change their security role.
 *
 * `'followsParent'` (task 175, owner round 87) is the 409 `sdap.access.access_follows_parent`: THIS change would
 * make the record looser than the floor its parent sets. It is NOT a deny state: it refuses that one action only,
 * shown inline (the action's notice) with the server's `detail`, or the designed sentence when there is none. Callers
 * split it off with {@link splitAccessFailure}.
 */
/** The two designed deny states, which block every write until the modal is reopened. */
interface IAccessDeny {
  kind: 'delegation' | 'unauthenticated';
  title: string;
  message: string;
}

/** Task 179: the two `DelegationRuleFilter` refusals that are about the caller's rights (the rest are "could not check"). */
const DELEGATION_RIGHTS_REASON_CODES: ReadonlySet<string> = new Set([
  'sdap.access.deny.delegation_write_required',
  'sdap.access.deny.delegation_share_required',
]);

type IAccessFailure = IAccessDeny | { kind: 'followsParent'; message: string };

function classifyAccessFailure(err: unknown): IAccessFailure | null {
  if (!(err instanceof AccessGrantModalApiError)) return null;
  if (err.status === 401) {
    return {
      kind: 'unauthenticated',
      title: 'Sign-in expired',
      message: 'Your sign-in has expired. Refresh the page and try again.',
    };
  }
  if (err.status === 409 && err.reasonCode === ACCESS_FOLLOWS_PARENT_REASON_CODE) {
    return {
      kind: 'followsParent',
      message: err.problemDetail ?? followsParentFallbackMessage(err.parentRecordType),
    };
  }
  if (err.status === 403 && err.reasonCode?.startsWith('sdap.access.deny.delegation_')) {
    if (!DELEGATION_RIGHTS_REASON_CODES.has(err.reasonCode)) {
      return {
        kind: 'delegation',
        title: 'Access could not be checked',
        message:
          'Whether you can change who has access to this record could not be checked. Close Manage Access and open it again to retry.',
      };
    }
    return {
      kind: 'delegation',
      title: 'Write and Share required',
      // Task 179 (owner round 89): the same sentence as the server's DelegationRuleFilter refusal (Write and Share).
      message:
        'To change who else can access this record you need Write access to it and the Share privilege on its table (set in your security role).',
    };
  }
  return null;
}

/** Splits a failure into a deny state (the banner, every write blocked), a parent-floor refusal of this one action
 * (task 175), or neither. */
function splitAccessFailure(err: unknown): { deny: IAccessDeny | null; parentRefusal: string | null } {
  const failure = classifyAccessFailure(err);
  if (!failure) return { deny: null, parentRefusal: null };
  return failure.kind === 'followsParent'
    ? { deny: null, parentRefusal: failure.message }
    : { deny: failure, parentRefusal: null };
}

/** The write-time refusals the grant routes return. Task 138: the record's
 * access policy — `sdap.access.grant.record_restricted` /
 * `.org_grant_direct_only_record` (422) and `.policy_unreadable` (503). Task
 * 139 (owner Q1, "cap every grant at the grantor's own level"):
 * `.caller_cannot_grant` (403 — your own access allows granting nothing),
 * `.would_lower_existing` (409 — the grant was capped at your level and the
 * person already holds more; also returned by `/share-user`) and
 * `.grantee_denied` (422 — the grantee is on the record's No Access list), plus
 * `/share-user`'s own `sdap.access.user_share.caller_cannot_grant`. Task 142
 * (owner round 13 item 4; round 18): `.no_access_unverifiable` (503 — whether
 * the grantee is on the No Access list could not be checked, a read fault;
 * nothing was granted). Their ProblemDetails `detail` is written for the
 * person, so the modal shows it verbatim instead of a generic "try again": a
 * policy refusal says why retrying would fail the same way, and the two 503s
 * (`.policy_unreadable`, `.no_access_unverifiable`) say themselves that a retry
 * may succeed. Task 099c: `.expiry_in_past` (400 — the add's Expiration is
 * before UTC today; the server's sentence names its "today"). */
const GRANT_POLICY_REASON_CODES = new Set([
  'sdap.access.grant.expiry_in_past',
  'sdap.access.grant.record_restricted',
  'sdap.access.grant.org_grant_direct_only_record',
  'sdap.access.grant.policy_unreadable',
  'sdap.access.grant.caller_cannot_grant',
  'sdap.access.grant.would_lower_existing',
  'sdap.access.grant.grantee_denied',
  'sdap.access.grant.no_access_unverifiable',
  'sdap.access.user_share.caller_cannot_grant',
]);

/** Task 149: `/share-user` or `/unshare-user` changed the share on the record itself, but not yet on every related
 * record of a secure one. NOT a refusal: the share WAS written (or removed), so it counts as done and the server's
 * sentence (how many related records, and that they complete automatically) is shown as a warning. */
const USER_SHARE_CHILDREN_INCOMPLETE_REASON_CODE = 'sdap.access.user_share.children_incomplete';

/** The server's sentence when `/share-user` wrote the share but some related records of a secure record are not yet
 * updated (task 149), or `null` for any other outcome. */
function childrenIncompleteDetail(err: unknown): string | null {
  if (!(err instanceof AccessGrantModalApiError)) return null;
  return err.reasonCode === USER_SHARE_CHILDREN_INCOMPLETE_REASON_CODE ? err.detail : null;
}

/** Task 139 (owner round 3, S5): `/unshare-user` refuses to remove the last
 * person who can open a secure record. Its `detail` says what to do instead. */
const UNSHARE_LAST_READER_REASON_CODE = 'sdap.access.user_share.last_reader_on_secure_record';

/** Task 114 (owner test feedback 2026-10-07): `/share-user`'s refusals about the person being shared with — who cannot
 * receive a share (disabled, not a person, external on a Restricted record, no such user), is on the record's No Access
 * list, or could not be checked. The server's sentence says "this user"/"this person", so the modal names the person,
 * with their email because several users can share a name. Without this they fell through to the generic "1 failed.
 * Please try again." — wrong advice for every refusal here except the two read faults, whose own sentence says to try
 * again. */
const USER_SHARE_NAMED_REFUSAL_CODES = new Set([
  'sdap.access.user_share.user_disabled',
  'sdap.access.user_share.user_not_a_person',
  'sdap.access.user_share.user_not_internal',
  'sdap.access.user_share.user_not_found',
  'sdap.access.user_share.subject_no_access',
  'sdap.access.user_share.no_access_unverifiable',
  'sdap.access.user_share.read_failed',
]);
const USER_NOT_INTERNAL_REASON_CODE = 'sdap.access.user_share.user_not_internal';

/** The named sentence for a `/share-user` eligibility refusal, or `null` for any other failure. */
function userShareRefusalDetail(err: unknown, user: IUserPick): string | null {
  if (!(err instanceof AccessGrantModalApiError)) return null;
  if (!err.reasonCode || !USER_SHARE_NAMED_REFUSAL_CODES.has(err.reasonCode)) return null;
  const who = user.email ? `${user.name} (${user.email})` : user.name;
  if (err.reasonCode === USER_NOT_INTERNAL_REASON_CODE) {
    return `System user ${who} is an external user. Restricted records cannot be shared with external users.`;
  }
  return `System user ${who}: ${err.detail}`;
}

/** Task 193 (#2338): `/grant`'s refusals when the contact is linked to more than one internal user (409) or who it
 * represents could not be read (503). Nothing was written. */
const CONTACT_REPRESENTS_SEVERAL_USERS_REASON_CODE = 'sdap.access.grant.contact_represents_several_users';
const CONTACT_REPRESENTATION_UNREADABLE_REASON_CODE = 'sdap.access.grant.contact_representation_unreadable';

/**
 * Task 193 (#2338): the named sentence when `/grant` refused a contact because of who the contact represents — linked to
 * several users, unreadable, or one colleague who cannot receive a share (the share core's refusals, as `/share-user`
 * answers them). `null` for any other failure. The server's sentence says "this contact"/"this user", so the modal
 * names the contact.
 */
function contactShareRefusalDetail(err: unknown, contactName: string): string | null {
  if (!(err instanceof AccessGrantModalApiError) || !err.reasonCode) return null;
  if (
    err.reasonCode === CONTACT_REPRESENTS_SEVERAL_USERS_REASON_CODE ||
    err.reasonCode === CONTACT_REPRESENTATION_UNREADABLE_REASON_CODE
  ) {
    return `${contactName}: ${err.detail}`;
  }
  if (!USER_SHARE_NAMED_REFUSAL_CODES.has(err.reasonCode)) return null;
  if (err.reasonCode === USER_NOT_INTERNAL_REASON_CODE) {
    return `${contactName} is linked to a user flagged as external. Restricted records cannot be shared with external users.`;
  }
  return `${contactName} (linked to an internal user): ${err.detail}`;
}

/** The server's own explanation when the record's access policy refused a
 * grant (task 138), or `null` for any other failure. */
function grantPolicyRefusalDetail(err: unknown): string | null {
  if (!(err instanceof AccessGrantModalApiError)) return null;
  return err.reasonCode && GRANT_POLICY_REASON_CODES.has(err.reasonCode) ? err.detail : null;
}

/** The `RevokeAccessResponse` fields this modal reads (task-024 finding M2 /
 * owner directive 2026-09-10) — camelCase per the BFF's default STJ naming.
 * `speContainerOutcome` mirrors `SpeContainerRevokeOutcome`; only the values
 * a per-contact/org revoke from THIS modal can produce are named (an
 * organization-member breakdown is `speOrgMemberCleanup`, unused here). */
interface IRevokeAccessResponseBody {
  speContainerOutcome?: SpeContainerRevokeOutcome;
  deactivatedCount?: number;
  /** Task 142 (criterion 17): the read-time terms that still confer access when the revoked grant was an Assigned-To
   * auto grant — for a contact `standing-grant` / `organization-standing-grant`, for an organization
   * `organization-members-standing-grant`, or `unknown`. Absent when none. */
  residualAccessTerms?: string[] | null;
}

/**
 * One Assigned-To ledger entry (task 142) as `GET /api/v1/external-access/assigned-access` returns it — a suggestion
 * waiting on a secure record (`PendingConfirmation`), the provenance of an automatic grant/share, or a declined/skipped
 * entry. Read directly by this modal (entity-agnostic given {recordType, recordId}, like `/user-shares`).
 */
export interface IAssignedAccessEntry {
  entryId: string;
  sourceField: string;
  /** The field's label, e.g. "Assigned Paralegal 1" — what the suggestion names. */
  sourceFieldLabel: string;
  subjectKind: 'contact' | 'organization';
  subjectId: string;
  subjectName?: string | null;
  /** Set when the contact represents an internal user: Grant shares to this user instead of granting the contact. */
  systemUserId?: string | null;
  accessRecordId?: string | null;
  state: string;
  reason?: string | null;
  /** The read-time terms that keep this contact on the record if its grant is removed (owner A2: they stay). */
  residualAccessTerms: string[];
}

/** The Collaborate level every Assigned-To grant/share is made at (owner rule 5) — the BFF's fixed enum value. */
export const ASSIGNED_ACCESS_LEVEL = 100000001;

/**
 * Names the read-time terms that still bring a contact to the record (task 142, criterion 17 — owner A2 reversed:
 * standing and organization access STAY), so the operator is never told "removed" while access silently remains.
 * `null` when no term applies.
 */
export function describeResidualAccess(fullName: string, terms: readonly string[] | null | undefined): string | null {
  if (!terms || terms.length === 0) return null;
  const unique = Array.from(new Set(terms));
  if (unique.length === 1 && unique[0] === 'organization-members-standing-grant') {
    // An ORGANIZATION's automatic grant: its people keep access through the organization's own standing grant.
    return (
      `${fullName}'s people still reach this record through the organization's standing grant (the organization is ` +
      'assigned to this record). Removing this grant does not remove that — change the standing grant on the ' +
      'organization to remove it.'
    );
  }
  const parts = unique.map(t =>
    t === 'standing-grant'
      ? 'their standing grant'
      : t === 'organization-standing-grant'
        ? "their organization's standing grant (the organization is assigned to this record)"
        : t === 'organization-members-standing-grant'
          ? "the organization's standing grant"
          : 'access that could not be checked'
  );
  return (
    `${fullName} still reaches this record through ${parts.join(' and ')}. Removing this grant does not remove that — ` +
    'change the standing grant on the contact or organization to remove it.'
  );
}

/**
 * Builds the revoke notice from the response body — the owner's 2026-09-10
 * directive: distinguish fully revoked / grant-revoked-but-file-access-may-
 * remain / nothing-revoked, and never discard `deactivatedCount`. Uses fields
 * the endpoint ALREADY returns today (no server change required).
 */
export function buildRevokeNotice(
  fullName: string,
  data: IRevokeAccessResponseBody
): { intent: 'success' | 'warning' | 'error'; text: string } {
  if (data.speContainerOutcome === 'Failed') {
    return {
      intent: 'warning',
      text:
        `Revoked ${fullName}'s access record, but their file access could not be confirmed removed — ` +
        `they may still be able to open files on this record. Retry the revoke, and escalate if it persists.`,
    };
  }
  if ((data.deactivatedCount ?? 0) === 0) {
    return { intent: 'warning', text: `${fullName} already had no active access record to revoke.` };
  }
  // Task 142 (criterion 17): an automatic grant whose contact still reaches the record through a read-time term.
  const residual = describeResidualAccess(fullName, data.residualAccessTerms);
  if (residual) {
    return { intent: 'warning', text: `Revoked ${fullName}'s grant. ${residual}` };
  }
  return { intent: 'success', text: `Revoked access for ${fullName}.` };
}

/** Outcome tally from one `handleGrantSelected` batch — pure input to
 * {@link buildGrantBatchNotice}, kept separate from the component so the
 * notice text is independently reasoned about and testable, the same
 * decomposition already applied to revoke via {@link buildRevokeNotice}. */
interface IGrantBatchOutcome {
  granted: number;
  selectedCount: number;
  failures: number;
  denied: boolean;
  /** Task 181: the server could not tell at least one person they were given access (`notificationFailed`). */
  anyNotificationFailed: boolean;
  anyNarrowed: boolean;
  /** The server's `detail` for each grant the record's access policy refused
   * (task 138) — shown verbatim, never replaced by a generic "try again". */
  policyRefusals?: string[];
  /** Task 149: the server's `detail` for each share that WAS written while some related records of the secure record
   * are not updated yet. Those shares are counted in `granted`; this only adds the server's sentence. */
  relatedRecordsPending?: string[];
  /** Slice B3: the people granted whose invite is for a work account (`WorkAccountInvited`). */
  workAccountInvited?: string[];
  /** The people granted by `/grant` whose automatic invite did not complete (`invite.status` Failed): the grant
   * stands but they cannot sign in yet. `organisationUnchecked` is the part whose organisation could not be checked.
   * They stay selected and the notice offers Retry. */
  inviteNotSent?: { names: string[]; organisationUnchecked: string[] };
  /** Slice C2b: the people whose invite was not sent because they already have an account (`/invite` answered
   * {@link INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE}). They stay selected; the notice shows the server's text and
   * the sign-in link for staff to pass on. */
  alreadyHasAccount?: IAlreadyHasAccount[];
  /** Task 193 (#2338): the contacts `/grant` answered with a share with the colleague they represent. */
  sharedWithColleagues?: ISharedWithColleague[];
}

/** Task 193 (#2338): a contact add the server answered by sharing with the one colleague the contact represents. */
interface ISharedWithColleague {
  /** The colleague's name, or {@link COLLEAGUE_FALLBACK_NAME} when the server could not read it. */
  name: string;
  /** The add carried an Expiration, which the share does not take (a share has no end date). */
  expiryNotApplied: boolean;
}

/** Task 193: the colleague's label when the server shared with them but could not read their name. */
export const COLLEAGUE_FALLBACK_NAME = 'your colleague';

/** Task 193 (#2338; expiry ruling (c)): the notice's sentence for a contact add the server answered with a share. A
 * share has no end date; when the add carried an Expiration, the server says it was not applied, and so does this. */
export function sharedWithColleagueSentence(shared: ISharedWithColleague): string {
  return shared.expiryNotApplied
    ? `Shared with ${shared.name}. Shares with colleagues have no end date.`
    : `Shared with ${shared.name} — internal user.`;
}

/** Slice C2b: a person `/invite` sent nothing to because they already have an account. */
interface IAlreadyHasAccount {
  name: string;
  /** The server's staff text, or {@link ALREADY_HAS_ACCOUNT_FALLBACK_SENTENCE} when the response has none. */
  message: string;
  signInUrl?: string;
}

/** Slice C2b: `/invite`'s `emailErrorCode` when the person already has an account: no invite was due or sent. */
export const INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE = 'sdap.access.invite.contact_already_has_account';

/** Slice C2b: shown for a person who already has an account when the response carries no staff text. */
export const ALREADY_HAS_ACCOUNT_FALLBACK_SENTENCE = 'This person already has an account, so no invite was sent.';

/** Slice C2b: the batch notice's sentence for one person who already has an account. */
export function alreadyHasAccountSentence(entry: IAlreadyHasAccount): string {
  return `${entry.name}: ${entry.message}${entry.signInUrl ? ` ${entry.signInUrl}` : ''}`;
}

/**
 * Slice C2c: the server's sign-in link, as a link target only when it is an absolute `https:` URL; otherwise `undefined`
 * and the link is shown as text, never as a link. Pure.
 */
export function safeSignInHref(url: string | undefined): string | undefined {
  if (!url) return undefined;
  try {
    return new URL(url).protocol === 'https:' ? url : undefined;
  } catch {
    return undefined; // not an absolute URL
  }
}

/** Slice B3: `/invite`'s 503 when the person's organisation could not be checked (nothing was written); `/grant` reports
 * it as its invite's `errorCode` (the grant stands). */
export const INVITE_TENANT_LOOKUP_UNAVAILABLE_REASON_CODE = 'sdap.access.invite.tenant_lookup_unavailable';

/** Slice B3: the invite status of a person who will sign in with their work account (`/invite`'s `status`, `/grant`'s
 * `invite.status`). */
const WORK_ACCOUNT_INVITED_STATUS = 'WorkAccountInvited';

/** Slice B3: added to the batch notice after the names of people invited for a work-account sign-in. */
export const WORK_ACCOUNT_INVITED_SENTENCE = 'invited — will sign in with their work account.';

/** `/grant`'s `invite.status` when the automatic invite did not complete (the grant stands). */
const GRANT_INVITE_FAILED_STATUS = 'Failed';

/** C2d: `/invite`'s `emailErrorCode` when the invite email was attempted and not sent. */
export const INVITE_EMAIL_NOT_SENT_REASON_CODE = 'sdap.access.invite.email_not_sent';

/** The batch notice's sentence for people granted by `/grant` whose invite was not sent; the notice offers Retry. */
export function inviteNotSentRetrySentence(inviteNotSent: {
  names: string[];
  organisationUnchecked: string[];
}): string {
  const unchecked = inviteNotSent.organisationUnchecked;
  const others = inviteNotSent.names.filter(n => !unchecked.includes(n));
  const parts: string[] = [];
  if (unchecked.length > 0) {
    parts.push(`Couldn't check the organisation of ${unchecked.join(', ')}, so no invite was sent.`);
  }
  if (others.length > 0) parts.push(`No invite was sent to ${others.join(', ')}.`);
  return `${parts.join(' ')} They hold access but cannot sign in yet. Retry.`;
}

/** The notice the modal shows; `retry` adds a Retry action that re-runs Add for the rows still selected (slice B3). */
interface IModalNotice {
  intent: 'success' | 'warning' | 'error';
  text: string;
  retry?: boolean;
  /** Slice C2c: the sign-in links in `text` that render as links: each one {@link safeSignInHref} accepted. Any other
   * link in `text` stays text. */
  links?: string[];
}

/** Slice C2c: `text`, with each of `links` it contains rendered as a link (opened in a new tab); the rest as text. */
function noticeContent(text: string, links: string[] | undefined): React.ReactNode {
  if (!links || links.length === 0) return text;
  const parts: React.ReactNode[] = [];
  let rest = text;
  links.forEach((href, i) => {
    const at = rest.indexOf(href);
    if (at < 0) return;
    parts.push(rest.slice(0, at));
    parts.push(
      <Link key={`notice-link-${i}`} href={href} target="_blank" rel="noopener noreferrer">
        {href}
      </Link>
    );
    rest = rest.slice(at + href.length);
  });
  parts.push(rest);
  return parts;
}

/** Builds the post-batch notice for `Add (N)` — five distinct shapes ordered by
 * priority (a denial or a failure dominates a related-records-pending or
 * narrowed success). Denial and failure are reported separately because they call for
 * different next actions: a failure invites retry; a denial does not (retrying
 * without Write on the record fails the same way). Task 181: whichever shape
 * applies, a grant whose person could not be notified adds
 * {@link NOTIFICATION_FAILED_SENTENCE}, and a success becomes a warning. */
function buildGrantBatchNotice(outcome: IGrantBatchOutcome): IModalNotice {
  const { granted, selectedCount, failures, denied, anyNotificationFailed, anyNarrowed } = outcome;
  const policyRefusals = outcome.policyRefusals ?? [];
  const inviteNotSent = outcome.inviteNotSent ?? { names: [], organisationUnchecked: [] };
  const alreadyHasAccount = outcome.alreadyHasAccount ?? [];
  // Slice B3: who was invited for a work-account sign-in, named once, before the other suffixes.
  const workAccount = outcome.workAccountInvited ?? [];
  // Task 193: the contacts the server shared with their colleague instead, named first.
  const sharedSuffix = (outcome.sharedWithColleagues ?? [])
    .map(shared => ` ${sharedWithColleagueSentence(shared)}`)
    .join('');
  const workSuffix = `${sharedSuffix}${
    workAccount.length > 0 ? ` ${workAccount.join(', ')}: ${WORK_ACCOUNT_INVITED_SENTENCE}` : ''
  }`;
  // Task 149: shares that WERE written (counted in `granted`) while some related records of the secure record are not
  // updated yet — the server's sentence is appended to whichever notice applies, never reported as a failure.
  // Task 181: likewise the could-not-notify sentence, last.
  const relatedPending = Array.from(new Set(outcome.relatedRecordsPending ?? [])).join(' ');
  const notifySuffix = anyNotificationFailed ? ` ${NOTIFICATION_FAILED_SENTENCE}` : '';
  const relatedSuffix = `${workSuffix}${relatedPending ? ` ${relatedPending}` : ''}${notifySuffix}`;

  if (denied) {
    return {
      intent: 'error',
      text: `Granted access to ${granted} of ${selectedCount} before access was denied; the message above says why.${relatedSuffix}`,
    };
  }
  const needsRetry = inviteNotSent.names.length > 0;
  if (needsRetry || alreadyHasAccount.length > 0) {
    // A `/grant` whose invite did not complete gets the notice and Retry (the grant stands; Retry re-sends it).
    // Slice C2b: a person who already has an account was sent nothing; they stay selected and the notice shows the
    // server's text and sign-in link. Retrying would answer the same, so that alone offers no Retry.
    const inviteSentence = inviteNotSent.names.length > 0 ? ` ${inviteNotSentRetrySentence(inviteNotSent)}` : '';
    const accountSentence = alreadyHasAccount.map(entry => ` ${alreadyHasAccountSentence(entry)}`).join('');
    const reasons = policyRefusals.length > 0 ? ` ${Array.from(new Set(policyRefusals)).join(' ')}` : '';
    const others = failures > 0 ? ` ${failures} other item(s) failed; please try those again.` : '';
    const text = `Granted access to ${granted} of ${selectedCount}.${inviteSentence}${accountSentence}${reasons}${others}${relatedSuffix}`;
    // Slice C2c: a sign-in link is a link only when it is an absolute https URL; any other value stays text.
    const links = alreadyHasAccount.flatMap(entry => safeSignInHref(entry.signInUrl) ?? []);
    if (!needsRetry) return { intent: policyRefusals.length > 0 || failures > 0 ? 'error' : 'warning', text, links };
    return { intent: 'error', text, retry: true, links };
  }
  if (policyRefusals.length > 0) {
    // Task 138: the record's access policy refused at least one grant. The
    // server's sentence says why and what to do (for a policy refusal,
    // retrying would fail the same way; for the two 503 read faults the
    // sentence itself says to try again), so the modal adds no retry advice
    // of its own.
    const reasons = Array.from(new Set(policyRefusals)).join(' ');
    const others = failures > 0 ? ` ${failures} other item(s) failed; please try those again.` : '';
    return {
      intent: 'error',
      text: `Granted access to ${granted} of ${selectedCount}. ${reasons}${others}${relatedSuffix}`,
    };
  }
  if (failures > 0) {
    return {
      intent: 'error',
      text: `Granted access to ${granted} of ${selectedCount}; ${failures} failed. Please try again.${relatedSuffix}`,
    };
  }
  const narrowedNote = anyNarrowed
    ? ' Some were narrowed to your own access level on this record (you can only grant what you hold).'
    : '';
  if (relatedPending || anyNarrowed || anyNotificationFailed) {
    return { intent: 'warning', text: `Granted access to ${granted} item(s).${narrowedNote}${relatedSuffix}` };
  }
  return { intent: 'success', text: `Granted access to ${granted} item(s).${workSuffix}` };
}

/** Task 181 (owner round 89 item 3): added to the batch notice when the server granted access but could not send the
 * person the in-app notification that links to the record. */
export const NOTIFICATION_FAILED_SENTENCE = 'Some people could not be notified; share the record link with them.';

/**
 * Task 114 (owner round 67 amendment 4(c), owner-authored copy): the label of a user share on a Restricted record whose
 * holder is flagged external (`externalNoAccess` from `/user-shares`) — shown until the server removes the share.
 */
export const EXTERNAL_USER_NO_ACCESS_LABEL = 'External user — no access';

/**
 * The ONE explanatory MessageBar for a record's Access Permission (task 138;
 * owner O1 FINAL, 2026-10-01: "Secure – Restricted" with the explanation when
 * the record is secure AND Restricted; a "Secure" explanation when secure only).
 * `null` for a Standard, non-secure record — nothing to explain. Pure, so the
 * copy is pinned by tests without rendering.
 */
export function describeAccessPermission(
  state: AccessPermissionState,
  isSecureRecord: boolean,
  inheritedFrom: string | null = null
): { intent: 'error' | 'warning'; title: string; text: string } | null {
  const banner = describeOwnAccessPermission(state, isSecureRecord);
  return banner && inheritedFrom ? { ...banner, text: `${banner.text} ${inheritedFrom}` } : banner;
}

function describeOwnAccessPermission(
  state: AccessPermissionState,
  isSecureRecord: boolean
): { intent: 'error' | 'warning'; title: string; text: string } | null {
  const restrictedText =
    'Only internal users can be given access to this record. Contacts and organizations cannot be granted ' +
    'access or invited. You can still share it with a colleague (Add person) and revoke existing access.';
  if (state === 'restricted') {
    return isSecureRecord
      ? {
          intent: 'error',
          title: 'Secure – Restricted',
          text: `This record is secure and Restricted. ${restrictedText}`,
        }
      : { intent: 'error', title: 'Restricted Access', text: restrictedText };
  }
  if (isSecureRecord) {
    return {
      intent: 'error',
      title: 'Secure',
      text:
        'This record is secure. Contacts get access only through grants made to them by name; organization-wide ' +
        'grants, standing grants and organization membership give no access to it.',
    };
  }
  if (state === 'limited') {
    return {
      intent: 'warning',
      title: 'Limited Access',
      text:
        'Contacts get access to this record only through grants made to them by name. Organization-wide grants, ' +
        'standing grants and organization membership give no access to it.',
    };
  }
  return null;
}

/** A pending revoke confirmation — either a `sprk_externalrecordaccess` grant
 * (contact/organization, `/revoke`) or an internal system-user POA share
 * (task 065, `/unshare-user`). The two use different endpoints and different
 * identifying fields, so the confirm dialog branches on `kind`. */
type PendingRevoke =
  | { kind: 'grant'; accessRecordId: string; contactId: string; fullName: string }
  | { kind: 'share'; systemUserId: string; fullName: string };

export const AccessGrantModal: React.FC<IAccessGrantModalProps> = ({
  open,
  onClose,
  recordId,
  recordType = 'project',
  // 🔴 Fail CLOSED (task 118). Was `= true`; an omitted prop now renders the not-authorized
  // state rather than the full grant UI. See the prop's doc in ./types.ts.
  canGrantAccess = false,
  authenticatedFetch,
  fetchCandidates,
  fetchExistingGrants,
  fetchStandingContacts,
  // searchContacts / searchOrganizations remain in the props contract (SPA-host
  // fallback) but are unused by this PCF-hosted modal, which uses the native
  // advanced-lookup pickers below (task 073 UAT v1.0.24 #1/#4).
  // Task 193 (#2338): the one "Add person" native picker (contacts and users together).
  pickPerson,
  pickOrganization,
  onOpenContact,
  title = 'Manage Access',
  accessLevelOptions = DEFAULT_ACCESS_LEVEL_OPTIONS,
  defaultAccessLevel,
  accessPermissionState: hostAccessPermissionState = 'standard',
  isSecureRecord: hostIsSecureRecord = false,
  fetchSecureOwnerInfo,
  fetchContactOrganizationMemberships,
  initialSection,
  followsParents,
  onOpenParent,
  accessFloor,
  recordAccessPermission,
}) => {
  const styles = useStyles();

  // Task 174 (owner round 84; task 067's amendment): the record's EFFECTIVE access, as task 064's read reports it — a
  // work assignment or project filed under a secure, Limited or Restricted parent is enforced as its parent is, whatever
  // its own stored values (which the host passes) read. The gate, the banner and the "No effect" marks below use the
  // STRICTER of the two; never less strict than the host's. `null` (not read yet, or untrusted): the host's values alone.
  const [serverAccess, setServerAccess] = React.useState<IEffectiveRecordAccess | null>(null);
  const { state: accessPermissionState, isSecure: isSecureRecord } = effectiveAccessState(
    hostAccessPermissionState,
    hostIsSecureRecord,
    serverAccess
  );

  // Access-Permission sharing gate (task 043, FR-14 Option A; task 138). Deliberately
  // computed from the props alone — never from `effectiveAccessLevel` or any other
  // per-grant `sprk_accesslevel` concept above, so the two stay structurally
  // independent (see the module doc comment).
  //   - contactGrantsOffered: Restricted admits no contact-based access, so neither contacts ("Add person" looks up
  //     users only) nor the role-based candidates are offered. User shares are unaffected.
  //   - organizationGrantsOffered: an organization-wide grant needs a Standard record
  //     (Limited and Secure admit only named, direct contact grants).
  const isRestricted = accessPermissionState === 'restricted';
  const contactGrantsOffered = !isRestricted;
  const organizationGrantsOffered = accessPermissionState === 'standard';
  // The one explanatory banner per non-standard state (owner O1 FINAL, 2026-10-01); task 174 names the parent the state
  // follows when it is inherited.
  const permissionBanner = describeAccessPermission(
    accessPermissionState,
    isSecureRecord,
    describeInheritedFrom(serverAccess)
  );

  const [loading, setLoading] = React.useState(false);
  const [candidates, setCandidates] = React.useState<IAccessGrantCandidate[]>([]);
  const [existingGrants, setExistingGrants] = React.useState<IAccessGrantRecord[]>([]);
  // Task 099a: set when the record's grants could not be read — Current Access then shows this message (the server's
  // ProblemDetails `detail`, verbatim) instead of a list, never "No active grants". `notShown`: another message on screen
  // already says why (the deny banner for a refused grants read, the generic notice when another read failed) — the
  // section says only that it is not shown, so one failure is one message.
  const [currentAccessError, setCurrentAccessError] = React.useState<
    { kind: 'failed'; text: string } | { kind: 'notShown' } | null
  >(null);
  // Task 099a (verifier F2): the host's source listed only part of the record's grants (its cap) — stated, never silent.
  // Holds how many it listed; null when the list is whole.
  const [grantsTruncatedAt, setGrantsTruncatedAt] = React.useState<number | null>(null);
  // Task 099c: the record's external shares as the grants read returned them (null: not read, or the read failed) —
  // the source of the toolbar Expiration — and the date the user has picked but not yet applied (null: none).
  const [recordShares, setRecordShares] = React.useState<IAccessGrantRecord[] | null>(null);
  const [expiryDraft, setExpiryDraft] = React.useState<string | null>(null);
  const [expiryApplying, setExpiryApplying] = React.useState(false);
  // Task 099c: the record's one Expiration (owner 2026-09-10: it "applies to all sharing", and every add sends it).
  // The minimum mirrors the route (UTC today, today valid, no maximum); the server stays the authority.
  const shareExpiry = summarizeShareExpiry(recordShares);
  const expiryMin = utcDateOnly();
  const expiryDraftBeforeMin = expiryDraft !== null && expiryDraft !== '' && expiryDraft < expiryMin;
  const expiryDraftValid = expiryDraft !== null && /^\d{4}-\d{2}-\d{2}$/.test(expiryDraft) && !expiryDraftBeforeMin;
  // With no share yet, the field's own value is the record's date: an unusable one holds every add back.
  const addExpiryInvalid = shareExpiry.kind === 'noShares' && expiryDraft !== null && !expiryDraftValid;
  /**
   * THE ADD RULE (task 099c): every external add — contact, organization, suggestion — sends the record's APPLIED date.
   * With shares, that is their date (never an unapplied edit, so a new share cannot silently land off the record's
   * date); re-adding someone whose live grant ends later moves it to the record date (one date per record, owner
   * 2026-09-10; `/grant` writes an explicit date as given). With no share, it is the field's value (default
   * today + 90). Nothing is sent when the date is unknown (the grants read failed) or has lapsed (the route would
   * refuse it): the server then keeps a live grant's date and gives a new or lapsed one today + 90.
   */
  const addExpiryDate: string | undefined =
    shareExpiry.kind === 'shares'
      ? shareExpiry.earliest !== null && shareExpiry.earliest >= expiryMin
        ? shareExpiry.earliest
        : undefined
      : shareExpiry.kind === 'noShares'
        ? expiryDraft === null
          ? utcDateOnly(DEFAULT_SHARE_EXPIRY_DAYS)
          : expiryDraftValid
            ? expiryDraft
            : undefined
        : undefined;
  // Task 099c (review F1-c): an add with no share sent the field's date, and the refresh now shows that date as the
  // shares' own — the draft has been applied, so it is no longer pending. (A draft the shares did not take stays.)
  const shareExpiryKind = shareExpiry.kind;
  const shareExpiryEarliest = shareExpiry.kind === 'shares' ? shareExpiry.earliest : null;
  const prevShareExpiryKindRef = React.useRef(shareExpiryKind);
  React.useEffect(() => {
    const was = prevShareExpiryKindRef.current;
    prevShareExpiryKindRef.current = shareExpiryKind;
    if (
      was === 'noShares' &&
      shareExpiryKind === 'shares' &&
      expiryDraft !== null &&
      expiryDraft === shareExpiryEarliest
    ) {
      setExpiryDraft(null);
    }
  }, [shareExpiryKind, shareExpiryEarliest, expiryDraft]);
  // Items (contacts OR organizations) checked to grant, keyed by contactId/orgId.
  const [selectedCandidateIds, setSelectedCandidateIds] = React.useState<Set<string>>(new Set());
  const [approving, setApproving] = React.useState(false);
  // A native advanced-lookup pick is in flight — used only to disable the
  // "Add person"/"+ Organization" buttons so a double-click can't open two panes.
  const [picking, setPicking] = React.useState(false);

  // Per-row access level (task 073 v1.0.23) — keyed by item id (contactId/orgId/systemUserId).
  // NO default: a row is not grantable until the admin picks its level ("Pick access level").
  const [rowLevels, setRowLevels] = React.useState<Record<string, number | undefined>>({});
  // Contacts + organizations + users staged via the native "Add person" /
  // "+ Organization" advanced lookup — appended into the "Add Access
  // Permissions" list as selectable rows.
  const [lookedUpContacts, setLookedUpContacts] = React.useState<IContactSearchResult[]>([]);
  /** Contacts whose `/grant` was written but whose invite was not sent. Their grant stands, so Retry invites them through
   * `/invite` (which always sends) instead of granting again: a re-grant answers an invite whose marker was written before
   * the send failed as still pending, and sends nothing. */
  const [inviteRetryContactIds, setInviteRetryContactIds] = React.useState<ReadonlySet<string>>(new Set());
  const [lookedUpOrgs, setLookedUpOrgs] = React.useState<IOrganizationPick[]>([]);
  const [lookedUpUsers, setLookedUpUsers] = React.useState<IUserPick[]>([]);

  const [pendingRevoke, setPendingRevoke] = React.useState<PendingRevoke | null>(null);
  const [revoking, setRevoking] = React.useState(false);

  // Task 142: the record's Assigned-To ledger (suggestions on a secure record; provenance and residual read-time
  // access of automatic grants). `suggestionBusy` is the entry being granted or dismissed.
  const [assignedEntries, setAssignedEntries] = React.useState<IAssignedAccessEntry[]>([]);
  const [suggestionBusy, setSuggestionBusy] = React.useState<string | null>(null);

  const [notice, setNotice] = React.useState<IModalNotice | null>(null);

  // The designed 401/403 deny states (task 008 FR-07 delegation gate; project
  // constraint "the 403 delegation deny is a designed UI state, not a toast").
  // Persists across the SAME modal session (not per-call) so once the server
  // has said "no", every write action stays disabled until the modal is
  // reopened — reset alongside the rest of the transient state below.
  const [accessDenyState, setAccessDenyState] = React.useState<IAccessDeny | null>(null);

  // Task 175 (owner round 87): the record's direct filing parents, which set its minimum Secure and Access Permission.
  // Display only here: grants and shares are unaffected.
  const parents: IFollowsParent[] = followsParents ?? [];

  // Secure-record owner/BU read-only display (task 065, design.md §6).
  const [secureOwnerInfo, setSecureOwnerInfo] = React.useState<ISecureOwnerInfo | null>(null);

  /** Calls the host `authenticatedFetch` and returns the OK response. A non-OK
   * answer becomes {@link AccessGrantModalApiError} whichever way the fetch
   * delivers it: THROWN (`@spaarke/auth`'s authenticatedFetch — every host
   * today) or RETURNED (a non-throwing fetch). Anything else it throws (a
   * network failure) is rethrown untouched. */
  const requestOk = React.useCallback(
    async (path: string, init: RequestInit): Promise<Response> => {
      let res: Response;
      try {
        res = await authenticatedFetch(path, init);
      } catch (err) {
        throw AccessGrantModalApiError.fromThrown(err) ?? err;
      }
      if (!res.ok) throw await AccessGrantModalApiError.fromResponse(res);
      return res;
    },
    [authenticatedFetch]
  );

  // Task 067: the read-only No Access List (064's per-record read), and the contacts in Current Access that belong to
  // a walled organization (contact id → that organization's name). `orgWallCheck` is 'notChecked' when an
  // organization wall is in force but the memberships could not be read, so those rows are unmarked AND say so.
  const [noAccessState, setNoAccessState] = React.useState<NoAccessSectionState>({ kind: 'loading' });
  const [contactWalledOrgs, setContactWalledOrgs] = React.useState<Map<string, string>>(new Map());
  const [orgWallCheck, setOrgWallCheck] = React.useState<'notNeeded' | 'done' | 'notChecked'>('notNeeded');
  // Task 153: the section this open was asked to show (`initialSection`), armed on open and cleared once revealed. A
  // state, not a ref, so it is set in the same batch as this open's `loading` No Access state and can never act on the
  // previous open's list.
  const [sectionToReveal, setSectionToReveal] = React.useState<'noAccess' | null>(null);
  const noAccessSectionRef = React.useRef<HTMLElement>(null);

  // Verifier F4-a: the record the modal shows NOW (an in-flight answer is checked against it), and the number of the
  // latest load (an older load's results are dropped).
  const currentRecordIdRef = React.useRef(recordId);
  currentRecordIdRef.current = recordId;
  const loadSeqRef = React.useRef(0);
  // Unmounted: no load in flight may write state any more.
  React.useEffect(
    () => () => {
      loadSeqRef.current += 1;
    },
    []
  );
  // Verifier F4-c: a write handler runs in the closure of the render where the user clicked. When its request
  // finishes after the host rebound the modal to another record, its reload and its notice belong to a record that is
  // no longer shown, and must not reach the screen.
  const showsThisRecord = React.useCallback(
    () => cleanGuid(recordId) === cleanGuid(currentRecordIdRef.current),
    [recordId]
  );
  const setNoticeIfCurrent = React.useCallback(
    (n: IModalNotice | null) => {
      if (showsThisRecord()) setNotice(n);
    },
    [showsThisRecord]
  );
  const setDenyIfCurrent = React.useCallback(
    (deny: IAccessDeny) => {
      if (showsThisRecord()) setAccessDenyState(deny);
    },
    [showsThisRecord]
  );

  /** GETs a relative BFF path via the host `authenticatedFetch` and returns
   * the parsed JSON body. Throws {@link AccessGrantModalApiError} on a non-OK
   * response (task-024 finding M8) instead of reading the failure body as a
   * success. */
  const getJson = React.useCallback(
    async <T,>(path: string): Promise<T> => {
      const res = await requestOk(path, { method: 'GET' });
      return (await res.json()) as T;
    },
    [requestOk]
  );

  /** Reads this record's internal system-user shares (task 063/065, FR-29) —
   * a direct BFF call (not host-injected, unlike fetchCandidates/
   * fetchExistingGrants): `GET /user-shares` is already entity-agnostic given
   * {recordType, recordId}, the same shape every write on this modal already
   * sends, so no new TrackingFieldTrio wiring is needed to read it. Mapped
   * into {@link IAccessGrantRecord} shape with `provenance: 'share'` so it
   * merges into the SAME Current Access list, where it is labeled and
   * revocable (and marked walled off when a user wall is in force, task
   * 067). The Dataverse `contactId` field is reused to carry the
   * systemUserId for row-keying — the same convention this file already uses
   * for organization-grant rows (see fetchExistingGrants' `isOrgGrant`
   * comment) — since a share has no contact at all.
   *
   * This read is ALSO behind the delegation gate (task 063: "the group is a
   * closed access-management surface — mutations, plus one read... which
   * discloses who can reach a record"), so a caller without Write on this
   * record sees the deny banner as soon as the modal opens, before they ever
   * attempt a write — a stricter, earlier surfacing of the same designed
   * state the write paths hit, never a silent failure. */
  const fetchUserShares = React.useCallback(async (): Promise<IAccessGrantRecord[]> => {
    const query = `recordType=${encodeURIComponent(recordType)}&recordId=${encodeURIComponent(recordId)}`;
    const data = await getJson<{
      // Untrusted server JSON: a response without `shares` reads as an empty list (see the `?? []` below).
      shares?: Array<{
        systemUserId: string;
        fullName?: string | null;
        accessLevel?: number | null;
        modifiedOn?: string;
        externalNoAccess?: boolean;
        // Task 175: the secure parent that passed this share on (task 158's provenance); absent/null = a direct share.
        inheritedFrom?: { recordType?: unknown; recordId?: unknown } | null;
      }>;
    }>(`/api/v1/external-access/user-shares?${query}`);
    return (data.shares ?? []).map(s => {
      const from = s.inheritedFrom;
      // Any inheritedFrom object makes the row read-only (inherited), even one whose fields are off-contract: a share
      // the parent owns is never offered for revoke here.
      const inheritedFrom =
        from && typeof from === 'object'
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
        // the "Custom" display below rather than rendering a raw `null`.
        accessLevel: s.accessLevel ?? 0,
        grantedDate: s.modifiedOn,
        provenance: inheritedFrom ? 'inherited' : 'share',
        // Task 114: Restricted record + user flagged external — shown as "External user — no access" until removed.
        externalNoAccess: s.externalNoAccess === true,
      };
      if (inheritedFrom) row.inheritedFrom = inheritedFrom;
      return row;
    });
  }, [getJson, recordType, recordId]);

  /** Reads the record's Assigned-To ledger (task 142) — a direct, entity-agnostic BFF call like `/user-shares`, behind
   * the same delegation gate. Fails soft to an empty list: suggestions and provenance are a convenience; every grant is
   * still visible in Current Access. */
  const fetchAssignedAccess = React.useCallback(async (): Promise<IAssignedAccessEntry[]> => {
    const query = `recordType=${encodeURIComponent(recordType)}&recordId=${encodeURIComponent(recordId)}`;
    const data = await getJson<{
      entries?: Array<Omit<IAssignedAccessEntry, 'residualAccessTerms'> & { residualAccessTerms?: string[] | null }>;
    }>(`/api/v1/external-access/assigned-access?${query}`);
    return (data.entries ?? []).map(e => ({ ...e, residualAccessTerms: e.residualAccessTerms ?? [] }));
  }, [getJson, recordType, recordId]);

  /** Reads the record's No Access entries (task 067; task 064's route, frozen contract
   * `notes/phase4-access-report-contract.md`). Never throws: any answer it cannot trust — a non-200 (including the
   * route's uniform 404), a network failure, an unparseable body, another record's answer — is the section's error
   * state, never an empty list. Not behind the delegation gate (the route has its own Read/Write tiers), so a failure
   * here never sets the Write-required banner. */
  const fetchNoAccess = React.useCallback(async (): Promise<{
    section: NoAccessSectionState;
    access: IEffectiveRecordAccess | null;
  }> => {
    try {
      const res = await authenticatedFetch(buildNoAccessPath(recordType, recordId), { method: 'GET' });
      if (res.status !== 200) return { section: { kind: 'error' }, access: null };
      // The echo is checked against the record shown NOW, not the one this request was sent for: the modal stays
      // mounted while the form rebinds, so an answer for the previous record must never be accepted.
      const body: unknown = await res.json();
      return {
        section: parseNoAccessResponse(body, currentRecordIdRef.current),
        // Task 174: the same answer carries the record's effective access, for Read and Write callers alike.
        access: parseEffectiveAccess(body, currentRecordIdRef.current),
      };
    } catch {
      return { section: { kind: 'error' }, access: null };
    }
  }, [authenticatedFetch, recordType, recordId]);

  const loadData = React.useCallback(async () => {
    // Task 067 (verifier F4-a): loads can overlap — the modal stays mounted while the host form rebinds to another
    // record, and every grant/revoke reloads. Only the LATEST load may write state; an older one that resolves later
    // is dropped whole (grants, shares, candidates and the No Access List alike), and cannot clear `loading` while the
    // newer one runs.
    // Verifier F4-c: a reload from a stale closure (a write that finished after the host rebound the modal) would read
    // the OLD record's shares, suggestions and No Access List. It must not even take a load number.
    if (!showsThisRecord()) return;
    const seq = ++loadSeqRef.current;
    const isCurrent = () => seq === loadSeqRef.current;
    setLoading(true);
    setNotice(null);
    try {
      const [candidateList, grantsSettled, standingList, userShareList, ownerInfo, assignedList, noAccessRead] =
        await Promise.all([
          fetchCandidates(),
          // Task 099a (verifier pass 2, F1): the grants read settles on its own, like /user-shares — its failure is
          // Current Access's own state, and every other section still loads.
          fetchExistingGrants().then(
            read => ({ read, failed: null as { err: unknown } | null }),
            (err: unknown) => ({ read: null, failed: { err } })
          ),
          // Standing-grant members (task 073 UAT #2) — optional; a host that
          // hasn't wired the flag omits it. Failing soft so a standing-read
          // problem (e.g. field-level-security denial) never blocks the modal.
          fetchStandingContacts ? fetchStandingContacts().catch(() => [] as IAccessGrantRecord[]) : Promise.resolve([]),
          // Internal system-user shares (task 065). A delegation 403 here is a
          // designed state (see fetchUserShares' doc comment above), not a
          // load failure — recorded via accessDenyState and the read fails
          // soft to an empty list so the rest of the modal still loads.
          fetchUserShares().catch(err => {
            const { deny } = splitAccessFailure(err);
            if (deny && isCurrent()) setAccessDenyState(deny);
            return [] as IAccessGrantRecord[];
          }),
          // Secure-record owner/BU (task 065, design.md §6) — optional; a host
          // that hasn't wired it, or a non-secure record, resolves to null.
          // Fails soft: this is a read-only display, never a blocking concern.
          fetchSecureOwnerInfo ? fetchSecureOwnerInfo().catch(() => null) : Promise.resolve(null),
          // Task 142: Assigned-To suggestions + provenance. Fails soft (a convenience, never blocking).
          fetchAssignedAccess().catch(() => [] as IAssignedAccessEntry[]),
          // Task 067: the No Access List. Never rejects (its failure is the section's own error state).
          fetchNoAccess(),
        ]);
      if (!isCurrent()) return;
      // Task 099a: a capped source says so (`{ grants, truncated }`); a plain array is the whole list.
      const grantsRead = grantsSettled.read;
      const grantList = grantsRead === null ? [] : Array.isArray(grantsRead) ? grantsRead : grantsRead.grants;
      const noAccess = noAccessRead.section;
      // Task 174: the effective access the gate and the "No effect" marks use (null: the host's values alone).
      setServerAccess(noAccessRead.access);
      // Union standing + user-share rows into Current Access, deduped by
      // contactId — an explicit per-record `sprk_externalrecordaccess` grant
      // (which carries an accessRecordId and IS revocable) wins over a
      // standing row for the same contact, so a contact with both shows once
      // and stays revocable. User-share rows key on a DIFFERENT id space
      // (systemUserId, reusing the `contactId` field per the doc comment
      // above) so they never collide with contact-keyed rows.
      // Task 099a (verifier F1): only a grant that still CONFERS hides the contact's standing row. A lapsed grant confers
      // nothing, while the standing row still does (the server's union, AccessibleRecordSetService.ComposeForContactAsync),
      // so both are listed — the lapsed one marked Expired, the standing one as live access.
      const liveGrantContactIds = new Set(grantList.filter(g => !g.isExpired).map(g => g.contactId));
      const standingOnly = standingList.filter(s => !liveGrantContactIds.has(s.contactId));
      // Task 099a: a failed grants read lists nothing — Current Access shows its own state instead (never a partial
      // list without the record's grants, never "No active grants"). A refusal by the delegation/sign-in rule is the
      // deny banner's state (every write blocked, as for /user-shares); any other failure is the section's own error,
      // with the server's ProblemDetails `detail` verbatim (rendered as text) or the generic sentence.
      const currentAccess = grantsSettled.failed ? [] : [...grantList, ...standingOnly, ...userShareList];
      setExistingGrants(currentAccess);
      // Task 099c: the Expiration reads the shares themselves; a failed read leaves it unknown, never "no shares".
      setRecordShares(grantsSettled.failed ? null : grantList);
      if (grantsSettled.failed) {
        const grantsErr = grantsSettled.failed.err;
        const grantsDeny = splitAccessFailure(AccessGrantModalApiError.fromThrown(grantsErr) ?? grantsErr).deny;
        if (grantsDeny) setAccessDenyState(grantsDeny);
        const detail: unknown = problemOf(grantsErr)?.detail;
        setCurrentAccessError(
          grantsDeny
            ? { kind: 'notShown' }
            : {
                kind: 'failed',
                text:
                  typeof detail === 'string' && detail.trim() !== ''
                    ? detail.trim()
                    : CURRENT_ACCESS_READ_FAILED_MESSAGE,
              }
        );
      } else {
        setCurrentAccessError(null);
      }
      setGrantsTruncatedAt(
        grantsRead !== null && !Array.isArray(grantsRead) && grantsRead.truncated === true ? grantList.length : null
      );

      // Task 067: an organization wall in force walls off the organization's people, so the contacts in Current Access
      // are checked against it — only when such a wall exists and there are contacts to check.
      const walledOrgs = new Map<string, string>();
      let orgCheck: 'notNeeded' | 'done' | 'notChecked' = 'notNeeded';
      if (noAccess.kind === 'list') {
        const walls = buildVetoIndex(noAccess.entries);
        const contactIds = contactIdsToCheck(currentAccess);
        if (walls.organizations.size > 0 && contactIds.length > 0) {
          orgCheck = 'notChecked';
          if (fetchContactOrganizationMemberships) {
            try {
              const memberships = await fetchContactOrganizationMemberships(
                contactIds,
                Array.from(walls.organizations.keys())
              );
              if (!isCurrent()) return;
              for (const m of memberships) {
                const orgName = walls.organizations.get(cleanGuid(m.organizationId));
                if (orgName) walledOrgs.set(cleanGuid(m.contactId), orgName);
              }
              orgCheck = 'done';
            } catch {
              // Unmarked rows plus a visible "could not be checked" note — never a silent "not walled".
              if (!isCurrent()) return;
            }
          }
        }
      }
      setNoAccessState(noAccess);
      setContactWalledOrgs(walledOrgs);
      setOrgWallCheck(orgCheck);
      // Exclude both explicitly-granted AND standing members from the
      // candidate-approve list (they already have access).
      // Task 099a: a LAPSED grant's contact is excluded too — they are listed once, in Current Access, as Expired and
      // revocable. Re-adding them (the contact lookup) restores that same row on the server (owner round 80 / task 113),
      // so they are not offered as a fresh candidate beside it.
      const currentAccessContactIds = new Set([
        ...grantList.map(g => g.contactId),
        ...standingOnly.map(s => s.contactId),
      ]);
      // Task 142: a contact the server already SUGGESTS (a secure record) is offered once — in Suggested Access,
      // with Grant / Dismiss — not a second time as a role-based candidate.
      const suggestedContactIds = new Set(
        assignedList.filter(e => e.state === 'PendingConfirmation').map(e => e.subjectId)
      );
      setCandidates(
        candidateList.filter(c => !currentAccessContactIds.has(c.contactId) && !suggestedContactIds.has(c.contactId))
      );
      setAssignedEntries(assignedList);
      setSecureOwnerInfo(ownerInfo);
    } catch {
      if (!isCurrent()) return;
      // A read other than the grants read failed (the grants read never rejects here): the generic notice says so, and
      // Current Access — not shown, never "No active grants" — does not add a second error of its own (task 099a,
      // verifier pass 2).
      setNotice({ intent: 'error', text: 'Failed to load access data. Close and reopen to retry.' });
      setCurrentAccessError({ kind: 'notShown' });
      setGrantsTruncatedAt(null);
      setRecordShares(null);
      // Never leave the No Access List spinning, or showing the previous load's rows as current.
      setNoAccessState({ kind: 'error' });
      setServerAccess(null);
      setContactWalledOrgs(new Map());
      setOrgWallCheck('notNeeded');
    } finally {
      if (isCurrent()) setLoading(false);
    }
  }, [
    fetchCandidates,
    fetchExistingGrants,
    fetchStandingContacts,
    fetchUserShares,
    fetchSecureOwnerInfo,
    fetchAssignedAccess,
    fetchNoAccess,
    fetchContactOrganizationMemberships,
    showsThisRecord,
  ]);

  React.useEffect(() => {
    if (open && canGrantAccess) {
      setSelectedCandidateIds(new Set());
      setRowLevels({});
      setLookedUpContacts([]);
      setInviteRetryContactIds(new Set());
      setLookedUpOrgs([]);
      setLookedUpUsers([]);
      setPendingRevoke(null);
      // A prior session's deny/notice is not this session's — a fresh open
      // gets a fresh chance to load and write (server truth only; no
      // client-side memory of "you were denied last time").
      setAccessDenyState(null);
      // Task 099c: nor the previous record's shares or an unapplied Expiration.
      setRecordShares(null);
      setExpiryDraft(null);
      // Task 067: a fresh open never shows the previous record's or session's No Access answer.
      setNoAccessState({ kind: 'loading' });
      // Task 174: nor the previous record's effective access.
      setServerAccess(null);
      setContactWalledOrgs(new Map());
      setOrgWallCheck('notNeeded');
      setSectionToReveal(initialSection === 'noAccess' ? 'noAccess' : null);
      void loadData();
    } else {
      // Closed (or no longer permitted): any load still in flight belongs to a session that has ended.
      loadSeqRef.current += 1;
    }
    // Only re-run when the modal transitions open (and once per open), not on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, canGrantAccess]);

  // Task 153: once this open's load has finished, bring the requested section into view (and focus it, so keyboard
  // and screen-reader users land there too). Only once per open; a hidden section (no Write) is simply not revealed.
  React.useEffect(() => {
    if (!open || sectionToReveal !== 'noAccess' || loading || noAccessState.kind === 'loading') return;
    setSectionToReveal(null);
    const section = noAccessSectionRef.current;
    if (!section) return;
    if (typeof section.scrollIntoView === 'function') section.scrollIntoView({ block: 'start' });
    if (typeof section.focus === 'function') section.focus({ preventScroll: true });
  }, [open, sectionToReveal, loading, noAccessState.kind]);

  /** Posts a JSON body to a relative BFF path via the host `authenticatedFetch`
   * and returns the parsed response body. Throws {@link AccessGrantModalApiError}
   * on a non-OK response (task-024 finding M8) — callers wrap with a try/catch
   * that classifies the failure (see {@link classifyAccessFailure}) and surfaces
   * either the designed deny banner or a non-blocking notice. */
  const postJson = React.useCallback(
    async <T,>(path: string, body: unknown): Promise<T> => {
      const res = await requestOk(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      return (await res.json()) as T;
    },
    [requestOk]
  );

  /** Outcome of a single grant or share write — the write itself either succeeds or throws; the flags describe what
   * the server reported alongside it, so callers can build one combined notice. */
  interface IGrantOutcome {
    /** Task 181: the server could not send the person the in-app notification (best effort; the grant stands). */
    notificationFailed: boolean;
    /** Task 139: the server capped the grant at the caller's own level. */
    narrowed: boolean;
    /** Slice B3: the person's invite is for a work account (they sign in with it; no portal account). */
    workAccountInvited?: boolean;
    /** `/grant`: the grant was written but its automatic invite did not complete (`invite.status` Failed); `errorCode`
     * is the server's reason. Absent when the invite was sent, not needed, or not reported. */
    inviteFailed?: { errorCode?: string };
    /** Task 193: `/grant` shared with the colleague the contact represents (no grant row). */
    sharedWith?: ISharedWithColleague;
  }

  /** The additive fields `/grant` and `/share-user` return (task 139; task 181's `notificationFailed`). */
  interface IGrantWriteResponseBody {
    grantedAccessLevel?: number | null;
    narrowed?: boolean;
    notificationFailed?: boolean;
    /** `/grant`: what the grant did about the person's invite (slice B3: `WorkAccountInvited`). */
    invite?: { status?: string; errorCode?: string | null } | null;
    /** `/grant` (task 193, #2338): the contact represents exactly one internal colleague, so the record was SHARED with
     * that user and no grant row was written. `fullName` is `null` when the server could not read it. */
    sharedWithUser?: { systemUserId: string; fullName?: string | null } | null;
    /** `/grant` (task 193): the answer is a share and the add carried an Expiration, which a share does not take. */
    expiryNotApplied?: boolean;
  }

  /**
   * The single contact write shared by candidate-approve and named-add (per the task's "no duplicate write path"
   * constraint). Task 193 (#2338): every contact goes to `/grant` and the SERVER decides — it shares with the one
   * colleague the contact represents (`sharedWithUser`, no grant row) or grants the contact, inviting an unbound one
   * with an email. The modal never classifies a contact. Throws only when the write itself fails; what the server
   * reported with it is returned so a caller driving multiple grants (approve-selected) can aggregate one notice
   * instead of each write overwriting the last.
   */
  const grantContact = React.useCallback(
    async (contact: { contactId: string }, opts: { level: number }): Promise<IGrantOutcome> => {
      const data = await postJson<IGrantWriteResponseBody>('/api/v1/external-access/grant', {
        contactId: contact.contactId,
        recordType,
        recordId,
        accessLevel: opts.level,
        // Task 099c: the record's applied Expiration rides on the add (see addExpiryDate). A share does not take it, and
        // the server then says so (expiryNotApplied).
        ...(addExpiryDate ? { expiryDate: addExpiryDate } : {}),
      });

      // Task 139: every grant is capped at the caller's own level; the server
      // says when it did, and the batch notice reports it.
      return {
        notificationFailed: data.notificationFailed === true,
        narrowed: data.narrowed === true,
        workAccountInvited: data.invite?.status === WORK_ACCOUNT_INVITED_STATUS,
        // The grant was written but its automatic invite did not complete: the not-sent notice, and Retry through
        // `/invite`.
        inviteFailed:
          data.invite?.status === GRANT_INVITE_FAILED_STATUS
            ? { errorCode: data.invite.errorCode ?? undefined }
            : undefined,
        sharedWith: data.sharedWithUser
          ? {
              name: data.sharedWithUser.fullName || COLLEAGUE_FALLBACK_NAME,
              expiryNotApplied: data.expiryNotApplied === true,
            }
          : undefined,
      };
    },
    [postJson, recordId, recordType, addExpiryDate]
  );

  /**
   * Retry for a person whose grant stands but whose invite was not sent: `/invite` (onboard only; it writes no grant),
   * which re-runs the organisation decision and sends. Returns whether the invite was sent; throws when the request
   * fails. Slice C2b: sent only when `emailSent` is `true`; `false`, `null` or absent is not sent. A person who already
   * has an account is reported with the server's text and sign-in link (`alreadyHasAccount`).
   */
  const inviteGrantedContact = React.useCallback(
    async (
      contact: { email: string },
      level: number
    ): Promise<{
      sent: boolean;
      workAccountInvited: boolean;
      alreadyHasAccount?: { message: string; signInUrl?: string };
    }> => {
      const data = await postJson<{
        status?: string;
        emailSent?: boolean | null;
        emailErrorCode?: string | null;
        staffMessage?: string | null;
        signInUrl?: string | null;
      }>('/api/v1/external-access/invite', {
        email: contact.email,
        recordType,
        recordId,
        accessLevel: level,
      });
      const sent = data.emailSent === true;
      const alreadyHasAccount =
        !sent && data.emailErrorCode === INVITE_CONTACT_ALREADY_HAS_ACCOUNT_REASON_CODE
          ? {
              message: data.staffMessage || ALREADY_HAS_ACCOUNT_FALLBACK_SENTENCE,
              signInUrl: data.signInUrl || undefined,
            }
          : undefined;
      // C5: a work-account invite whose notice was not sent is not "invited"; it stays staged for Retry.
      return {
        sent,
        workAccountInvited: data.status === WORK_ACCOUNT_INVITED_STATUS && data.emailSent !== false,
        alreadyHasAccount,
      };
    },
    [postJson, recordId, recordType]
  );

  // Unified "Available Contacts & Organizations & Users" list (task 073 v1.0.23; task
  // 065 adds the 'user' kind): role-based membership candidates + any contacts/
  // organizations/users staged via the "Add person" / "+ Organization" side pane. Each item carries its own per-row access level. Granted contacts are
  // excluded by loadData() (user shares are not — a user with no share is simply
  // absent from Current Access, so nothing to exclude here).
  interface IAvailableItem {
    id: string; // contactId (contact) or organizationId (organization) or systemUserId (user)
    name: string;
    meta: string;
    kind: 'contact' | 'organization' | 'user';
    contact?: { contactId: string; fullName: string; email?: string };
    org?: IOrganizationPick;
    user?: IUserPick;
  }
  const availableItems = React.useMemo<IAvailableItem[]>(() => {
    const items: IAvailableItem[] = [];
    // Task 138: a Restricted record offers no contact rows at all (candidates or looked-up),
    // and a Limited/Secure record no organization rows — the server would refuse them.
    if (contactGrantsOffered) {
      const byId = new Map<string, IAccessGrantCandidate>();
      for (const c of candidates) byId.set(c.contactId, c);
      for (const c of lookedUpContacts) {
        if (!byId.has(c.contactId)) {
          byId.set(c.contactId, { contactId: c.contactId, fullName: c.fullName, email: c.email, role: 'Looked up' });
        }
      }
      for (const c of byId.values()) {
        items.push({
          id: c.contactId,
          name: c.fullName,
          meta: `${c.role}${c.email ? ` · ${c.email}` : ''}`,
          kind: 'contact',
          contact: { contactId: c.contactId, fullName: c.fullName, email: c.email },
        });
      }
    }
    if (organizationGrantsOffered) {
      for (const o of lookedUpOrgs) {
        items.push({ id: o.id, name: o.name, meta: 'All organization contacts', kind: 'organization', org: o });
      }
    }
    for (const u of lookedUpUsers) {
      items.push({
        id: u.id,
        name: u.name,
        // Neutral: a user flagged external can be picked too (allowed on a non-Restricted record, round 78).
        meta: u.email ? `System user · ${u.email}` : 'System user',
        kind: 'user',
        user: u,
      });
    }
    return items;
  }, [candidates, lookedUpContacts, lookedUpOrgs, lookedUpUsers, contactGrantsOffered, organizationGrantsOffered]);

  // Only rows still offered count toward "Add (N)" — a row hidden by the Access-Permission
  // gate (task 138) is never granted, so it must not be counted or enable Add.
  const selectedOfferedCount = availableItems.filter(it => selectedCandidateIds.has(it.id)).length;

  /** Writes a first-class ORGANIZATION grant (task 073 #7) — access for all contacts at the org.
   * `contactId` is omitted so the BFF treats (empty contact + organizationId) as an org grant; every
   * active member of the organization then inherits access at check time (server Term-3 union). */
  const grantOrganization = React.useCallback(
    async (org: IOrganizationPick, level: number): Promise<{ narrowed: boolean }> => {
      const data = await postJson<IGrantWriteResponseBody | null>('/api/v1/external-access/grant', {
        recordType,
        recordId,
        accessLevel: level,
        organizationId: org.id,
        // Task 099c: the record's applied Expiration rides on the add (see addExpiryDate).
        ...(addExpiryDate ? { expiryDate: addExpiryDate } : {}),
      });
      // Task 139: an organization-wide grant is capped at the caller's level too.
      return { narrowed: data?.narrowed === true };
    },
    [postJson, recordType, recordId, addExpiryDate]
  );

  /** Writes an internal system-user POA SHARE (task 065, FR-29) via the task-063
   * endpoint — NOT the `/grant` core the contact/org flows above use (a share is a
   * different write; see the module doc comment). `narrowed: true` means the
   * caller's own rights on the record were narrower than the requested level, so
   * the share carries the intersection (owner decision 2026-09-16) — reported back
   * so the caller can build one combined notice, same pattern as grantContact's
   * outcome (task 181: with `notificationFailed`). */
  const shareUser = React.useCallback(
    async (user: IUserPick, level: number): Promise<IGrantOutcome> => {
      const data = await postJson<IGrantWriteResponseBody>('/api/v1/external-access/share-user', {
        recordType,
        recordId,
        systemUserId: user.id,
        accessLevel: level,
      });
      return { narrowed: data.narrowed === true, notificationFailed: data.notificationFailed === true };
    },
    [postJson, recordType, recordId]
  );

  // Commits the checked rows (the "Add (N)" action). Returns `true` only when every
  // selected row was granted successfully — Save uses this to decide whether to close
  // (task 073 UAT v1.0.29 #1B). Returns `false` (staying open) when a level is missing
  // or any grant failed, so the notice explains what to fix.
  const handleGrantSelected = React.useCallback(async (): Promise<boolean> => {
    const selected = availableItems.filter(it => selectedCandidateIds.has(it.id));
    if (selected.length === 0) return true;
    // Task 099c: contacts and organizations carry the record's Expiration; an unusable one is corrected first.
    if (addExpiryInvalid && selected.some(it => it.kind !== 'user')) {
      setNoticeIfCurrent({ intent: 'warning', text: addExpiryInvalidMessage() });
      return false;
    }
    // Every selected row must have a level (no default) before it can be granted.
    const missing = selected.filter(it => rowLevels[it.id] === undefined);
    if (missing.length > 0) {
      setNoticeIfCurrent({
        intent: 'warning',
        text: `Pick an access level for: ${missing.map(m => m.name).join(', ')}.`,
      });
      return false;
    }
    setApproving(true);
    let failures = 0;
    let granted = 0;
    let anyNotificationFailed = false;
    let anyNarrowed = false;
    let denied = false;
    const policyRefusals: string[] = [];
    const relatedRecordsPending: string[] = [];
    const workAccountInvited: string[] = [];
    // `/grant` wrote the grant but its automatic invite did not complete: reported with Retry, kept selected.
    const inviteNotSent: IAvailableItem[] = [];
    const organisationUnchecked: string[] = [];
    // Slice C2b: `/invite` sent nothing because the person already has an account; kept selected with the server's text.
    const alreadyHasAccount: { item: IAvailableItem; message: string; signInUrl?: string }[] = [];
    // Task 193: contacts the server answered by sharing with the colleague they represent.
    const sharedWithColleagues: ISharedWithColleague[] = [];
    for (const it of selected) {
      const level = rowLevels[it.id];
      // Unreachable: the missing-level guard above returned before any write. Narrows `level` for the writes below.
      if (level === undefined) continue;
      // The person's grant stands and only their invite is retried (inviteRetryContactIds).
      const inviteRetryEmail =
        it.kind === 'contact' && it.contact && inviteRetryContactIds.has(it.contact.contactId)
          ? it.contact.email
          : undefined;
      try {
        if (inviteRetryEmail) {
          const invited = await inviteGrantedContact({ email: inviteRetryEmail }, level);
          if (invited.workAccountInvited) workAccountInvited.push(it.name);
          if (invited.alreadyHasAccount) alreadyHasAccount.push({ item: it, ...invited.alreadyHasAccount });
          else if (!invited.sent) inviteNotSent.push(it);
        } else if (it.kind === 'contact' && it.contact) {
          const outcome = await grantContact(it.contact, { level });
          anyNotificationFailed = anyNotificationFailed || outcome.notificationFailed;
          anyNarrowed = anyNarrowed || outcome.narrowed;
          if (outcome.workAccountInvited) workAccountInvited.push(it.name);
          if (outcome.sharedWith) sharedWithColleagues.push(outcome.sharedWith);
          if (outcome.inviteFailed) {
            inviteNotSent.push(it);
            if (outcome.inviteFailed.errorCode === INVITE_TENANT_LOOKUP_UNAVAILABLE_REASON_CODE) {
              organisationUnchecked.push(it.name);
            }
          }
        } else if (it.kind === 'organization' && it.org) {
          const outcome = await grantOrganization(it.org, level);
          anyNarrowed = anyNarrowed || outcome.narrowed;
        } else if (it.kind === 'user' && it.user) {
          const outcome = await shareUser(it.user, level);
          anyNarrowed = anyNarrowed || outcome.narrowed;
          anyNotificationFailed = anyNotificationFailed || outcome.notificationFailed;
        }
        granted += 1;
      } catch (err) {
        // The delegation deny (project constraint: "a designed UI state ...
        // scope: all three write actions") is NOT counted as an ordinary
        // failure — it stops the batch and renders the persistent banner
        // instead of "N failed, try again" (retrying without Write would
        // just fail the same way for every remaining item).
        const { deny, parentRefusal } = splitAccessFailure(err);
        if (deny) {
          setDenyIfCurrent(deny);
          denied = true;
          break;
        }
        // Task 175: this item would make the record looser than its parent's floor. A refusal of this item only.
        if (parentRefusal) {
          policyRefusals.push(`${it.name}: ${parentRefusal}`);
          continue;
        }
        // An invite retry that did not send: the grant still stands, so the person stays staged with the same notice.
        if (inviteRetryEmail) {
          inviteNotSent.push(it);
          if (
            err instanceof AccessGrantModalApiError &&
            err.reasonCode === INVITE_TENANT_LOOKUP_UNAVAILABLE_REASON_CODE
          ) {
            organisationUnchecked.push(it.name);
          }
          continue;
        }
        // Task 149: the share on the record WAS written; only some related records of the secure record are not yet
        // updated. Counted as granted, with the server's sentence kept for the notice.
        const pendingDetail = childrenIncompleteDetail(err);
        if (pendingDetail) {
          granted += 1;
          relatedRecordsPending.push(pendingDetail);
          // Task 181: that response also says whether the person could not be notified.
          anyNotificationFailed =
            anyNotificationFailed || (err instanceof AccessGrantModalApiError && err.notificationFailed === true);
          continue;
        }
        // Task 138: a refusal by the record's access policy carries the
        // server's own explanation — kept, and shown instead of a generic error.
        // Task 193: a contact refused because of who it represents is named, as a user is.
        const refusal =
          grantPolicyRefusalDetail(err) ??
          (it.kind === 'user' && it.user ? userShareRefusalDetail(err, it.user) : null) ??
          (it.kind === 'contact' && it.contact ? contactShareRefusalDetail(err, it.name) : null);
        if (refusal) {
          policyRefusals.push(refusal);
          continue;
        }
        failures += 1;
      }
    }

    setApproving(false);
    // Rebound to another record meanwhile: its staged picks, list and notice are not this batch's to touch.
    if (!showsThisRecord()) return false;
    // A granted person whose invite was not sent is no longer a candidate once Current Access lists them, so they are
    // kept as a looked-up contact for Retry.
    // Slice C2b: a person who already has an account stays staged the same way, with the notice's text and link.
    const keptInvitees = [...inviteNotSent, ...alreadyHasAccount.map(a => a.item)];
    const retryIds = new Set(keptInvitees.map(it => it.id));
    setInviteRetryContactIds(new Set(keptInvitees.flatMap(it => (it.contact?.email ? [it.contact.contactId] : []))));
    setSelectedCandidateIds(new Set(retryIds));
    setRowLevels(Object.fromEntries(Object.entries(rowLevels).filter(([id]) => retryIds.has(id))));
    setLookedUpContacts(prev => {
      const kept = prev.filter(c => retryIds.has(c.contactId));
      const keptIds = new Set(kept.map(c => c.contactId));
      for (const it of keptInvitees) {
        if (it.contact && !keptIds.has(it.contact.contactId)) kept.push(it.contact);
      }
      return kept;
    });
    setLookedUpOrgs([]);
    setLookedUpUsers([]);
    await loadData();

    setNoticeIfCurrent(
      buildGrantBatchNotice({
        granted,
        selectedCount: selected.length,
        failures,
        denied,
        anyNotificationFailed,
        anyNarrowed,
        policyRefusals,
        relatedRecordsPending,
        workAccountInvited,
        inviteNotSent: { names: inviteNotSent.map(it => it.name), organisationUnchecked },
        alreadyHasAccount: alreadyHasAccount.map(a => ({
          name: a.item.name,
          message: a.message,
          signInUrl: a.signInUrl,
        })),
        sharedWithColleagues,
      })
    );
    // Success (for Save's close decision) iff nothing failed, nothing was refused
    // and access was not denied partway — a narrowed grant still
    // succeeded (the access row/share was written). A share whose related records are
    // still pending (task 149) also succeeded, but Save keeps the modal open once so the
    // warning is read: it can name related records an administrator must repair. Task 181:
    // likewise a grant whose person could not be notified — the granter is asked to share
    // the link. Nothing is staged any more, so the next Save closes it.
    return (
      !denied &&
      failures === 0 &&
      policyRefusals.length === 0 &&
      relatedRecordsPending.length === 0 &&
      inviteNotSent.length === 0 &&
      alreadyHasAccount.length === 0 &&
      !anyNotificationFailed
    );
  }, [
    availableItems,
    selectedCandidateIds,
    rowLevels,
    grantContact,
    inviteGrantedContact,
    inviteRetryContactIds,
    grantOrganization,
    shareUser,
    loadData,
    showsThisRecord,
    setNoticeIfCurrent,
    setDenyIfCurrent,
    addExpiryInvalid,
  ]);

  // Save (task 073 UAT v1.0.29 #1B): if rows are staged but not yet added, COMMIT
  // them (respecting the level-required guard), then close on success; otherwise
  // just close. So the user's pending selection isn't silently lost on Save.
  const handleSave = React.useCallback(async () => {
    if (selectedOfferedCount === 0) {
      onClose();
      return;
    }
    const ok = await handleGrantSelected();
    if (ok) onClose();
  }, [selectedOfferedCount, handleGrantSelected, onClose]);

  // Cancel / × (task 073 UAT v1.0.29 #1B): warn if there are pending (staged,
  // not-yet-added) selections so they aren't silently discarded.
  const [showPendingWarning, setShowPendingWarning] = React.useState(false);
  const handleCancelAttempt = React.useCallback(() => {
    if (selectedOfferedCount > 0) setShowPendingWarning(true);
    else onClose();
  }, [selectedOfferedCount, onClose]);

  // ── Native advanced-lookup pickers (task 073 v1.0.24 #1/#4; task 193) ───────
  // "Add person" / "+ Organization" open the host's NATIVE Dataverse advanced-lookup
  // side pane (Xrm.Utility.lookupObjects, injected as pickPerson/pickOrganization)
  // — the same advanced-find surface the wizards use. The pick is staged into the
  // "Add Access Permissions" list and auto-selected. Works because the modal is
  // `nonBlocking` (no backdrop covering the page-level lookup pane).

  /** Task 193 (#2338): the ONE "Add person" lookup over contacts and system users. A picked contact is staged as a
   * contact (Add sends it to `/grant`, where the server decides between a share and a grant); a picked user as a user
   * (Add sends it to `/share-user`). On a Restricted record the lookup offers users only, and none flagged external
   * (task 114) — a Restricted record admits no contact-based access. */
  const openPersonPicker = React.useCallback(async () => {
    if (!pickPerson) return;
    setPicking(true);
    try {
      const picked = await pickPerson({ includeContacts: contactGrantsOffered, excludeExternal: isRestricted });
      if (!picked) return;
      if (picked.kind === 'contact') {
        const contact = picked.contact;
        setLookedUpContacts(prev => (prev.some(c => c.contactId === contact.contactId) ? prev : [...prev, contact]));
        setSelectedCandidateIds(prev => new Set(prev).add(contact.contactId));
      } else {
        const user = picked.user;
        setLookedUpUsers(prev => (prev.some(u => u.id === user.id) ? prev : [...prev, user]));
        setSelectedCandidateIds(prev => new Set(prev).add(user.id));
      }
    } finally {
      setPicking(false);
    }
  }, [pickPerson, contactGrantsOffered, isRestricted]);

  const openOrgPicker = React.useCallback(async () => {
    if (!pickOrganization) return;
    setPicking(true);
    try {
      const picked = await pickOrganization();
      if (!picked) return;
      setLookedUpOrgs(prev => (prev.some(o => o.id === picked.id) ? prev : [...prev, picked]));
      setSelectedCandidateIds(prev => new Set(prev).add(picked.id));
    } finally {
      setPicking(false);
    }
  }, [pickOrganization]);

  /** Task 142 (owner A3 = prompt): "Grant" on a suggestion writes through the NORMAL path — `/share-user` for a contact
   * that represents an internal user, `/grant` otherwise — at Collaborate (owner rule 5), and the server marks the entry
   * Adopted. Refusals show the server's own sentence. */
  const grantSuggestion = React.useCallback(
    async (entry: IAssignedAccessEntry) => {
      // Task 099c: an external grant carries the record's Expiration; an unusable one is corrected first.
      if (!entry.systemUserId && addExpiryInvalid) {
        setNoticeIfCurrent({ intent: 'warning', text: addExpiryInvalidMessage() });
        return;
      }
      setSuggestionBusy(entry.entryId);
      const name = entry.subjectName ?? 'This person';
      try {
        // Task 181: both routes report whether the person could not be notified.
        const data = entry.systemUserId
          ? await postJson<IGrantWriteResponseBody>('/api/v1/external-access/share-user', {
              recordType,
              recordId,
              systemUserId: entry.systemUserId,
              accessLevel: ASSIGNED_ACCESS_LEVEL,
            })
          : await postJson<IGrantWriteResponseBody>('/api/v1/external-access/grant', {
              contactId: entry.subjectId,
              accessLevel: ASSIGNED_ACCESS_LEVEL,
              recordType,
              recordId,
              // Task 099c: the record's applied Expiration rides on the add (see addExpiryDate).
              ...(addExpiryDate ? { expiryDate: addExpiryDate } : {}),
            });
        await loadData();
        const notificationFailed = data.notificationFailed === true;
        // Task 193: `/grant` may answer a contact with a share with the colleague the contact represents.
        const shared = data.sharedWithUser
          ? ` ${sharedWithColleagueSentence({
              name: data.sharedWithUser.fullName || COLLEAGUE_FALLBACK_NAME,
              expiryNotApplied: data.expiryNotApplied === true,
            })}`
          : '';
        setNoticeIfCurrent({
          intent: notificationFailed ? 'warning' : 'success',
          text:
            `Granted ${name} access (suggested from ${entry.sourceFieldLabel}).${shared}` +
            (notificationFailed ? ` ${NOTIFICATION_FAILED_SENTENCE}` : ''),
        });
      } catch (err) {
        const { deny, parentRefusal } = splitAccessFailure(err);
        // Task 149: the share WAS written; only some related records of the secure record are not updated yet.
        const pendingDetail = deny || parentRefusal ? null : childrenIncompleteDetail(err);
        if (deny) setDenyIfCurrent(deny);
        // Task 175: refused because it would make the record looser than its parent's floor.
        else if (parentRefusal) setNoticeIfCurrent({ intent: 'error', text: parentRefusal });
        else if (pendingDetail) {
          await loadData();
          const notificationFailed = err instanceof AccessGrantModalApiError && err.notificationFailed === true;
          setNoticeIfCurrent({
            intent: 'warning',
            text:
              `Granted ${name} access (suggested from ${entry.sourceFieldLabel}). ${pendingDetail}` +
              (notificationFailed ? ` ${NOTIFICATION_FAILED_SENTENCE}` : ''),
          });
        } else
          setNoticeIfCurrent({
            intent: 'error',
            text:
              (entry.systemUserId ? userShareRefusalDetail(err, { id: entry.systemUserId, name }) : null) ??
              (err instanceof AccessGrantModalApiError && err.detail
                ? err.detail
                : `Failed to grant ${name} access. Please try again.`),
          });
      } finally {
        setSuggestionBusy(null);
      }
    },
    [postJson, recordType, recordId, loadData, setNoticeIfCurrent, setDenyIfCurrent, addExpiryDate, addExpiryInvalid]
  );

  /** Task 142 (owner A3): "Dismiss" declines the suggestion — the Assigned-To rule will not suggest or grant it again
   * while the assignment persists; a manual grant still succeeds. */
  const dismissSuggestion = React.useCallback(
    async (entry: IAssignedAccessEntry) => {
      setSuggestionBusy(entry.entryId);
      const name = entry.subjectName ?? 'This person';
      try {
        await postJson('/api/v1/external-access/assigned-access/dismiss', {
          recordType,
          recordId,
          entryId: entry.entryId,
        });
        await loadData();
        setNoticeIfCurrent({
          intent: 'success',
          text: `Dismissed. ${name} will not be suggested again while they stay in ${entry.sourceFieldLabel}.`,
        });
      } catch (err) {
        const { deny, parentRefusal } = splitAccessFailure(err);
        if (deny) setDenyIfCurrent(deny);
        else if (parentRefusal) setNoticeIfCurrent({ intent: 'error', text: parentRefusal });
        else
          setNoticeIfCurrent({
            intent: 'error',
            text:
              err instanceof AccessGrantModalApiError && err.detail
                ? err.detail
                : `Failed to dismiss the suggestion for ${name}. Please try again.`,
          });
      } finally {
        setSuggestionBusy(null);
      }
    },
    [postJson, recordType, recordId, loadData, setNoticeIfCurrent, setDenyIfCurrent]
  );

  /**
   * Task 099c (FR-33, #961): applies the toolbar Expiration to every external share of the record — ONE call to
   * `/set-record-share-expiry`, the date only. Success refreshes Current Access from the grants read. A failure says
   * what did not happen (the server's own sentence when it gave one) and puts the picker back on the shares' date; a
   * delegation/sign-in refusal is the deny banner. When the server could not confirm the outcome (it may have applied),
   * the list is re-read so it shows which, beside the server's sentence.
   */
  const applyShareExpiry = React.useCallback(
    async (expiryDate: string) => {
      setExpiryApplying(true);
      try {
        const data = await postJson<{ updatedCount?: number }>(SET_RECORD_SHARE_EXPIRY_PATH, {
          recordType,
          recordId,
          expiryDate,
        });
        // With no active share the route stores nothing and the date is kept as the next add's (the route's contract,
        // `SetRecordShareExpiryResponse.UpdatedCount`): the field, now in its no-shares state, holds it.
        setExpiryDraft(data.updatedCount === 0 ? expiryDate : null);
        await loadData();
        setNoticeIfCurrent(
          data.updatedCount === 0
            ? {
                intent: 'warning',
                text: `This record has no external shares now, so no share was changed. People you add have access until ${formatGrantDate(expiryDate)}.`,
              }
            : {
                intent: 'success',
                text: `Every external share on this record now ends on ${formatGrantDate(expiryDate)}.`,
              }
        );
      } catch (err) {
        // The picker returns to the shares' own date: nothing here may suggest the new date took.
        setExpiryDraft(null);
        const { deny, parentRefusal } = splitAccessFailure(err);
        if (deny) {
          setDenyIfCurrent(deny);
          return;
        }
        const apiErr = err instanceof AccessGrantModalApiError ? err : null;
        if (apiErr?.reasonCode === SHARE_EXPIRY_UNCONFIRMED_REASON_CODE) await loadData();
        setNoticeIfCurrent({
          intent: 'error',
          text:
            parentRefusal ??
            apiErr?.problemDetail ??
            `The expiration date was not changed to ${formatGrantDate(expiryDate)}. Please try again.`,
        });
      } finally {
        setExpiryApplying(false);
      }
    },
    [postJson, recordType, recordId, loadData, setNoticeIfCurrent, setDenyIfCurrent]
  );

  /** Confirms the pending revoke (task 065 extends this to branch on
   * {@link PendingRevoke}'s `kind`: a contact/organization grant goes through
   * `/revoke`; an internal system-user share goes through `/unshare-user` —
   * a share carries no `accessRecordId`, so it cannot use the same call). A
   * delegation/auth deny is the designed banner state (project constraint),
   * not the per-target error notice this previously always showed. */
  const confirmRevoke = React.useCallback(async () => {
    if (!pendingRevoke) return;
    setRevoking(true);
    try {
      if (pendingRevoke.kind === 'grant') {
        // Revoke is root-agnostic (task 070): it deactivates by accessRecordId +
        // contactId and no longer requires a root id, so no recordType/recordId is sent.
        const data = await postJson<IRevokeAccessResponseBody>('/api/v1/external-access/revoke', {
          accessRecordId: pendingRevoke.accessRecordId,
          contactId: pendingRevoke.contactId,
        });
        const fullName = pendingRevoke.fullName;
        setPendingRevoke(null);
        await loadData();
        setNoticeIfCurrent(buildRevokeNotice(fullName, data));
      } else {
        // Internal user share (task 063/065): unshare-user, not /revoke.
        await postJson('/api/v1/external-access/unshare-user', {
          recordType,
          recordId,
          systemUserId: pendingRevoke.systemUserId,
        });
        const fullName = pendingRevoke.fullName;
        setPendingRevoke(null);
        await loadData();
        setNoticeIfCurrent({ intent: 'success', text: `Removed ${fullName}'s share.` });
      }
    } catch (err) {
      const { deny, parentRefusal } = splitAccessFailure(err);
      if (deny) {
        setDenyIfCurrent(deny);
        setPendingRevoke(null);
      } else if (parentRefusal) {
        // Task 175: refused because it would make the record looser than its parent's floor; nothing was revoked.
        setPendingRevoke(null);
        setNoticeIfCurrent({ intent: 'error', text: parentRefusal });
      } else if (
        pendingRevoke.kind === 'grant' &&
        err instanceof AccessGrantModalApiError &&
        err.reasonCode === REVOKE_SPE_CLEANUP_INCOMPLETE_REASON_CODE
      ) {
        // M2 (task 024 -> task 065): /revoke now returns 500 + ProblemDetails for this exact
        // shape (aligned with /close-project) instead of 200 + Failed-in-body. The Dataverse
        // grant WAS deactivated server-side even though the SPE half failed, so the SAME
        // three-outcome message + deactivatedCount the 200 path renders must still reach the
        // person — a 500 must never regress into a generic "please try again" that drops
        // deactivatedCount (owner directive 2026-09-10: "the status code is for the client,
        // the MESSAGE is for the person").
        const fullName = pendingRevoke.fullName;
        setPendingRevoke(null);
        await loadData();
        setNoticeIfCurrent(
          buildRevokeNotice(fullName, {
            speContainerOutcome: err.speContainerOutcome ?? 'Failed',
            deactivatedCount: err.deactivatedCount,
          })
        );
      } else if (
        pendingRevoke.kind === 'grant' &&
        err instanceof AccessGrantModalApiError &&
        err.reasonCode !== undefined &&
        NON_RETRYABLE_REVOKE_REASON_CODES.has(err.reasonCode)
      ) {
        // #2103 follow-up: the organization is too large for one revoke to sweep; #2104 round 3: a matching container
        // permission is ambiguous or unverifiable. Either way the grant WAS deactivated, and a retry meets the same
        // answer — so show the server's sentence (what an administrator must do), never the generic "please try
        // again" below.
        setPendingRevoke(null);
        await loadData();
        setNoticeIfCurrent({ intent: 'warning', text: err.detail });
      } else if (
        pendingRevoke.kind === 'share' &&
        err instanceof AccessGrantModalApiError &&
        err.reasonCode === UNSHARE_LAST_READER_REASON_CODE
      ) {
        // Task 139 (S5): the last person who can open a secure record cannot be
        // removed. The server's sentence says what to do; nothing was removed.
        setPendingRevoke(null);
        setNoticeIfCurrent({ intent: 'warning', text: err.detail });
      } else if (
        pendingRevoke.kind === 'share' &&
        err instanceof AccessGrantModalApiError &&
        err.reasonCode === USER_SHARE_CHILDREN_INCOMPLETE_REASON_CODE
      ) {
        // Task 149: the share on the record IS gone; some related records of the secure record still need
        // updating (the server completes them within minutes). Reload so the row disappears, and show the
        // server's sentence — never "Failed to revoke", which would claim the share is still there.
        setPendingRevoke(null);
        await loadData();
        setNoticeIfCurrent({ intent: 'warning', text: err.detail });
      } else {
        setNoticeIfCurrent({
          intent: 'error',
          text: `Failed to revoke access for ${pendingRevoke.fullName}. Please try again.`,
        });
      }
    } finally {
      setRevoking(false);
    }
  }, [pendingRevoke, loadData, postJson, recordType, recordId, setNoticeIfCurrent, setDenyIfCurrent]);

  const toggleCandidateSelected = (contactId: string) => {
    setSelectedCandidateIds(prev => {
      const next = new Set(prev);
      if (next.has(contactId)) next.delete(contactId);
      else next.add(contactId);
      return next;
    });
  };

  // Actions blocked by a server-confirmed 401/403 deny (task 008/065 — the
  // delegation gate), which renders its own banner below. The Access-Permission
  // gate is NOT part of this any more (task 138): it HIDES the grantee kinds a
  // record does not admit (contactGrantsOffered / organizationGrantsOffered)
  // instead of disabling everything — on a Restricted record "Add person" (users), user rows,
  // their level dropdowns and Add must stay usable, because Restricted never
  // limits internal access.
  const actionsBlocked = accessDenyState !== null;

  // Task 099c (FR-33, #961): the toolbar Expiration (state and the add rule: see addExpiryDate above). With shares it
  // shows their date and Apply moves every share to a new one; with no share it holds the date the next add sends
  // (default today + 90) — there is nothing to Apply to. It cannot be changed while the date is unknown (the grants
  // read failed) or writes are blocked.
  const expiryShown =
    shareExpiry.kind === 'shares'
      ? (shareExpiry.earliest ?? '')
      : shareExpiry.kind === 'noShares'
        ? utcDateOnly(DEFAULT_SHARE_EXPIRY_DAYS)
        : '';
  // Review F1-a: not while an add or a suggestion's Grant is in flight — it sends the date read when it started, so an
  // Apply meanwhile would leave the rest of that batch on the old date.
  const expiryEditable =
    shareExpiry.kind !== 'unknown' && !actionsBlocked && !expiryApplying && !approving && suggestionBusy === null;
  // With shares: an edit that differs from the applied date is pending (review F1-c: a draft the shares now carry is
  // not). Apply sends the edit, or — when the shares end on different dates — the shown date, which aligns them
  // (review F1-b: the divergence line offers Apply, so it is there with no edit).
  const expiryPending = shareExpiry.kind === 'shares' && expiryDraft !== null && expiryDraft !== expiryShown;
  // Review round 2 (F4): Apply with no edit is offered only while the shown (earliest) date is still usable — when it
  // has lapsed there is nothing valid to apply, so the user is told to choose a date instead.
  const expiryShownLapsed = expiryShown !== '' && expiryShown < expiryMin;
  const expiryApplyOffered =
    shareExpiry.kind === 'shares' && (expiryPending || (shareExpiry.diverges && expiryShown >= expiryMin));
  const expiryApplyValue = expiryDraft ?? expiryShown;
  const expiryCanApply =
    expiryEditable &&
    expiryApplyOffered &&
    /^\d{4}-\d{2}-\d{2}$/.test(expiryApplyValue) &&
    expiryApplyValue >= expiryMin;
  const expiryValidationMessage = expiryDraftBeforeMin
    ? `Choose ${formatGrantDate(expiryMin)} or later.`
    : addExpiryInvalid
      ? 'Choose the date new shares end.'
      : undefined;
  // With no share and no contact-based access offered (Restricted), there is nothing for the Expiration to govern.
  const showShareExpiry = shareExpiry.kind !== 'noShares' || contactGrantsOffered;

  // Task 142: suggestions the server is waiting on (secure records) — contact suggestions only where contacts may be
  // granted (never on Restricted); a linked internal user's suggestion is a share, which Restricted never limits.
  const pendingSuggestions = assignedEntries.filter(
    e => e.state === 'PendingConfirmation' && (e.systemUserId || contactGrantsOffered)
  );
  /** The automatic-grant provenance of a Current Access row — the source field that granted it (task 142). */
  const autoSourceFor = (contactId: string): IAssignedAccessEntry | undefined =>
    assignedEntries.find(
      e =>
        (e.subjectId === contactId || e.systemUserId === contactId) && (e.state === 'Granted' || e.state === 'Shared')
    );
  /** Read-time terms that keep a contact on the record if its grant is removed (criterion 17). */
  const residualFor = (contactId: string): string[] =>
    assignedEntries.find(e => e.subjectId === contactId && e.residualAccessTerms.length > 0)?.residualAccessTerms ?? [];
  // Revoke is never gated by the Access Permission — reviewing + revoking
  // EXISTING access stays available on every record (task 073 UAT #4).
  // The delegation/auth deny DOES block revoke — it is one of the "three write
  // actions" the project constraint names explicitly.
  const revokeBlocked = revoking || accessDenyState !== null;
  /** Task 175: "matter {name}" with the name (or, unnamed, the type) as a link that opens the parent when the host can. */
  const renderParentReference = (parent: IFollowsParent): React.ReactNode => {
    const label = parentTypeLabel(parent.recordType);
    const open = onOpenParent ? () => onOpenParent(parent) : undefined;
    if (!parent.name) {
      return open ? (
        <Link inline onClick={open}>
          {label}
        </Link>
      ) : (
        label
      );
    }
    return (
      <>
        {label}{' '}
        {open ? (
          <Link inline onClick={open}>
            {parent.name}
          </Link>
        ) : (
          <strong>{parent.name}</strong>
        )}
      </>
    );
  };

  // Task 175: the direct parents the No Access List may name (only a direct parent is ever named, 064's contract).
  const knownDirectParents: IKnownDirectParent[] = serverAccess?.inheritedFrom
    ? [...parents, serverAccess.inheritedFrom]
    : parents;
  // Task 175 (round 87): the effective values and where they come from, for the parent bar.
  const effectiveAccessLines =
    parents.length > 0 && accessFloor && recordAccessPermission
      ? describeEffectiveAccess({
          accessPermission: recordAccessPermission,
          isSecure: isSecureRecord,
          floor: accessFloor,
          parents,
        })
      : [];

  // Task 067: the walls in force on this record, keyed by subject — what marks a Current Access row walled off.
  const vetoIndex = React.useMemo(
    () => buildVetoIndex(noAccessState.kind === 'list' ? noAccessState.entries : []),
    [noAccessState]
  );

  return (
    <>
      <SprkModal
        // task 073 v1.0.24 — the modal is `nonBlocking` (Fluent non-modal: no
        // backdrop, no focus trap) so the host's NATIVE advanced-lookup pane
        // (Xrm.Utility.lookupObjects, opened by "Add person"/"+ Organization")
        // renders on top and stays interactive instead of being covered by a
        // modal backdrop — the proven CommunicationActions composer pattern.
        open={open}
        onClose={handleCancelAttempt}
        title={title}
        size="lg"
        dismiss="explicit"
        nonBlocking
        // While a native advanced-lookup pane is open (picking), the surface moves
        // left of the pane and dims, so it neither covers the lookup (task 073 UAT
        // v1.0.29 #1A: it sits above the pane's z-index) nor disappears (owner test
        // feedback 2026-10-07: hiding it read as the modal closing). It stays
        // mounted — staged picks survive.
        yieldToSidePane={picking}
        footerStart={
          <Button appearance="secondary" onClick={handleCancelAttempt}>
            Cancel
          </Button>
        }
        footer={
          // Review F1-a: Save commits staged adds, so not while an Expiration Apply is in flight.
          <Button appearance="primary" disabled={expiryApplying} onClick={() => void handleSave()}>
            Save
          </Button>
        }
      >
        {!canGrantAccess ? (
          <div className={styles.notAuthorized}>
            <DismissCircleRegular fontSize={32} />
            <Text>You do not have permission to grant or revoke access for this record.</Text>
          </div>
        ) : (
          <>
            {notice && (
              <MessageBar intent={notice.intent} style={{ marginBottom: tokens.spacingVerticalM }}>
                <MessageBarBody>
                  <MessageBarTitle>
                    {notice.intent === 'success' ? 'Success' : notice.intent === 'warning' ? 'Notice' : 'Error'}
                  </MessageBarTitle>
                  {noticeContent(notice.text, notice.links)}
                </MessageBarBody>
                {notice.retry && (
                  <MessageBarActions>
                    <Button size="small" disabled={approving} onClick={() => void handleGrantSelected()}>
                      Retry
                    </Button>
                  </MessageBarActions>
                )}
              </MessageBar>
            )}

            {loading ? (
              <div className={styles.loadingRow}>
                <Spinner size="tiny" />
                <Text>Loading access data…</Text>
              </div>
            ) : (
              <>
                {/* Access-Permission banner (task 073 UAT #4; task 138 + owner O1 FINAL) — ONE
                    bar per non-standard state: Restricted / Secure – Restricted / Secure /
                    Limited. It explains what the hidden options below would have done. */}
                {permissionBanner && (
                  <MessageBar intent={permissionBanner.intent} style={{ marginBottom: tokens.spacingVerticalM }}>
                    <MessageBarBody>
                      <MessageBarTitle>{permissionBanner.title}</MessageBarTitle>
                      {permissionBanner.text}
                    </MessageBarBody>
                  </MessageBar>
                )}

                {/* Task 175 (owner round 87, coordinator O-4): the parent's floor, shown WITH the Access Permission
                    bar above. Display only: nothing below is hidden or disabled by it. */}
                {parents.length > 0 && (
                  <MessageBar intent="info" style={{ marginBottom: tokens.spacingVerticalM }}>
                    <MessageBarBody>
                      <MessageBarTitle>Minimum access from the parent</MessageBarTitle>
                      Minimum access comes from the parent {renderParentReference(parents[0])}
                      {otherParentsSuffix(parents.length)}: this record can be made stricter but not looser.
                      {effectiveAccessLines.map(line => (
                        <div key={line}>{line}</div>
                      ))}
                    </MessageBarBody>
                  </MessageBar>
                )}

                {/* Delegation/auth deny banner (task 008 FR-07 / task 065) — a
                    DESIGNED state per the project constraint, never a toast or
                    raw error. Disables every write action below via
                    actionsBlocked until the modal is reopened. */}
                {accessDenyState && (
                  <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalM }}>
                    <MessageBarBody>
                      <MessageBarTitle>{accessDenyState.title}</MessageBarTitle>
                      {accessDenyState.message}
                    </MessageBarBody>
                  </MessageBar>
                )}

                {/* Secure-record owner/BU read-only display (task 065, design.md §6). */}
                {secureOwnerInfo && (
                  <div className={styles.secureOwnerRow}>
                    <Text>
                      Secure record — Owner: <strong>{secureOwnerInfo.ownerName}</strong> · Business Unit:{' '}
                      <strong>{secureOwnerInfo.businessUnitName}</strong>
                    </Text>
                  </div>
                )}

                {/* Suggested Access (task 142, owner A3 = prompt): on a SECURE record an "Assigned *" person is
                    suggested, not granted. Server-derived from the access-conferring registry (never a client list).
                    Grant writes through the normal path at Collaborate; Dismiss declines it while the assignment
                    persists. Hidden when there is nothing to suggest. */}
                {pendingSuggestions.length > 0 && (
                  <div className={styles.section} style={{ marginTop: tokens.spacingVerticalL }}>
                    <Text className={styles.sectionTitle}>Suggested Access</Text>
                    <div className={styles.listArea}>
                      {pendingSuggestions.map(entry => (
                        <div className={styles.row} key={entry.entryId}>
                          <div className={styles.rowMain}>
                            <Text className={styles.rowName}>
                              {entry.systemUserId ? <PersonAccountsRegular /> : <PersonRegular />}{' '}
                              {entry.subjectName ?? '(no name)'}
                            </Text>
                            <Text className={styles.rowMeta}>Suggested from {entry.sourceFieldLabel}</Text>
                          </div>
                          <div className={styles.rowActions}>
                            <Button
                              appearance="primary"
                              size="small"
                              onClick={() => void grantSuggestion(entry)}
                              // Review F1-a: not while an Apply is in flight — the grant would carry the old date.
                              disabled={actionsBlocked || suggestionBusy !== null || expiryApplying}
                              aria-label={`Grant ${entry.subjectName ?? 'suggested person'}`}
                            >
                              Grant
                            </Button>
                            <Button
                              appearance="subtle"
                              size="small"
                              onClick={() => void dismissSuggestion(entry)}
                              disabled={actionsBlocked || suggestionBusy !== null}
                              aria-label={`Dismiss ${entry.subjectName ?? 'suggested person'}`}
                            >
                              Dismiss
                            </Button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Add Access Permissions (task 073 UAT v1.0.24 #2; task 193: one "Add person") —
                    role-based members + looked-up contacts/orgs/users. "Add person" / "+ Organization"
                    (icon-only, #4) open the NATIVE advanced-lookup pane. Select, choose a level, Add. */}
                <div className={styles.section} style={{ marginTop: tokens.spacingVerticalL }}>
                  <div className={styles.sectionHeaderRow}>
                    <Text className={styles.sectionTitle}>Add Access Permissions</Text>
                    <div className={styles.sectionHeaderActions}>
                      {/* Task 099c (FR-33, #961; owner 2026-09-10): ONE Expiration for every external share of the
                          record. Picking a date stages it; Apply (or Enter) sends it once. The date only — never a
                          level (ISS-023). */}
                      {showShareExpiry && (
                        <div className={styles.shareExpiryField}>
                          <Field
                            label="Expiration"
                            orientation="horizontal"
                            size="small"
                            validationState={expiryValidationMessage ? 'error' : 'none'}
                            validationMessage={expiryValidationMessage}
                          >
                            <Input
                              type="date"
                              size="small"
                              className={styles.shareExpiryInput}
                              value={expiryDraft ?? expiryShown}
                              min={expiryMin}
                              disabled={!expiryEditable}
                              onChange={(_, d) => setExpiryDraft(d.value)}
                              onKeyDown={e => {
                                if (e.key === 'Enter' && expiryCanApply) {
                                  // With shares only: with none, Enter has nothing to apply (the date goes with an add).
                                  e.preventDefault();
                                  void applyShareExpiry(expiryApplyValue);
                                }
                              }}
                              data-testid="share-expiry-input"
                            />
                          </Field>
                          {expiryApplyOffered && (
                            <Button
                              appearance="primary"
                              size="small"
                              disabled={!expiryCanApply}
                              onClick={() => void applyShareExpiry(expiryApplyValue)}
                              data-testid="share-expiry-apply"
                            >
                              {expiryApplying ? 'Applying…' : 'Apply'}
                            </Button>
                          )}
                        </div>
                      )}
                      {/* Icon-only "+" triggers (task 073 UAT v1.0.24 #4) — open the native advanced-lookup pane
                          (pickPerson/pickOrganization). Task 193 (#2338): ONE "Add person" over contacts and users
                          replaced "+ Contact" and "+ User"; on a Restricted record it looks up users only. */}
                      {pickPerson && (
                        <Tooltip content="Add person" relationship="label">
                          <Button
                            appearance="secondary"
                            size="small"
                            icon={<PersonRegular />}
                            onClick={() => void openPersonPicker()}
                            disabled={actionsBlocked || picking}
                            aria-label="Add person"
                          >
                            +
                          </Button>
                        </Tooltip>
                      )}
                      {pickOrganization && organizationGrantsOffered && (
                        <Tooltip content="Add organization" relationship="label">
                          <Button
                            appearance="secondary"
                            size="small"
                            icon={<BuildingRegular />}
                            onClick={() => void openOrgPicker()}
                            disabled={actionsBlocked || picking}
                            aria-label="Add organization"
                          >
                            +
                          </Button>
                        </Tooltip>
                      )}
                    </div>
                  </div>

                  {/* Task 099c: what the Expiration shows and what applying it does. Different dates are stated,
                      never silently resolved to one (owner 2026-09-10). */}
                  {showShareExpiry && shareExpiry.kind === 'noShares' && (
                    <Text className={styles.shareExpiryHint} data-testid="share-expiry-default">
                      No external shares yet. People you add have access until this date.
                    </Text>
                  )}
                  {shareExpiry.kind === 'shares' && shareExpiry.diverges && (
                    <Text className={styles.shareExpiryHint} data-testid="share-expiry-divergence">
                      {expiryShownLapsed && !(expiryPending && expiryDraftValid)
                        ? `Shares on this record end on different dates, and the earliest has passed. Choose ${formatGrantDate(expiryMin)} or later.`
                        : 'Shares on this record end on different dates; the earliest is shown. Apply, or adding a person, sets them to this date.'}
                    </Text>
                  )}
                  {expiryPending && (
                    <Text className={styles.shareExpiryHint} data-testid="share-expiry-unapplied">
                      Not applied yet. Until you apply it, people you add get{' '}
                      {addExpiryDate ? formatGrantDate(addExpiryDate) : `${DEFAULT_SHARE_EXPIRY_DAYS} days of access`}.
                    </Text>
                  )}
                  {shareExpiry.kind === 'shares' && shareExpiry.anyLapsed && (
                    <Text className={styles.shareExpiryHint} data-testid="share-expiry-lapsed">
                      Shares marked Expired are renewed to the date you apply.
                    </Text>
                  )}

                  {/* Padding below the header row, before the list (task 073 UAT v1.0.24 #3). */}
                  <div className={styles.listArea}>
                    {availableItems.length === 0 ? (
                      <Text className={styles.emptyState}>
                        {!contactGrantsOffered
                          ? 'No users yet. Use “Add person” to share this record with a colleague.'
                          : !organizationGrantsOffered
                            ? 'No contacts or users yet. Use “Add person” to add.'
                            : 'No contacts, organizations or users yet. Use “Add person” or “+ Organization” to add.'}
                      </Text>
                    ) : (
                      availableItems.map(item => (
                        <div className={styles.row} key={item.id}>
                          <Checkbox
                            checked={selectedCandidateIds.has(item.id)}
                            onChange={() => toggleCandidateSelected(item.id)}
                            aria-label={`Select ${item.name}`}
                            disabled={actionsBlocked}
                          />
                          <div className={styles.rowMain}>
                            {/* Contact name → link opening the Contact record (task 073 UAT v1.0.24 #6);
                                organization rows show a building glyph; user rows (task 065) show a
                                person-accounts glyph — both render plain (no open-record link). */}
                            {item.kind === 'contact' && item.contact && onOpenContact ? (
                              <Link
                                className={styles.contactLink}
                                onClick={() => onOpenContact(item.contact!.contactId)}
                              >
                                {item.name}
                              </Link>
                            ) : (
                              <Text className={styles.rowName}>
                                {item.kind === 'organization' ? (
                                  <BuildingRegular />
                                ) : item.kind === 'user' ? (
                                  <PersonAccountsRegular />
                                ) : null}{' '}
                                {item.name}
                              </Text>
                            )}
                            <Text className={styles.rowMeta}>{item.meta}</Text>
                          </div>
                          <div className={styles.rowActions}>
                            {/* Per-row access level (task 073 v1.0.23) — no default; "Pick access level". */}
                            <Dropdown
                              className={styles.rowLevelDropdown}
                              placeholder="Pick access level"
                              value={
                                rowLevels[item.id] !== undefined
                                  ? (accessLevelOptions.find(o => o.value === rowLevels[item.id])?.label ?? '')
                                  : ''
                              }
                              selectedOptions={rowLevels[item.id] !== undefined ? [String(rowLevels[item.id])] : []}
                              disabled={actionsBlocked}
                              onOptionSelect={(_, data) => {
                                if (data.optionValue) {
                                  const v = Number(data.optionValue);
                                  setRowLevels(prev => ({ ...prev, [item.id]: v }));
                                }
                              }}
                            >
                              {accessLevelOptions.map(o => (
                                <Option key={o.value} value={String(o.value)} text={o.label}>
                                  {o.label}
                                </Option>
                              ))}
                            </Dropdown>
                          </div>
                        </div>
                      ))
                    )}
                  </div>

                  <div className={styles.levelRow}>
                    <Button
                      appearance="primary"
                      // Review F1-a: not while an Apply is in flight — the add would carry the old date.
                      disabled={selectedOfferedCount === 0 || approving || actionsBlocked || expiryApplying}
                      icon={approving ? <Spinner size="tiny" /> : undefined}
                      onClick={handleGrantSelected}
                    >
                      Add ({selectedOfferedCount})
                    </Button>
                  </div>
                </div>

                {/* Current Access (task 073 UAT v1.0.24 #7 — extra top padding above the section) */}
                <div className={styles.section} style={{ marginTop: tokens.spacingVerticalXXL }}>
                  <Text className={styles.sectionTitle}>Current Access</Text>
                  <div className={styles.listArea}>
                    {/* Task 099a (verifier F2): a capped list says it is partial (NFR-03). */}
                    {currentAccessError === null && grantsTruncatedAt !== null && (
                      <MessageBar intent="warning" data-testid="current-access-truncated">
                        <MessageBarBody>Only the first {grantsTruncatedAt} grants are shown.</MessageBarBody>
                      </MessageBar>
                    )}
                    {currentAccessError?.kind === 'notShown' ? (
                      // Task 099a: the message above (the deny banner, or the load notice) says why — not repeated
                      // here, and never "No active grants".
                      <Text className={styles.emptyState} data-testid="current-access-not-shown">
                        Not shown.
                      </Text>
                    ) : currentAccessError?.kind === 'failed' ? (
                      // Task 099a: the read failed — say so (the server's own sentence), never "No active grants".
                      <MessageBar intent="error" data-testid="current-access-error">
                        <MessageBarBody>
                          <MessageBarTitle>Current Access unavailable</MessageBarTitle>
                          {currentAccessError.text}
                        </MessageBarBody>
                      </MessageBar>
                    ) : existingGrants.length === 0 ? (
                      <Text className={styles.emptyState}>No active grants for this record.</Text>
                    ) : (
                      existingGrants.map(grant => {
                        // Row kind (noAccess.ts classifyCurrentAccessRow):
                        //  - an internal system-user POA share (task 065) is checked FIRST: a share also carries
                        //    no accessRecordId, so it must not fall into the standing branch;
                        //  - a standing-grant row (task 073 UAT #2) confers ongoing membership via the contact's
                        //    global `sprk_standinggrant` flag — there is NO per-record `sprk_externalrecordaccess`
                        //    row to revoke here, so it renders non-revocable with a "Standing" badge;
                        //  - an organization grant (task 073 #7): everyone at the firm inherits access. Unlike a
                        //    standing grant it IS a real per-record row, so it keeps the level badge + Revoke.
                        //  - an inherited share (task 175) is a user share a secure parent passed on: read-only here.
                        const rowKind = classifyCurrentAccessRow(grant);
                        const isInherited = rowKind === 'inherited';
                        const isUserShare = rowKind === 'share' || isInherited;
                        const isStanding = rowKind === 'standing';
                        const isOrg = rowKind === 'organization';
                        // Task 067: a wall in force overrides the row; otherwise the record's own policy may cancel
                        // it (task 066). Different reasons, different markers; the wall wins.
                        const vetoReason = vetoFor(grant, rowKind, vetoIndex, contactWalledOrgs);
                        const suppressedReason = vetoReason
                          ? null
                          : suppressionFor(rowKind, accessPermissionState, isSecureRecord);
                        const levelClassName = vetoReason ? styles.vetoedLevel : undefined;
                        // Task 099a: a lapsed grant confers nothing, so its level is outlined like any row without effect.
                        const levelAppearance = vetoReason || suppressedReason || grant.isExpired ? 'outline' : 'tint';
                        const rowKey = grant.accessRecordId
                          ? grant.accessRecordId
                          : isInherited
                            ? `inherited-${grant.contactId}`
                            : isUserShare
                              ? `share-${grant.contactId}`
                              : `standing-${grant.contactId}`;
                        return (
                          <div
                            className={styles.row}
                            key={rowKey}
                            data-access-state={
                              vetoReason
                                ? 'vetoed'
                                : grant.isExpired
                                  ? 'expired'
                                  : suppressedReason
                                    ? 'suppressed'
                                    : 'active'
                            }
                          >
                            <div className={styles.rowMain}>
                              {/* Contact name → link opening the Contact record (task 073 UAT v1.0.24 #6).
                                Org grants key on the org id (not a contact); user-share rows key on a
                                systemUserId (not a contact) — both stay plain text. */}
                              {!isOrg && !isUserShare && onOpenContact ? (
                                <Link className={styles.contactLink} onClick={() => onOpenContact(grant.contactId)}>
                                  {grant.fullName}
                                </Link>
                              ) : (
                                <Text className={styles.rowName}>
                                  {isUserShare ? <PersonAccountsRegular /> : null} {grant.fullName}
                                </Text>
                              )}
                              <Text className={styles.rowMeta}>
                                {isUserShare
                                  ? grant.externalNoAccess
                                    ? EXTERNAL_USER_NO_ACCESS_LABEL
                                    : isInherited
                                      ? `${describeInheritedShare(grant)} — last updated ${formatGrantDate(grant.grantedDate)}`
                                      : `Internal user share — last updated ${formatGrantDate(grant.grantedDate)}`
                                  : isStanding
                                    ? 'Standing grant — ongoing access to assigned records'
                                    : isOrg
                                      ? 'Organization grant — all organization contacts have access'
                                      : autoSourceFor(grant.contactId)
                                        ? `Automatic — ${autoSourceFor(grant.contactId)!.sourceFieldLabel} · granted ${formatGrantDate(grant.grantedDate)}`
                                        : grant.grantedByContactName
                                          ? // Task 140: issued by a contact from the external SPA (sprk_grantedbycontact).
                                            `Granted by ${grant.grantedByContactName} (external contact) on ${formatGrantDate(grant.grantedDate)}`
                                          : `Granted by ${grant.grantedByName ?? 'unknown'} on ${formatGrantDate(grant.grantedDate)}`}
                              </Text>
                              {vetoReason && <Text className={styles.vetoReason}>{vetoReason}</Text>}
                              {suppressedReason && <Text className={styles.suppressedReason}>{suppressedReason}</Text>}
                            </div>
                            <div className={styles.rowActions}>
                              {/* Task 099a: a lapsed grant confers nothing — shown as expired, never as live access. */}
                              {grant.isExpired && (
                                <Badge appearance="outline" color="danger" data-testid="grant-expired-badge">
                                  {grant.expiresDate ? `Expired ${formatGrantDate(grant.expiresDate)}` : 'Expired'}
                                </Badge>
                              )}
                              {/* Task 067: the wall's marker, over the level the row would otherwise carry. */}
                              {vetoReason && (
                                <Badge appearance="filled" color="danger">
                                  No Access
                                </Badge>
                              )}
                              {/* Task 066 (folded into 067): cancelled by the record's own policy. */}
                              {suppressedReason && (
                                <Badge appearance="outline" color="subtle">
                                  No effect
                                </Badge>
                              )}
                              {isStanding ? (
                                <Badge
                                  appearance={levelAppearance}
                                  color={vetoReason || suppressedReason ? 'subtle' : 'success'}
                                  className={levelClassName}
                                >
                                  Standing
                                </Badge>
                              ) : isUserShare ? (
                                <>
                                  <Badge
                                    appearance={levelAppearance}
                                    color={vetoReason ? 'subtle' : 'brand'}
                                    className={levelClassName}
                                  >
                                    {accessLevelOptions.find(o => o.value === grant.accessLevel)?.label ?? 'Custom'}
                                  </Badge>
                                  <Badge appearance="outline" size="small">
                                    {isInherited ? 'Inherited' : 'User (share)'}
                                  </Badge>
                                  {/* Task 175: an inherited share is changed on its parent, so it has no Revoke. */}
                                  {!isInherited && (
                                    <Button
                                      appearance="subtle"
                                      size="small"
                                      onClick={() =>
                                        setPendingRevoke({
                                          kind: 'share',
                                          systemUserId: grant.contactId,
                                          fullName: grant.fullName,
                                        })
                                      }
                                      disabled={revokeBlocked}
                                    >
                                      Revoke
                                    </Button>
                                  )}
                                </>
                              ) : (
                                <>
                                  <Badge
                                    appearance={levelAppearance}
                                    color={vetoReason || suppressedReason ? 'subtle' : 'informative'}
                                    className={levelClassName}
                                  >
                                    {accessLevelOptions.find(o => o.value === grant.accessLevel)?.label ??
                                      grant.accessLevel}
                                  </Badge>
                                  <Button
                                    appearance="subtle"
                                    size="small"
                                    onClick={() =>
                                      setPendingRevoke({
                                        kind: 'grant',
                                        accessRecordId: grant.accessRecordId!,
                                        contactId: grant.contactId,
                                        fullName: grant.fullName,
                                      })
                                    }
                                    disabled={revokeBlocked}
                                  >
                                    Revoke
                                  </Button>
                                </>
                              )}
                            </div>
                          </div>
                        );
                      })
                    )}
                  </div>
                </div>

                {/* No Access List (task 067, owner round 59 item 3) — READ-ONLY: no add, no remove. Hidden when
                    064 answers `notShown` (the caller lacks Write, owner O2). */}
                {noAccessState.kind !== 'hidden' && (
                  <section
                    ref={noAccessSectionRef}
                    // Focusable by script only (task 153: the access-status indicator opens the modal here).
                    tabIndex={-1}
                    className={styles.section}
                    style={{ marginTop: tokens.spacingVerticalXXL }}
                    aria-label="No Access List"
                  >
                    <Text className={styles.sectionTitle}>No Access List</Text>
                    <Text className={styles.sectionSubtitle}>
                      People and organizations walled off from this record. Read-only here: an access administrator adds
                      and removes entries in No Access Entries.
                    </Text>
                    <div className={styles.listArea}>
                      {noAccessState.kind === 'loading' ? (
                        <div className={styles.loadingRow}>
                          <Spinner size="tiny" />
                          <Text>Loading the No Access List…</Text>
                        </div>
                      ) : noAccessState.kind === 'error' ? (
                        <MessageBar intent="error">
                          <MessageBarBody>
                            <MessageBarTitle>No Access List unavailable</MessageBarTitle>
                            The No Access List for this record could not be read, so whether anyone is walled off is not
                            shown here. Close and reopen to try again.
                          </MessageBarBody>
                        </MessageBar>
                      ) : noAccessState.entries.length === 0 ? (
                        <Text className={styles.emptyState}>No one is on this record&apos;s No Access List.</Text>
                      ) : (
                        noAccessState.entries.map((entry: IRecordNoAccessEntry) => {
                          const notInForce = describeNotInForce(entry);
                          return (
                            <div
                              className={styles.row}
                              key={entry.entryId}
                              data-in-force={entry.inForce === null ? 'undetermined' : String(entry.inForce)}
                            >
                              <div className={styles.rowMain}>
                                <Text className={styles.rowName}>
                                  {entry.subjectKind === 'organization' ? (
                                    <BuildingRegular />
                                  ) : entry.subjectKind === 'systemuser' ? (
                                    <PersonAccountsRegular />
                                  ) : (
                                    <PersonRegular />
                                  )}{' '}
                                  {entry.subjectName ?? entry.name ?? '(no name)'}
                                </Text>
                                <Text className={styles.rowMeta}>
                                  {describeSubjectKind(entry)} · {describeCoverage(entry, knownDirectParents)}
                                </Text>
                                {notInForce && <Text className={styles.notInForceReason}>{notInForce}</Text>}
                                {(entry.modifiedByName || entry.modifiedOn) && (
                                  <Text className={styles.rowMeta}>
                                    Last changed{entry.modifiedByName ? ` by ${entry.modifiedByName}` : ''}
                                    {entry.modifiedOn ? ` on ${formatGrantDate(entry.modifiedOn)}` : ''}
                                  </Text>
                                )}
                              </div>
                              <div className={styles.rowActions}>
                                {entry.inForce === true ? (
                                  <Badge appearance="filled" color="danger">
                                    No Access
                                  </Badge>
                                ) : entry.inForce === null ? (
                                  <Badge appearance="outline" color="warning">
                                    Undetermined
                                  </Badge>
                                ) : (
                                  <Badge appearance="outline" color="subtle">
                                    Not in force
                                  </Badge>
                                )}
                              </div>
                            </div>
                          );
                        })
                      )}
                      {noAccessState.kind === 'list' && noAccessState.truncated && (
                        // Verifier F4-b: markers come only from the listed entries, so with a truncated list a row
                        // walled by an unlisted entry would read as active. Say so, as the org check does.
                        <MessageBar intent="warning" style={{ marginTop: tokens.spacingVerticalS }}>
                          <MessageBarBody>
                            More entries cover this record than are listed here (the first{' '}
                            {noAccessState.entries.length} are shown). Current Access rows are marked from the listed
                            entries only, so someone walled off by an entry not shown here may still appear without the
                            No Access marker.
                          </MessageBarBody>
                        </MessageBar>
                      )}
                      {orgWallCheck === 'notChecked' && (
                        <MessageBar intent="warning" style={{ marginTop: tokens.spacingVerticalS }}>
                          <MessageBarBody>
                            An organization on this list is walled off, but whether the contacts in Current Access
                            belong to it could not be checked, so their rows are not marked.
                          </MessageBarBody>
                        </MessageBar>
                      )}
                    </div>
                  </section>
                )}
              </>
            )}
          </>
        )}
      </SprkModal>

      {/* Revoke confirm — a second, stacked Fluent Dialog is supported (unlike
          the OOB-navigateTo-inside-a-Fluent-dialog anti-pattern, which mixes
          chrome families; this nests two proprietary Fluent v9 dialogs). Task
          065: works for either PendingRevoke kind (grant or share) via the
          shared confirmRevoke handler. */}
      <SprkModal
        open={pendingRevoke !== null}
        onClose={() => setPendingRevoke(null)}
        title="Revoke access?"
        size="xs"
        dismiss="alert"
        maximizable={false}
        footerStart={
          <Button appearance="secondary" onClick={() => setPendingRevoke(null)} disabled={revoking}>
            Cancel
          </Button>
        }
        footer={
          <Button
            appearance="primary"
            onClick={confirmRevoke}
            disabled={revoking}
            icon={revoking ? <Spinner size="tiny" /> : undefined}
          >
            Revoke
          </Button>
        }
      >
        <Text>
          Revoke access for <strong>{pendingRevoke?.fullName}</strong>? They will immediately lose access to this record
          (unless a standing grant or other membership still applies).
        </Text>
        {/* Task 142 (criterion 17): BEFORE the removal, name the read-time term that will keep this contact on the
            record (owner A2: standing and organization access stay). */}
        {pendingRevoke?.kind === 'grant' &&
          describeResidualAccess(pendingRevoke.fullName, residualFor(pendingRevoke.contactId)) && (
            <MessageBar intent="warning" style={{ marginTop: tokens.spacingVerticalM }}>
              <MessageBarBody>
                {describeResidualAccess(pendingRevoke.fullName, residualFor(pendingRevoke.contactId))}
              </MessageBarBody>
            </MessageBar>
          )}
      </SprkModal>

      {/* Pending-changes warning (task 073 UAT v1.0.29 #1B) — the user staged a
          contact/organization but didn't click "Add" before Cancel/×. */}
      <SprkModal
        open={showPendingWarning}
        onClose={() => setShowPendingWarning(false)}
        title="Unsaved access permissions"
        size="xs"
        dismiss="alert"
        maximizable={false}
        footerStart={
          <Button
            appearance="secondary"
            onClick={() => {
              setShowPendingWarning(false);
              onClose();
            }}
          >
            Discard &amp; close
          </Button>
        }
        footer={
          <Button appearance="primary" onClick={() => setShowPendingWarning(false)}>
            Keep editing
          </Button>
        }
      >
        <Text>
          New access permissions are pending. Click <strong>Add</strong> to confirm, or they will not be saved.
        </Text>
      </SprkModal>
    </>
  );
};

export default AccessGrantModal;
