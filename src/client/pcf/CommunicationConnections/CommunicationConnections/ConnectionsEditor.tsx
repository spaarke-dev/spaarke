/**
 * Connections editor (W4 / FR-17; rebuilt task 105 / UAT R2 C1) — the
 * multi-association review surface, GROUPED BY ACTION NEEDED.
 *
 * An email is regarding MANY records at once (Matter + Organization + Contact +
 * Invoice …, per ADR-024's regarding family and task-015's multi-field candidates).
 * The original surface rendered every match in one flat grid with opaque
 * "Ambiguous / Primary / Filed / Suggested" chips, and UAT round 2 (C1) found it
 * confusing — reviewers could not tell which rows needed action or what a status
 * meant. This rebuild follows the owner-approved mockup (3c1eaf58): matches are
 * grouped into three action sections with plain-language verbs + a status legend,
 * and each row states exactly what to do:
 *   1. "Needs your decision" — ambiguous conflicts as selectable options + a
 *      "Set as primary" action (files the chosen target).
 *   2. "Filed automatically" — confirmed auto-links, marked done, with the ★
 *      primary shown + change / unlink.
 *   3. "Suggested" — soft matches (AI create-suggestions, name matches, contact
 *      suggestions) each with explicit Confirm ✓ / Dismiss ✕.
 *
 * DATA CONTRACT UNCHANGED — only the presentation/interaction layer is reworked.
 * Grouping is driven off the SAME provenance candidates + statuses via the pure
 * `groupConnectionsByAction`; confirm/set-primary still delegate to the App's
 * existing additive task-042 write path (`applyRegardingSelection`, never
 * clear-and-set). Dismiss is a client-side in-session hide (the match was never
 * filed, so hiding it needs no write). Unlink removes exactly one filed lookup.
 *
 * React 16 JSX element types (`JSX.Element`) per ADR-022; Fluent v9 tokens only,
 * theme-correct light + dark (ADR-021). Only the `rail` layout is used inside the
 * PCF (the OOB form's review modal); the `card` / `summary` hosts are retained for
 * prototype parity and route through the same grouped body.
 */

import * as React from 'react';
import {
  makeStyles,
  mergeClasses,
  tokens,
  Text,
  Button,
  Badge,
  Tooltip,
  Radio,
  RadioGroup,
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
  Link20Regular,
  ChevronDown16Regular,
  ChevronUp16Regular,
  Star16Filled,
  Star16Regular,
  Dismiss16Regular,
} from '@fluentui/react-icons';
import { AssociationStatus, type ICommunicationRecord } from './types';
import {
  type ProvenanceDoc,
  type ProvenanceCandidate,
  type Connection,
  type FiledAssociation,
  type AiSuggestedType,
  type CandidateGroup,
  confidenceBand,
  deriveConnections,
  deriveAiSuggestedTypes,
  mergeFiledConnections,
  topCandidate,
  groupCandidatesByName,
  groupConnectionsByAction,
  isConnectionConfirmed,
  entityLabel,
} from './provenance';

export type ReviewLayout = 'summary' | 'card' | 'rail';

/** Write/launch callbacks the PCF App supplies to turn the review stubs into real actions. */
export interface ConnectionsCallbacks {
  /** Confirm a slot's suggestion (or, for ambiguous, the chosen alternative). */
  onConfirm?: (conn: Connection, chosen?: ProvenanceCandidate) => void;
  /** Confirm every outstanding suggested slot at once (retained; not surfaced in the rebuilt UI). */
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
   * Remove ONE filed association (null exactly that entity's typed regarding
   * lookup — additive-safe, never a bulk clear). Siblings are untouched.
   */
  onUnlink?: (conn: Connection) => void;
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
  rail: {
    padding: tokens.spacingVerticalL,
    paddingInline: tokens.spacingHorizontalL,
    display: 'flex',
    flexDirection: 'column',
    minHeight: 0,
    height: '100%',
  },
  card: {
    backgroundColor: tokens.colorNeutralBackground1,
    borderRadius: tokens.borderRadiusLarge,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    boxShadow: tokens.shadow4,
    padding: tokens.spacingVerticalM,
    paddingInline: tokens.spacingHorizontalL,
  },

