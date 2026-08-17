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
export function matchesTodoSearchQuery(item: ITodo, query: string): boolean {
  const q = query.trim().toLowerCase();
  if (!q) return true;

  const name = (item.sprk_name ?? '').toLowerCase();
  const desc = (item.sprk_description ?? '').toLowerCase();
  const regardingName = (item.sprk_regardingrecordname ?? '').toLowerCase();
  const regardingNumber = (item.sprk_regardingrecordnumber ?? '').toLowerCase();
  const assignedTo = (item.assignedToName ?? '').toLowerCase();

  return (
    name.includes(q) ||
    desc.includes(q) ||
    regardingName.includes(q) ||
    regardingNumber.includes(q) ||
    assignedTo.includes(q)
  );
}
