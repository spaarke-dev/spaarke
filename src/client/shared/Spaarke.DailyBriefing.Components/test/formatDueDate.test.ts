/**
 * Unit tests for formatDueDate (R2.2 Item 2).
 *
 * Verifies bucket selection (overdue / today / tomorrow / future-week /
 * future-month) and null handling. Fixed `now` is used so tests are
 * deterministic regardless of when they run.
 */

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
