/** @jest-environment ./jest.newYorkEnvironment.js */
/**
 * DateRangeFilter presets — timezone regression guard (#3093).
 *
 * Both former copies (SemanticSearch code page and PCF) built preset dates with
 * `toISOString().split('T')[0]`, the UTC date: at 23:30 on 10 March in New York that is
 * 11 March, so "This year" / "Last 30 days" ended tomorrow. The shared filter uses the LOCAL
 * calendar (`formatDateOnly`).
 *
 * Runs in America/New_York through the @jest-environment on line 1 (assigning process.env.TZ in a
 * test file does not reach Node — see dateLocal.formatDateOnly.tz.test.ts).
 */

import { calculatePresetRange, detectActivePreset } from '../DateRangeFilter';

// 23:30 EDT on 10 March 2026 = 03:30Z on 11 March.
const lateEvening = () => new Date(2026, 2, 10, 23, 30);

describe('calculatePresetRange — the LOCAL calendar, not the UTC date', () => {
  it('ends today, not tomorrow, late in the evening', () => {
    const now = lateEvening();
    expect(now.toISOString().slice(0, 10)).toBe('2026-03-11'); // the date the former copies used
    expect(calculatePresetRange('thisYear', now)).toEqual({ from: '2026-01-01', to: '2026-03-10' });
    expect(calculatePresetRange('last7', now)).toEqual({ from: '2026-03-03', to: '2026-03-10' });
    expect(calculatePresetRange('last30', now)).toEqual({ from: '2026-02-08', to: '2026-03-10' });
    expect(calculatePresetRange('last90', now)).toEqual({ from: '2025-12-10', to: '2026-03-10' });
    expect(calculatePresetRange('lastYear', now)).toEqual({ from: '2025-01-01', to: '2025-12-31' });
    expect(calculatePresetRange('off', now)).toEqual({ from: null, to: null });
  });

  it('recognises its own presets late in the evening', () => {
    const now = lateEvening();
    expect(detectActivePreset({ from: '2026-01-01', to: '2026-03-10' }, now)).toBe('thisYear');
  });
});
