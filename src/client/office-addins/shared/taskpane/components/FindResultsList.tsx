import React, { useEffect, useMemo, useRef } from 'react';
import { Badge, Body1, Button, Spinner, Text, makeStyles, tokens, type GriffelStyle } from '@fluentui/react-components';
import { DocumentSearchRegular } from '@fluentui/react-icons';
import { useLazyResults } from '../hooks/useLazyResults';
import type { AnnounceMode } from '../hooks/useAnnounce';
import type { RecordSeedSource, RecordMatch, UseFindRecordMatchesResult } from '../hooks/useFindRecordMatches';

/**
 * FindResultsList — renders the Find tab's results (spaarkeai-word-add-in-r1 task 034).
 *
 * **Path 2 (owner-approved 2026-09-15, Find list only)** — the documented exception to ADR-051's
 * literal "fetch progressively" MUST. `GET /api/ai/visualization/related/{documentId}` (the only
 * source for these results, hardened by task 032) has no paging mechanism of any kind: it returns ONE
 * complete, authorization-trimmed, bounded response (at most 50 rows) per call — there is no "page 2"
 * to fetch. See `projects/spaarkeai-word-add-in-r1/notes/034-route-paging-escalation.md` for the full
 * evidence and the owner's decision.
 *
 * **Framing.** This is rendered as a RANKED "top matches" list ("Most similar documents"), never as a
 * pageable dataset, and never implies further pages exist. Scrolling reveals more of the rows already
 * in hand (`useLazyResults` — DOM reveal only, no re-fetch); it never issues a second network call.
 *
 * **Hub nodes** (`matter` / `project` / `invoice` / `email`) are the SOURCE document's own parent
 * record(s) — built from the source document's Dataverse lookups
 * (`VisualizationService.GetHardcodedRelationshipsAsync`), never a content-similarity match. They
 * render in their own, distinctly labeled section and are never mixed into the ranked list.
 *
 * **No pager, ever** (ADR-051): no numbered pages, no prev/next, no chevron, no "Load more" button.
 * The only navigation is scroll.
 *
 * **F-c (records bridge) — task 077 REVERSED task 034's documents-only decision.** Records now render
 * after the documents, from `POST /api/ai/search/records` (per-row authorized), seeded by the document's
 * own AI-profile keywords — the seed task 034 never considered (it rejected title and body text, both
 * genuinely bad). Fetching lives in `useFindRecordMatches`; this component only presents. Records are a
 * TEXT match on the document's subject, not content similarity, and the heading says so. Omitting the
 * `records` prop keeps the documents-only behaviour exactly as before.
 *
 * **One scroll area, two paging models, no conflict.** Documents are revealed from a single bounded
 * response (reveal-only); records are fetched page by page (progressive). Records sit AFTER the
 * documents, so the only section that ever grows is the tail — content never moves under the reader.
 *
 * **Opening a result is out of scope.** `onOpenResult` is a seam only — task 027's
 * `openRecordLauncher.ts` wires the actual navigation separately. When omitted, rows render as
 * non-interactive text (still legible, just not actionable yet).
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

/** Mirrors the server's `NodeTypes` hub-type set (`IsParentHub`) — matter/project/invoice/email. */
const HUB_NODE_TYPES = new Set(['matter', 'project', 'invoice', 'email']);
const SOURCE_NODE_TYPE = 'source';

const HUB_LABELS: Record<string, string> = {
  matter: 'Matter',
  project: 'Project',
  invoice: 'Invoice',
  email: 'Email',
};

function isHubNode(node: FindResultNode): boolean {
  return HUB_NODE_TYPES.has(node.type);
}