  // ── overall body: intro → scrolling sections → legend ──
  body: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM, height: '100%', minHeight: 0 },
  intro: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  subtitle: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase300 },
  ctxCounts: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  scroll: { flex: 1, overflowY: 'auto', minHeight: 0, display: 'flex', flexDirection: 'column' },

  // ── section ──
  section: {
    paddingBlock: tokens.spacingVerticalM,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  sectionLast: { borderBottom: 'none' },
  secHead: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, marginBottom: tokens.spacingVerticalS },
  dot: { width: '8px', height: '8px', borderRadius: '50%', flexShrink: 0 },
  dotDecide: { backgroundColor: tokens.colorPaletteMarigoldForeground1 },
  dotFiled: { backgroundColor: tokens.colorPaletteGreenForeground1 },
  dotSuggest: { backgroundColor: tokens.colorBrandForeground1 },
  secTitle: { fontWeight: tokens.fontWeightSemibold, fontSize: tokens.fontSizeBase300, color: tokens.colorNeutralForeground1 },
  secCount: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  secHint: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    marginBottom: tokens.spacingVerticalM,
    marginInlineStart: tokens.spacingHorizontalL,
  },

  // ── needs-decision: selectable option cards ──
  options: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  opt: {
    display: 'grid',
    gridTemplateColumns: 'auto 1fr auto',
    gap: tokens.spacingHorizontalM,
    alignItems: 'center',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalM,
    border: `1px solid ${tokens.colorPaletteMarigoldBorder2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorPaletteMarigoldBackground1,
    cursor: 'pointer',
  },
  optSel: {
    border: `1px solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
    boxShadow: `inset 0 0 0 1px ${tokens.colorBrandStroke1}`,
  },
  optRec: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  optName: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
  optWhy: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase200 },
  optConf: { display: 'flex', flexDirection: 'column', alignItems: 'flex-end', textAlign: 'right' },
  confLbl: {
    fontSize: tokens.fontSizeBase100,
    textTransform: 'uppercase',
    letterSpacing: '0.04em',
    color: tokens.colorNeutralForeground3,
  },
  decideAction: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    marginTop: tokens.spacingVerticalM,
    flexWrap: 'wrap',
  },
  decideHelper: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  decideDivider: { height: tokens.spacingVerticalL },

  // ── filed / suggested rows ──
  row: {
    display: 'grid',
    gridTemplateColumns: '20px 1fr auto auto',
    gap: tokens.spacingHorizontalM,
    alignItems: 'center',
    paddingBlock: tokens.spacingVerticalS,
    paddingInline: tokens.spacingHorizontalXS,
    borderRadius: tokens.borderRadiusMedium,
    ':hover': { backgroundColor: tokens.colorNeutralBackground2 },
  },
  rowBorder: { borderTop: `1px solid ${tokens.colorNeutralStroke3}` },
  ico: { color: tokens.colorNeutralForeground3, display: 'flex', flexShrink: 0 },
  star: { color: tokens.colorPaletteMarigoldForeground1 },
  rec: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  recName: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexWrap: 'wrap',
  },
  recNum: { color: tokens.colorNeutralForeground2, fontVariantNumeric: 'tabular-nums', fontWeight: tokens.fontWeightRegular },
  typeTag: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase100, fontWeight: tokens.fontWeightRegular },
  recWhy: { color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase200 },
  rowActs: { display: 'flex', gap: tokens.spacingHorizontalXS, justifyContent: 'flex-end' },

  confHigh: { color: tokens.colorPaletteGreenForeground1, whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },
  confMedium: { color: tokens.colorPaletteMarigoldForeground1, whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },
  confLow: { color: tokens.colorNeutralForeground3, whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },

  linkRow: { paddingTop: tokens.spacingVerticalM },

  // ── legend footer ──
  legend: {
    display: 'flex',
    flexWrap: 'wrap',
    rowGap: tokens.spacingVerticalXS,
    columnGap: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalM,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  legendItem: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, color: tokens.colorNeutralForeground2, fontSize: tokens.fontSizeBase200 },
  legendSw: { width: '9px', height: '9px', borderRadius: '50%', flexShrink: 0 },

  empty: { color: tokens.colorNeutralForeground3, padding: tokens.spacingVerticalM },
  confirmedTick: { color: tokens.colorPaletteGreenForeground1, display: 'flex' },
  headRow: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },

  // ── summary host (one-line bar) ──
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
  grow: { flex: 1 },
});

function confClass(s: ReturnType<typeof useStyles>, v: number): string {
  const b = confidenceBand(v);
  return b === 'high' ? s.confHigh : b === 'medium' ? s.confMedium : s.confLow;
}
function pct(v: number): number {
  return Math.round(v * 100);
}
function confText(v: number): string {
  return `${pct(v)}% · ${BAND_WORD[confidenceBand(v)]}`;
}

