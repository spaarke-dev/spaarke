/**
 * EmailConnectionsReview.tsx — the reading-pane ASSOCIATION RESOLVER (email-
 * communication-solution-r5, single-primary reading-pane redesign 2026-07-29).
 *
 * Redesign goal (owner-locked): the reading pane makes exactly ONE primary
 * association per email — the record that owns the denormalized
 * `sprk_regardingrecord*` fields ("model A": the UI sets the single primary; the
 * engine's multi-lookup auto-writes are unchanged). One section, three states,
 * keyed to the section status dot:
 *   - 🔴 REQUIRES REVIEW    — no engine auto-match. Top-3 candidate cards (always
 *     3 slots; a slot is blank when its candidate is < 70%). Click a card to
 *     select it → [✓ Confirm] appears directly BENEATH that card. Or "Link
 *     another record" — a card-styled tile whose single click opens the
 *     record-type dropdown directly, then the host's polymorphic lookup dialog
 *     (all regarding targets; reuses the shared picker's `getXrmForPicker` bridge).
 *   - 🟡 NEEDS CONFIRMATION — the engine auto-matched (100% / autoFiled) and wrote
 *     the denorm primary; the top card is GREEN and pre-selected, but a human must
 *     Confirm (or switch to another candidate — downgrade allowed).
 *   - 🟢 CONFIRMED          — a human confirmed the primary. A "{Type}: {number}"
 *     chip with a remove (×); removing returns to candidate selection.
 *
 * WRITE PATH (binding MUST): confirm / switch / link-another persist via the
 * task-020 ADDITIVE `applyRegardingSelection` (which writes the chosen typed
 * lookup + all five denorm `sprk_regardingrecord*` fields), then
 * `advanceAssociationStatus` marks the record Resolved (→ green). Remove calls
 * `unlinkRegarding` (nulls exactly the one primary lookup). No text-search write
 * path; the connection model is the shared `derivePrimaryReview` (no client-side
 * recompute of engine decisions; ADR-045). Fluent v9 tokens only (ADR-021,
 * dark-mode correct). No `as React.ComponentType` cast (NFR-05).
 */
import * as React from 'react';
import {
  MessageBar,
  MessageBarBody,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
  Button,
} from '@fluentui/react-components';
import { Search20Regular, DocumentAdd20Regular } from '@fluentui/react-icons';
import { getXrmForPicker } from '@spaarke/ui-components';
import {
  derivePrimaryReview,
  applyRegardingSelection,
  advanceAssociationStatus,
  type PrimaryCandidate,
} from '../../logic/connections';
import type { EmailConnectionsReviewProps } from './EmailAssociationsAndTracking.types';
import { useConnectionsReviewStyles } from './EmailConnectionsReview.styles';
import { DEFAULT_LINK_CATALOG } from './EmailConnectionsReview.helpers';
import { CandidateCard, BlankCard } from './EmailConnectionsReviewRows';

const candidateKey = (c: Pick<PrimaryCandidate, 'entity' | 'targetId'>): string =>
  `${c.entity}:${c.targetId.replace(/[{}]/g, '').toLowerCase()}`;

