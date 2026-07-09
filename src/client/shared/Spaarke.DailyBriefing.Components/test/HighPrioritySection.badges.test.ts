/**
 * Unit tests for `actionToBadge` + `reasonToLabel` (HighPrioritySection.tsx)
 * — map the server-computed `action`/`reason` enums to the Fluent v9 Badge
 * style/label shown on each High Priority item card.
 *
 * Task 035 (R5 Phase B.6 hardening, per notes/inbound-from-r7/03 item 2):
 * these helpers shipped without tests in R7 W12. Added `export` to the two
 * previously-module-private functions (minimal change per task constraint;
 * no behavior change) so they can be unit tested directly.
 *
 * Date-formatted labels (`formatShortDate`/`formatRelative`) are internal
 * and not exported — this file computes the SAME `Intl.DateTimeFormat` call
 * to derive the expected fragment rather than hardcoding a locale-formatted
 * string, keeping the assertions environment-independent.
 *
 * Covers:
 *   - actionToBadge: each mapped action enum value ('Overdue', 'DueToday',
 *     'DueSoon', 'Recent') plus an unknown/undefined value falling back to
 *     null (no badge rendered).
 *   - reasonToLabel: each mapped reason enum value ('Both', 'HighPriority',
 *     'Monitor') plus an unknown/undefined value falling back to ''.
 */

import { actionToBadge, reasonToLabel } from '../src/components/HighPrioritySection';

function expectedShortDate(iso: string): string {
  return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric' }).format(new Date(iso));
}

describe('actionToBadge', () => {
  it("'Overdue' with a dueDate: danger/filled badge labelled 'Overdue · {shortDate}'", () => {
    const badge = actionToBadge('Overdue', '2026-06-01T00:00:00Z');
    expect(badge).toEqual({
      label: `Overdue · ${expectedShortDate('2026-06-01T00:00:00Z')}`,
      color: 'danger',
      appearance: 'filled',
    });
  });

  it("'Overdue' without a dueDate: falls back to the bare 'Overdue' label", () => {
    const badge = actionToBadge('Overdue');
    expect(badge).toEqual({ label: 'Overdue', color: 'danger', appearance: 'filled' });
  });

  it("'DueToday': warning/filled badge labelled 'Due today'", () => {
    const badge = actionToBadge('DueToday');
    expect(badge).toEqual({ label: 'Due today', color: 'warning', appearance: 'filled' });
  });

  it("'DueSoon' with a dueDate: informative/outline badge labelled 'Due {shortDate}'", () => {
    const badge = actionToBadge('DueSoon', '2026-07-15T00:00:00Z');
    expect(badge).toEqual({
      label: `Due ${expectedShortDate('2026-07-15T00:00:00Z')}`,
      color: 'informative',
      appearance: 'outline',
    });
  });

  it("'DueSoon' without a dueDate: falls back to 'Due soon'", () => {
    const badge = actionToBadge('DueSoon');
    expect(badge).toEqual({ label: 'Due soon', color: 'informative', appearance: 'outline' });
  });

  it("'Recent' with a modifiedOn older than 7 days: subtle/outline badge labelled 'Updated {shortDate}'", () => {
    // A fixed date far in the past guarantees the >=7-day branch of the
    // internal (non-exported) formatRelative helper, independent of "now".
    const oldModifiedOn = '2020-01-01T00:00:00Z';
    const badge = actionToBadge('Recent', undefined, oldModifiedOn);
    expect(badge).toEqual({
      label: `Updated ${expectedShortDate(oldModifiedOn)}`,
      color: 'subtle',
      appearance: 'outline',
    });
  });

  it("'Recent' without a modifiedOn: falls back to 'Recently updated'", () => {
    const badge = actionToBadge('Recent');
    expect(badge).toEqual({ label: 'Recently updated', color: 'subtle', appearance: 'outline' });
  });

  it('unknown action value falls back safely to null (no badge rendered)', () => {
    expect(actionToBadge('SomeUnknownAction')).toBeNull();
  });

  it("'None' (the widget's explicit ?? fallback for a missing/undefined action) falls back safely to null", () => {
    expect(actionToBadge('None')).toBeNull();
  });

  it('empty string action falls back safely to null', () => {
    expect(actionToBadge('')).toBeNull();
  });
});

describe('reasonToLabel', () => {
  it("'Both' → 'HighPriority + Monitor'", () => {
    expect(reasonToLabel('Both')).toBe('HighPriority + Monitor');
  });

  it("'HighPriority' → 'HighPriority'", () => {
    expect(reasonToLabel('HighPriority')).toBe('HighPriority');
  });

  it("'Monitor' → 'Monitor'", () => {
    expect(reasonToLabel('Monitor')).toBe('Monitor');
  });

  it('undefined reason falls back safely to an empty string', () => {
    expect(reasonToLabel(undefined)).toBe('');
  });

  it('unknown reason value falls back safely to an empty string', () => {
    expect(reasonToLabel('SomeUnknownReason')).toBe('');
  });
});