// ── Needs-your-decision: one ambiguous conflict rendered as selectable options ──
function DecisionBlock({
  conn,
  busy,
  readOnly,
  resolveDisplayName,
  onConfirm,
}: {
  conn: Connection;
  busy: boolean;
  readOnly: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  onConfirm: (conn: Connection, chosen?: ProvenanceCandidate) => void;
}): JSX.Element {
  const s = useStyles();
  // Collapse duplicate-named alternatives so the reviewer sees the DISTINCT choices.
  const groups: CandidateGroup[] = React.useMemo(
    () => groupCandidatesByName(conn.alternatives ?? []),
    [conn.alternatives]
  );
  const [selected, setSelected] = React.useState<string | null>(null);
  const slot = conn.slotLabel.toLowerCase();

  const nameOf = (g: CandidateGroup) =>
    resolveDisplayName?.(g.candidates[0].targetEntity, g.candidates[0].targetId) ?? g.targetName;

  return (
    <div>
      <Text className={s.secHint}>
        {groups.length} strong matches matched this email. Pick the one it is really about — it becomes the primary{' '}
        {slot}. The other stays available under &ldquo;Link another record.&rdquo;
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
                {g.matchReason ? <Text className={s.optWhy}>{g.matchReason}</Text> : null}
              </div>
              <div className={s.optConf}>
                <Text className={confClass(s, g.confidence)} weight="semibold">
                  {pct(g.confidence)}%
                </Text>
                <Text className={s.confLbl}>{BAND_WORD[confidenceBand(g.confidence)]}</Text>
              </div>
            </div>
          ))}
        </div>
      </RadioGroup>
      {!readOnly && (
        <div className={s.decideAction}>
          <Button
            appearance="primary"
            disabled={selected === null || busy}
            onClick={() => {
              if (selected === null) return;
              onConfirm(conn, groups[Number(selected)].candidates[0]);
            }}
          >
            Set as primary {slot}
          </Button>
          <Text className={s.decideHelper}>
            {selected === null
              ? `Select a ${slot} above to continue.`
              : `Will file "${nameOf(groups[Number(selected)])}" as the primary ${slot}.`}
          </Text>
        </div>
      )}
    </div>
  );
}

// ── Filed-automatically: a done row with ★ primary + change / unlink ──
function FiledRow({
  conn,
  first,
  isPrimary,
  busy,
  readOnly,
  resolveDisplayName,
  onChange,
  onSetPrimary,
  onUnlink,
}: {
  conn: Connection;
  first: boolean;
  isPrimary: boolean;
  busy: boolean;
  readOnly: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  onChange: (conn: Connection) => void;
  onSetPrimary: (conn: Connection) => void;
  onUnlink: (conn: Connection) => void;
}): JSX.Element {
  const s = useStyles();
  const name = resolveDisplayName?.(conn.entity, conn.targetId) ?? conn.targetName;
  const why = conn.matchReason
    ? conn.confidence
      ? `${conn.matchReason} · ${confText(conn.confidence)}`
      : conn.matchReason
    : undefined;

  return (
    <div className={mergeClasses(s.row, !first && s.rowBorder)}>
      <span className={mergeClasses(s.ico, isPrimary && s.star)}>
        {isPrimary ? <Star16Filled /> : entityIcon(conn.entity)}
      </span>
      <div className={s.rec}>
        <Text className={s.recName}>
          {name}
          {conn.recordNumber ? <span className={s.recNum}>· {conn.recordNumber}</span> : null}
          <span className={s.typeTag}>{entityLabel(conn.entity)}</span>
        </Text>
        {why ? <Text className={s.recWhy}>{why}</Text> : null}
      </div>
      {isPrimary ? (
        <Badge appearance="tint" color="warning" icon={<Star16Filled />}>
          Primary
        </Badge>
      ) : (
        <Badge appearance="tint" color="success">
          Linked
        </Badge>
      )}
      <div className={s.rowActs}>
        {readOnly ? null : isPrimary ? (
          <Tooltip content="Change primary" relationship="label">
            <Button
              size="small"
              appearance="subtle"
              icon={<ArrowSwap16Regular />}
              aria-label="Change primary"
              disabled={busy}
              onClick={() => onChange(conn)}
            />
          </Tooltip>
        ) : (
          <>
            <Tooltip content="Make primary" relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Star16Regular />}
                aria-label="Make primary"
                disabled={busy}
                onClick={() => onSetPrimary(conn)}
              />
            </Tooltip>
            <Tooltip content="Unlink" relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Dismiss16Regular />}
                aria-label="Unlink"
                disabled={busy}
                onClick={() => onUnlink(conn)}
              />
            </Tooltip>
          </>
        )}
      </div>
    </div>
  );
}

