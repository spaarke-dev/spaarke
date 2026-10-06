/**
 * saveEvent — the ORDER of the side pane's two writes (unified-access-control-r2 task 147 r1c-v1, verifier item 6).
 *
 * Owner round 36: an event's FILING (its `sprk_regarding…` lookups and regarding fields) is re-filed through the BFF's
 * one event filing route; every other field stays the caller's own Xrm.WebApi update. The filing goes FIRST — the seam's
 * order (`updateFilingThenRest` in `@spaarke/ui-components`) — so a refused or failed re-file writes nothing at all and
 * the pane never leaves the other fields saved around a filing the server refused.
 */

import { saveEvent } from '../eventService';
import { refileEventThroughBff } from '../childRecordWrites';

jest.mock('../childRecordWrites', () => ({
  refileEventThroughBff: jest.fn(),
}));

const EVENT_ID = '{0a1b2c3d-0000-4000-8000-000000000001}';
const ID = '0a1b2c3d-0000-4000-8000-000000000001';
const MATTER = '/sprk_matters(0a1b2c3d-0000-4000-8000-0000000000aa)';

const refile = refileEventThroughBff as jest.MockedFunction<typeof refileEventThroughBff>;
const calls: string[] = [];
const updateRecord = jest.fn();

beforeEach(() => {
  calls.length = 0;
  refile.mockReset();
  updateRecord.mockReset();
  refile.mockImplementation(async () => {
    calls.push('filing');
  });
  updateRecord.mockImplementation(async (entity: string, id: string) => {
    calls.push('rest');
    return { entityType: entity, id };
  });
  (globalThis as unknown as { window: unknown }).window = { parent: { Xrm: { WebApi: { updateRecord } } } };
});

afterAll(() => {
  delete (globalThis as unknown as { window?: unknown }).window;
});

describe('saveEvent', () => {
  it('writes the filing through the BFF FIRST, then every other field as the caller', async () => {
    const result = await saveEvent(EVENT_ID, {
      sprk_eventname: 'Hearing',
      'sprk_RegardingMatter@odata.bind': MATTER,
      'sprk_RegardingProject@odata.bind': null,
      sprk_regardingrecordname: 'Smith v. Jones',
      statuscode: 2,
    });

    expect(calls).toEqual(['filing', 'rest']);
    expect(refile).toHaveBeenCalledWith(ID, {
      'sprk_RegardingMatter@odata.bind': MATTER,
      'sprk_RegardingProject@odata.bind': null,
      sprk_regardingrecordname: 'Smith v. Jones',
    });
    expect(updateRecord).toHaveBeenCalledWith('sprk_event', ID, { sprk_eventname: 'Hearing', statuscode: 2 });
    expect(result.success).toBe(true);
    expect(result.savedFields).toEqual([
      'sprk_RegardingMatter@odata.bind',
      'sprk_RegardingProject@odata.bind',
      'sprk_regardingrecordname',
      'sprk_eventname',
      'statuscode',
    ]);
  });

  it('writes NOTHING when the re-file is refused, and shows the server message', async () => {
    refile.mockRejectedValue(new Error('Only someone with Full Access to the matter can move this event out of it.'));

    const result = await saveEvent(EVENT_ID, {
      sprk_eventname: 'Hearing',
      'sprk_RegardingMatter@odata.bind': null,
    });

    expect(updateRecord).not.toHaveBeenCalled();
    expect(result).toEqual({
      success: false,
      error: 'Only someone with Full Access to the matter can move this event out of it.',
      savedFields: [],
    });
  });

  it('names the saved filing when the other fields then fail', async () => {
    updateRecord.mockRejectedValue(new Error('Validation failed.'));

    const result = await saveEvent(EVENT_ID, {
      sprk_eventname: 'Hearing',
      'sprk_RegardingMatter@odata.bind': MATTER,
    });

    expect(calls).toEqual(['filing']);
    expect(result).toEqual({
      success: false,
      error: 'What the event is filed under was saved, but the other changes were not: Validation failed.',
      savedFields: ['sprk_RegardingMatter@odata.bind'],
    });
  });

  it('makes no BFF call when nothing the event is filed under changed', async () => {
    const result = await saveEvent(EVENT_ID, { sprk_eventname: 'Hearing', sprk_duedate: undefined });

    expect(refile).not.toHaveBeenCalled();
    expect(updateRecord).toHaveBeenCalledWith('sprk_event', ID, { sprk_eventname: 'Hearing', sprk_duedate: null });
    expect(result.success).toBe(true);
  });

  it('makes no caller update when only the filing changed', async () => {
    const result = await saveEvent(EVENT_ID, { 'sprk_RegardingProject@odata.bind': undefined });

    expect(refile).toHaveBeenCalledWith(ID, { 'sprk_RegardingProject@odata.bind': null });
    expect(updateRecord).not.toHaveBeenCalled();
    expect(result).toEqual({ success: true, savedFields: ['sprk_RegardingProject@odata.bind'] });
  });

  it('a plain failure of the other fields (no filing) is reported as it is', async () => {
    updateRecord.mockRejectedValue(new Error('Record is locked.'));

    const result = await saveEvent(EVENT_ID, { statuscode: 2 });

    expect(result).toEqual({ success: false, error: 'Record is locked.', savedFields: [] });
  });
});
