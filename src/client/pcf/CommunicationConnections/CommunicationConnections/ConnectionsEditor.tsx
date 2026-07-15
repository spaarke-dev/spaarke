/**
 * Connections editor (W4 / FR-17) — the multi-association review surface.
 *
 * An email is regarding MANY records at once (Matter + Organization + Contact +
 * Invoice …, per ADR-024's regarding family and task-015's multi-field candidates).
 * This surface shows every typed association slot with its own suggestion +
 * confidence + confirm/change, lets the user add missing dimensions, and offers
 * "create from this email" actions (Event / To Do / Invoice) — some engine-suggested
 * from the structural-detector signals (task 014).
 *
 * PORTED from the converged prototype
 * `code-pages/CommunicationPage/src/components/ConnectionsEditor.tsx` (W4 pivot,
 * task 042). Changes from the prototype (wiring only — the UX is unchanged):
 *   - React 16 JSX element types (`JSX.Element`, not `React.JSX.Element`) per ADR-022.
 *   - Confirm / Accept-all / Change / File-here / Link-another / Create are no
 *     longer visual stubs — they invoke the callback props the PCF App supplies,
 *     which drive the real `applyResolverFields` write path + create-flow launch.
 *   - `readOnly` suppresses the write affordances (FR-24 parity with RegardingResolver).
 * Only the `rail` layout is used inside the PCF (the 34% accessories column); the
 * `card` / `summary` hosts are retained from the prototype for reuse/parity.
 */

import * as React from 'react';
import { makeStyles, tokens, Text, Button, Badge, Divider, Tooltip } from '@fluentui/react-components';
import {
  Briefcase20Regular,
  Building20Regular,
  Person20Regular,
  Receipt20Regular,
  CalendarLtr20Regular,
  DocumentText20Regular,
  Sparkle16Regular,
  CheckmarkCircle20Filled,
  ArrowSwap16Regular,
  Link20Regular,
  ChevronDown16Regular,
  ChevronUp16Regular,
  Star16Filled,
  Star16Regular,
} from '@fluentui/react-icons';
import { AssociationStatus, type ICommunicationRecord } from './types';
import {
  type ProvenanceDoc,
  type ProvenanceCandidate,
  type Connection,
  type CreateAction,
  confidenceBand,
  deriveConnections,
  deriveCreateActions,
  topCandidate,
} from './provenance';

export type ReviewLayout = 'summary' | 'card' | 'rail';

/** Write/launch callbacks the PCF App supplies to turn the review stubs into real actions. */
export interface ConnectionsCallbacks {
  /** Confirm a slot's suggestion (or, for ambiguous, the chosen alternative). */
  onConfirm?: (conn: Connection, chosen?: ProvenanceCandidate) => void;
  /** Confirm every outstanding suggested slot at once. */
  onAcceptAll?: (conns: Connection[]) => void;
  /** Reviewer wants to change/override a slot (captures an override reason). */
  onChange?: (conn: Connection) => void;
  /**
   * Designate this (confirmed) slot as the PRIMARY regarding — the one whose
   * target populates the denormalized `sprk_regardingrecord*` fields. An email is
   * regarding many records (one per entity type), but exactly one is the primary
   * shown in the Regarding Record fields (owner requirement, 2026-07-15).
   */
  onSetPrimary?: (conn: Connection) => void;
  /** Open the regarding picker to add a missing dimension. */
  onLinkAnother?: () => void;
  /** Launch a create-from-email flow (Event / To Do / Invoice). */
  onCreate?: (action: CreateAction) => void;
}

export interface ConnectionsEditorProps extends ConnectionsCallbacks {
  record: ICommunicationRecord;
  provenance: ProvenanceDoc | null;
  layout: ReviewLayout;
  readOnly?: boolean;
  /** True while a write is in flight — disables affordances. */
  busy?: boolean;
  /**
   * Fields the App has SUCCESSFULLY filed (single source of truth). Controlled by
   * the App from write-success only, so a failed write never renders a row as
   * "filed" (W1). Optional for the card/summary reuse hosts.
   */
  confirmedFields?: Set<string>;
  /**
   * The field currently designated PRIMARY (owns the denormalized Regarding
   * Record fields). When omitted, the editor defaults the badge to the first
   * confirmed slot in priority order.
   */
  primaryField?: string;
}

