/**
 * computeDueLabel — the canonical due-date tier function (C-17, owner decision
 * 2026-10-03: 3/7/10 days), pinned at its boundaries (task 081 / F6 + F7).
 *
 * Every surface that colours a due date maps THIS function's `urgency` to its
 * own colours (LegalWorkspace feed card, SmartTodo Kanban/list), so the same
 * item gets the same tier everywhere. The day difference is calendar days
 * between local midnights via the shared `daysBetweenLocalMidnight` (U5), and
 * date-only values parse as LOCAL midnight; the zone is pinned to a
 * negative-offset zone so a UTC-midnight regression would be visible.
 */

const ORIGINAL_TZ = process.env.TZ;
process.env.TZ = 'America/New_York';

jest.mock('@spaarke/ui-components', () => ({
  // The REAL shared date primitives (parseDueDate, daysBetweenLocalMidnight and
  // the task-081 tier function dueUrgencyForDays) from source — the whole
  // module, so a newly used export cannot silently arrive as undefined.
  ...jest.requireActual('../../Spaarke.UI.Components/src/utils/dateLocal'),
}));

import { computeDueLabel, parseDueDate } from '../src/utils/todoScoring';

afterAll(() => {
  if (ORIGINAL_TZ === undefined) {
    delete process.env.TZ;
  } else {
    process.env.TZ = ORIGINAL_TZ;
  }
});

/** Date-only `YYYY-MM-DD` N local calendar days from 2026-10-05. */
function dateOnly(daysFromToday: number): string {
  const d = new Date(2026, 9, 5 + daysFromToday);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

describe('computeDueLabel — 3/7/10 tier boundaries (calendar days, America/New_York)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    // 9:00pm local — late in the day, where an elapsed-hours or UTC-midnight
    // reading would already be off by one.
    jest.setSystemTime(new Date(2026, 9, 5, 21, 0, 0));
  });
  afterEach(() => {
    jest.useRealTimers();
  });

  it('the zone override is in effect', () => {
    expect(new Date(Date.UTC(2026, 9, 5)).getHours()).toBe(20);
  });

  it.each([
    [-1, 'overdue', 'Overdue'],
    [0, '3d', 'Today'],
    [3, '3d', '3d'],
    [4, '7d', '4d'],
    [7, '7d', '7d'],
    [8, '10d', '8d'],
    [10, '10d', '10d'],
    [11, 'none', ''],
  ])('due in %i day(s) → urgency %s, label "%s"', (days, urgency, label) => {
    expect(computeDueLabel(parseDueDate(dateOnly(days)))).toEqual({ urgency, label });
  });

  it('no due date → none', () => {
    expect(computeDueLabel(null)).toEqual({ urgency: 'none', label: '' });
  });
});
