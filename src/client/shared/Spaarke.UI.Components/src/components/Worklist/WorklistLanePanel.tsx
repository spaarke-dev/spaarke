/**
 * WorklistLanePanel - one lane of the worklist: heading with its count and its start button, the lane's count filters, the
 * "Showing n of m" line, and one MatterCard per core record in the route's order.
 *
 * Not exported from the barrel: it is part of `WorklistWidget`, like `IssueLine` is part of `MatterCard`.
 *
 * - The heading count and the count cards always describe the lane's FULL list; a filter only hides cards (HANDOFF section 1.4 L121).
 * - The start button walks the lane's visible (filtered) list (W-2).
 * - The aggregate row is passed in by the host; it is shown only at the top of the Do lane, outside both counts, and only while
 *   the Do lane has no filter on (W-11).
 *
 * Task: spaarke-ontology-platform-r1, task 059.
 */

import * as React from 'react';
import { Button, CounterBadge, Link, Text, makeStyles, tokens } from '@fluentui/react-components';
import { PlayRegular } from '@fluentui/react-icons';
import { MetricCardRow } from '../WorkspaceShell/MetricCardRow';
import { MatterCard } from './MatterCard';
import { bucketLaneItems, buildLaneCountCards } from './laneCountFilters';
import type { LaneFilterKey } from './laneCountFilters';
import type { OpenItemEvent, WorklistLane } from './types';
import { hiddenItemsNote } from './worklistView';
import type { LaneView } from './worklistView';

export interface WorklistLanePanelProps {
  lane: WorklistLane;
  view: LaneView;
  /** Items finished today in this lane; undefined while that set is not available (no progress line then). */
  doneToday?: number;
  today: Date;
  onSelectKey: (key: LaneFilterKey) => void;
  onOpenItem: (event: OpenItemEvent) => void;
  onStart: () => void;
  /** The email aggregate row; rendered at the top of the lane when no filter is on. */
  aggregateRow?: React.ReactNode;
}

const LANE_COPY: Record<WorklistLane, { heading: string; start: string; empty: string }> = {
  Decide: { heading: 'Decide', start: 'Start review', empty: 'Nothing to decide.' },
  Do: { heading: 'Do', start: 'Work through these', empty: 'Nothing to do.' },
};

const useStyles = makeStyles({
  lane: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalM },
  head: { display: 'flex', alignItems: 'center', columnGap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  spacer: { flexGrow: 1 },
  showing: { display: 'flex', alignItems: 'baseline', columnGap: tokens.spacingHorizontalS },
  muted: { color: tokens.colorNeutralForeground3 },
  list: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS },
});

export const WorklistLanePanel: React.FC<WorklistLanePanelProps> = ({
  lane,
  view,
  doneToday,
  today,
  onSelectKey,
  onOpenItem,
  onStart,
  aggregateRow,
}) => {
  const styles = useStyles();
  const copy = LANE_COPY[lane];
  const total = view.allItems.length;
  const shown = view.visibleIds.length;
  const filtered = view.filterKey !== 'all';
  const filterLabel = filtered
    ? (bucketLaneItems(view.allItems, lane).find(b => b.key === view.filterKey)?.label ?? '')
    : '';

  const cards = buildLaneCountCards({
    lane,
    items: view.allItems,
    selectedKey: view.filterKey,
    onSelectKey,
    doneToday,
    today,
  });

  return (
    <section className={styles.lane} aria-label={`${copy.heading} lane`} data-testid={`worklist-lane-${lane}`}>
      <div className={styles.head}>
        <Text as="h2" size={500} weight="semibold">
          {copy.heading}
        </Text>
        <CounterBadge
          count={total}
          showZero
          appearance="filled"
          color="informative"
          aria-label={`${total} ${lane} ${total === 1 ? 'item' : 'items'}`}
          data-testid={`lane-count-${lane}`}
        />
        <span className={styles.spacer} />
        <Button
          appearance="primary"
          icon={<PlayRegular />}
          disabled={shown === 0}
          onClick={onStart}
          data-testid={`lane-start-${lane}`}
        >
          {copy.start}
        </Button>
      </div>

      {total > 0 && <MetricCardRow layout="wide" ariaLabel={`${copy.heading} filters`} cards={cards} />}

      {filtered && (
        <div className={styles.showing} role="status" data-testid={`lane-showing-${lane}`}>
          <Text size={200} className={styles.muted}>
            Showing {shown} of {total} - {filterLabel}.
          </Text>
          <Link as="button" onClick={() => onSelectKey('all')}>
            Show all
          </Link>
        </div>
      )}

      {aggregateRow && !filtered && <div data-testid="worklist-aggregate-row">{aggregateRow}</div>}

      <div className={styles.list}>
        {view.groups.map(g => (
          <MatterCard
            key={g.key}
            core={g.group.core}
            items={g.items}
            visibleList={view.visibleIds}
            onOpenItem={onOpenItem}
            note={hiddenItemsNote(g.hiddenCount, g.group.core?.typeLabel, g.group.core === null)}
            today={today}
          />
        ))}
        {total === 0 && (
          <Text className={styles.muted} data-testid={`lane-empty-${lane}`}>
            {/* The aggregate row above is not a Do item, so a bare "Nothing to do." would contradict it. */}
            {lane === 'Do' && aggregateRow && !filtered ? 'No other items to do.' : copy.empty}
          </Text>
        )}
      </div>
    </section>
  );
};

WorklistLanePanel.displayName = 'WorklistLanePanel';
