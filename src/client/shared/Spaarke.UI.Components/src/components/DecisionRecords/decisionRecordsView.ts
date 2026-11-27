/**
 * decisionRecordsView - pure shaping for the Decision Record tab (task 045, D-47): the route's `decided` answer flattened to one
 * row per Decision Record, newest first, and the class filter over it. No React, no fetch.
 *
 * The route is the only source (D-15): it returns, for each item, the Decision Records the CALLER can read (a record the caller
 * cannot read is not returned). Task 226 (D-231): the route reads records first, so an item reviewed twice comes back once per
 * record, and a record that resolved no item (an email-gate record; task 235, D-234: a Record Procedure, billing or assign-firm
 * record, under its core record) comes back in its group's `records` with no item: it is listed
 * with its own summary and nothing opens it. This module never widens or invents: an item without a record is dropped here as
 * well, so a defect upstream cannot show a row with nothing behind it.
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

/** One Decision Record, the decided item it resolved (null for a record that resolved none), and its core record (if any). */
export interface DecisionRecordRow {
  item: WorklistRouteItem | null;
  decision: WorklistDecision;
  core: WorklistCore | null;
}

const decidedAt = (row: DecisionRecordRow): number => {
  const t = Date.parse(row.decision.decidedOn ?? row.item?.resolvedOn ?? '');
  return Number.isNaN(t) ? 0 : t;
};

/**
 * Every Decision Record in the answer, newest decision first (ties: the record number, the record id, then the Signal id, so the
 * order is the same on every read). The route returns items grouped by core record; this list is a history, not a worklist, so it
 * is ordered by time.
 */
export function buildDecisionRows(data: WorklistData): DecisionRecordRow[] {
  const rows: DecisionRecordRow[] = [];
  for (const group of [...data.Decide, ...data.Do]) {
    for (const item of group.items) {
      if (item.decision) rows.push({ item, decision: item.decision, core: group.core });
    }
    for (const decision of group.records ?? []) rows.push({ item: null, decision, core: group.core });
  }
  return rows.sort(
    (a, b) =>
      decidedAt(b) - decidedAt(a) ||
      (a.decision.number ?? '').localeCompare(b.decision.number ?? '') ||
      a.decision.recordId.localeCompare(b.decision.recordId) ||
      (a.item?.signalId ?? '').localeCompare(b.item?.signalId ?? '')
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

/**
 * The row's headline: the core record's name (when it has one) and the finding. D-227: the finding is the rule's short name, falling
 * back to the Signal's stored title, then the subject's name, and never the stored sentence. For a record with no item, the record's own
 * summary (task 226). Never blank.
 */
export function rowTitle(row: DecisionRecordRow): string {
  const what =
    (row.item ? (row.item.ruleShortName ?? row.item.title ?? row.item.subjectName) : row.decision.summary) ??
    'Decision';
  return row.core?.name ? `${row.core.name} - ${what}` : what;
}
