import React, { useEffect, useMemo, useRef } from 'react';
import {
  Badge,
  Button,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Spinner,
  Text,
  makeStyles,
  tokens,
  type GriffelStyle,
} from '@fluentui/react-components';
import { useLazyResults } from '../hooks/useLazyResults';
import type { AnnounceMode } from '../hooks/useAnnounce';
import type { RecordSeedSource, RecordMatch, UseFindRecordMatchesResult } from '../hooks/useFindRecordMatches';
import { cleanGuid } from '@spaarke/ui-components/guid';
import { FindSplitPane } from './FindSplitPane';

/**
 * FindResultsList — renders the Find tab's results (spaarkeai-word-add-in-r1 task 034, extended task 077,
 * restructured task 092 / UAT-3+UAT-8, 2026-10-03 round-3 UAT).
 *
 * **Task 092 — two INDEPENDENT sections, not one combined list.** The owner's round-3 UAT (UAT-8) asked
 * for "Most similar documents" and "Matching records" to be two separate sections, and (UAT-3) for
 * Matching records to show something even while documents are still loading or have failed — the OLD
 * behaviour nested records inside the documents half and only rendered either once the documents request
 * had resolved. `documents` (this component's own loading/error/loaded state, mapped 1:1 from `FindView`'s
 * `RelatedDocumentsState`) and `records` (`useFindRecordMatches`'s result) are now rendered UNCONDITIONALLY
 * side by side, each owning its own heading, loading spinner, empty copy and error MessageBar — neither
 * section's render path reads the other's state.
 *
 * **Task 102 (UAT round 6 item 4) — SUPERSEDES task 092's "one scroll container".** The two sections are
 * now separate panes ("Similar Documents", "Matching records"), each a fixed heading + its OWN scroll
 * container (`find-documents-scroll`, `find-records-scroll`), sharing the Find tab's height through a
 * draggable/keyboard-operable divider (`FindSplitPane`). The pre-092 fixed-height cap stays gone — heights
 * come from the split ratio, never a pixel cap. Each progressive-load sentinel (documents reveal, records
 * paging) sits at the end of its own scroller, so each is triggered by its OWN list's scroll.
 *
 * **Path 2 (owner-approved 2026-09-15, Find list only)** — the documented exception to ADR-051's literal
 * "fetch progressively" MUST, for the DOCUMENTS half only. `GET /api/ai/visualization/related/{documentId}`
 * has no paging mechanism of any kind: it returns ONE complete, authorization-trimmed, bounded response (at
 * most 50 rows) per call. See `projects/spaarkeai-word-add-in-r1/notes/034-route-paging-escalation.md`.
 *
 * **Hub nodes** (`matter` / `project` / `invoice` / `email`) are the SOURCE document's own parent
 * record(s) — never a content-similarity match. They render inside the Documents section, in their own,
 * distinctly labeled sub-section.
 *
 * **Records (task 077)** come from `POST /api/ai/search/records` (per-row authorized), seeded by the
 * document's own AI-profile keywords. They are a TEXT match on the document's subject, not content
 * similarity, and the heading/caption say so.
 *
 * **Opening a result (task 092).** `onOpenRecord`, when provided, is called with `(entityType, recordId)`
 * for a document row (`sprk_document`), a hub/parent row (`sprk_matter` / `sprk_project` / `sprk_invoice` /
 * `sprk_document` for an email hub — see `extractHubRecordId`'s doc comment), or a matching-record row
 * (`record.recordType` / `record.recordId`). `FindView` is the only caller and wires this to
 * `openRecord` from `services/openRecordLauncher.ts`, gated on `canOpenBrowserWindow` + `ORG_URL` (NFR-10).
 * Omitting the prop (no capability / no config) renders every row as plain, non-interactive text — never a
 * dead link.
 *
 * **No pager, ever** (ADR-051): no numbered pages, no prev/next, no chevron, no "Load more" button. The
 * only navigation is scroll.
 */

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Minimal client-side mirror of the BFF's DocumentGraphResponse node shape
// (Sprk.Bff.Api.Services.Ai.Visualization.DocumentNode / DocumentNodeData / NodeTypes) — only the
// fields this list renders. Not a full port of the visualization contract.
// ─────────────────────────────────────────────────────────────────────────────────────────────

