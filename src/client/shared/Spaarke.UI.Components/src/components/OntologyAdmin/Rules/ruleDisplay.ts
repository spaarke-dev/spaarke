/**
 * ruleDisplay - the words and tones the Rules list and rule page share (task 100; #43, #44).
 *
 * The BFF sends option values as the labels the Console shows; this maps them onto the generic StatusBadge tones and formats the
 * few numbers #44 allows: open items, last fired and the action rate over the version in force (H-7).
 */

import type { StatusBadgeTone } from '../../StatusBadge';
import type { RuleMetrics, RuleSummary } from './rulesService';

export type RuleFilter = 'all' | 'decide' | 'do' | 'needsALook';

export const RULE_FILTERS: ReadonlyArray<{ readonly id: RuleFilter; readonly label: string }> = [
  { id: 'all', label: 'All' },
  { id: 'decide', label: 'Decide' },
  { id: 'do', label: 'Do' },
  { id: 'needsALook', label: 'Needs a look' },
];

/** Whether a rule belongs under a filter. "Needs a look" is the BFF's own call (rate below one half with enough judged). */
export function matchesFilter(rule: RuleSummary, filter: RuleFilter): boolean {
  switch (filter) {
    case 'all':
      return true;
    case 'decide':
      return rule.lane === 'Decide';
    case 'do':
      return rule.lane === 'Do';
    case 'needsALook':
      return rule.metrics?.needsALook === true;
  }
}

export function stateTone(state: string): StatusBadgeTone {
  switch (state) {
    case 'On':
      return 'success';
    case 'Retired':
      return 'critical';
    default:
      return 'neutral';
  }
}

export function severityTone(severity: string): StatusBadgeTone {
  switch (severity) {
    case 'Critical':
      return 'critical';
    case 'Warning':
      return 'warning';
    default:
      return 'info';
  }
}

/** "62% (5 of 8)", or "Too few to judge" below the minimum, or a dash when the numbers could not be read. */
export function describeActionRate(metrics: RuleMetrics | null, minimumJudged: number): string {
  if (metrics === null) {
    return '-';
  }
  const { acted, dismissed, rate, tooFewToJudge } = metrics.actionRate;
  if (tooFewToJudge || rate === null) {
    return `Too few to judge (needs ${minimumJudged})`;
  }
  return `${Math.round(rate * 100)}% (${acted} of ${acted + dismissed})`;
}

/** A readable local date and time, or a dash for nothing. An unparseable value is shown as it came rather than hidden. */
export function formatWhen(iso: string | null): string {
  if (iso === null) {
    return '-';
  }
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

export function describeLastFired(metrics: RuleMetrics | null): string {
  if (metrics === null) {
    return '-';
  }
  return metrics.lastFiredOn === null ? 'Never' : formatWhen(metrics.lastFiredOn);
}
