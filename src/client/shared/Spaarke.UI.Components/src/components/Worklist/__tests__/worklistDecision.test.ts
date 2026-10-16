/**
 * The Decision Record on a closed worklist item (task 045, #1959 item 2). The stated contract:
 *  - the route's `decision` object is parsed; a malformed one is no record (null), never a made-up one
 *  - `resolutionFor` hands the wizard the record number, who decided, the note and the steps, so the closed item REPLAYS
 *  - an item with no readable record (the route omits it) opens read-only with no steps, so the wizard does not replay "Skipped"
 *  - a record whose stored steps were unreadable (steps null) is not replayed either
 */

import { hasRecordedDecision } from '../../DecisionWizard/DecisionItemWizard';
import { parseWorklistResponse } from '../worklistService';
import type { WorklistRouteItem } from '../worklistService';
import { buildWizardItem, resolutionFor } from '../worklistWizardItem';

const TODAY = new Date(2026, 9, 14, 12, 0);

function itemFrom(extra: Record<string, unknown>): WorklistRouteItem {
  const data = parseWorklistResponse(
    {
      lanes: [
        {
          lane: 'Decide',
          groups: [
            {
              core: null,
              notFiled: true,
              items: [
                {
                  signalId: 's1',
                  lane: 'Decide',
                  resolvedOn: '2026-10-12T09:00:00Z',
                  resolutionType: 100000000,
                  ...extra,
                },
              ],
            },
          ],
        },
        { lane: 'Do', groups: [] },
      ],
    },
    'closedEarlier'
  );
  return data.Decide[0].items[0];
}

const DECISION = {
  recordId: 'r1',
  number: 'DR-00042',
  recordClass: 'Judgement',
  outcome: 'Authorized',
  decidedOn: '2026-10-12T09:30:00Z',
  decidedByName: 'Dana Decider',
  reason: 'Agreed with the firm',
  steps: [
    { code: 'reassign', taken: true, values: { person: 'p-1' }, status: 'Done' },
    { code: 'extend', taken: false, values: null, status: 'Skipped' },
    { taken: true },
    'junk',
  ],
  followOns: [{ code: 'notify', entity: 'sprk_communication', id: 'c1' }, { entity: 'x' }],
};

describe('parsing the route decision', () => {
  it('reads the record, skipping step and follow-on entries that are not objects with a code', () => {
    const d = itemFrom({ decision: DECISION }).decision!;
    expect(d.number).toBe('DR-00042');
    expect(d.recordClass).toBe('Judgement');
    expect(d.outcome).toBe('Authorized');
    expect(d.decidedByName).toBe('Dana Decider');
    expect(d.steps?.map(s => [s.code, s.taken, s.status])).toEqual([
      ['reassign', true, 'Done'],
      ['extend', false, 'Skipped'],
    ]);
    expect(d.steps?.[0].values).toEqual({ person: 'p-1' });
    expect(d.steps?.[1].values).toBeNull();
    expect(d.followOns).toEqual([{ code: 'notify', entity: 'sprk_communication', id: 'c1' }]);
  });

  it('an unknown class or outcome is null, and a missing or malformed decision is null', () => {
    const odd = itemFrom({ decision: { ...DECISION, recordClass: 'Mystery', outcome: 'Maybe' } }).decision!;
    expect(odd.recordClass).toBeNull();
    expect(odd.outcome).toBeNull();
    expect(itemFrom({}).decision).toBeNull();
    expect(itemFrom({ decision: 'x' }).decision).toBeNull();
    expect(itemFrom({ decision: { number: 'DR-1' } }).decision).toBeNull();
  });

  it('steps that are not an array are null (no replay), an empty array stays an empty list', () => {
    expect(itemFrom({ decision: { ...DECISION, steps: null } }).decision?.steps).toBeNull();
    expect(itemFrom({ decision: { ...DECISION, steps: 'x' } }).decision?.steps).toBeNull();
    expect(itemFrom({ decision: { ...DECISION, steps: [] } }).decision?.steps).toEqual([]);
  });
});

describe('resolutionFor with a Decision Record', () => {
  it('carries the record number, who decided, the note and the steps, and the wizard treats it as a recorded decision', () => {
    const resolution = resolutionFor(itemFrom({ decision: DECISION }), 'closedEarlier', TODAY)!;
    expect(resolution.recordNumber).toBe('DR-00042');
    expect(resolution.decidedByName).toBe('Dana Decider');
    expect(resolution.decidedOn).toBe('2026-10-12T09:30:00Z');
    expect(resolution.reason).toBe('Agreed with the firm');
    expect(resolution.recordClass).toBe('Judgement');
    expect(resolution.steps?.map(s => s.code)).toEqual(['reassign', 'extend']);
    expect(resolution.followOns).toEqual([{ code: 'notify', entity: 'sprk_communication', id: 'c1' }]);
    expect(hasRecordedDecision(resolution)).toBe(true);
  });

  it('is the same for Closed today and the Decided set', () => {
    for (const set of ['closedToday', 'decided'] as const) {
      expect(hasRecordedDecision(resolutionFor(itemFrom({ decision: DECISION }), set, TODAY))).toBe(true);
    }
  });

  it('NEGATIVE: an item with no readable record has no steps, so nothing is replayed as Skipped', () => {
    const resolution = resolutionFor(itemFrom({}), 'closedEarlier', TODAY)!;
    expect(resolution.steps).toBeUndefined();
    expect(resolution.recordNumber).toBeUndefined();
    expect(hasRecordedDecision(resolution)).toBe(false);
  });

  it('NEGATIVE: a record whose stored steps were unreadable is not replayed', () => {
    const resolution = resolutionFor(itemFrom({ decision: { ...DECISION, steps: null } }), 'closedEarlier', TODAY)!;
    expect(resolution.recordNumber).toBe('DR-00042');
    expect(resolution.steps).toBeUndefined();
    expect(hasRecordedDecision(resolution)).toBe(false);
  });

  it('an inquiry-sent record (Signal still Acknowledged, no resolution type) is shown as recorded and still open', () => {
    const resolution = resolutionFor(
      itemFrom({ resolutionType: null, resolvedOn: null, decision: DECISION }),
      'decided',
      TODAY
    )!;
    expect(resolution.leftOpen).toBe(true);
    expect(resolution.resolutionType).toBe('Acted');
    expect(resolution.recordNumber).toBe('DR-00042');
    expect(hasRecordedDecision(resolution)).toBe(true);
    const dismissal = resolutionFor(
      itemFrom({ resolutionType: null, resolvedOn: null, decision: { ...DECISION, recordClass: 'Dismissal' } }),
      'decided',
      TODAY
    )!;
    expect(dismissal.resolutionType).toBe('Dismissed');
  });

  it('an open item never has a resolution, record or not', () => {
    expect(resolutionFor(itemFrom({ decision: DECISION }), 'lanes', TODAY)).toBeNull();
  });

  it('buildWizardItem puts the resolution on the wizard item', () => {
    const item = itemFrom({ decision: DECISION });
    const wizardItem = buildWizardItem({
      item,
      group: { core: null, items: [item] },
      set: 'closedEarlier',
      today: TODAY,
    });
    expect(wizardItem.resolution?.recordNumber).toBe('DR-00042');
    expect(wizardItem.otherOpenItems).toEqual([]);
  });
});