export interface FindResultNodeData {
  label: string;
  documentType?: string | null;
  similarity?: number | null;
  parentEntityName?: string | null;
}

export interface FindResultNode {
  id: string;
  type: string;
  data: FindResultNodeData;
}

/**
 * Task 092 — the Documents section's own state, mapped 1:1 from `FindView`'s `RelatedDocumentsState`
 * (that type keeps its own `idle` transient; this component only ever needs these three, so `FindView`
 * folds `idle` into `loading` at the call site).
 */
export type DocumentsResultState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'loaded'; nodes: FindResultNode[]; partialResultsWarning?: string | null };

/** Mirrors the server's `NodeTypes` hub-type set (`IsParentHub`) — matter/project/invoice/email. */
const HUB_NODE_TYPES = new Set(['matter', 'project', 'invoice', 'email']);
const SOURCE_NODE_TYPE = 'source';

const HUB_LABELS: Record<string, string> = {
  matter: 'Matter',
  project: 'Project',
  invoice: 'Invoice',
  email: 'Email',
};

/**
 * The Dataverse entity a hub node's OWN record opens as. An "email" hub is the parent EMAIL DOCUMENT
 * (a `sprk_document`), not a distinct "email" entity — `VisualizationService.CreateParentHubNode`'s
 * `SameEmail` branch builds its `RecordUrl` via the exact same `BuildRecordUrl(documentId)` plain
 * document rows use (`etn=sprk_document`), confirmed by reading that method. See
 * {@link extractHubRecordId} for why a `thread-` id is deliberately excluded.
 */
const HUB_ENTITY_TYPES: Record<string, string> = {
  matter: 'sprk_matter',
  project: 'sprk_project',
  invoice: 'sprk_invoice',
  email: 'sprk_document',
};

const GUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** A stable empty-array identity — see `DocumentsSection`'s `nodes` memo for why this matters. */
const EMPTY_NODES: FindResultNode[] = [];

/**
 * The real Dataverse id behind a hub node, or `null` when there is none to open.
 *
 * `node.id` is server-prefixed, never the bare record id — confirmed by reading
 * `VisualizationService.CreateParentHubNode`: `matter-{sourceDoc.MatterId}`, `project-{…}`,
 * `invoice-{…}`, `email-{sourceDoc.ParentDocumentId}` (or `email-{sourceDoc.Id}`) are real GUIDs behind
 * a prefix, but the `SameThread` branch's `thread-{conversationIndexPrefix}` carries NO record id at
 * all — a truncated conversation index, not a GUID — and is typed `email` exactly like the openable
 * case. This strips the prefix matching the node's own type and verifies what remains is actually
 * GUID-shaped before offering an open action, so a `thread-` hub degrades to plain text rather than
 * building a broken deep link — the same "never open a link that can't resolve" discipline
 * `openRecord` itself applies for a missing/empty id.
 */
export function extractHubRecordId(node: FindResultNode): { entityType: string; recordId: string } | null {
  const entityType = HUB_ENTITY_TYPES[node.type];
  if (!entityType) return null;
  const prefix = `${node.type}-`;
  if (!node.id.startsWith(prefix)) return null;
  const candidate = cleanGuid(node.id.slice(prefix.length));
  return GUID_RE.test(candidate) ? { entityType, recordId: candidate } : null;
}

function isHubNode(node: FindResultNode): boolean {
  return HUB_NODE_TYPES.has(node.type);
}

