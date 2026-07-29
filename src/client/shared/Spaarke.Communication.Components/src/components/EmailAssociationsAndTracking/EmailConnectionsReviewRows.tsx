/**
 * Row-level sub-components for the reading-pane ASSOCIATION RESOLVER (email-
 * communication-solution-r5, reading-pane MAIN-AREA redesign, section #6). The
 * resolver answers ONE plain question with an obvious action per state; these
 * are the per-state presentations:
 *
 *   - `DecisionBlock`   — AMBIGUOUS (2+ strong candidates conflict): "Which is
 *     this about?" + a ranked radio list (best pre-selected), each candidate
 *     showing {label} · {real %} · {plain-English rationale}. Enter confirms.
 *   - `SuggestedMatch`  — a single not-yet-filed candidate. HIGH confidence →
 *     the "clear match" one-liner ("This email looks like it's about X — why ·
 *     97%") with [✓ Confirm] / "Not this". LOW/MED → "Possible match: X · % ·
 *     why" with [Confirm] / "Not related".
 *   - `FiledRow`        — FILED (green, silent): "✓ Filed to X" + Change / Remove.
 *
 * Every row pairs the real % (from `sprk_associationprovenance` confidence) with
 * its *why* — never a bare, unexplained percentage. All writes are orchestrated
 * by the parent `EmailConnectionsReview` (it owns `applyRegardingSelection` /
 * `unlinkRegarding`) and handed down as callback props. Fluent v9 tokens only
 * (ADR-021, dark-mode correct). No `as React.ComponentType` cast (NFR-05).
 */
import * as React from 'react';
import { mergeClasses, Text, Button, Tooltip, Radio, RadioGroup } from '@fluentui/react-components';
import { Checkmark16Filled, ArrowSwap16Regular, Dismiss16Regular } from '@fluentui/react-icons';
import { PolymorphicPicker, type RecordTypeCatalogEntry, type IPolymorphicPickerWebApi } from '@spaarke/ui-components';
import {
  groupCandidatesByName,
  confidenceBand,
  entityLabel,
  type Connection,
  type CandidateGroup,
} from '../../logic/connections';
import type { ConnectionsReviewStyles } from './EmailConnectionsReview.styles';

function pct(confidence: number): string {
  return `${Math.round(confidence * 100)}%`;
}

// ── AMBIGUOUS — "Which is this about?" ranked options, best pre-selected ────────
export function DecisionBlock({
  conn,
  busy,
  readOnly,
  resolveDisplayName,
  onConfirm,
  s,
}: {
  conn: Connection;
  busy: boolean;
  readOnly: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  onConfirm: (conn: Connection, chosen: { targetEntity: string; targetId: string; targetName?: string }) => void;
  s: ConnectionsReviewStyles;
}): React.ReactElement {
  const groups: CandidateGroup[] = React.useMemo(() => groupCandidatesByName(conn.alternatives ?? []), [conn.alternatives]);
  // Best (highest-confidence) candidate pre-selected — groups are sorted desc.
  const [selected, setSelected] = React.useState<number>(0);
  const nameOf = (g: CandidateGroup) =>
    resolveDisplayName?.(g.candidates[0].targetEntity, g.candidates[0].targetId) ?? g.targetName;

  const confirm = React.useCallback(() => {
    if (readOnly || busy) return;
    const top = groups[selected]?.candidates[0];
    if (!top) return;
    onConfirm(conn, { targetEntity: top.targetEntity, targetId: top.targetId, targetName: top.targetName });
  }, [groups, selected, readOnly, busy, onConfirm, conn]);

  return (
    <div
      className={s.block}
      data-testid="association-decision"
      onKeyDown={e => {
        // Enter confirms the selected candidate (the arrow-key radio navigation
        // is handled by Fluent's RadioGroup itself).
        if (e.key === 'Enter') {
          e.preventDefault();
          confirm();
        }
      }}
    >
      <Text className={s.question}>Which is this about?</Text>
      <RadioGroup
        className={s.options}
        aria-label="Which is this about?"
        value={String(selected)}
        onChange={(_, d) => !readOnly && setSelected(Number(d.value))}
      >
        {groups.map((g, i) => (
          <div
            key={i}
            className={mergeClasses(s.opt, selected === i && s.optSel)}
            onClick={() => !readOnly && setSelected(i)}
            role="presentation"
          >
            <Radio value={String(i)} disabled={readOnly} aria-label={nameOf(g)} />
            <div className={s.optRec}>
              <Text className={s.optName}>
                {nameOf(g)}
                {g.recordNumber ? <span className={s.recNum}> · {g.recordNumber}</span> : null}
                {g.candidates.length > 1 ? <span className={s.typeTag}> · {g.candidates.length} records</span> : null}
              </Text>
              {g.matchReason ? <Text className={s.why}>{g.matchReason}</Text> : null}
            </div>
            <Text className={s.pct}>{pct(g.confidence)}</Text>
          </div>
        ))}
      </RadioGroup>
      {!readOnly && (
        <div className={s.actionsRow}>
          <Button appearance="primary" icon={<Checkmark16Filled />} disabled={busy} onClick={confirm}>
            Confirm
          </Button>
        </div>
      )}
    </div>
  );
}

