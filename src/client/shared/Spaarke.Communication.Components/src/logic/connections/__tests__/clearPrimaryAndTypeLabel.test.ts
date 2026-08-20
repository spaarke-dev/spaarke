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

describe('candidate display name (item 3 — parse name from contributor provenance, not the GUID)', () => {
  // Real provenance shape (spaarkedev1 2026-08-19): the flat candidate carries only a GUID
  // targetId; the resolved name lives in the contributor's `name="…"` token.
  const prov = JSON.stringify({
    version: 1,
    direction: 'Incoming',
    decision: { status: '', autoFiled: false, killSwitchEnabled: false, autoFileThreshold: 0.85 },
    rungsFired: [],
    candidates: [
      {
        field: 'sprk_regardingmatter',
        targetEntity: 'sprk_matter',
        targetId: '86335ce3-4c18-f111-8343-7ced8d1dc988',
        reinforcedConfidence: 0.97,
        deterministicConfidence: 0.97,
        written: false,
        conflict: false,
        contributors: [
          {
            rung: 'RecordNameMatch',
            confidence: 0.97,
            provenance:
              'record-name-match:sprk_matter:where=subject:matched=number:name="Monte Rosa Biotechnology v Spaarke Inc":number="LITG-119896":reason="reference number in subject"',
          },
        ],
      },
      {
        field: 'sprk_regardingperson',
        targetEntity: 'contact',
        targetId: '8e9918a9-9021-f111-88b5-7c1e520aa4df',
        reinforcedConfidence: 0.7,
        deterministicConfidence: 0,
        written: false,
        conflict: false,
        contributors: [
          {
            rung: 'ContactNameMatch',
            confidence: 0.7,
            provenance: 'contact-name-match:where=body:matched=fullname:name="Ralph Schroeder":reason="name in body"',
          },
        ],
      },
    ],
    signals: [],
  });

  it('flattens the candidate name/number from the contributor provenance (never the raw GUID)', () => {
    const model = derivePrimaryReview(prov, 100000001 /* pending */, [], {});
    const matter = model.candidates.find(c => c.entity === 'sprk_matter');
    const contact = model.candidates.find(c => c.entity === 'contact');
    expect(matter?.targetName).toBe('Monte Rosa Biotechnology v Spaarke Inc');
    expect(matter?.recordNumber).toBe('LITG-119896');
    expect(contact?.targetName).toBe('Ralph Schroeder');
    // Never the GUID.
    expect(model.candidates.some(c => c.targetName.includes('-f111-'))).toBe(false);
  });
});
