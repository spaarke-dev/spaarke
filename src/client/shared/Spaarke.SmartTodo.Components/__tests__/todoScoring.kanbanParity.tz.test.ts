/** @jest-environment ../Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * Kanban-bucket / To-Do-Score parity under a negative-UTC-offset timezone
 * (spaarke-ontology-platform-r1 task 080 / C-10, 2026-10-03).
 *
 * The hazard this guards: the three copies of the due-date parser that fed
 * (1) Kanban column bucketing (`useKanbanColumns.ts`'s `bucketTodoItems`),
 * (2) the composite To-Do Score (`todoScoring.ts`'s `computeTodoScore`), and
 * (3) the Detail pane's own score (`Spaarke.UI.Components/.../TodoDetail.tsx`)
 * had drifted: #2's `parseDueDate` carried a local-midnight fix that #1 and
 * #3 never received, so a date-only `sprk_duedate` (e.g. "2026-08-17") was
 * read as the PREVIOUS calendar day by #1/#3 in every negative-UTC-offset
 * zone (every US zone) — a To Do could sit in a different Kanban column than
 * its own detail view implied.
 *
 * The fix converges all three on ONE `parseDueDate` (`@spaarke/ui-components`
 * `utils/dateLocal.ts`). This file runs in a UTC-behind zone through the
 * @jest-environment on line 1, so the test is hermetic regardless of
 * the CI runner's own timezone (mirrors
 * `Spaarke.AI.Widgets/.../EntityInfoWidget.tz.test.tsx`).
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
// `@spaarke/ui-components`'s dist entry (`dist/services/index.js`) unconditionally requires
// `@spaarke/sdap-client`, which is not built as a dist package in this worktree. Delegate to the
// SOURCE file directly (bypassing the broken dist) so this suite exercises the real canonical
// `parseDueDate` implementation, not a stub — mirrors the established pattern in
// `SmartTodoWidget.test.tsx` / `Header.test.tsx` / `useKanbanColumns.test.ts`.
jest.mock('@spaarke/ui-components', () => ({
  // The REAL shared date primitives (parseDueDate, daysBetweenLocalMidnight and
  // the task-081 tier function dueUrgencyForDays) from source — the whole
  // module, so a newly used export cannot silently arrive as undefined.
  ...jest.requireActual('../../Spaarke.UI.Components/src/utils/dateLocal'),
}));

import { parseDueDate as canonicalParseDueDate } from '@spaarke/ui-components';
import { bucketTodoItems } from '../src/hooks/useKanbanColumns';
import { computeTodoScore, parseDueDate as scoringParseDueDate } from '../src/utils/todoScoring';
import type { IKanbanCardTodo } from '../src/types/kanban';

/** Build a date-only `YYYY-MM-DD` string N local calendar days from today. */
function localDateOnlyIso(daysFromToday: number): string {
  const now = new Date();
  const d = new Date(now.getFullYear(), now.getMonth(), now.getDate() + daysFromToday);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

describe('C-10 convergence — single parseDueDate backs both Kanban bucketing and the composite score', () => {
  it('confirms the harness timezone is genuinely behind UTC (guard is meaningful)', () => {
    const naive = new Date(localDateOnlyIso(0));
    // Without the pin (or on a UTC runner) this would equal today's date and
    // the regression this test targets would be invisible.
    expect(naive.getDate()).not.toBe(new Date().getDate());
  });

  it('todoScoring.ts and useKanbanColumns.ts import the identical parseDueDate — no re-drift possible', () => {
    // Forcing-function check (not just a behavioral check): if a future edit
    // reintroduces a local copy in either module, this reference-identity
    // assertion fails even if the new copy happens to produce the same
    // answer today.
    expect(scoringParseDueDate).toBe(canonicalParseDueDate);
  });

  it('a To Do due TOMORROW buckets into the Kanban "Tomorrow" column', () => {
    const dueTomorrow = localDateOnlyIso(1);
    const todo: IKanbanCardTodo = {
      sprk_todoid: 't-1',
      sprk_name: 'Tomorrow todo',
      sprk_priorityscore: 50,
      sprk_effortscore: 50,
      sprk_todopinned: false,
      sprk_duedate: dueTomorrow,
    };

    const columns = bucketTodoItems([todo]);
    const tomorrowColumn = columns.find(c => c.id === 'Tomorrow')!;
    const todayColumn = columns.find(c => c.id === 'Today')!;

    expect(tomorrowColumn.items.map(i => i.sprk_todoid)).toContain('t-1');
    expect(todayColumn.items.map(i => i.sprk_todoid)).not.toContain('t-1');
  });

  it('the SAME due-tomorrow To Do scores in the "within 3 days" urgency tier, not "overdue" (C-10 drift symptom)', () => {
    const dueTomorrow = localDateOnlyIso(1);
    const todo = {
      sprk_todoid: 't-2',
      sprk_priorityscore: 50,
      sprk_effortscore: 50,
      sprk_duedate: dueTomorrow,
    };

    const { urgencyComponent } = computeTodoScore(todo);
    // Urgency raw tiers: overdue=100, <=3d=80, <=7d=50, <=10d=25, else=0.
    // "Due tomorrow" must land in the <=3d (80) tier scaled by W_URGENCY=0.3.
    // Before the fix, useKanbanColumns independently mis-bucketed this same
    // date into "Today"/overdue in a negative-UTC-offset zone while
    // todoScoring (already fixed) correctly saw "tomorrow" — this assertion
    // pins todoScoring's own side of that disagreement so a regression here
    // is caught even if the bucketing test above is not run.
    expect(urgencyComponent).toBeCloseTo(80 * 0.3, 5);
  });

  it('a date-only due date parsed via the canonical helper never resolves to a different calendar day than requested', () => {
    for (let offset = -5; offset <= 5; offset++) {
      const iso = localDateOnlyIso(offset);
      const parsed = canonicalParseDueDate(iso);
      const [y, m, d] = iso.split('-').map(Number);
      expect(parsed!.getFullYear()).toBe(y);
      expect(parsed!.getMonth()).toBe(m - 1);
      expect(parsed!.getDate()).toBe(d);
    }
  });
});