/** Mirrors the server's `IsResultRow`: a genuine similarity match — not the source, not a hub. */
function isResultRow(node: FindResultNode): boolean {
  return node.type !== SOURCE_NODE_TYPE && !isHubNode(node);
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
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
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
  scrollArea: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    overflowY: 'auto',
    maxHeight: '360px',
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
  emptyState: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalXXL,
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
  },
  icon: {
    fontSize: '32px',
    color: tokens.colorNeutralForeground3,
  },
  // Task 077 — records group. Semantic tokens only (ADR-021); a divider rather than a second scroller.
  recordsGroup: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    marginTop: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalS,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
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
  documentsEmptyLine: {
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingVerticalXS,
  },
});

type Styles = ReturnType<typeof useStyles>;

function HubSection({ hubNodes, styles }: { hubNodes: FindResultNode[]; styles: Styles }): React.ReactElement {
  return (
    <div className={styles.hubSection} data-testid="find-results-hub-section">
      <Text size={200} weight="semibold" className={styles.hubLabel}>
        This document&rsquo;s record{hubNodes.length > 1 ? 's' : ''} — not a similarity match
      </Text>
      {hubNodes.map(node => (
        <Text key={node.id} size={200}>
          {HUB_LABELS[node.type] ?? 'Related record'}: {node.data.label || 'Untitled'}
        </Text>
      ))}
    </div>
  );
}

function rowMetaText(node: FindResultNode): string {
  const similarityPct = typeof node.data.similarity === 'number' ? Math.round(node.data.similarity * 100) : null;
  return [node.data.documentType, similarityPct !== null ? `${similarityPct}% match` : null, node.data.parentEntityName]
    .filter((part): part is string => Boolean(part))
    .join(' · ');
}

