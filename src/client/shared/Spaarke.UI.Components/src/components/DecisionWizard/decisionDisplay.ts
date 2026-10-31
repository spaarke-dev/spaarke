/**
 * decisionDisplay - pure display helpers shared by the Confirm step and the read-only replay: a step's values as
 * label / text rows, and the sentence that names why a step was removed. No React.
 *
 * Task: spaarke-ontology-platform-r1, task 058.
 */

import type {
  DecisionAction,
  DecisionCommitFailure,
  DecisionCommitRef,
  DecisionParameter,
  DecisionPlan,
  DismissalReason,
  SuppressionStatus,
} from './decisionWizardTypes';

export interface ValueRow {
  code: string;
  label: string;
  text: string;
}

/** The text a stored or typed value shows as. Lookup / Person ids show the picked name when it is known. */
function formatValue(
  parameter: DecisionParameter | undefined,
  value: unknown,
  nameOf: (code: string) => string | null
): string {
  if (value === null || value === undefined) return '';
  // The record stores the picked id, not its name: show the name when this session picked it, else say a record was selected
  // (an id means nothing to a reader).
  if (parameter?.kind === 'Lookup' || parameter?.kind === 'Person')
    return nameOf(parameter.code) ?? '(selected record)';
  if (parameter?.kind === 'Choice') {
    const option = parameter.options?.find(o => o.value === value);
    if (option) return option.label;
  }
  if (parameter?.kind === 'Boolean') return value === true || String(value).toLowerCase() === 'true' ? 'Yes' : 'No';
  if (parameter?.kind === 'Money' && typeof value === 'number')
    return value.toLocaleString(undefined, { minimumFractionDigits: 2 });
  if (parameter?.kind === 'Date' && typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
    const [y, m, d] = value.split('-').map(Number);
    return new Date(y, m - 1, d).toLocaleDateString();
  }
  return String(value);
}

/**
 * The values of one step as rows, in the catalog's parameter order, then any value the record holds that the plan no longer
 * describes (shown under its code, so a replay never hides what was sent).
 */
export function describeValues(
  action: DecisionAction | undefined,
  values: Record<string, unknown> | null | undefined,
  nameOf: (code: string) => string | null = () => null
): ValueRow[] {
  if (!values) return [];
  const rows: ValueRow[] = [];
  const seen = new Set<string>();
  for (const p of action?.parameters ?? []) {
    if (!(p.code in values)) continue;
    const text = formatValue(p, values[p.code], nameOf);
    if (text.trim() !== '') rows.push({ code: p.code, label: p.label, text });
    seen.add(p.code);
  }
  for (const code of Object.keys(values)) {
    if (seen.has(code)) continue;
    const text = formatValue(undefined, values[code], nameOf);
    if (text.trim() !== '') rows.push({ code, label: code, text });
  }
  return rows;
}

/**
 * What dismissing will do to the (policy, core record) suppression count, from the plan's reason and task 034's count - or the
 * plain statement that it cannot be known. Never a guess: when the count could not be read this says so.
 */
export function suppressionEffect(
  suppression: SuppressionStatus | null,
  failedToRead: boolean,
  reason: DismissalReason | undefined,
  subjectName: string | null
): string {
  const subject = subjectName ?? 'this item';
  if (failedToRead || !suppression) {
    return 'The dismissal history could not be read, so the effect of this dismissal on future items is not shown.';
  }
  if (suppression.suppressedUntil) {
    const until = new Date(suppression.suppressedUntil);
    if (!Number.isNaN(until.getTime()) && until.getTime() > Date.now()) {
      return `This rule is already suppressed for ${subject} until ${until.toLocaleDateString()}.`;
    }
  }
  if (!suppression.countsTowardSuppression) return 'Dismissals of this rule do not count toward suppressing it.';
  if (!reason) return 'Choose a reason to see how this dismissal counts toward suppressing the rule.';
  if (!reason.countsTowardSuppression) return 'This reason does not count toward suppressing the rule.';
  const after = suppression.dismissalCount + 1;
  const base = `This dismissal counts: ${after} of ${suppression.threshold} in the last ${suppression.windowDays} days for ${subject}.`;
  return after >= suppression.threshold
    ? `${base} At ${suppression.threshold} the rule stops raising this for a time.`
    : base;
}

/** "Revise budget and Reassign" - a list of names in words. */
export function joinLabels(labels: readonly string[]): string {
  if (labels.length <= 1) return labels.join('');
  return `${labels.slice(0, -1).join(', ')} and ${labels[labels.length - 1]}`;
}

