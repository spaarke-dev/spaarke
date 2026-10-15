/**
 * dateLocal — timezone-safe parsing of date-only (`YYYY-MM-DD`) strings.
 *
 * `new Date("YYYY-MM-DD")` parses per the ECMAScript spec as **UTC midnight**.
 * In any negative-UTC-offset zone (every US zone, i.e. most of Spaarke's
 * customer base), reading back LOCAL calendar components
 * (`getFullYear`/`getMonth`/`getDate`) on that instant yields the **previous**
 * calendar day — a due date of "2026-08-17" silently becomes Aug 16 locally.
 *
 * `parseDueDate` below treats a bare `YYYY-MM-DD` value as a calendar date
 * and builds it from LOCAL midnight components directly, sidestepping the
 * UTC round-trip entirely. Full ISO timestamps (with a time component) are
 * real instants and are left to the normal `Date` constructor, unchanged.
 *
 * ## Why this file exists (spaarke-ontology-platform-r1 task 080 / C-10)
 *
 * This exact fix already existed in
 * `Spaarke.SmartTodo.Components/src/utils/todoScoring.ts`, but two
 * independent copies of the due-date parsing logic — one in
 * `Spaarke.SmartTodo.Components/src/hooks/useKanbanColumns.ts`, one in
 * `Spaarke.UI.Components/src/components/TodoDetail/TodoDetail.tsx` — never
 * received it, so a To Do's Kanban bucket and its own detail-view urgency
 * score disagreed by one calendar day in every negative-UTC-offset zone.
 *
 * `Spaarke.UI.Components` is the common dependency of both call sites
 * (`Spaarke.SmartTodo.Components` depends on `@spaarke/ui-components`, not
 * the reverse — see that package's `package.json`), so this is the only
 * location both can import from without inverting that dependency edge.
 * `todoScoring.ts`'s own composite-score FORMULA/WEIGHTS stay locked in that
 * package per `Spaarke.UI.Components/src/utils/todoScoreMappings.ts`'s own
 * doc comment — only this narrow date-parsing primitive moved.
 *
 * All three call sites now import this single function. There is no second
 * copy left to drift.
 */

/**
 * Parse an ISO date string defensively, treating a bare `YYYY-MM-DD` value as
 * a LOCAL calendar date (not a UTC instant). Returns `null` for
 * null/undefined/invalid input.
 */
export function parseDueDate(isoString: string | undefined | null): Date | null {
  if (!isoString) return null;
  const dateOnly = /^(\d{4})-(\d{2})-(\d{2})$/.exec(isoString.trim());
  if (dateOnly) {
    const dt = new Date(Number(dateOnly[1]), Number(dateOnly[2]) - 1, Number(dateOnly[3]));
    return Number.isNaN(dt.getTime()) ? null : dt;
  }
  const d = new Date(isoString);
  return Number.isNaN(d.getTime()) ? null : d;
}

/**
 * The LOCAL calendar date of `date` as `YYYY-MM-DD` — the value a Dataverse
 * Date Only column takes (spaarke-ontology-platform-r1 task 098: the Web API
 * refuses a timestamp for one with HTTP 400). Never
 * `toISOString().split('T')[0]`, which is the UTC date: after about 20:00 in
 * any US zone that is tomorrow.
 *
 * §11: the same local-components formatting already existed privately as
 * `DateRangeFilterChip.formatLocalDate` (DataGrid chip) and
 * `CalendarWorkspaceWidget.toIsoDate` (now folded into this); this is the one
 * exported copy writers use.
 */
export function formatDateOnly(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * True when `value` is a bare `YYYY-MM-DD` — the shape Dataverse returns ONLY
 * for a column whose behaviour is Date Only (UserLocal and
 * TimeZoneIndependent columns always come back with a time and `Z`).
 */
export function isDateOnlyString(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value.trim());
}

/**
 * Number of CALENDAR days between two dates, both normalized to local
 * midnight first. Positive when `b` is after `a`. Safe against DST's ±1h
 * skew (uses `Math.round`, not a raw ms division).
 */
export function daysBetweenLocalMidnight(a: Date, b: Date): number {
  const startA = new Date(a.getFullYear(), a.getMonth(), a.getDate());
  const startB = new Date(b.getFullYear(), b.getMonth(), b.getDate());
  const msPerDay = 1000 * 60 * 60 * 24;
  return Math.round((startB.getTime() - startA.getTime()) / msPerDay);
}

