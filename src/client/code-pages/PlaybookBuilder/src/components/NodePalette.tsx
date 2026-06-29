/**
 * NodePalette — categorized 33-executor Node Types left panel for the
 * PlaybookBuilder canvas (R7 Wave 8 task 082, FR-22).
 *
 * Replaces the legacy ~11-tile inline palette in BuilderLayout.tsx with a
 * 6-tier Accordion that surfaces every server-side ExecutorType. Each tile is
 * draggable into the canvas; the drop handler in PlaybookCanvas.tsx reads the
 * payload (containing both the legacy canvas `type` discriminator AND the new
 * `executorType` Choice value) and creates a node with the correct dispatch
 * identity baked in.
 *
 * Design references:
 *   - R7 design.md §11 (Node Types panel categorization)
 *   - R7 spec.md FR-22 (33-entry categorized panel with 6 tiers)
 *   - R7 spec.md FR-26 (sprk_executortype is the dispatch field — `data.executorType` carries it)
 *   - ADR-006 (Fluent UI v9 only — Accordion, Input, Tooltip, etc.)
 *   - ADR-021 (Dark mode binding — semantic tokens only, NO hex colors)
 *   - Task 080 audit (`notes/spikes/playbookbuilder-sprk-nodetype-audit.md`)
 *
 * Coordination with sibling tasks:
 *   - Task 083 (parallel): wires the typed config-form renderer driven by the
 *     /api/ai/playbook-builder/executor-config-schemas endpoint. Orthogonal —
 *     this panel is metadata only; that endpoint drives per-executor config UX.
 *   - Task 088 (later): retires the legacy `data.type` discriminator from canvas
 *     state. Until then, the drag payload carries BOTH `type` AND `executorType`
 *     so existing rendering doesn't regress.
 */

import React, { useState, useMemo, useCallback, memo } from 'react';
import {
  makeStyles,
  tokens,
  shorthands,
  Accordion,
  AccordionHeader,
  AccordionItem,
  AccordionPanel,
  Text,
  Input,
  Badge,
  Tooltip,
} from '@fluentui/react-components';
import { Search20Regular, DismissCircle16Regular } from '@fluentui/react-icons';
import {
  EXECUTOR_METADATA,
  TIER_ORDER,
  TIER_LABEL,
  groupExecutorsByTier,
  type ExecutorMetadata,
  type ExecutorTier,
} from '../config/executorMetadata';
import type { PlaybookNodeType } from '../types/canvas';

// ---------------------------------------------------------------------------
// Drag-drop payload shape
// ---------------------------------------------------------------------------

/**
 * Payload written to the dataTransfer on drag-start and read by
 * `PlaybookCanvas.handleDrop`. Carries BOTH legacy `type` (renderer discriminator)
 * AND new `executorType` Choice value (Wave 8 FR-26 dispatch field) so node
 * creation works before task 088 retires `data.type`.
 *
 * Backward-compatible: existing callers that read only `{ type, label }` continue
 * to work (extra fields ignored).
 */
export interface NodePaletteDragPayload {
  /** Legacy canvas node-type discriminator (drives React Flow nodeTypes registry). */
  type: PlaybookNodeType;
  /** Display label for the new node. */
  label: string;
  /** R7 FR-26: `sprk_executortype` Choice value baked into node data. */
  executorType: number;
  /** Server PascalCase executor name — diagnostic + telemetry only. */
  executorName: string;
}

