/**
 * CalendarVisual — Date Only due dates bucket on their own calendar day
 * (spaarke-ontology-platform-r1 task 098, 2026-10-05).
 *
 * sprk_duedate / sprk_finalduedate are Dataverse Date Only: the Web API returns
 * "2026-10-02", which `new Date("2026-10-02")` reads as UTC midnight — Oct 1 in
 * New York, so the event dot landed on the previous day. TZ pinned to
 * America/New_York before any Date use (a UTC runner would pass trivially).
 */

const ORIGINAL_TZ = process.env.TZ;
process.env.TZ = 'America/New_York';

import { mapRecordToEvent } from '../CalendarVisual';

afterAll(() => {
  if (ORIGINAL_TZ === undefined) delete process.env.TZ;
  else process.env.TZ = ORIGINAL_TZ;
});

describe('CalendarVisual.mapRecordToEvent — Date Only dates (task 098)', () => {
  it('the harness is behind UTC (guard is meaningful)', () => {
    expect(new Date('2026-10-02').getDate()).toBe(1);
  });

  it('buckets a Date Only final due date on its own day', () => {
    const e = mapRecordToEvent({ sprk_eventid: 'e1', sprk_eventname: 'Filing', sprk_finalduedate: '2026-10-02' }, 'sprk_event', undefined);
    expect(e).not.toBeNull();
    expect([e!.date.getFullYear(), e!.date.getMonth(), e!.date.getDate()]).toEqual([2026, 9, 2]);
  });

  it('falls back to the Date Only due date, same rule', () => {
    const e = mapRecordToEvent({ sprk_eventid: 'e2', sprk_duedate: '2026-10-31' }, 'sprk_event', undefined);
    expect(e!.date.getDate()).toBe(31);
  });

  it('a configured datetime field (an instant) keeps its local day', () => {
    const e = mapRecordToEvent({ sprk_eventid: 'e3', createdon: '2026-10-02T03:00:00Z' }, 'sprk_event', 'createdon');
    expect(e!.date.getDate()).toBe(1); // 23:00 on Oct 1 in New York
  });
});
