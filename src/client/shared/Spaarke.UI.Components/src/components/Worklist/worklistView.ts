/**
 * worklistView - what one lane shows under its current count filter. Pure: no React, no fetching.
 *
 * The route's order is the list's order: groups keep the order they arrive in, and so do the items inside a group and the
 * flattened browse set. A filter only HIDES items (the count filters of task 052 are lenses on the list the lane already
 * holds, never another query) and each group counts what it hides so its card can say so (HANDOFF section 1.4 L121-123).
 * There is no sort, reverse or re-grouping here, and no branch on a core type (D-36): a group is whatever the route says.
 *
 * Task: spaarke-ontology-platform-r1, task 059.
 */

import { filterLaneItems, resolveLaneFilterKey } from './laneCountFilters';
import type { LaneFilterKey } from './laneCountFilters';
import type { WorklistGroupData, WorklistRouteItem } from './worklistService';
import type { WorklistLane } from './types';

/** One card's worth: the group's visible items and how many of its items the filter hides. */
export interface LaneGroupView {
  /** Stable React key: the core pair, or `not-filed`. */
  key: string;
  group: WorklistGroupData;
  /** The group's items under the filter, in the route's order. */
  items: WorklistRouteItem[];
  /** The group's items the filter hides: "+n other open items on this matter, not in this filter". */
  hiddenCount: number;
}

export interface LaneView {
  lane: WorklistLane;
  /** Every item in the lane, in the route's order. The count cards always describe this full list. */
  allItems: WorklistRouteItem[];
  /** The filter in force: the one asked for, or `all` when that filter holds nothing. */
  filterKey: LaneFilterKey;
  /** Groups that still hold a visible item, in the route's order. */
  groups: LaneGroupView[];
  /** The ids of the visible items in display order: the wizard's browse set and the start button's list. */
  visibleIds: string[];
}

export function groupKey(group: WorklistGroupData): string {
  return group.core ? `${group.core.recordType}:${group.core.recordId}` : 'not-filed';
}

export function buildLaneView(
  groups: readonly WorklistGroupData[],
  lane: WorklistLane,
  requestedKey: LaneFilterKey
): LaneView {
  const allItems = groups.flatMap(g => g.items);
  const filterKey = resolveLaneFilterKey(allItems, lane, requestedKey);
  const shown = new Set(filterLaneItems(allItems, lane, filterKey).map(i => i.signalId));

  const views: LaneGroupView[] = [];
  const visibleIds: string[] = [];
  for (const group of groups) {
    const items = group.items.filter(i => shown.has(i.signalId));
    if (items.length === 0) continue;
    views.push({ key: groupKey(group), group, items, hiddenCount: group.items.length - items.length });
    for (const item of items) visibleIds.push(item.signalId);
  }
  return { lane, allItems, filterKey, groups: views, visibleIds };
}

/** "+1 other open item on this matter, not in this filter" (the type word comes from the route's label). */
export function hiddenItemsNote(
  hiddenCount: number,
  typeLabel: string | null | undefined,
  notFiled: boolean
): string | undefined {
  if (hiddenCount <= 0) return undefined;
  const noun = hiddenCount === 1 ? 'item' : 'items';
  const where = notFiled ? 'in Not filed' : `on this ${typeLabel?.trim().toLowerCase() || 'record'}`;
  return `+${hiddenCount} other open ${noun} ${where}, not in this filter`;
}
