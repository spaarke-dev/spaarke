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
import { PersonRegular, DismissCircleRegular, BuildingRegular } from '@fluentui/react-icons';
import { SprkModal } from '../SprkModal';
// Record picker (task 073 v1.0.24) — "+ Contact" / "+ Organization" open the host's
// NATIVE Dataverse advanced-lookup side pane (Xrm.Utility.lookupObjects, injected as
// pickContact/pickOrganization) — the same advanced-find surface the wizards use. The
// modal renders `nonBlocking` so that page-level pane is not covered by a backdrop.
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
  // searchContacts / searchOrganizations remain in the props contract (SPA-host
  // fallback) but are unused by this PCF-hosted modal, which uses the native
  // advanced-lookup pickers below (task 073 UAT v1.0.24 #1/#4).
  pickContact,
  pickOrganization,
  onOpenContact,
  isInternalContact,
  // onSetStandingGrant remains in the props contract but is unused: the standing
  // grant is now set on the Contact record itself, not here (task 073 UAT v1.0.24 #5).
  title = 'Manage Access',
  accessLevelOptions = DEFAULT_ACCESS_LEVEL_OPTIONS,
  defaultAccessLevel,
  accessPermissionState = 'standard',
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

  // Per-row access level (task 073 v1.0.23) — keyed by item id (contactId/orgId).
  // NO default: a row is not grantable until the admin picks its level ("Pick access level").
  const [rowLevels, setRowLevels] = React.useState<Record<string, number>>({});
  // Contacts + organizations staged via the native "+ Contact" / "+ Organization"
  // advanced lookup — appended into the "Add Access Permissions" list as selectable rows.
  const [lookedUpContacts, setLookedUpContacts] = React.useState<IContactSearchResult[]>([]);
  const [lookedUpOrgs, setLookedUpOrgs] = React.useState<IOrganizationPick[]>([]);

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
      setRowLevels({});
      setLookedUpContacts([]);
      setLookedUpOrgs([]);
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

  // Unified "Available Contacts & Organizations" list (task 073 v1.0.23): role-based membership
  // candidates + any contacts/organizations staged via the "+ Contact" / "+ Organization" side pane.
  // Each item carries its own per-row access level. Granted contacts are excluded by loadData().
  interface IAvailableItem {
    id: string; // contactId (contact) or organizationId (organization)
    name: string;
    meta: string;
    kind: 'contact' | 'organization';
    contact?: { contactId: string; fullName: string; email?: string };
    org?: IOrganizationPick;
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
    return items;
  }, [candidates, lookedUpContacts, lookedUpOrgs]);

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

  const handleGrantSelected = React.useCallback(async () => {
    const selected = availableItems.filter(it => selectedCandidateIds.has(it.id));
    if (selected.length === 0) return;
    // Every selected row must have a level (no default) before it can be granted.
    const missing = selected.filter(it => rowLevels[it.id] === undefined);
    if (missing.length > 0) {
      setNotice({ intent: 'warning', text: `Pick an access level for: ${missing.map(m => m.name).join(', ')}.` });
      return;
    }
    setApproving(true);
    let failures = 0;
    let granted = 0;
    let anyNotifyPending = false;
    for (const it of selected) {
      const level = rowLevels[it.id];
      try {
        if (it.kind === 'contact' && it.contact) {
          const outcome = await grantContact(it.contact, { level });
          anyNotifyPending = anyNotifyPending || outcome.notifyPending;
        } else if (it.kind === 'organization' && it.org) {
          await grantOrganization(it.org, level);
        }
        granted += 1;
      } catch {
        failures += 1;
      }
    }

    setApproving(false);
    setSelectedCandidateIds(new Set());
    setRowLevels({});
    setLookedUpContacts([]);
    setLookedUpOrgs([]);
    await loadData();

    if (failures > 0) {
      setNotice({
        intent: 'error',
        text: `Granted access to ${granted} of ${selected.length}; ${failures} failed. Please try again.`,
      });
    } else if (anyNotifyPending) {
      setNotice({
        intent: 'warning',
        text: `Granted access to ${granted} item(s). Internal notify (deep-link) is not yet available for internal workforce contacts (escalated; see project notes).`,
      });
    } else {
      setNotice({ intent: 'success', text: `Granted access to ${granted} item(s).` });
    }
  }, [availableItems, selectedCandidateIds, rowLevels, grantContact, grantOrganization, loadData]);

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

  const toggleCandidateSelected = (contactId: string) => {
    setSelectedCandidateIds(prev => {
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
        // task 073 v1.0.24 — the modal is `nonBlocking` (Fluent non-modal: no
        // backdrop, no focus trap) so the host's NATIVE advanced-lookup pane
        // (Xrm.Utility.lookupObjects, opened by "+ Contact"/"+ Organization")
        // renders on top and stays interactive instead of being covered by a
        // modal backdrop — the proven CommunicationActions composer pattern.
        open={open}
        onClose={onClose}
        title={title}
        size="lg"
        dismiss="explicit"
        nonBlocking
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

                {/* Add Access Permissions (task 073 UAT v1.0.24 #2) — role-based members + looked-up
                    contacts/orgs. "+ Contact" / "+ Organization" (icon-only, #4) open the NATIVE
                    advanced-lookup pane. Select, choose a level, Add. */}
                <div className={styles.section} style={{ marginTop: tokens.spacingVerticalL }}>
                  <div className={styles.sectionHeaderRow}>
                    <Text className={styles.sectionTitle}>Add Access Permissions</Text>
                    <div className={styles.sectionHeaderActions}>
                      {/* Icon-only "+" triggers (task 073 UAT v1.0.24 #4) — open the native
                          advanced-lookup pane (pickContact/pickOrganization). */}
                      {pickContact && (
                        <Tooltip content="Add contact" relationship="label">
                          <Button
                            appearance="secondary"
                            size="small"
                            icon={<PersonRegular />}
                            onClick={() => void openContactPicker()}
                            disabled={grantsBlocked || picking}
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
                            disabled={grantsBlocked || picking}
                            aria-label="Add organization"
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
                        No contacts or organizations yet. Use “+ Contact” or “+ Organization” to add.
                      </Text>
                    ) : (
                      availableItems.map(item => (
                        <div className={styles.row} key={item.id}>
                          <Checkbox
                            checked={selectedCandidateIds.has(item.id)}
                            onChange={() => toggleCandidateSelected(item.id)}
                            aria-label={`Select ${item.name}`}
                            disabled={grantsBlocked}
                          />
                          <div className={styles.rowMain}>
                            {/* Contact name → link opening the Contact record (task 073 UAT v1.0.24 #6);
                                organization rows show a building glyph + plain name. */}
                            {item.kind === 'contact' && item.contact && onOpenContact ? (
                              <Link
                                className={styles.contactLink}
                                onClick={() => onOpenContact(item.contact!.contactId)}
                              >
                                {item.name}
                              </Link>
                            ) : (
                              <Text className={styles.rowName}>
                                {item.kind === 'organization' ? <BuildingRegular /> : null} {item.name}
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
                              disabled={grantsBlocked}
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
                      disabled={selectedCandidateIds.size === 0 || approving || grantsBlocked}
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
                              {/* Contact name → link opening the Contact record (task 073 UAT v1.0.24 #6).
                                Org grants key on the org id (not a contact), so they stay plain text. */}
                              {!isOrg && onOpenContact ? (
                                <Link className={styles.contactLink} onClick={() => onOpenContact(grant.contactId)}>
                                  {grant.fullName}
                                </Link>
                              ) : (
                                <Text className={styles.rowName}>{grant.fullName}</Text>
                              )}
                              <Text className={styles.rowMeta}>
                                {isStanding
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