// ── SUGGESTED — one candidate, phrased by confidence (clear match vs possible) ──
export function SuggestedMatch({
  conn,
  busy,
  readOnly,
  resolveDisplayName,
  onConfirm,
  onDismiss,
  s,
}: {
  conn: Connection;
  busy: boolean;
  readOnly: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  onConfirm: (conn: Connection) => void;
  onDismiss: (key: string) => void;
  s: ConnectionsReviewStyles;
}): React.ReactElement {
  const name = resolveDisplayName?.(conn.entity, conn.targetId) ?? conn.targetName;
  const isClear = confidenceBand(conn.confidence) === 'high';
  const label = (
    <>
      <span className={s.strongName}>
        {entityLabel(conn.entity)} · {name}
      </span>
      {conn.recordNumber ? <span className={s.recNum}> · {conn.recordNumber}</span> : null}
    </>
  );

  return (
    <div className={s.block} data-testid="association-suggested">
      {isClear ? (
        <Text className={s.leadText}>
          This email looks like it&apos;s about {label}
          {conn.matchReason ? <span className={s.why}> — {conn.matchReason}</span> : null}{' '}
          <span className={s.pct}>· {pct(conn.confidence)}</span>
        </Text>
      ) : (
        <Text className={s.leadText}>
          Possible match: {label} <span className={s.pct}>· {pct(conn.confidence)}</span>
          {conn.matchReason ? <span className={s.why}> · {conn.matchReason}</span> : null}
        </Text>
      )}
      {!readOnly && (
        <div className={s.actionsRow}>
          <Button appearance="primary" icon={<Checkmark16Filled />} disabled={busy} onClick={() => onConfirm(conn)}>
            Confirm
          </Button>
          <Button appearance="transparent" className={s.linkBtn} disabled={busy} onClick={() => onDismiss(conn.field)}>
            {isClear ? 'Not this' : 'Not related'}
          </Button>
        </div>
      )}
    </div>
  );
}

// ── FILED — green, silent confirmed row (Change re-files via picker / Remove unlinks) ──
export function FiledRow({
  conn,
  busy,
  readOnly,
  resolveDisplayName,
  isChanging,
  onRequestChange,
  onCancelChange,
  onChangeSelected,
  onRemove,
  pickerWebApi,
  catalog,
  s,
}: {
  conn: Connection;
  busy: boolean;
  readOnly: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  isChanging: boolean;
  onRequestChange: () => void;
  onCancelChange: () => void;
  onChangeSelected: (entityType: string, recordId: string, recordName: string) => void;
  onRemove: () => void;
  pickerWebApi: IPolymorphicPickerWebApi;
  catalog: readonly RecordTypeCatalogEntry[];
  s: ConnectionsReviewStyles;
}): React.ReactElement {
  const name = resolveDisplayName?.(conn.entity, conn.targetId) ?? conn.targetName;
  const scopedCatalog = React.useMemo(() => catalog.filter(c => c.logicalName === conn.entity), [catalog, conn.entity]);

  return (
    <div className={s.filedRow} data-testid="association-filed">
      <span className={s.filedCheck} aria-hidden="true">
        <Checkmark16Filled />
      </span>
      <div className={s.filedRec}>
        <Text className={s.filedName}>
          {name}
          {conn.recordNumber ? <span className={s.recNum}>· {conn.recordNumber}</span> : null}
          <span className={s.typeTag}>{entityLabel(conn.entity)}</span>
        </Text>
      </div>
      {readOnly ? (
        <span />
      ) : isChanging ? (
        <div className={s.rowActs}>
          <PolymorphicPicker
            title="Change"
            catalog={scopedCatalog}
            webApi={pickerWebApi}
            onSelect={onChangeSelected}
            disabled={busy}
          />
          <Button size="small" appearance="subtle" onClick={onCancelChange}>
            Cancel
          </Button>
        </div>
      ) : (
        <div className={s.rowActs}>
          <Tooltip content="Change" relationship="label">
            <Button
              size="small"
              appearance="subtle"
              icon={<ArrowSwap16Regular />}
              aria-label="Change"
              disabled={busy}
              onClick={onRequestChange}
            />
          </Tooltip>
          <Tooltip content="Remove" relationship="label">
            <Button
              size="small"
              appearance="subtle"
              icon={<Dismiss16Regular />}
              aria-label="Remove"
              disabled={busy}
              onClick={onRemove}
            />
          </Tooltip>
        </div>
      )}
    </div>
  );
}
