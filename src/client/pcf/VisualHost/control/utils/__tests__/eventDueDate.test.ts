/**
 * eventDueDate — DateOnly due dates under a negative-UTC-offset zone
 * (task 081 / H2) and the tier the card receives (task 081 / H1).
 *
 * Before task 081 the containers parsed `sprk_duedate` with
 * `new Date("YYYY-MM-DD")` (UTC midnight = 8pm the previous day in New York),
 * so a due date of TODAY read as 1 day overdue, and the "active date"
 * selection skipped a `sprk_duedate` of today in favour of `sprk_finalduedate`.
 * The zone is pinned BEFORE any Date-touching import so the test is hermetic.
 */

const ORIGINAL_TZ = process.env.TZ;
process.env.TZ = 'America/New_York';

import { computeEventDueDays, mapEventToCardProps, selectActiveDueDate } from '../eventDueDate';

afterAll(() => {
  if (ORIGINAL_TZ === undefined) {
    delete process.env.TZ;
  } else {
    process.env.TZ = ORIGINAL_TZ;
  }
});

/** 9:00pm local on 2026-10-05 — late in the day, where UTC-midnight parsing is off by one. */
const NOW = new Date(2026, 9, 5, 21, 0, 0);

function dateOnly(daysFromNow: number): string {
  const d = new Date(2026, 9, 5 + daysFromNow);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

describe('eventDueDate (America/New_York)', () => {
  it('the zone override is in effect', () => {
    expect(new Date('2026-10-05').getDate()).toBe(4);
  });

  it('a DateOnly due date of today is 0 days, not overdue', () => {
    const props = mapEventToCardProps({ sprk_eventid: 'e1', sprk_duedate: dateOnly(0) }, NOW);
    expect(props.daysUntilDue).toBe(0);
    expect(props.isOverdue).toBe(false);
    expect(props.urgency).toBe('3d');
    expect(props.dueDate.getDate()).toBe(5);
  });

  it('a DateOnly due date of yesterday is overdue by 1', () => {
    const props = mapEventToCardProps({ sprk_eventid: 'e1', sprk_duedate: dateOnly(-1) }, NOW);
    expect(props.daysUntilDue).toBe(-1);
    expect(props.isOverdue).toBe(true);
    expect(props.urgency).toBe('overdue');
  });

  it('the active date keeps a sprk_duedate of today (does not fall through to sprk_finalduedate)', () => {
    const active = selectActiveDueDate(dateOnly(0), dateOnly(4), NOW);
    expect(active.getDate()).toBe(5);
  });

  it('the active date falls back to sprk_finalduedate once sprk_duedate has passed', () => {
    const active = selectActiveDueDate(dateOnly(-2), dateOnly(4), NOW);
    expect(active.getDate()).toBe(9);
  });

  it.each([
    [-1, 'overdue'],
    [0, '3d'],
    [3, '3d'],
    [4, '7d'],
    [7, '7d'],
    [8, '10d'],
    [10, '10d'],
    [11, 'none'],
  ])('due in %i day(s) → urgency %s', (days, urgency) => {
    expect(computeEventDueDays(new Date(2026, 9, 5 + days), NOW).urgency).toBe(urgency);
  });
});
