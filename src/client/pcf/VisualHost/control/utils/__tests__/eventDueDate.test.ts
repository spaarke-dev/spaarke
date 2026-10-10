/** @jest-environment ../../shared/Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * eventDueDate — DateOnly due dates under a negative-UTC-offset zone
 * (task 081 / H2) and the tier the card receives (task 081 / H1).
 *
 * Before task 081 the containers parsed `sprk_duedate` with
 * `new Date("YYYY-MM-DD")` (UTC midnight = 8pm the previous day in New York),
 * so a due date of TODAY read as 1 day overdue, and the "active date"
 * selection skipped a `sprk_duedate` of today.
 * The file runs in America/New_York through the @jest-environment on line 1, so it is hermetic.
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
import { computeEventDueDays, mapEventToCardProps, selectDueDate } from '../eventDueDate';

/** 9:00pm local on 2026-10-05 — late in the day, where UTC-midnight parsing is off by one. */
const NOW = new Date(2026, 9, 5, 21, 0, 0);

function dateOnly(daysFromNow: number): string {
  const d = new Date(2026, 9, 5 + daysFromNow);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** The card props of a record that has a sprk_duedate (never null here). */
const cardOf = (record: Record<string, unknown>, now: Date) => mapEventToCardProps(record, now)!;

describe('eventDueDate (America/New_York)', () => {
  it('the zone override is in effect', () => {
    expect(new Date('2026-10-05').getDate()).toBe(4);
  });

  it('a DateOnly due date of today is 0 days, not overdue', () => {
    const props = cardOf({ sprk_eventid: 'e1', sprk_duedate: dateOnly(0) }, NOW);
    expect(props.daysUntilDue).toBe(0);
    expect(props.isOverdue).toBe(false);
    expect(props.urgency).toBe('3d');
    expect(props.dueDate.getDate()).toBe(5);
  });

  it('a DateOnly due date of yesterday is overdue by 1', () => {
    const props = cardOf({ sprk_eventid: 'e1', sprk_duedate: dateOnly(-1) }, NOW);
    expect(props.daysUntilDue).toBe(-1);
    expect(props.isOverdue).toBe(true);
    expect(props.urgency).toBe('overdue');
  });

  it('the due date is sprk_duedate, today included', () => {
    expect(selectDueDate(dateOnly(0))!.getDate()).toBe(5);
  });

  // D-63: sprk_finalduedate is informational. These two fail on the old fallback (which switched to the final due
  // date once sprk_duedate had passed, and so hid the overdue state).
  it('a passed sprk_duedate stays the due date: the card is overdue, never rescued by a later sprk_finalduedate', () => {
    const props = cardOf({ sprk_eventid: 'e1', sprk_duedate: dateOnly(-2), sprk_finalduedate: dateOnly(4) }, NOW);
    expect(props.dueDate.getDate()).toBe(3);
    expect(props.daysUntilDue).toBe(-2);
    expect(props.isOverdue).toBe(true);
  });

  it('a rescheduled task (due in future, final due long past) is not overdue', () => {
    const props = cardOf({ sprk_eventid: 'e1', sprk_duedate: dateOnly(3), sprk_finalduedate: dateOnly(-9) }, NOW);
    expect(props.dueDate.getDate()).toBe(8);
    expect(props.isOverdue).toBe(false);
  });

  it('a record with only sprk_finalduedate has no due date: it is left out, never shown as due today', () => {
    expect(mapEventToCardProps({ sprk_eventid: 'e1', sprk_finalduedate: dateOnly(-9) }, NOW)).toBeNull();
    expect(mapEventToCardProps({ sprk_eventid: 'e2' }, NOW)).toBeNull();
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
