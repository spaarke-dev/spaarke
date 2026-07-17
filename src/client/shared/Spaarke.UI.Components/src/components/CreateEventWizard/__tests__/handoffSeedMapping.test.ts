/**
 * handoffSeedMapping.test.ts — CreateEventWizard hand-off pre-seed mapper
 * (spaarkeai-assistant-enhancements-r1 task 013 part 2).
 */

import { mapEventHandoffSeed } from '../handoffSeedMapping';
import type { HandoffSeed } from '../../../services/surfaceHandoff';

function seed(partial: Partial<HandoffSeed>): HandoffSeed {
  return {
    draftValues: partial.draftValues ?? {},
    resolvedLookups: partial.resolvedLookups ?? {},
    fileIds: partial.fileIds ?? [],
  };
}

describe('mapEventHandoffSeed', () => {
  it('returns undefined for a null / empty seed', () => {
    expect(mapEventHandoffSeed(null)).toBeUndefined();
    expect(mapEventHandoffSeed(seed({}))).toBeUndefined();
  });

  it('maps drafted event name + description onto eventName / description', () => {
    expect(
      mapEventHandoffSeed(seed({ draftValues: { event_name: 'Kickoff', task_description: 'Prep docs.' } }))
    ).toEqual({ eventName: 'Kickoff', description: 'Prep docs.' });
    expect(mapEventHandoffSeed(seed({ draftValues: { eventName: 'A', description: 'B' } }))).toEqual({
      eventName: 'A',
      description: 'B',
    });
  });

  it('does NOT map the create-task registry preset event-type GUID (part-3 gap: no display name)', () => {
    // `sprk_eventtype_ref` in draftValues is the raw subtype GUID injected by the
    // registry preset — NOT a ResolvedLookup — so it is intentionally skipped.
    const result = mapEventHandoffSeed(
      seed({ draftValues: { event_name: 'Named', sprk_eventtype_ref: '124f5fc9-98ff-f011-8406-7c1e525abd8b' } })
    );
    expect(result).toEqual({ eventName: 'Named' });
  });

  it('pre-selects a HIGH-confidence resolved event-type', () => {
    const result = mapEventHandoffSeed(
      seed({
        draftValues: { event_name: 'Named' },
        resolvedLookups: {
          eventType: { confidence: 'high', recordId: 'et-1', candidates: [{ recordId: 'et-1', label: 'Deadline' }] },
        },
      })
    );
    expect(result).toMatchObject({ eventName: 'Named', eventTypeId: 'et-1', eventTypeName: 'Deadline' });
  });
});
