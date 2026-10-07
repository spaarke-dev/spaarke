/** @jest-environment ./jest.newYorkEnvironment.js */
/**
 * formatDateOnly / isDateOnlyString — timezone regression guard
 * (spaarke-ontology-platform-r1 task 098, 2026-10-05).
 *
 * A Dataverse Date Only column accepts only "YYYY-MM-DD" (a timestamp is HTTP
 * 400) and the value written must be the user's LOCAL calendar day. The former
 * writers sent `new Date().toISOString()` — a timestamp, and the UTC date: at
 * 23:30 on Oct 5 in New York that is "2026-10-06T03:30:00.000Z".
 *
 * TZ pinned to America/New_York before any Date use (see dateLocal.test.ts).
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
import { formatDateOnly, isDateOnlyString, parseDueDate } from '../dateLocal';

describe('formatDateOnly — the LOCAL calendar day as YYYY-MM-DD', () => {
  it('late evening in New York is still that day (toISOString would give tomorrow)', () => {
    const lateEvening = new Date(2026, 9, 5, 23, 30); // 23:30 EDT, Oct 5 = 03:30Z Oct 6
    expect(lateEvening.toISOString().slice(0, 10)).toBe('2026-10-06'); // the former value's date
    expect(formatDateOnly(lateEvening)).toBe('2026-10-05');
  });

  it('round-trips with parseDueDate', () => {
    expect(formatDateOnly(parseDueDate('2026-03-08')!)).toBe('2026-03-08'); // DST start day
    expect(formatDateOnly(parseDueDate('2026-11-01')!)).toBe('2026-11-01'); // DST end day
  });
});

describe('isDateOnlyString — the wire shape only a Date Only behaviour column has', () => {
  it.each(['2026-10-02', ' 2026-10-02 '])('accepts %p', v => expect(isDateOnlyString(v)).toBe(true));
  it.each(['2026-10-02T00:00:00Z', '10/02/2026', '', null, 20261002])('rejects %p', v =>
    expect(isDateOnlyString(v)).toBe(false)
  );
});
