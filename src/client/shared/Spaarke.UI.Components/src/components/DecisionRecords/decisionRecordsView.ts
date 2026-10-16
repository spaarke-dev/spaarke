/**
 * decisionRecordsView - pure shaping for the Decision Record tab (task 045, D-47): the route's `decided` answer flattened to one
 * row per decided item, newest first, and the class filter over it. No React, no fetch.
 *
 * The route is the only source (D-15): it returns, for each item, the Decision Record the CALLER can read (a record the caller
 * cannot read is not returned, so its item is not either). This module never widens or invents: an item without a record is
 * dropped here as well, so a defect upstream cannot show a row with nothing behind it.
 */

import type { RecordClass } from '../ConsoleKit';
import type { WorklistCore } from '../Worklist/types';
import type { WorklistData, WorklistDecision, WorklistRouteItem } from '../Worklist/worklistService';

/** The filter choices, in the order shown. */
export type DecisionClassFilter = 'all' | RecordClass;

export const DECISION_CLASS_FILTERS: readonly { key: DecisionClassFilter; label: string }[] = [
  { key: 'all', label: 'All' },
  { key: 'Judgement', label: 'Judgements' },
  { key: 'Routine', label: 'Routine' },
  { key: 'Dismissal', label: 'Dismissals' },
];

/** One decided item with its record, and the core record (if any) it was filed under. */
export interface DecisionRecordRow {
  item: WorklistRouteItem;
  decision: WorklistDecision;
  core: WorklistCore | null;
}

const decidedAt = (row: DecisionRecordRow): number => {
  const t = Date.parse(row.decision.decidedOn ?? row.item.resolvedOn ?? '');
  return Number.isNaN(t) ? 0 : t;
};

/**
 * Every decided item in the answer, newest decision first (ties: the record number, then the Signal id, so the order is the same on
 * every read). The route returns items grouped by core record; this list is a history, not a worklist, so it is ordered by time.
 */
export function buildDecisionRows(data: WorklistData): DecisionRecordRow[] {
  const rows: DecisionRecordRow[] = [];
  for (const group of [...data.Decide, ...data.Do]) {
    for (const item of group.items) {
      if (item.decision) rows.push({ item, decision: item.decision, core: group.core });
    }
  }
  return rows.sort(
    (a, b) =>
      decidedAt(b) - decidedAt(a) ||
      (a.decision.number ?? '').localeCompare(b.decision.number ?? '') ||
      a.item.signalId.localeCompare(b.item.signalId)
  );
}

/** Only the rows of one record class; `all` keeps every row. A record whose class is unknown matches `all` only. */
export function filterDecisionRows(
  rows: readonly DecisionRecordRow[],
  filter: DecisionClassFilter
): DecisionRecordRow[] {
  return filter === 'all' ? [...rows] : rows.filter(r => r.decision.recordClass === filter);
}

/** How many rows each filter would show (for the filter labels). */
export function countByFilter(rows: readonly DecisionRecordRow[]): Record<DecisionClassFilter, number> {
  return {
    all: rows.length,
    Judgement: filterDecisionRows(rows, 'Judgement').length,
    Routine: filterDecisionRows(rows, 'Routine').length,
    Dismissal: filterDecisionRows(rows, 'Dismissal').length,
  };
}

/** The row's headline: the core record's name (when it has one) and what the item was. Never blank. */
export function rowTitle(row: DecisionRecordRow): string {
  const what = row.item.title ?? row.item.ruleShortName ?? row.item.subjectName ?? 'Decision';
  return row.core?.name ? `${row.core.name} - ${what}` : what;
}
