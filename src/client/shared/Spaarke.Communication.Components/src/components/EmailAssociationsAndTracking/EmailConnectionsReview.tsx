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
 *     another record" (all regarding targets via the shared `PolymorphicPicker`).
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
import { MessageBar, MessageBarBody } from '@fluentui/react-components';
import { Search20Regular } from '@fluentui/react-icons';
import { PolymorphicPicker } from '@spaarke/ui-components';
import {
  derivePrimaryReview,
  applyRegardingSelection,
  advanceAssociationStatus,
  PRIMARY_CANDIDATE_SLOTS,
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
    filedAssociations = [],
    writeContext,
    pickerWebApi = {},
    linkAnotherCatalog,
    readOnly = false,
    onAssociationsChanged,
  } = props;
  const s = useConnectionsReviewStyles();

  const [selectedKey, setSelectedKey] = React.useState<string | undefined>(undefined);
  const [busy, setBusy] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [linking, setLinking] = React.useState(false);

  // Session-local review state is per-selected-email — reset on selection change.
  React.useEffect(() => {
    setSelectedKey(undefined);
    setError(null);
    setLinking(false);
  }, [communicationId]);

  // SAME data path as the engine (no client recompute; ADR-045).
  const model = React.useMemo(
    () =>
      derivePrimaryReview(associationProvenanceJson, associationStatus, filedAssociations, {
        recordName: regardingRecordName,
        recordNumber: regardingRecordNumber,
      }),
    [associationProvenanceJson, associationStatus, filedAssociations, regardingRecordName, regardingRecordNumber]
  );

  const catalog = linkAnotherCatalog ?? DEFAULT_LINK_CATALOG;

  // Highlight the confirmed (🟢) or auto-matched (🟡) primary card green; a user
  // pick highlights blue. The confirmed primary already owns the denorm fields, so
  // it shows NO Confirm button (that primary's chip + Remove live in the section
  // header — the reviewer switches by picking a different card here).
  const greenKey = model.primary ? candidateKey(model.primary) : undefined;
  const confirmedKey = model.state === 'confirmed' && model.primary ? candidateKey(model.primary) : undefined;
  const activeSelectedKey = selectedKey ?? greenKey;

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
      setLinking(false);
      void confirmCandidate({ entity: entityType, targetId: recordId, targetName: recordName, confidence: 1 });
    },
    [confirmCandidate]
  );

  return (
    <div className={s.root} data-testid="email-connections-review">
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Candidate cards + the "Link another record" tile share ONE grid, so the
          link tile sits directly AFTER the last card (owner UAT). Always render the
          fixed candidate-slot count; a slot is blank below 70%. */}
      <div className={s.cards}>
        {Array.from({ length: PRIMARY_CANDIDATE_SLOTS }, (_, i) => {
          const c = model.candidates[i];
          if (!c) return <BlankCard key={`blank-${i}`} s={s} />;
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
              busy={busy}
              readOnly={readOnly}
              onSelect={() => setSelectedKey(k)}
              onConfirm={() => void confirmCandidate(c)}
              s={s}
            />
          );
        })}

        {/* Link another record — a tile matching the candidate cards. Clicking it
            reveals the record-type dropdown + right-pane lookup IN PLACE (same grid
            cell — no jump to a separate link); all regarding targets via the shared
            picker (§11 reuse). */}
        {!readOnly && (
          <div className={s.cardCell}>
            {linking ? (
              <PolymorphicPicker
                title="Link another record"
                catalog={catalog}
                webApi={pickerWebApi}
                onSelect={onLinkSelected}
                onError={m => setError(m)}
                disabled={busy}
              />
            ) : (
              <button type="button" className={s.linkCard} disabled={busy} onClick={() => setLinking(true)}>
                <span className={s.linkCardLabel}>Link another record</span>
                <Search20Regular className={s.linkCardIcon} aria-hidden="true" />
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
