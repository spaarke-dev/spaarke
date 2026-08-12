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
  Input,
  Spinner,
  Text,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Badge,
  OverlayDrawer,
  DrawerHeader,
  DrawerHeaderTitle,
  DrawerBody,
  makeStyles,
  tokens,
  shorthands,
} from '@fluentui/react-components';
import {
  PersonRegular,
  DismissCircleRegular,
  BuildingRegular,
  DismissRegular,
  SearchRegular,
} from '@fluentui/react-icons';
import { SprkModal } from '../SprkModal';
// Record picker (task 073 v1.0.23 redesign) — "+ Contact" / "+ Organization"
// open a Fluent `OverlayDrawer` side pane (search + pick) that portals ABOVE the
// modal, so the modal is never hidden while the user picks.
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
  // Per-row access-level dropdown (task 073 v1.0.23) — sized so "Pick access level" fits.
  rowLevelDropdown: {
    minWidth: '160px',
  },
  // A clickable result row inside the side-pane picker.
  pickerResultRow: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalS),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
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

  // Access-Permission sharing gate (task 043, FR-14 Option A). Deliberately
  // computed from the prop alone — never from `effectiveAccessLevel` or any
  // other per-grant `sprk_accesslevel` concept above, so the two stay
  // structurally independent (see the module doc comment).
  const grantsBlocked = accessPermissionState === 'restricted';
  const standingGrantAllowed = accessPermissionState === 'standard';

  const [loading, setLoading] = React.useState(false);
  const [candidates, setCandidates] = React.useState<IAccessGrantCandidate[]>([]);
  const [existingGrants, setExistingGrants] = React.useState<IAccessGrantRecord[]>([]);
  // Items (contacts OR organizations) checked to grant, keyed by contactId/orgId.
  const [selectedCandidateIds, setSelectedCandidateIds] = React.useState<Set<string>>(new Set());
  const [standingForCandidate, setStandingForCandidate] = React.useState<Set<string>>(new Set());
  const [approving, setApproving] = React.useState(false);

  // Per-row access level (task 073 v1.0.23) — keyed by item id (contactId/orgId).
  // NO default: a row is not grantable until the admin picks its level ("Pick access level").
  const [rowLevels, setRowLevels] = React.useState<Record<string, number>>({});
  // Contacts + organizations staged via the "+ Contact" / "+ Organization" side pane —
  // appended into the "Available Contacts & Organizations" list as selectable rows.
  const [lookedUpContacts, setLookedUpContacts] = React.useState<IContactSearchResult[]>([]);
  const [lookedUpOrgs, setLookedUpOrgs] = React.useState<IOrganizationPick[]>([]);

  // Fluent OverlayDrawer side-pane picker (task 073 v1.0.23) — opened by the
  // "+ Contact" / "+ Organization" buttons; searches + picks without hiding the modal.
  const [pickerMode, setPickerMode] = React.useState<'contact' | 'organization' | null>(null);
  const [pickerQuery, setPickerQuery] = React.useState('');
  const [pickerResults, setPickerResults] = React.useState<Array<{ id: string; name: string; meta?: string }>>([]);
  const [pickerSearching, setPickerSearching] = React.useState(false);

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
      setRowLevels({});
      setLookedUpContacts([]);
      setLookedUpOrgs([]);
      setPickerMode(null);
      setPickerQuery('');
      setPickerResults([]);
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
      opts: { standingGrant: boolean; level: number }
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
    [isInternalContact, onSetStandingGrant, postJson, recordId, recordType]
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
    let anyStandingGrantFailed = false;
    for (const it of selected) {
      const level = rowLevels[it.id];
      try {
        if (it.kind === 'contact' && it.contact) {
          const outcome = await grantContact(it.contact, { standingGrant: standingForCandidate.has(it.id), level });
          anyNotifyPending = anyNotifyPending || outcome.notifyPending;
          anyStandingGrantFailed = anyStandingGrantFailed || outcome.standingGrantFailed;
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
    setStandingForCandidate(new Set());
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
    } else if (anyStandingGrantFailed) {
      setNotice({
        intent: 'warning',
        text: `Granted access to ${granted} item(s), but setting the standing-grant flag failed for at least one contact. You can retry from the Contact form.`,
      });
    } else {
      setNotice({ intent: 'success', text: `Granted access to ${granted} item(s).` });
    }
  }, [
    availableItems,
    selectedCandidateIds,
    rowLevels,
    grantContact,
    grantOrganization,
    standingForCandidate,
    loadData,
  ]);

  // ── Side-pane record picker (task 073 v1.0.23) ──────────────────────────────
  // `+ Contact` / `+ Organization` open a Fluent OverlayDrawer that searches +
  // picks WITHOUT hiding the modal. Contact picks need the email (external-vs-
  // internal routing), so cache full contact results by id and resolve on pick.
  const contactSearchCacheRef = React.useRef<Map<string, IContactSearchResult>>(new Map());

  const openPicker = React.useCallback((mode: 'contact' | 'organization') => {
    setPickerMode(mode);
    setPickerQuery('');
    setPickerResults([]);
  }, []);
  const closePicker = React.useCallback(() => setPickerMode(null), []);

  const runPickerSearch = React.useCallback(
    async (query: string, mode: 'contact' | 'organization') => {
      setPickerQuery(query);
      if (!query || query.trim().length < 2) {
        setPickerResults([]);
        return;
      }
      setPickerSearching(true);
      try {
        if (mode === 'contact') {
          const results = await searchContacts(query);
          for (const r of results) contactSearchCacheRef.current.set(r.contactId, r);
          setPickerResults(results.map(r => ({ id: r.contactId, name: r.fullName, meta: r.email })));
        } else if (searchOrganizations) {
          const results = await searchOrganizations(query);
          setPickerResults(results.map(r => ({ id: r.id, name: r.name })));
        }
      } catch {
        setPickerResults([]);
      } finally {
        setPickerSearching(false);
      }
    },
    [searchContacts, searchOrganizations]
  );

  const pickResult = React.useCallback(
    (result: { id: string; name: string }) => {
      if (pickerMode === 'contact') {
        const full = contactSearchCacheRef.current.get(result.id) ?? { contactId: result.id, fullName: result.name };
        setLookedUpContacts(prev => (prev.some(c => c.contactId === full.contactId) ? prev : [...prev, full]));
        setSelectedCandidateIds(prev => new Set(prev).add(full.contactId));
      } else if (pickerMode === 'organization') {
        setLookedUpOrgs(prev =>
          prev.some(o => o.id === result.id) ? prev : [...prev, { id: result.id, name: result.name }]
        );
        setSelectedCandidateIds(prev => new Set(prev).add(result.id));
      }
      closePicker();
    },
    [pickerMode, closePicker]
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
        // task 073 v1.0.23 — the modal never hides for picking: "+ Contact" /
        // "+ Organization" open a Fluent OverlayDrawer that portals ABOVE the
        // modal (see the drawer at the end of this component). So `open` is just
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
                <div className={styles.section} style={{ marginTop: tokens.spacingVerticalL }}>
                  {/* Header: title left, "+ Contact" / "+ Organization" side-pane triggers right
                      (task 073 v1.0.23). Each opens a Fluent OverlayDrawer that overlays the modal. */}
                  <div className={styles.sectionHeaderRow}>
                    <Text className={styles.sectionTitle}>Available Contacts &amp; Organizations</Text>
                    <div className={styles.sectionHeaderActions}>
                      <Button
                        appearance="secondary"
                        size="small"
                        icon={<PersonRegular />}
                        onClick={() => openPicker('contact')}
                        disabled={grantsBlocked}
                      >
                        + Contact
                      </Button>
                      {searchOrganizations && (
                        <Button
                          appearance="secondary"
                          size="small"
                          icon={<BuildingRegular />}
                          onClick={() => openPicker('organization')}
                          disabled={grantsBlocked}
                        >
                          + Organization
                        </Button>
                      )}
                    </div>
                  </div>

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
                          <Text className={styles.rowName}>
                            {item.kind === 'organization' ? <BuildingRegular /> : null} {item.name}
                          </Text>
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
                          {item.kind === 'contact' && onSetStandingGrant && standingGrantAllowed && (
                            <Checkbox
                              label="Standing"
                              checked={standingForCandidate.has(item.id)}
                              onChange={() => toggleCandidateStanding(item.id)}
                              disabled={!selectedCandidateIds.has(item.id)}
                            />
                          )}
                        </div>
                      </div>
                    ))
                  )}

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

      {/* Record picker side pane (task 073 v1.0.23) — a Fluent OverlayDrawer that portals ABOVE the modal
          (position="end"), so the user searches + picks a contact/organization WITHOUT the modal hiding.
          Picking appends the item to the Available list and closes the drawer. */}
      <OverlayDrawer
        position="end"
        size="small"
        open={pickerMode !== null}
        onOpenChange={(_, data) => {
          if (!data.open) closePicker();
        }}
      >
        <DrawerHeader>
          <DrawerHeaderTitle
            action={<Button appearance="subtle" icon={<DismissRegular />} aria-label="Close" onClick={closePicker} />}
          >
            {pickerMode === 'organization' ? 'Add organization' : 'Add contact'}
          </DrawerHeaderTitle>
        </DrawerHeader>
        <DrawerBody>
          <Input
            placeholder={pickerMode === 'organization' ? 'Search organizations…' : 'Search contacts…'}
            value={pickerQuery}
            contentBefore={<SearchRegular />}
            onChange={(_, data) => {
              if (pickerMode) void runPickerSearch(data.value, pickerMode);
            }}
            style={{ width: '100%', marginBottom: tokens.spacingVerticalM }}
          />
          {pickerSearching && (
            <div className={styles.loadingRow}>
              <Spinner size="tiny" />
              <Text>Searching…</Text>
            </div>
          )}
          {!pickerSearching && pickerQuery.trim().length >= 2 && pickerResults.length === 0 && (
            <Text className={styles.emptyState}>No matches.</Text>
          )}
          {pickerResults.map(r => (
            <div
              key={r.id}
              className={styles.pickerResultRow}
              role="button"
              tabIndex={0}
              onClick={() => pickResult(r)}
              onKeyDown={e => {
                if (e.key === 'Enter' || e.key === ' ') pickResult(r);
              }}
            >
              <Text className={styles.rowName}>{r.name}</Text>
              {r.meta && <Text className={styles.rowMeta}>{r.meta}</Text>}
            </div>
          ))}
        </DrawerBody>
      </OverlayDrawer>
    </>
  );
};

export default AccessGrantModal;
