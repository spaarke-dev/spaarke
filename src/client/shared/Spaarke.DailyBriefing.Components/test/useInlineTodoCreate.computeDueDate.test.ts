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
  const NOW = new Date('2026-06-20T14:00:00Z');

  it('uses the item dueDate verbatim when present and parseable', () => {
    const item = makeItem('2026-06-25T17:00:00Z');
    expect(computeDueDate(item, NOW)).toBe('2026-06-25T17:00:00.000Z');
  });

  it('falls back to +3 days at 17:00 local when item.dueDate is null', () => {
    const item = makeItem(null);
    const result = computeDueDate(item, NOW);
    const resultDate = new Date(result);
    const expected = new Date(NOW);
    expected.setDate(expected.getDate() + 3);
    expected.setHours(17, 0, 0, 0);
    // Match to the second (avoid millisecond drift between expected/actual)
    expect(Math.abs(resultDate.getTime() - expected.getTime())).toBeLessThan(1000);
  });

  it('falls back to +3 days when item.dueDate is empty string', () => {
    const item = makeItem('');
    const result = computeDueDate(item, NOW);
    expect(result).not.toBe('');
    expect(new Date(result).getTime()).toBeGreaterThan(NOW.getTime());
  });

  it('falls back to +3 days when item.dueDate is unparseable', () => {
    const item = makeItem('not-a-date');
    const result = computeDueDate(item, NOW);
    expect(result).not.toBe('not-a-date');
    expect(new Date(result).getTime()).toBeGreaterThan(NOW.getTime());
  });
});
