/**
 * composeIssueLine — builds what one line displays from a Work Item's DISPLAY COLUMNS (D-23, FR-48).
 *
 * The headline is a display, not a claim, so spec section 0.3 is not engaged: it is the subject's name and nothing
 * else. It never interpolates a value into the rule sentence and never shows a witness value (sender, date): those
 * are evidence lines in the wizard (task 058). Age (Decide) and due state (Do) are computed here, client-side, in the
 * viewer's local calendar date; the tier comes from the one shared `dueUrgencyForDays` (task 081 / C-17).
 *
 * Pure: no sorting, no membership, no React.
 *
 * Task: spaarke-ontology-platform-r1, task 051.
 */

import { daysBetweenLocalMidnight, dueUrgencyForDays, parseDueDate } from '../../utils/dateLocal';
import type { DueUrgency } from '../../utils/dateLocal';
import type { WorklistItem } from './types';

/** `age` for a Decide line (neutral); otherwise the due tier of a Do line. */
export type TimingTone = 'age' | DueUrgency;

export interface IssueLineView {
  /** Never empty: the subject name, else the Signal's stored title, else a neutral placeholder. */
  headline: string;
  /** The rule's short name, or null when the policy has none. */
  ruleShortName: string | null;
  /** Age (Decide) or due state (Do). Never blank: missing data reads as a plain statement of what is missing. */
  timing: { text: string; tone: TimingTone };
}

const UNTITLED = 'Untitled item';

function clean(value: string | null | undefined): string | null {
  const t = value?.trim();
  return t ? t : null;
}

/** "today" or "Nd" — calendar days since the item was raised (never negative). */
export function formatAge(raisedOn: string | null | undefined, today: Date): string {
  const raised = parseDueDate(raisedOn);
  if (!raised) return 'Age unknown';
  const days = Math.max(0, daysBetweenLocalMidnight(raised, today));
  return days === 0 ? 'today' : `${days}d`;
}

/**
 * The due state of a Do item: "5d overdue" / "3d late" / "due today" / "due 2 Oct". The tier is the shared
 * `dueUrgencyForDays` of the calendar-day distance from the viewer's today.
 */
export function formatDueState(
  dueDate: string | null | undefined,
  today: Date,
  pastDueWording: 'overdue' | 'late' = 'overdue'
): { text: string; tone: DueUrgency } {
  const due = parseDueDate(dueDate);
  if (!due) return { text: 'No due date', tone: 'none' };
  const days = daysBetweenLocalMidnight(today, due);
  const tone = dueUrgencyForDays(days);
  if (days < 0) return { text: `${-days}d ${pastDueWording}`, tone };
  if (days === 0) return { text: 'due today', tone };
  return { text: `due ${due.toLocaleDateString(undefined, { day: 'numeric', month: 'short' })}`, tone };
}

export function composeIssueLine(item: WorklistItem, today: Date = new Date()): IssueLineView {
  const headline = clean(item.subjectName) ?? clean(item.title) ?? UNTITLED;
  const ruleShortName = clean(item.ruleShortName);
  if (item.lane === 'Do') {
    const due = formatDueState(item.dueDate, today, item.pastDueWording === 'late' ? 'late' : 'overdue');
    return { headline, ruleShortName, timing: due };
  }
  return { headline, ruleShortName, timing: { text: formatAge(item.raisedOn, today), tone: 'age' } };
}
