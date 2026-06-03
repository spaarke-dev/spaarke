/**
 * dateUtils Unit Tests — `localDateToUtcBounds` from {@link DateRangeFilterChip}.
 *
 * Covers task 007 acceptance criterion: "Given LOCAL date range
 * `2026-06-01..2026-06-30` in EDT, when `localDateToUtcBounds` runs, then
 * UTC bounds are correct (covering full LOCAL days)."
 *
 * The function is a verbatim port of the proven logic from
 * `src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx#L343`
 * (task 133 / R13 follow-up #12, 2026-05-23). The original GridSection helper
 * was for a single LOCAL day from a `YYYY-MM-DD` string; this version handles
 * two `Date` objects for a range, but the per-bound math is identical.
 *
 * **Test strategy**: the math is timezone-dependent (`new Date(y, m-1, d, …)`
 * uses the host timezone). To make the test deterministic regardless of
 * Jest worker timezone we mock `Date.prototype.getTimezoneOffset` to force
 * EDT (`-240` minutes = UTC-4). Without the mock, a test run in UTC or
 * PST would produce different UTC bounds and break.
 */

import { localDateToUtcBounds } from '../DateRangeFilterChip';

describe('localDateToUtcBounds', () => {
  // ─── Timezone harness ────────────────────────────────────────────────────
  // Force EDT (-04:00) regardless of the host timezone so the test's
  // expected UTC bounds are reproducible. We patch:
  //   - `Date.prototype.getTimezoneOffset` → +240 (EDT = UTC-4)
  //   - the LOCAL → UTC math performed by the `new Date(y, m, d, h, …)`
  //     constructor by intercepting it with a Proxy so the resulting
  //     instant's `.toISOString()` reflects EDT.
  //
  // Implementation: we override the global `Date` constructor for the
  // duration of this suite so `new Date(2026, 5, 1, 0, 0, 0, 0)` resolves
  // to "EDT midnight on 2026-06-01" → UTC 2026-06-01T04:00:00.000Z.
  let OriginalDate: typeof Date;
  const EDT_OFFSET_MINUTES = 240;

  beforeAll(() => {
    OriginalDate = global.Date;
    // Build a forced-EDT Date constructor. The constructor delegates to the
    // real Date, then adjusts the instant by the difference between the
    // host's actual offset and EDT so that getFullYear()/Month()/Date()/
    // toISOString() all behave as if the host were in EDT.
    class ForcedEdtDate extends OriginalDate {
      constructor(...args: unknown[]) {
        if (args.length === 0) {
          super();
          return;
        }
        // 7-arg form: (y, m, d, h?, min?, s?, ms?) — interpret as EDT-LOCAL.
        if (typeof args[0] === 'number' && args.length >= 2) {
          const [y, m, d = 1, h = 0, min = 0, s = 0, ms = 0] = args as number[];
          // Real instant for "EDT y/m/d h:min:s.ms" = UTC of same Y/M/D/H/min/s
          // SHIFTED by +EDT_OFFSET_MINUTES minutes.
          const utcMs = OriginalDate.UTC(y, m, d, h, min, s, ms) + EDT_OFFSET_MINUTES * 60_000;
          super(utcMs);
          return;
        }
        // 1-arg form (timestamp / string): delegate.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        super(args[0] as any);
      }

      // Override LOCAL accessors so they reflect EDT (real instant minus 4h).
      getFullYear(): number {
        return new OriginalDate(this.getTime() - EDT_OFFSET_MINUTES * 60_000).getUTCFullYear();
      }
      getMonth(): number {
        return new OriginalDate(this.getTime() - EDT_OFFSET_MINUTES * 60_000).getUTCMonth();
      }
      getDate(): number {
        return new OriginalDate(this.getTime() - EDT_OFFSET_MINUTES * 60_000).getUTCDate();
      }
      getHours(): number {
        return new OriginalDate(this.getTime() - EDT_OFFSET_MINUTES * 60_000).getUTCHours();
      }
      getMinutes(): number {
        return new OriginalDate(this.getTime() - EDT_OFFSET_MINUTES * 60_000).getUTCMinutes();
      }
      getSeconds(): number {
        return new OriginalDate(this.getTime() - EDT_OFFSET_MINUTES * 60_000).getUTCSeconds();
      }
      getMilliseconds(): number {
        return new OriginalDate(this.getTime() - EDT_OFFSET_MINUTES * 60_000).getUTCMilliseconds();
      }
      getTimezoneOffset(): number {
        return EDT_OFFSET_MINUTES;
      }
    }
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (global as any).Date = ForcedEdtDate;
  });

  afterAll(() => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (global as any).Date = OriginalDate;
  });

  // ─── Tests ────────────────────────────────────────────────────────────────

  it('localDateToUtcBounds_LocalJune2026FullMonth_EDT_ReturnsUtcBoundsCoveringFullLocalDays', () => {
    // June is in Daylight Saving for Eastern → EDT = UTC-4.
    // LOCAL 2026-06-01 00:00:00.000 → UTC 2026-06-01 04:00:00.000Z
    // LOCAL 2026-06-30 23:59:59.999 → UTC 2026-07-01 03:59:59.999Z
    const start = new Date(2026, 5, 1); // June (zero-indexed month)
    const end = new Date(2026, 5, 30);

    const { startUtc, endUtc } = localDateToUtcBounds(start, end);

    expect(startUtc.toISOString()).toBe('2026-06-01T04:00:00.000Z');
    expect(endUtc.toISOString()).toBe('2026-07-01T03:59:59.999Z');
  });

  it('localDateToUtcBounds_SingleDayRange_ReturnsFullLocalDayBounds', () => {
    // start === end (single LOCAL day) — should still cover the full LOCAL day.
    const day = new Date(2026, 2, 12); // 2026-03-12 (March)
    const { startUtc, endUtc } = localDateToUtcBounds(day, day);

    // March is also EDT-eligible (DST started 2026-03-08 in US Eastern), so
    // the offset is -04:00 by the harness's fixed-offset model. Result:
    //   LOCAL 2026-03-12 00:00 → UTC 2026-03-12 04:00
    //   LOCAL 2026-03-12 23:59:59.999 → UTC 2026-03-13 03:59:59.999
    expect(startUtc.toISOString()).toBe('2026-03-12T04:00:00.000Z');
    expect(endUtc.toISOString()).toBe('2026-03-13T03:59:59.999Z');
  });

  it('localDateToUtcBounds_IgnoresTimeComponentsOfInputDates_AlignsToLocalDayBoundaries', () => {
    // Even if the caller passes Dates with non-midnight time-of-day,
    // the helper must align both bounds to LOCAL day boundaries
    // (start = 00:00:00.000, end = 23:59:59.999).
    const start = new Date(2026, 5, 1, 17, 30, 45, 123); // 5:30:45.123 PM
    const end = new Date(2026, 5, 30, 8, 15, 0, 0); // 8:15:00 AM
    const { startUtc, endUtc } = localDateToUtcBounds(start, end);

    expect(startUtc.toISOString()).toBe('2026-06-01T04:00:00.000Z');
    expect(endUtc.toISOString()).toBe('2026-07-01T03:59:59.999Z');
  });

  it('localDateToUtcBounds_ReturnsDateObjectsNotStrings', () => {
    // Type-contract sanity check: callers downstream (e.g. a future
    // FetchXML builder) expect `Date` instances they can `.toISOString()` on.
    const start = new Date(2026, 0, 1);
    const end = new Date(2026, 0, 31);
    const result = localDateToUtcBounds(start, end);

    expect(result.startUtc).toBeInstanceOf(Date);
    expect(result.endUtc).toBeInstanceOf(Date);
  });
});
