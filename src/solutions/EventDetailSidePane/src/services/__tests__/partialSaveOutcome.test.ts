/**
 * Decision round 51 item 2 (unified-access-control-r2 task 147, integration): when the event's FILING saves through the
 * BFF and the other fields then fail, the pane's rollback reverts ONLY the fields that failed — never the persisted
 * filing. Driven through the REAL `saveEvent` (BFF transport and Xrm.WebApi at their module boundaries) and the split the
 * pane applies to its result (`App.tsx` → `splitPartialSave` → `optimistic.handleSaveSuccess` / `handleSaveError`).
 */

import { saveEvent, type DirtyFields } from '../eventService';
import { refileEventThroughBff } from '../childRecordWrites';
import { splitPartialSave } from '../partialSaveOutcome';
import { readFileSync } from 'fs';
import { join } from 'path';

jest.mock('../childRecordWrites', () => ({
  refileEventThroughBff: jest.fn(),
}));

const EVENT_ID = '0a1b2c3d-0000-4000-8000-000000000051';
const MATTER_ID = '0a1b2c3d-0000-4000-8000-0000000000aa';

const refile = refileEventThroughBff as jest.MockedFunction<typeof refileEventThroughBff>;
const updateRecord = jest.fn();

/** The pane's payload rule for these fields (App.tsx `buildSavePayload`): a lookup is sent as its navigation bind. */
const NAV: Record<string, string> = { sprk_regardingmatter: 'sprk_RegardingMatter' };
function buildPayload(fields: DirtyFields): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [field, value] of Object.entries(fields)) {
    if (value && typeof value === 'object' && 'id' in (value as Record<string, unknown>)) {
      const lv = value as { id: string; entityType: string };
      out[`${NAV[field] ?? field}@odata.bind`] = `/${lv.entityType}s(${lv.id})`;
    } else {
      out[field] = value;
    }
  }
  return out;
}

/** The rollback the pane offers: the original value of each field handed to `handleSaveError` (useOptimisticUpdate). */
function rollbackFieldsFor(failed: DirtyFields): string[] {
  return Object.keys(failed);
}

beforeEach(() => {
  refile.mockReset();
  updateRecord.mockReset();
  refile.mockResolvedValue(undefined);
  (globalThis as unknown as { window: unknown }).window = { parent: { Xrm: { WebApi: { updateRecord } } } };
});

afterAll(() => {
  delete (globalThis as unknown as { window?: unknown }).window;
});

describe('round 51 item 2 — a partial save never rolls back the persisted filing', () => {
  const fields: DirtyFields = {
    sprk_regardingmatter: { id: MATTER_ID, entityType: 'sprk_matter', name: 'Smith v. Jones' } as unknown as DirtyFields[string],
    sprk_regardingrecordname: 'Smith v. Jones',
    sprk_eventname: 'Hearing',
    sprk_description: 'prep',
  };

  it('the filing saved and the rest failed: the filing is persisted (never rolled back or retried), only the rest fails', async () => {
    updateRecord.mockRejectedValue(new Error('The user does not hold Write on sprk_event.'));

    const result = await saveEvent(EVENT_ID, buildPayload(fields));
    const { persisted, failed } = splitPartialSave(fields, result.savedFields, buildPayload);

    expect(result.success).toBe(false);
    expect(refile).toHaveBeenCalledTimes(1);
    expect(Object.keys(persisted).sort()).toEqual(['sprk_regardingmatter', 'sprk_regardingrecordname']);
    expect(Object.keys(failed).sort()).toEqual(['sprk_description', 'sprk_eventname']);
    expect(rollbackFieldsFor(failed)).not.toContain('sprk_regardingmatter');
    expect(rollbackFieldsFor(failed)).not.toContain('sprk_regardingrecordname');
  });

  it('a refused re-file saved nothing: every field stays rollback-able', async () => {
    refile.mockRejectedValue(new Error('Only someone with Full Access to the matter can move this event out of it.'));

    const result = await saveEvent(EVENT_ID, buildPayload(fields));
    const { persisted, failed } = splitPartialSave(fields, result.savedFields, buildPayload);

    expect(updateRecord).not.toHaveBeenCalled();
    expect(persisted).toEqual({});
    expect(Object.keys(failed).sort()).toEqual(Object.keys(fields).sort());
  });

  it('fails closed: a field only some of whose payload keys were saved, or with no saved keys at all, counts as failed', () => {
    const twoKeys = (f: DirtyFields) => (Object.keys(f).length ? { a: 1, b: 2 } : {});
    expect(splitPartialSave({ x: 1 }, ['a'], twoKeys).failed).toEqual({ x: 1 });
    expect(splitPartialSave({ x: 1 }, undefined, twoKeys).failed).toEqual({ x: 1 });
    expect(splitPartialSave({ x: 1 }, ['a', 'b'], twoKeys).persisted).toEqual({ x: 1 });
  });
});

describe('round 51 item 2 — App.tsx applies the split on a failed save', () => {
  // The node harness does not render the pane, so its failure branch is pinned at the source: it splits the result and
  // hands ONLY the failed fields to the rollback (never every dirty field), after marking the persisted filing saved.
  const app = readFileSync(join(__dirname, '..', '..', 'App.tsx'), 'utf8').split('\r\n').join('\n');
  const failureBranch = app.slice(app.indexOf('} else {', app.indexOf('if (result.success) {')), app.indexOf('} catch (error) {', app.indexOf('const result = await saveEvent(params.eventId, savePayload);')));

  it('splits the save result and rolls back only the failed fields', () => {
    expect(failureBranch).toMatch(/splitPartialSave\(fields, result\.savedFields, buildSavePayload\)/);
    expect(failureBranch).toMatch(/optimistic\.handleSaveSuccess\(persisted,/);
    expect(failureBranch).toMatch(/optimistic\.handleSaveError\(\s*errorMsg,\s*failed,/);
    expect(failureBranch).not.toMatch(/optimistic\.handleSaveError\(\s*errorMsg,\s*fields,/);
  });
});