const ENTITY_ICON: Record<string, JSX.Element> = {
  sprk_matter: <Briefcase20Regular />,
  sprk_organization: <Building20Regular />,
  account: <Building20Regular />,
  contact: <Person20Regular />,
  sprk_invoice: <Receipt20Regular />,
  sprk_event: <CalendarLtr20Regular />,
};
function entityIcon(entity: string): JSX.Element {
  return ENTITY_ICON[entity] ?? <DocumentText20Regular />;
}
const CREATE_ICON = {
  event: <CalendarLtr20Regular />,
  todo: <CheckmarkCircle20Filled />,
  invoice: <Receipt20Regular />,
} as const;

const BAND_WORD = { high: 'High', medium: 'Medium', low: 'Low' } as const;

const useStyles = makeStyles({
  wrap: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  card: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusLarge,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    boxShadow: tokens.shadow4,
    padding: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
  },
  rail: {
    padding: tokens.spacingVerticalL,
    paddingInline: tokens.spacingHorizontalL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },

  headRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  kicker: { color: tokens.colorNeutralForeground3, textTransform: 'uppercase', letterSpacing: '0.04em' },
  grow: { flex: 1 },
  rollup: { color: tokens.colorNeutralForeground2 },

  row: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalSNudge,
  },
  slotIcon: { color: tokens.colorNeutralForeground3, display: 'flex', flexShrink: 0 },
  slotLabel: { width: '92px', flexShrink: 0, color: tokens.colorNeutralForeground3 },
  target: { display: 'flex', flexDirection: 'column', minWidth: 0, flex: 1 },
  targetName: { fontWeight: tokens.fontWeightSemibold },
  confHigh: { color: tokens.colorPaletteGreenForeground1, whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },
  confMedium: {
    color: tokens.colorPaletteMarigoldForeground1,
    whiteSpace: 'nowrap',
    fontVariantNumeric: 'tabular-nums',
  },
  confLow: { color: tokens.colorNeutralForeground3, whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },
  rowActions: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, flexShrink: 0 },
  confirmedTick: { color: tokens.colorPaletteGreenForeground1, display: 'flex' },

  addRow: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap', paddingTop: tokens.spacingVerticalXS },
  createRow: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  suggestedChip: { border: `1px dashed ${tokens.colorBrandStroke1}` },
  ambigNote: { color: tokens.colorPaletteRedForeground1 },
  altRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    paddingLeft: '108px',
    paddingBlock: '2px',
  },

  summaryBar: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalSNudge,
    paddingInline: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground2,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  summaryExpand: {
    padding: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
    backgroundColor: tokens.colorNeutralBackground2,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
});

function confClass(s: ReturnType<typeof useStyles>, v: number): string {
  const b = confidenceBand(v);
  return b === 'high' ? s.confHigh : b === 'medium' ? s.confMedium : s.confLow;
}
function confText(v: number): string {
  return `${Math.round(v * 100)}% · ${BAND_WORD[confidenceBand(v)]}`;
}

function ConnectionRow({
  conn,
  confirmed,
  isPrimary,
  readOnly,
  busy,
  onConfirm,
  onChange,
  onSetPrimary,
}: {
  conn: Connection;
  confirmed: boolean;
  isPrimary: boolean;
  readOnly: boolean;
  busy: boolean;
  onConfirm: (conn: Connection, chosen?: ProvenanceCandidate) => void;
  onChange: (conn: Connection) => void;
  onSetPrimary: (conn: Connection) => void;
}): JSX.Element {
  const s = useStyles();
  const isConfirmed = confirmed || conn.status === 'confirmed';

  return (
    <>
      <div className={s.row}>
        <span className={s.slotIcon}>{entityIcon(conn.entity)}</span>
        <Text size={200} className={s.slotLabel}>
          {conn.slotLabel}
        </Text>
        <div className={s.target}>
          {conn.status === 'ambiguous' ? (
            <Text className={s.ambigNote} weight="semibold" size={300}>
              Two possible matches — choose one
            </Text>
          ) : (
            <Text className={s.targetName} size={300} truncate wrap={false}>
              {conn.targetName}
            </Text>
          )}
        </div>
        {conn.status !== 'ambiguous' && (
          <Text size={200} className={confClass(s, conn.confidence)}>
            {confText(conn.confidence)}
          </Text>
        )}
        <div className={s.rowActions}>
          {isConfirmed ? (
            <>
              <span className={s.confirmedTick}>
                <CheckmarkCircle20Filled />
              </span>
              {isPrimary ? (
                <Badge appearance="tint" color="warning" icon={<Star16Filled />}>
                  Primary
                </Badge>
              ) : (
                !readOnly && (
                  <Tooltip content="Show this record in the Regarding Record fields" relationship="label">
                    <Button
                      size="small"
                      appearance="subtle"
                      icon={<Star16Regular />}
                      disabled={busy}
                      onClick={() => onSetPrimary(conn)}
                    >
                      Primary
                    </Button>
                  </Tooltip>
                )
              )}
              {!readOnly && (
                <Button
                  size="small"
                  appearance="subtle"
                  icon={<ArrowSwap16Regular />}
                  disabled={busy}
                  onClick={() => onChange(conn)}
                >
                  Change
                </Button>
              )}
            </>
          ) : readOnly ? null : conn.status === 'ambiguous' ? (
            <Button size="small" appearance="secondary" disabled={busy} onClick={() => onChange(conn)}>
              Review
            </Button>
          ) : (
            <>
              <Button
                size="small"
                appearance="primary"
                disabled={busy}
                onClick={() => onConfirm(conn)}
              >
                Confirm
              </Button>
              <Tooltip content="Pick a different record" relationship="label">
                <Button
                  size="small"
                  appearance="subtle"
                  icon={<ArrowSwap16Regular />}
                  disabled={busy}
                  onClick={() => onChange(conn)}
                />
              </Tooltip>
            </>
          )}
        </div>
      </div>
      {conn.status === 'ambiguous' &&
        !readOnly &&
        conn.alternatives?.map((alt, i) => (
          <div key={i} className={s.altRow}>
            <Text size={200} weight="semibold">
              {alt.targetName}
            </Text>
            <Text size={200} className={confClass(s, alt.reinforcedConfidence)}>
              {confText(alt.reinforcedConfidence)}
            </Text>
            <div className={s.grow} />
            <Button size="small" appearance="primary" disabled={busy} onClick={() => onConfirm(conn, alt)}>
              File here
            </Button>
          </div>
        ))}
    </>
  );
}