export function EmailConnectionsReview(props: EmailConnectionsReviewProps): React.ReactElement {
  const {
    communicationId,
    associationStatus,
    associationProvenanceJson,
    regardingRecordName,
    regardingRecordNumber,
    regardingRecordType,
    filedAssociations = [],
    writeContext,
    linkAnotherCatalog,
    readOnly = false,
    variant = 'default',
    onAssociationsChanged,
    onCreateNewRecord,
    onLaunchCreateRecord,
  } = props;
  const s = useConnectionsReviewStyles();
  const reconcile = variant === 'reconcile';

  const [selectedKey, setSelectedKey] = React.useState<string | undefined>(undefined);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Session-local review state is per-selected-email — reset on selection change.
  React.useEffect(() => {
    setSelectedKey(undefined);
    setError(null);
  }, [communicationId]);

  // SAME data path as the engine (no client recompute; ADR-045).
  const model = React.useMemo(
    () =>
      derivePrimaryReview(associationProvenanceJson, associationStatus, filedAssociations, {
        recordName: regardingRecordName,
        recordNumber: regardingRecordNumber,
        recordTypeLabel: regardingRecordType,
      }),
    [
      associationProvenanceJson,
      associationStatus,
      filedAssociations,
      regardingRecordName,
      regardingRecordNumber,
      regardingRecordType,
    ]
  );

  const catalog = linkAnotherCatalog ?? DEFAULT_LINK_CATALOG;

  // Highlight the confirmed (🟢) or auto-matched (🟡) primary card green; a user
  // pick highlights blue. The confirmed primary already owns the denorm fields, so
  // it shows NO Confirm button (that primary's chip + Remove live in the section
  // header — the reviewer switches by picking a different card here).
  const greenKey = model.primary ? candidateKey(model.primary) : undefined;
  const confirmedKey = model.state === 'confirmed' && model.primary ? candidateKey(model.primary) : undefined;
  const activeSelectedKey = selectedKey ?? greenKey;
  // Confirmed → the primary is the header chip, so the cards row shows ONLY the
  // "Link another record" tile (owner UAT 2026-07-31).
  const isConfirmed = model.state === 'confirmed';
  // Reconcile-variant "Filed to …" banner label — the confirmed primary's name
  // (with its record number when known).
  const filedLabel = model.primary
    ? model.primary.recordNumber
      ? `${model.primary.targetName} (${model.primary.recordNumber})`
      : model.primary.targetName
    : '';

  const confirmCandidate = React.useCallback(
    async (c: PrimaryCandidate): Promise<void> => {
      setBusy(true);
      setError(null);
      try {
        const res = await applyRegardingSelection(writeContext, {
          entityType: c.entity,
          recordId: c.targetId,
          recordName: c.targetName,
        });
        if (!res.success) {
          setError(res.error ?? 'Could not file this association.');
          return;
        }
        await advanceAssociationStatus(writeContext);
        // FR-A4 (R-1): fire-and-forget affinity learning — a HUMAN confirmed this email is regarding
        // c.entity:c.targetId. Host-injected (ADR-012); a no-op when unwired. Best-effort: the impl swallows
        // its own errors, so this never blocks or fails the confirmation the user just made.
        writeContext.recordAffinity?.(c.entity, c.targetId);
        setSelectedKey(undefined);
        onAssociationsChanged?.();
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Unexpected error while filing.');
      } finally {
        setBusy(false);
      }
    },
    [writeContext, onAssociationsChanged]
  );

  const onLinkSelected = React.useCallback(
    (entityType: string, recordId: string, recordName: string): void => {
      void confirmCandidate({ entity: entityType, targetId: recordId, targetName: recordName, confidence: 1 });
    },
    [confirmCandidate]
  );

  // "New record" (task 064, E1b) → the host opens Quick Start / a Create*Wizard and
  // resolves with the created record's ref (or null when cancelled). A created ref is
  // filed as the confirmed regarding via the SAME additive `confirmCandidate` →
  // `applyRegardingSelection` path a picked record uses (no second write path; NFR-10).
  // The subsequent `onAssociationsChanged` (inside confirmCandidate) re-scopes Fields/Tasks.
  // Falls back to the fire-and-forget `onCreateNewRecord` when no launcher is wired.
  const handleCreateNew = React.useCallback(async (): Promise<void> => {
    if (!onLaunchCreateRecord) {
      onCreateNewRecord?.();
      return;
    }
    setError(null);
    try {
      const ref = await onLaunchCreateRecord();
      if (!ref) return; // user cancelled the wizard — no write
      await confirmCandidate({
        entity: ref.entityType,
        targetId: ref.id,
        targetName: ref.name ?? '',
        confidence: 1,
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create and file the new record.');
    }
  }, [onLaunchCreateRecord, onCreateNewRecord, confirmCandidate]);

  // "Link another record" → SINGLE click opens the record-type dropdown directly
  // (owner UAT #6). Picking a type opens the host's polymorphic lookup dialog
  // (`Xrm.Utility.lookupObjects`, reused via the shared picker's `getXrmForPicker`
  // bridge). In a non-MDA / dev host that bridge is absent, so the lookup no-ops
  // (expected). A picked record is filed via the same additive confirm path.
  const handleLinkPick = React.useCallback(
    async (entityType: string): Promise<void> => {
      setError(null);
      try {
        const xrm = getXrmForPicker();
        if (!xrm?.Utility?.lookupObjects) return; // dev/non-MDA host — no-op (expected)
        const results = await xrm.Utility.lookupObjects({
          entityTypes: [entityType],
          defaultEntityType: entityType,
          allowMultiSelect: false,
        });
        if (!results || results.length === 0) return; // user cancelled
        const picked = results[0];
        onLinkSelected(entityType, picked.id.replace(/[{}]/g, '').toLowerCase(), picked.name);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not open the record picker.');
      }
    },
    [onLinkSelected]
  );

  return (
    <div className={s.root} data-testid="email-connections-review">
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Reconcile variant (owner UAT round-3 2026-08-13) — "Filed to …" success
          banner once a primary is confirmed. The reconciliation browse tab renders
          NO confirmed chip of its own, so this banner is the filed-state feedback
          the reviewer sees (matches the Pillar E prototype's Related-to banner). */}
      {reconcile && isConfirmed && model.primary && (
        <MessageBar intent="success" data-testid="association-filed-banner">
          <MessageBarBody>
            Filed to <strong>{filedLabel}</strong>. Move to Fields / Tasks to continue.
          </MessageBarBody>
        </MessageBar>
      )}

      {/* Candidate cards + the "Link another record" tile share ONE grid, so the link
          tile sits directly AFTER the last card. Card set depends on state (owner UAT
          2026-07-31):
            • confirmed  → NO candidate cards (the confirmed record is the header chip);
              only "Link another record" shows.
            • has matches → only the actual candidate cards (no "No confident match"
              filler slots).
            • no matches → a single "No confident match" card.
          The "Link another record" tile renders in every non-read-only state. */}
      <div className={s.cards}>
        {!isConfirmed &&
          (model.candidates.length > 0 ? (
            model.candidates.map(c => {
              const k = candidateKey(c);
              const isGreen = k === greenKey;
              const isSelected = k === activeSelectedKey;
              return (
                <CandidateCard
                  key={k}
                  candidate={c}
                  selected={isSelected || isGreen}
                  tone={isGreen ? 'primary' : 'select'}
                  showConfirm={isSelected && k !== confirmedKey}
                  confirmLabel={reconcile ? 'Select' : undefined}
                  busy={busy}
                  readOnly={readOnly}
                  onSelect={() => setSelectedKey(k)}
                  onConfirm={() => void confirmCandidate(c)}
                  s={s}
                />
              );
            })
          ) : (
            <BlankCard key="blank" s={s} />
          ))}

        {/* Default variant — "Link another record" as a card TILE (a VISUAL SIBLING
            of the candidate cards; owner UAT 2026-07-31 #5/#6). The reconcile variant
            renders this as a labelled field BELOW the grid instead (see below). */}
        {!reconcile && !readOnly && (
          <div className={s.cardCell}>
            <Menu positioning="below-start">
              <MenuTrigger disableButtonEnhancement>
                <button type="button" className={s.linkCard} disabled={busy} data-testid="link-another-record">
                  <span className={s.linkCardLabel}>Link another record</span>
                  <span className={s.linkCardIconRow}>
                    <Search20Regular className={s.linkCardIcon} aria-hidden="true" />
                  </span>
                </button>
              </MenuTrigger>
              <MenuPopover>
                <MenuList>
                  {catalog.map(entry => (
                    <MenuItem
                      key={entry.recordTypeRefId}
                      onClick={() => void handleLinkPick(entry.logicalName)}
                      data-testid={`link-another-record-item-${entry.logicalName}`}
                    >
                      {entry.displayName}
                    </MenuItem>
                  ))}
                </MenuList>
              </MenuPopover>
            </Menu>
          </div>
        )}

        {/* Default variant — "New record" as a card tile (see reconcile full-width
            button below). Rendered ONLY when the host wires a create-new launcher. */}
        {!reconcile && (onLaunchCreateRecord || onCreateNewRecord) && !readOnly && (
          <div className={s.cardCell}>
            <button
              type="button"
              className={s.linkCard}
              disabled={busy}
              onClick={() => void handleCreateNew()}
              data-testid="create-new-record"
            >
              <span className={s.linkCardLabel}>New record</span>
              <span className={s.linkCardIconRow}>
                <DocumentAdd20Regular className={s.linkCardIcon} aria-hidden="true" />
              </span>
            </button>
          </div>
        )}
      </div>

      {/* Reconcile variant — "Look up another record" as a LABELLED FIELD (owner UAT
          round-3: "lookup record as more of a field"). A single click on the field
          opens the record-type menu → the host's polymorphic lookup dialog (same
          `handleLinkPick` path the default tile uses). */}
      {reconcile && !readOnly && (
        <div className={s.lookupField}>
          <span className={s.lookupFieldLabel} id="reconcile-lookup-label">
            Look up another record
          </span>
          <Menu positioning="below-start">
            <MenuTrigger disableButtonEnhancement>
              <button
                type="button"
                className={s.lookupControl}
                disabled={busy}
                data-testid="link-another-record"
                aria-labelledby="reconcile-lookup-label"
              >
                <span className={s.lookupPlaceholder}>Matter / Project / Service Request…</span>
                <Search20Regular className={s.lookupControlIcon} aria-hidden="true" />
              </button>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {catalog.map(entry => (
                  <MenuItem
                    key={entry.recordTypeRefId}
                    onClick={() => void handleLinkPick(entry.logicalName)}
                    data-testid={`link-another-record-item-${entry.logicalName}`}
                  >
                    {entry.displayName}
                  </MenuItem>
                ))}
              </MenuList>
            </MenuPopover>
          </Menu>
        </div>
      )}

      {/* Reconcile variant — "New record" as a FULL-WIDTH button (owner UAT round-3:
          "+New record as a full width button"). Same additive create-and-file path. */}
      {reconcile && (onLaunchCreateRecord || onCreateNewRecord) && !readOnly && (
        <Button
          className={s.newRecordFullWidth}
          appearance="secondary"
          icon={<DocumentAdd20Regular />}
          disabled={busy}
          onClick={() => void handleCreateNew()}
          data-testid="create-new-record"
        >
          New record
        </Button>
      )}
    </div>
  );
}
