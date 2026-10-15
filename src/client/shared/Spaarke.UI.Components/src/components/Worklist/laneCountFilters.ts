/**
 * laneCountFilters — the per-lane count filters of the worklist, as data for the EXISTING
 * `WorkspaceShell/MetricCardRow` + `WorkspaceShell/MetricCard` (D-24; never `StatTiles`, never a new card component).
 *
 * A lane's cards split the lane BY WORK TYPE so the counts add up (W-8): every item has exactly one work type, so the
 * type cards sum to the lane total. An item whose work type is missing or does not belong to its lane is counted in a
 * trailing "Other" card instead of being dropped, so the sum holds on bad data too (Missing-never-blank).
 *
 * The cards are LENSES ON MEMBERSHIP, not queues (FR-34): choosing one narrows the list the host already shows
 * (`filterLaneItems`); it never opens another list. Choosing the selected card again returns to All. A type whose count
 * falls to 0 is disabled by the card, and a selection that points at it resolves back to All (`resolveLaneFilterKey`),
 * so a user can never be left on an empty lens.
 *
 * Pure: no React state, no fetching, no sorting, no membership. The host owns `selectedKey`.
 *
 * Task: spaarke-ontology-platform-r1, task 052.
 */

import { daysBetweenLocalMidnight, parseDueDate } from '../../utils/dateLocal';
import type { MetricCardConfig } from '../WorkspaceShell/types';
import type { WorkType, WorklistItem, WorklistLane } from './types';

/** `all` (the whole lane), a work type, or `other` (items with no valid work type for the lane). */
export type LaneFilterKey = 'all' | 'other' | WorkType;

/** The work types of each lane and their labels, in display order (`sprk_policy.sprk_worktype`, task 007). */
export const LANE_WORK_TYPES: Readonly<Record<WorklistLane, ReadonlyArray<{ key: WorkType; label: string }>>> = {
  Decide: [
    { key: 'askOutsideCounsel', label: 'Ask outside counsel' },
    { key: 'approveOrRebudget', label: 'Approve or rebudget' },
    { key: 'chaseReply', label: 'Chase a reply' },
  ],
  Do: [
    { key: 'finishOrReschedule', label: 'Finish or reschedule' },
    { key: 'comingDue', label: 'Coming due' },
    { key: 'chaseResponse', label: 'Chase a response' },
  ],
};

/** One filter and the items it holds. */
export interface LaneCountBucket {
  key: LaneFilterKey;
  label: string;
  items: WorklistItem[];
}

function laneItems(items: readonly WorklistItem[], lane: WorklistLane): WorklistItem[] {
  return items.filter(i => i.lane === lane);
}

/**
 * The lane's filters: `all` first, then one per work type of the lane, then `other` only when something is in it. The
 * non-`all` buckets partition the lane, so their counts sum to the `all` count.
 */
export function bucketLaneItems(items: readonly WorklistItem[], lane: WorklistLane): LaneCountBucket[] {
  const inLane = laneItems(items, lane);
  const types = LANE_WORK_TYPES[lane];
  const typeKeys = new Set<string>(types.map(t => t.key));
  const buckets: LaneCountBucket[] = [{ key: 'all', label: 'All', items: inLane }];
  for (const t of types) {
    buckets.push({ key: t.key, label: t.label, items: inLane.filter(i => i.workType === t.key) });
  }
  const other = inLane.filter(i => !i.workType || !typeKeys.has(i.workType));
  if (other.length > 0) buckets.push({ key: 'other', label: 'Other', items: other });
  return buckets;
}

/** The key in force: `key` when that filter still holds an item, else `all`. */
export function resolveLaneFilterKey(
  items: readonly WorklistItem[],
  lane: WorklistLane,
  key: LaneFilterKey
): LaneFilterKey {
  if (key === 'all') return 'all';
  const bucket = bucketLaneItems(items, lane).find(b => b.key === key);
  return bucket && bucket.items.length > 0 ? key : 'all';
}

/** The items the lane shows under a filter: a subset of the list the host already has, in the order it was given. */
export function filterLaneItems(
  items: readonly WorklistItem[],
  lane: WorklistLane,
  key: LaneFilterKey
): WorklistItem[] {
  const resolved = resolveLaneFilterKey(items, lane, key);
  const bucket = bucketLaneItems(items, lane).find(b => b.key === resolved);
  return bucket ? bucket.items : [];
}

/** Decide: "oldest 4 days" (calendar days since the oldest `raisedOn`). Do: "3 past due". Otherwise no note. */
function noteFor(bucket: LaneCountBucket, lane: WorklistLane, today: Date): string | undefined {
  if (bucket.items.length === 0) return undefined;
  if (lane === 'Decide') {
    let oldest: number | null = null;
    for (const item of bucket.items) {
      const raised = parseDueDate(item.raisedOn);
      if (!raised) continue;
      const days = Math.max(0, daysBetweenLocalMidnight(raised, today));
      if (oldest === null || days > oldest) oldest = days;
    }
    if (oldest === null) return undefined;
    if (oldest === 0) return 'oldest today';
    return oldest === 1 ? 'oldest 1 day' : `oldest ${oldest} days`;
  }
  let pastDue = 0;
  for (const item of bucket.items) {
    const due = parseDueDate(item.dueDate);
    if (due && daysBetweenLocalMidnight(today, due) < 0) pastDue += 1;
  }
  return pastDue > 0 ? `${pastDue} past due` : undefined;
}

export interface BuildLaneCountCardsOptions {
  lane: WorklistLane;
  /** The lane's full list (items of the other lane are ignored). */
  items: readonly WorklistItem[];
  /** The host's current filter; resolved to `all` when that filter is empty. */
  selectedKey: LaneFilterKey;
  /** Called with the NEW filter: the chosen key, or `all` when the selected card is chosen again. */
  onSelectKey: (key: LaneFilterKey) => void;
  /**
   * Items finished today in this lane (they are no longer in `items`). When given, the All card shows
   * "n of m done today" with m = done + still open.
   */
  doneToday?: number;
  /** Viewer-local "today"; defaults to now. Injected by tests. */
  today?: Date;
}

/** `MetricCardConfig[]` for `<MetricCardRow layout="wide" cards={...} />`. */
export function buildLaneCountCards(options: BuildLaneCountCardsOptions): MetricCardConfig[] {
  const { lane, items, selectedKey, onSelectKey, doneToday, today = new Date() } = options;
  const buckets = bucketLaneItems(items, lane);
  const resolved = resolveLaneFilterKey(items, lane, selectedKey);
  return buckets.map(bucket => {
    const count = bucket.items.length;
    const note = noteFor(bucket, lane, today);
    const card: MetricCardConfig = {
      id: `${lane}-${bucket.key}`,
      label: bucket.label,
      ariaLabel: `${bucket.label}, ${count} ${lane} ${count === 1 ? 'item' : 'items'}${note ? `, ${note}` : ''}`,
      value: count,
      selected: bucket.key === resolved,
      note,
      // `all` stays enabled so there is always a way back; a type with nothing in it is disabled.
      disableWhenEmpty: bucket.key !== 'all',
      onClick: () => onSelectKey(bucket.key === resolved && bucket.key !== 'all' ? 'all' : bucket.key),
    };
    if (bucket.key === 'all' && doneToday !== undefined) {
      card.progress = { done: doneToday, total: doneToday + count };
    }
    return card;
  });
}
