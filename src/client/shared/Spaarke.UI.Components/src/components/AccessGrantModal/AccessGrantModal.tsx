/**
 * AccessGrantModal — the person-icon access-grant modal (teams-app-r1 task 041).
 *
 * Opened from `TrackingFieldTrio`'s `onOpenGrantModal` callback (task 040). Per
 * `docs/standards/MODAL-DECISION-CRITERIA.md` this is a **Family 2** modal
 * (proprietary Fluent v9 dialog — a picker/approve UX, not a full-form edit and
 * not a browse-in-context collection). Per `docs/standards/MODAL-DESIGN-SYSTEM.md`
 * it is built directly on the **`SprkModal` base shell** (NOT one of the six
 * presets): none of `ConfirmModal`/`ChoiceModal`/`FormModal`/`PreviewModal`/
 * `BrowseModal`/`WizardModal` fit — this modal has THREE independent sections
 * (candidate-approve list, named-contact picker, existing-grants+revoke list)
 * each with its OWN per-row action, not a single primary Save/Submit — so a thin
 * `SprkModal` config (size `lg`, `dismiss="explicit"`, a single footer "Close")
 * is the correct fit per the doc's explicit scope note: "does not require
 * adopting every preset ... if a simpler ... preset fits" — here, none of the
 * six presets fit and the base shell itself is the right level.
 *
 * Write path (per the task's binding constraint — MUST reuse the built
 * `sprk_externalrecordaccess` write path, MUST NOT write directly to the table):
 *   - External contact with a known email → `POST /api/v1/external-access/invite-and-grant`
 *     (the built, atomic onboard+grant+CIAM-email endpoint). This is the literal
 *     endpoint task 041's steps named.
 *   - Internal workforce contact (or an external contact with no email on file)
 *     → `POST /api/v1/external-access/grant` (the built, audited grant CORE that
 *     `/invite-and-grant` itself calls internally — same write, same table, same
 *     `sprk_grantedby` provenance — invoked directly rather than through the
 *     onboard-first endpoint, because `/invite-and-grant`'s contract is
 *     structurally email+CIAM-onboard-first and would incorrectly attempt to
 *     CIAM-provision an internal workforce person). See the ESCALATION note
 *     below for why the internal deep-link notify branch is NOT implemented
 *     here.
 *   - Revoke (both) → `POST /api/v1/external-access/revoke` (the built endpoint).
 *
 * ESCALATION (per this task's `<escalation>` trigger + root CLAUDE.md §6/§6.5):
 * design.md §5.1 calls for an "internal workforce contact → deep-link
 * notification (small addition — they already have M365)" branch. No such
 * endpoint exists in the BFF today (verified: `ExternalAccessEndpoints.cs` maps
 * only `/grant`, `/revoke`, `/invite`, `/invite-and-grant`, `/close-project`,
 * `/provision-project` — no notify-only endpoint), and this task's guardrails
 * explicitly forbid modifying any BFF `.cs` file (concurrent-agent boundary).
 * Building that "small addition" is BFF work outside this task's scope, and
 * `InviteAndGrantExternalUserEndpoint`'s CIAM-onboard-first contract cannot be
 * repurposed for it without incorrectly provisioning a CIAM account for an
 * internal person. Per the task's own escalation instruction ("STOP and
 * escalate ... rather than building a second write path or a parallel notify
 * mechanism"), this modal WRITES the grant for internal contacts (the
 * `sprk_externalrecordaccess` row — the record-access outcome — succeeds
 * unconditionally) but surfaces the missing notify step as a non-blocking,
 * clearly-labeled "Notify pending" state rather than inventing a client-side
 * notify mechanism. See the task's final report for the full escalation
 * writeup.
 *
 * ACCESS-PERMISSION SHARING GATE (task 043, spec FR-14 Option A, added
 * teams-app-r1). The record-level Access-Permission state — `'restricted'` /
 * `'limited'` / `'standard'` (see {@link AccessPermissionState} in
 * `types.ts`) — governs WHICH grant types this modal permits: Restricted
 * blocks all external-grant actions (candidate-approve + named-contact-add)
 * behind a disabled state + explanatory banner; Limited allows those grants
 * but hides the standing-grant option; Standard (the default when the prop
 * is omitted) is task 041's unmodified baseline. This gate is STRUCTURALLY
 * independent of the per-grant `sprk_accesslevel` (`accessLevelOptions` /
 * `defaultAccessLevel`): the gating logic below only ever touches candidate/
 * named-contact/standing-grant availability, never `effectiveAccessLevel` or
 * the level sent in a grant's request body.
 *
 * "+ USER" — INTERNAL SYSTEM-USER SHARES (task 065, unified-access-control-r2,
 * spec FR-29). A FOURTH write path alongside the three above: picking a
 * `systemuser` (native advanced lookup, {@link IAccessGrantModalProps.pickUser})
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
 * predict that outcome client-side (no privilege pre-check, no hiding the
 * button) — server truth only. A 403 with a `sdap.access.deny.delegation_*`
 * reason code is rendered as a persistent, dismissable-only-by-reopening
 * banner (`accessDenyState` below) that disables every write action (+User,
 * +Contact, +Organization, Add, Revoke) — never a toast, never a raw error.
 * A 401 (expired sign-in) gets its own distinct banner for the same reason.
 *
 * M8 / M2 (task-024 finding, transferred to this task 2026-09-09; owner
 * directive 2026-09-10). `postJson`/`getJson` below now check `res.ok` before
 * parsing the body as success (M8 — previously `(await res.json()) as T` read
 * a failed response's ProblemDetails as if it were the success shape). Revoke
 * additionally reads the EXISTING `RevokeAccessResponse.SpeContainerOutcome` +
 * `DeactivatedCount` fields (no server change needed — both already exist) to
 * render one of three distinct outcomes: fully revoked, grant revoked but SPE
 * removal could not be confirmed (the person may retain file access — retry/
 * escalate), or nothing was revoked. M2 itself (aligning `/revoke`'s HTTP
 * status with `/close-project`'s for an SPE failure) is a `RevokeExternalAccessEndpoint.cs`
 * change — OUT OF THIS TASK'S FILE LANE (concurrent-agent boundary; this task
 * touches only `AccessGrantModal/**` + `TrackingFieldTrio/**`) — NOT
 * implemented here; reported as an escalation in the task's final report. The
 * owner's message requirement is fully satisfied without it, since the
 * distinguishing fields are already present in today's 200 response body.
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
  MessageBarBody,
  MessageBarTitle,
  Badge,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import { PersonRegular, PersonAccountsRegular, DismissCircleRegular, BuildingRegular } from '@fluentui/react-icons';
import { SprkModal } from '../SprkModal';
// Record picker (task 073 v1.0.24) — "+ Contact" / "+ Organization" open the host's
// NATIVE Dataverse advanced-lookup side pane (Xrm.Utility.lookupObjects, injected as
// pickContact/pickOrganization) — the same advanced-find surface the wizards use. The
// modal renders `nonBlocking` so that page-level pane is not covered by a backdrop.
// "+ User" (task 065, FR-29) mirrors the same pattern via pickUser.
import type {
  IAccessGrantModalProps,
  IAccessGrantCandidate,
  IAccessGrantRecord,
  IContactSearchResult,
  IOrganizationPick,
  IUserPick,
  ISecureOwnerInfo,
} from './types';
import { DEFAULT_ACCESS_LEVEL_OPTIONS } from './types';

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
  // Subsection header row: title on the left, action buttons (+ Contact / + Organization) right-aligned.
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
});

/** Formats an ISO date string for display; falls back to the raw value when
 * parsing fails, and to an em-dash when absent. */
