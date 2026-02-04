/**
 * formatFilter Utility Tests
 * Task 008: Unit tests for EventCalendarFilter PCF control
 *
 * Tests cover:
 * - toIsoDateString: Date to ISO string conversion
 * - parseIsoDate: ISO string to Date conversion
 * - formatSingleDateFilter: Single date filter object creation
 * - formatRangeFilter: Range filter object creation (with sorting)
 * - formatClearFilter: Clear filter object creation
 * - formatFilterOutputToJson: Filter to JSON string
 * - Convenience functions: createSingleFilterJson, createRangeFilterJson, createClearFilterJson
 *
 * @see formatFilter.ts
 */

import {
  toIsoDateString,
  parseIsoDate,
  formatSingleDateFilter,
  formatRangeFilter,
  formatClearFilter,
  formatFilterOutputToJson,
  createSingleFilterJson,
  createRangeFilterJson,
  createClearFilterJson,
} from '../formatFilter';

describe('formatFilter utilities', () => {
  describe('toIsoDateString', () => {
    it('converts Date to ISO string (YYYY-MM-DD)', () => {
      const date = new Date(2026, 1, 10); // February 10, 2026
      expect(toIsoDateString(date)).toBe('2026-02-10');
    });

    it('pads single-digit months with zero', () => {
      const date = new Date(2026, 0, 15); // January 15, 2026
      expect(toIsoDateString(date)).toBe('2026-01-15');
    });

    it('pads single-digit days with zero', () => {
      const date = new Date(2026, 2, 5); // March 5, 2026
      expect(toIsoDateString(date)).toBe('2026-03-05');
    });

    it('handles December correctly', () => {
      const date = new Date(2025, 11, 25); // December 25, 2025
      expect(toIsoDateString(date)).toBe('2025-12-25');
    });

    it('handles leap year February 29', () => {
      const date = new Date(2024, 1, 29); // February 29, 2024 (leap year)
      expect(toIsoDateString(date)).toBe('2024-02-29');
    });

    it('handles first day of year', () => {
      const date = new Date(2026, 0, 1); // January 1, 2026
      expect(toIsoDateString(date)).toBe('2026-01-01');
    });

    it('handles last day of year', () => {
      const date = new Date(2026, 11, 31); // December 31, 2026
      expect(toIsoDateString(date)).toBe('2026-12-31');
    });
  });

  describe('parseIsoDate', () => {
    it('parses ISO string to Date', () => {
      const date = parseIsoDate('2026-02-10');
      expect(date.getFullYear()).toBe(2026);
      expect(date.getMonth()).toBe(1); // February (0-indexed)
      expect(date.getDate()).toBe(10);
    });

    it('parses dates with leading zeros', () => {
      const date = parseIsoDate('2026-01-05');
      expect(date.getMonth()).toBe(0); // January
      expect(date.getDate()).toBe(5);
    });

    it('creates Date at midnight (local time)', () => {
      const date = parseIsoDate('2026-06-15');
      expect(date.getHours()).toBe(0);
      expect(date.getMinutes()).toBe(0);
      expect(date.getSeconds()).toBe(0);
    });

    it('roundtrips with toIsoDateString', () => {
      const original = '2026-07-22';
      const date = parseIsoDate(original);
      expect(toIsoDateString(date)).toBe(original);
    });
  });

  describe('formatSingleDateFilter', () => {
    it('creates single date filter from Date object', () => {
      const date = new Date(2026, 1, 10);
      const filter = formatSingleDateFilter(date);

      expect(filter).toEqual({
        type: 'single',
        date: '2026-02-10',
      });
    });

    it('creates single date filter from ISO string', () => {
      const filter = formatSingleDateFilter('2026-02-10');

      expect(filter).toEqual({
        type: 'single',
        date: '2026-02-10',
      });
    });

    it('preserves exact string date', () => {
      const filter = formatSingleDateFilter('2026-12-25');
      expect(filter.date).toBe('2026-12-25');
    });
  });

  describe('formatRangeFilter', () => {
    it('creates range filter with dates in order', () => {
      const filter = formatRangeFilter('2026-02-01', '2026-02-07');

      expect(filter).toEqual({
        type: 'range',
        start: '2026-02-01',
        end: '2026-02-07',
      });
    });

    it('sorts dates chronologically (start before end)', () => {
      const filter = formatRangeFilter('2026-02-07', '2026-02-01');

      expect(filter).toEqual({
        type: 'range',
        start: '2026-02-01',
        end: '2026-02-07',
      });
    });

    it('handles Date objects', () => {
      const date1 = new Date(2026, 1, 7);
      const date2 = new Date(2026, 1, 1);
      const filter = formatRangeFilter(date1, date2);

      expect(filter.start).toBe('2026-02-01');
      expect(filter.end).toBe('2026-02-07');
    });

    it('handles mixed Date and string', () => {
      const filter = formatRangeFilter(new Date(2026, 1, 15), '2026-02-10');

      expect(filter.start).toBe('2026-02-10');
      expect(filter.end).toBe('2026-02-15');
    });

    it('handles same date for start and end', () => {
      const filter = formatRangeFilter('2026-02-10', '2026-02-10');

      expect(filter).toEqual({
        type: 'range',
        start: '2026-02-10',
        end: '2026-02-10',
      });
    });

    it('handles cross-month ranges', () => {
      const filter = formatRangeFilter('2026-01-28', '2026-02-05');

      expect(filter).toEqual({
        type: 'range',
        start: '2026-01-28',
        end: '2026-02-05',
      });
    });

    it('handles cross-year ranges', () => {
      const filter = formatRangeFilter('2026-01-15', '2025-12-20');

      expect(filter.start).toBe('2025-12-20');
      expect(filter.end).toBe('2026-01-15');
    });
  });

  describe('formatClearFilter', () => {
    it('creates clear filter object', () => {
      const filter = formatClearFilter();

      expect(filter).toEqual({
        type: 'clear',
      });
    });

    it('returns consistent object structure', () => {
      const filter1 = formatClearFilter();
      const filter2 = formatClearFilter();

      expect(filter1).toEqual(filter2);
      expect(filter1.type).toBe('clear');
    });
  });

  describe('formatFilterOutputToJson', () => {
    it('serializes single date filter', () => {
      const filter = formatSingleDateFilter('2026-02-10');
      const json = formatFilterOutputToJson(filter);

      expect(json).toBe('{"type":"single","date":"2026-02-10"}');
    });

    it('serializes range filter', () => {
      const filter = formatRangeFilter('2026-02-01', '2026-02-07');
      const json = formatFilterOutputToJson(filter);

      expect(json).toBe('{"type":"range","start":"2026-02-01","end":"2026-02-07"}');
    });

    it('serializes clear filter', () => {
      const filter = formatClearFilter();
      const json = formatFilterOutputToJson(filter);

      expect(json).toBe('{"type":"clear"}');
    });

    it('produces valid JSON', () => {
      const filter = formatRangeFilter('2026-02-01', '2026-02-07');
      const json = formatFilterOutputToJson(filter);

      expect(() => JSON.parse(json)).not.toThrow();
      expect(JSON.parse(json)).toEqual(filter);
    });
  });

  describe('convenience functions', () => {
    describe('createSingleFilterJson', () => {
      it('creates JSON string for single date', () => {
        const json = createSingleFilterJson('2026-02-10');

        expect(json).toBe('{"type":"single","date":"2026-02-10"}');
      });

      it('works with Date object', () => {
        const json = createSingleFilterJson(new Date(2026, 1, 10));

        expect(JSON.parse(json)).toEqual({
          type: 'single',
          date: '2026-02-10',
        });
      });
    });

    describe('createRangeFilterJson', () => {
      it('creates JSON string for date range', () => {
        const json = createRangeFilterJson('2026-02-01', '2026-02-07');

        expect(json).toBe('{"type":"range","start":"2026-02-01","end":"2026-02-07"}');
      });

      it('sorts dates in output', () => {
        const json = createRangeFilterJson('2026-02-07', '2026-02-01');
        const parsed = JSON.parse(json);

        expect(parsed.start).toBe('2026-02-01');
        expect(parsed.end).toBe('2026-02-07');
      });
    });

    describe('createClearFilterJson', () => {
      it('creates JSON string for clear action', () => {
        const json = createClearFilterJson();

        expect(json).toBe('{"type":"clear"}');
      });
    });
  });

  describe('edge cases', () => {
    it('handles date boundaries correctly', () => {
      // First day of month
      expect(toIsoDateString(new Date(2026, 0, 1))).toBe('2026-01-01');

      // Last day of month
      expect(toIsoDateString(new Date(2026, 0, 31))).toBe('2026-01-31');
      expect(toIsoDateString(new Date(2026, 1, 28))).toBe('2026-02-28'); // Non-leap year
      expect(toIsoDateString(new Date(2024, 1, 29))).toBe('2024-02-29'); // Leap year
    });

    it('handles different years', () => {
      expect(toIsoDateString(new Date(2020, 5, 15))).toBe('2020-06-15');
      expect(toIsoDateString(new Date(2030, 5, 15))).toBe('2030-06-15');
    });

    it('parseIsoDate handles all months', () => {
      for (let month = 0; month < 12; month++) {
        const monthStr = String(month + 1).padStart(2, '0');
        const dateStr = `2026-${monthStr}-15`;
        const date = parseIsoDate(dateStr);
        expect(date.getMonth()).toBe(month);
      }
    });
  });
});