/** Mirrors the server's `IsResultRow`: a genuine similarity match — not the source, not a hub. */
function isResultRow(node: FindResultNode): boolean {
  return node.type !== SOURCE_NODE_TYPE && !isHubNode(node);
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Task 092 — safe match-reason rendering (security constraint). `<em>…</em>` (Azure AI Search's own
// highlight pre/post tags — plain, no attributes) becomes bold; ANY other tag is dropped — never via
// `dangerouslySetInnerHTML`. Every piece of text this function returns is a plain JS string passed as
// React children, so even an unstripped payload could never execute; stripping is defense-in-depth
// (and satisfies the "drop any other tag" wording) on top of that structural guarantee.
// ─────────────────────────────────────────────────────────────────────────────────────────────

const EM_PATTERN = /<em>([\s\S]*?)<\/em>/gi;
const ANY_TAG_PATTERN = /<[^>]*>/g;

/** Pure — independently unit-tested. Never touches the DOM, never parses HTML into elements. */
export function renderMatchReason(raw: string): React.ReactNode[] {
  const nodes: React.ReactNode[] = [];
  let lastIndex = 0;
  let key = 0;
  let match: RegExpExecArray | null;
  EM_PATTERN.lastIndex = 0;
  while ((match = EM_PATTERN.exec(raw)) !== null) {
    const plain = raw.slice(lastIndex, match.index).replace(ANY_TAG_PATTERN, '');
    if (plain) nodes.push(plain);
    // Defense-in-depth: strip any tag INSIDE the <em> span too, before bolding its text.
    const bold = (match[1] ?? '').replace(ANY_TAG_PATTERN, '');
    if (bold) nodes.push(<strong key={`em-${key++}`}>{bold}</strong>);
    lastIndex = EM_PATTERN.lastIndex;
  }
  const rest = raw.slice(lastIndex).replace(ANY_TAG_PATTERN, '');
  if (rest) nodes.push(rest);
  return nodes.length > 0 ? nodes : [raw.replace(ANY_TAG_PATTERN, '')];
}

// Recreated locally per `.claude/patterns/ui/thin-scrollbar.md` — the add-in does not depend on
// `@spaarke/ui-components` (this project's documented ADR-012 Path-A exception). Values match the
// canonical `src/client/shared/Spaarke.UI.Components/src/theme/scrollbar.ts` exactly. Semantic tokens
// only (ADR-021): `colorNeutralStroke1` resolves to the correct thumb color in both light and dark
// theme automatically — no hardcoded hex, no light/dark branching.
const thinScrollbarStyle: GriffelStyle = {
  scrollbarWidth: 'thin',
  scrollbarColor: `${tokens.colorNeutralStroke1} transparent`,
  '::-webkit-scrollbar': { width: '8px', height: '8px' },
  '::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
  '::-webkit-scrollbar-thumb': {
    backgroundColor: tokens.colorNeutralStroke1,
    borderRadius: tokens.borderRadiusMedium,
  },
  '::-webkit-scrollbar-thumb:hover': { backgroundColor: tokens.colorNeutralStroke1Hover },
};

const useStyles = makeStyles({
  // Task 092: the single root this component contributes to the flex:1/minHeight:0 chain — see
  // FindView's own container comment. This element itself never scrolls; `scrollArea` below does.
  root: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
  },
  heading: {
    fontWeight: tokens.fontWeightSemibold,
  },
  hubSection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  hubLabel: {
    color: tokens.colorNeutralForeground3,
  },
  hubRowButton: {
    justifyContent: 'flex-start',
    textAlign: 'left',
    height: 'auto',
    minHeight: 0,
    padding: tokens.spacingVerticalXS,
  },
  // Task 102 (UAT round 6 item 4): each section is a pane = fixed heading + its OWN scroll container.
  // The pane fills whatever height `FindSplitPane` gives its region (flex:1/minHeight:0); the heading
  // never scrolls away, the `scroller` below it does. The scroller is a grid with max-content rows so
  // rows keep their natural height and the scroller scrolls instead of crushing them.
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    flex: 1,
    minHeight: 0,
  },
  scroller: {
    display: 'grid',
    gridTemplateColumns: 'minmax(0, 1fr)',
    gridAutoRows: 'max-content',
    alignContent: 'start',
    gap: tokens.spacingVerticalXS,
    flex: 1,
    minHeight: 0,
    overflowY: 'auto',
    ...thinScrollbarStyle,
  },
  row: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: '2px',
    width: '100%',
    minWidth: 0,
    textAlign: 'left',
    justifyContent: 'flex-start',
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    height: 'auto',
  },
  rowStatic: {
    border: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  rowLabel: {
    fontWeight: tokens.fontWeightSemibold,
  },
  rowMeta: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  list: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  sentinel: {
    height: '1px',
  },
  emptyLine: {
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingVerticalXS,
  },
  loadingLine: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalXS,
  },
  groupCaption: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  recordTitleLine: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    minWidth: 0,
    maxWidth: '100%',
  },
  inlineStatus: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground3,
  },
  inlineError: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
  },
  reasonsList: {
    display: 'flex',
    flexDirection: 'column',
    gap: '2px',
    marginTop: '2px',
  },
  reasonLine: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
  },
});

