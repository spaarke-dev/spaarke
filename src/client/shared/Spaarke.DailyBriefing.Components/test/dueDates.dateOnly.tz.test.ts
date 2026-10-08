/** @jest-environment ../Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * Daily Briefing — event due dates are calendar dates (spaarke-ontology-platform-r1 task 098, 2026-10-05).
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
// filterByDueWithinDays (notificationService.ts) is not tested here: ontology-platform-r1 task 010 / C-1 deleted
// that module as wholly dead (no live consumer), so task 098's fix to it is moot on this branch.
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
    const due = new Date(computeDueDate(item('2026-10-02'), new Date(2026, 9, 1, 9, 0)));
    expect([due.getFullYear(), due.getMonth(), due.getDate()]).toEqual([2026, 9, 2]);
  });

  it('computeDueDate: a timestamp is still used verbatim', () => {
    expect(computeDueDate(item('2026-06-25T17:00:00Z'))).toBe('2026-06-25T17:00:00.000Z');
  });
});