// ── Suggested: a soft match to Confirm or Dismiss ──
function SuggestedRow({
  conn,
  first,
  busy,
  readOnly,
  resolveDisplayName,
  onConfirm,
  onDismiss,
}: {
  conn: Connection;
  first: boolean;
  busy: boolean;
  readOnly: boolean;
  resolveDisplayName?: (entity: string, id: string) => string | undefined;
  onConfirm: (conn: Connection) => void;
  onDismiss: (key: string) => void;
}): JSX.Element {
  const s = useStyles();
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
        {conn.matchReason ? <Text className={s.recWhy}>{conn.matchReason}</Text> : null}
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

// ── Suggested: an AI "looks like a new X" — Create or Dismiss ──
function AiSuggestionRow({
  suggestion,
  first,
  busy,
  readOnly,
  onCreateType,
  onDismiss,
}: {
  suggestion: AiSuggestedType;
  first: boolean;
  busy: boolean;
  readOnly: boolean;
  onCreateType: (entityType: string) => void;
  onDismiss: (key: string) => void;
}): JSX.Element {
  const s = useStyles();
  return (
    <div className={mergeClasses(s.row, !first && s.rowBorder)}>
      <span className={s.ico}>{entityIcon(suggestion.entityType)}</span>
      <div className={s.rec}>
        <Text className={s.recName}>
          Looks like a new {suggestion.label}
          <Badge appearance="tint" color="brand" icon={<Sparkle16Regular />}>
            AI
          </Badge>
        </Text>
        <Text className={s.recWhy}>{suggestion.reason}</Text>
      </div>
      <span />
      <div className={s.rowActs}>
        {!readOnly && (
          <>
            <Tooltip content={`Create and link a ${suggestion.label}`} relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Add16Regular />}
                aria-label="Create and link"
                disabled={busy}
                onClick={() => onCreateType(suggestion.entityType)}
              />
            </Tooltip>
            <Tooltip content="Dismiss" relationship="label">
              <Button
                size="small"
                appearance="subtle"
                icon={<Dismiss16Regular />}
                aria-label="Dismiss"
                disabled={busy}
                onClick={() => onDismiss(`ai:${suggestion.entityType}`)}
              />
            </Tooltip>
          </>
        )}
      </div>
    </div>
  );
}