// ---------------------------------------------------------------------------
// Styles (ADR-021: semantic tokens only)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground2,
    overflow: 'hidden',
  },
  searchContainer: {
    ...shorthands.padding('8px'),
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke2),
    backgroundColor: tokens.colorNeutralBackground2,
    flexShrink: 0,
  },
  searchInput: {
    width: '100%',
  },
  scrollArea: {
    flex: 1,
    overflowY: 'auto',
    overflowX: 'hidden',
  },
  accordion: {
    // Accordion has its own padding — keep wrapper bare
  },
  accordionItem: {
    ...shorthands.borderBottom('1px', 'solid', tokens.colorNeutralStroke3),
  },
  accordionHeader: {
    backgroundColor: tokens.colorNeutralBackground3,
  },
  tierHeaderLabel: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap('6px'),
    fontWeight: tokens.fontWeightSemibold,
  },
  tierBadge: {
    marginLeft: 'auto',
  },
  paletteList: {
    ...shorthands.padding('4px', '8px', '8px', '8px'),
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap('4px'),
  },
  paletteItem: {
    display: 'flex',
    alignItems: 'flex-start',
    ...shorthands.gap('8px'),
    ...shorthands.padding('6px', '8px'),
    ...shorthands.borderRadius('4px'),
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
    cursor: 'grab',
    transition: 'background-color 0.12s ease, border-color 0.12s ease',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
      ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke1Hover),
    },
    ':active': {
      cursor: 'grabbing',
      backgroundColor: tokens.colorNeutralBackground1Pressed,
    },
  },
  paletteItemPrefix: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    minWidth: '24px',
    paddingTop: '1px',
    flexShrink: 0,
  },
  paletteItemInfo: {
    display: 'flex',
    flexDirection: 'column',
    minWidth: 0, // allow truncation
    flex: 1,
  },
  paletteItemLabel: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  },
  paletteItemDescription: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    display: '-webkit-box',
    WebkitLineClamp: 1,
    WebkitBoxOrient: 'vertical',
    lineHeight: tokens.lineHeightBase100,
  },
  emptyState: {
    ...shorthands.padding('16px', '8px'),
    textAlign: 'center',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
});

// ---------------------------------------------------------------------------
// Sub-component: PaletteTile (one draggable executor entry)
// ---------------------------------------------------------------------------

interface PaletteTileProps {
  metadata: ExecutorMetadata;
  onDragStart: (event: React.DragEvent, metadata: ExecutorMetadata) => void;
}

const PaletteTile = memo(function PaletteTile({ metadata, onDragStart }: PaletteTileProps) {
  const styles = useStyles();

  const handleDragStart = useCallback(
    (event: React.DragEvent) => onDragStart(event, metadata),
    [metadata, onDragStart]
  );

  // Tooltip carries the full description (useful when CSS truncates the inline copy).
  const tooltipContent = `${metadata.tierPrefix} · ${metadata.label}\n${metadata.description}`;

  return (
    <Tooltip content={tooltipContent} relationship="description" positioning="after">
      <div
        className={styles.paletteItem}
        draggable
        onDragStart={handleDragStart}
        role="button"
        tabIndex={0}
        aria-label={`Drag to add ${metadata.label} node (Executor Type ${metadata.value})`}
        data-executor-value={metadata.value}
        data-executor-name={metadata.name}
      >
        <span className={styles.paletteItemPrefix}>{metadata.tierPrefix}</span>
        <div className={styles.paletteItemInfo}>
          <span className={styles.paletteItemLabel}>{metadata.label}</span>
          <span className={styles.paletteItemDescription}>{metadata.description}</span>
        </div>
      </div>
    </Tooltip>
  );
});

// ---------------------------------------------------------------------------
// NodePalette component
// ---------------------------------------------------------------------------

export interface NodePaletteProps {
  /**
   * Optional drag-start callback invoked when an executor tile begins a drag.
   * Default behavior (when omitted) writes the payload to dataTransfer with
   * MIME type 'application/reactflow' — matching the contract that
   * PlaybookCanvas.handleDrop already reads.
   */
  onTileDragStart?: (event: React.DragEvent, payload: NodePaletteDragPayload) => void;
}

/**
 * Categorized 33-executor Node Types panel. Mount as the left sidebar of the
 * PlaybookBuilder canvas.
 */
