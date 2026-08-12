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
 */

import * as React from 'react';
import {
  Button,
  Checkbox,
  Combobox,
  Option,
  Dropdown,
  Field,
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Badge,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import { PersonRegular, DismissCircleRegular, BuildingRegular, DismissRegular } from '@fluentui/react-icons';
import { SprkModal } from '../SprkModal';
// Inline record picker (task 073 UAT #4) — the wizard pattern: renders its
// result list as an inline dropdown INSIDE the modal body (no portal, no
// Xrm side-pane), so it stacks within the modal and never has to hide it.
import { LookupField } from '../LookupField';
import type { ILookupItem } from '../../types/LookupTypes';
import type {
  IAccessGrantModalProps,
  IAccessGrantCandidate,
  IAccessGrantRecord,
  IContactSearchResult,
  IOrganizationPick,
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
  pickerRow: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
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
});

/** Formats an ISO date string for display; falls back to the raw value when
 * parsing fails, and to an em-dash when absent. */
function formatGrantDate(iso: string | undefined): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

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
  searchContacts,
  searchOrganizations,
  isInternalContact,
  onSetStandingGrant,
  title = 'Manage Access',
  accessLevelOptions = DEFAULT_ACCESS_LEVEL_OPTIONS,
  defaultAccessLevel,
  accessPermissionState = 'standard',
}) => {
  const styles = useStyles();
  const effectiveAccessLevel =
    defaultAccessLevel ?? accessLevelOptions[0]?.value ?? DEFAULT_ACCESS_LEVEL_OPTIONS[0].value;

  // Per-grant access level chosen in the modal (task 073 UAT #4). Defaults to the effective level; the
  // admin can raise it to Collaborate / Full Access. Every grant written from this modal uses this value.
  const [selectedLevel, setSelectedLevel] = React.useState<number>(effectiveAccessLevel);
  const selectedLevelLabel = accessLevelOptions.find(o => o.value === selectedLevel)?.label ?? String(selectedLevel);

  // Access-Permission sharing gate (task 043, FR-14 Option A). Deliberately
  // computed from the prop alone — never from `effectiveAccessLevel` or any
  // other per-grant `sprk_accesslevel` concept above, so the two stay
  // structurally independent (see the module doc comment).
  const grantsBlocked = accessPermissionState === 'restricted';
  const standingGrantAllowed = accessPermissionState === 'standard';

  const [loading, setLoading] = React.useState(false);
  const [candidates, setCandidates] = React.useState<IAccessGrantCandidate[]>([]);
  const [existingGrants, setExistingGrants] = React.useState<IAccessGrantRecord[]>([]);
  const [selectedCandidateIds, setSelectedCandidateIds] = React.useState<Set<string>>(new Set());
  const [standingForCandidate, setStandingForCandidate] = React.useState<Set<string>>(new Set());
  const [approving, setApproving] = React.useState(false);

  const [searchQuery, setSearchQuery] = React.useState('');
  const [searchResults, setSearchResults] = React.useState<IContactSearchResult[]>([]);
  const [searching, setSearching] = React.useState(false);
  const [selectedNamed, setSelectedNamed] = React.useState<IContactSearchResult | null>(null);
  const [standingForNamed, setStandingForNamed] = React.useState(false);
  const [addingNamed, setAddingNamed] = React.useState(false);
  // Optional grantee firm/org (task 071) sent as `organizationId`. Picked via
  // the in-app org `LookupField` (task 073 UAT #4 — no more side-pane).
  const [pickedOrg, setPickedOrg] = React.useState<IOrganizationPick | null>(null);
  // Contacts looked up via the in-app "Add contact" LookupField (task 073 UAT
  // #4/#5) — staged into the "Available Contacts & Organizations" list alongside
  // the role-based membership candidates, then selected + granted.
  const [lookedUpContacts, setLookedUpContacts] = React.useState<IContactSearchResult[]>([]);

  const [revokeTargetId, setRevokeTargetId] = React.useState<string | null>(null);
  const [revoking, setRevoking] = React.useState(false);

  const [notice, setNotice] = React.useState<{ intent: 'success' | 'warning' | 'error'; text: string } | null>(null);

  const loadData = React.useCallback(async () => {
    setLoading(true);
    setNotice(null);
    try {
      const [candidateList, grantList, standingList] = await Promise.all([
        fetchCandidates(),
        fetchExistingGrants(),
        // Standing-grant members (task 073 UAT #2) — optional; a host that
        // hasn't wired the flag omits it. Failing soft so a standing-read
        // problem (e.g. field-level-security denial) never blocks the modal.
        fetchStandingContacts ? fetchStandingContacts().catch(() => [] as IAccessGrantRecord[]) : Promise.resolve([]),
      ]);
      // Union standing rows into Current Access, deduped by contactId — an
      // explicit per-record `sprk_externalrecordaccess` grant (which carries an
      // accessRecordId and IS revocable) wins over a standing row for the same
      // contact, so a contact with both shows once and stays revocable.
      const grantedContactIds = new Set(grantList.map(g => g.contactId));
      const standingOnly = standingList.filter(s => !grantedContactIds.has(s.contactId));
      setExistingGrants([...grantList, ...standingOnly]);
      // Exclude both explicitly-granted AND standing members from the
      // candidate-approve list (they already have access).
      const currentAccessContactIds = new Set([...grantedContactIds, ...standingOnly.map(s => s.contactId)]);
      setCandidates(candidateList.filter(c => !currentAccessContactIds.has(c.contactId)));
    } catch {
      setNotice({ intent: 'error', text: 'Failed to load access data. Close and reopen to retry.' });
    } finally {
      setLoading(false);
    }
  }, [fetchCandidates, fetchExistingGrants, fetchStandingContacts]);

  React.useEffect(() => {
    if (open && canGrantAccess) {
      setSelectedCandidateIds(new Set());
      setStandingForCandidate(new Set());
      setSearchQuery('');
      setSearchResults([]);
      setSelectedNamed(null);
      setStandingForNamed(false);
      setPickedOrg(null);
      setLookedUpContacts([]);
      setSelectedLevel(effectiveAccessLevel);
      void loadData();
    }
    // Only re-run when the modal transitions open (and once per open), not on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, canGrantAccess]);

  /** Posts a JSON body to a relative BFF path via the host `authenticatedFetch`
   * and returns the parsed response body. Throws (message-bearing) on failure —
   * callers wrap with a try/catch that surfaces a non-blocking notice. */
  const postJson = React.useCallback(
    async <T,>(path: string, body: unknown): Promise<T> => {
      const res = await authenticatedFetch(path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      });
      return (await res.json()) as T;
    },
    [authenticatedFetch]
  );

  /** Outcome of a single {@link grantContact} call — the grant write itself
   * either succeeds or throws; these flags describe the two BEST-EFFORT,
   * non-blocking follow-ons (NFR-06) so callers can build one combined notice
   * instead of each sub-step clobbering the other's message. */
  interface IGrantOutcome {
    notifyPending: boolean;
    standingGrantFailed: boolean;
  }

  /**
   * The single grant core shared by candidate-approve and named-add (per the
   * task's "no duplicate write path" constraint). Classifies the contact
   * internal-vs-external, routes to the correct BUILT endpoint, then
   * best-effort applies the standing-grant flag (NFR-06 — never rolls back
   * the grant on a standing-grant-write or notify failure). Throws only when
   * the grant write itself fails; the two follow-on concerns are reported via
   * the returned flags so a caller driving multiple grants (approve-selected)
   * can aggregate one notice instead of each write overwriting the last.
   */
  const grantContact = React.useCallback(
    async (
      contact: { contactId: string; fullName: string; email?: string },
      opts: { standingGrant: boolean; organizationId?: string }
    ): Promise<IGrantOutcome> => {
      const [firstName, ...rest] = contact.fullName.trim().split(/\s+/);
      const lastName = rest.join(' ') || firstName;
      // Optional grantee firm/org (task 070/071). Spread into the body only when
      // set, so a grant without an org selection omits the key entirely.
      const orgFields = opts.organizationId ? { organizationId: opts.organizationId } : {};

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
          accessLevel: selectedLevel,
          firstName,
          lastName,
          ...orgFields,
        });
      } else {
        // Internal workforce contact, or an external contact with no email on
        // file → the built grant-only core (no CIAM onboarding attempted).
        await postJson('/api/v1/external-access/grant', {
          contactId: contact.contactId,
          recordType,
          recordId,
          accessLevel: selectedLevel,
          ...orgFields,
        });
        if (internal) {
          // Escalated gap — see the module doc comment. The grant itself
          // succeeded; only the deep-link notify step is unavailable.
          notifyPending = true;
        }
      }

      let standingGrantFailed = false;
      if (opts.standingGrant && onSetStandingGrant) {
        try {
          await onSetStandingGrant(contact.contactId, true);
        } catch {
          standingGrantFailed = true;
        }
      }

      return { notifyPending, standingGrantFailed };
    },
    [selectedLevel, isInternalContact, onSetStandingGrant, postJson, recordId, recordType]
  );

  const handleApproveSelected = React.useCallback(async () => {
    if (selectedCandidateIds.size === 0) return;
    setApproving(true);
    const toApprove = candidates.filter(c => selectedCandidateIds.has(c.contactId));
    let failures = 0;
    let anyNotifyPending = false;
    let anyStandingGrantFailed = false;
    for (const candidate of toApprove) {
      try {
        const outcome = await grantContact(candidate, {
          standingGrant: standingForCandidate.has(candidate.contactId),
          organizationId: pickedOrg?.id,
        });
        anyNotifyPending = anyNotifyPending || outcome.notifyPending;
        anyStandingGrantFailed = anyStandingGrantFailed || outcome.standingGrantFailed;
      } catch {
        failures += 1;
      }
    }
    setApproving(false);
    await loadData();

    if (failures > 0) {
      setNotice({
        intent: 'error',
        text: `${toApprove.length - failures} of ${toApprove.length} grant(s) succeeded; ${failures} failed. Try again for the failed contact(s).`,
      });
    } else if (anyNotifyPending) {
      setNotice({
        intent: 'warning',
        text: `Granted access to ${toApprove.length} contact(s). Internal notify (deep-link) is not yet available for internal workforce contacts — they will not receive an automatic notification (escalated; see project notes).`,
      });
    } else if (anyStandingGrantFailed) {
      setNotice({
        intent: 'warning',
        text: `Granted access to ${toApprove.length} contact(s), but setting the standing-grant flag failed for at least one. You can retry from the Contact form.`,
      });
    } else {
      setNotice({ intent: 'success', text: `Granted access to ${toApprove.length} contact(s).` });
    }
  }, [candidates, grantContact, loadData, selectedCandidateIds, standingForCandidate, pickedOrg]);

  const handleAddNamed = React.useCallback(async () => {
    if (!selectedNamed) return;
    setAddingNamed(true);
    try {
      const outcome = await grantContact(selectedNamed, {
        standingGrant: standingForNamed,
        organizationId: pickedOrg?.id,
      });
      const grantedName = selectedNamed.fullName;
      setSelectedNamed(null);
      setSearchQuery('');
      setSearchResults([]);
      setStandingForNamed(false);
      await loadData();
      if (outcome.notifyPending) {
        setNotice({
          intent: 'warning',
          text: `${grantedName} was granted access. Internal notify (deep-link) is not yet available — the contact will not receive an automatic notification (escalated; see project notes).`,
        });
      } else if (outcome.standingGrantFailed) {
        setNotice({
          intent: 'warning',
          text: `${grantedName} was granted access, but setting the standing-grant flag failed. You can retry from the Contact form.`,
        });
      } else {
        setNotice({ intent: 'success', text: `Granted access to ${grantedName}.` });
      }
    } catch {
      setNotice({ intent: 'error', text: `Failed to grant access to ${selectedNamed.fullName}. Please try again.` });
    } finally {
      setAddingNamed(false);
    }
  }, [grantContact, loadData, selectedNamed, standingForNamed, pickedOrg]);

  // Unified "Available Contacts & Organizations" list (task 073 UAT #5): role-based membership
  // candidates + any contacts staged via "+ Contact". Granted contacts are excluded by loadData().
  const availableContacts = React.useMemo<IAccessGrantCandidate[]>(() => {
    const merged = [...candidates];
    for (const c of lookedUpContacts) {
      if (!merged.some(m => m.contactId === c.contactId)) {
        merged.push({ contactId: c.contactId, fullName: c.fullName, email: c.email, role: 'Looked up' });
      }
    }
    return merged;
  }, [candidates, lookedUpContacts]);

  /** Writes a first-class ORGANIZATION grant (task 073 #7) — access for EVERYONE at the picked firm.
   * `contactId` is omitted so the BFF treats (empty contact + organizationId) as an org grant; every
   * active member of the firm then inherits access at check time (server Term-3 union). */
  const grantOrganization = React.useCallback(async (): Promise<void> => {
    if (!pickedOrg) return;
    await postJson('/api/v1/external-access/grant', {
      recordType,
      recordId,
      accessLevel: selectedLevel,
      organizationId: pickedOrg.id,
    });
  }, [pickedOrg, postJson, recordType, recordId, selectedLevel]);

  const handleGrantSelected = React.useCallback(async () => {
    const toGrant = availableContacts.filter(c => selectedCandidateIds.has(c.contactId));
    const orgToGrant = pickedOrg; // capture before reset (used for the write + the notice)
    if (toGrant.length === 0 && !orgToGrant) return;
    setApproving(true);
    let failures = 0;
    let anyNotifyPending = false;
    let anyStandingGrantFailed = false;
    for (const contact of toGrant) {
      try {
        const outcome = await grantContact(contact, {
          standingGrant: standingForCandidate.has(contact.contactId),
          // Org selection now writes a first-class ORG grant (below), not per-contact scope metadata.
        });
        anyNotifyPending = anyNotifyPending || outcome.notifyPending;
        anyStandingGrantFailed = anyStandingGrantFailed || outcome.standingGrantFailed;
      } catch {
        failures += 1;
      }
    }

    // Organization grant (task 073 #7) — grants access to EVERYONE at the firm.
    let orgGranted = false;
    let orgFailed = false;
    if (orgToGrant) {
      try {
        await grantOrganization();
        orgGranted = true;
      } catch {
        orgFailed = true;
      }
    }

    setApproving(false);
    setLookedUpContacts([]);
    setPickedOrg(null);
    await loadData();

    const granted: string[] = [];
    if (toGrant.length - failures > 0) granted.push(`${toGrant.length - failures} contact(s)`);
    if (orgGranted) granted.push(`everyone at ${orgToGrant!.name}`);
    const grantedText = granted.length > 0 ? granted.join(' and ') : 'no one';

    if (failures > 0 || orgFailed) {
      const failed: string[] = [];
      if (failures > 0) failed.push(`${failures} contact grant(s)`);
      if (orgFailed) failed.push('the organization grant');
      setNotice({
        intent: 'error',
        text: `Granted ${selectedLevelLabel} access to ${grantedText}; ${failed.join(' and ')} failed. Please try again.`,
      });
    } else if (anyNotifyPending) {
      setNotice({
        intent: 'warning',
        text: `Granted ${selectedLevelLabel} access to ${grantedText}. Internal notify (deep-link) is not yet available for internal workforce contacts (escalated; see project notes).`,
      });
    } else if (anyStandingGrantFailed) {
      setNotice({
        intent: 'warning',
        text: `Granted ${selectedLevelLabel} access to ${grantedText}, but setting the standing-grant flag failed for at least one contact. You can retry from the Contact form.`,
      });
    } else {
      setNotice({ intent: 'success', text: `Granted ${selectedLevelLabel} access to ${grantedText}.` });
    }
  }, [
    availableContacts,
    selectedCandidateIds,
    grantContact,
    grantOrganization,
    standingForCandidate,
    pickedOrg,
    loadData,
    selectedLevelLabel,
  ]);

  const handleSearchChange = React.useCallback(
    (query: string) => {
      setSearchQuery(query);
      setSelectedNamed(null);
      if (!query || query.trim().length < 2) {
        setSearchResults([]);
        return;
      }
      setSearching(true);
      searchContacts(query)
        .then(results => setSearchResults(results))
        .catch(() => setSearchResults([]))
        .finally(() => setSearching(false));
    },
    [searchContacts]
  );

  const confirmRevoke = React.useCallback(async () => {
    const target = existingGrants.find(g => g.accessRecordId === revokeTargetId);
    if (!target) {
      setRevokeTargetId(null);
      return;
    }
    setRevoking(true);
    try {
      // Revoke is root-agnostic (task 070): it deactivates by accessRecordId +
      // contactId and no longer requires a root id, so no recordType/recordId is sent.
      await postJson('/api/v1/external-access/revoke', {
        accessRecordId: target.accessRecordId,
        contactId: target.contactId,
      });
      setRevokeTargetId(null);
      await loadData();
      setNotice({ intent: 'success', text: `Revoked access for ${target.fullName}.` });
    } catch {
      setNotice({ intent: 'error', text: `Failed to revoke access for ${target.fullName}. Please try again.` });
    } finally {
      setRevoking(false);
    }
  }, [existingGrants, loadData, postJson, revokeTargetId]);

  // In-app contact picker (task 073 UAT #4). `LookupField` returns `{id, name}`
  // only, but the grant routing needs the contact's EMAIL (external-vs-internal),
  // so cache each search result by id and resolve the full record on pick.
  const contactSearchCacheRef = React.useRef<Map<string, IContactSearchResult>>(new Map());

  /** onSearch for the contact `LookupField` — runs the host `searchContacts`,
   * caches full results (for email), and maps to `ILookupItem` for the field. */
  const handleSearchContactsForLookup = React.useCallback(
    async (query: string): Promise<ILookupItem[]> => {
      const results = await searchContacts(query);
      for (const r of results) contactSearchCacheRef.current.set(r.contactId, r);
      return results.map(r => ({ id: r.contactId, name: r.fullName }));
    },
    [searchContacts]
  );

  /** onChange for the contact `LookupField` — stages the picked contact into the
   * Available list (with its cached email) and pre-selects it. Runs entirely
   * in-modal (no side-pane, no modal-hide). */
  const handleAddLookedUpContact = React.useCallback((item: ILookupItem | null) => {
    if (!item) return;
    const full = contactSearchCacheRef.current.get(item.id) ?? { contactId: item.id, fullName: item.name };
    setLookedUpContacts(prev => (prev.some(c => c.contactId === full.contactId) ? prev : [...prev, full]));
    setSelectedCandidateIds(prev => {
      const next = new Set(prev);
      next.add(full.contactId);
      return next;
    });
  }, []);

  /** onChange for the organization `LookupField` — sets the optional grantee
   * firm/org (sent as `organizationId`). Clearing the field clears the scope. */
  const handlePickOrgLookup = React.useCallback((item: ILookupItem | null) => {
    setPickedOrg(item ? { id: item.id, name: item.name } : null);
  }, []);

  const toggleCandidateSelected = (contactId: string) => {
    setSelectedCandidateIds(prev => {
      const next = new Set(prev);
      if (next.has(contactId)) next.delete(contactId);
      else next.add(contactId);
      return next;
    });
  };

  const toggleCandidateStanding = (contactId: string) => {
    setStandingForCandidate(prev => {
      const next = new Set(prev);
      if (next.has(contactId)) next.delete(contactId);
      else next.add(contactId);
      return next;
    });
  };

  const revokeTarget = existingGrants.find(g => g.accessRecordId === revokeTargetId) ?? null;

  return (
    <>
      <SprkModal
        // task 073 UAT #4 — the modal no longer hides for picking: contact + org
        // pickers are inline `LookupField`s that render their result list INSIDE
        // the modal body (no Xrm side-pane behind the portal). So `open` is just
        // the prop.
        open={open}
        onClose={onClose}
        title={title}
        size="lg"
        dismiss="explicit"
        footerStart={
          <Button appearance="secondary" onClick={onClose}>
            Cancel
          </Button>
        }
        footer={
          <Button appearance="primary" onClick={onClose}>
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

                {/* Available Contacts & Organizations (task 073 UAT #5) — role-based members + looked-up
                    contacts, with "+ Contact" / "+ Organization" in the header. Select, choose a level, Add. */}
                <div className={styles.section}>
                  <div className={styles.sectionHeaderRow}>
                    <Text className={styles.sectionTitle}>Available Contacts &amp; Organizations</Text>
                  </div>
                  <Text className={styles.sectionSubtitle}>
                    Role-assigned members and any contacts you look up. Search to add a contact (or organization),
                    select who to grant, choose an access level, then Add.
                  </Text>
                  {/* In-app pickers (task 073 UAT #4) — inline `LookupField`s that
                      drop their result list INSIDE the modal body (the wizard
                      pattern: no Xrm side-pane, no modal-hide). Contact search
                      reuses the host `searchContacts`; the org picker uses
                      `searchOrganizations` when the host wires it. */}
                  <LookupField
                    label="Add contact"
                    value={null}
                    onChange={handleAddLookedUpContact}
                    onSearch={handleSearchContactsForLookup}
                    minSearchLength={2}
                  />
                  {searchOrganizations && (
                    <LookupField
                      label="Add organization"
                      value={null}
                      onChange={handlePickOrgLookup}
                      onSearch={searchOrganizations}
                      minSearchLength={2}
                    />
                  )}

                  {/* Selected organization (task 073 #7) — clicking Add grants access to EVERYONE at this
                      firm (a first-class org grant), not just firm-scoping metadata on a contact grant. */}
                  {pickedOrg && (
                    <div className={styles.row}>
                      <BuildingRegular />
                      <div className={styles.rowMain}>
                        <Text className={styles.rowName}>{pickedOrg.name}</Text>
                        <Text className={styles.rowMeta}>Grants access to everyone at this firm</Text>
                      </div>
                      <Button
                        appearance="subtle"
                        size="small"
                        icon={<DismissRegular />}
                        aria-label="Clear organization"
                        onClick={() => setPickedOrg(null)}
                        disabled={grantsBlocked}
                      />
                    </div>
                  )}

                  {availableContacts.length === 0 ? (
                    <Text className={styles.emptyState}>No available contacts. Use “+ Contact” to look one up.</Text>
                  ) : (
                    availableContacts.map(candidate => (
                      <div className={styles.row} key={candidate.contactId}>
                        <Checkbox
                          checked={selectedCandidateIds.has(candidate.contactId)}
                          onChange={() => toggleCandidateSelected(candidate.contactId)}
                          aria-label={`Select ${candidate.fullName}`}
                          disabled={grantsBlocked}
                        />
                        <div className={styles.rowMain}>
                          <Text className={styles.rowName}>{candidate.fullName}</Text>
                          <Text className={styles.rowMeta}>
                            {candidate.role}
                            {candidate.email ? ` · ${candidate.email}` : ''}
                          </Text>
                        </div>
                        <div className={styles.rowActions}>
                          {onSetStandingGrant && standingGrantAllowed && (
                            <Checkbox
                              label="Standing grant"
                              checked={standingForCandidate.has(candidate.contactId)}
                              onChange={() => toggleCandidateStanding(candidate.contactId)}
                              disabled={!selectedCandidateIds.has(candidate.contactId)}
                            />
                          )}
                        </div>
                      </div>
                    ))
                  )}

                  {/* Access level + Add (task 073 UAT #4) */}
                  <div className={styles.levelRow}>
                    <Field label="Access level" className={styles.levelField}>
                      <Dropdown
                        value={selectedLevelLabel}
                        selectedOptions={[String(selectedLevel)]}
                        disabled={grantsBlocked}
                        onOptionSelect={(_, data) => {
                          if (data.optionValue) setSelectedLevel(Number(data.optionValue));
                        }}
                      >
                        {accessLevelOptions.map(o => (
                          <Option key={o.value} value={String(o.value)} text={o.label}>
                            {o.label}
                          </Option>
                        ))}
                      </Dropdown>
                    </Field>
                    <Button
                      appearance="primary"
                      // Enabled when at least one contact is selected OR an organization is picked
                      // (an org-only grant is valid — it grants the whole firm; task 073 #7).
                      disabled={(selectedCandidateIds.size === 0 && !pickedOrg) || approving || grantsBlocked}
                      icon={approving ? <Spinner size="tiny" /> : undefined}
                      onClick={handleGrantSelected}
                    >
                      Add ({selectedCandidateIds.size + (pickedOrg ? 1 : 0)})
                    </Button>
                  </div>
                </div>

                {/* Current Access (task 073 UAT #5 — 20px header) */}
                <div className={styles.section}>
                  <Text className={styles.sectionTitle}>Current Access</Text>
                  {existingGrants.length === 0 ? (
                    <Text className={styles.emptyState}>No active grants for this record.</Text>
                  ) : (
                    existingGrants.map(grant => {
                      // Standing-grant rows (task 073 UAT #2) confer ongoing
                      // membership via the contact's global `sprk_standinggrant`
                      // flag — there is NO per-record `sprk_externalrecordaccess`
                      // row to revoke here, so they render non-revocable with a
                      // "Standing" badge instead of an access-level + Revoke.
                      const isStanding = grant.provenance === 'standing' || !grant.accessRecordId;
                      // Organization grant (task 073 #7): everyone at the firm inherits access. Unlike a
                      // standing grant it IS a real per-record row, so it keeps the level badge + Revoke.
                      const isOrg = grant.provenance === 'organization';
                      return (
                        <div className={styles.row} key={grant.accessRecordId ?? `standing-${grant.contactId}`}>
                          <div className={styles.rowMain}>
                            <Text className={styles.rowName}>{grant.fullName}</Text>
                            <Text className={styles.rowMeta}>
                              {isStanding
                                ? 'Standing grant — ongoing access to assigned records'
                                : isOrg
                                  ? 'Organization grant — everyone at this firm has access'
                                  : `Granted by ${grant.grantedByName ?? 'unknown'} on ${formatGrantDate(grant.grantedDate)}`}
                            </Text>
                          </div>
                          <div className={styles.rowActions}>
                            {isStanding ? (
                              <Badge appearance="tint" color="success">
                                Standing
                              </Badge>
                            ) : (
                              <>
                                <Badge appearance="tint" color="informative">
                                  {accessLevelOptions.find(o => o.value === grant.accessLevel)?.label ??
                                    grant.accessLevel}
                                </Badge>
                                <Button
                                  appearance="subtle"
                                  size="small"
                                  onClick={() => setRevokeTargetId(grant.accessRecordId!)}
                                  disabled={revoking}
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
              </>
            )}
          </>
        )}
      </SprkModal>

      {/* Revoke confirm — a second, stacked Fluent Dialog is supported (unlike
          the OOB-navigateTo-inside-a-Fluent-dialog anti-pattern, which mixes
          chrome families; this nests two proprietary Fluent v9 dialogs). */}
      <SprkModal
        open={revokeTarget !== null}
        onClose={() => setRevokeTargetId(null)}
        title="Revoke access?"
        size="xs"
        dismiss="alert"
        maximizable={false}
        footerStart={
          <Button appearance="secondary" onClick={() => setRevokeTargetId(null)} disabled={revoking}>
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
          Revoke access for <strong>{revokeTarget?.fullName}</strong>? They will immediately lose access to this record
          (unless a standing grant or other membership still applies).
        </Text>
      </SprkModal>
    </>
  );
};

export default AccessGrantModal;
