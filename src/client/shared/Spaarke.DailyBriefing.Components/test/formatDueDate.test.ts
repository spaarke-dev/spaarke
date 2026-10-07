/** @jest-environment ../Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * Unit tests for formatDueDate (R2.2 Item 2).
 *
 * Verifies bucket selection (overdue / today / tomorrow / future-week /
 * future-month) and null handling. Fixed `now` is used so tests are
 * deterministic regardless of when they run.
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
import { formatDueDate } from '../src/utils/formatDueDate';

const NOW = new Date('2026-06-20T14:00:00Z');

describe('formatDueDate', () => {
  it('returns null for null input', () => {
    expect(formatDueDate(null, NOW)).toBeNull();
  });

  it('returns null for undefined input', () => {
    expect(formatDueDate(undefined, NOW)).toBeNull();
  });

  it('returns null for empty string', () => {
    expect(formatDueDate('', NOW)).toBeNull();
  });

  it('returns null for unparseable date', () => {
    expect(formatDueDate('not-a-date', NOW)).toBeNull();
  });

  it('returns "Overdue by Nd" for past dates beyond today', () => {
    // 3 days before NOW
    expect(formatDueDate('2026-06-17T12:00:00Z', NOW)).toBe('Overdue by 3d');
  });

  it('returns "Due today" for the same calendar day', () => {
    // Same day as NOW, different time of day
    expect(formatDueDate('2026-06-20T08:00:00Z', NOW)).toBe('Due today');
    expect(formatDueDate('2026-06-20T20:00:00Z', NOW)).toBe('Due today');
  });

  it('returns "Due tomorrow" for the next calendar day', () => {
    expect(formatDueDate('2026-06-21T10:00:00Z', NOW)).toBe('Due tomorrow');
  });

  it('returns "Due in Nd" for 2-7 days out', () => {
    expect(formatDueDate('2026-06-22T10:00:00Z', NOW)).toBe('Due in 2d');
    expect(formatDueDate('2026-06-27T10:00:00Z', NOW)).toBe('Due in 7d');
  });

  it('returns short locale date for >7 days out', () => {
    // 14 days out — should be "Due {locale-formatted Mon DD}"
    const result = formatDueDate('2026-07-04T10:00:00Z', NOW);
    expect(result).toMatch(/^Due /);
    expect(result).not.toMatch(/Overdue|today|tomorrow|in \d/);
  });
});

// ---------------------------------------------------------------------------
// task 081 (F4): a DateOnly due date (`YYYY-MM-DD`, e.g. Dataverse
// `sprk_duedate`) is a calendar day. `new Date("2026-10-05")` is UTC midnight,
// i.e. 8pm on Oct 4 in New York, so before the fix a task due TODAY read
// "Overdue by 1d" and one due tomorrow read "Due today" in every US zone.
// Run in a fixed negative-offset zone; restore TZ so other files in the same
// jest worker are unaffected.
// ---------------------------------------------------------------------------

describe('formatDueDate — DateOnly values in America/New_York', () => {
  // 9:00am local on Mon 2026-10-05.
  const localNow = (): Date => new Date(2026, 9, 5, 9, 0, 0);

  it('the zone override is in effect (guards the cases below)', () => {
    expect(new Date(Date.UTC(2026, 9, 5)).getHours()).toBe(20);
  });

  it.each([
    ['2026-10-02', 'Overdue by 3d'],
    ['2026-10-04', 'Overdue by 1d'],
    ['2026-10-05', 'Due today'],
    ['2026-10-06', 'Due tomorrow'],
    ['2026-10-08', 'Due in 3d'],
    ['2026-10-12', 'Due in 7d'],
  ])('%s at 9am on 2026-10-05 → %s (never hours)', (dateOnly, expected) => {
    expect(formatDueDate(dateOnly, localNow())).toBe(expected);
  });

  it('a date-only value due today reads "Due today" late in the evening too', () => {
    expect(formatDueDate('2026-10-05', new Date(2026, 9, 5, 23, 30))).toBe('Due today');
  });
});
