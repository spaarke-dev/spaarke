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

describe('actionToBadge', () => {
  it("'Overdue' → danger/tint badge labelled 'Overdue' (word only, no date)", () => {
    expect(actionToBadge('Overdue')).toEqual({ label: 'Overdue', color: 'danger', appearance: 'tint' });
  });

  it("'DueToday' → warning/tint badge labelled 'Due today'", () => {
    expect(actionToBadge('DueToday')).toEqual({ label: 'Due today', color: 'warning', appearance: 'tint' });
  });

  it("'DueSoon' → informative/tint badge labelled 'Due soon' (word only, no date)", () => {
    expect(actionToBadge('DueSoon')).toEqual({ label: 'Due soon', color: 'informative', appearance: 'tint' });
  });

  it("'Recent' → subtle/tint badge labelled 'Recently updated'", () => {
    expect(actionToBadge('Recent')).toEqual({ label: 'Recently updated', color: 'subtle', appearance: 'tint' });
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
