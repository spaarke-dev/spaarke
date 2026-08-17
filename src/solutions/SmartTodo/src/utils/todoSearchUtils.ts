/**
 * todoSearchUtils — client-side text-search predicate for the Kanban's
 * expanding search bar (smart-todo-r5 UAT 2026-08-17).
 *
 * Extracted as a pure function (out of `SmartToDo.tsx`'s `displayItems`
 * memo) so the match logic — name / description / regarding-record /
 * assigned-to, case-insensitive substring — is directly unit-testable
 * without mounting the full Kanban component tree.
 *
 * History: originally a hotfix (2026-06-27) matching name+description only;
 * DEF-11 Part 3 (2026-07-04) extended it to the regarding-record display
 * name + number; the smart-todo-r5 UAT (2026-08-17) — which REPLACED the
 * structured Filter pane (task 021) with this text search — extended it
 * again to the assigned-to contact's display name, closing the gap left by
 * the removed Assigned-To typeahead category.
 *
 * @see ADR-021 Fluent UI v9 design system (not directly relevant here — no
 *      UI in this module — but the predicate backs `SearchFilter`'s
 *      consumer, `SmartToDo.tsx`)
 * @see projects/smart-todo-r5/notes/uat-filter-text-search.md
 */
import type { ITodo } from '../types/entities';

/**
 * True when `item` matches `query` on NAME, DESCRIPTION, the regarding-record
 * display name/number, OR the assigned-to contact's display name
 * (case-insensitive substring match on each field).
 *
 * An empty/whitespace-only `query` always matches (no filter applied) —
 * callers that want to skip filtering entirely should still call this (it's
 * cheap) rather than special-case empty queries themselves, so the match
 * rule lives in exactly one place.
 */
const MONTHS_LONG = [
  'january', 'february', 'march', 'april', 'may', 'june',
  'july', 'august', 'september', 'october', 'november', 'december',
];
const MONTHS_SHORT = [
  'jan', 'feb', 'mar', 'apr', 'may', 'jun',
  'jul', 'aug', 'sep', 'oct', 'nov', 'dec',
];

/**
 * Build a lowercased, whitespace-joined "search blob" of every reasonable
 * textual representation of a To Do's due date so the single search box matches
 * natural date input (UAT 2026-08-17). Matches: month long ("august"), short
 * ("aug"), "august 18", "aug 18 2026", numeric ("8/18", "8/18/2026", padded
 * "08/18/2026"), ISO ("2026-08-18"), bare day ("18") + year ("2026"), and the
 * Dataverse locale-formatted value (`dueDateFormatted`).
 *
 * Timezone-safe: parses the ISO date PARTS with a regex — never `new Date(iso)`,
 * which would shift a DATE-ONLY value by a day in negative-UTC-offset locales.
 * Returns just the formatted value (or "") when `sprk_duedate` isn't a parseable
 * ISO date.
 */
export function buildTodoDueDateSearchBlob(item: ITodo): string {
  const iso = item.sprk_duedate ?? '';
  const formatted = (item.dueDateFormatted ?? '').toLowerCase();
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!m) return formatted;

  const [, yStr, moStr, dStr] = m;
  const monthIdx = parseInt(moStr, 10) - 1;
  const dayNum = parseInt(dStr, 10);
  const monthNum = monthIdx + 1;
  const monthLong = MONTHS_LONG[monthIdx] ?? '';
  const monthShort = MONTHS_SHORT[monthIdx] ?? '';

  return [
    monthLong, monthShort,
    `${monthLong} ${dayNum}`, `${monthLong} ${dayNum} ${yStr}`,
    `${monthShort} ${dayNum}`, `${monthShort} ${dayNum} ${yStr}`,
    `${monthNum}/${dayNum}/${yStr}`, `${monthNum}/${dayNum}`,
    `${moStr}/${dStr}/${yStr}`,
    iso,
    String(dayNum), dStr, yStr,
    formatted,
  ].join(' ').toLowerCase();
}

export function matchesTodoSearchQuery(item: ITodo, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;

  const name = (item.sprk_name ?? '').toLowerCase();
  const desc = (item.sprk_description ?? '').toLowerCase();
  const regardingName = (item.sprk_regardingrecordname ?? '').toLowerCase();
  const regardingNumber = (item.sprk_regardingrecordnumber ?? '').toLowerCase();
  const assignedTo = (item.assignedToName ?? '').toLowerCase();
  const dueDate = buildTodoDueDateSearchBlob(item);

  return (
    name.includes(q) ||
    desc.includes(q) ||
    regardingName.includes(q) ||
    regardingNumber.includes(q) ||
    assignedTo.includes(q) ||
    dueDate.includes(q)
  );
}
