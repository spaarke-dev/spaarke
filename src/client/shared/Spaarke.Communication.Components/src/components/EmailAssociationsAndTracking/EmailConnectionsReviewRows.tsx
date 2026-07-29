/**
 * Row-level sub-components for the ASSOCIATIONS review view (task 035). Split
 * out of `EmailConnectionsReview.tsx` at code-review time (Step 9.5) purely
 * to keep the main component file under this repo's review-metric line
 * thresholds — no behavior change. Each component is presentational; all
 * writes are orchestrated by the parent `EmailConnectionsReview` (it owns the
 * `applyRegardingSelection` / `unlinkRegarding` calls) and handed down as
 * callback props.
 */
import * as React from 'react';
import { mergeClasses, Text, Button, Badge, Tooltip, Radio, RadioGroup } from '@fluentui/react-components';
import { Dismiss16Regular, Checkmark16Regular, ArrowSwap16Regular } from '@fluentui/react-icons';
import { PolymorphicPicker, type RecordTypeCatalogEntry, type IPolymorphicPickerWebApi } from '@spaarke/ui-components';
import { groupCandidatesByName, entityLabel, type Connection, type CandidateGroup } from '../../logic/connections';
import { entityIcon } from './EmailConnectionsReview.helpers';
import type { ConnectionsReviewStyles } from './EmailConnectionsReview.styles';

// ── "Needs your decision" — one ambiguous conflict as selectable options ──
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
  const [selected, setSelected] = React.useState<string | null>(null);
  const nameOf = (g: CandidateGroup) =>
    resolveDisplayName?.(g.candidates[0].targetEntity, g.candidates[0].targetId) ?? g.targetName;

  return (
    <div>
      <Text className={s.secHint}>
        {groups.length} possible matches for {conn.slotLabel.toLowerCase()} — pick the one this email is really about.
      </Text>
      <RadioGroup value={selected ?? ''} onChange={(_, d) => setSelected(d.value)}>
        <div className={s.options}>
          {groups.map((g, i) => (
            <div
              key={i}
              className={mergeClasses(s.opt, selected === String(i) && s.optSel)}
              onClick={() => !readOnly && setSelected(String(i))}
              role="presentation"
            >
              <Radio value={String(i)} disabled={readOnly} aria-label={nameOf(g)} />
              <div className={s.optRec}>
                <Text className={s.optName}>
                  {nameOf(g)}
                  {g.recordNumber ? <span className={s.recNum}> · {g.recordNumber}</span> : null}
                  {g.candidates.length > 1 ? <span className={s.typeTag}> · {g.candidates.length} records</span> : null}
                </Text>
              </div>
              <Text className={s.confLbl}>{Math.round(g.confidence * 100)}%</Text>
            </div>
          ))}
        </div>
      </RadioGroup>
      {!readOnly && (
        <Button
          appearance="primary"
          disabled={selected === null || busy}
          onClick={() => {
            if (selected === null) return;
            const top = groups[Number(selected)].candidates[0];
            onConfirm(conn, { targetEntity: top.targetEntity, targetId: top.targetId, targetName: top.targetName });
          }}
        >
          Confirm
        </Button>
      )}
    </div>
  );
}

// ── "Filed automatically" — a done row with Change (re-file via picker) / Dismiss (unlink) ──
export function FiledRow({
  conn,
  first,
  busy,
  readOnly,
  resolveDisplayName,
  isChanging,
  onRequestChange,
  onCancelChange,
  onChangeSelected,
  onDismiss,
  pickerWebApi,
  catalog,
  s,
}: {
  conn: Connection;
  first: boolean;
  busy: boolean;
  readOnly: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  isChanging: boolean;
  onRequestChange: () => void;
  onCancelChange: () => void;
  onChangeSelected: (entityType: string, recordId: string, recordName: string) => void;
  onDismiss: () => void;
  pickerWebApi: IPolymorphicPickerWebApi;
  catalog: readonly RecordTypeCatalogEntry[];
  s: ConnectionsReviewStyles;
}): React.ReactElement {
  const name = resolveDisplayName?.(conn.entity, conn.targetId) ?? conn.targetName;
  const scopedCatalog = React.useMemo(() => catalog.filter(c => c.logicalName === conn.entity), [catalog, conn.entity]);

  return (
    <div className={mergeClasses(s.row, !first && s.rowBorder)}>
      <span className={s.ico}>{entityIcon(conn.entity)}</span>
      <div className={s.rec}>
        <Text className={s.recName}>
          {name}
          {conn.recordNumber ? <span className={s.recNum}>· {conn.recordNumber}</span> : null}
          <span className={s.typeTag}>{entityLabel(conn.entity)}</span>
        </Text>
      </div>
      <Badge appearance="tint" color="success">
        Linked
      </Badge>
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
          <Tooltip content="Dismiss" relationship="label">
            <Button
              size="small"
              appearance="subtle"
              icon={<Dismiss16Regular />}
              aria-label="Dismiss"
              disabled={busy}
              onClick={onDismiss}
            />
          </Tooltip>
        </div>
      )}
    </div>
  );
}

// ── "Suggested" — a soft match to Confirm or Dismiss (client-side hide, no write) ──
export function SuggestedRow({
  conn,
  first,
  busy,
  readOnly,
  resolveDisplayName,
  onConfirm,
  onDismiss,
  s,
}: {
  conn: Connection;
  first: boolean;
  busy: boolean;
  readOnly: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  onConfirm: (conn: Connection) => void;
  onDismiss: (key: string) => void;
  s: ConnectionsReviewStyles;
}): React.ReactElement {
  const name = resolveDisplayName?.(conn.entity, conn.targetId) ?? conn.targetName;
  return (
    <div className={mergeClasses(s.row, !first && s.rowBorder)}>
      <span className={s.ico}>{entityIcon(conn.entity)}</span>
      <div className={s.rec}>
        <Text className={s.recName}>
          {name}
          {conn.recordNumber ? <span className={s.recNum}>· {conn.recordNumber}</span> : null}
          <span className={s.typeTag}>{entityLabel(conn.entity)}</span>
        </Text>
      </div>
      <span />
      <div className={s.rowActs}>
        {!readOnly && (
          <>
            <Tooltip content="Confirm" relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Checkmark16Regular />}
                aria-label="Confirm"
                disabled={busy}
                onClick={() => onConfirm(conn)}
              />
            </Tooltip>
            <Tooltip content="Dismiss" relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Dismiss16Regular />}
                aria-label="Dismiss"
                disabled={busy}
                onClick={() => onDismiss(conn.field)}
              />
            </Tooltip>
          </>
        )}
      </div>
    </div>
  );
}