// ---------------------------------------------------------------------------
// Failures and warnings, in words a person can act on (no HTTP phrases, codes or ids)
// ---------------------------------------------------------------------------

/** The heading of a failed attempt: what happened to the decision, not the status line of the request. */
export function failureHeading(failure: DecisionCommitFailure): string {
  switch (failure.httpStatus) {
    case 0:
      return 'The decision may not have been recorded';
    case 403:
      return 'You cannot record this decision';
    case 404:
      return 'This item could not be found';
    case 409:
      return 'This decision is already being recorded';
    case 422:
      return 'The decision was refused';
    case 503:
      return 'Recording is unavailable right now';
    default:
      return 'The decision was not recorded';
  }
}

/** What to tell the person; the route's own sentence is used only when it has no code to translate. */
export function failureMessage(failure: DecisionCommitFailure): string {
  switch (failure.reasonCode) {
    case 'ontology.decision.review_changed':
      return 'This decision was changed after an earlier attempt that may have been partly carried out, so it was not recorded again. Put your choices back as they were and try again, or close without recording.';
    case 'ontology.decision.not_resumable':
      return 'The earlier attempt cannot be continued. Nothing more was recorded.';
    case 'ontology.decision.review_in_progress':
      return 'This decision is already being recorded. Wait a moment, then try again.';
  }
  if (failure.httpStatus === 409) return 'This decision is already being recorded. Wait a moment, then try again.';
  if (failure.httpStatus === 503) return 'Recording could not start. Nothing was written. Try again in a moment.';
  if (failure.httpStatus === 0)
    return 'The request could not be completed, so it is not known whether anything was written.';
  return failure.detail && failure.detail !== failure.title
    ? failure.detail
    : 'Something went wrong while recording this decision.';
}

const ENTITY_NOUNS: Partial<Record<string, string>> = {
  sprk_budgetrevision: 'Budget revision',
  sprk_budget: 'Budget',
  sprk_event: 'Event',
  sprk_todo: 'To Do',
  sprk_communication: 'Message',
  sprk_workassignment: 'Work assignment',
  sprk_servicerequest: 'Service request',
  sprk_decisionrecord: 'Decision Record',
};

/**
 * The writes of a failed attempt as one line each: the action they belong to ("Revise budget"), else the kind of record
 * ("Budget revision"). Never an entity name or an id.
 */
export function describeWrites(
  refs: readonly DecisionCommitRef[],
  failure: DecisionCommitFailure,
  plan: DecisionPlan
): string[] {
  const labelOf = (code: string): string | null =>
    [...plan.actions, ...plan.nextSteps].find(a => a.code === code)?.label ?? null;
  const lines: string[] = [];
  for (const ref of refs) {
    const step = failure.steps.find(s => [...s.written, ...s.possiblyWritten].some(w => w.id === ref.id));
    const line = (step && labelOf(step.code)) ?? ENTITY_NOUNS[ref.entity] ?? 'A record';
    if (!lines.includes(line)) lines.push(line);
  }
  return lines;
}

const WARNING_TEXT: Record<string, string> = {
  signal_close_not_checked: 'An item could not be checked, so it was left open.',
  signal_close_not_authorized: 'You are not allowed to close one of the other items, so it was left open.',
  signal_unreadable: 'An item could not be read, so it was left open.',
  signal_unversioned: 'An item could not be closed safely, so it was left open.',
  signal_changed: 'An item kept changing while it was being closed, so it was left as it is.',
  signal_close_failed: 'An item could not be closed and was left open.',
  signal_subject_missing: 'An item refers to a record that no longer exists, so it was left open.',
  // Task 168 (D-194): the task was created and the decision recorded; only the email did not go.
  assignee_email_not_sent: 'The task was assigned, but the email to the assignee could not be sent.',
  assignee_email_missing: 'The task was assigned, but the assignee has no email address, so no email was sent.',
  // Task 168 fix round (D-199): the assignee is emailed only when they can see the record.
  assignee_email_not_sent_no_access:
    'The task was assigned, but no email was sent: the assignee does not have access to this record.',
  assignee_email_not_sent_several_users:
    "The task was assigned, but no email was sent: the assignee's contact is linked to more than one user.",
  assignee_email_not_sent_unverified:
    'The task was assigned, but no email was sent: whether the assignee can see this record could not be checked.',
};

/** A warning from the commit answer as a sentence; a code this build does not know reads generically. */
export function describeWarning(warning: string): string {
  return (
    WARNING_TEXT[warning] ??
    (/^[a-z0-9_.]+$/.test(warning) ? 'The decision was recorded, but one follow-up step needs a look.' : warning)
  );
}