/** The shared grouped body: intro → three action sections → legend. */
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
  onChange,
  onSetPrimary,
  onUnlink,
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
  onChange: (conn: Connection) => void;
  onSetPrimary: (conn: Connection) => void;
  onUnlink: (conn: Connection) => void;
  onLinkAnother: (entityType?: string) => void;
  onCreateType: (entityType: string) => void;
}): JSX.Element {
  const s = useStyles();
  const isResolved = record.sprk_associationstatus === AssociationStatus.Resolved;

  // Merge the engine's suggested slots with what's actually filed on the record
  // (authoritative — includes manual "Link another" associations). SAME data path.
  const connections = React.useMemo(
    () => mergeFiledConnections(deriveConnections(provenance, isResolved), filedAssociations ?? []),
    [provenance, isResolved, filedAssociations]
  );
  const aiSuggestions = React.useMemo(
    () => deriveAiSuggestedTypes(provenance, new Set(connections.map(c => c.entity))),
    [provenance, connections]
  );

  // Client-side in-session dismissals (never-filed matches → hide needs no write).
  const [dismissed, setDismissed] = React.useState<ReadonlySet<string>>(new Set());
  const onDismiss = React.useCallback(
    (key: string) => setDismissed(prev => new Set(prev).add(key)),
    []
  );

  const grouped = groupConnectionsByAction(connections, aiSuggestions, confirmedFields, dismissed);
  const { needsDecision, filed, suggested, aiSuggested } = grouped;

  // Effective primary: the explicitly-designated field if it's confirmed, else the
  // first filed slot in priority order (connections are SLOT_META-sorted).
  const effectivePrimary =
    primaryField && filed.some(c => c.field === primaryField)
      ? primaryField
      : filed[0]?.field;

  const suggestedCount = suggested.length + aiSuggested.length;
  const hasRows = needsDecision.length > 0 || filed.length > 0 || suggestedCount > 0;
  // The Suggested section renders whenever there's something to suggest OR the
  // reviewer can add a record via "Link another" (so the affordance is reachable).
  const showSuggestedSection = suggestedCount > 0 || !readOnly;

  return (
    <div className={s.body}>
      <div className={s.intro}>
        <Text className={s.subtitle}>
          What this email is linked to.
          {needsDecision.length > 0
            ? ' Resolve the conflict below, then confirm any suggestions.'
            : ' Confirm any suggestions below.'}
        </Text>
        <Text className={s.ctxCounts}>
          {needsDecision.length} needs your decision · {filed.length} filed · {suggestedCount} suggested
        </Text>
      </div>

      <div className={s.scroll}>
        {needsDecision.length > 0 && (
          <section className={s.section}>
            <div className={s.secHead}>
              <span className={mergeClasses(s.dot, s.dotDecide)} />
              <Text className={s.secTitle}>Needs your decision</Text>
              <Text className={s.secCount}>
                · {needsDecision.length} conflict{needsDecision.length === 1 ? '' : 's'}
              </Text>
            </div>
            {needsDecision.map((conn, i) => (
              <React.Fragment key={conn.field}>
                {i > 0 && <div className={s.decideDivider} />}
                <DecisionBlock
                  conn={conn}
                  busy={busy}
                  readOnly={readOnly}
                  resolveDisplayName={resolveDisplayName}
                  onConfirm={onConfirm}
                />
              </React.Fragment>
            ))}
          </section>
        )}

        {filed.length > 0 && (
          <section className={s.section}>
            <div className={s.secHead}>
              <span className={mergeClasses(s.dot, s.dotFiled)} />
              <Text className={s.secTitle}>Filed automatically</Text>
              <Text className={s.secCount}>· {filed.length} linked</Text>
            </div>
            <Text className={s.secHint}>
              High-confidence matches Spaarke linked for you. No action needed — change the star or unlink if something
              looks off.
            </Text>
            {filed.map((conn, i) => (
              <FiledRow
                key={conn.field}
                conn={conn}
                first={i === 0}
                isPrimary={conn.field === effectivePrimary}
                busy={busy}
                readOnly={readOnly}
                resolveDisplayName={resolveDisplayName}
                onChange={onChange}
                onSetPrimary={onSetPrimary}
                onUnlink={onUnlink}
              />
            ))}
          </section>
        )}

        {showSuggestedSection && (
          <section className={mergeClasses(s.section, s.sectionLast)}>
            <div className={s.secHead}>
              <span className={mergeClasses(s.dot, s.dotSuggest)} />
              <Text className={s.secTitle}>Suggested</Text>
              <Text className={s.secCount}>· {suggestedCount} to confirm</Text>
            </div>
            <Text className={s.secHint}>
              Possible matches Spaarke found but will not link on its own. Confirm the ones that belong, dismiss the
              rest.
            </Text>
            {suggested.map((conn, i) => (
              <SuggestedRow
                key={conn.field}
                conn={conn}
                first={i === 0}
                busy={busy}
                readOnly={readOnly}
                resolveDisplayName={resolveDisplayName}
                onConfirm={onConfirm}
                onDismiss={onDismiss}
              />
            ))}
            {aiSuggested.map((sug, i) => (
              <AiSuggestionRow
                key={sug.entityType}
                suggestion={sug}
                first={suggested.length === 0 && i === 0}
                busy={busy}
                readOnly={readOnly}
                onCreateType={onCreateType}
                onDismiss={onDismiss}
              />
            ))}
            {suggestedCount === 0 && (
              <Text className={s.recWhy}>No suggestions right now.</Text>
            )}
            {!readOnly && (
              <div className={s.linkRow}>
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
          </section>
        )}

        {!hasRows && readOnly && <Text className={s.empty}>No connections yet.</Text>}
      </div>

      <div className={s.legend}>
        <span className={s.legendItem}>
          <span className={mergeClasses(s.legendSw, s.dotDecide)} /> Needs a decision
        </span>
        <span className={s.legendItem}>
          <span className={mergeClasses(s.legendSw, s.dotFiled)} /> Filed automatically
        </span>
        <span className={s.legendItem}>
          <span className={mergeClasses(s.legendSw, s.dotSuggest)} /> Suggested — confirm or dismiss
        </span>
      </div>
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
    onChange: props.onChange ?? (() => undefined),
    onSetPrimary: props.onSetPrimary ?? (() => undefined),
    onUnlink: props.onUnlink ?? (() => undefined),
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
  const isResolved = record.sprk_associationstatus === AssociationStatus.Resolved;
  const connections = mergeFiledConnections(deriveConnections(doc, isResolved), filedAssociations);
  const toReview = connections.filter(c => !isConnectionConfirmed(c, bodyProps.confirmedFields)).length;
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
