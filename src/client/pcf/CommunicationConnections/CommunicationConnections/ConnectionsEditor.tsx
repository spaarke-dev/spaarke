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
import {
  makeStyles,
  tokens,
  Text,
  Button,
  Badge,
  Tooltip,
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItem,
} from '@fluentui/react-components';
import { TODO_REGARDING_CATALOG } from '@spaarke/ui-components';
import {
  Briefcase20Regular,
  Building20Regular,
  Person20Regular,
  Receipt20Regular,
  CalendarLtr20Regular,
  DocumentText20Regular,
  Sparkle16Regular,
  CheckmarkCircle20Filled,
  Checkmark16Regular,
  ArrowSwap16Regular,
  Add16Regular,
  Link16Regular,
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
  type FiledAssociation,
  type AiSuggestedType,
  confidenceBand,
  deriveConnections,
  deriveAiSuggestedTypes,
  mergeFiledConnections,
  topCandidate,
  groupCandidatesByName,
  candidateMatchReason,
  candidateRecordNumber,
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
  /**
   * Open the regarding picker to add a missing dimension. Called with the chosen
   * record type so the host can scope the side-pane lookup to that one entity
   * (type-first UX). Called with no argument = all-types fallback.
   */
  onLinkAnother?: (entityType?: string) => void;
  /**
   * Launch the create form for an AI-suggested record type (e.g. "Create Matter" when the
   * classifier flags a new-matter email). Distinct from onLinkAnother (which files an
   * existing record).
   */
  onCreateType?: (entityType: string) => void;
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
  /**
   * Resolve a friendly display name for a target (entity + GUID). The App supplies
   * this from the catalog-driven name resolution; returns undefined when no name
   * has been resolved yet, in which case the slot's own `targetName` (GUID
   * fallback) is used. Keeps the editor free of any webApi dependency.
   */
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  /**
   * The record's actually-filed regarding lookups (incl. manual "Link another"
   * ones the engine never suggested). Merged into the slots so the surface shows
   * every association, not just engine suggestions.
   */
  filedAssociations?: FiledAssociation[];
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
const BAND_WORD = { high: 'High', medium: 'Medium', low: 'Low' } as const;

// Empty decision doc — used when every association was filed manually (no engine
// provenance) so the surface can still render the filed rows.
const EMPTY_PROVENANCE: ProvenanceDoc = {
  version: 1,
  direction: '',
  decision: {
    status: '',
    autoFiled: false,
    killSwitchEnabled: false,
    autoFileThreshold: 0.85,
    topDeterministicConfidence: 0,
    topConfidence: 0,
    aiInvolved: false,
    reason: '',
  },
  rungsFired: [],
  candidates: [],
  signals: [],
};

// Record types offered by "Link another record…". The reviewer picks the TYPE
// first (Regarding-Resolver UX), then the side-pane lookup opens scoped to that
// one entity. Only types the shared regarding catalog can actually write are
// offered (a picked type must have a lookup attribute to file into).
const LINK_TYPE_LABEL: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_organization: 'Organization',
  contact: 'Contact',
  sprk_invoice: 'Invoice',
  sprk_event: 'Event',
  sprk_workassignment: 'Work Assignment',
  sprk_budget: 'Budget',
  sprk_analysis: 'Analysis',
  sprk_document: 'Document',
  sprk_communication: 'Communication',
  sprk_reportcard: 'Report Card',
};
const LINK_TYPE_ORDER = [
  'sprk_matter',
  'sprk_project',
  'sprk_organization',
  'contact',
  'sprk_invoice',
  'sprk_event',
  'sprk_workassignment',
  'sprk_budget',
  'sprk_analysis',
  'sprk_document',
  'sprk_communication',
  'sprk_reportcard',
];
const LINK_ANOTHER_TYPES: { entityType: string; label: string }[] = LINK_TYPE_ORDER.filter(et =>
  TODO_REGARDING_CATALOG.some(c => c.entityType === et)
).map(et => ({ entityType: et, label: LINK_TYPE_LABEL[et] ?? et }));

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

  // One shared column template drives header + every row so columns align like a grid.
  gridRow: {
    display: 'grid',
    gridTemplateColumns: '160px minmax(0, 1fr) 120px 110px 108px',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalSNudge,
    paddingInline: tokens.spacingHorizontalS,
    borderBottom: `1px solid ${tokens.colorNeutralStroke3}`,
  },
  gridHeader: {
    display: 'grid',
    gridTemplateColumns: '160px minmax(0, 1fr) 120px 110px 108px',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    paddingBlock: tokens.spacingVerticalXS,
    paddingInline: tokens.spacingHorizontalS,
    borderBottom: `2px solid ${tokens.colorNeutralStroke2}`,
    position: 'sticky',
    top: 0,
    backgroundColor: tokens.colorNeutralBackground1,
    zIndex: 1,
  },
  colHead: {
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.03em',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  typeCell: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, minWidth: 0 },
  slotIcon: { color: tokens.colorNeutralForeground3, display: 'flex', flexShrink: 0 },
  slotLabel: {
    color: tokens.colorNeutralForeground2,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  target: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  targetName: {
    fontWeight: tokens.fontWeightSemibold,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
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
  // Match reason (why a record matched) + record number, shown subtly under the record name.
  matchReason: { color: tokens.colorNeutralForeground3, display: 'block', fontStyle: 'italic' },
  recordNumber: { color: tokens.colorNeutralForeground3, fontVariantNumeric: 'tabular-nums' },
  dupCount: { color: tokens.colorNeutralForeground3 },

  // ── grid layout (the review modal's main surface) ──
  gridWrap: { display: 'flex', flexDirection: 'column', height: '100%', gap: tokens.spacingVerticalS, minHeight: 0 },
  gridScroll: { flex: 1, overflowY: 'auto', minHeight: 0 },
  colType: { width: '150px' },
  colConf: { width: '130px' },
  colStatus: { width: '120px' },
  colActions: { width: '120px' },
  actionsCell: { display: 'flex', gap: tokens.spacingHorizontalXS, justifyContent: 'flex-end', width: '100%' },
  subCell: {
    color: tokens.colorNeutralForeground2,
    paddingLeft: tokens.spacingHorizontalL,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
  },
  newTag: { color: tokens.colorNeutralForeground3, fontStyle: 'italic' },
  emptyGrid: { color: tokens.colorNeutralForeground3, padding: tokens.spacingVerticalM },
  subRow: {
    display: 'grid',
    gridTemplateColumns: '160px minmax(0, 1fr) 120px 110px 108px',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalM,
    paddingBlock: '2px',
    paddingInline: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  expanderRow: { justifySelf: 'start' },

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

const STATUS_META: Record<Connection['status'], { label: string; color: 'success' | 'brand' | 'danger' }> = {
  confirmed: { label: 'Filed', color: 'success' },
  suggested: { label: 'Suggested', color: 'brand' },
  ambiguous: { label: 'Ambiguous', color: 'danger' },
};

/** The Status-column badge — Primary wins over the raw status when a confirmed slot is primary. */
function StatusBadge({
  status,
  isPrimary,
  isConfirmed,
}: {
  status: Connection['status'];
  isPrimary: boolean;
  isConfirmed: boolean;
}): JSX.Element {
  if (isConfirmed && isPrimary) {
    return (
      <Badge appearance="tint" color="warning" icon={<Star16Filled />}>
        Primary
      </Badge>
    );
  }
  const meta = STATUS_META[isConfirmed ? 'confirmed' : status];
  return (
    <Badge appearance="tint" color={meta.color}>
      {meta.label}
    </Badge>
  );
}

/** An indented candidate sub-row (ambiguous alternative / other candidate) with a File action. */
function SubRow({
  s,
  name,
  recordNumber,
  reason,
  count,
  confidence,
  busy,
  onFile,
}: {
  s: ReturnType<typeof useStyles>;
  name: string;
  recordNumber?: string;
  reason?: string;
  /** Number of duplicate-named records this group collapses (>1 shows "· N records"). */
  count?: number;
  confidence: number;
  busy: boolean;
  onFile: () => void;
}): JSX.Element {
  return (
    <div className={s.subRow}>
      <span />
      <div className={s.subCell}>
        <Text size={200} weight="semibold">
          {name}
          {recordNumber ? <span className={s.recordNumber}> · {recordNumber}</span> : null}
          {count && count > 1 ? <span className={s.dupCount}> · {count} records</span> : null}
        </Text>
        {reason ? (
          <Text size={100} className={s.matchReason}>
            {reason}
          </Text>
        ) : null}
      </div>
      <Text size={200} className={confClass(s, confidence)}>
        {confText(confidence)}
      </Text>
      <span />
      <div className={s.actionsCell}>
        <Tooltip content="File this one" relationship="label">
          <Button size="small" appearance="primary" icon={<Checkmark16Regular />} disabled={busy} onClick={onFile} />
        </Tooltip>
      </div>
    </div>
  );
}

/** One connection = a grid row (+ any alternative / other-candidate sub-rows). Actions are icon-only. */
function ConnectionRow({
  conn,
  confirmed,
  isPrimary,
  readOnly,
  busy,
  resolveDisplayName,
  onConfirm,
  onChange,
  onSetPrimary,
}: {
  conn: Connection;
  confirmed: boolean;
  isPrimary: boolean;
  readOnly: boolean;
  busy: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  onConfirm: (conn: Connection, chosen?: ProvenanceCandidate) => void;
  onChange: (conn: Connection) => void;
  onSetPrimary: (conn: Connection) => void;
}): JSX.Element {
  const s = useStyles();
  const isConfirmed = confirmed || conn.status === 'confirmed';
  const targetName = resolveDisplayName?.(conn.entity, conn.targetId) ?? conn.targetName;
  const [showOthers, setShowOthers] = React.useState(false);
  const others = conn.otherCandidates ?? [];
  const hasOthers = conn.status !== 'ambiguous' && !readOnly && others.length > 0;
  // Group ambiguous alternatives by display name so duplicate-named records collapse to one expandable
  // row ("Name · N records") — the reviewer sees the DISTINCT choices, not one row per duplicate.
  const alternativeGroups =
    conn.status === 'ambiguous' && !readOnly ? groupCandidatesByName(conn.alternatives ?? []) : [];
  const nameOf = (alt: ProvenanceCandidate) =>
    resolveDisplayName?.(alt.targetEntity, alt.targetId) ?? alt.targetName ?? alt.targetId;

  return (
    <>
      <div className={s.gridRow}>
        {/* Type */}
        <div className={s.typeCell}>
          <span className={s.slotIcon}>{entityIcon(conn.entity)}</span>
          <Text size={300} className={s.slotLabel}>
            {conn.slotLabel}
          </Text>
        </div>
        {/* Record */}
        <div className={s.target}>
          {conn.status === 'ambiguous' ? (
            <Text className={s.ambigNote} size={300}>
              {alternativeGroups.length} possible {alternativeGroups.length === 1 ? 'match' : 'matches'} — choose one
            </Text>
          ) : (
            <>
              <Text className={s.targetName} size={300}>
                {targetName}
                {conn.recordNumber ? <span className={s.recordNumber}> · {conn.recordNumber}</span> : null}
              </Text>
              {conn.matchReason && conn.status === 'suggested' ? (
                <Text size={100} className={s.matchReason}>
                  {conn.matchReason}
                </Text>
              ) : null}
            </>
          )}
          {hasOthers && (
            <Button
              className={s.expanderRow}
              size="small"
              appearance="transparent"
              icon={showOthers ? <ChevronUp16Regular /> : <ChevronDown16Regular />}
              onClick={() => setShowOthers(v => !v)}
            >
              {showOthers
                ? 'Hide other candidates'
                : `${others.length} other candidate${others.length === 1 ? '' : 's'}`}
            </Button>
          )}
        </div>
        {/* Confidence */}
        {conn.status === 'ambiguous' ? (
          <Text size={200} className={s.confLow}>
            —
          </Text>
        ) : (
          <Text size={200} className={confClass(s, conn.confidence)}>
            {confText(conn.confidence)}
          </Text>
        )}
        {/* Status */}
        <StatusBadge status={conn.status} isPrimary={isPrimary} isConfirmed={isConfirmed} />
        {/* Actions (icon-only) */}
        <div className={s.actionsCell}>
          {readOnly ? null : isConfirmed ? (
            <>
              {!isPrimary && (
                <Tooltip content="Make primary (show in Regarding Record)" relationship="label">
                  <Button
                    size="small"
                    appearance="subtle"
                    icon={<Star16Regular />}
                    disabled={busy}
                    onClick={() => onSetPrimary(conn)}
                  />
                </Tooltip>
              )}
              <Tooltip content="Change / override" relationship="label">
                <Button
                  size="small"
                  appearance="subtle"
                  icon={<ArrowSwap16Regular />}
                  disabled={busy}
                  onClick={() => onChange(conn)}
                />
              </Tooltip>
            </>
          ) : conn.status === 'ambiguous' ? (
            <Tooltip content="Change / override" relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<ArrowSwap16Regular />}
                disabled={busy}
                onClick={() => onChange(conn)}
              />
            </Tooltip>
          ) : (
            <>
              <Tooltip content="Confirm" relationship="label">
                <Button
                  size="small"
                  appearance="primary"
                  icon={<Checkmark16Regular />}
                  disabled={busy}
                  onClick={() => onConfirm(conn)}
                />
              </Tooltip>
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
      {hasOthers &&
        showOthers &&
        others.map((alt, i) => (
          <SubRow
            key={`o${i}`}
            s={s}
            busy={busy}
            name={nameOf(alt)}
            recordNumber={candidateRecordNumber(alt)}
            reason={candidateMatchReason(alt)}
            confidence={alt.reinforcedConfidence}
            onFile={() => onConfirm(conn, alt)}
          />
        ))}
      {alternativeGroups.map((g, i) => (
        <SubRow
          key={`a${i}`}
          s={s}
          busy={busy}
          name={resolveDisplayName?.(g.candidates[0].targetEntity, g.candidates[0].targetId) ?? g.targetName}
          recordNumber={g.recordNumber}
          reason={g.matchReason}
          count={g.candidates.length}
          confidence={g.confidence}
          onFile={() => onConfirm(conn, g.candidates[0])}
        />
      ))}
    </>
  );
}

/** An AI-classifier suggested record TYPE with no matching record yet (e.g. "looks like a new Matter"). */
function AiSuggestionRow({
  suggestion,
  readOnly,
  busy,
  onCreateType,
  onLinkAnother,
}: {
  suggestion: AiSuggestedType;
  readOnly: boolean;
  busy: boolean;
  onCreateType: (entityType: string) => void;
  onLinkAnother: (entityType?: string) => void;
}): JSX.Element {
  const s = useStyles();
  return (
    <div className={s.gridRow}>
      <div className={s.typeCell}>
        <span className={s.slotIcon}>{entityIcon(suggestion.entityType)}</span>
        <Text size={300} className={s.slotLabel}>
          {suggestion.label}
        </Text>
      </div>
      <Tooltip content={suggestion.reason} relationship="label">
        <Text size={300} className={s.newTag}>
          Looks like a new {suggestion.label} (AI)
        </Text>
      </Tooltip>
      <Badge appearance="tint" color="brand" icon={<Sparkle16Regular />}>
        AI
      </Badge>
      <Badge appearance="outline" color="informative">
        Suggested
      </Badge>
      <div className={s.actionsCell}>
        {!readOnly && (
          <>
            <Tooltip content={`Create a new ${suggestion.label}`} relationship="label">
              <Button
                size="small"
                appearance="primary"
                icon={<Add16Regular />}
                disabled={busy}
                onClick={() => onCreateType(suggestion.entityType)}
              />
            </Tooltip>
            <Tooltip content={`Link an existing ${suggestion.label}`} relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Link16Regular />}
                disabled={busy}
                onClick={() => onLinkAnother(suggestion.entityType)}
              />
            </Tooltip>
          </>
        )}
      </div>
    </div>
  );
}

/** The shared body: rollup + slots + link-another. */
function EditorBody({
  record,
  provenance,
  readOnly,
  busy,
  confirmedFields,
  primaryField,
  resolveDisplayName,
  filedAssociations,
  onConfirm,
  onAcceptAll,
  onChange,
  onSetPrimary,
  onLinkAnother,
  onCreateType,
}: {
  record: ICommunicationRecord;
  provenance: ProvenanceDoc;
  readOnly: boolean;
  busy: boolean;
  confirmedFields: Set<string>;
  primaryField?: string;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  filedAssociations?: FiledAssociation[];
  onConfirm: (conn: Connection, chosen?: ProvenanceCandidate) => void;
  onAcceptAll: (conns: Connection[]) => void;
  onChange: (conn: Connection) => void;
  onSetPrimary: (conn: Connection) => void;
  onLinkAnother: (entityType?: string) => void;
  onCreateType: (entityType: string) => void;
}): JSX.Element {
  const s = useStyles();
  const isResolved = record.sprk_associationstatus === AssociationStatus.Resolved;
  // Merge the engine's suggested slots with what's actually filed on the record
  // (authoritative — includes manual "Link another" associations).
  const connections = React.useMemo(
    () => mergeFiledConnections(deriveConnections(provenance, isResolved), filedAssociations ?? []),
    [provenance, isResolved, filedAssociations]
  );
  // AI-classifier suggested types with no matching slot (e.g. a brand-new matter).
  const aiSuggestions = React.useMemo(
    () => deriveAiSuggestedTypes(provenance, new Set(connections.map(c => c.entity))),
    [provenance, connections]
  );

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

  const connReview = connections.filter(c => c.status !== 'confirmed' && !confirmedFields.has(c.field)).length;
  const confirmedCount = connections.length - connReview;
  // Only 'suggested' slots are safely bulk-confirmable — an 'ambiguous' slot needs the reviewer to pick.
  const acceptableCount = connections.filter(
    c => c.status === 'suggested' && !confirmedFields.has(c.field)
  ).length;
  // AI-suggested types (e.g. "Create Matter") are also review items, so they count toward "to review".
  const toReview = connReview + aiSuggestions.length;

  const hasRows = connections.length > 0 || aiSuggestions.length > 0;

  return (
    <div className={s.gridWrap}>
      <div className={s.headRow}>
        <Text size={200} weight="semibold" className={s.kicker}>
          Connections
        </Text>
        <Text size={200} className={s.rollup}>
          · {confirmedCount} filed · {toReview} to review
        </Text>
        <div className={s.grow} />
        {!readOnly && acceptableCount > 0 && (
          <Tooltip
            content={`Files the ${acceptableCount} clearly-suggested ${
              acceptableCount === 1 ? 'connection' : 'connections'
            } at once. Ambiguous matches (where you must choose) are left for you to review.`}
            relationship="label"
          >
            <Button
              size="small"
              appearance="primary"
              icon={<CheckmarkCircle20Filled />}
              disabled={busy}
              onClick={handleAcceptAll}
            >
              Confirm {acceptableCount} suggestion{acceptableCount === 1 ? '' : 's'}
            </Button>
          </Tooltip>
        )}
      </div>

      <div className={s.gridScroll}>
        <div className={s.gridHeader}>
          <Text className={s.colHead}>Type</Text>
          <Text className={s.colHead}>Record</Text>
          <Text className={s.colHead}>Confidence</Text>
          <Text className={s.colHead}>Status</Text>
          <Text className={s.colHead} style={{ textAlign: 'right' }}>
            Actions
          </Text>
        </div>

        {connections.map(c => (
          <ConnectionRow
            key={c.field}
            conn={c}
            confirmed={confirmedFields.has(c.field)}
            isPrimary={c.field === effectivePrimary}
            readOnly={readOnly}
            busy={busy}
            resolveDisplayName={resolveDisplayName}
            onConfirm={onConfirm}
            onChange={onChange}
            onSetPrimary={onSetPrimary}
          />
        ))}

        {aiSuggestions.map(sug => (
          <AiSuggestionRow
            key={sug.entityType}
            suggestion={sug}
            readOnly={readOnly}
            busy={busy}
            onCreateType={onCreateType}
            onLinkAnother={onLinkAnother}
          />
        ))}

        {!hasRows && <Text className={s.emptyGrid}>No connections yet — use “Link another record” to add one.</Text>}
      </div>

      {!readOnly && (
        <div className={s.addRow}>
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button size="small" appearance="subtle" icon={<Link20Regular />} disabled={busy}>
                Link another record…
              </Button>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {LINK_ANOTHER_TYPES.map(t => (
                  <MenuItem
                    key={t.entityType}
                    icon={entityIcon(t.entityType)}
                    onClick={() => onLinkAnother(t.entityType)}
                  >
                    {t.label}
                  </MenuItem>
                ))}
              </MenuList>
            </MenuPopover>
          </Menu>
        </div>
      )}
    </div>
  );
}

// ── hosts ─────────────────────────────────────────────────────────────────────

export function ConnectionsEditor(props: ConnectionsEditorProps): JSX.Element | null {
  const { record, provenance, layout, readOnly = false, busy = false } = props;
  const s = useStyles();
  const [open, setOpen] = React.useState(false);

  const filedAssociations = props.filedAssociations ?? [];
  const bodyProps = {
    readOnly,
    busy,
    confirmedFields: props.confirmedFields ?? new Set<string>(),
    primaryField: props.primaryField,
    resolveDisplayName: props.resolveDisplayName,
    filedAssociations,
    onConfirm: props.onConfirm ?? (() => undefined),
    onAcceptAll: props.onAcceptAll ?? (() => undefined),
    onChange: props.onChange ?? (() => undefined),
    onSetPrimary: props.onSetPrimary ?? (() => undefined),
    onLinkAnother: props.onLinkAnother ?? (() => undefined),
    onCreateType: props.onCreateType ?? (() => undefined),
  };

  // Nothing to show: no engine provenance AND nothing filed → only a bare
  // "filed to X" line when Resolved, else render nothing.
  if (!provenance && filedAssociations.length === 0) {
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

  // Provenance may be absent (all associations filed manually); synthesize an empty
  // decision doc so EditorBody can still render the filed rows.
  const doc: ProvenanceDoc = provenance ?? EMPTY_PROVENANCE;
  const isResolved = record.sprk_associationstatus === AssociationStatus.Resolved;
  const connections = mergeFiledConnections(deriveConnections(doc, isResolved), filedAssociations);
  const toReview = connections.filter(c => c.status !== 'confirmed').length;

  if (layout === 'rail') {
    return (
      <aside className={s.rail}>
        <EditorBody record={record} provenance={doc} {...bodyProps} />
      </aside>
    );
  }

  if (layout === 'card') {
    return (
      <div className={s.card}>
        <EditorBody record={record} provenance={doc} {...bodyProps} />
      </div>
    );
  }

  // summary: one-line bar that expands.
  const top = topCandidate(doc);
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
          <EditorBody record={record} provenance={doc} {...bodyProps} />
        </div>
      )}
    </>
  );
}
