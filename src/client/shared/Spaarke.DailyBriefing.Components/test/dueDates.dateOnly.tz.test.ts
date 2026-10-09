/** @jest-environment ../Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * Daily Briefing — event due dates are calendar dates (spaarke-ontology-platform-r1 task 098, 2026-10-05), and so is the
 * To Do it creates (task 106: sprk_todo.sprk_duedate is Date Only and refuses a timestamp).
 *
 * sprk_event's due dates are Dataverse Date Only: a notification's dueDate can be a bare "YYYY-MM-DD". Read with
 * new Date() / Date.parse that is UTC midnight — the previous day in New York — so "Add to To Do" dated the To Do a
 * day early, and the "due within N days" filter measured from the wrong instant.
 *
 * Runs in America/New_York through the @jest-environment on line 1 (a UTC runner would pass trivially).
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
import { computeDueDate } from '../src/hooks/useInlineTodoCreate';
import { filterByDueWithinDays } from '../src/services/notificationService';
import type { NotificationItem } from '../src/types/notifications';

function item(dueDate: string | null): NotificationItem {
  return {
    id: 'n-098',
    title: 'Filing',
    body: '',
    category: 'tasks-overdue',
    priority: 'normal',
    actionUrl: '',
    regardingName: 'Acme',
    regardingEntityType: 'sprk_event',
    regardingId: '00000000-0000-0000-0000-000000000098',
    isRead: false,
    isAiGenerated: false,
    createdOn: new Date().toISOString(),
    dueDate,
  };
}

describe('Date Only due dates in the Daily Briefing (task 098)', () => {
  it('the harness is behind UTC (guard is meaningful)', () => {
    expect(new Date('2026-10-02').getDate()).toBe(1);
  });

  it('computeDueDate: a bare YYYY-MM-DD dates the To Do on THAT local day', () => {
    // Task 106: the To Do's sprk_duedate is Date Only, so the value is the calendar day itself.
    expect(computeDueDate(item('2026-10-02'), new Date(2026, 9, 1, 9, 0))).toBe('2026-10-02');
  });

  it('computeDueDate: a timestamp dates the To Do on the LOCAL day of that instant (task 106)', () => {
    // 2026-10-03T01:30Z is 21:30 on Oct 2 in New York — the day this user saw. Sent as a timestamp it was HTTP 400.
    expect(computeDueDate(item('2026-10-03T01:30:00Z'))).toBe('2026-10-02');
  });

  it('computeDueDate: the +3-day default is three LOCAL days on, also in the evening (task 106)', () => {
    // 21:30 on Oct 1 in New York (01:30Z Oct 2): the UTC date is already Oct 2, the user's day is Oct 1.
    expect(computeDueDate(item(null), new Date(Date.parse('2026-10-02T01:30:00Z')))).toBe('2026-10-04');
  });

  it('filterByDueWithinDays: a bare date is measured from its LOCAL midnight', () => {
    // 2026-10-03 local midnight is 04:00Z; "now" is 2026-10-02 03:30Z (23:30 on Oct 1 in New York), so it is
    // 24.5 h away — outside a 1-day window. Read as UTC midnight (the former Date.parse) it was 20.5 h away: inside.
    const realNow = Date.now;
    Date.now = () => Date.parse('2026-10-02T03:30:00Z');
    try {
      expect(filterByDueWithinDays([item('2026-10-03')], 1)).toHaveLength(0);
      expect(filterByDueWithinDays([item('2026-10-02')], 1)).toHaveLength(1);
    } finally {
      Date.now = realNow;
    }
  });
});
