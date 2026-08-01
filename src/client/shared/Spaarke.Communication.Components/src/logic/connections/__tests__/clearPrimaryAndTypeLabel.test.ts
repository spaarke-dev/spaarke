/**
 * clearPrimaryAndTypeLabel.test.ts — owner UAT 2026-07-31 items 1 + 2.
 *
 * Item 1: a CONFIRMED primary filed via DENORM fields only (all typed lookups null,
 *   so no `entity` logical name) carries the `sprk_regardingrecordtype` label
 *   ("Matter") as `typeLabel` — that is what the confirmed chip shows instead of the
 *   generic "Record".
 * Item 2: removing that confirmed (denorm-only) primary must actually clear it —
 *   `clearPrimaryRegarding` nulls the 5 denorm fields + resets the status, WITHOUT a
 *   typed-lookup fetch (there is no typed lookup to null). Plain `unlinkRegarding`
 *   only nulled a typed lookup, which silently no-oped for a denorm-only primary.
 */
import { clearPrimaryRegarding } from '../ConnectionsWriteHandler';
import { derivePrimaryReview, ASSOCIATION_STATUS_RESOLVED_VALUE } from '../provenance';

const GUID = '11111111-1111-1111-1111-111111111111';

function ctx(updateRecord: jest.Mock) {
  return { webApi: { updateRecord }, hostEntity: 'sprk_communication', hostRecordId: GUID };
}

describe('clearPrimaryRegarding (item 2 — delete a confirmed denorm-only primary)', () => {
  it('clears the denorm fields + status for a denorm-only primary (entity "") with no typed-lookup fetch', async () => {
    const updateRecord = jest.fn().mockResolvedValue({});
    const fetchSpy = jest.fn();
    const res = await clearPrimaryRegarding(ctx(updateRecord), '', fetchSpy as unknown as typeof fetch);

    expect(res.success).toBe(true);
    // Denorm-only primary → no typed lookup to null → no metadata fetch.
    expect(fetchSpy).not.toHaveBeenCalled();

    const [entity, id, payload] = updateRecord.mock.calls[0];
    expect(entity).toBe('sprk_communication');
    expect(id).toBe(GUID);
    expect(payload).toMatchObject({
      sprk_regardingrecordid: null,
      sprk_regardingrecordname: null,
      sprk_regardingrecordnumber: null,
      sprk_regardingrecordurl: null,
      'sprk_RegardingRecordType@odata.bind': null,
      sprk_associationstatus: null,
    });
  });

  it('is a no-op success in CREATE mode (no host guid) and never writes', async () => {
    const updateRecord = jest.fn();
    const res = await clearPrimaryRegarding(
      { webApi: { updateRecord }, hostEntity: 'sprk_communication', hostRecordId: '' },
      ''
    );
    expect(res.success).toBe(true);
    expect(updateRecord).not.toHaveBeenCalled();
  });
});

describe('derivePrimaryReview type label (item 1 — "Matter", not "Record")', () => {
  it('a confirmed denorm-only primary carries recordTypeLabel as typeLabel', () => {
    const model = derivePrimaryReview(null, ASSOCIATION_STATUS_RESOLVED_VALUE, [], {
      recordName: 'Patent Application 19183531 - Elisa Liardo',
      recordNumber: 'PAT-942665',
      recordTypeLabel: 'Matter',
    });
    expect(model.state).toBe('confirmed');
    expect(model.primary?.targetName).toBe('Patent Application 19183531 - Elisa Liardo');
    expect(model.primary?.entity).toBe(''); // denorm-only: no typed entity
    expect(model.primary?.typeLabel).toBe('Matter');
  });
});