function formatGrantDate(iso: string | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

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

  constructor(status: number, detail: string, reasonCode?: string) {
    super(`AccessGrantModal request failed (${status}): ${detail}`);
    this.name = 'AccessGrantModalApiError';
    this.status = status;
    this.detail = detail;
    this.reasonCode = reasonCode;
    // Restore the prototype chain (extending built-ins across ES5/ts-jest
    // transpilation targets can otherwise break `instanceof` checks) — same
    // fix as communicationApi.ts's SendCommunicationError.
    Object.setPrototypeOf(this, AccessGrantModalApiError.prototype);
  }

  /** Builds an error from a non-OK {@link Response}. Never throws while parsing. */
  static async fromResponse(response: Response): Promise<AccessGrantModalApiError> {
    const status = response.status;
    try {
      const body = (await response.json()) as { reasonCode?: string; detail?: string; title?: string };
      const reasonCode = typeof body?.reasonCode === 'string' ? body.reasonCode : undefined;
      const detail = body?.detail ?? body?.title ?? `HTTP ${status}`;
      return new AccessGrantModalApiError(status, detail, reasonCode);
    } catch {
      return new AccessGrantModalApiError(status, `HTTP ${status}`);
    }
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
 * Write check itself failing, or the ordinary Write-required deny) — all four
 * mean the same thing to this UI: "you cannot manage access on this record
 * right now," which is the one designed banner state the project constraint
 * requires (not a raw error, not a retry loop).
 */
function classifyAccessFailure(err: unknown): { kind: 'delegation' | 'unauthenticated'; message: string } | null {
  if (!(err instanceof AccessGrantModalApiError)) return null;
  if (err.status === 401) {
    return { kind: 'unauthenticated', message: 'Your sign-in has expired. Refresh the page and try again.' };
  }
  if (err.status === 403 && err.reasonCode?.startsWith('sdap.access.deny.delegation_')) {
    return {
      kind: 'delegation',
      message: 'You need Write access on this record to change who else can access it.',
    };
  }
  return null;
}

/** The `RevokeAccessResponse` fields this modal reads (task-024 finding M2 /
 * owner directive 2026-09-10) — camelCase per the BFF's default STJ naming.
 * `speContainerOutcome` mirrors `SpeContainerRevokeOutcome`; only the values
 * a per-contact/org revoke from THIS modal can produce are named (an
 * organization-member breakdown is `speOrgMemberCleanup`, unused here). */
interface IRevokeAccessResponseBody {
  speContainerOutcome?: 'NotAttempted' | 'PermissionRemoved' | 'NoPermissionFound' | 'Failed';
  deactivatedCount?: number;
}

/**
 * Builds the revoke notice from the response body — the owner's 2026-09-10
 * directive: distinguish fully revoked / grant-revoked-but-file-access-may-
 * remain / nothing-revoked, and never discard `deactivatedCount`. Uses fields
 * the endpoint ALREADY returns today (no server change required).
 */
function buildRevokeNotice(
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
  anyNotifyPending: boolean;
  anyNarrowed: boolean;
}

/** Builds the post-batch notice for `Add (N)` — six distinct shapes ordered by
 * priority (a denial or a failure dominates a narrowed/notify-pending
 * success). Denial and failure are reported separately because they call for
 * different next actions: a failure invites retry; a denial does not (retrying
 * without Write on the record fails the same way). */
function buildGrantBatchNotice(outcome: IGrantBatchOutcome): { intent: 'success' | 'warning' | 'error'; text: string } {
  const { granted, selectedCount, failures, denied, anyNotifyPending, anyNarrowed } = outcome;

  if (denied) {
    return {
      intent: 'error',
      text: `Granted access to ${granted} of ${selectedCount} before access was denied. You need Write access on this record to grant more.`,
    };
  }
  if (failures > 0) {
    return {
      intent: 'error',
      text: `Granted access to ${granted} of ${selectedCount}; ${failures} failed. Please try again.`,
    };
  }
  if (anyNotifyPending && anyNarrowed) {
    return {
      intent: 'warning',
      text: `Granted access to ${granted} item(s). Some were narrowed to your own access level, and internal notify (deep-link) is not yet available for internal workforce contacts (escalated; see project notes).`,
    };
  }
  if (anyNotifyPending) {
    return {
      intent: 'warning',
      text: `Granted access to ${granted} item(s). Internal notify (deep-link) is not yet available for internal workforce contacts (escalated; see project notes).`,
    };
  }
  if (anyNarrowed) {
    return {
      intent: 'warning',
      text: `Granted access to ${granted} item(s). Some were narrowed to your own access level on this record (you can only grant what you hold).`,
    };
  }
  return { intent: 'success', text: `Granted access to ${granted} item(s).` };
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
  canGrantAccess = true,
  authenticatedFetch,
  fetchCandidates,
  fetchExistingGrants,
  fetchStandingContacts,
  // searchContacts / searchOrganizations remain in the props contract (SPA-host
  // fallback) but are unused by this PCF-hosted modal, which uses the native
  // advanced-lookup pickers below (task 073 UAT v1.0.24 #1/#4).
  pickContact,
  pickOrganization,
  // "+ User" native picker (task 065, FR-29) — mirrors pickContact/pickOrganization.
  pickUser,
  onOpenContact,
  isInternalContact,
  // onSetStandingGrant remains in the props contract but is unused: the standing
  // grant is now set on the Contact record itself, not here (task 073 UAT v1.0.24 #5).
  title = 'Manage Access',
  accessLevelOptions = DEFAULT_ACCESS_LEVEL_OPTIONS,
  defaultAccessLevel,
  accessPermissionState = 'standard',
  fetchSecureOwnerInfo,
}) => {
  const styles = useStyles();

  // Access-Permission sharing gate (task 043, FR-14 Option A). Deliberately
  // computed from the prop alone — never from `effectiveAccessLevel` or any
  // other per-grant `sprk_accesslevel` concept above, so the two stay
  // structurally independent (see the module doc comment).
  const grantsBlocked = accessPermissionState === 'restricted';

  const [loading, setLoading] = React.useState(false);
  const [candidates, setCandidates] = React.useState<IAccessGrantCandidate[]>([]);
  const [existingGrants, setExistingGrants] = React.useState<IAccessGrantRecord[]>([]);
  // Items (contacts OR organizations) checked to grant, keyed by contactId/orgId.
  const [selectedCandidateIds, setSelectedCandidateIds] = React.useState<Set<string>>(new Set());
  const [approving, setApproving] = React.useState(false);
  // A native advanced-lookup pick is in flight — used only to disable the
  // "+ Contact"/"+ Organization" buttons so a double-click can't open two panes.
  const [picking, setPicking] = React.useState(false);

  // Per-row access level (task 073 v1.0.23) — keyed by item id (contactId/orgId/systemUserId).
  // NO default: a row is not grantable until the admin picks its level ("Pick access level").
  const [rowLevels, setRowLevels] = React.useState<Record<string, number>>({});
  // Contacts + organizations + users staged via the native "+ Contact" /
  // "+ Organization" / "+ User" advanced lookup — appended into the "Add
  // Access Permissions" list as selectable rows.
  const [lookedUpContacts, setLookedUpContacts] = React.useState<IContactSearchResult[]>([]);
  const [lookedUpOrgs, setLookedUpOrgs] = React.useState<IOrganizationPick[]>([]);
  const [lookedUpUsers, setLookedUpUsers] = React.useState<IUserPick[]>([]);

  const [pendingRevoke, setPendingRevoke] = React.useState<PendingRevoke | null>(null);
  const [revoking, setRevoking] = React.useState(false);

  const [notice, setNotice] = React.useState<{ intent: 'success' | 'warning' | 'error'; text: string } | null>(null);

  // The designed 401/403 deny states (task 008 FR-07 delegation gate; project
  // constraint "the 403 delegation deny is a designed UI state, not a toast").
  // Persists across the SAME modal session (not per-call) so once the server
  // has said "no", every write action stays disabled until the modal is
  // reopened — reset alongside the rest of the transient state below.
  const [accessDenyState, setAccessDenyState] = React.useState<{
    kind: 'delegation' | 'unauthenticated';
    message: string;
  } | null>(null);

  // Secure-record owner/BU read-only display (task 065, design.md §6).
  const [secureOwnerInfo, setSecureOwnerInfo] = React.useState<ISecureOwnerInfo | null>(null);

  /** GETs a relative BFF path via the host `authenticatedFetch` and returns
   * the parsed JSON body. Throws {@link AccessGrantModalApiError} on a non-OK
   * response (task-024 finding M8) instead of reading the failure body as a
   * success. */
  const getJson = React.useCallback(
    async <T,>(path: string): Promise<T> => {
      const res = await authenticatedFetch(path, { method: 'GET' });
      if (!res.ok) throw await AccessGrantModalApiError.fromResponse(res);
      return (await res.json()) as T;
    },
    [authenticatedFetch]
  );

  /** Reads this record's internal system-user shares (task 063/065, FR-29) —
   * a direct BFF call (not host-injected, unlike fetchCandidates/
   * fetchExistingGrants): `GET /user-shares` is already entity-agnostic given
   * {recordType, recordId}, the same shape every write on this modal already
   * sends, so no new TrackingFieldTrio wiring is needed to read it. Mapped
   * into {@link IAccessGrantRecord} shape with `provenance: 'share'` so it
   * merges into the SAME Current Access list (task 066 owns full provenance
   * rendering; here the row only needs to exist, be labeled, and be
   * revocable). The Dataverse `contactId` field is reused to carry the
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
      shares: Array<{
        systemUserId: string;
        fullName?: string | null;
        accessLevel?: number | null;
        modifiedOn?: string;
      }>;
    }>(`/api/v1/external-access/user-shares?${query}`);
    return (data.shares ?? []).map(s => ({
      contactId: s.systemUserId,
      fullName: s.fullName ?? '(unknown user)',
      // Unmapped mask (a share holding rights outside the three levels) reads
      // as `null` server-side; 0 is a safe sentinel — it matches none of the
      // fixed ExternalAccessLevel option values, so the row falls through to
      // the "Custom" display below rather than rendering a raw `null`.
      accessLevel: s.accessLevel ?? 0,
      grantedDate: s.modifiedOn,
      provenance: 'share' as const,
    }));
  }, [getJson, recordType, recordId]);

  const loadData = React.useCallback(async () => {
    setLoading(true);
    setNotice(null);
    try {
      const [candidateList, grantList, standingList, userShareList, ownerInfo] = await Promise.all([
        fetchCandidates(),
        fetchExistingGrants(),
        // Standing-grant members (task 073 UAT #2) — optional; a host that
        // hasn't wired the flag omits it. Failing soft so a standing-read
        // problem (e.g. field-level-security denial) never blocks the modal.
        fetchStandingContacts ? fetchStandingContacts().catch(() => [] as IAccessGrantRecord[]) : Promise.resolve([]),
        // Internal system-user shares (task 065). A delegation 403 here is a
        // designed state (see fetchUserShares' doc comment above), not a
        // load failure — recorded via accessDenyState and the read fails
        // soft to an empty list so the rest of the modal still loads.
        fetchUserShares().catch(err => {
          const deny = classifyAccessFailure(err);
          if (deny) setAccessDenyState(deny);
          return [] as IAccessGrantRecord[];
        }),
        // Secure-record owner/BU (task 065, design.md §6) — optional; a host
        // that hasn't wired it, or a non-secure record, resolves to null.
        // Fails soft: this is a read-only display, never a blocking concern.
        fetchSecureOwnerInfo ? fetchSecureOwnerInfo().catch(() => null) : Promise.resolve(null),
      ]);
      // Union standing + user-share rows into Current Access, deduped by
      // contactId — an explicit per-record `sprk_externalrecordaccess` grant
      // (which carries an accessRecordId and IS revocable) wins over a
      // standing row for the same contact, so a contact with both shows once
      // and stays revocable. User-share rows key on a DIFFERENT id space
      // (systemUserId, reusing the `contactId` field per the doc comment
      // above) so they never collide with contact-keyed rows.
      const grantedContactIds = new Set(grantList.map(g => g.contactId));
      const standingOnly = standingList.filter(s => !grantedContactIds.has(s.contactId));
      setExistingGrants([...grantList, ...standingOnly, ...userShareList]);
      // Exclude both explicitly-granted AND standing members from the
      // candidate-approve list (they already have access).
      const currentAccessContactIds = new Set([...grantedContactIds, ...standingOnly.map(s => s.contactId)]);
      setCandidates(candidateList.filter(c => !currentAccessContactIds.has(c.contactId)));
      setSecureOwnerInfo(ownerInfo);
    } catch {
      setNotice({ intent: 'error', text: 'Failed to load access data. Close and reopen to retry.' });
    } finally {
      setLoading(false);
    }
  }, [fetchCandidates, fetchExistingGrants, fetchStandingContacts, fetchUserShares, fetchSecureOwnerInfo]);

  React.useEffect(() => {
    if (open && canGrantAccess) {
      setSelectedCandidateIds(new Set());
      setRowLevels({});
      setLookedUpContacts([]);
      setLookedUpOrgs([]);
      setLookedUpUsers([]);
      setPendingRevoke(null);
      // A prior session's deny/notice is not this session's — a fresh open
      // gets a fresh chance to load and write (server truth only; no
      // client-side memory of "you were denied last time").
      setAccessDenyState(null);
      void loadData();
    }
    // Only re-run when the modal transitions open (and once per open), not on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, canGrantAccess]);

  /** Posts a JSON body to a relative BFF path via the host `authenticatedFetch`
   * and returns the parsed response body. Throws {@link AccessGrantModalApiError}
   * on a non-OK response (task-024 finding M8) — callers wrap with a try/catch
   * that classifies the failure (see {@link classifyAccessFailure}) and surfaces
   * either the designed deny banner or a non-blocking notice. */
  const postJson = React.useCallback(
    async <T,>(path: string, body: unknown): Promise<T> => {
      const res = await authenticatedFetch(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      if (!res.ok) throw await AccessGrantModalApiError.fromResponse(res);
      return (await res.json()) as T;
    },
    [authenticatedFetch]
  );

  /** Outcome of a single {@link grantContact} call — the grant write itself
   * either succeeds or throws; `notifyPending` describes the one BEST-EFFORT,
   * non-blocking follow-on (NFR-06 — the escalated internal deep-link notify gap)
   * so callers can build one combined notice. */
  interface IGrantOutcome {
    notifyPending: boolean;
  }

  /**
   * The single grant core shared by candidate-approve and named-add (per the
   * task's "no duplicate write path" constraint). Classifies the contact
   * internal-vs-external and routes to the correct BUILT endpoint. Throws only
   * when the grant write itself fails; the notify concern is reported via the
   * returned flag so a caller driving multiple grants (approve-selected) can
   * aggregate one notice instead of each write overwriting the last.
   */
  const grantContact = React.useCallback(
    async (
      contact: { contactId: string; fullName: string; email?: string },
      opts: { level: number }
    ): Promise<IGrantOutcome> => {
      const [firstName, ...rest] = contact.fullName.trim().split(/\s+/);
      const lastName = rest.join(' ') || firstName;

      // Fail SAFE toward "internal" on a classification error: the
      // consequence of wrongly treating an external contact as internal is
      // a missed CIAM-invite email (annoying, recoverable — the operator can
      // re-invite from the grant list); the consequence of wrongly treating
      // an internal workforce person as external is an UNWANTED CIAM account
      // creation for someone who already has an M365 identity. The two
      // failure directions are not symmetric, so the catch must not default
      // to the more harmful one.
      const internal = await isInternalContact(contact.contactId).catch(() => true);
      let notifyPending = false;

      if (!internal && contact.email) {
        // External, known email → the built, atomic onboard+grant+CIAM-email endpoint.
        // Polymorphic root (task 070/071): send {recordType, recordId} — the BFF
        // binds the correct typed root lookup (project|matter|workassignment).
        await postJson('/api/v1/external-access/invite-and-grant', {
          email: contact.email,
          recordType,
          recordId,
          accessLevel: opts.level,
          firstName,
          lastName,
        });
      } else {
        // Internal workforce contact, or an external contact with no email on
        // file → the built grant-only core (no CIAM onboarding attempted).
        await postJson('/api/v1/external-access/grant', {
          contactId: contact.contactId,
          recordType,
          recordId,
          accessLevel: opts.level,
        });
        if (internal) {
          // Escalated gap — see the module doc comment. The grant itself
          // succeeded; only the deep-link notify step is unavailable.
          notifyPending = true;
        }
      }

      return { notifyPending };
    },
    [isInternalContact, postJson, recordId, recordType]
  );

  // Unified "Available Contacts & Organizations & Users" list (task 073 v1.0.23; task
  // 065 adds the 'user' kind): role-based membership candidates + any contacts/
  // organizations/users staged via the "+ Contact" / "+ Organization" / "+ User"
  // side pane. Each item carries its own per-row access level. Granted contacts are
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
    const byId = new Map<string, IAccessGrantCandidate>();
    for (const c of candidates) byId.set(c.contactId, c);
    for (const c of lookedUpContacts) {
      if (!byId.has(c.contactId)) {
        byId.set(c.contactId, { contactId: c.contactId, fullName: c.fullName, email: c.email, role: 'Looked up' });
      }
    }
    const items: IAvailableItem[] = [];
    for (const c of byId.values()) {
      items.push({
        id: c.contactId,
        name: c.fullName,
        meta: `${c.role}${c.email ? ` · ${c.email}` : ''}`,
        kind: 'contact',
        contact: { contactId: c.contactId, fullName: c.fullName, email: c.email },
      });
    }
    for (const o of lookedUpOrgs) {
      items.push({ id: o.id, name: o.name, meta: 'All organization contacts', kind: 'organization', org: o });
    }
    for (const u of lookedUpUsers) {
      items.push({ id: u.id, name: u.name, meta: 'Internal system user', kind: 'user', user: u });
    }
    return items;
  }, [candidates, lookedUpContacts, lookedUpOrgs, lookedUpUsers]);

  /** Writes a first-class ORGANIZATION grant (task 073 #7) — access for all contacts at the org.
   * `contactId` is omitted so the BFF treats (empty contact + organizationId) as an org grant; every
   * active member of the organization then inherits access at check time (server Term-3 union). */
  const grantOrganization = React.useCallback(
    async (org: IOrganizationPick, level: number): Promise<void> => {
      await postJson('/api/v1/external-access/grant', {
        recordType,
        recordId,
        accessLevel: level,
        organizationId: org.id,
      });
    },
    [postJson, recordType, recordId]
  );

  /** Writes an internal system-user POA SHARE (task 065, FR-29) via the task-063
   * endpoint — NOT the `/grant` core the contact/org flows above use (a share is a
   * different write; see the module doc comment). `narrowed: true` means the
   * caller's own rights on the record were narrower than the requested level, so
   * the share carries the intersection (owner decision 2026-09-16) — reported back
   * so the caller can build one combined notice, same pattern as grantContact's
   * `notifyPending`. */
  const shareUser = React.useCallback(
    async (user: IUserPick, level: number): Promise<{ narrowed: boolean }> => {
      const data = await postJson<{ narrowed?: boolean }>('/api/v1/external-access/share-user', {
        recordType,
        recordId,
        systemUserId: user.id,
        accessLevel: level,
      });
      return { narrowed: data?.narrowed === true };
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
    // Every selected row must have a level (no default) before it can be granted.
    const missing = selected.filter(it => rowLevels[it.id] === undefined);
    if (missing.length > 0) {
      setNotice({ intent: 'warning', text: `Pick an access level for: ${missing.map(m => m.name).join(', ')}.` });
      return false;
    }
    setApproving(true);
    let failures = 0;
    let granted = 0;
    let anyNotifyPending = false;
    let anyNarrowed = false;
    let denied = false;
    for (const it of selected) {
      const level = rowLevels[it.id];
      try {
        if (it.kind === 'contact' && it.contact) {
          const outcome = await grantContact(it.contact, { level });
          anyNotifyPending = anyNotifyPending || outcome.notifyPending;
        } else if (it.kind === 'organization' && it.org) {
          await grantOrganization(it.org, level);
        } else if (it.kind === 'user' && it.user) {
          const outcome = await shareUser(it.user, level);
          anyNarrowed = anyNarrowed || outcome.narrowed;
        }
        granted += 1;
      } catch (err) {
        // The delegation deny (project constraint: "a designed UI state ...
        // scope: all three write actions") is NOT counted as an ordinary
        // failure — it stops the batch and renders the persistent banner
        // instead of "N failed, try again" (retrying without Write would
        // just fail the same way for every remaining item).
        const deny = classifyAccessFailure(err);
        if (deny) {
          setAccessDenyState(deny);
          denied = true;
          break;
        }
        failures += 1;
      }
    }

    setApproving(false);
    setSelectedCandidateIds(new Set());
    setRowLevels({});
    setLookedUpContacts([]);
    setLookedUpOrgs([]);
    setLookedUpUsers([]);
    await loadData();

    setNotice(
      buildGrantBatchNotice({
        granted,
        selectedCount: selected.length,
        failures,
        denied,
        anyNotifyPending,
        anyNarrowed,
      })
    );
    // Success (for Save's close decision) iff nothing failed and access was not
    // denied partway — a notify-pending or narrowed grant still succeeded (the
    // access row/share was written).
    return !denied && failures === 0;
  }, [availableItems, selectedCandidateIds, rowLevels, grantContact, grantOrganization, shareUser, loadData]);

  // Save (task 073 UAT v1.0.29 #1B): if rows are staged but not yet added, COMMIT
  // them (respecting the level-required guard), then close on success; otherwise
  // just close. So the user's pending selection isn't silently lost on Save.
  const handleSave = React.useCallback(async () => {
    if (selectedCandidateIds.size === 0) {
      onClose();
      return;
    }
    const ok = await handleGrantSelected();
    if (ok) onClose();
  }, [selectedCandidateIds, handleGrantSelected, onClose]);

  // Cancel / × (task 073 UAT v1.0.29 #1B): warn if there are pending (staged,
  // not-yet-added) selections so they aren't silently discarded.
  const [showPendingWarning, setShowPendingWarning] = React.useState(false);
  const handleCancelAttempt = React.useCallback(() => {
    if (selectedCandidateIds.size > 0) setShowPendingWarning(true);
    else onClose();
  }, [selectedCandidateIds, onClose]);

  // ── Native advanced-lookup pickers (task 073 v1.0.24 #1/#4) ─────────────────
  // `+ Contact` / `+ Organization` open the host's NATIVE Dataverse advanced-lookup
  // side pane (Xrm.Utility.lookupObjects, injected as pickContact/pickOrganization)
  // — the same advanced-find surface the wizards use. The pick is staged into the
  // "Add Access Permissions" list and auto-selected. Works because the modal is
  // `nonBlocking` (no backdrop covering the page-level lookup pane).
  const openContactPicker = React.useCallback(async () => {
    if (!pickContact) return;
    setPicking(true);
    try {
      const picked = await pickContact();
      if (!picked) return;
      setLookedUpContacts(prev => (prev.some(c => c.contactId === picked.contactId) ? prev : [...prev, picked]));
      setSelectedCandidateIds(prev => new Set(prev).add(picked.contactId));
    } finally {
      setPicking(false);
    }
  }, [pickContact]);

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

  /** Opens the host's NATIVE advanced-lookup side pane for a single `systemuser`
   * (task 065, FR-29) — mirrors {@link openContactPicker}/{@link openOrgPicker}. */
  const openUserPicker = React.useCallback(async () => {
    if (!pickUser) return;
    setPicking(true);
    try {
      const picked = await pickUser();
      if (!picked) return;
      setLookedUpUsers(prev => (prev.some(u => u.id === picked.id) ? prev : [...prev, picked]));
      setSelectedCandidateIds(prev => new Set(prev).add(picked.id));
    } finally {
      setPicking(false);
    }
  }, [pickUser]);

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
        setNotice(buildRevokeNotice(fullName, data));
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
        setNotice({ intent: 'success', text: `Removed ${fullName}'s share.` });
      }
    } catch (err) {
      const deny = classifyAccessFailure(err);
      if (deny) {
        setAccessDenyState(deny);
        setPendingRevoke(null);
      } else {
        setNotice({
          intent: 'error',
          text: `Failed to revoke access for ${pendingRevoke.fullName}. Please try again.`,
        });
      }
    } finally {
      setRevoking(false);
    }
  }, [pendingRevoke, loadData, postJson, recordType, recordId]);

  const toggleCandidateSelected = (contactId: string) => {
    setSelectedCandidateIds(prev => {
      const next = new Set(prev);
      if (next.has(contactId)) next.delete(contactId);
      else next.add(contactId);
      return next;
    });
  };

  // Actions blocked by EITHER the Access-Permission sharing gate (task 043,
  // 'restricted' — unrelated to delegation) OR a server-confirmed 401/403 deny
  // (task 008/065 — the delegation gate). Both render their own banner below;
  // this union is only for disabling the interactive GRANT controls.
  const actionsBlocked = grantsBlocked || accessDenyState !== null;
  // Revoke is intentionally NOT gated by `grantsBlocked` — 'restricted' blocks
  // NEW grants only; reviewing + revoking EXISTING access stays available
  // (task 073 UAT #4's "still allows reviewing and revoking existing access").
  // The delegation/auth deny DOES block revoke — it is one of the "three write
  // actions" the project constraint names explicitly.
  const revokeBlocked = revoking || accessDenyState !== null;

  return (
    <>
      <SprkModal
        // task 073 v1.0.24 — the modal is `nonBlocking` (Fluent non-modal: no
        // backdrop, no focus trap) so the host's NATIVE advanced-lookup pane
        // (Xrm.Utility.lookupObjects, opened by "+ Contact"/"+ Organization")
        // renders on top and stays interactive instead of being covered by a
        // modal backdrop — the proven CommunicationActions composer pattern.
        open={open}
        onClose={handleCancelAttempt}
        title={title}
        size="lg"
        dismiss="explicit"
        nonBlocking
        // While a native advanced-lookup pane is open (picking), hide this surface
        // so the (higher-z-index) modal doesn't cover the lookup (task 073 UAT
        // v1.0.29 #1A). The modal stays mounted — staged picks survive.
        hidden={picking}
        footerStart={
          <Button appearance="secondary" onClick={handleCancelAttempt}>
            Cancel
          </Button>
        }
        footer={
          <Button appearance="primary" onClick={() => void handleSave()}>
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
                  {notice.text}
                </MessageBarBody>
              </MessageBar>
            )}

            {loading ? (
              <div className={styles.loadingRow}>
                <Spinner size="tiny" />
                <Text>Loading access data…</Text>
              </div>
            ) : (
              <>
                {/* Restricted banner (task 073 UAT #4) — light-red (error intent). */}
                {grantsBlocked && (
                  <MessageBar intent="error" style={{ marginBottom: tokens.spacingVerticalM }}>
                    <MessageBarBody>
                      <MessageBarTitle>Restricted Access</MessageBarTitle>
                      Only system users may have access. External users must be assigned system user licenses to access.
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
                      <MessageBarTitle>
                        {accessDenyState.kind === 'delegation' ? 'Write access required' : 'Sign-in expired'}
                      </MessageBarTitle>
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

                {/* Add Access Permissions (task 073 UAT v1.0.24 #2; task 065 adds "+ User") —
                    role-based members + looked-up contacts/orgs/users. "+ Contact" / "+ Organization" /
                    "+ User" (icon-only, #4) open the NATIVE advanced-lookup pane. Select, choose a
                    level, Add. */}
                <div className={styles.section} style={{ marginTop: tokens.spacingVerticalL }}>
                  <div className={styles.sectionHeaderRow}>
                    <Text className={styles.sectionTitle}>Add Access Permissions</Text>
                    <div className={styles.sectionHeaderActions}>
                      {/* Icon-only "+" triggers (task 073 UAT v1.0.24 #4; task 065 adds "+ User") —
                          open the native advanced-lookup pane (pickContact/pickOrganization/pickUser). */}
                      {pickContact && (
                        <Tooltip content="Add contact" relationship="label">
                          <Button
                            appearance="secondary"
                            size="small"
                            icon={<PersonRegular />}
                            onClick={() => void openContactPicker()}
                            disabled={actionsBlocked || picking}
                            aria-label="Add contact"
                          >
                            +
                          </Button>
                        </Tooltip>
                      )}
                      {pickOrganization && (
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
                      {pickUser && (
                        <Tooltip content="Add user" relationship="label">
                          <Button
                            appearance="secondary"
                            size="small"
                            icon={<PersonAccountsRegular />}
                            onClick={() => void openUserPicker()}
                            disabled={actionsBlocked || picking}
                            aria-label="Add user"
                          >
                            +
                          </Button>
                        </Tooltip>
                      )}
                    </div>
                  </div>

                  {/* Padding below the header row, before the list (task 073 UAT v1.0.24 #3). */}
                  <div className={styles.listArea}>
                    {availableItems.length === 0 ? (
                      <Text className={styles.emptyState}>
                        No contacts, organizations or users yet. Use “+ Contact”, “+ Organization” or “+ User” to add.
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
                      disabled={selectedCandidateIds.size === 0 || approving || actionsBlocked}
                      icon={approving ? <Spinner size="tiny" /> : undefined}
                      onClick={handleGrantSelected}
                    >
                      Add ({selectedCandidateIds.size})
                    </Button>
                  </div>
                </div>

                {/* Current Access (task 073 UAT v1.0.24 #7 — extra top padding above the section) */}
                <div className={styles.section} style={{ marginTop: tokens.spacingVerticalXXL }}>
                  <Text className={styles.sectionTitle}>Current Access</Text>
                  <div className={styles.listArea}>
                    {existingGrants.length === 0 ? (
                      <Text className={styles.emptyState}>No active grants for this record.</Text>
                    ) : (
                      existingGrants.map(grant => {
                        // Internal system-user POA share (task 065) — checked FIRST: a share also
                        // carries no accessRecordId, so it must not fall into the standing branch below.
                        const isUserShare = grant.provenance === 'share';
                        // Standing-grant rows (task 073 UAT #2) confer ongoing
                        // membership via the contact's global `sprk_standinggrant`
                        // flag — there is NO per-record `sprk_externalrecordaccess`
                        // row to revoke here, so they render non-revocable with a
                        // "Standing" badge instead of an access-level + Revoke.
                        const isStanding = !isUserShare && (grant.provenance === 'standing' || !grant.accessRecordId);
                        // Organization grant (task 073 #7): everyone at the firm inherits access. Unlike a
                        // standing grant it IS a real per-record row, so it keeps the level badge + Revoke.
                        const isOrg = grant.provenance === 'organization';
                        const rowKey = grant.accessRecordId
                          ? grant.accessRecordId
                          : isUserShare
                            ? `share-${grant.contactId}`
                            : `standing-${grant.contactId}`;
                        return (
                          <div className={styles.row} key={rowKey}>
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
                                  ? `Internal user share — last updated ${formatGrantDate(grant.grantedDate)}`
                                  : isStanding
                                    ? 'Standing grant — ongoing access to assigned records'
                                    : isOrg
                                      ? 'Organization grant — all organization contacts have access'
                                      : `Granted by ${grant.grantedByName ?? 'unknown'} on ${formatGrantDate(grant.grantedDate)}`}
                              </Text>
                            </div>
                            <div className={styles.rowActions}>
                              {isStanding ? (
                                <Badge appearance="tint" color="success">
                                  Standing
                                </Badge>
                              ) : isUserShare ? (
                                <>
                                  <Badge appearance="tint" color="brand">
                                    {accessLevelOptions.find(o => o.value === grant.accessLevel)?.label ?? 'Custom'}
                                  </Badge>
                                  <Badge appearance="outline" size="small">
                                    User (share)
                                  </Badge>
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
                                </>
                              ) : (
                                <>
                                  <Badge appearance="tint" color="informative">
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
