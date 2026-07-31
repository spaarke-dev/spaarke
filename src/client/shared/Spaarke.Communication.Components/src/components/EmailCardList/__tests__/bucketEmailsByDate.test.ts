/**
 * bucketEmailsByDate.test.ts
 *
 * Deterministic unit tests for the pure date-bucketing helper (email-
 * communication-solution-r5 — Outlook-style date dividers). `now` is injected
 * explicitly so no clock mocking is required. Boundaries under test are the
 * LOCAL-calendar-day definitions documented on `classifyBucket`.
 */
import { bucketEmailsByDate } from '../EmailCardList';
import { EMAIL_COMMUNICATION_TYPE, type EmailCardItem } from '../EmailCardList.types';

// Fixed reference clock: Wed 2026-07-15, mid-month so "This Week" stays inside July.
const NOW = new Date(2026, 6, 15, 10, 0, 0); // month index 6 = July (local time)

function makeItem(id: string, date: string | Date): EmailCardItem {
  return {
    id,
    from: `${id}@example.com`,
    subject: `Subject ${id}`,
    preview: `Preview ${id}`,
    date,
    isUnread: false,
    communicationType: EMAIL_COMMUNICATION_TYPE,
  };
}

/** Build a local-time Date offset by whole days from NOW's calendar day. */
function daysAgo(n: number): Date {
  return new Date(2026, 6, 15 - n, 9, 0, 0);
}

describe('bucketEmailsByDate', () => {
  it('places items in Today / Yesterday / This Week / This Month / Older with correct labels + membership', () => {
    const items: EmailCardItem[] = [
      makeItem('today-a', daysAgo(0)),
      makeItem('today-b', new Date(2026, 6, 15, 23, 59, 0)), // later same day
      makeItem('yesterday', daysAgo(1)),
      makeItem('thisWeek-a', daysAgo(2)),
      makeItem('thisWeek-b', daysAgo(6)), // still within last 7 calendar days
      makeItem('thisMonth', daysAgo(9)), // older than a week, same month (July)
      makeItem('older', new Date(2026, 5, 20, 9, 0, 0)), // June → previous month
    ];

    const buckets = bucketEmailsByDate(items, NOW);

    expect(buckets.map(b => b.label)).toEqual(['Today', 'Yesterday', 'This Week', 'This Month', 'Older']);
    expect(buckets.map(b => b.key)).toEqual(['today', 'yesterday', 'thisWeek', 'thisMonth', 'older']);

    const byKey = Object.fromEntries(buckets.map(b => [b.key, b.items.map(i => i.id)]));
    expect(byKey.today).toEqual(['today-a', 'today-b']);
    expect(byKey.yesterday).toEqual(['yesterday']);
    expect(byKey.thisWeek).toEqual(['thisWeek-a', 'thisWeek-b']);
    expect(byKey.thisMonth).toEqual(['thisMonth']);
    expect(byKey.older).toEqual(['older']);
  });

  it('omits empty buckets entirely', () => {
    const items: EmailCardItem[] = [
      makeItem('t1', daysAgo(0)),
      makeItem('o1', new Date(2026, 0, 1, 9, 0, 0)), // January → Older
    ];

    const buckets = bucketEmailsByDate(items, NOW);

    expect(buckets.map(b => b.key)).toEqual(['today', 'older']);
    expect(buckets.find(b => b.key === 'yesterday')).toBeUndefined();
    expect(buckets.find(b => b.key === 'thisWeek')).toBeUndefined();
    expect(buckets.find(b => b.key === 'thisMonth')).toBeUndefined();
  });

  it('preserves incoming order within a bucket (no re-sort beyond bucketing)', () => {
    const items: EmailCardItem[] = [
      makeItem('a', new Date(2026, 6, 15, 8, 0, 0)),
      makeItem('b', new Date(2026, 6, 15, 20, 0, 0)),
      makeItem('c', new Date(2026, 6, 15, 12, 0, 0)),
    ];

    const [today] = bucketEmailsByDate(items, NOW);
    expect(today.items.map(i => i.id)).toEqual(['a', 'b', 'c']);
  });

  it('sinks unparseable dates into Older (robustness)', () => {
    const items: EmailCardItem[] = [makeItem('bad', 'not-a-date')];
    const buckets = bucketEmailsByDate(items, NOW);
    expect(buckets.map(b => b.key)).toEqual(['older']);
    expect(buckets[0].items.map(i => i.id)).toEqual(['bad']);
  });

  it('returns an empty array for no items', () => {
    expect(bucketEmailsByDate([], NOW)).toEqual([]);
  });
});