function CreateActions({
  actions,
  readOnly,
  busy,
  onCreate,
}: {
  actions: CreateAction[];
  readOnly: boolean;
  busy: boolean;
  onCreate: (action: CreateAction) => void;
}): JSX.Element {
  const s = useStyles();
  return (
    <div className={s.createRow}>
      {actions.map(a => (
        <Tooltip key={a.kind} content={a.reason ?? a.label} relationship="label">
          <Button
            size="small"
            appearance={a.suggested ? 'outline' : 'subtle'}
            className={a.suggested ? s.suggestedChip : undefined}
            icon={CREATE_ICON[a.kind]}
            disabled={readOnly || busy}
            onClick={() => onCreate(a)}
          >
            {a.label}
            {a.suggested ? ' ✨' : ''}
          </Button>
        </Tooltip>
      ))}
    </div>
  );
}

/** The shared body: rollup + slots + add + create. */
function EditorBody({
  record,
  provenance,
  readOnly,
  busy,
  confirmedFields,
  primaryField,
  onConfirm,
  onAcceptAll,
  onChange,
  onSetPrimary,
  onLinkAnother,
  onCreate,
}: {
  record: ICommunicationRecord;
  provenance: ProvenanceDoc;
  readOnly: boolean;
  busy: boolean;
  confirmedFields: Set<string>;
  primaryField?: string;
  onConfirm: (conn: Connection, chosen?: ProvenanceCandidate) => void;
  onAcceptAll: (conns: Connection[]) => void;
  onChange: (conn: Connection) => void;
  onSetPrimary: (conn: Connection) => void;
  onLinkAnother: () => void;
  onCreate: (action: CreateAction) => void;
}): JSX.Element {
  const s = useStyles();
  const isResolved = record.sprk_associationstatus === AssociationStatus.Resolved;
  const connections = React.useMemo(() => deriveConnections(provenance, isResolved), [provenance, isResolved]);
  const createActions = React.useMemo(() => deriveCreateActions(provenance), [provenance]);

  // Controlled by the App (write-success only) — no local optimistic state (W1).
  const handleAcceptAll = () => {
    onAcceptAll(connections.filter(c => c.status === 'suggested'));
  };

  const isSlotConfirmed = (c: Connection) => c.status === 'confirmed' || confirmedFields.has(c.field);
  // Effective primary: the explicitly-designated field if it's confirmed, else
  // the first confirmed slot in priority order (connections are SLOT_META-sorted).
  const effectivePrimary =
    primaryField && connections.some(c => c.field === primaryField && isSlotConfirmed(c))
      ? primaryField
      : connections.find(isSlotConfirmed)?.field;

  const toReview = connections.filter(c => c.status !== 'confirmed' && !confirmedFields.has(c.field)).length;
  const confirmedCount = connections.length - toReview;

  return (
    <div className={s.wrap}>
      <div className={s.headRow}>
        <Text size={200} weight="semibold" className={s.kicker}>
          Connections
        </Text>
        <Text size={200} className={s.rollup}>
          · {confirmedCount} filed · {toReview} to review
        </Text>
        <div className={s.grow} />
        {!readOnly && toReview > 0 && (
          <Button
            size="small"
            appearance="primary"
            icon={<CheckmarkCircle20Filled />}
            disabled={busy}
            onClick={handleAcceptAll}
          >
            Accept all
          </Button>
        )}
      </div>

      {connections.map(c => (
        <ConnectionRow
          key={c.field}
          conn={c}
          confirmed={confirmedFields.has(c.field)}
          isPrimary={c.field === effectivePrimary}
          readOnly={readOnly}
          busy={busy}
          onConfirm={onConfirm}
          onChange={onChange}
          onSetPrimary={onSetPrimary}
        />
      ))}

      {!readOnly && (
        <div className={s.addRow}>
          <Button
            size="small"
            appearance="subtle"
            icon={<Link20Regular />}
            disabled={busy}
            onClick={onLinkAnother}
          >
            Link another record…
          </Button>
        </div>
      )}

      <Divider />
      <div className={s.headRow}>
        <Text size={200} weight="semibold" className={s.kicker}>
          Create from this email
        </Text>
      </div>
      <CreateActions actions={createActions} readOnly={readOnly} busy={busy} onCreate={onCreate} />
    </div>
  );
}

