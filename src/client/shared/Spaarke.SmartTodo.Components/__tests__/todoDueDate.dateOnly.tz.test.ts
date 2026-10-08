/** @jest-environment ../Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * To Do due dates are calendar dates (spaarke-ontology-platform-r1 task 106, owner decision D-41).
 *
 * sprk_todo.sprk_duedate is Dataverse Date Only since 2026-10-08 (docs/data-model/sprk_todo-date-columns.md): the Web
 * API returns "YYYY-MM-DD", refuses a timestamp on write (HTTP 400), and compares a timestamp `$filter` literal by its
 * UTC date (probed live).
 *
 *   - Quick-add (SmartTodoWidget and the SmartTodo Code Page, one builder): sent toISOString() of 23:59 LOCAL on the
 *     picked day — HTTP 400 now, and the NEXT day in UTC from 20:00 Eastern before.
 *   - LegalWorkspace "Open Tasks" overdue badge: `sprk_duedate lt <now ISO>` — the UTC date, so from 20:00 Eastern a
 *     to-do due today counted as overdue.
 *
 * Runs at 21:00 on 2026-10-05 in America/New_York (01:00Z on 10-06), where the UTC date is already tomorrow.
 */

// America/New_York is set by the @jest-environment above (a UTC runner would pass trivially; the guard below fails).
jest.mock('@spaarke/ui-components', () => ({
  ...jest.requireActual('../../Spaarke.UI.Components/src/utils/dateLocal'),
}));

import { buildQuickAddTodoPayload } from '../src/utils/quickAddTodoPayload';
import { QUICK_SUMMARY_CARDS } from '../../../../solutions/LegalWorkspace/src/components/QuickSummary/quickSummaryConfig';

const NOW = new Date(Date.parse('2026-10-06T01:00:00Z'));

describe('To Do due dates are calendar dates (task 106, America/New_York 21:00)', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    jest.setSystemTime(NOW);
  });
  afterEach(() => {
    jest.useRealTimers();
  });

  it('the harness is behind UTC and it is still Oct 5 locally (guard is meaningful)', () => {
    expect(new Date('2026-10-02').getDate()).toBe(1);
    expect(new Date().getDate()).toBe(5);
  });

  describe('quick-add create payload (SmartTodoWidget + SmartTodo Code Page)', () => {
    it('sends the picked day as "YYYY-MM-DD" — the only shape a Date Only column accepts', () => {
      const payload = buildQuickAddTodoPayload({ title: 'zz-106 quick add', dueDate: '2026-10-05' });
      expect(payload.sprk_duedate).toBe('2026-10-05');
    });

    it('omits the due date when none was picked, and binds Assigned To by the PascalCase navigation property', () => {
      const payload = buildQuickAddTodoPayload({ title: 'zz-106 quick add', dueDate: '', assignedToContactId: 'c-1' });
      expect(payload).toEqual({ sprk_name: 'zz-106 quick add', 'sprk_AssignedTo@odata.bind': '/contacts(c-1)' });
    });
  });

  describe('LegalWorkspace Quick Summary "Open Tasks" overdue badge', () => {
    const openTasks = QUICK_SUMMARY_CARDS.find(c => c.id === 'open-tasks')!;
    const ctx = { userId: 'u-106', scope: 'my' } as unknown as Parameters<NonNullable<typeof openTasks.badgeFilter>>[0];

    it('counts a to-do as overdue only when it was due before the LOCAL today', () => {
      const filter = openTasks.badgeFilter!(ctx);
      expect(filter).toContain('sprk_duedate lt 2026-10-05');
    });

    it('never compares the Date Only column with a timestamp (read by its UTC date: tomorrow, here)', () => {
      expect(openTasks.badgeFilter!(ctx)).not.toMatch(/sprk_duedate lt \d{4}-\d{2}-\d{2}T/);
    });
  });
});
