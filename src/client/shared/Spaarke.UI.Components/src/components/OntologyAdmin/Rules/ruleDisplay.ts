/**
 * ruleDisplay - the words and tones the Rules list and rule page share (task 100; #43, #44).
 *
 * The BFF sends option values as the labels the Console shows; this maps them onto the generic StatusBadge tones and formats the
 * few numbers #44 allows: open items, last fired and the action rate over the version in force (H-7). The three are read
 * independently, so each has its own "could not be read" state; a number that could not be read is never shown as zero or never.
 */

import type { StatusBadgeTone } from '../../StatusBadge';
import type { RuleMetrics, RuleMetricsAvailability, RuleSummary } from './rulesService';

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
      return rule.metrics.needsALook;
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

/** What a number that could not be read says (its own state, not zero and not "never"). */
export const COULD_NOT_BE_READ = 'Could not be read';

/** The names of the numbers that could not be read, for the banner. */
export function unreadNumbers(availability: RuleMetricsAvailability): string[] {
  const names: string[] = [];
  if (!availability.openItems) {
    names.push('open items');
  }
  if (!availability.lastFired) {
    names.push('last fired');
  }
  if (!availability.actionRate) {
    names.push('action rate');
  }
  return names;
}

export function describeOpenItems(metrics: RuleMetrics, availability: RuleMetricsAvailability): string {
  return availability.openItems && metrics.openItems !== null ? String(metrics.openItems) : COULD_NOT_BE_READ;
}

/** "62% (5 of 8)", or "Too few to judge" below the minimum, or "Could not be read". */
export function describeActionRate(metrics: RuleMetrics, minimumJudged: number, availability: RuleMetricsAvailability): string {
  if (!availability.actionRate || metrics.actionRate === null) {
    return COULD_NOT_BE_READ;
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

/** The newest Signal in the window, or "None in the last N days" (the window is part of the answer), or "Could not be read". */
export function describeLastFired(metrics: RuleMetrics, availability: RuleMetricsAvailability, windowDays: number): string {
  if (!availability.lastFired) {
    return COULD_NOT_BE_READ;
  }
  return metrics.lastFiredOn === null ? `None in the last ${windowDays} days` : formatWhen(metrics.lastFiredOn);
}

/** The label of the last-fired column or fact: the window is always stated, because the number looks back only that far. */
export function lastFiredLabel(windowDays: number): string {
  return `Last fired (in the last ${windowDays} days)`;
}
