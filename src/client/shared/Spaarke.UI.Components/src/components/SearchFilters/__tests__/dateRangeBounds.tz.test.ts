/** @jest-environment ./jest.newYorkEnvironment.js */
/**
 * toUtcDateRangeBounds — the search API's createdAt bounds for a range of LOCAL days (#3093).
 *
 * Runs in America/New_York (line 1). The former hosts sent `${to}T23:59:59Z` (PCF: a UTC day, so an
 * evening search in New York missed the last hours of local "today") or the bare date (code page:
 * read as midnight, so the whole `to` day was excluded). The bounds are now local midnight and local
 * 23:59:59.999 as UTC instants, which pairs with the presets' local "today".
 */

import { toUtcDateRangeBounds } from '../dateRangeBounds';
import { calculatePresetRange } from '../DateRangeFilter';

describe('toUtcDateRangeBounds (New York)', () => {
  it('sends local midnight and local end of day as UTC instants, across the DST change', () => {
    expect(toUtcDateRangeBounds({ from: '2026-03-01', to: '2026-03-10' })).toEqual({
      from: '2026-03-01T05:00:00.000Z', // EST
      to: '2026-03-11T03:59:59.999Z', // EDT
    });
  });

  it('covers the whole of local "today" for a preset picked late in the evening', () => {
    // 21:00 EDT on 10 March 2026 = 01:00Z on 11 March; a document created at 20:59 local today.
    const now = new Date(2026, 2, 10, 21, 0);
    const createdAt = new Date(2026, 2, 10, 20, 59).toISOString();
    const bounds = toUtcDateRangeBounds(calculatePresetRange('last30', now));
    expect(bounds.to).toBe('2026-03-11T03:59:59.999Z');
    expect(createdAt <= bounds.to!).toBe(true);
    // The PCF's former conversion of the same local day (`${to}T23:59:59Z`) would have excluded it.
    expect(createdAt <= '2026-03-10T23:59:59Z').toBe(false);
  });

  it('leaves an open side undefined and passes an instant through unchanged', () => {
    expect(toUtcDateRangeBounds({ from: null, to: '2026-06-01T12:00:00.000Z' })).toEqual({
      from: undefined,
      to: '2026-06-01T12:00:00.000Z',
    });
    expect(toUtcDateRangeBounds(null)).toEqual({ from: undefined, to: undefined });
  });

  it('drops an unparseable value rather than sending it', () => {
    expect(toUtcDateRangeBounds({ from: 'not-a-date', to: null })).toEqual({ from: undefined, to: undefined });
  });
});
