/** @jest-environment ../../shared/Spaarke.UI.Components/jest.newYorkEnvironment.js */
/**
 * CalendarVisual — Date Only due dates bucket on their own calendar day
 * (spaarke-ontology-platform-r1 task 098, 2026-10-05).
 *
 * sprk_duedate / sprk_finalduedate are Dataverse Date Only: the Web API returns
 * "2026-10-02", which `new Date("2026-10-02")` reads as UTC midnight — Oct 1 in
 * New York, so the event dot landed on the previous day. The file runs in America/New_York through the
 * @jest-environment on line 1 (a UTC runner would otherwise pass trivially).
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
import { mapRecordToEvent } from '../CalendarVisual';

describe('CalendarVisual.mapRecordToEvent — Date Only dates (task 098)', () => {
  it('the harness is behind UTC (guard is meaningful)', () => {
    expect(new Date('2026-10-02').getDate()).toBe(1);
  });

  it('buckets a Date Only final due date on its own day', () => {
    const e = mapRecordToEvent(
      { sprk_eventid: 'e1', sprk_eventname: 'Filing', sprk_finalduedate: '2026-10-02' },
      'sprk_event',
      undefined
    );
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
