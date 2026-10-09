/**
 * Unit tests for computeDueDate (R2.2 Item 3).
 *
 * Verifies the default due-date strategy used when adding a notification to
 * To Do: prefer the item's own dueDate when present, else fall back to
 * +3 calendar days at 17:00 local.
 */

import { computeDueDate } from '../src/hooks/useInlineTodoCreate';
import type { NotificationItem } from '../src/types/notifications';

function makeItem(dueDate: string | null): NotificationItem {
  return {
    id: 'n-test',
    title: 'Test todo',
    body: '',
    category: 'tasks-overdue',
    priority: 'normal',
    actionUrl: '',
    regardingName: 'Acme',
    regardingEntityType: 'sprk_matter',
    regardingId: '00000000-0000-0000-0000-000000000001',
    isRead: false,
    isAiGenerated: false,
    createdOn: new Date().toISOString(),
    dueDate,
  };
}

describe('computeDueDate', () => {
  const NOW = new Date(2026, 5, 20, 10, 0, 0); // 2026-06-20 10:00 local

  /** The local calendar day of `d` as YYYY-MM-DD (independent of the module under test). */
  function localDay(d: Date): string {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  it('a bare YYYY-MM-DD item dueDate is kept as that calendar day', () => {
    expect(computeDueDate(makeItem('2026-06-25'), NOW)).toBe('2026-06-25');
  });

  it('a timestamp item dueDate becomes the LOCAL calendar day of that instant (never a timestamp — task 106)', () => {
    const ts = '2026-06-25T17:00:00Z';
    expect(computeDueDate(makeItem(ts), NOW)).toBe(localDay(new Date(ts)));
  });

  it('falls back to +3 local calendar days when item.dueDate is null', () => {
    expect(computeDueDate(makeItem(null), NOW)).toBe('2026-06-23');
  });

  it('falls back to +3 days when item.dueDate is empty string', () => {
    expect(computeDueDate(makeItem(''), NOW)).toBe('2026-06-23');
  });

  it('falls back to +3 days when item.dueDate is unparseable', () => {
    expect(computeDueDate(makeItem('not-a-date'), NOW)).toBe('2026-06-23');
  });

  it('always returns the Date Only wire shape YYYY-MM-DD', () => {
    for (const due of ['2026-06-25', '2026-06-25T17:00:00Z', null, '', 'not-a-date']) {
      expect(computeDueDate(makeItem(due), NOW)).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    }
  });
});
