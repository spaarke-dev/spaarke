/**
 * MatterCard — the ONE worklist row component (FR-25, prototype finding 10). One card per CORE record (a matter, a
 * project, a work assignment, a service request or a future type, D-34/D-36) holding one IssueLine per Work Item; a
 * Do-lane "Not filed" card (core = null) holds the reader's items that have no core record (D-35).
 *
 * Data-driven variants: every Work Item shape (cross-source, threshold, SLA, Do-lane) and every core type renders
 * through this one component; only the data differs, and there is no per-type branch. The card does not sort, filter or
 * pick membership: items arrive in rank order and are rendered in that order (row-contract requirement 2). A line
 * resolves to an object, a Signal id plus its core record, and raises `onOpenItem` with the item id and the visible
 * list for the decision wizard (tasks 058 / 059).
 *
 * Per ASSISTANT-UI-ELEMENT-CRITERIA this is a CARD: a persistent act-on item whose clickable region is the line.
 * Fluent v9 tokens only (ADR-021): light and dark both follow the host FluentProvider. No React 18-only API (the
 * shared library is consumed by React 16/17 PCF hosts too).
 *
 * Task: spaarke-ontology-platform-r1, task 051.
 */

import * as React from 'react';
import { Caption1, Text, makeStyles, mergeClasses, tokens } from '@fluentui/react-components';
import { composeIssueLine } from './composeIssueLine';
import { IssueLine } from './IssueLine';
import type { OpenItemEvent, WorklistCore, WorklistItem } from './types';

export interface MatterCardProps {
  /** The core record this card groups under; `null` renders the "Not filed" card (D-35). */
  core: WorklistCore | null;
  /** The card's items, ALREADY in rank order. Rendered in the order given; never re-sorted. */
  items: readonly WorklistItem[];
  /**
   * Ids of every item visible in the list, in order (the wizard's browse set). The host supplies it because a card only
   * knows its own items; defaults to this card's items.
   */
  visibleList?: readonly string[];
  /** Raised when any line is clicked or activated with Enter or Space. */
  onOpenItem?: (event: OpenItemEvent) => void;
  /** A quiet sentence under the lines, for example "+1 other open item on this matter, not in this filter". */
  note?: string;
  /** Today in the viewer's local calendar; defaults to now. */
  today?: Date;
  className?: string;
}

const useStyles = makeStyles({
  card: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    backgroundColor: tokens.colorNeutralBackground1,
    borderTopWidth: tokens.strokeWidthThin,
    borderRightWidth: tokens.strokeWidthThin,
    borderBottomWidth: tokens.strokeWidthThin,
    borderLeftWidth: tokens.strokeWidthThin,
    borderTopStyle: 'solid',
    borderRightStyle: 'solid',
    borderBottomStyle: 'solid',
    borderLeftStyle: 'solid',
    borderTopColor: tokens.colorNeutralStroke2,
    borderRightColor: tokens.colorNeutralStroke2,
    borderBottomColor: tokens.colorNeutralStroke2,
    borderLeftColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow2,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
  },
  head: {
    display: 'flex',
    alignItems: 'baseline',
    columnGap: tokens.spacingHorizontalS,
    minWidth: 0,
    flexWrap: 'wrap',
  },
  name: { minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis' },
  muted: { color: tokens.colorNeutralForeground3 },
  list: { listStyleType: 'none', marginTop: '0', marginBottom: '0', paddingLeft: '0', paddingRight: '0' },
  note: { color: tokens.colorNeutralForeground3, paddingLeft: tokens.spacingHorizontalS },
});

/** Heading text from the row data only: the route supplies label, name and number (catalog-driven, D-36). */
function coreHeading(core: WorklistCore | null): { name: string; detail: string } {
  if (core === null) return { name: 'Not filed', detail: 'No matter or project' };
  const label = core.typeLabel?.trim() || null;
  return {
    name: core.name?.trim() || (label ? `Unnamed ${label.toLowerCase()}` : 'Unnamed record'),
    detail: [label, core.number?.trim() || null].filter((p): p is string => !!p).join(' · '),
  };
}

export const MatterCard: React.FC<MatterCardProps> = ({
  core,
  items,
  visibleList,
  onOpenItem,
  note,
  today,
  className,
}) => {
  const styles = useStyles();
  if (items.length === 0) return null;

  const { name, detail } = coreHeading(core);
  const ids = visibleList ?? items.map(i => i.signalId);
  const coreRef = core ? { recordType: core.recordType, recordId: core.recordId } : null;
  const now = today ?? new Date();

  const open = (itemId: string) => onOpenItem?.({ itemId, core: coreRef, visibleList: [...ids] });

  return (
    <section
      className={mergeClasses(styles.card, className)}
      aria-label={`${name}, ${items.length} ${items.length === 1 ? 'item' : 'items'}`}
      data-testid="matter-card"
      data-core-type={core?.recordType ?? 'not-filed'}
      data-core-id={core?.recordId ?? ''}
    >
      <div className={styles.head}>
        <Text weight="semibold" className={styles.name}>
          {name}
        </Text>
        {detail && (
          <Caption1 className={styles.muted} data-testid="matter-card-detail">
            {detail}
          </Caption1>
        )}
      </div>
      <ul className={styles.list}>
        {items.map(item => (
          <li key={item.signalId}>
            <IssueLine itemId={item.signalId} view={composeIssueLine(item, now)} onOpen={open} />
          </li>
        ))}
      </ul>
      {note && <Caption1 className={styles.note}>{note}</Caption1>}
    </section>
  );
};

MatterCard.displayName = 'MatterCard';
