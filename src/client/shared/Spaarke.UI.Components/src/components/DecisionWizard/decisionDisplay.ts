/**
 * decisionDisplay - pure display helpers shared by the Confirm step and the read-only replay: a step's values as
 * label / text rows, and the sentence that names why a step was removed. No React.
 *
 * Task: spaarke-ontology-platform-r1, task 058.
 */

import type { DecisionAction, DecisionParameter, DismissalReason, SuppressionStatus } from './decisionWizardTypes';

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
