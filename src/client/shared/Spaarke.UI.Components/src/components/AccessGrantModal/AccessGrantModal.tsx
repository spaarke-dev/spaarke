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
 */

import * as React from 'react';
import {
  Button,
  Checkbox,
  Combobox,
  Option,
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
import { PersonRegular, DismissCircleRegular } from '@fluentui/react-icons';
import { SprkModal } from '../SprkModal';
import type {
  IAccessGrantModalProps,
  IAccessGrantCandidate,
  IAccessGrantRecord,
  IContactSearchResult,
} from './types';
import { DEFAULT_ACCESS_LEVEL_OPTIONS } from './types';

const useStyles = makeStyles({
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginBottom: tokens.spacingVerticalXL,
  },
  sectionTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
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
  canGrantAccess = true,
  authenticatedFetch,
  fetchCandidates,
  fetchExistingGrants,
  searchContacts,
  isInternalContact,
  onSetStandingGrant,
  title = 'Manage Access',
  accessLevelOptions = DEFAULT_ACCESS_LEVEL_OPTIONS,
  defaultAccessLevel,
}) => {
  const styles = useStyles();
  const effectiveAccessLevel = defaultAccessLevel ?? accessLevelOptions[0]?.value ?? DEFAULT_ACCESS_LEVEL_OPTIONS[0].value;

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

  const [revokeTargetId, setRevokeTargetId] = React.useState<string | null>(null);
  const [revoking, setRevoking] = React.useState(false);

  const [notice, setNotice] = React.useState<{ intent: 'success' | 'warning' | 'error'; text: string } | null>(null);

  const loadData = React.useCallback(async () => {
    setLoading(true);
    setNotice(null);
    try {
      const [candidateList, grantList] = await Promise.all([fetchCandidates(), fetchExistingGrants()]);
      setExistingGrants(grantList);
      const grantedContactIds = new Set(grantList.map(g => g.contactId));
      setCandidates(candidateList.filter(c => !grantedContactIds.has(c.contactId)));
    } catch {
      setNotice({ intent: 'error', text: 'Failed to load access data. Close and reopen to retry.' });
    } finally {
      setLoading(false);
    }
  }, [fetchCandidates, fetchExistingGrants]);

  React.useEffect(() => {
    if (open && canGrantAccess) {
      setSelectedCandidateIds(new Set());
      setStandingForCandidate(new Set());
      setSearchQuery('');
      setSearchResults([]);
      setSelectedNamed(null);
      setStandingForNamed(false);
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
      opts: { standingGrant: boolean }
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
        await postJson('/api/v1/external-access/invite-and-grant', {
          email: contact.email,
          projectId: recordId,
          accessLevel: effectiveAccessLevel,
          firstName,
          lastName,
        });
      } else {
        // Internal workforce contact, or an external contact with no email on
        // file → the built grant-only core (no CIAM onboarding attempted).
        await postJson('/api/v1/external-access/grant', {
          contactId: contact.contactId,
          projectId: recordId,
          accessLevel: effectiveAccessLevel,
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
    [effectiveAccessLevel, isInternalContact, onSetStandingGrant, postJson, recordId]
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
        const outcome = await grantContact(candidate, { standingGrant: standingForCandidate.has(candidate.contactId) });
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
  }, [candidates, grantContact, loadData, selectedCandidateIds, standingForCandidate]);

  const handleAddNamed = React.useCallback(async () => {
    if (!selectedNamed) return;
    setAddingNamed(true);
    try {
      const outcome = await grantContact(selectedNamed, { standingGrant: standingForNamed });
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
  }, [grantContact, loadData, selectedNamed, standingForNamed]);

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
      await postJson('/api/v1/external-access/revoke', {
        accessRecordId: target.accessRecordId,
        contactId: target.contactId,
        projectId: recordId,
      });
      setRevokeTargetId(null);
      await loadData();
      setNotice({ intent: 'success', text: `Revoked access for ${target.fullName}.` });
    } catch {
      setNotice({ intent: 'error', text: `Failed to revoke access for ${target.fullName}. Please try again.` });
    } finally {
      setRevoking(false);
    }
  }, [existingGrants, loadData, postJson, recordId, revokeTargetId]);

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
        open={open}
        onClose={onClose}
        title={title}
        size="lg"
        dismiss="explicit"
        footer={
          <Button appearance="primary" onClick={onClose}>
            Close
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
                  <MessageBarTitle>{notice.intent === 'success' ? 'Success' : notice.intent === 'warning' ? 'Notice' : 'Error'}</MessageBarTitle>
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
                {/* Section 1 — candidate approve list (task 021 role-allowlist source) */}
                <div className={styles.section}>
                  <Text className={styles.sectionTitle}>Membership candidates</Text>
                  <Text className={styles.sectionSubtitle}>
                    Contacts assigned to this record via an access-conferring role. Approving writes a grant
                    (provenance: membership-approved).
                  </Text>
                  {candidates.length === 0 ? (
                    <Text className={styles.emptyState}>No unapproved membership candidates.</Text>
                  ) : (
                    candidates.map(candidate => (
                      <div className={styles.row} key={candidate.contactId}>
                        <Checkbox
                          checked={selectedCandidateIds.has(candidate.contactId)}
                          onChange={() => toggleCandidateSelected(candidate.contactId)}
                          aria-label={`Select ${candidate.fullName}`}
                        />
                        <div className={styles.rowMain}>
                          <Text className={styles.rowName}>{candidate.fullName}</Text>
                          <Text className={styles.rowMeta}>
                            {candidate.role}
                            {candidate.email ? ` · ${candidate.email}` : ''}
                          </Text>
                        </div>
                        <div className={styles.rowActions}>
                          {onSetStandingGrant && (
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
                  {candidates.length > 0 && (
                    <Button
                      appearance="primary"
                      disabled={selectedCandidateIds.size === 0 || approving}
                      icon={approving ? <Spinner size="tiny" /> : undefined}
                      onClick={handleApproveSelected}
                      style={{ alignSelf: 'flex-start' }}
                    >
                      Approve selected ({selectedCandidateIds.size})
                    </Button>
                  )}
                </div>

                {/* Section 2 — named-contact person-picker */}
                <div className={styles.section}>
                  <Text className={styles.sectionTitle}>Add a named contact</Text>
                  <Text className={styles.sectionSubtitle}>
                    Search for any Contact to grant explicit access, regardless of role assignment.
                  </Text>
                  <div className={styles.pickerRow}>
                    <Combobox
                      freeform
                      placeholder="Search contacts by name or email…"
                      value={searchQuery}
                      onInput={e => handleSearchChange((e.target as HTMLInputElement).value)}
                      onOptionSelect={(_, data) => {
                        const found = searchResults.find(r => r.contactId === data.optionValue);
                        setSelectedNamed(found ?? null);
                        if (found) setSearchQuery(found.fullName);
                      }}
                      expandIcon={searching ? <Spinner size="tiny" /> : undefined}
                    >
                      {searchResults.map(result => (
                        <Option key={result.contactId} value={result.contactId} text={result.fullName}>
                          {result.fullName}
                          {result.email ? ` (${result.email})` : ''}
                        </Option>
                      ))}
                    </Combobox>
                    {onSetStandingGrant && (
                      <Checkbox
                        label="Standing grant"
                        checked={standingForNamed}
                        onChange={(_, data) => setStandingForNamed(Boolean(data.checked))}
                        disabled={!selectedNamed}
                      />
                    )}
                    <Button
                      appearance="primary"
                      icon={addingNamed ? <Spinner size="tiny" /> : <PersonRegular />}
                      disabled={!selectedNamed || addingNamed}
                      onClick={handleAddNamed}
                    >
                      Add
                    </Button>
                  </div>
                </div>

                {/* Section 3 — existing grants + revoke */}
                <div className={styles.section}>
                  <Text className={styles.sectionTitle}>Current access</Text>
                  {existingGrants.length === 0 ? (
                    <Text className={styles.emptyState}>No active grants for this record.</Text>
                  ) : (
                    existingGrants.map(grant => (
                      <div className={styles.row} key={grant.accessRecordId}>
                        <div className={styles.rowMain}>
                          <Text className={styles.rowName}>{grant.fullName}</Text>
                          <Text className={styles.rowMeta}>
                            Granted by {grant.grantedByName ?? 'unknown'} on {formatGrantDate(grant.grantedDate)}
                            {grant.provenance ? ` · ${grant.provenance}` : ''}
                          </Text>
                        </div>
                        <div className={styles.rowActions}>
                          <Badge appearance="tint" color="informative">
                            {accessLevelOptions.find(o => o.value === grant.accessLevel)?.label ?? grant.accessLevel}
                          </Badge>
                          <Button
                            appearance="subtle"
                            size="small"
                            onClick={() => setRevokeTargetId(grant.accessRecordId)}
                            disabled={revoking}
                          >
                            Revoke
                          </Button>
                        </div>
                      </div>
                    ))
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
          Revoke access for <strong>{revokeTarget?.fullName}</strong>? They will immediately lose access to this
          record (unless a standing grant or other membership still applies).
        </Text>
      </SprkModal>
    </>
  );
};

export default AccessGrantModal;