/**
 * Due-date urgency tier (owner decision 2026-10-03, C-17: 3/7/10 calendar days).
 *
 *   - `'overdue'` — due before today
 *   - `'3d'`      — due today through 3 days out (day 3 included)
 *   - `'7d'`      — 4 to 7 days out
 *   - `'10d'`     — 8 to 10 days out
 *   - `'none'`    — 11+ days out, or no due date
 */
export type DueUrgency = 'overdue' | '3d' | '7d' | '10d' | 'none';

/**
 * THE due-date tier function (spaarke-ontology-platform-r1 task 081 / C-17).
 * Every surface that colours a due date calls this — SmartTodo
 * (`computeDueLabel`), the LegalWorkspace feed card, and the VisualHost event
 * due-date card (which passes the tier to `@spaarke/visuals`) — so one item
 * gets one tier everywhere. Each surface keeps only its own tier → colour map,
 * all following SmartTodo's badge palette (red / dark orange / yellow / grey).
 *
 * @param days CALENDAR days from today to the due date (negative = overdue),
 *   i.e. `daysBetweenLocalMidnight(new Date(), parseDueDate(value))`. Never an
 *   elapsed-ms quotient: a date-only value parsed as UTC midnight reads one
 *   day early in every US zone.
 * @returns the tier; `null` (no due date) → `'none'`.
 */
export function dueUrgencyForDays(days: number | null): DueUrgency {
  if (days === null || Number.isNaN(days)) return 'none';
  if (days < 0) return 'overdue';
  if (days <= 3) return '3d';
  if (days <= 7) return '7d';
  if (days <= 10) return '10d';
  return 'none';
}

// ---------------------------------------------------------------------------
// To Do composite score (spaarke-ontology-platform-r1 task 067 / D-29, FR-63)
// ---------------------------------------------------------------------------

/**
 * Urgency points for a due date, 0-100: overdue 100, 0-3 days 80, 4-7 days 50,
 * 8-10 days 25, otherwise (or no due date) 0.
 *
 * THE urgency component of the To Do score. It counts CALENDAR days (local
 * midnight to local midnight, the same `daysBetweenLocalMidnight` the due label
 * uses) and takes the tier from the same `dueUrgencyForDays`, so a To Do's score
 * and its due label can never place it in different tiers. The previous
 * implementations (five copies) used `Math.ceil` over a millisecond difference,
 * so a task due today at 01:00 scored differently at 08:00 and at 23:00.
 *
 * @param now "today" - the user's local day (D-25). Injectable for tests.
 */
export function todoUrgencyRaw(dueDate: Date | null | undefined, now: Date = new Date()): number {
  if (!dueDate) return 0;
  switch (dueUrgencyForDays(daysBetweenLocalMidnight(now, dueDate))) {
    case 'overdue':
      return 100;
    case '3d':
      return 80;
    case '7d':
      return 50;
    case '10d':
      return 25;
    default:
      return 0;
  }
}

/** Breakdown of the To Do score components. */
export interface ITodoScoreBreakdown {
  /** Final composite score, 0-100, rounded and clamped. */
  todoScore: number;
  /** priority x 0.50 */
  priorityComponent: number;
  /** (100 - effort) x 0.20 */
  effortComponent: number;
  /** urgency points x 0.30 */
  urgencyComponent: number;
}

/**
 * THE To Do composite score: priority 0.50, inverted effort 0.20, urgency 0.30
 * (weights locked, `todoScoreMappings.ts`). Every board, list and detail view
 * calls this one function (task 067); there is no other copy of the formula.
 * `sprk_duedate` goes through `parseDueDate`, so a date-only value is the user's
 * local calendar day.
 */
export function computeTodoScoreBreakdown(
  todo: { sprk_priorityscore?: number | null; sprk_effortscore?: number | null; sprk_duedate?: string | null },
  now: Date = new Date()
): ITodoScoreBreakdown {
  const priorityComponent = (todo.sprk_priorityscore ?? 50) * 0.5;
  const effortComponent = (100 - (todo.sprk_effortscore ?? 50)) * 0.2;
  const urgencyComponent = todoUrgencyRaw(parseDueDate(todo.sprk_duedate), now) * 0.3;
  const raw = priorityComponent + effortComponent + urgencyComponent;
  const todoScore = Math.max(0, Math.min(100, Math.round(raw)));
  return { todoScore, priorityComponent, effortComponent, urgencyComponent };
}