export function NodePalette({ onTileDragStart }: NodePaletteProps = {}): React.ReactElement {
  const styles = useStyles();
  const [searchQuery, setSearchQuery] = useState('');

  // Filter once per render; case-insensitive match against label + name + description + tierPrefix.
  const filteredEntries = useMemo(() => {
    const q = searchQuery.trim().toLowerCase();
    if (!q) return EXECUTOR_METADATA;
    return EXECUTOR_METADATA.filter(e =>
      e.label.toLowerCase().includes(q) ||
      e.name.toLowerCase().includes(q) ||
      e.description.toLowerCase().includes(q) ||
      e.tierPrefix.includes(q)
    );
  }, [searchQuery]);

  // Group the filtered entries by tier; skip tiers with no matches.
  const groupedFiltered = useMemo(() => {
    const grouped = groupExecutorsByTier();
    // Reset and refill based on filtered set.
    const result: Record<ExecutorTier, ExecutorMetadata[]> = {
      AI: [],
      Compute: [],
      Mutations: [],
      Control: [],
      Delivery: [],
      Capability: [],
    };
    for (const entry of filteredEntries) {
      result[entry.tier].push(entry);
    }
    return result;
  }, [filteredEntries]);

  // All 6 tiers open by default (FR-22 requirement: "first-time user sees all 6 tiers expanded").
  // Multiple-open Accordion lets the maker browse without losing context.
  const defaultOpenItems = useMemo<ExecutorTier[]>(() => [...TIER_ORDER], []);

  // Default drag-start handler — writes the FR-26 payload to dataTransfer
  // with the legacy 'application/reactflow' MIME type read by PlaybookCanvas.
  const handleTileDragStart = useCallback(
    (event: React.DragEvent, metadata: ExecutorMetadata) => {
      const payload: NodePaletteDragPayload = {
        type: metadata.canvasType,
        label: metadata.label,
        executorType: metadata.value,
        executorName: metadata.name,
      };
      event.dataTransfer.setData('application/reactflow', JSON.stringify(payload));
      event.dataTransfer.effectAllowed = 'move';
      onTileDragStart?.(event, payload);
    },
    [onTileDragStart]
  );

  const totalMatchCount = filteredEntries.length;

  return (
    <div className={styles.root}>
      {/* Search filter — small but valuable UX win when the catalog grows. */}
      <div className={styles.searchContainer}>
        <Input
          className={styles.searchInput}
          size="small"
          placeholder="Search executors..."
          value={searchQuery}
          onChange={(_e, data) => setSearchQuery(data.value)}
          contentBefore={<Search20Regular />}
          contentAfter={
            searchQuery ? (
              <DismissCircle16Regular
                role="button"
                tabIndex={0}
                aria-label="Clear search"
                onClick={() => setSearchQuery('')}
                style={{ cursor: 'pointer' }}
              />
            ) : undefined
          }
        />
      </div>

      <div className={styles.scrollArea}>
        {totalMatchCount === 0 ? (
          <div className={styles.emptyState}>
            <Text>No executors match &ldquo;{searchQuery}&rdquo;.</Text>
          </div>
        ) : (
          <Accordion
            multiple
            collapsible
            defaultOpenItems={defaultOpenItems}
            className={styles.accordion}
          >
            {TIER_ORDER.map(tier => {
              const entries = groupedFiltered[tier];
              // When searching, hide empty tiers entirely; in default state show all 6 tiers
              // (always non-empty in default catalog).
              if (entries.length === 0) return null;
              return (
                <AccordionItem
                  key={tier}
                  value={tier}
                  className={styles.accordionItem}
                >
                  <AccordionHeader className={styles.accordionHeader}>
                    <span className={styles.tierHeaderLabel}>
                      {TIER_LABEL[tier]}
                      <Badge
                        className={styles.tierBadge}
                        size="small"
                        appearance="ghost"
                        color="informative"
                      >
                        {entries.length}
                      </Badge>
                    </span>
                  </AccordionHeader>
                  <AccordionPanel>
                    <div className={styles.paletteList}>
                      {entries.map(metadata => (
                        <PaletteTile
                          key={metadata.value}
                          metadata={metadata}
                          onDragStart={handleTileDragStart}
                        />
                      ))}
                    </div>
                  </AccordionPanel>
                </AccordionItem>
              );
            })}
          </Accordion>
        )}
      </div>
    </div>
  );
}