// ── hosts ─────────────────────────────────────────────────────────────────────

export function ConnectionsEditor(props: ConnectionsEditorProps): JSX.Element | null {
  const { record, provenance, layout, readOnly = false, busy = false } = props;
  const s = useStyles();
  const [open, setOpen] = React.useState(false);

  const bodyProps = {
    readOnly,
    busy,
    confirmedFields: props.confirmedFields ?? new Set<string>(),
    primaryField: props.primaryField,
    onConfirm: props.onConfirm ?? (() => undefined),
    onAcceptAll: props.onAcceptAll ?? (() => undefined),
    onChange: props.onChange ?? (() => undefined),
    onSetPrimary: props.onSetPrimary ?? (() => undefined),
    onLinkAnother: props.onLinkAnother ?? (() => undefined),
    onCreate: props.onCreate ?? (() => undefined),
  };

  // Resolved with no provenance: simple confirmation.
  if (!provenance) {
    if (record.sprk_associationstatus === AssociationStatus.Resolved) {
      return (
        <div className={layout === 'rail' ? s.rail : s.card}>
          <div className={s.headRow}>
            <span className={s.confirmedTick}>
              <CheckmarkCircle20Filled />
            </span>
            <Text weight="semibold">Filed to {record.sprk_regardingrecordname ?? 'this record'}</Text>
          </div>
        </div>
      );
    }
    return null;
  }

  const isResolved = record.sprk_associationstatus === AssociationStatus.Resolved;
  const connections = deriveConnections(provenance, isResolved);
  const toReview = connections.filter(c => c.status !== 'confirmed').length;

  if (layout === 'rail') {
    return (
      <aside className={s.rail}>
        <EditorBody record={record} provenance={provenance} {...bodyProps} />
      </aside>
    );
  }

  if (layout === 'card') {
    return (
      <div className={s.card}>
        <EditorBody record={record} provenance={provenance} {...bodyProps} />
      </div>
    );
  }

  // summary: one-line bar that expands.
  const top = topCandidate(provenance);
  return (
    <>
      <div className={s.summaryBar}>
        <Link20Regular />
        <Text>
          {isResolved ? 'Filed to ' : 'Connected to '}
          <b>{connections.length}</b> {connections.length === 1 ? 'record' : 'records'}
          {toReview > 0 && (
            <>
              {' '}
              · <b>{toReview}</b> to review
            </>
          )}
        </Text>
        {top && !isResolved && (
          <Text size={200} className={confClass(s, top.reinforcedConfidence)}>
            top {confText(top.reinforcedConfidence)}
          </Text>
        )}
        <div className={s.grow} />
        {toReview > 0 && (
          <Badge appearance="tint" color="warning" icon={<Sparkle16Regular />}>
            needs review
          </Badge>
        )}
        <Button
          size="small"
          appearance="secondary"
          icon={open ? <ChevronUp16Regular /> : <ChevronDown16Regular />}
          iconPosition="after"
          onClick={() => setOpen(v => !v)}
        >
          {open ? 'Hide' : 'Review'}
        </Button>
      </div>
      {open && (
        <div className={s.summaryExpand}>
          <EditorBody record={record} provenance={provenance} {...bodyProps} />
        </div>
      )}
    </>
  );
}
