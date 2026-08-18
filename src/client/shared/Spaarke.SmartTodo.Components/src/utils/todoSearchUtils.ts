/**
 * todoSearchUtils — the SINGLE client-side To Do search predicate, shared by
 * BOTH SmartTodo surfaces (§11 — one predicate, not two):
 *   - the standalone SmartTodo Code Page (`sprk_smarttodo`) top-bar search, and
 *   - the `SmartTodoWidget` search box in the SpaarkeAi workspace.
 *
 * Hoisted into `@spaarke/smart-todo-components` (smart-todo-r5 follow-up,
 * 2026-08-17) from `src/solutions/SmartTodo/src/utils/todoSearchUtils.ts` after
 * the two surfaces were found to have DIVERGED search: the Code Page matched
 * name/description/regarding/assigned-to/**date**, while the widget's own inline
 * `items.filter` matched only name + description (so the widget silently lacked
 * the blended date search). Both now import THIS module.
 *
 * Typed against a minimal STRUCTURAL interface (`TodoSearchableFields`, all
 * optional) rather than a concrete `ITodo`/`ITodoRecord`, so every To Do item
 * shape across the surfaces satisfies it without coupling this util to one type.
 *
 * @see ADR-021 (no UI here — backs both surfaces' search predicate)
 */

/** The (all-optional) fields the search predicate reads off a To Do item. */
export interface TodoSearchableFields {
  sprk_name?: string;
  sprk_description?: string;
  sprk_regardingrecordname?: string;
  sprk_regardingrecordnumber?: string;
  assignedToName?: string;
  /** ISO date (e.g. "2026-08-18"). */
  sprk_duedate?: string;
  /** Dataverse locale-formatted value of sprk_duedate (e.g. "8/18/2026"). */
  dueDateFormatted?: string;
}

const MONTHS_LONG = [
  'january',
  'february',
  'march',
  'april',
  'may',
  'june',
  'july',
  'august',
  'september',
  'october',
  'november',
  'december',
];
const MONTHS_SHORT = ['jan', 'feb', 'mar', 'apr', 'may', 'jun', 'jul', 'aug', 'sep', 'oct', 'nov', 'dec'];

/**
 * Build a lowercased, whitespace-joined "search blob" of every reasonable
 * textual representation of a To Do's due date so a single search box matches
 * natural date input. Matches: month long ("august"), short ("aug"),
 * "august 18", "aug 18 2026", numeric ("8/18", "8/18/2026", padded
 * "08/18/2026"), ISO ("2026-08-18"), bare day ("18") + year ("2026"), and the
 * Dataverse locale-formatted value (`dueDateFormatted`).
 *
 * Timezone-safe: parses the ISO date PARTS with a regex — never `new Date(iso)`,
 * which would shift a DATE-ONLY value by a day in negative-UTC-offset locales.
 * Returns just the formatted value (or "") when `sprk_duedate` isn't a parseable
 * ISO date.
 */
export function buildTodoDueDateSearchBlob(item: TodoSearchableFields): string {
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
    monthLong,
    monthShort,
    `${monthLong} ${dayNum}`,
    `${monthLong} ${dayNum} ${yStr}`,
    `${monthShort} ${dayNum}`,
    `${monthShort} ${dayNum} ${yStr}`,
    `${monthNum}/${dayNum}/${yStr}`,
    `${monthNum}/${dayNum}`,
    `${moStr}/${dStr}/${yStr}`,
    iso,
    String(dayNum),
    dStr,
    yStr,
    formatted,
  ]
    .join(' ')
    .toLowerCase();
}

/**
 * True when `item` matches `query` on NAME, DESCRIPTION, the regarding-record
 * display name/number, the assigned-to contact's display name, OR the due date
 * (case-insensitive substring match on each field).
 *
 * An empty/whitespace-only `query` always matches (no filter applied).
 */
export function matchesTodoSearchQuery(item: TodoSearchableFields, query: string): boolean {
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
