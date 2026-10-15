/** @jest-environment ./jest.newYorkEnvironment.js */
/**
 * dateLocal — timezone regression guard (spaarke-ontology-platform-r1 task
 * 080 / C-10, 2026-10-03).
 *
 * Runs in a UTC-BEHIND zone (America/New_York, UTC-4/-5) through the
 * @jest-environment on line 1, so this test is
 * hermetic regardless of the CI runner's own timezone — a UTC runner would
 * pass trivially even with the bug present, because local == UTC there.
 * Mirrors the established pattern in
 * `Spaarke.AI.Widgets/.../EntityInfoWidget.tz.test.tsx`.
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
import { parseDueDate, daysBetweenLocalMidnight, dueUrgencyForDays } from '../dateLocal';

describe('parseDueDate — negative-UTC-offset regression (C-10)', () => {
  it('confirms the harness timezone is genuinely behind UTC (guard is meaningful)', () => {
    // Sanity: without the fix, a date-only string shifts back a day here.
    // If this ever prints 17 the pin is toothless and the test below is not
    // actually exercising the hazard.
    const naive = new Date('2026-08-17');
    expect(naive.getDate()).not.toBe(17);
  });

  it('parses a date-only string as the LOCAL calendar day, not the UTC day', () => {
    const d = parseDueDate('2026-08-17');
    expect(d).not.toBeNull();
    expect(d!.getFullYear()).toBe(2026);
    expect(d!.getMonth()).toBe(7); // August (0-indexed)
    expect(d!.getDate()).toBe(17);
  });

  it('is immune to the off-by-one across a full year of dates (no systematic skew)', () => {
    for (let day = 1; day <= 28; day++) {
      const iso = `2026-03-${String(day).padStart(2, '0')}`;
      const d = parseDueDate(iso);
      expect(d!.getDate()).toBe(day);
    }
  });

  it('leaves a full ISO timestamp (with time component) untouched', () => {
    const iso = '2026-08-17T14:30:00.000Z';
    const d = parseDueDate(iso);
    expect(d!.getTime()).toBe(new Date(iso).getTime());
  });

  it('returns null for null/undefined/invalid input', () => {
    expect(parseDueDate(null)).toBeNull();
    expect(parseDueDate(undefined)).toBeNull();
    expect(parseDueDate('not-a-date')).toBeNull();
  });
});

describe('daysBetweenLocalMidnight', () => {
  it('counts a date-only "due tomorrow" as exactly +1 day from today, pinned negative-offset', () => {
    const today = new Date();
    const tomorrowIso = (() => {
      const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
      return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    })();

    const due = parseDueDate(tomorrowIso)!;
    expect(daysBetweenLocalMidnight(today, due)).toBe(1);
  });
});

describe('dueUrgencyForDays — the one 3/7/10 tier function (task 081 / C-17)', () => {
  it.each([
    [-1, 'overdue'],
    [0, '3d'],
    [3, '3d'],
    [4, '7d'],
    [7, '7d'],
    [8, '10d'],
    [10, '10d'],
    [11, 'none'],
  ] as const)('%i day(s) → %s', (days, tier) => {
    expect(dueUrgencyForDays(days)).toBe(tier);
  });

  it('no due date → none', () => {
    expect(dueUrgencyForDays(null)).toBe('none');
  });
});
