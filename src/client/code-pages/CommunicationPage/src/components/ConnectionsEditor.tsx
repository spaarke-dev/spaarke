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
 * Hosted three ways for the layout comparison: 'rail' | 'card' | 'summary'.
 * Prototype: confirm/change/create are visual stubs; task 042 wires the real
 * PolymorphicResolverService + create flows.
 */

import * as React from 'react';
import {
  makeStyles,
  tokens,
  mergeClasses,
  Text,
  Button,
  Badge,
  Divider,
  Checkbox,
  Tooltip,
} from '@fluentui/react-components';
import {
  Briefcase20Regular,
  Building20Regular,
  Person20Regular,
  Receipt20Regular,
  CalendarLtr20Regular,
  DocumentText20Regular,
  Add20Regular,
  Sparkle16Regular,
  CheckmarkCircle20Filled,
  Warning20Regular,
  ArrowSwap16Regular,
  Link20Regular,
  ChevronDown16Regular,
  ChevronUp16Regular,
} from '@fluentui/react-icons';
import { AssociationStatus, type ICommunicationRecord } from '../types/communication';
import {
  type ProvenanceDoc,
  type Connection,
  type CreateAction,
  confidenceBand,
  deriveConnections,
  deriveCreateActions,
  rationaleSentence,
  topCandidate,
} from './provenance';

export type ReviewLayout = 'summary' | 'card' | 'rail';

export interface ConnectionsEditorProps {
  record: ICommunicationRecord;
  provenance: ProvenanceDoc | null;
  layout: ReviewLayout;
}

const ENTITY_ICON: Record<string, React.JSX.Element> = {
  sprk_matter: <Briefcase20Regular />,
  sprk_organization: <Building20Regular />,
  account: <Building20Regular />,
  contact: <Person20Regular />,
  sprk_invoice: <Receipt20Regular />,
  sprk_event: <CalendarLtr20Regular />,
};
function entityIcon(entity: string): React.JSX.Element {
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
    paddingBlock: 2,
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
  onConfirm,
}: {
  conn: Connection;
  confirmed: boolean;
  onConfirm: (field: string) => void;
}): React.JSX.Element {
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
              <Button size="small" appearance="subtle" icon={<ArrowSwap16Regular />}>
                Change
              </Button>
            </>
          ) : conn.status === 'ambiguous' ? (
            <Button size="small" appearance="secondary">
              Review
            </Button>
          ) : (
            <>
              <Button size="small" appearance="primary" onClick={() => onConfirm(conn.field)}>
                Confirm
              </Button>
              <Tooltip content="Pick a different record" relationship="label">
                <Button size="small" appearance="subtle" icon={<ArrowSwap16Regular />} />
              </Tooltip>
            </>
          )}
        </div>
      </div>
      {conn.status === 'ambiguous' &&
        conn.alternatives?.map((alt, i) => (
          <div key={i} className={s.altRow}>
            <Text size={200} weight="semibold">
              {alt.targetName}
            </Text>
            <Text size={200} className={confClass(s, alt.reinforcedConfidence)}>
              {confText(alt.reinforcedConfidence)}
            </Text>
            <div className={s.grow} />
            <Button size="small" appearance="primary" onClick={() => onConfirm(conn.field)}>
              File here
            </Button>
          </div>
        ))}
    </>
  );
}

function CreateActions({ actions }: { actions: CreateAction[] }): React.JSX.Element {
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
}: {
  record: ICommunicationRecord;
  provenance: ProvenanceDoc;
}): React.JSX.Element {
  const s = useStyles();
  const isResolved = record.sprk_associationstatus === AssociationStatus.Resolved;
  const connections = React.useMemo(() => deriveConnections(provenance, isResolved), [provenance, isResolved]);
  const createActions = React.useMemo(() => deriveCreateActions(provenance), [provenance]);
  const [confirmed, setConfirmed] = React.useState<Set<string>>(new Set());

  const confirm = (field: string) => setConfirmed(prev => new Set(prev).add(field));
  const confirmAll = () => setConfirmed(new Set(connections.filter(c => c.status === 'suggested').map(c => c.field)));

  const toReview = connections.filter(c => c.status !== 'confirmed' && !confirmed.has(c.field)).length;
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
        {toReview > 0 && (
          <Button size="small" appearance="primary" icon={<CheckmarkCircle20Filled />} onClick={confirmAll}>
            Accept all
          </Button>
        )}
      </div>

      {connections.map(c => (
        <ConnectionRow key={c.field} conn={c} confirmed={confirmed.has(c.field)} onConfirm={confirm} />
      ))}

      <div className={s.addRow}>
        <Button size="small" appearance="subtle" icon={<Link20Regular />}>
          Link another record…
        </Button>
      </div>

      <Divider />
      <div className={s.headRow}>
        <Text size={200} weight="semibold" className={s.kicker}>
          Create from this email
        </Text>
      </div>
      <CreateActions actions={createActions} />
    </div>
  );
}

// ── hosts ─────────────────────────────────────────────────────────────────────

export function ConnectionsEditor({ record, provenance, layout }: ConnectionsEditorProps): React.JSX.Element | null {
  const s = useStyles();
  const [open, setOpen] = React.useState(false);

  // Resolved with no provenance: simple confirmation.
  if (!provenance) {
    if (record.sprk_associationstatus === AssociationStatus.Resolved) {
      return (
        <div className={layout === 'rail' ? s.rail : s.card}>
          <div className={s.headRow}>
            <span className={s.confirmedTick}>
              <CheckmarkCircle20Filled />
            </span>
            <Text weight="semibold">Filed to {record.sprk_regardingrecordname}</Text>
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
        <EditorBody record={record} provenance={provenance} />
      </aside>
    );
  }

  if (layout === 'card') {
    return (
      <div className={s.card}>
        <EditorBody record={record} provenance={provenance} />
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
          <EditorBody record={record} provenance={provenance} />
        </div>
      )}
    </>
  );
}