function ResultRow({
  node,
  onOpenResult,
  styles,
}: {
  node: FindResultNode;
  onOpenResult: ((node: FindResultNode) => void) | undefined;
  styles: Styles;
}): React.ReactElement {
  const label = node.data.label || 'Untitled document';
  const meta = rowMetaText(node);

  if (onOpenResult) {
    return (
      <Button appearance="subtle" className={styles.row} onClick={() => onOpenResult(node)}>
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

// ─────────────────────────────────────────────────────────────────────────────────────────────
// Task 077 — the records group
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

function RecordRow({ record, styles }: { record: RecordMatch; styles: Styles }): React.ReactElement {
  const typeLabel = RECORD_TYPE_LABELS[record.recordType.toLowerCase()] ?? 'Record';
  const reference = record.referenceNumbers?.find(ref => Boolean(ref?.trim()));

  return (
    <div className={`${styles.row} ${styles.rowStatic}`} data-testid="find-record-row">
      <span className={styles.recordTitleLine}>
        {/* The type badge is what makes a record row visually distinct from a document row. */}
        <Badge appearance="outline" size="small" color="informative">
          {typeLabel}
        </Badge>
        <Text className={styles.rowLabel} truncate wrap={false}>
          {record.recordName || 'Untitled record'}
        </Text>
      </span>
      {reference && <Text className={styles.rowMeta}>{reference}</Text>}
    </div>
  );
}

function RecordsGroup({
  records,
  styles,
}: {
  records: UseFindRecordMatchesResult;
  styles: Styles;
}): React.ReactElement {
  return (
    <div className={styles.recordsGroup} data-testid="find-records-group">
      <Text className={styles.heading}>Matching records</Text>
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
        <Text className={styles.groupCaption}>No records you can see match this document.</Text>
      )}

      {records.status === 'ready' && records.records.length > 0 && (
        <div role="list" aria-label="Matching records" className={styles.list}>
          {records.records.map(record => (
            <div key={`${record.recordType}:${record.recordId}`} role="listitem">
              <RecordRow record={record} styles={styles} />
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
  );
}

export interface FindResultsListProps {
  /** All nodes from the single bounded, per-row-authorized response (tasks 032/033). */
  nodes: FindResultNode[];
  /**
   * Announces reveal events (NFR-11) and the empty state. Pass the SAME `announce` the parent view
   * already owns from its own `useAnnounce()` — this component renders no live region of its own.
   */
  announce: (message: string, mode?: AnnounceMode) => void;
  /**
   * Seam only (task 034 does not implement opening a result — task 027's `openRecordLauncher.ts`
   * wires this separately). Omitted → rows render as plain, non-interactive text.
   */
  onOpenResult?: (node: FindResultNode) => void;
  /**
   * Task 077 — the records half, from `useFindRecordMatches` (fetching lives there; this component only
   * presents). Omitted → documents-only, exactly as before task 077.
   */
  records?: UseFindRecordMatchesResult;
}

export const FindResultsList: React.FC<FindResultsListProps> = ({ nodes, announce, onOpenResult, records }) => {
  const styles = useStyles();

  const resultRows = useMemo(() => nodes.filter(isResultRow), [nodes]);
  const hubNodes = useMemo(() => nodes.filter(isHubNode), [nodes]);

  const { visibleItems, hasMore, sentinelRef } = useLazyResults(resultRows);

  // NFR-11: announce the empty state once per empty result set.
  const announcedEmptyForRef = useRef<FindResultNode[] | null>(null);
  useEffect(() => {
    if (resultRows.length === 0 && announcedEmptyForRef.current !== nodes) {
      announcedEmptyForRef.current = nodes;
      announce('No similar documents found.', 'polite');
    }
  }, [resultRows.length, nodes, announce]);

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

  // The big empty state is used only when there is NOTHING to show in either half. A documents-empty
  // result must not hide records that are loading, that exist, or that failed (the failure must show).
  //   - records omitted (pre-077 callers)  → exactly the pre-077 empty state
  //   - records never searched (no seed)   → documents-empty state; it must NOT claim "no related
  //                                          records", because none were looked for — a caption says why
  //   - records searched, zero came back   → the combined "documents or records" empty state
  const recordsSearchedAndEmpty = records?.status === 'ready' && recordsCount === 0;
  const recordsNotSearched = records?.status === 'no-seed';
  if (resultRows.length === 0 && (records === undefined || recordsSearchedAndEmpty || recordsNotSearched)) {
    return (
      <div className={styles.root}>
        {hubNodes.length > 0 && <HubSection hubNodes={hubNodes} styles={styles} />}
        <div className={styles.emptyState}>
          <DocumentSearchRegular className={styles.icon} />
          <Text weight="semibold">
            {recordsSearchedAndEmpty ? 'No similar documents or matching records found' : 'No similar documents found'}
          </Text>
          <Body1>
            {recordsSearchedAndEmpty
              ? 'Spaarke didn’t find any documents or records you can see that relate to this one.'
              : 'Spaarke didn’t find any documents you can see that are similar to this one.'}
          </Body1>
          {recordsNotSearched && <Text className={styles.groupCaption}>{NO_SEED_CAPTION}</Text>}
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      <Text className={styles.heading}>Most similar documents</Text>
      {hubNodes.length > 0 && <HubSection hubNodes={hubNodes} styles={styles} />}
      <div className={styles.scrollArea} data-testid="find-results-scroll-area">
        {resultRows.length === 0 ? (
          <Text className={styles.documentsEmptyLine}>No similar documents found.</Text>
        ) : (
          /* A real list for screen readers (NFR-11). The sentinel sits outside the list, but must stay
             inside the scroll area: the observer only sees it once it scrolls into view there. */
          <div role="list" aria-label="Most similar documents" className={styles.list}>
            {visibleItems.map(node => (
              <div key={node.id} role="listitem">
                <ResultRow node={node} onOpenResult={onOpenResult} styles={styles} />
              </div>
            ))}
          </div>
        )}
        {hasMore && (
          <div ref={sentinelRef} className={styles.sentinel} aria-hidden="true" data-testid="find-results-sentinel" />
        )}
        {/* Task 077: records AFTER documents, in the SAME scroll area — only the tail ever grows. */}
        {records !== undefined && <RecordsGroup records={records} styles={styles} />}
      </div>
    </div>
  );
};

export default FindResultsList;
