/**
 * formatRelativeTime — bucket boundaries, styles and locale (task 081 /
 * F3, F5, F9, F10, F11 + compact style).
 *
 * The zone is pinned to America/New_York (restored afterwards so other files in
 * the same jest worker are unaffected) and "now" is a fixed LOCAL instant via
 * fake timers; offsets are built from local date components so calendar-day
 * buckets are deterministic across DST.
 */

import { formatRelativeTime } from '../relativeTime';

const ORIGINAL_TZ = process.env.TZ;

/** Fixed local "now": Mon 2026-06-15 12:00 America/New_York. */
const NOW = (): Date => new Date(2026, 5, 15, 12, 0, 0);

const secondsFromNow = (s: number): string => new Date(NOW().getTime() + s * 1000).toISOString();
const daysFromNow = (d: number): string => new Date(2026, 5, 15 + d, 12, 0, 0).toISOString();

beforeAll(() => {
  process.env.TZ = 'America/New_York';
});
afterAll(() => {
  if (ORIGINAL_TZ === undefined) {
    delete process.env.TZ;
  } else {
    process.env.TZ = ORIGINAL_TZ;
  }
});
beforeEach(() => {
  jest.useFakeTimers();
  jest.setSystemTime(NOW());
});
afterEach(() => {
  jest.useRealTimers();
});

describe('formatRelativeTime', () => {
  it('runs in the pinned zone (guards the calendar-day cases)', () => {
    expect(new Date(Date.UTC(2026, 5, 15)).getHours()).toBe(20);
  });

  describe('sub-minute: "just now" in BOTH directions (F5 — clock skew never reads "in 3 seconds")', () => {
    it.each([0, 3, 30, 59])('%i s ago → "just now"', s => {
      expect(formatRelativeTime(secondsFromNow(-s))).toBe('just now');
    });
    it.each([3, 30, 59])('%i s in the future → "just now"', s => {
      expect(formatRelativeTime(secondsFromNow(s))).toBe('just now');
    });
    it('59.9 s either side is still "just now"', () => {
      expect(formatRelativeTime(new Date(NOW().getTime() - 59_900).toISOString())).toBe('just now');
      expect(formatRelativeTime(new Date(NOW().getTime() + 59_900).toISOString())).toBe('just now');
    });
  });

  describe('elapsed minutes / hours (truncated)', () => {
    it.each([
      [-60, '1 minute ago'],
      [60, 'in 1 minute'],
      [-119, '1 minute ago'],
      [-59 * 60, '59 minutes ago'],
      [-3600, '1 hour ago'],
      [3600, 'in 1 hour'],
      [-23 * 3600, '23 hours ago'],
      [23 * 3600, 'in 23 hours'],
      [-(23 * 3600 + 59 * 60), '23 hours ago'],
    ])('%i s → "%s"', (s, expected) => {
      expect(formatRelativeTime(secondsFromNow(s))).toBe(expected);
    });
  });

  describe('calendar days, weeks (rounded), months, years — past (F3)', () => {
    it.each([
      [1, 'yesterday'],
      [6, '6 days ago'],
      [7, 'last week'],
      [13, '2 weeks ago'],
      [14, '2 weeks ago'],
      [27, '4 weeks ago'],
      [28, '4 weeks ago'],
      [29, '4 weeks ago'],
      [30, 'last month'],
      [59, '2 months ago'],
      [60, '2 months ago'],
      [364, 'last year'],
      [365, 'last year'],
    ])('%i days ago → "%s"', (d, expected) => {
      expect(formatRelativeTime(daysFromNow(-d))).toBe(expected);
    });
  });

  describe('calendar days, weeks, months, years — future', () => {
    it.each([
      [1, 'tomorrow'],
      [6, 'in 6 days'],
      [7, 'next week'],
      [13, 'in 2 weeks'],
      [14, 'in 2 weeks'],
      [27, 'in 4 weeks'],
      [28, 'in 4 weeks'],
      [29, 'in 4 weeks'],
      [30, 'next month'],
      [59, 'in 2 months'],
      [60, 'in 2 months'],
      [364, 'next year'],
      [365, 'next year'],
    ])('in %i days → "%s"', (d, expected) => {
      expect(formatRelativeTime(daysFromNow(d))).toBe(expected);
    });
  });

  it('uses calendar days once ≥ 24 h has elapsed (F10): Sat 23:00 seen Mon 00:30 → "2 days ago"', () => {
    jest.setSystemTime(new Date(2026, 5, 15, 0, 30)); // Mon 00:30
    expect(formatRelativeTime(new Date(2026, 5, 13, 23, 0).toISOString())).toBe('2 days ago');
  });

  it('sub-day buckets run first (F10): yesterday 23:00 seen at 09:00 → "10 hours ago", not "yesterday"', () => {
    jest.setSystemTime(new Date(2026, 5, 15, 9, 0));
    expect(formatRelativeTime(new Date(2026, 5, 14, 23, 0).toISOString())).toBe('10 hours ago');
  });

  describe('compact style', () => {
    it.each([
      [-30, 'just now'],
      [30, 'just now'],
      [-5 * 60, '5m ago'],
      [-3 * 3600, '3h ago'],
      [5 * 60, 'in 5m'],
    ])('%i s → "%s"', (s, expected) => {
      expect(formatRelativeTime(secondsFromNow(s), { style: 'compact' })).toBe(expected);
    });
    it.each([
      [-1, '1d ago'],
      [-6, '6d ago'],
      [-13, '2w ago'],
      [-29, '4w ago'],
      [-59, '2mo ago'],
      [-365, '1y ago'],
      [3, 'in 3d'],
    ])('%i days → "%s"', (d, expected) => {
      expect(formatRelativeTime(daysFromNow(d), { style: 'compact' })).toBe(expected);
    });
  });

  describe('locale (F11)', () => {
    const originalLanguage = Object.getOwnPropertyDescriptor(window.navigator, 'language');
    afterEach(() => {
      if (originalLanguage) {
        Object.defineProperty(window.navigator, 'language', originalLanguage);
      } else {
        delete (window.navigator as unknown as { language?: string }).language;
      }
    });

    it('defaults to English even when the browser language is German', () => {
      Object.defineProperty(window.navigator, 'language', { value: 'de-DE', configurable: true });
      expect(formatRelativeTime(secondsFromNow(-5 * 60))).toBe('5 minutes ago');
    });

    it('an explicit locale is honoured', () => {
      expect(formatRelativeTime(secondsFromNow(-5 * 60), { locale: 'de' })).toBe('vor 5 Minuten');
    });

    it('an empty locale falls back to English (|| not ??)', () => {
      expect(formatRelativeTime(secondsFromNow(-5 * 60), { locale: '' })).toBe('5 minutes ago');
    });

    it('a malformed locale falls back to English instead of throwing', () => {
      expect(formatRelativeTime(secondsFromNow(-5 * 60), { locale: 'not a locale!!' })).toBe('5 minutes ago');
    });

    it('"just now" is English-only; other locales use their own "now"', () => {
      expect(formatRelativeTime(secondsFromNow(-10), { locale: 'de' })).toBe('jetzt');
    });
  });

  it('returns an unparseable input unchanged', () => {
    expect(formatRelativeTime('not-a-date')).toBe('not-a-date');
  });
});