type Styles = ReturnType<typeof useStyles>;
type OpenRecordHandler = (entityType: string, recordId: string) => void;

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Documents section
// ─────────────────────────────────────────────────────────────────────────────────────────────

function HubSection({
  hubNodes,
  onOpenRecord,
  styles,
}: {
  hubNodes: FindResultNode[];
  onOpenRecord: OpenRecordHandler | undefined;
  styles: Styles;
}): React.ReactElement {
  return (
    <div className={styles.hubSection} data-testid="find-results-hub-section">
      <Text size={200} weight="semibold" className={styles.hubLabel}>
        This document&rsquo;s record{hubNodes.length > 1 ? 's' : ''} — not a similarity match
      </Text>
      {hubNodes.map(node => {
        const label = hubRowLabel(node);
        const target = onOpenRecord ? extractHubRecordId(node) : null;
        if (target) {
          return (
            <Button
              key={node.id}
              appearance="subtle"
              size="small"
              className={styles.hubRowButton}
              onClick={() => onOpenRecord!(target.entityType, target.recordId)}
            >
              {label}
            </Button>
          );
        }
        return (
          <Text key={node.id} size={200}>
            {label}
          </Text>
        );
      })}
    </div>
  );
}

/**
 * Task 102 label check. The BFF's `CreateParentHubNode` builds `Label = sourceDoc.MatterName ?? "Matter"`
 * (likewise Project/Invoice/Email), so when the parent record's NAME is unavailable the "name" it sends is
 * the TYPE word — which this row used to render as "Matter: Matter". A label equal to the type word is a
 * placeholder, not a name: show the type with an honest "name unavailable" instead of repeating it.
 */
function hubRowLabel(node: FindResultNode): string {
  const typeLabel = HUB_LABELS[node.type] ?? 'Related record';
  const name = node.data.label?.trim();
  if (!name || name.toLowerCase() === typeLabel.toLowerCase()) {
    return `${typeLabel} (name unavailable)`;
  }
  return `${typeLabel}: ${name}`;
}

/**
 * The BFF's `CreateNode` sends `DocumentType = document.DocumentType ?? "Unknown"` — a server fallback
 * for an index row with no document type, not a type. Showing it as a type is noise, so it is omitted.
 */
function displayableDocumentType(documentType: string | null | undefined): string | null {
  const trimmed = documentType?.trim();
  return trimmed && trimmed.toLowerCase() !== 'unknown' ? trimmed : null;
}

function rowMetaText(node: FindResultNode): string {
  const similarityPct = typeof node.data.similarity === 'number' ? Math.round(node.data.similarity * 100) : null;
  return [
    displayableDocumentType(node.data.documentType),
    similarityPct !== null ? `${similarityPct}% match` : null,
    node.data.parentEntityName,
  ]
    .filter((part): part is string => Boolean(part))
    .join(' · ');
}

function ResultRow({
  node,
  onOpenRecord,
  styles,
}: {
  node: FindResultNode;
  onOpenRecord: OpenRecordHandler | undefined;
  styles: Styles;
}): React.ReactElement {
  const label = node.data.label || 'Untitled document';
  const meta = rowMetaText(node);

  if (onOpenRecord) {
    return (
      <Button
        appearance="subtle"
        className={styles.row}
        onClick={() => onOpenRecord('sprk_document', cleanGuid(node.id))}
      >
        <span className={styles.rowLabel}>{label}</span>
        {meta && <span className={styles.rowMeta}>{meta}</span>}
      </Button>
    );
  }

  return (
    <div className={`${styles.row} ${styles.rowStatic}`}>
      <Text className={styles.rowLabel}>{label}</Text>
      {meta && <Text className={styles.rowMeta}>{meta}</Text>}
    </div>
  );
}

