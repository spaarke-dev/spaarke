import React, { useEffect, useMemo, useRef } from 'react';
import { Body1, Button, Text, makeStyles, tokens, type GriffelStyle } from '@fluentui/react-components';
import { DocumentSearchRegular } from '@fluentui/react-icons';
import { useLazyResults } from '../hooks/useLazyResults';
import type { AnnounceMode } from '../hooks/useAnnounce';

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
 * **F-c (records bridge, task 034 decision):** documents only. No call to
 * `POST /api/ai/search/records` — see `notes/034-records-bridge-decision.md`.
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
}

export const FindResultsList: React.FC<FindResultsListProps> = ({ nodes, announce, onOpenResult }) => {
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

  if (resultRows.length === 0) {
    return (
      <div className={styles.root}>
        {hubNodes.length > 0 && <HubSection hubNodes={hubNodes} styles={styles} />}
        <div className={styles.emptyState}>
          <DocumentSearchRegular className={styles.icon} />
          <Text weight="semibold">No similar documents found</Text>
          <Body1>Spaarke didn&rsquo;t find any documents you can see that are similar to this one.</Body1>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.root}>
      <Text className={styles.heading}>Most similar documents</Text>
      {hubNodes.length > 0 && <HubSection hubNodes={hubNodes} styles={styles} />}
      <div className={styles.scrollArea} data-testid="find-results-scroll-area">
        {/* A real list for screen readers (NFR-11). The sentinel sits outside the list, but must stay
            inside the scroll area: the observer only sees it once it scrolls into view there. */}
        <div role="list" aria-label="Most similar documents" className={styles.list}>
          {visibleItems.map(node => (
            <div key={node.id} role="listitem">
              <ResultRow node={node} onOpenResult={onOpenResult} styles={styles} />
            </div>
          ))}
        </div>
        {hasMore && (
          <div ref={sentinelRef} className={styles.sentinel} aria-hidden="true" data-testid="find-results-sentinel" />
        )}
      </div>
    </div>
  );
};

export default FindResultsList;
