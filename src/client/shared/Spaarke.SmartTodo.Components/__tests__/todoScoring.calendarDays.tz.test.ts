/** @jest-environment ../Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * To Do composite score counts CALENDAR days (spaarke-ontology-platform-r1 task 067 / D-29, FR-63).
 *
 * The urgency component used `Math.ceil` over a millisecond difference, so the same due
 * day scored differently depending on the time of day (and across a DST change), while
 * `computeDueLabel` already compared local-midnight calendar days. These tests pin "now" and
 * run in America/New_York (the @jest-environment above; assigning process.env.TZ inside a test
 * file does not reach Node, task 098), so they are hermetic on any CI runner and cross both
 * 2026 DST changes (spring forward 8 Mar, fall back 1 Nov).
 */

jest.mock('@spaarke/ui-components', () => ({
  // The REAL shared date primitives and the shared score from source (the dist entry is not
  // buildable in every worktree) - the whole module, so a newly used export is never undefined.
  ...jest.requireActual('../../Spaarke.UI.Components/src/utils/dateLocal'),
}));

import { computeDueLabel, computeTodoScore } from '../src/utils/todoScoring';

/** Urgency points per tier, written out independently of the implementation. */
const POINTS: Record<string, number> = { overdue: 100, '3d': 80, '7d': 50, '10d': 25, none: 0 };

/** The urgency points (0-100) the score gives a due instant. */
function urgencyPoints(due: Date): number {
  const { urgencyComponent } = computeTodoScore({
    sprk_todoid: 't',
    sprk_priorityscore: 50,
    sprk_effortscore: 50,
    sprk_duedate: due.toISOString(),
  });
  return Math.round(urgencyComponent / 0.3);
}

afterEach(() => jest.useRealTimers());

function setNow(y: number, m: number, d: number, h = 8, min = 0): void {
  jest.useFakeTimers();
  jest.setSystemTime(new Date(y, m - 1, d, h, min));
}

describe('urgency counts calendar days, not elapsed milliseconds', () => {
  it('the zone is America/New_York (guard is meaningful)', () => {
    expect(new Date(2026, 0, 1).getTimezoneOffset()).toBe(300);
  });

  it('due dates at different times of the same calendar day have equal urgency', () => {
    setNow(2026, 10, 9, 8, 0);
    // 3 days out: 00:00, 08:00 and 23:59 all sit in the "within 3 days" tier.
    const points = [0, 8, 23].map(h => urgencyPoints(new Date(2026, 9, 12, h, h === 23 ? 59 : 0)));
    expect(points).toEqual([80, 80, 80]);
    // 4 days out, including 00:30 (the old ceil put 3d15h at 4 days, and 3d-minutes at 3).
    const next = [1, 12, 23].map(h => urgencyPoints(new Date(2026, 9, 13, h, 0)));
    expect(next).toEqual([50, 50, 50]);
  });

  it('due today at 23:00 and tomorrow at 01:00 score by calendar day (same tier, 22 hours apart)', () => {
    setNow(2026, 10, 9, 8, 0);
    expect(urgencyPoints(new Date(2026, 9, 9, 23, 0))).toBe(80); // today
    expect(urgencyPoints(new Date(2026, 9, 10, 1, 0))).toBe(80); // tomorrow
    // The tier edge: day 3 (23:00) is still 80, day 4 (01:00) is 50 - two hours apart.
    expect(urgencyPoints(new Date(2026, 9, 12, 23, 0))).toBe(80);
    expect(urgencyPoints(new Date(2026, 9, 13, 1, 0))).toBe(50);
  });

  it('yesterday late in the evening is overdue (100) regardless of the hour now', () => {
    setNow(2026, 10, 9, 1, 0);
    expect(urgencyPoints(new Date(2026, 9, 8, 23, 0))).toBe(100);
    setNow(2026, 10, 9, 23, 0);
    expect(urgencyPoints(new Date(2026, 9, 8, 23, 0))).toBe(100);
    expect(urgencyPoints(new Date(2026, 9, 9, 0, 30))).toBe(80); // due today (earlier hour) is not overdue
  });

  it('a date-only due date is the user local day', () => {
    setNow(2026, 10, 9, 22, 0);
    const today = computeTodoScore({ sprk_todoid: 't', sprk_duedate: '2026-10-09' });
    const yesterday = computeTodoScore({ sprk_todoid: 't', sprk_duedate: '2026-10-08' });
    expect(Math.round(today.urgencyComponent / 0.3)).toBe(80);
    expect(Math.round(yesterday.urgencyComponent / 0.3)).toBe(100);
  });
});

describe('across a DST change', () => {
  it('spring forward: 3 and 4 calendar days out, with a 23-hour day between', () => {
    // Sat 7 Mar 23:30 EST. Due Wed 11 Mar 00:00 EDT is 4 calendar days but only 71.5 elapsed hours
    // (the lost hour), so the old Math.ceil(71.5/24) = 3 put it in the 80 tier.
    setNow(2026, 3, 7, 23, 30);
    expect(urgencyPoints(new Date(2026, 2, 10, 0, 0))).toBe(80); // 3 days
    expect(urgencyPoints(new Date(2026, 2, 11, 0, 0))).toBe(50); // 4 days
  });

  it('fall back: the 25-hour day does not move a due date to the next tier', () => {
    // Sat 31 Oct 00:30 EDT. Due Tue 3 Nov 00:00 EST is 3 calendar days but 72.5 elapsed hours (the
    // extra hour), so the old Math.ceil(72.5/24) = 4 put it in the 50 tier.
    setNow(2026, 10, 31, 0, 30);
    expect(urgencyPoints(new Date(2026, 10, 3, 0, 0))).toBe(80);
    expect(urgencyPoints(new Date(2026, 10, 4, 0, 0))).toBe(50);
  });
});

describe('label and score agree for every due date', () => {
  it.each([
    ['spring DST week', 2026, 3, 5],
    ['fall DST week', 2026, 10, 29],
    ['an ordinary week', 2026, 10, 9],
    ['year end', 2026, 12, 28],
  ])('%s: same tier from computeDueLabel and the urgency score at every hour', (_name, y, m, d) => {
    for (const nowHour of [0, 1, 8, 12, 23]) {
      setNow(y, m, d, nowHour, nowHour === 23 ? 59 : 15);
      for (let offset = -4; offset <= 14; offset++) {
        for (const dueHour of [0, 1, 12, 23]) {
          const due = new Date(y, m - 1, d + offset, dueHour, 30);
          const { urgency } = computeDueLabel(due);
          expect({ offset, nowHour, dueHour, points: urgencyPoints(due) }).toEqual({
            offset,
            nowHour,
            dueHour,
            points: POINTS[urgency],
          });
        }
      }
    }
  });

  it('no due date scores 0 urgency and an empty label', () => {
    expect(computeDueLabel(null).urgency).toBe('none');
    expect(computeTodoScore({ sprk_todoid: 't' }).urgencyComponent).toBe(0);
  });
});