function DocumentsSection({
  documents,
  onOpenRecord,
  announce,
  styles,
}: {
  documents: DocumentsResultState;
  onOpenRecord: OpenRecordHandler | undefined;
  announce: (message: string, mode?: AnnounceMode) => void;
  styles: Styles;
}): React.ReactElement {
  // A stable empty-array identity for the non-loaded cases, so the memos/effects below don't see a
  // fresh `[]` (and therefore a "changed" dependency) on every render while loading/erroring.
  const nodes = useMemo(() => (documents.kind === 'loaded' ? documents.nodes : EMPTY_NODES), [documents]);
  const resultRows = useMemo(() => nodes.filter(isResultRow), [nodes]);
  const hubNodes = useMemo(() => nodes.filter(isHubNode), [nodes]);

  const { visibleItems, hasMore, sentinelRef } = useLazyResults(resultRows);

  // NFR-11: announce the empty state once per empty, LOADED result set (never while loading/error).
  const announcedEmptyForRef = useRef<FindResultNode[] | null>(null);
  useEffect(() => {
    if (documents.kind === 'loaded' && resultRows.length === 0 && announcedEmptyForRef.current !== nodes) {
      announcedEmptyForRef.current = nodes;
      announce('No similar documents found.', 'polite');
    }
  }, [documents.kind, resultRows.length, nodes, announce]);

  // NFR-11: announce each SCROLL-DRIVEN reveal (not the first chunk — the parent view's own
  // state-transition announcement already covers "results are here").
  const prevVisibleCountRef = useRef(visibleItems.length);
  const isFirstRevealRef = useRef(true);
  useEffect(() => {
    if (isFirstRevealRef.current) {
      isFirstRevealRef.current = false;
      prevVisibleCountRef.current = visibleItems.length;
      return;
    }
    const added = visibleItems.length - prevVisibleCountRef.current;
    prevVisibleCountRef.current = visibleItems.length;
    if (added > 0) {
      announce(`${added} more document${added === 1 ? '' : 's'} shown.`, 'polite');
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [visibleItems.length]);

  return (
    <div className={styles.section} data-testid="find-documents-section">
      <Text className={styles.heading}>Similar Documents</Text>
      <div className={styles.scroller} data-testid="find-documents-scroll">
        {documents.kind === 'loading' && (
          <div className={styles.loadingLine}>
            <Spinner size="tiny" />
            <Text size={200}>Finding similar documents…</Text>
          </div>
        )}

        {documents.kind === 'error' && (
          <MessageBar intent="error" layout="multiline">
            <MessageBarBody>
              <MessageBarTitle>Couldn&rsquo;t load similar documents</MessageBarTitle>
              {documents.message}
            </MessageBarBody>
          </MessageBar>
        )}

        {documents.kind === 'loaded' && (
          <>
            {documents.partialResultsWarning && (
              <MessageBar intent="warning" layout="multiline">
                <MessageBarBody>
                  <MessageBarTitle>Results may be incomplete</MessageBarTitle>
                  {documents.partialResultsWarning}
                </MessageBarBody>
              </MessageBar>
            )}
            {hubNodes.length > 0 && <HubSection hubNodes={hubNodes} onOpenRecord={onOpenRecord} styles={styles} />}
            {resultRows.length === 0 ? (
              <Text className={styles.emptyLine}>No similar documents found</Text>
            ) : (
              // A real list for screen readers (NFR-11). The sentinel sits outside the list, but must
              // stay inside the scroll area: the observer only sees it once it scrolls into view there.
              <div role="list" aria-label="Similar Documents" className={styles.list}>
                {visibleItems.map(node => (
                  <div key={node.id} role="listitem">
                    <ResultRow node={node} onOpenRecord={onOpenRecord} styles={styles} />
                  </div>
                ))}
              </div>
            )}
            {hasMore && (
              <div
                ref={sentinelRef}
                className={styles.sentinel}
                aria-hidden="true"
                data-testid="find-results-sentinel"
              />
            )}
          </>
        )}
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Matching records section (task 077; restructured as an independent section — task 092)
// ─────────────────────────────────────────────────────────────────────────────────────────────

const RECORD_TYPE_LABELS: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_invoice: 'Invoice',
};

/** Names the match basis honestly (034 §4 condition ii): a text match on the profile, not similarity. */
const SEED_SOURCE_CAPTION: Record<RecordSeedSource, string> = {
  keywords: 'Matched on this document’s AI keywords — not a content-similarity match.',
  tldr: 'Matched on this document’s AI summary — not a content-similarity match.',
  summary: 'Matched on this document’s AI summary — not a content-similarity match.',
};

/**
 * Shown when there is nothing honest to search records on. True in BOTH cases that produce it — a profile
 * still pending, and a completed profile that has no keywords/TL;DR/summary — which is why it does not say
 * "once the profile is complete" (that would be false for the second).
 */
const NO_SEED_CAPTION =
  'Matching records are found using this document’s AI keywords or summary — none is available for it yet.';

/** Up to 3 reasons are shown per record (task 092 / UAT-3) — the server returns up to 5. */
const MAX_DISPLAYED_REASONS = 3;

function recordMetaText(record: RecordMatch, reference: string | undefined): string {
  const pct = Math.round((record.confidenceScore ?? 0) * 100);
  return [`${pct}% match`, reference].filter((part): part is string => Boolean(part)).join(' · ');
}

function RecordRow({
  record,
  onOpenRecord,
  styles,
}: {
  record: RecordMatch;
  onOpenRecord: OpenRecordHandler | undefined;
  styles: Styles;
}): React.ReactElement {
  const typeLabel = RECORD_TYPE_LABELS[record.recordType.toLowerCase()] ?? 'Record';
  const reference = record.referenceNumbers?.find(ref => Boolean(ref?.trim()));
  const meta = recordMetaText(record, reference);
  const reasons = (record.matchReasons ?? []).filter(reason => Boolean(reason?.trim())).slice(0, MAX_DISPLAYED_REASONS);

  const content = (
    <>
      <span className={styles.recordTitleLine}>
        {/* The type badge is what makes a record row visually distinct from a document row. */}
        <Badge appearance="outline" size="small" color="informative">
          {typeLabel}
        </Badge>
        <Text className={styles.rowLabel} truncate wrap={false}>
          {record.recordName || 'Untitled record'}
        </Text>
      </span>
      <Text className={styles.rowMeta}>{meta}</Text>
      {reasons.length > 0 && (
        <div className={styles.reasonsList}>
          {reasons.map(reason => (
            <Text key={reason} size={200} className={styles.reasonLine}>
              {renderMatchReason(reason)}
            </Text>
          ))}
        </div>
      )}
    </>
  );

  if (onOpenRecord) {
    return (
      <Button
        appearance="subtle"
        className={styles.row}
        data-testid="find-record-row"
        onClick={() => onOpenRecord(record.recordType, record.recordId)}
      >
        {content}
      </Button>
    );
  }

  return (
    <div className={`${styles.row} ${styles.rowStatic}`} data-testid="find-record-row">
      {content}
    </div>
  );
}

function RecordsSection({
  records,
  onOpenRecord,
  styles,
}: {
  records: UseFindRecordMatchesResult;
  onOpenRecord: OpenRecordHandler | undefined;
  styles: Styles;
}): React.ReactElement {
  return (
    <div className={styles.section} data-testid="find-records-section">
      <Text className={styles.heading}>Matching records</Text>
      <div className={styles.scroller} data-testid="find-records-scroll">
        {records.seedSource && <Text className={styles.groupCaption}>{SEED_SOURCE_CAPTION[records.seedSource]}</Text>}

        {records.status === 'no-seed' && <Text className={styles.groupCaption}>{NO_SEED_CAPTION}</Text>}

        {records.status === 'loading' && (
          <div className={styles.inlineStatus}>
            <Spinner size="tiny" />
            <Text size={200}>Finding matching records…</Text>
          </div>
        )}

        {records.status === 'error' && (
          <Text className={styles.inlineError} role="alert">
            Couldn&rsquo;t load matching records. {records.error}
          </Text>
        )}

        {records.status === 'ready' && records.records.length === 0 && (
          <Text className={styles.emptyLine}>No matching records found</Text>
        )}

        {records.status === 'ready' && records.records.length > 0 && (
          <div role="list" aria-label="Matching records" className={styles.list}>
            {records.records.map(record => (
              <div key={`${record.recordType}:${record.recordId}`} role="listitem">
                <RecordRow record={record} onOpenRecord={onOpenRecord} styles={styles} />
              </div>
            ))}
          </div>
        )}

        {records.isLoadingMore && (
          <div className={styles.inlineStatus}>
            <Spinner size="tiny" />
            <Text size={200}>Loading more records…</Text>
          </div>
        )}

        {records.loadMoreError && (
          <Text className={styles.inlineError} role="alert">
            Couldn&rsquo;t load more records. {records.loadMoreError}
          </Text>
        )}

        {/* The progressive-fetch sentinel (ADR-051). Must sit inside the scroll area, after the last row. */}
        {records.status === 'ready' && records.hasMore && (
          <div
            ref={records.sentinelRef}
            className={styles.sentinel}
            aria-hidden="true"
            data-testid="find-records-sentinel"
          />
        )}
      </div>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────────────────────

export interface FindResultsListProps {
  /** Task 092: the Documents section's own state — loading / error / loaded. Always rendered. */
  documents: DocumentsResultState;
  /**
   * Announces reveal events (NFR-11) and the empty state for the DOCUMENTS section. Pass the SAME
   * `announce` the parent view already owns from its own `useAnnounce()` — this component renders no
   * live region of its own. (The records section announces via `FindView` today — unchanged by 092.)
   */
  announce: (message: string, mode?: AnnounceMode) => void;
  /**
   * Task 092: opens a document row (`sprk_document`), a parent/hub row (`sprk_matter` /
   * `sprk_project` / `sprk_invoice` / `sprk_document`), or a matching-record row (`record.recordType`)
   * via `(entityType, recordId)`. `FindView` wires this to `openRecord`, gated on
   * `canOpenBrowserWindow` + `ORG_URL` (NFR-10). Omitted → every row renders as plain, non-interactive
   * text (never a dead link).
   */
  onOpenRecord?: OpenRecordHandler;
  /**
   * The records half, from `useFindRecordMatches` (fetching lives there; this component only
   * presents). Omitted → the Matching records section renders nothing at all (no heading either) —
   * `FindView` always supplies it once a document is indexed, so this is a resilience fallback, not a
   * real production path.
   */
  records?: UseFindRecordMatchesResult;
}

export const FindResultsList: React.FC<FindResultsListProps> = ({ documents, announce, onOpenRecord, records }) => {
  const styles = useStyles();

  // NFR-11 (task 077): announce the records half — once when its first page settles, then each page
  // that scrolling appends. Keyed on the settled count so a re-render never repeats an announcement.
  const recordsStatus = records?.status;
  const recordsCount = records?.records.length ?? 0;
  const announcedRecordsCountRef = useRef<number | null>(null);
  useEffect(() => {
    if (recordsStatus !== 'ready') {
      if (recordsStatus === 'loading' || recordsStatus === 'no-seed') {
        announcedRecordsCountRef.current = null;
      }
      return;
    }
    const previous = announcedRecordsCountRef.current;
    announcedRecordsCountRef.current = recordsCount;
    if (previous === null) {
      announce(
        recordsCount === 0
          ? 'No matching records found.'
          : `${recordsCount} matching record${recordsCount === 1 ? '' : 's'} found.`,
        'polite'
      );
    } else if (recordsCount > previous) {
      const added = recordsCount - previous;
      announce(`${added} more matching record${added === 1 ? '' : 's'} shown.`, 'polite');
    }
  }, [recordsStatus, recordsCount, announce]);

  const documentsSection = (
    <DocumentsSection documents={documents} onOpenRecord={onOpenRecord} announce={announce} styles={styles} />
  );

  return (
    <div className={styles.root}>
      {/* Task 102 — two sections, each with its OWN scroll container, sharing the height through a
          draggable divider. Both always render, independently of each other's state (task 092 / UAT-3:
          records must not wait on documents). Without `records` only the documents pane renders. */}
      {records !== undefined ? (
        <FindSplitPane
          top={documentsSection}
          bottom={<RecordsSection records={records} onOpenRecord={onOpenRecord} styles={styles} />}
        />
      ) : (
        documentsSection
      )}
    </div>
  );
};

export default FindResultsList;
