/**
 * Row-level sub-components for the reading-pane ASSOCIATION RESOLVER (email-
 * communication-solution-r5, single-primary reading-pane redesign 2026-07-29).
 * The resolver makes ONE primary association across three states; these are the
 * shared building blocks:
 *
 *   - `CandidateCard` — a 2-line candidate card: line 1 `{REC#} : {name}` + a
 *     right-aligned match-% tag; line 2 the plain-English match reason. Clicking
 *     the card selects it (radio semantics); the parent renders the `Confirm`
 *     button directly BENEATH the selected card via the `showConfirm` slot. The
 *     auto-matched primary (needs-confirmation) renders green (`tone="primary"`).
 *   - `BlankCard`     — an empty slot for a candidate below the 70% floor.
 *   - `ConfirmedChip` — the confirmed primary as a "{Type}: {number}" chip with a
 *     remove (×) that returns the reviewer to candidate selection.
 *
 * Every % is REAL (from `sprk_associationprovenance` confidence) and is always
 * paired with its *why*. All writes are orchestrated by the parent
 * `EmailConnectionsReview` (it owns `applyRegardingSelection` / `unlinkRegarding`)
 * and handed down as callbacks. Fluent v9 tokens only (ADR-021, dark-mode
 * correct). No `as React.ComponentType` cast (NFR-05).
 */
import * as React from 'react';
import { mergeClasses, Text, Button, Tooltip } from '@fluentui/react-components';
import { Checkmark16Filled, Dismiss12Regular } from '@fluentui/react-icons';
import { entityLabel, type PrimaryCandidate } from '../../logic/connections';
import type { ConnectionsReviewStyles } from './EmailConnectionsReview.styles';

function pct(confidence: number): string {
  return `${Math.round(confidence * 100)}%`;
}

// ── One 2-line candidate card (click to select; Confirm shows under the selection) ──
export function CandidateCard({
  candidate,
  selected,
  tone,
  showConfirm,
  compact = false,
  busy,
  readOnly,
  onSelect,
  onConfirm,
  s,
}: {
  candidate: PrimaryCandidate;
  selected: boolean;
  /** `'primary'` → green auto-matched card; `'select'` → brand-blue when picked. */
  tone: 'select' | 'primary';
  showConfirm: boolean;
  /**
   * Reconcile variant (owner UAT 2026-08-14): the prototype's COMPACT single-row card
   * — `{name}` + `{type} · {n}% match` on the left, an always-visible inline "Confirm"
   * button on the right, no 72px floor. Default (`false`) keeps the email-form's taller
   * 2-line select-then-confirm card unchanged.
   */
  compact?: boolean;
  busy: boolean;
  readOnly: boolean;
  onSelect: () => void;
  onConfirm: () => void;
  s: ConnectionsReviewStyles;
}): React.ReactElement {
  const selectedClass = tone === 'primary' ? s.cardPrimary : s.cardSelected;

  // ── Compact (reconcile) — one row: meta on the left, inline Confirm on the right ──
  if (compact) {
    const type = candidate.entity ? entityLabel(candidate.entity) : 'Record';
    return (
      <div className={s.cardCell}>
        <div
          className={mergeClasses(s.candCompact, selected && selectedClass)}
          aria-label={`${candidate.recordNumber ? candidate.recordNumber + ' ' : ''}${candidate.targetName}`}
        >
          <div className={s.compactMeta} onClick={() => !readOnly && onSelect()}>
            <Text className={s.compactName} title={candidate.targetName}>
              {candidate.recordNumber ? `${candidate.recordNumber} : ${candidate.targetName}` : candidate.targetName}
            </Text>
            <Text className={s.compactScore}>
              {type} · {pct(candidate.confidence)} match
            </Text>
          </div>
          {!readOnly ? (
            <Button
              appearance="primary"
              size="small"
              icon={<Checkmark16Filled />}
              disabled={busy}
              onClick={onConfirm}
            >
              Confirm
            </Button>
          ) : null}
        </div>
      </div>
    );
  }

  return (
    <div className={s.cardCell}>
      <div
        role="radio"
        aria-checked={selected}
        aria-label={`${candidate.recordNumber ? candidate.recordNumber + ' ' : ''}${candidate.targetName}`}
        tabIndex={readOnly ? -1 : 0}
        className={mergeClasses(s.card, selected && selectedClass)}
        onClick={() => !readOnly && onSelect()}
        onKeyDown={e => {
          if (readOnly) return;
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            onSelect();
          }
        }}
      >
        <div className={s.cardHeadRow}>
          <Text className={s.cardIdentity}>
            {candidate.recordNumber ? (
              <>
                <span className={s.recNum}>{candidate.recordNumber}</span>
                <span className={s.cardName}> : {candidate.targetName}</span>
              </>
            ) : (
              <span className={s.recNum}>{candidate.targetName}</span>
            )}
          </Text>
          <span className={s.pctTag}>{pct(candidate.confidence)}</span>
        </div>
        {candidate.matchReason ? <Text className={s.cardReason}>{candidate.matchReason}</Text> : null}
      </div>
      {showConfirm && !readOnly ? (
        <div className={s.confirmSlot}>
          <Button appearance="primary" size="small" icon={<Checkmark16Filled />} disabled={busy} onClick={onConfirm}>
            Confirm
          </Button>
        </div>
      ) : null}
    </div>
  );
}

// ── Blank slot — a candidate below the 70% floor (legitimately no match here) ──
export function BlankCard({ s }: { s: ConnectionsReviewStyles }): React.ReactElement {
  return (
    <div className={s.cardCell}>
      <div className={s.cardBlank} aria-hidden="true">
        No confident match
      </div>
    </div>
  );
}

// ── Confirmed primary — "{Type}: {number}" chip, clickable to open the record,
//    with a remove (×). Rendered in the merged "Related to" SECTION HEADER so the
//    confirmed association is visible while the section is collapsed. ──
export function ConfirmedChip({
  primary,
  busy,
  readOnly,
  onOpen,
  onRemove,
  s,
}: {
  primary: PrimaryCandidate;
  busy: boolean;
  readOnly: boolean;
  /** Open the associated record (omit → the value renders as plain text). */
  onOpen?: () => void;
  onRemove: () => void;
  s: ConnectionsReviewStyles;
}): React.ReactElement {
  // Prefer the denorm type label ("Matter") when the primary was filed via denorm
  // fields only (no typed `entity` to resolve); else map the typed entity; else the
  // generic fallback (owner UAT 2026-07-31 item 1). Feeds BOTH the chip and its tooltip.
  const type = primary.typeLabel ?? (primary.entity ? entityLabel(primary.entity) : 'Record');
  const value = primary.recordNumber ?? primary.targetName;
  const title = `${type} · ${primary.targetName}${primary.recordNumber ? ' · ' + primary.recordNumber : ''}`;
  return (
    <div className={s.chipRow} data-testid="association-confirmed">
      <span className={s.chip} title={title}>
        <Text className={s.chipType}>{type}:</Text>
        {onOpen ? (
          <button type="button" className={s.chipLink} onClick={onOpen} title={`Open ${title}`}>
            {value}
          </button>
        ) : (
          <Text className={s.chipValue}>{value}</Text>
        )}
        {!readOnly ? (
          <Tooltip content="Remove" relationship="label">
            <Button
              className={s.chipRemove}
              appearance="transparent"
              size="small"
              icon={<Dismiss12Regular />}
              aria-label="Remove"
              disabled={busy}
              onClick={onRemove}
            />
          </Tooltip>
        ) : null}
      </span>
    </div>
  );
}
